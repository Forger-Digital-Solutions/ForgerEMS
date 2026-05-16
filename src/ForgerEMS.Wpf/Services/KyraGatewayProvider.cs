using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using VentoyToolkitSetup.Wpf.Configuration;
using VentoyToolkitSetup.Wpf.Services.Kyra;

namespace VentoyToolkitSetup.Wpf.Services;

public sealed class KyraGatewayProviderConfig
{
    public string GatewayUrl { get; init; } = string.Empty;

    public string BetaToken { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; } = 60;

    public int DailyRequestLimit { get; init; }

    public bool ShareSystemContext { get; init; }

    public KyraProviderEndpointState UrlState { get; init; }

    public KyraProviderCredentialState TokenState { get; init; }

    public bool IsConfigured =>
        UrlState == KyraProviderEndpointState.Ready &&
        TokenState is not KyraProviderCredentialState.Missing and not KyraProviderCredentialState.Placeholder &&
        !string.IsNullOrWhiteSpace(BetaToken);

    public string GatewayHost
    {
        get
        {
            return Uri.TryCreate(GatewayUrl, UriKind.Absolute, out var uri)
                ? uri.Host
                : string.Empty;
        }
    }

    public static KyraGatewayProviderConfig FromEnvironment()
    {
        var url = ForgerEmsEnvironmentConfiguration.KyraGatewayUrl;
        return new KyraGatewayProviderConfig
        {
            GatewayUrl = url,
            BetaToken = KyraProviderConfigResolver.ResolveApiKeyValue(
                KyraGatewayProvider.ProviderId,
                CopilotProviderEnvironmentVariableNames.KyraGatewayBetaToken,
                url),
            TimeoutSeconds = ForgerEmsEnvironmentConfiguration.KyraGatewayTimeoutSeconds,
            DailyRequestLimit = ForgerEmsEnvironmentConfiguration.KyraGatewayDailyRequestLimit,
            ShareSystemContext = ForgerEmsEnvironmentConfiguration.KyraGatewayShareSystemContext,
            UrlState = ResolveUrlState(url),
            TokenState = KyraProviderConfigResolver.ResolveNamedCredential(CopilotProviderEnvironmentVariableNames.KyraGatewayBetaToken)
        };
    }

    public static KyraGatewayProviderConfig FromProviderConfiguration(CopilotProviderConfiguration configuration)
    {
        var url = KyraProviderConfigResolver.IsMissingOrPlaceholder(configuration.BaseUrl)
            ? ForgerEmsEnvironmentConfiguration.KyraGatewayUrl
            : configuration.BaseUrl.Trim();
        var envName = string.IsNullOrWhiteSpace(configuration.ApiKeyEnvironmentVariable)
            ? CopilotProviderEnvironmentVariableNames.KyraGatewayBetaToken
            : configuration.ApiKeyEnvironmentVariable.Trim();

        return new KyraGatewayProviderConfig
        {
            GatewayUrl = url,
            BetaToken = KyraProviderConfigResolver.ResolveApiKeyValue(KyraGatewayProvider.ProviderId, envName, url),
            TimeoutSeconds = configuration.TimeoutSeconds > 0 ? configuration.TimeoutSeconds : ForgerEmsEnvironmentConfiguration.KyraGatewayTimeoutSeconds,
            DailyRequestLimit = configuration.DailyRequestCap > 0 ? configuration.DailyRequestCap : ForgerEmsEnvironmentConfiguration.KyraGatewayDailyRequestLimit,
            ShareSystemContext = ForgerEmsEnvironmentConfiguration.KyraGatewayShareSystemContext,
            UrlState = ResolveUrlState(url),
            TokenState = KyraProviderConfigResolver.ResolveCredentialState(KyraGatewayProvider.ProviderId, envName, url)
        };
    }

    private static KyraProviderEndpointState ResolveUrlState(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return KyraProviderEndpointState.Missing;
        }

        if (KyraProviderConfigResolver.IsPlaceholderSecretOrValue(url))
        {
            return KyraProviderEndpointState.Placeholder;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http"))
        {
            return KyraProviderEndpointState.InvalidUrl;
        }

        return string.IsNullOrWhiteSpace(uri.UserInfo)
            ? KyraProviderEndpointState.Ready
            : KyraProviderEndpointState.EmbeddedCredentials;
    }
}

public sealed class KyraGatewayRequest
{
    public string AppVersion { get; init; } = string.Empty;

    public string ReleaseChannel { get; init; } = string.Empty;

    public string LicenseTier { get; init; } = string.Empty;

    public string BetaToken { get; init; } = string.Empty;

    public string ConversationId { get; init; } = string.Empty;

    public string MessageId { get; init; } = string.Empty;

    public string UserMessage { get; init; } = string.Empty;

    public string Personality { get; init; } = string.Empty;

    public string Intent { get; init; } = string.Empty;

    public IReadOnlyList<string> ToolsRequested { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, string>? MachineContext { get; init; }

    public string MemorySummary { get; init; } = string.Empty;

    public int MaxTokens { get; init; } = 1000;

    public double Temperature { get; init; } = 0.5;
}

public sealed class KyraGatewayResponse
{
    public bool Ok { get; init; }

    public string ProviderUsed { get; init; } = string.Empty;

    public string ModelUsed { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public JsonElement[] ToolResults { get; init; } = Array.Empty<JsonElement>();

    public bool FallbackUsed { get; init; }

    public KyraGatewayRateLimit? RateLimit { get; init; }

    public string DiagnosticNote { get; init; } = string.Empty;

    public string ErrorCode { get; init; } = string.Empty;

    public int? RetryAfterSeconds { get; init; }
}

public sealed class KyraGatewayRateLimit
{
    public int? RemainingToday { get; init; }

    public DateTimeOffset? ResetUtc { get; init; }
}

public sealed class KyraGatewayClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public KyraGatewayClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<KyraGatewayResponse> SendAsync(
        string gatewayUrl,
        KyraGatewayRequest request,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 2, 120)));

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, gatewayUrl);
        httpRequest.Headers.TryAddWithoutValidation("Authorization", "Bearer " + request.BetaToken.Trim());
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(BuildWireRequest(request), JsonOptions), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, timeout.Token).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        KyraGatewayResponse? parsed = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                parsed = JsonSerializer.Deserialize<KyraGatewayResponse>(body, JsonOptions);
            }
            catch (JsonException)
            {
                parsed = null;
            }
        }

        if (response.IsSuccessStatusCode && parsed is not null)
        {
            return parsed;
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return parsed ?? new KyraGatewayResponse
            {
                Ok = false,
                ErrorCode = "BetaLimitReached",
                Message = "Kyra beta API time is used up for today. Local/offline mode is still available.",
                RetryAfterSeconds = TryGetRetryAfterSeconds(response)
            };
        }

        return parsed ?? new KyraGatewayResponse
        {
            Ok = false,
            ErrorCode = $"GatewayHttp{(int)response.StatusCode}",
            Message = "Kyra Gateway returned an error. Local/offline mode is still available."
        };
    }

    private static int? TryGetRetryAfterSeconds(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return (int)Math.Ceiling(delta.TotalSeconds);
        }

        return null;
    }

    private static object BuildWireRequest(KyraGatewayRequest request) => new
    {
        request.AppVersion,
        request.ReleaseChannel,
        request.LicenseTier,
        request.ConversationId,
        request.MessageId,
        request.UserMessage,
        request.Personality,
        request.Intent,
        request.ToolsRequested,
        request.MachineContext,
        request.MemorySummary,
        request.MaxTokens,
        request.Temperature
    };
}

public sealed class KyraGatewayProvider : ICopilotProvider
{
    public const string ProviderId = "forgerems-gateway";
    private readonly KyraGatewayClient _client;

    public KyraGatewayProvider(KyraGatewayClient? client = null)
    {
        _client = client ?? new KyraGatewayClient();
    }

    public string Id => ProviderId;

    public string DisplayName => "ForgerEMS Gateway";

    public CopilotProviderType ProviderType => CopilotProviderType.ForgerEmsGateway;

    public string Category => "ForgerEMS Beta Gateway";

    public bool IsOnlineProvider => true;

    public bool IsPaidProvider => false;

    public bool EnabledByDefault => ForgerEmsEnvironmentConfiguration.KyraGatewayConfigured;

    public string DefaultBaseUrl => ForgerEmsEnvironmentConfiguration.KyraGatewayUrl;

    public string DefaultModelName => "forgerems-gateway";

    public string DefaultApiKeyEnvironmentVariable => CopilotProviderEnvironmentVariableNames.KyraGatewayBetaToken;

    public string StatusText => "ForgerEMS beta gateway. Uses a revocable gateway token only; provider API keys stay server-side.";

    public bool IsConfigured(CopilotProviderConfiguration configuration) =>
        KyraGatewayProviderConfig.FromProviderConfiguration(configuration).IsConfigured;

    /// <summary>
    /// Gateway worker is for <b>live tool / research</b> flows (crypto, weather, system tools, etc.), not generic casual chat.
    /// Empty tool list + non-research intent → use Groq/OpenRouter/Ollama instead so we don’t spam a failing chat path.
    /// </summary>
    public bool CanHandle(CopilotProviderRequest request)
    {
        var ctx = request.Context;
        if (BuildToolList(ctx).Count > 0)
        {
            return true;
        }

        return ctx.Intent == KyraIntent.LiveOnlineQuestion;
    }

    public async Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken)
    {
        if (!ForgerEmsEnvironmentConfiguration.KyraGatewayEnabled)
        {
            return GatewayNotConfigured("gateway disabled via FORGEREMS_KYRA_GATEWAY_ENABLED");
        }

        if (!request.Settings.KyraRealtimeGatewayEnabled)
        {
            return GatewayNotConfigured("realtime gateway disabled in Kyra settings");
        }

        var config = KyraGatewayProviderConfig.FromProviderConfiguration(request.ProviderConfiguration);
        if (config.UrlState != KyraProviderEndpointState.Ready)
        {
            return GatewayNotConfigured("gateway URL is missing, placeholder, invalid, or contains embedded credentials");
        }

        if (config.TokenState is KyraProviderCredentialState.Missing or KyraProviderCredentialState.Placeholder ||
            string.IsNullOrWhiteSpace(config.BetaToken))
        {
            return GatewayNotConfigured("gateway beta token is missing or a placeholder");
        }

        var gatewayRequest = BuildGatewayRequest(request, config);
        try
        {
            var response = await _client.SendAsync(config.GatewayUrl, gatewayRequest, config.TimeoutSeconds, cancellationToken).ConfigureAwait(false);
            if (response.Ok)
            {
                return new CopilotProviderResult
                {
                    Succeeded = true,
                    UsedOnlineData = true,
                    UserMessage = string.IsNullOrWhiteSpace(response.Message)
                        ? "Kyra Gateway returned an empty response."
                        : response.Message,
                    DiagnosticMessage = BuildSafeDiagnostic(response)
                };
            }

            var isLimit = response.ErrorCode.Equals("BetaLimitReached", StringComparison.OrdinalIgnoreCase);
            return new CopilotProviderResult
            {
                Succeeded = false,
                IsTransientFailure = true,
                FailureReason = isLimit ? KyraProviderFailureReason.RateLimited : KyraProviderFailureReason.ServiceUnavailable,
                UserMessage = string.IsNullOrWhiteSpace(response.Message)
                    ? "Kyra Gateway is unavailable. Local/offline mode is still available."
                    : response.Message,
                DiagnosticMessage = BuildSafeErrorDiagnostic(response)
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new CopilotProviderResult
            {
                Succeeded = false,
                IsTransientFailure = true,
                FailureReason = KyraProviderFailureReason.Timeout,
                UserMessage = "Kyra Gateway timed out. Local/offline mode is still available.",
                DiagnosticMessage = "provider=forgerems-gateway error=timeout"
            };
        }
        catch (HttpRequestException)
        {
            return new CopilotProviderResult
            {
                Succeeded = false,
                IsTransientFailure = true,
                FailureReason = KyraProviderFailureReason.NetworkError,
                UserMessage = "Kyra Gateway network request failed. Local/offline mode is still available.",
                DiagnosticMessage = "provider=forgerems-gateway error=network"
            };
        }
    }

    public static KyraGatewayRequest BuildGatewayRequest(CopilotProviderRequest request, KyraGatewayProviderConfig config)
    {
        var shareSystemContext = request.Settings.AllowOnlineSystemContextSharing && config.ShareSystemContext;
        return new KyraGatewayRequest
        {
            AppVersion = CopilotRedactor.Redact(request.AppVersion, enabled: true),
            ReleaseChannel = ForgerEmsEnvironmentConfiguration.ReleaseChannel,
            LicenseTier = ForgerEmsEnvironmentConfiguration.LicenseTierRaw,
            BetaToken = config.BetaToken,
            ConversationId = $"local-{Guid.NewGuid():N}",
            MessageId = $"msg-{Guid.NewGuid():N}",
            UserMessage = CopilotRedactor.Redact(request.Prompt, enabled: true),
            Personality = request.Settings.PersonalityProfile,
            Intent = request.Context.Intent.ToString(),
            ToolsRequested = BuildToolList(request.Context),
            MachineContext = shareSystemContext
                ? new Dictionary<string, string>
                {
                    ["summary"] = KyraPrivacyGate.BuildSanitizedProviderSummary(request.Context)
                }
                : null,
            MemorySummary = BuildMemorySummary(request.Context),
            MaxTokens = Math.Clamp(request.ProviderConfiguration.MaxOutputTokens, 128, 2048),
            Temperature = 0.5
        };
    }

    private static CopilotProviderResult GatewayNotConfigured(string reason) =>
        new()
        {
            Succeeded = false,
            FailureReason = KyraProviderFailureReason.NotConfigured,
            UserMessage = "ForgerEMS Gateway is not configured. Local/offline mode is still available.",
            DiagnosticMessage = $"provider=forgerems-gateway skipped={reason}"
        };

    private static IReadOnlyList<string> BuildToolList(CopilotContext context) =>
        context.Intent switch
        {
            KyraIntent.SystemHealthSummary or KyraIntent.PerformanceLag or KyraIntent.AppFreezing
                or KyraIntent.SlowBoot or KyraIntent.UpgradeAdvice or KyraIntent.DriverIssue
                or KyraIntent.StorageIssue or KyraIntent.MemoryIssue or KyraIntent.GPUQuestion
                or KyraIntent.OSRecommendation => ["system"],
            KyraIntent.Weather => ["weather"],
            KyraIntent.News => ["news"],
            KyraIntent.StockPrice => ["stocks"],
            KyraIntent.CryptoPrice => ["crypto"],
            KyraIntent.Sports => ["sports"],
            _ => []
        };

    private static string BuildMemorySummary(CopilotContext context)
    {
        if (!KyraPromptIsolation.LooksLikeExplicitThreadContinuation(context.UserQuestion))
        {
            return string.Empty;
        }

        var recap = KyraProviderPromptBuilder.FormatConversationRecap(context);
        if (string.IsNullOrWhiteSpace(recap))
        {
            return string.Empty;
        }

        return recap.Length <= 2000 ? recap : recap[..2000] + Environment.NewLine + "[trimmed]";
    }

    private static string BuildSafeDiagnostic(KyraGatewayResponse response)
    {
        var parts = new List<string>
        {
            "provider=forgerems-gateway",
            $"providerUsed={SafeDiagnosticValue(response.ProviderUsed, "gateway")}",
            $"model={SafeDiagnosticValue(response.ModelUsed, "unknown")}",
            $"fallbackUsed={response.FallbackUsed.ToString().ToLowerInvariant()}"
        };

        if (response.RateLimit?.RemainingToday is { } remaining)
        {
            parts.Add($"remainingToday={remaining}");
        }

        return string.Join(" ", parts);
    }

    private static string BuildSafeErrorDiagnostic(KyraGatewayResponse response)
    {
        var code = SafeDiagnosticValue(response.ErrorCode, "GatewayError");
        var retry = response.RetryAfterSeconds.HasValue ? $" retryAfterSeconds={response.RetryAfterSeconds.Value}" : string.Empty;
        return $"provider=forgerems-gateway errorCode={code}{retry}";
    }

    private static string SafeDiagnosticValue(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var redacted = CopilotRedactor.Redact(value.Trim(), enabled: true);
        return redacted.Replace(' ', '_');
    }
}
