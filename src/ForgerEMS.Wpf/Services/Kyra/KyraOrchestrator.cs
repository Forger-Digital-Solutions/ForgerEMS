using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.KyraTools;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Orchestrates Kyra reply generation (providers are engines; local facts are authoritative).</summary>
public sealed class KyraOrchestrator
{
    private readonly IKyraOrchestrationHost _host;
    private readonly ICopilotProviderRegistry _providerRegistry;
    private readonly ICopilotContextBuilder _contextBuilder;
    private readonly KyraToolRegistry _toolRegistry;

    public KyraOrchestrator(
        IKyraOrchestrationHost host,
        ICopilotProviderRegistry providerRegistry,
        ICopilotContextBuilder contextBuilder,
        KyraToolRegistry toolRegistry)
    {
        _host = host;
        _providerRegistry = providerRegistry;
        _contextBuilder = contextBuilder;
        _toolRegistry = toolRegistry;
    }

    public static (KyraToolCallPlan ToolPlan, IReadOnlyList<ICopilotProvider> Providers) BuildExecutionPlan(
        CopilotRequest request,
        CopilotSettings settings,
        CopilotContext context,
        IReadOnlyList<ICopilotProvider> providers,
        Func<ICopilotProvider, CopilotProviderConfiguration> configResolver,
        KyraConversationState memoryState,
        KyraToolRegistry toolRegistry,
        KyraToolHostFacts hostFacts)
    {
        var toolPlan = KyraMessagePlanner.BuildPlan(request, context, settings, memoryState, toolRegistry, hostFacts);
        if (toolPlan.ShouldUseLocalToolAnswer)
        {
            return (toolPlan, Array.Empty<ICopilotProvider>());
        }

        var scored = KyraProviderRouter.ScoreProviders(providers, request, settings, context, configResolver);
        return (toolPlan, scored.Select(item => item.Provider).ToArray());
    }

    public async Task<CopilotResponse> GenerateReplyAsync(CopilotRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = request.Settings ?? new CopilotSettings();
            string? lastReported = null;
            void Report(string message)
            {
                if (string.Equals(lastReported, message, StringComparison.Ordinal))
                {
                    return;
                }

                lastReported = message;
                try
                {
                    request.KyraActivityStatusCallback?.Invoke(message);
                }
                catch
                {
                }
            }

            Report("Checking system context…");
            var built = _contextBuilder.Build(request);
            Report("Sanitizing context…");
            Report("Checking configured tool…");
            var hostFacts = KyraToolRegistry.BuildHostFacts(request);
            var toolAugmentation = await _toolRegistry.BuildAugmentationAsync(
                new KyraToolExecutionRequest
                {
                    Intent = built.Intent,
                    Prompt = request.Prompt,
                    Context = built,
                    Settings = settings,
                    HostFacts = hostFacts
                },
                cancellationToken).ConfigureAwait(false);
            var context = _host.AttachToolAugmentation(built, toolAugmentation, settings);
            context = _host.AttachConversationMemory(context);
            var memoryState = _host.Memory.GetState();
            _host.SetLastSystemContext(context.SystemContext);
            var notes = new List<string> { $"Intent detected: {context.Intent}", $"Previous intent: {memoryState.LastIntent}" };
            var localProvider = _providerRegistry.FindByType(CopilotProviderType.LocalOffline) ?? new LocalOfflineCopilotProvider();
            var decision = KyraProviderDecision.Build(
                request,
                settings,
                context,
                _providerRegistry.Providers,
                provider => _host.ResolveProviderConfig(settings, provider),
                memoryState,
                _toolRegistry,
                hostFacts);
            var plan = decision.ToolPlan;
            notes.Add($"Tool plan: {plan.ToolName}");
            if (request.VerboseDiagnosticNotes)
            {
                if (plan.StayLocalReason == KyraStayLocalReason.MachineContextPrivacy &&
                    settings.Mode is not CopilotMode.OfflineOnly and not CopilotMode.AskFirst)
                {
                    notes.Add("Kyra routing: machine-specific with online context sharing OFF -> Local Kyra (System Intelligence)");
                }
                else if (plan.StayLocalReason == KyraStayLocalReason.DeviceToolkitRouting &&
                         plan.ShouldUseLocalToolAnswer &&
                         settings.Mode is not CopilotMode.OfflineOnly and not CopilotMode.AskFirst)
                {
                    notes.Add("Kyra routing: local tool intent -> Local Kyra");
                }
                else if (plan.StayLocalReason == KyraStayLocalReason.LiveDataNotConfigured &&
                         plan.ShouldUseLocalToolAnswer &&
                         settings.Mode is not CopilotMode.OfflineOnly and not CopilotMode.AskFirst)
                {
                    notes.Add("Kyra routing: live data tool not configured -> Local Kyra (honest offline guidance)");
                }
            }

            var ledger = KyraFactsLedger.FromCopilotContext(context);
            var ctxPackage = KyraContextBuilder.BuildPackage(context, ledger, _host.Memory, plan, settings);
            KyraOrchestrationLog.Append(
                $"Kyra ctx intent={ctxPackage.Intent} localTruth={ctxPackage.LocalTruthAvailable} requiresLocal={ctxPackage.RequiresLocalTruth} caps={decision.EffectiveCapabilities} liveUnavailable={(plan.StayLocalReason == KyraStayLocalReason.LiveDataNotConfigured ? 1 : 0)}");

            if (KyraLocalSpecAnswerBuilder.TryBuildLocalSpecAnswer(request.Prompt, context.SystemProfile, out var localSpecResponse))
            {
                Report("Formatting Kyra response…");
                if (KyraResponseCache.IsCacheablePrompt(request.Prompt))
                {
                    _host.StoreResponseCache(BuildCacheKey(request.Prompt), localSpecResponse.Text);
                }

                var localSpecDone = _host.CompleteResponse(request, context, localSpecResponse);
                Report("Done.");
                return localSpecDone;
            }

            if (_host.TryGetResponseCache(BuildCacheKey(request.Prompt), out var cached))
            {
                Report("Formatting Kyra response…");
                var hit = _host.CompleteResponse(request, context, new CopilotResponse
                {
                    Text = cached,
                    UsedOnlineData = false,
                    ProviderType = CopilotProviderType.LocalOffline,
                    OnlineStatus = "Kyra Mode: Local cache hit - no provider call needed.",
                    ProviderNotes = notes,
                    ResponseSource = KyraResponseSource.LocalKyra,
                    SourceLabel = KyraResponseComposer.KyraIdentityLabel
                });
                Report("Done.");
                return hit;
            }

            if (settings.Mode is CopilotMode.OfflineOnly or CopilotMode.AskFirst || plan.ShouldUseLocalToolAnswer)
            {
                Report("Using local fallback…");
                var localResultEarly = await RunInstrumentedAsync(localProvider, request, settings, context, notes, cancellationToken).ConfigureAwait(false);
                var offlineStatus = settings.Mode == CopilotMode.AskFirst
                    ? "Kyra Mode: Hybrid (Ask First) - currently local/offline until you explicitly enable an online lookup."
                    : "Kyra Mode: Offline Local - no data leaves this machine.";
                var localResponse = _host.ApplyLocalKyraSourceLabel(
                    _host.BuildResponse(localResultEarly, localProvider, notes, offlineStatus),
                    plan,
                    context,
                    request.Prompt,
                    settings);

                Report("Formatting Kyra response…");
                var earlyDone = _host.CompleteResponse(request, context, localResponse);
                Report("Done.");
                return earlyDone;
            }

            var candidates = decision.OrderedProviders;
            if (candidates.Count == 0)
            {
                notes.Add("Kyra routing: no API providers selected; answering with Local Kyra.");
                Report("Using local fallback…");
                var localResultEmpty = await RunInstrumentedAsync(localProvider, request, settings, context, notes, cancellationToken).ConfigureAwait(false);
                var status = settings.Mode is CopilotMode.OnlineAssisted or CopilotMode.OnlineWhenAvailable
                    ? "No online provider is configured yet. Local Kyra is still available."
                    : "Kyra Mode: Free API Pool unavailable. Fallback: Local Kyra active.";
                Report("Formatting Kyra response…");
                var emptyProviders = _host.CompleteResponse(
                    request,
                    context,
                    _host.ApplyLocalKyraSourceLabel(_host.BuildResponse(localResultEmpty, localProvider, notes, status), plan, context, request.Prompt, settings));
                Report("Done.");
                return emptyProviders;
            }

            var apiFirst = decision.ApiFirst;

            KyraOrchestrationLog.Append(
                $"Kyra orchestration intent={context.Intent} apiFirst={apiFirst} mode={settings.Mode} stayLocal={plan.ShouldUseLocalToolAnswer}");

            if (apiFirst)
            {
                var freePoolAttemptFailed = false;
                for (var i = 0; i < candidates.Count; i++)
                {
                    var provider = candidates[i];
                    Report(provider.IsOnlineProvider ? "Asking API provider…" : "Thinking locally…");
                    var result = await RunInstrumentedAsync(provider, request, settings, context, notes, cancellationToken).ConfigureAwait(false);
                    if (result.Succeeded)
                    {
                        var ledgerGuard = KyraFactsLedger.FromCopilotContext(context);
                        var onlineText = result.UserMessage ?? string.Empty;
                        if (provider.IsOnlineProvider &&
                            result.UsedOnlineData &&
                            (KyraSafetyPolicy.ShouldDiscardOnlineAnswer(onlineText, localReferenceText: null, ledgerGuard) ||
                             KyraSafetyPolicy.ContradictsLocalHardwareLedger(onlineText, ledgerGuard)))
                        {
                            notes.Add("Kyra routing: API-first answer discarded — aligned to local ForgerEMS facts.");
                            KyraOrchestrationLog.Append(
                                "Kyra api_first_discard=1 reason=truth_guard discarded=1 discardReason=truth_guard fallback_used=1");
                            var localTruth = await RunInstrumentedAsync(localProvider, request, settings, context, notes, cancellationToken).ConfigureAwait(false);
                            if (localTruth.Succeeded)
                            {
                                result = localTruth;
                                provider = localProvider;
                            }
                        }
                        else if (provider.IsOnlineProvider && result.UsedOnlineData && ledgerGuard.HasTrustedLocalHardwareFacts)
                        {
                            onlineText = KyraResponseComposer.SanitizeProviderSelfIdentification(onlineText, ledgerGuard);
                            if (!string.Equals(onlineText, result.UserMessage, StringComparison.Ordinal))
                            {
                                result = new CopilotProviderResult
                                {
                                    Succeeded = true,
                                    UsedOnlineData = result.UsedOnlineData,
                                    UserMessage = onlineText
                                };
                            }
                        }

                        if (KyraResponseCache.IsCacheablePrompt(request.Prompt))
                        {
                            _host.StoreResponseCache(BuildCacheKey(request.Prompt), result.UserMessage ?? string.Empty);
                        }

                        var status = result.UsedOnlineData
                            ? "Kyra Mode: online assist used (sanitized context when enabled)."
                            : "Kyra Mode: hybrid — local rules engine with optional online assist.";
                        if (freePoolAttemptFailed &&
                            provider.ProviderType is CopilotProviderType.OllamaLocal or CopilotProviderType.LmStudioLocal)
                        {
                            notes.Add("Kyra routing: API exhausted -> Local AI");
                        }

                        notes.Add($"Kyra routing: normal chat -> {provider.DisplayName}");
                        var enhancementApplied = provider.IsOnlineProvider && result.UsedOnlineData;
                        var onlineResponse = _host.BuildResponse(result, provider, notes, status, enhancementApplied);

                        Report("Formatting Kyra response…");
                        var apiOk = _host.CompleteResponse(request, context, onlineResponse);
                        Report("Done.");
                        return apiOk;
                    }

                    if (provider.IsOnlineProvider)
                    {
                        freePoolAttemptFailed = true;
                    }

                    if (i < candidates.Count - 1)
                    {
                        notes.Add($"Kyra routing: provider failed ({provider.DisplayName}) -> trying next provider");
                    }
                }

                if (settings.OfflineFallbackEnabled)
                {
                    notes.Add("Kyra routing: all AI unavailable -> Local Kyra");
                    Report("Using local fallback…");
                    var localFallback = await RunInstrumentedAsync(localProvider, request, settings, context, notes, cancellationToken).ConfigureAwait(false);
                    var fb = localFallback.UserMessage?.Trim() ?? string.Empty;
                    if (!fb.StartsWith("I couldn’t reach", StringComparison.OrdinalIgnoreCase) &&
                        !fb.StartsWith("I couldn't reach", StringComparison.OrdinalIgnoreCase))
                    {
                        fb = "I couldn’t reach the online assistants right now, so I’m answering offline with Local Kyra.\n\n" + fb;
                    }

                    var wrappedLocal = new CopilotProviderResult
                    {
                        Succeeded = true,
                        UsedOnlineData = false,
                        UserMessage = fb
                    };
                    Report("Formatting Kyra response…");
                    var fbResp = _host.CompleteResponse(
                        request,
                        context,
                        _host.ApplyLocalKyraSourceLabel(
                            _host.BuildResponse(wrappedLocal, localProvider, notes, "Local Kyra fallback — online providers were unavailable."),
                            plan,
                            context,
                            request.Prompt,
                            settings));
                    Report("Done.");
                    return fbResp;
                }

                Report("Formatting Kyra response…");
                var noFb = _host.CompleteResponse(request, context, new CopilotResponse
                {
                    Text = "Kyra could not get a provider response and offline fallback is disabled. Re-enable offline fallback or check provider settings.",
                    OnlineStatus = "Error state - no fallback available.",
                    ProviderNotes = notes
                });
                Report("Done.");
                return noFb;
            }

            Report("Thinking locally…");
            var localResult = await RunInstrumentedAsync(localProvider, request, settings, context, notes, cancellationToken).ConfigureAwait(false);
            var freePoolAttemptFailedLegacy = false;
            for (var i = 0; i < candidates.Count; i++)
            {
                var provider = candidates[i];
                Report(provider.IsOnlineProvider ? "Asking API provider…" : "Thinking locally…");
                var result = await RunInstrumentedAsync(provider, request, settings, context, notes, cancellationToken).ConfigureAwait(false);
                if (result.Succeeded)
                {
                    var effectiveResult = result;
                    var responseProvider = provider;
                    var discardedOnlineForTruth = false;
                    if (provider.IsOnlineProvider && localResult.Succeeded && !plan.ShouldPolishWithProvider)
                    {
                        var ledgerLocal = KyraFactsLedger.FromCopilotContext(context);
                        if (KyraSafetyPolicy.ShouldDiscardOnlineAnswer(
                                effectiveResult.UserMessage ?? string.Empty,
                                localResult.UserMessage ?? string.Empty,
                                ledgerLocal) ||
                            KyraSafetyPolicy.ContradictsLocalHardwareLedger(effectiveResult.UserMessage ?? string.Empty, ledgerLocal))
                        {
                            notes.Add("Kyra routing: online answer discarded — aligned to local ForgerEMS facts.");
                            KyraOrchestrationLog.Append(
                                "Kyra discard_online=1 reason=truth_guard discarded=1 discardReason=truth_guard");
                            effectiveResult = new CopilotProviderResult
                            {
                                Succeeded = true,
                                UsedOnlineData = false,
                                UserMessage = localResult.UserMessage ?? string.Empty
                            };
                            responseProvider = localProvider;
                            discardedOnlineForTruth = true;
                        }
                    }

                    if (plan.ShouldPolishWithProvider && !discardedOnlineForTruth && provider.IsOnlineProvider)
                    {
                        effectiveResult = new CopilotProviderResult
                        {
                            Succeeded = true,
                            UsedOnlineData = effectiveResult.UsedOnlineData,
                            UserMessage =
                                $"Quick draft (local):{Environment.NewLine}{localResult.UserMessage}{Environment.NewLine}{Environment.NewLine}Polished version (online assist):{Environment.NewLine}{result.UserMessage}"
                        };
                    }

                    if (KyraResponseCache.IsCacheablePrompt(request.Prompt))
                    {
                        _host.StoreResponseCache(BuildCacheKey(request.Prompt), effectiveResult.UserMessage ?? string.Empty);
                    }

                    var status = effectiveResult.UsedOnlineData
                        ? "Kyra Mode: online assist used (sanitized context when enabled)."
                        : "Kyra Mode: hybrid — local rules engine with optional online assist.";
                    if (freePoolAttemptFailedLegacy &&
                        provider.ProviderType is CopilotProviderType.OllamaLocal or CopilotProviderType.LmStudioLocal)
                    {
                        notes.Add("Kyra routing: API exhausted -> Local AI");
                    }

                    notes.Add($"Kyra routing: normal chat -> {provider.DisplayName}");
                    var enhancementApplied = responseProvider.IsOnlineProvider && effectiveResult.UsedOnlineData;
                    var onlineResponse = _host.BuildResponse(effectiveResult, responseProvider, notes, status, enhancementApplied);

                    Report("Formatting Kyra response…");
                    var legacyOk = _host.CompleteResponse(request, context, onlineResponse);
                    Report("Done.");
                    return legacyOk;
                }

                if (provider.IsOnlineProvider)
                {
                    freePoolAttemptFailedLegacy = true;
                }

                if (i < candidates.Count - 1)
                {
                    notes.Add($"Kyra routing: provider failed ({provider.DisplayName}) -> trying next provider");
                }
            }

            if (settings.OfflineFallbackEnabled)
            {
                notes.Add("Kyra routing: all AI unavailable -> Local Kyra");
                Report("Using local fallback…");
                Report("Formatting Kyra response…");
                var allFail = _host.CompleteResponse(
                    request,
                    context,
                    _host.ApplyLocalKyraSourceLabel(
                        _host.BuildResponse(localResult, localProvider, notes, "All configured AI providers failed, so I answered with Local Kyra."),
                        plan,
                        context,
                        request.Prompt,
                        settings));
                Report("Done.");
                return allFail;
            }

            Report("Formatting Kyra response…");
            var errEnd = _host.CompleteResponse(request, context, new CopilotResponse
            {
                Text = "Kyra could not get a provider response and offline fallback is disabled. Re-enable offline fallback or check provider settings.",
                OnlineStatus = "Error state - no fallback available.",
                ProviderNotes = notes
            });
            Report("Done.");
            return errEnd;
        }
        catch (OperationCanceledException)
        {
            return new CopilotResponse
            {
                Text = "Kyra generation was stopped.",
                OnlineStatus = "Stopped",
                ProviderNotes = ["Request cancelled by operator."]
            };
        }
        catch (Exception exception)
        {
            return new CopilotResponse
            {
                Text = "Kyra hit an internal error and fell back safely. Try again after refreshing the System Intelligence scan.",
                OnlineStatus = "Error state - safe fallback",
                ProviderNotes = [$"Internal Kyra error: {exception.Message}"]
            };
        }
    }

    private async Task<CopilotProviderResult> RunInstrumentedAsync(
        ICopilotProvider provider,
        CopilotRequest request,
        CopilotSettings settings,
        CopilotContext context,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        var usedContext = context.SystemProfile is not null;
        var (legacy, norm) = await KyraProviderInstrumentedCall.RunAsync(
            () => _host.RunProviderSafeAsync(provider, request, settings, context, notes, cancellationToken),
            provider.Id,
            provider.IsOnlineProvider,
            usedContext,
            enhancementApplied: true,
            wasDiscarded: false,
            discardReason: string.Empty,
            requiresFallback: false).ConfigureAwait(false);

        KyraOrchestrationLog.Append(
            $"Kyra provider_run intent={context.Intent} providerId={norm.ProviderId} success={norm.Success} " +
            $"latencyMs={norm.LatencyMs} errorCategory={norm.ErrorCategory} usedOnlineData={legacy.UsedOnlineData} " +
            $"enhancementApplied={(norm.EnhancementApplied ? 1 : 0)} refused={norm.Refused} discarded={norm.WasDiscarded} " +
            $"localTruth={(KyraFactsLedger.FromCopilotContext(context).HasTrustedLocalHardwareFacts ? 1 : 0)}");

        return legacy;
    }

    private static string BuildCacheKey(string prompt) => prompt.Trim().ToLowerInvariant();
}
