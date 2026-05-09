#pragma warning disable CA1822 // DI-related code; instance methods called via interface references
// FORGEREMS_KYRA_ADAPTER: ForgerEMS-specific coupling; stays in ForgerEMS.KyraAdapter.
// Reads FORGEREMS_OLLAMA_* env vars; env-prefix must be abstracted for Kyra.Core.
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

public sealed class OllamaCopilotProvider : ICopilotProvider
{
    private static readonly HttpClient HttpClient = new();

    public string Id => "ollama-local";
    public string DisplayName => "Ollama Local Model";
    public CopilotProviderType ProviderType => CopilotProviderType.OllamaLocal;
    public string Category => "Offline/local AI";
    public bool IsOnlineProvider => false;
    public bool IsPaidProvider => false;
    public bool EnabledByDefault => false;
    public string DefaultBaseUrl => "http://localhost:11434";
    public string DefaultModelName => "llama3.2";
    public string DefaultApiKeyEnvironmentVariable => string.Empty;
    public string StatusText => "Local Ollama provider. Requires Ollama running on localhost.";

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
            using var ping = await HttpClient.GetAsync($"{baseUrl}/api/tags", cancellationToken).ConfigureAwait(false);
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
            stream = false,
            prompt = KyraPromptBuilder.BuildOnlinePrompt(request.Context, includeSystemContext: true)
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/generate")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        using var response = await HttpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new CopilotProviderResult
            {
                Succeeded = false,
                IsTransientFailure = true,
                UserMessage = "Ollama returned an error. Offline fallback is available.",
                DiagnosticMessage = $"HTTP {(int)response.StatusCode}"
            };
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var text = document.RootElement.TryGetProperty("response", out var responseText)
                ? responseText.GetString()
                : string.Empty;
            return string.IsNullOrWhiteSpace(text)
                ? new CopilotProviderResult
                {
                    Succeeded = false,
                    UserMessage = "Ollama returned an empty response. Offline fallback is available.",
                    DiagnosticMessage = "Empty response."
                }
                : new CopilotProviderResult
                {
                    Succeeded = true,
                    UsedOnlineData = false,
                    UserMessage = text,
                    DiagnosticMessage = "Ollama local response."
                };
        }
        catch (JsonException)
        {
            return new CopilotProviderResult
            {
                Succeeded = false,
                UserMessage = "Ollama returned an unreadable response. Offline fallback is available.",
                DiagnosticMessage = "Invalid JSON."
            };
        }
    }

    private static CopilotProviderResult NotReachable()
    {
        return new CopilotProviderResult
        {
            Succeeded = false,
            IsTransientFailure = true,
            UserMessage = "Ollama is not reachable at the configured endpoint. Offline fallback is available.",
            DiagnosticMessage = "Ollama not reachable."
        };
    }
}
