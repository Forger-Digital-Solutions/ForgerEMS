#pragma warning disable CA1822 // DI-related code; instance methods called via interface references
// KYRA_CORE_CANDIDATE: No ForgerEMS-specific coupling; eligible for Kyra.Core in Phase 3.
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

public sealed class GeminiCopilotProvider : ICopilotProvider
{
    private static readonly HttpClient HttpClient = new();
    public string Id => "gemini-free";
    public string DisplayName => "Gemini (Free Tier)";
    public CopilotProviderType ProviderType => CopilotProviderType.GeminiApi;
    public string Category => "Free API pool";
    public bool IsOnlineProvider => true;
    public bool IsPaidProvider => false;
    public bool EnabledByDefault => false;
    public string DefaultBaseUrl => "https://generativelanguage.googleapis.com/v1beta";
    public string DefaultModelName => "gemini-1.5-flash";
    public string DefaultApiKeyEnvironmentVariable => "GEMINI_API_KEY";
    public string StatusText => "Google AI Studio/Gemini free-tier provider. Key is optional and BYOK.";

    public bool IsConfigured(CopilotProviderConfiguration configuration)
    {
        return !string.IsNullOrWhiteSpace(KyraApiKeyStore.ResolveApiKey(Id, configuration));
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
                UserMessage = "Gemini is not configured. Offline fallback is available."
            };
        }

        var model = string.IsNullOrWhiteSpace(request.ProviderConfiguration.ModelName) ? DefaultModelName : request.ProviderConfiguration.ModelName;
        var baseUrl = request.ProviderConfiguration.BaseUrl.TrimEnd('/');
        var prompt = KyraPromptBuilder.BuildOnlinePrompt(request.Context, includeSystemContext: true);
        var payload = new
        {
            contents = new object[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = $"{PromptTemplates.GetSystemPrompt(request.Context.PromptMode)}\n\n{prompt}" } }
                }
            }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/models/{model}:generateContent?key={Uri.EscapeDataString(apiKey)}");
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await HttpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new CopilotProviderResult
            {
                Succeeded = false,
                IsTransientFailure = (int)response.StatusCode is 408 or 429 or >= 500,
                FailureReason = (int)response.StatusCode switch
                {
                    401 or 403 => KyraProviderFailureReason.AuthFailed,
                    429 => KyraProviderFailureReason.RateLimited,
                    >= 500 => KyraProviderFailureReason.ServiceUnavailable,
                    _ => KyraProviderFailureReason.Unknown
                },
                UserMessage = "Gemini provider failed. Offline fallback is available.",
                DiagnosticMessage = $"HTTP {(int)response.StatusCode}"
            };
        }

        var text = ExtractGeminiText(body);
        return string.IsNullOrWhiteSpace(text)
            ? new CopilotProviderResult { Succeeded = false, FailureReason = KyraProviderFailureReason.Unknown, UserMessage = "Gemini returned no text." }
            : new CopilotProviderResult { Succeeded = true, UsedOnlineData = true, UserMessage = text };
    }

    private static string ExtractGeminiText(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            foreach (var candidate in candidates.EnumerateArray())
            {
                if (!candidate.TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts))
                {
                    continue;
                }

                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var text))
                    {
                        return text.GetString() ?? string.Empty;
                    }
                }
            }
        }
        catch
        {
        }

        return string.Empty;
    }
}
