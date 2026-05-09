#pragma warning disable CA1822 // DI-related code; instance methods called via interface references
// FORGEREMS_KYRA_ADAPTER: ForgerEMS-specific coupling; stays in ForgerEMS.KyraAdapter.
// Reads FORGEREMS_LMSTUDIO_* env vars; env-prefix must be abstracted for Kyra.Core.
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

public sealed class LmStudioCopilotProvider : ICopilotProvider
{
    private static readonly HttpClient HttpClient = new();

    public string Id => "lm-studio-local";
    public string DisplayName => "LM Studio Local Model";
    public CopilotProviderType ProviderType => CopilotProviderType.LmStudioLocal;
    public string Category => "Offline/local AI";
    public bool IsOnlineProvider => false;
    public bool IsPaidProvider => false;
    public bool EnabledByDefault => false;
    public string DefaultBaseUrl => "http://localhost:1234/v1";
    public string DefaultModelName => "local-model";
    public string DefaultApiKeyEnvironmentVariable => string.Empty;
    public string StatusText => "Local LM Studio provider. Requires the local server running on localhost.";

    public bool IsConfigured(CopilotProviderConfiguration configuration)
    {
        return !string.IsNullOrWhiteSpace(configuration.BaseUrl) && !string.IsNullOrWhiteSpace(configuration.ModelName);
    }

    public bool CanHandle(CopilotProviderRequest request) => true;

    public async Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken)
    {
        var baseUrl = request.ProviderConfiguration.BaseUrl.TrimEnd('/');
        try
        {
            using var ping = await HttpClient.GetAsync($"{baseUrl}/models", cancellationToken).ConfigureAwait(false);
            if (!ping.IsSuccessStatusCode)
            {
                return NotReachable();
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return NotReachable();
        }

        var payload = new
        {
            model = request.ProviderConfiguration.ModelName,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = PromptTemplates.GetSystemPrompt(request.Context.PromptMode)
                },
                new
                {
                    role = "user",
                    content = KyraPromptBuilder.BuildOnlinePrompt(request.Context, includeSystemContext: true)
                }
            },
            temperature = 0.3
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions");
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await HttpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new CopilotProviderResult
            {
                Succeeded = false,
                IsTransientFailure = (int)response.StatusCode is 408 or 429 or >= 500,
                UserMessage = "LM Studio returned an error. Offline fallback is available.",
                DiagnosticMessage = $"LM Studio HTTP {(int)response.StatusCode}"
            };
        }

        var text = TryExtractChatCompletionText(body);
        return string.IsNullOrWhiteSpace(text)
            ? new CopilotProviderResult
            {
                Succeeded = false,
                UserMessage = "LM Studio returned an empty response. Offline fallback is available.",
                DiagnosticMessage = "Empty LM Studio response text."
            }
            : new CopilotProviderResult
            {
                Succeeded = true,
                UsedOnlineData = false,
                UserMessage = text,
                DiagnosticMessage = "LM Studio local response."
            };
    }

    private static CopilotProviderResult NotReachable()
    {
        return new CopilotProviderResult
        {
            Succeeded = false,
            IsTransientFailure = true,
            UserMessage = "LM Studio is not reachable at the configured endpoint. Offline fallback is available.",
            DiagnosticMessage = "LM Studio not reachable."
        };
    }

    private static string TryExtractChatCompletionText(string body)
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
