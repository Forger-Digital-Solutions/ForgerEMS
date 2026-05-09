// FORGEREMS_KYRA_ADAPTER: ForgerEMS-specific coupling; stays in ForgerEMS.KyraAdapter.
// All FORGEREMS_* env-var bindings; Kyra.Core would use generic config abstractions.
using System;
using System.Globalization;

namespace VentoyToolkitSetup.Wpf.Configuration;

/// <summary>
/// Environment-variable configuration (no secrets stored). Values are read on each access so
/// operator changes to user/session env vars can be picked up without restart where safe.
/// See docs/ENVIRONMENT.md for the full list.
/// </summary>
public static class ForgerEmsEnvironmentConfiguration
{
    public static string GetString(string name, string defaultValue = "")
    {
        var v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(v) ? defaultValue : v.Trim();
    }

    public static string GetConfigString(string name, string defaultValue = "")
    {
        var v = GetString(name, defaultValue);
        return VentoyToolkitSetup.Wpf.Services.KyraProviderConfigResolver.IsPlaceholderSecretOrValue(v) ? string.Empty : v;
    }

    public static bool GetBool(string name, bool defaultValue)
    {
        var v = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(v))
        {
            return defaultValue;
        }

        if (bool.TryParse(v, out var b))
        {
            return b;
        }

        if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            return n != 0;
        }

        return defaultValue;
    }

    public static int GetInt(string name, int defaultValue, int min = int.MinValue, int max = int.MaxValue)
    {
        var v = Environment.GetEnvironmentVariable(name);
        if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            return defaultValue;
        }

        return Math.Clamp(n, min, max);
    }

    private static bool GetConfigBool(string name, bool defaultValue)
    {
        var v = VentoyToolkitSetup.Wpf.Services.KyraProviderConfigResolver.ResolveNamedValue(name, "");
        if (string.IsNullOrWhiteSpace(v))
        {
            return defaultValue;
        }

        if (bool.TryParse(v, out var b))
        {
            return b;
        }

        return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n != 0
            : defaultValue;
    }

    private static int GetConfigInt(string name, int defaultValue, int min = int.MinValue, int max = int.MaxValue)
    {
        var v = VentoyToolkitSetup.Wpf.Services.KyraProviderConfigResolver.ResolveNamedValue(name, "");
        return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? Math.Clamp(n, min, max)
            : defaultValue;
    }

    // Core
    public static string ForgerEmsEnv => GetString("FORGEREMS_ENV", "Production");
    public static string ReleaseChannel => GetString("FORGEREMS_RELEASE_CHANNEL", "preview");
    public static bool PortableMode => GetBool("FORGEREMS_PORTABLE_MODE", false);
    public static string LogLevel => GetString("FORGEREMS_LOG_LEVEL", "Info");
    public static bool VerboseLiveLogs => GetBool("FORGEREMS_VERBOSE_LIVE_LOGS", false);
    public static string SupportEmail => GetString("FORGEREMS_SUPPORT_EMAIL", "ForgerDigitalSolutions@outlook.com");

    // Updates / GitHub
    public static string GitHubOwner => GetString("FORGEREMS_GITHUB_OWNER", "Forger-Digital-Solutions");
    public static string GitHubRepo => GetString("FORGEREMS_GITHUB_REPO", "ForgerEMS");
    public static string UpdateChannel => GetString("FORGEREMS_UPDATE_CHANNEL", ReleaseChannel);
    public static bool UpdateIncludePrerelease => GetBool("FORGEREMS_UPDATE_INCLUDE_PRERELEASE", true);
    public static string UpdateUserAgent => GetString("FORGEREMS_UPDATE_USER_AGENT", "ForgerEMS");
    public static int UpdateTimeoutSeconds => GetInt("FORGEREMS_UPDATE_TIMEOUT_SECONDS", 20, 5, 120);

    // Kyra
    public static string KyraMode => GetString("FORGEREMS_KYRA_MODE", "hybrid");
    public static string KyraProvider => GetString("FORGEREMS_KYRA_PROVIDER", "offline");
    public static bool KyraOnlineEnabled => GetBool("FORGEREMS_KYRA_ONLINE_ENABLED", false);
    public static bool KyraShareSystemContext => GetBool("FORGEREMS_KYRA_SHARE_SYSTEM_CONTEXT", false);
    public static bool KyraRequireLocalFacts => GetBool("FORGEREMS_KYRA_REQUIRE_LOCAL_FACTS", true);
    public static bool KyraApiFirst => GetBool("FORGEREMS_KYRA_API_FIRST", true);
    public static string KyraProviderPriority => GetString(
        "FORGEREMS_KYRA_PROVIDER_PRIORITY",
        "forgerems-gateway,openai-compatible,custom,openrouter,groq,gemini,anthropic,mistral,cerebras,github-models,cloudflare,lmstudio,ollama,offline");
    public static int KyraProviderTimeoutSeconds => GetInt("FORGEREMS_KYRA_PROVIDER_TIMEOUT_SECONDS", 60, 3, 120);
    public static bool KyraConsensusMode => GetBool("FORGEREMS_KYRA_CONSENSUS_MODE", false);
    public static string KyraMemoryMode => GetString("FORGEREMS_KYRA_MEMORY_MODE", "session");
    public static bool KyraPersistMemory => GetBool("FORGEREMS_KYRA_PERSIST_MEMORY", false);
    public static int KyraMaxContextTurns => GetInt("FORGEREMS_KYRA_MAX_CONTEXT_TURNS", 100, 1, 200);
    public static int KyraContextMaxChars => GetInt("FORGEREMS_KYRA_CONTEXT_MAX_CHARS", 12000, 1000, 50000);
    public static string KyraPersonality => GetString("FORGEREMS_KYRA_PERSONALITY", "bubbly-tech");

    public static string KyraGatewayUrl =>
        VentoyToolkitSetup.Wpf.Services.KyraProviderConfigResolver.ResolveNamedValue(
            VentoyToolkitSetup.Wpf.Services.CopilotProviderEnvironmentVariableNames.KyraGatewayUrl,
            "");

    public static bool KyraGatewayBetaTokenPresent =>
        VentoyToolkitSetup.Wpf.Services.KyraProviderConfigResolver.ResolveNamedCredential(
            VentoyToolkitSetup.Wpf.Services.CopilotProviderEnvironmentVariableNames.KyraGatewayBetaToken) is not VentoyToolkitSetup.Wpf.Services.KyraProviderCredentialState.Missing and not VentoyToolkitSetup.Wpf.Services.KyraProviderCredentialState.Placeholder;

    public static int KyraGatewayTimeoutSeconds => GetConfigInt("FORGEREMS_KYRA_GATEWAY_TIMEOUT_SECONDS", 60, 3, 120);

    public static int KyraGatewayDailyRequestLimit => GetConfigInt("FORGEREMS_KYRA_GATEWAY_DAILY_REQUEST_LIMIT", 0, 0, 10000);

    public static bool KyraGatewayShareSystemContext => GetConfigBool("FORGEREMS_KYRA_GATEWAY_SHARE_SYSTEM_CONTEXT", false);

    public static bool KyraGatewayConfigured =>
        !string.IsNullOrWhiteSpace(KyraGatewayUrl) && KyraGatewayBetaTokenPresent;

    /// <summary>When false, the app does not call the ForgerEMS Kyra Gateway (chat or research).</summary>
    public static bool KyraGatewayEnabled => GetConfigBool("FORGEREMS_KYRA_GATEWAY_ENABLED", true);

    /// <summary>When true, current/realtime-style prompts may use POST /v1/kyra/research when gateway is configured.</summary>
    public static bool KyraResearchEnabled => GetConfigBool("FORGEREMS_KYRA_RESEARCH_ENABLED", false);

    /// <summary>When true, the user must consent in Kyra Advanced before realtime gateway research runs.</summary>
    public static bool KyraGatewayRequireConsent => GetConfigBool("FORGEREMS_KYRA_GATEWAY_REQUIRE_CONSENT", false);

    public static string OpenAiBaseUrl => GetConfigString("FORGEREMS_OPENAI_BASE_URL", "");
    public static string OpenAiModel => GetConfigString("FORGEREMS_OPENAI_MODEL", "");
    public static bool OpenAiApiKeyPresent =>
        VentoyToolkitSetup.Wpf.Services.KyraProviderConfigResolver.ResolveNamedCredential("FORGEREMS_OPENAI_API_KEY") is not VentoyToolkitSetup.Wpf.Services.KyraProviderCredentialState.Missing and not VentoyToolkitSetup.Wpf.Services.KyraProviderCredentialState.Placeholder ||
        VentoyToolkitSetup.Wpf.Services.KyraProviderConfigResolver.ResolveNamedCredential("OPENAI_API_KEY") is not VentoyToolkitSetup.Wpf.Services.KyraProviderCredentialState.Missing and not VentoyToolkitSetup.Wpf.Services.KyraProviderCredentialState.Placeholder;

    public static string LmStudioBaseUrl => GetConfigString("FORGEREMS_LMSTUDIO_BASE_URL", "http://localhost:1234/v1");
    public static string LmStudioModel => GetConfigString("FORGEREMS_LMSTUDIO_MODEL", "");

    public static string OllamaBaseUrl => GetConfigString("FORGEREMS_OLLAMA_BASE_URL", "http://localhost:11434");
    public static string OllamaModel => GetConfigString("FORGEREMS_OLLAMA_MODEL", "");

    public static bool AnthropicApiKeyPresent =>
        VentoyToolkitSetup.Wpf.Services.KyraProviderConfigResolver.ResolveNamedCredential("FORGEREMS_ANTHROPIC_API_KEY") is not VentoyToolkitSetup.Wpf.Services.KyraProviderCredentialState.Missing and not VentoyToolkitSetup.Wpf.Services.KyraProviderCredentialState.Placeholder ||
        VentoyToolkitSetup.Wpf.Services.KyraProviderConfigResolver.ResolveNamedCredential("ANTHROPIC_API_KEY") is not VentoyToolkitSetup.Wpf.Services.KyraProviderCredentialState.Missing and not VentoyToolkitSetup.Wpf.Services.KyraProviderCredentialState.Placeholder;
    public static string AnthropicModel => GetConfigString("FORGEREMS_ANTHROPIC_MODEL", "");
    public static bool GeminiApiKeyPresent =>
        VentoyToolkitSetup.Wpf.Services.KyraProviderConfigResolver.ResolveNamedCredential("FORGEREMS_GEMINI_API_KEY") is not VentoyToolkitSetup.Wpf.Services.KyraProviderCredentialState.Missing and not VentoyToolkitSetup.Wpf.Services.KyraProviderCredentialState.Placeholder ||
        VentoyToolkitSetup.Wpf.Services.KyraProviderConfigResolver.ResolveNamedCredential("GEMINI_API_KEY") is not VentoyToolkitSetup.Wpf.Services.KyraProviderCredentialState.Missing and not VentoyToolkitSetup.Wpf.Services.KyraProviderCredentialState.Placeholder;
    public static string GeminiModel => GetConfigString("FORGEREMS_GEMINI_MODEL", "");

    public static string GitHubModelsDefaultModel =>
        VentoyToolkitSetup.Wpf.Services.KyraProviderConfigResolver.ResolveNamedValue(
            VentoyToolkitSetup.Wpf.Services.CopilotProviderEnvironmentVariableNames.GitHubModelsDefaultModel,
            "");

    public static string GitHubModelsFastModel =>
        VentoyToolkitSetup.Wpf.Services.KyraProviderConfigResolver.ResolveNamedValue(
            VentoyToolkitSetup.Wpf.Services.CopilotProviderEnvironmentVariableNames.GitHubModelsFastModel,
            "");

    public static string GitHubModelsAltModel =>
        VentoyToolkitSetup.Wpf.Services.KyraProviderConfigResolver.ResolveNamedValue(
            VentoyToolkitSetup.Wpf.Services.CopilotProviderEnvironmentVariableNames.GitHubModelsAltModel,
            "");

    public static string GitHubModelsPrimaryModel =>
        VentoyToolkitSetup.Wpf.Services.GitHubModelsProviderConfig.FromEnvironment().PrimaryModel;

    public static string CustomProviderBaseUrl => GetConfigString("FORGEREMS_CUSTOM_PROVIDER_BASE_URL", "");
    public static string CustomProviderModel => GetConfigString("FORGEREMS_CUSTOM_PROVIDER_MODEL", "");
    public static bool CustomProviderApiKeyPresent =>
        VentoyToolkitSetup.Wpf.Services.KyraProviderConfigResolver.ResolveNamedCredential("FORGEREMS_CUSTOM_PROVIDER_API_KEY") is not VentoyToolkitSetup.Wpf.Services.KyraProviderCredentialState.Missing and not VentoyToolkitSetup.Wpf.Services.KyraProviderCredentialState.Placeholder;

    public static string WeatherProvider => GetString("FORGEREMS_WEATHER_PROVIDER", "");
    public static bool WeatherApiKeyPresent => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGEREMS_WEATHER_API_KEY"));
    public static string WeatherDefaultLocation => GetString("FORGEREMS_WEATHER_DEFAULT_LOCATION", "");
    public static string NewsProvider => GetString("FORGEREMS_NEWS_PROVIDER", "");
    public static bool NewsApiKeyPresent => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGEREMS_NEWS_API_KEY"));
    public static string NewsRegion => GetString("FORGEREMS_NEWS_REGION", "");
    public static string FinanceProvider => GetString("FORGEREMS_FINANCE_PROVIDER", "");
    public static bool FinanceApiKeyPresent => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGEREMS_FINANCE_API_KEY"));
    public static string FinanceRegion => GetString("FORGEREMS_FINANCE_REGION", "");
    public static string CryptoProvider => GetString("FORGEREMS_CRYPTO_PROVIDER", "");
    public static bool CryptoApiKeyPresent => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGEREMS_CRYPTO_API_KEY"));
    public static string StatsProvider => GetString("FORGEREMS_STATS_PROVIDER", "");
    public static bool StatsApiKeyPresent => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGEREMS_STATS_API_KEY"));

    // Diagnostics export
    public static string DiagnosticsExportDir => GetString("FORGEREMS_DIAGNOSTICS_EXPORT_DIR", "");
    public static bool DiagnosticsRedactionStrict => GetBool("FORGEREMS_DIAGNOSTICS_REDACTION_STRICT", true);
    public static bool EnableDiagnosticBundle => GetBool("FORGEREMS_ENABLE_DIAGNOSTIC_BUNDLE", true);

    // Marketplace / valuation (stubs)
    public static bool MarketplaceEnabled => GetBool("FORGEREMS_MARKETPLACE_ENABLED", false);
    public static bool EbayEnabled => GetBool("FORGEREMS_EBAY_ENABLED", false);
    public static string ValuationMode => GetString("FORGEREMS_VALUATION_MODE", "offline");

    // System Intelligence sensors
    public static string DeepSensorMode => DeepSensorModeResolver.Resolve().Mode;

    public static DeepSensorModeResolution DeepSensorModeResolution => DeepSensorModeResolver.Resolve();

    // Telemetry (default off)
    public static bool TelemetryEnabled => GetBool("FORGEREMS_TELEMETRY_ENABLED", false);
    public static bool CrashReportingEnabled => GetBool("FORGEREMS_CRASH_REPORTING_ENABLED", false);

    /// <summary>Optional license tier hint for local preview builds (no cloud activation).</summary>
    public static string LicenseTierRaw => GetString("FORGEREMS_LICENSE_TIER", "");
}
