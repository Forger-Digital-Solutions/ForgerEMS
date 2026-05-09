#pragma warning disable CA1822 // DI-injected service; methods called via instance reference
// FORGEREMS_KYRA_ADAPTER: ForgerEMS-specific coupling; stays in ForgerEMS.KyraAdapter.
// Host service: DI entry point, ForgerEMS scan integration, settings store binding.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Data;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Configuration;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using VentoyToolkitSetup.Wpf.Services.Kyra;
using VentoyToolkitSetup.Wpf.Services.KyraTools;

namespace VentoyToolkitSetup.Wpf.Services;

public sealed class CopilotService : ICopilotService
{
    private readonly ICopilotProviderRegistry _providerRegistry;
    private readonly ICopilotContextBuilder _contextBuilder;
    private readonly KyraToolRegistry _toolRegistry = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _providerRequests = new(StringComparer.OrdinalIgnoreCase);
    private readonly KyraProviderUsageTracker _usageTracker = new();
    private readonly KyraResponseCache _responseCache = new();
    private readonly KyraConversationMemory _memory;
    private CopilotSettings? _lastSettingsForMemory;
    private SystemContext _lastSystemContext = new();
    private readonly KyraOrchestrationHostAdapter _kyraHost;
    private readonly KyraOrchestrator _kyraOrchestrator;

    public bool UseOnlineAI { get; set; }

    public CopilotService(ICopilotProviderRegistry providerRegistry)
        : this(providerRegistry, new CopilotContextBuilder())
    {
    }

    public CopilotService(ICopilotProviderRegistry providerRegistry, ICopilotContextBuilder contextBuilder)
    {
        _providerRegistry = providerRegistry;
        _contextBuilder = contextBuilder;
        _memory = new KyraConversationMemory(ForgerEmsEnvironmentConfiguration.KyraMaxContextTurns, new KyraMemoryStore());
        _memory.SetPersistenceGate(() => _lastSettingsForMemory?.KyraPersistentMemoryEnabled == true ||
                                         _lastSettingsForMemory?.PersistMemory == true);
        _kyraHost = new KyraOrchestrationHostAdapter(this);
        _kyraOrchestrator = new KyraOrchestrator(_kyraHost, _providerRegistry, _contextBuilder, _toolRegistry);
    }

    public async Task<CopilotResponse> GenerateReplyAsync(CopilotRequest request, CancellationToken cancellationToken = default)
    {
        var settings = request.Settings ?? new CopilotSettings();
        _lastSettingsForMemory = settings;
        EnsureProviderDefaults(settings);
        UseOnlineAI = settings.Mode is CopilotMode.ForgerEmsBetaGateway or CopilotMode.OnlineAssisted or CopilotMode.HybridAuto or CopilotMode.OnlineWhenAvailable;
        return await _kyraOrchestrator.GenerateReplyAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public KyraIntent DetectIntent(string prompt) => _memory.ResolveIntent(prompt, KyraIntentRouter.DetectIntent(prompt));

    public SystemContext GetSystemContext() => _lastSystemContext;

    public async Task<string> GenerateResponse(string prompt)
    {
        var intent = DetectIntent(prompt);
        if (UseOnlineAI && intent == KyraIntent.GeneralTechQuestion)
        {
            return await CallExternalAPI(prompt).ConfigureAwait(false);
        }

        return intent switch
        {
            KyraIntent.PerformanceLag or KyraIntent.AppFreezing or KyraIntent.SlowBoot => HandlePerformance(prompt),
            KyraIntent.UpgradeAdvice => HandleUpgrade(prompt),
            KyraIntent.ResaleValue => HandleResale(prompt),
            KyraIntent.SystemHealthSummary => HandleSystem(prompt),
            KyraIntent.GeneralTechQuestion => await HandleGeneral(prompt).ConfigureAwait(false),
            _ => LocalResponse(prompt)
        };
    }

    public void ClearMemory()
    {
        _memory.Clear();
    }

    private string HandlePerformance(string prompt) => LocalResponse(prompt);

    private string HandleUpgrade(string prompt) => LocalResponse(prompt);

    private string HandleResale(string prompt) => LocalResponse(prompt);

    private string HandleSystem(string prompt) => LocalResponse(prompt);

    private Task<string> HandleGeneral(string prompt) => Task.FromResult(LocalResponse(prompt));

    private string LocalResponse(string prompt)
    {
        var context = new CopilotContext
        {
            UserQuestion = prompt,
            Intent = DetectIntent(prompt),
            PreviousIntent = _memory.PreviousIntent,
            SystemContext = GetSystemContext(),
            ConversationHistory = GetHistorySnapshot()
        };
        return LocalRulesCopilotEngine.GenerateReply(prompt, context);
    }

    private static Task<string> CallExternalAPI(string prompt)
    {
        return Task.FromResult("Online Kyra is ready for provider wiring, but no external provider is configured in this build. I can still help offline with this PC, USB builds, resale prep, OS choices, and troubleshooting.");
    }

    private CopilotContext AttachConversationMemory(CopilotContext context)
    {
        if (context.Intent == KyraIntent.CodeAssist || KyraCodeSnippetDetector.LooksLikeCodeSnippet(context.UserQuestion))
        {
            return new CopilotContext
            {
                UserQuestion = context.UserQuestion,
                ContextText = context.ContextText,
                PromptMode = context.PromptMode,
                Intent = KyraIntent.CodeAssist,
                PreviousIntent = KyraIntent.Unknown,
                SystemContext = context.SystemContext,
                ConversationHistory = Array.Empty<CopilotChatMessage>(),
                ConversationMeta = new KyraConversationContext
                {
                    CurrentUserMessage = context.UserQuestion,
                    CurrentIntent = KyraIntent.CodeAssist,
                    PreviousIntent = KyraIntent.Unknown
                },
                SystemProfile = context.SystemProfile,
                HealthEvaluation = context.HealthEvaluation,
                Recommendations = context.Recommendations,
                PricingEstimate = context.PricingEstimate,
                ProviderRealtimeAugmentation = context.ProviderRealtimeAugmentation,
                PersonalityProfile = context.PersonalityProfile
            };
        }

        if (KyraPromptIsolation.ShouldIsolateFromConversationMemory(context.UserQuestion, context.Intent))
        {
            return new CopilotContext
            {
                UserQuestion = context.UserQuestion,
                ContextText = context.ContextText,
                PromptMode = context.PromptMode,
                Intent = context.Intent,
                PreviousIntent = KyraIntent.Unknown,
                SystemContext = context.SystemContext,
                ConversationHistory = Array.Empty<CopilotChatMessage>(),
                ConversationMeta = new KyraConversationContext
                {
                    CurrentUserMessage = context.UserQuestion,
                    CurrentIntent = context.Intent,
                    PreviousIntent = KyraIntent.Unknown
                },
                SystemProfile = context.SystemProfile,
                HealthEvaluation = context.HealthEvaluation,
                Recommendations = context.Recommendations,
                PricingEstimate = context.PricingEstimate,
                ProviderRealtimeAugmentation = context.ProviderRealtimeAugmentation,
                PersonalityProfile = context.PersonalityProfile
            };
        }

        var resolvedIntent = _memory.ResolveIntent(context.UserQuestion, context.Intent);
        var conversationMeta = KyraConversationContext.Capture(_memory, context.UserQuestion, resolvedIntent);
        return new CopilotContext
        {
            UserQuestion = context.UserQuestion,
            ContextText = context.ContextText,
            PromptMode = context.PromptMode,
            Intent = resolvedIntent,
            PreviousIntent = _memory.PreviousIntent,
            SystemContext = context.SystemContext,
            ConversationHistory = GetHistorySnapshot(),
            ConversationMeta = conversationMeta,
            SystemProfile = context.SystemProfile,
            HealthEvaluation = context.HealthEvaluation,
            Recommendations = context.Recommendations,
            PricingEstimate = context.PricingEstimate,
            ProviderRealtimeAugmentation = context.ProviderRealtimeAugmentation,
            PersonalityProfile = context.PersonalityProfile
        };
    }

    private static CopilotContext AttachToolAugmentation(CopilotContext context, string? augmentation, CopilotSettings settings)
    {
        if (string.IsNullOrWhiteSpace(augmentation))
        {
            return context;
        }

        var safe = KyraSystemContextSanitizer.SanitizeForExternalProviders(augmentation.Trim());
        var block = Environment.NewLine + Environment.NewLine + "Real-time tool context (informational; verify figures):" + Environment.NewLine + safe;
        var newText = context.ContextText + block;
        if (settings.MaxContextCharacters > 0 && newText.Length > settings.MaxContextCharacters)
        {
            newText = newText[..settings.MaxContextCharacters] + Environment.NewLine + "[context trimmed]";
        }

        return new CopilotContext
        {
            UserQuestion = context.UserQuestion,
            ContextText = newText,
            PromptMode = context.PromptMode,
            Intent = context.Intent,
            PreviousIntent = context.PreviousIntent,
            SystemContext = context.SystemContext,
            ConversationHistory = context.ConversationHistory,
            ConversationMeta = context.ConversationMeta,
            SystemProfile = context.SystemProfile,
            HealthEvaluation = context.HealthEvaluation,
            Recommendations = context.Recommendations,
            PricingEstimate = context.PricingEstimate,
            ProviderRealtimeAugmentation = safe,
            PersonalityProfile = context.PersonalityProfile
        };
    }

    private CopilotResponse CompleteResponse(CopilotRequest request, CopilotContext context, CopilotResponse response)
    {
        RecordConversationTurn(request.Prompt, response.Text, context.Intent);
        var filteredNotes = FilterProviderNotesForDisplay(response.ProviderNotes, request.VerboseDiagnosticNotes);
        var grounded = context.SystemProfile is not null;
        if (filteredNotes.Count == response.ProviderNotes.Count &&
            response.GroundedInSystemIntelligence == grounded)
        {
            return response;
        }

        return new CopilotResponse
        {
            Text = response.Text,
            UsedOnlineData = response.UsedOnlineData,
            OnlineStatus = response.OnlineStatus,
            ProviderType = response.ProviderType,
            ProviderNotes = filteredNotes,
            ResponseSource = response.ResponseSource,
            SourceLabel = response.SourceLabel,
            FallbackUsed = response.FallbackUsed,
            OnlineEnhancementApplied = response.OnlineEnhancementApplied,
            GroundedInSystemIntelligence = grounded,
            ActionSuggestions = response.ActionSuggestions,
            KyraTransparencySummary = response.KyraTransparencySummary
        };
    }

    private static IReadOnlyList<string> FilterProviderNotesForDisplay(IReadOnlyList<string> notes, bool verbose)
    {
        if (verbose || notes.Count == 0)
        {
            return notes;
        }

        return notes
            .Where(static note =>
                note.StartsWith("Intent detected:", StringComparison.OrdinalIgnoreCase) ||
                note.StartsWith("Previous intent:", StringComparison.OrdinalIgnoreCase) ||
                note.StartsWith("Tool plan:", StringComparison.OrdinalIgnoreCase) ||
                note.StartsWith("Kyra provider skipped:", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private CopilotChatMessage[] GetHistorySnapshot() => _memory.ToChatMessages();

    private void RecordConversationTurn(string prompt, string response, KyraIntent intent)
    {
        _memory.AddTurn(prompt, response, intent, GetSystemContext());
    }

    private static TimeSpan? ComputeProviderFailureCooldown(
        KyraProviderFailureReason reason,
        bool transient,
        int consecutiveFailures)
    {
        if (reason is KyraProviderFailureReason.None or KyraProviderFailureReason.SafetyBlocked)
        {
            return null;
        }

        if (reason == KyraProviderFailureReason.NotConfigured)
        {
            return TimeSpan.FromSeconds(45);
        }

        if (reason == KyraProviderFailureReason.RateLimited)
        {
            return TimeSpan.FromMinutes(10);
        }

        if (!transient && reason != KyraProviderFailureReason.Unknown)
        {
            return TimeSpan.FromSeconds(30);
        }

        var baseSeconds = reason switch
        {
            KyraProviderFailureReason.Timeout => 90,
            KyraProviderFailureReason.NetworkError => 75,
            KyraProviderFailureReason.ServiceUnavailable => 90,
            _ => 60
        };

        if (consecutiveFailures >= 3)
        {
            baseSeconds = Math.Max(baseSeconds, 120);
        }

        return TimeSpan.FromSeconds(baseSeconds);
    }

    private async Task<CopilotProviderResult> RunProviderSafeAsync(
        ICopilotProvider provider,
        CopilotRequest request,
        CopilotSettings settings,
        CopilotContext context,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        if (provider.IsOnlineProvider && !KyraOnlineSafetyGate.IsAllowedToCallOnline(request.Prompt, out _))
        {
            return new CopilotProviderResult
            {
                Succeeded = false,
                FailureReason = KyraProviderFailureReason.SafetyBlocked,
                UserMessage = "Kyra blocked this request before contacting any online provider."
            };
        }

        var providerConfig = GetProviderConfig(settings, provider);
        var resolvedConfig = KyraProviderConfigResolver.ResolveProvider(provider, providerConfig);
        var quotaState = _usageTracker.GetOrCreate(provider.Id);
        quotaState.IsConfigured = provider.IsConfigured(providerConfig) && resolvedConfig.IsReady;
        quotaState.IsEnabled = providerConfig.IsEnabled;
        if (quotaState.CooldownUntilUtc is not null && quotaState.CooldownUntilUtc > DateTimeOffset.UtcNow)
        {
            var sec = Math.Max(1, (int)Math.Ceiling((quotaState.CooldownUntilUtc.Value - DateTimeOffset.UtcNow).TotalSeconds));
            notes.Add($"Kyra provider skipped: {provider.DisplayName} cooling down (~{sec}s)");
            return new CopilotProviderResult
            {
                Succeeded = false,
                FailureReason = KyraProviderFailureReason.RateLimited,
                IsTransientFailure = true,
                UserMessage = $"{provider.DisplayName} appears rate-limited right now. I’m trying the next configured provider."
            };
        }

        if (!resolvedConfig.IsReady)
        {
            notes.Add($"{provider.DisplayName}: skipped ({resolvedConfig.SafeSkipReason})");
            return new CopilotProviderResult
            {
                Succeeded = false,
                FailureReason = KyraProviderFailureReason.NotConfigured,
                UserMessage = $"{provider.DisplayName} is not configured.",
                DiagnosticMessage = resolvedConfig.SafeSkipReason ?? string.Empty
            };
        }

        if (!provider.IsConfigured(providerConfig))
        {
            notes.Add($"{provider.DisplayName}: not configured");
            return new CopilotProviderResult
            {
                Succeeded = false,
                FailureReason = KyraProviderFailureReason.NotConfigured,
                UserMessage = $"{provider.DisplayName} is not configured.",
                DiagnosticMessage = provider.StatusText
            };
        }

        if (!TryEnterRateLimit(provider, providerConfig, notes))
        {
            return new CopilotProviderResult
            {
                Succeeded = false,
                FailureReason = KyraProviderFailureReason.RateLimited,
                UserMessage = $"{provider.DisplayName} appears rate-limited right now. I’m trying the next configured provider.",
                DiagnosticMessage = "Rate limit reached."
            };
        }

        if (provider.IsOnlineProvider && quotaState.DailyRequestCount >= Math.Max(1, providerConfig.DailyRequestCap))
        {
            return new CopilotProviderResult
            {
                Succeeded = false,
                FailureReason = KyraProviderFailureReason.RateLimited,
                UserMessage = $"{provider.DisplayName} local daily cap reached."
            };
        }

        var providerContext = provider.IsOnlineProvider
            ? KyraPrivacyGate.BuildProviderContext(context, settings.AllowOnlineSystemContextSharing)
            : context;

        var providerRequest = new CopilotProviderRequest
        {
            AppVersion = request.AppVersion,
            Prompt = request.Prompt,
            Context = providerContext,
            Settings = settings,
            ProviderConfiguration = providerConfig
        };

        var attempts = Math.Clamp(providerConfig.MaxRetries, 0, 3) + 1;
        CopilotProviderResult lastResult = new()
        {
            Succeeded = false,
            UserMessage = $"{provider.DisplayName} did not return a response."
        };

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(providerConfig.TimeoutSeconds, 2, 60)));
                lastResult = await provider.GenerateAsync(providerRequest, timeout.Token).ConfigureAwait(false);
                notes.Add($"{provider.DisplayName}: {(lastResult.Succeeded ? "OK" : lastResult.DiagnosticMessage)}");
                if (lastResult.Succeeded || !lastResult.IsTransientFailure)
                {
                    if (lastResult.Succeeded)
                    {
                        quotaState.LastSuccessUtc = DateTimeOffset.UtcNow;
                        quotaState.ConsecutiveFailures = 0;
                        quotaState.CooldownUntilUtc = null;
                        quotaState.DailyRequestCount++;
                    }
                    return lastResult;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastResult = new CopilotProviderResult
                {
                    Succeeded = false,
                    FailureReason = KyraProviderFailureReason.Timeout,
                    IsTransientFailure = true,
                    UserMessage = $"{provider.DisplayName} timed out.",
                    DiagnosticMessage = "Provider timeout."
                };
                quotaState.TimeoutCount++;
                notes.Add($"{provider.DisplayName}: timeout on attempt {attempt}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException exception)
            {
                lastResult = new CopilotProviderResult
                {
                    Succeeded = false,
                    FailureReason = KyraProviderFailureReason.NetworkError,
                    IsTransientFailure = true,
                    UserMessage = $"{provider.DisplayName} network request failed.",
                    DiagnosticMessage = exception.Message
                };
                quotaState.ErrorCount++;
                notes.Add($"{provider.DisplayName}: network failure on attempt {attempt}");
            }
            catch (Exception exception)
            {
                lastResult = new CopilotProviderResult
                {
                    Succeeded = false,
                    FailureReason = KyraProviderFailureReason.Unknown,
                    UserMessage = $"{provider.DisplayName} failed safely.",
                    DiagnosticMessage = exception.Message
                };
                quotaState.ErrorCount++;
                notes.Add($"{provider.DisplayName}: failed safely ({exception.Message})");
                return lastResult;
            }

            if (attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }

        quotaState.LastFailureUtc = DateTimeOffset.UtcNow;
        quotaState.LastFailureReason = lastResult.FailureReason;
        quotaState.ConsecutiveFailures++;
        if (!lastResult.Succeeded)
        {
            var cool = ComputeProviderFailureCooldown(lastResult.FailureReason, lastResult.IsTransientFailure, quotaState.ConsecutiveFailures);
            if (cool.HasValue)
            {
                var until = DateTimeOffset.UtcNow.Add(cool.Value);
                if (quotaState.CooldownUntilUtc is null || until > quotaState.CooldownUntilUtc)
                {
                    quotaState.CooldownUntilUtc = until;
                }
            }
        }

        return lastResult;
    }

    private IEnumerable<ICopilotProvider> SelectOnlineProviders(CopilotRequest request, CopilotSettings settings, CopilotContext context)
    {
        if (!KyraProviderRouter.ShouldUseOnline(context, settings))
        {
            return Array.Empty<ICopilotProvider>();
        }

        var scored = KyraProviderRouter.ScoreProviders(
            _providerRegistry.Providers,
            request,
            settings,
            context,
            provider => GetProviderConfig(settings, provider),
            _usageTracker);

        return scored.Select(item => item.Provider);
    }

    private bool TryEnterRateLimit(ICopilotProvider provider, CopilotProviderConfiguration configuration, List<string> notes)
    {
        var now = DateTimeOffset.UtcNow;
        if (!_providerRequests.TryGetValue(provider.Id, out var queue))
        {
            queue = new Queue<DateTimeOffset>();
            _providerRequests[provider.Id] = queue;
        }

        while (queue.Count > 0 && now - queue.Peek() > TimeSpan.FromMinutes(1))
        {
            queue.Dequeue();
        }

        if (queue.Count >= Math.Max(1, configuration.MaxRequestsPerMinute))
        {
            notes.Add($"{provider.DisplayName}: rate limit reached");
            return false;
        }

        queue.Enqueue(now);
        return true;
    }

    public void EnsureProviderDefaults(CopilotSettings settings)
    {
        settings.LiveTools ??= new KyraLiveToolsSettings();
        KyraProviderConfigResolver.ApplyLiveToolEnvironmentDefaults(settings.LiveTools);

        foreach (var provider in _providerRegistry.Providers)
        {
            _ = GetProviderConfig(settings, provider);
        }
    }

    private static CopilotProviderConfiguration GetProviderConfig(CopilotSettings settings, ICopilotProvider provider)
    {
        if (!settings.Providers.TryGetValue(provider.Id, out var providerConfig))
        {
            providerConfig = new CopilotProviderConfiguration
            {
                IsEnabled = provider.EnabledByDefault,
                BaseUrl = provider.DefaultBaseUrl,
                ModelName = provider.DefaultModelName,
                ApiKeyEnvironmentVariable = provider.DefaultApiKeyEnvironmentVariable,
                TimeoutSeconds = settings.TimeoutSeconds,
                MaxRequestsPerMinute = 12,
                MaxRetries = provider.IsOnlineProvider ? 1 : 0,
                DailyRequestCap = provider.IsOnlineProvider ? 60 : int.MaxValue,
                MaxInputCharacters = settings.MaxInputCharactersOnline,
                MaxOutputTokens = settings.MaxOutputTokensOnline
            };
            settings.Providers[provider.Id] = providerConfig;
        }

        if (string.IsNullOrWhiteSpace(providerConfig.BaseUrl) ||
            KyraProviderConfigResolver.IsPlaceholderSecretOrValue(providerConfig.BaseUrl))
        {
            providerConfig.BaseUrl = provider.DefaultBaseUrl;
        }

        if (string.IsNullOrWhiteSpace(providerConfig.ModelName) ||
            KyraProviderConfigResolver.IsPlaceholderSecretOrValue(providerConfig.ModelName))
        {
            providerConfig.ModelName = provider.DefaultModelName;
        }

        if (string.IsNullOrWhiteSpace(providerConfig.ApiKeyEnvironmentVariable))
        {
            providerConfig.ApiKeyEnvironmentVariable = provider.DefaultApiKeyEnvironmentVariable;
        }

        if (providerConfig.TimeoutSeconds <= 0)
        {
            providerConfig.TimeoutSeconds = Math.Max(2, settings.TimeoutSeconds);
        }

        return providerConfig;
    }

    private static string BuildCacheKey(string prompt)
    {
        return prompt.Trim().ToLowerInvariant();
    }

    /// <summary>Loads System Intelligence JSON for slash commands and host snapshots (same mapping as Kyra context).</summary>
    public static SystemProfile? TryLoadSystemProfileFromReport(string? reportPath)
    {
        if (string.IsNullOrWhiteSpace(reportPath) || !File.Exists(reportPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            return SystemProfileMapper.FromJson(document.RootElement);
        }
        catch
        {
            return null;
        }
    }

    private sealed class KyraOrchestrationHostAdapter : IKyraOrchestrationHost
    {
        private readonly CopilotService _owner;

        public KyraOrchestrationHostAdapter(CopilotService owner) => _owner = owner;

        public KyraConversationMemory Memory => _owner._memory;

        public KyraProviderUsageTracker ProviderUsage => _owner._usageTracker;

        public void SetLastSystemContext(SystemContext context) => _owner._lastSystemContext = context;

        public CopilotProviderConfiguration ResolveProviderConfig(CopilotSettings settings, ICopilotProvider provider) =>
            GetProviderConfig(settings, provider);

        public Task<CopilotProviderResult> RunProviderSafeAsync(
            ICopilotProvider provider,
            CopilotRequest request,
            CopilotSettings settings,
            CopilotContext context,
            List<string> notes,
            CancellationToken cancellationToken) =>
            _owner.RunProviderSafeAsync(provider, request, settings, context, notes, cancellationToken);

        public CopilotResponse BuildResponse(
            CopilotProviderResult result,
            ICopilotProvider provider,
            List<string> notes,
            string onlineStatus,
            bool onlineEnhancementApplied = false) =>
            KyraCopilotResponseBuilder.Build(result, provider, notes, onlineStatus, onlineEnhancementApplied);

        public CopilotResponse CompleteResponse(CopilotRequest request, CopilotContext context, CopilotResponse response) =>
            _owner.CompleteResponse(request, context, response);

        public CopilotResponse ApplyLocalKyraSourceLabel(
            CopilotResponse response,
            KyraToolCallPlan plan,
            CopilotContext context,
            string prompt,
            CopilotSettings settings) =>
            KyraCopilotResponseBuilder.ApplyLocalKyraSourceLabel(response, plan, context, prompt, settings);

        public CopilotContext AttachConversationMemory(CopilotContext context) => _owner.AttachConversationMemory(context);

        public CopilotContext AttachToolAugmentation(CopilotContext context, string? augmentation, CopilotSettings settings) =>
            CopilotService.AttachToolAugmentation(context, augmentation, settings);

        public bool TryGetResponseCache(string key, out string value) => _owner._responseCache.TryGet(key, out value);

        public void StoreResponseCache(string key, string value) => _owner._responseCache.Store(key, value);
    }
}
