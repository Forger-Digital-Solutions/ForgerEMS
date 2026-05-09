#pragma warning disable CA1822 // DI-related code; instance methods called via interface references
// FORGEREMS_KYRA_ADAPTER: ForgerEMS-specific coupling; stays in ForgerEMS.KyraAdapter.
// Reads FORGEREMS_OPENAI_* env vars; env-prefix must be abstracted for Kyra.Core.
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

public sealed class OpenAICompatibleCopilotProvider : ICopilotProvider
{
    private static readonly HttpClient HttpClient = new();

    public string Id => "openai-compatible";
    public string DisplayName => "OpenAI-Compatible";
    public CopilotProviderType ProviderType => CopilotProviderType.OpenAICompatible;
    public string Category => "Online/local AI";
    public bool IsOnlineProvider => true;
    public bool IsPaidProvider => true;
    public bool EnabledByDefault => false;
    public string DefaultBaseUrl => string.IsNullOrWhiteSpace(ForgerEmsEnvironmentConfiguration.OpenAiBaseUrl)
        ? "https://api.openai.com/v1"
        : ForgerEmsEnvironmentConfiguration.OpenAiBaseUrl;
    public string DefaultModelName => string.IsNullOrWhiteSpace(ForgerEmsEnvironmentConfiguration.OpenAiModel)
        ? "gpt-4.1-mini"
        : ForgerEmsEnvironmentConfiguration.OpenAiModel;
    public string DefaultApiKeyEnvironmentVariable => "FORGEREMS_OPENAI_API_KEY";
    public string StatusText => "Configurable OpenAI-compatible provider. API key is read from environment variable only.";

    public bool IsConfigured(CopilotProviderConfiguration configuration)
    {
        return KyraProviderUrlSafety.IsSafeBaseUrl(configuration.BaseUrl) &&
               !string.IsNullOrWhiteSpace(configuration.ModelName) &&
               !string.IsNullOrWhiteSpace(KyraApiKeyStore.ResolveApiKey(Id, configuration));
    }

    public bool CanHandle(CopilotProviderRequest request) => true;

    public async Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken)
    {
        var apiKey = KyraApiKeyStore.ResolveApiKey(Id, request.ProviderConfiguration);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return NotConfigured("OpenAI-compatible API key environment variable is not set.");
        }

        if (!KyraProviderUrlSafety.IsSafeBaseUrl(request.ProviderConfiguration.BaseUrl))
        {
            return new CopilotProviderResult
            {
                Succeeded = false,
                FailureReason = KyraProviderFailureReason.NotConfigured,
                UserMessage = "OpenAI-compatible provider base URL is invalid or contains embedded credentials. Offline fallback is available.",
                DiagnosticMessage = "Unsafe provider base URL rejected."
            };
        }

        var baseUrl = request.ProviderConfiguration.BaseUrl.TrimEnd('/');
        var payload = new
        {
            model = request.ProviderConfiguration.ModelName,
            input = new object[]
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
            }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/responses");
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
                UserMessage = "OpenAI-compatible provider returned an error. Offline fallback is available.",
                DiagnosticMessage = $"HTTP {(int)response.StatusCode}"
            };
        }

        var text = TryExtractOpenAIResponseText(body);
        return string.IsNullOrWhiteSpace(text)
            ? new CopilotProviderResult
            {
                Succeeded = false,
                UserMessage = "OpenAI-compatible provider returned an empty response. Offline fallback is available.",
                DiagnosticMessage = "Empty response text."
            }
            : new CopilotProviderResult
            {
                Succeeded = true,
                UsedOnlineData = true,
                UserMessage = text,
                DiagnosticMessage = "OpenAI-compatible response."
            };
    }

    private static CopilotProviderResult NotConfigured(string detail)
    {
        return new CopilotProviderResult
        {
            Succeeded = false,
            UserMessage = "OpenAI-compatible provider is not configured. Offline fallback is available.",
            DiagnosticMessage = detail
        };
    }

    private static string TryExtractOpenAIResponseText(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("output_text", out var outputText))
            {
                return outputText.GetString() ?? string.Empty;
            }

            if (document.RootElement.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
            {
                var chunks = new List<string>();
                foreach (var item in output.EnumerateArray())
                {
                    if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    chunks.AddRange(content.EnumerateArray()
                        .Where(part => part.TryGetProperty("text", out _))
                        .Select(part => part.GetProperty("text").GetString())
                        .Where(text => !string.IsNullOrWhiteSpace(text))!);
                }

                return string.Join(Environment.NewLine, chunks);
            }
        }
        catch
        {
        }

        return string.Empty;
    }
}
