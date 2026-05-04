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
    public static int KyraMaxContextTurns => GetInt("FORGEREMS_KYRA_MAX_CONTEXT_TURNS", 20, 1, 200);

    public static string OpenAiBaseUrl => GetString("FORGEREMS_OPENAI_BASE_URL", "");
    public static string OpenAiModel => GetString("FORGEREMS_OPENAI_MODEL", "");
    public static bool OpenAiApiKeyPresent => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGEREMS_OPENAI_API_KEY"));

    public static string LmStudioBaseUrl => GetString("FORGEREMS_LMSTUDIO_BASE_URL", "http://localhost:1234/v1");
    public static string LmStudioModel => GetString("FORGEREMS_LMSTUDIO_MODEL", "");

    public static string OllamaBaseUrl => GetString("FORGEREMS_OLLAMA_BASE_URL", "http://localhost:11434");
    public static string OllamaModel => GetString("FORGEREMS_OLLAMA_MODEL", "");

    public static bool AnthropicApiKeyPresent => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGEREMS_ANTHROPIC_API_KEY"));
    public static string AnthropicModel => GetString("FORGEREMS_ANTHROPIC_MODEL", "");
    public static bool GeminiApiKeyPresent => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGEREMS_GEMINI_API_KEY"));
    public static string GeminiModel => GetString("FORGEREMS_GEMINI_MODEL", "");

    public static string CustomProviderBaseUrl => GetString("FORGEREMS_CUSTOM_PROVIDER_BASE_URL", "");
    public static string CustomProviderModel => GetString("FORGEREMS_CUSTOM_PROVIDER_MODEL", "");
    public static bool CustomProviderApiKeyPresent =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGEREMS_CUSTOM_PROVIDER_API_KEY"));

    // Diagnostics export
    public static string DiagnosticsExportDir => GetString("FORGEREMS_DIAGNOSTICS_EXPORT_DIR", "");
    public static bool DiagnosticsRedactionStrict => GetBool("FORGEREMS_DIAGNOSTICS_REDACTION_STRICT", true);
    public static bool EnableDiagnosticBundle => GetBool("FORGEREMS_ENABLE_DIAGNOSTIC_BUNDLE", true);

    // Marketplace / valuation (stubs)
    public static bool MarketplaceEnabled => GetBool("FORGEREMS_MARKETPLACE_ENABLED", false);
    public static bool EbayEnabled => GetBool("FORGEREMS_EBAY_ENABLED", false);
    public static string ValuationMode => GetString("FORGEREMS_VALUATION_MODE", "offline");

    // Telemetry (default off)
    public static bool TelemetryEnabled => GetBool("FORGEREMS_TELEMETRY_ENABLED", false);
    public static bool CrashReportingEnabled => GetBool("FORGEREMS_CRASH_REPORTING_ENABLED", false);

    /// <summary>Optional license tier hint for local preview builds (no cloud activation).</summary>
    public static string LicenseTierRaw => GetString("FORGEREMS_LICENSE_TIER", "");
}
