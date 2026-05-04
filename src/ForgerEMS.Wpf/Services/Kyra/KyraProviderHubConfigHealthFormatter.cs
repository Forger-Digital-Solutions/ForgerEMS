using System.Text;
using VentoyToolkitSetup.Wpf.Configuration;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Non-secret provider hub summary for Kyra Advanced / diagnostics (URLs only; keys as present/missing).</summary>
public static class KyraProviderHubConfigHealthFormatter
{
    public static string BuildSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Kyra provider hub (env hints — API keys never shown)");
        sb.AppendLine($"FORGEREMS_KYRA_MODE: {ForgerEmsEnvironmentConfiguration.KyraMode}");
        sb.AppendLine($"FORGEREMS_KYRA_PROVIDER: {ForgerEmsEnvironmentConfiguration.KyraProvider}");
        sb.AppendLine($"Online enabled (env): {ForgerEmsEnvironmentConfiguration.KyraOnlineEnabled}");
        sb.AppendLine($"Offline/local rules: always available");
        sb.AppendLine();
        AppendOpenAiCompatible(sb);
        AppendLmStudio(sb);
        AppendOllama(sb);
        sb.AppendLine("Anthropic (stub env): " + (ForgerEmsEnvironmentConfiguration.AnthropicApiKeyPresent ? "API key present (hidden)" : "Missing API key") +
                      (string.IsNullOrWhiteSpace(ForgerEmsEnvironmentConfiguration.AnthropicModel)
                          ? ""
                          : $" | Model: {ForgerEmsEnvironmentConfiguration.AnthropicModel}"));
        sb.AppendLine("Gemini (stub env): " + (ForgerEmsEnvironmentConfiguration.GeminiApiKeyPresent ? "API key present (hidden)" : "Missing API key") +
                      (string.IsNullOrWhiteSpace(ForgerEmsEnvironmentConfiguration.GeminiModel)
                          ? ""
                          : $" | Model: {ForgerEmsEnvironmentConfiguration.GeminiModel}"));
        sb.AppendLine("Custom provider: " +
                      (string.IsNullOrWhiteSpace(ForgerEmsEnvironmentConfiguration.CustomProviderBaseUrl)
                          ? "Base URL not set"
                          : $"Base URL set ({RedactUrl(ForgerEmsEnvironmentConfiguration.CustomProviderBaseUrl)})") +
                      (ForgerEmsEnvironmentConfiguration.CustomProviderApiKeyPresent ? " | API key present (hidden)" : " | API key missing"));
        return sb.ToString().TrimEnd();
    }

    private static void AppendOpenAiCompatible(StringBuilder sb)
    {
        var url = ForgerEmsEnvironmentConfiguration.OpenAiBaseUrl;
        var model = ForgerEmsEnvironmentConfiguration.OpenAiModel;
        var key = ForgerEmsEnvironmentConfiguration.OpenAiApiKeyPresent;
        sb.AppendLine("OpenAI-compatible: " +
                      (string.IsNullOrWhiteSpace(url) ? "Base URL not set" : $"Base URL {RedactUrl(url)}") +
                      (string.IsNullOrWhiteSpace(model) ? "" : $" | Model {model}") +
                      (key ? " | API key present (hidden)" : " | API key missing"));
    }

    private static void AppendLmStudio(StringBuilder sb)
    {
        sb.AppendLine("LM Studio: " + RedactUrl(ForgerEmsEnvironmentConfiguration.LmStudioBaseUrl) +
                      (string.IsNullOrWhiteSpace(ForgerEmsEnvironmentConfiguration.LmStudioModel)
                          ? ""
                          : $" | Model {ForgerEmsEnvironmentConfiguration.LmStudioModel}"));
    }

    private static void AppendOllama(StringBuilder sb)
    {
        sb.AppendLine("Ollama: " + RedactUrl(ForgerEmsEnvironmentConfiguration.OllamaBaseUrl) +
                      (string.IsNullOrWhiteSpace(ForgerEmsEnvironmentConfiguration.OllamaModel)
                          ? ""
                          : $" | Model {ForgerEmsEnvironmentConfiguration.OllamaModel}"));
    }

    private static string RedactUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "(empty)";
        }

        try
        {
            var u = new Uri(url, UriKind.Absolute);
            return $"{u.Scheme}://{u.Host}{(u.IsDefaultPort ? "" : ":" + u.Port)}{u.AbsolutePath}";
        }
        catch
        {
            return "(unparseable URL)";
        }
    }
}
