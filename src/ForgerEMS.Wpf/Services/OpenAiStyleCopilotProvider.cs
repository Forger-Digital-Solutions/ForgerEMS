#pragma warning disable CA1822 // DI-related code; instance methods called via interface references
// KYRA_CORE_CANDIDATE: No ForgerEMS-specific coupling; eligible for Kyra.Core in Phase 3.
// Shared OpenAI-HTTP base; instantiated for 7+ providers.
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VentoyToolkitSetup.Wpf.Configuration;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using VentoyToolkitSetup.Wpf.Services.Kyra;
using VentoyToolkitSetup.Wpf.Services.KyraTools;

namespace VentoyToolkitSetup.Wpf.Services;

public sealed class OpenAiStyleCopilotProvider : ICopilotProvider
{
    private static readonly HttpClient HttpClient = new();
    private readonly string _id;
    private readonly string _displayName;
    private readonly CopilotProviderType _providerType;
    private readonly string _category;
    private readonly bool _isPaidProvider;
    private readonly string _defaultBaseUrl;
    private readonly string _defaultModelName;
    private readonly string _defaultApiKeyEnvironmentVariable;
    private readonly string _statusText;

    public OpenAiStyleCopilotProvider(
        string id,
        string displayName,
        CopilotProviderType providerType,
        string category,
        bool isPaidProvider,
        string defaultBaseUrl,
        string defaultModelName,
        string defaultApiKeyEnvironmentVariable,
        string statusText)
    {
        _id = id;
        _displayName = displayName;
        _providerType = providerType;
        _category = category;
        _isPaidProvider = isPaidProvider;
        _defaultBaseUrl = defaultBaseUrl;
        _defaultModelName = defaultModelName;
        _defaultApiKeyEnvironmentVariable = defaultApiKeyEnvironmentVariable;
        _statusText = statusText;
    }

    public string Id => _id;
    public string DisplayName => _displayName;
    public CopilotProviderType ProviderType => _providerType;
    public string Category => _category;
    public bool IsOnlineProvider => true;
    public bool IsPaidProvider => _isPaidProvider;
    public bool EnabledByDefault => false;
    public string DefaultBaseUrl => _defaultBaseUrl;
    public string DefaultModelName => _defaultModelName;
    public string DefaultApiKeyEnvironmentVariable => _defaultApiKeyEnvironmentVariable;
    public string StatusText => _statusText;

    public bool IsConfigured(CopilotProviderConfiguration configuration)
    {
        if (Id.Equals("cloudflare-workers-ai", StringComparison.OrdinalIgnoreCase) &&
            ProviderEnvironmentResolver.ResolveCloudflareAccountId().Source == KyraCredentialSource.None)
        {
            return false;
        }

        return KyraProviderUrlSafety.IsSafeBaseUrl(configuration.BaseUrl) &&
               !string.IsNullOrWhiteSpace(configuration.ModelName) &&
               (!string.IsNullOrWhiteSpace(KyraApiKeyStore.ResolveApiKey(Id, configuration)));
    }

    public bool CanHandle(CopilotProviderRequest request) => true;

    public async Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken)
    {
        var apiKey = KyraApiKeyStore.ResolveApiKey(Id, request.ProviderConfiguration);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new CopilotProviderResult
            {
                Succeeded = false,
                FailureReason = KyraProviderFailureReason.NotConfigured,
                UserMessage = $"{DisplayName} is not configured.",
                DiagnosticMessage = "API key environment variable is missing."
            };
        }

        if (Id.Equals("cloudflare-workers-ai", StringComparison.OrdinalIgnoreCase))
        {
            if (ProviderEnvironmentResolver.ResolveCloudflareAccountId().Source == KyraCredentialSource.None)
            {
                return new CopilotProviderResult
                {
                    Succeeded = false,
                    FailureReason = KyraProviderFailureReason.NotConfigured,
                    UserMessage = $"{DisplayName} requires CLOUDFLARE_ACCOUNT_ID.",
                    DiagnosticMessage = "Missing CLOUDFLARE_ACCOUNT_ID."
                };
            }
        }

        if (!KyraProviderUrlSafety.IsSafeBaseUrl(request.ProviderConfiguration.BaseUrl))
        {
            return new CopilotProviderResult
            {
                Succeeded = false,
                FailureReason = KyraProviderFailureReason.NotConfigured,
                UserMessage = $"{DisplayName} base URL is invalid or contains embedded credentials. Fix provider settings and try again.",
                DiagnosticMessage = "Unsafe provider base URL rejected."
            };
        }

        if (Id.Equals("github-models", StringComparison.OrdinalIgnoreCase))
        {
            return await GenerateGitHubModelsAsync(request, apiKey, cancellationToken).ConfigureAwait(false);
        }

        var baseUrl = request.ProviderConfiguration.BaseUrl.TrimEnd('/');
        var payload = new
        {
            model = request.ProviderConfiguration.ModelName,
            messages = new object[]
            {
                new { role = "system", content = PromptTemplates.GetSystemPrompt(request.Context.PromptMode) },
                new { role = "user", content = KyraPromptBuilder.BuildOnlinePrompt(request.Context, includeSystemContext: true) }
            },
            max_tokens = Math.Clamp(request.ProviderConfiguration.MaxOutputTokens, 128, 2048),
            temperature = 0.3
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await HttpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new CopilotProviderResult
            {
                Succeeded = false,
                IsTransientFailure = (int)response.StatusCode is 408 or 429 or >= 500,
                FailureReason = ClassifyFailureReason(response.StatusCode, body),
                UserMessage = $"{DisplayName} returned an error. Offline fallback is available.",
                DiagnosticMessage = $"HTTP {(int)response.StatusCode}"
            };
        }

        var text = ExtractChatCompletionText(body);
        return string.IsNullOrWhiteSpace(text)
            ? new CopilotProviderResult
            {
                Succeeded = false,
                FailureReason = KyraProviderFailureReason.Unknown,
                UserMessage = $"{DisplayName} returned an empty response. Offline fallback is available."
            }
            : new CopilotProviderResult
            {
                Succeeded = true,
                UsedOnlineData = true,
                UserMessage = text,
                DiagnosticMessage = $"{DisplayName} response."
            };
    }

    private async Task<CopilotProviderResult> GenerateGitHubModelsAsync(
        CopilotProviderRequest request,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var modelConfig = GitHubModelsProviderConfig.FromEnvironment();
        var selectedRoute = GitHubModelsRouteSelector.SelectRoute(request);
        var attempts = GitHubModelsRouteSelector.BuildAttemptPlan(modelConfig, selectedRoute);
        if (attempts.Count == 0)
        {
            return new CopilotProviderResult
            {
                Succeeded = false,
                FailureReason = KyraProviderFailureReason.NotConfigured,
                UserMessage = "GitHub Models has no usable model configured. Offline fallback is available.",
                DiagnosticMessage = GitHubModelsRouteSelector.BuildSafeDiagnostic(GitHubModelRoute.Fallback, GitHubModelsProviderConfig.BuiltInFallbackModel, true, modelConfig.ConfiguredModelsCount)
            };
        }

        CopilotProviderResult? lastResult = null;
        GitHubModelChoice? lastChoice = null;
        var lastIndex = 0;
        for (var i = 0; i < attempts.Count; i++)
        {
            var choice = attempts[i];
            var result = await SendGitHubModelsAttemptAsync(request, apiKey, choice.ModelId, cancellationToken).ConfigureAwait(false);
            var fallbackUsed = i > 0 || choice.Route != selectedRoute;
            var diagnostic = GitHubModelsRouteSelector.BuildSafeDiagnostic(choice.Route, choice.ModelId, fallbackUsed, modelConfig.ConfiguredModelsCount);

            if (result.Succeeded)
            {
                return new CopilotProviderResult
                {
                    Succeeded = true,
                    UsedOnlineData = result.UsedOnlineData,
                    UserMessage = result.UserMessage,
                    DiagnosticMessage = diagnostic
                };
            }

            lastResult = result;
            lastChoice = choice;
            lastIndex = i;
            if (!GitHubModelsRouteSelector.ShouldRetryWithNextModel(result))
            {
                break;
            }
        }

        var finalChoice = lastChoice ?? attempts[^1];
        var finalRoute = finalChoice.Route;
        var finalModel = finalChoice.ModelId;
        var finalDiagnostic = GitHubModelsRouteSelector.BuildSafeDiagnostic(finalRoute, finalModel, lastIndex > 0 || finalRoute != selectedRoute, modelConfig.ConfiguredModelsCount);
        return new CopilotProviderResult
        {
            Succeeded = false,
            IsTransientFailure = lastResult?.IsTransientFailure ?? false,
            FailureReason = lastResult?.FailureReason ?? KyraProviderFailureReason.Unknown,
            UserMessage = lastResult?.UserMessage ?? "GitHub Models returned an error. Offline fallback is available.",
            DiagnosticMessage = $"{finalDiagnostic}; all model attempts failed; last={(lastResult?.DiagnosticMessage ?? "unknown")}"
        };
    }

    private async Task<CopilotProviderResult> SendGitHubModelsAttemptAsync(
        CopilotProviderRequest request,
        string apiKey,
        string modelId,
        CancellationToken cancellationToken)
    {
        var baseUrl = request.ProviderConfiguration.BaseUrl.TrimEnd('/');
        var payload = new
        {
            model = modelId,
            messages = new object[]
            {
                new { role = "system", content = PromptTemplates.GetSystemPrompt(request.Context.PromptMode) },
                new { role = "user", content = KyraPromptBuilder.BuildOnlinePrompt(request.Context, includeSystemContext: true) }
            },
            max_tokens = Math.Clamp(request.ProviderConfiguration.MaxOutputTokens, 128, 2048),
            temperature = 0.3
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await HttpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new CopilotProviderResult
            {
                Succeeded = false,
                IsTransientFailure = (int)response.StatusCode is 408 or 429 or >= 500,
                FailureReason = ClassifyFailureReason(response.StatusCode, body),
                UserMessage = "GitHub Models returned an error. Offline fallback is available.",
                DiagnosticMessage = $"HTTP {(int)response.StatusCode}"
            };
        }

        var text = ExtractChatCompletionText(body);
        return string.IsNullOrWhiteSpace(text)
            ? new CopilotProviderResult
            {
                Succeeded = false,
                FailureReason = KyraProviderFailureReason.Unknown,
                UserMessage = "GitHub Models returned an empty response. Offline fallback is available.",
                DiagnosticMessage = "Empty response text."
            }
            : new CopilotProviderResult
            {
                Succeeded = true,
                UsedOnlineData = true,
                UserMessage = text,
                DiagnosticMessage = "GitHub Models response."
            };
    }

    private static KyraProviderFailureReason ClassifyFailureReason(System.Net.HttpStatusCode statusCode, string body)
    {
        return (int)statusCode switch
        {
            401 or 403 => KyraProviderFailureReason.AuthFailed,
            408 => KyraProviderFailureReason.Timeout,
            429 => KyraProviderFailureReason.RateLimited,
            >= 500 => KyraProviderFailureReason.ServiceUnavailable,
            _ when body.Contains("model", StringComparison.OrdinalIgnoreCase) && body.Contains("not", StringComparison.OrdinalIgnoreCase) => KyraProviderFailureReason.ModelUnavailable,
            _ => KyraProviderFailureReason.Unknown
        };
    }

    private static string ExtractChatCompletionText(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var content))
                {
                    return content.GetString() ?? string.Empty;
                }
            }
        }
        catch
        {
        }

        return string.Empty;
    }
}
