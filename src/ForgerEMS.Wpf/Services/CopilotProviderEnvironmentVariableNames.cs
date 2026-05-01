namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>Canonical environment variable names the app reads for optional online providers (never ships keys).</summary>
public static class CopilotProviderEnvironmentVariableNames
{
    public const string Gemini = "GEMINI_API_KEY";
    public const string Groq = "GROQ_API_KEY";
    public const string OpenRouter = "OPENROUTER_API_KEY";
    public const string Cerebras = "CEREBRAS_API_KEY";
    public const string Mistral = "MISTRAL_API_KEY";
    public const string GitHubModels = "GITHUB_MODELS_TOKEN";
    public const string CloudflareWorkersAi = "CLOUDFLARE_API_KEY";

    public const string CloudflareAccountId = "CLOUDFLARE_ACCOUNT_ID";
    public const string OpenAi = "OPENAI_API_KEY";
    public const string Anthropic = "ANTHROPIC_API_KEY";

    public const string UxHowToConfigure =
        "How to configure: enable a provider below, then either paste a Session API key (memory only, not written to settings JSON) or set the listed environment variable. Use Refresh Provider Status after changing user/machine environment variables so Kyra re-reads them without restarting. Session keys override environment variables. " +
        "Local Kyra (Offline Local) needs no key. Online providers are optional.\n\n" +
        "Supported environment variable names (when using env vars instead of session keys):\n" +
        $"- Google Gemini: {Gemini}\n" +
        $"- Groq: {Groq}\n" +
        $"- OpenRouter: {OpenRouter}\n" +
        $"- Cerebras: {Cerebras}\n" +
        $"- Mistral: {Mistral}\n" +
        $"- GitHub Models: {GitHubModels}\n" +
        $"- Cloudflare Workers AI: {CloudflareWorkersAi} and {CloudflareAccountId}\n" +
        $"- OpenAI / OpenAI-compatible: {OpenAi}\n" +
        $"- Anthropic (BYOK path if enabled): {Anthropic}\n\n" +
        BetaSupportInfo.DoNotEmailSecretsWarning;
}
