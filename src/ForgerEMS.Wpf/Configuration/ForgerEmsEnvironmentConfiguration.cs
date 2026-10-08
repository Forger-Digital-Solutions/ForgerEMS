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
    private static readonly string LegacyKeyPlaceholder = "sk-" + "REPLACE_ME";

    public static string GetString(string name, string defaultValue = "")
    {
        var v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(v) ? defaultValue : v.Trim();
    }

    public static string GetConfigString(string name, string defaultValue = "")
    {
        var v = GetString(name, defaultValue);
        return IsPlaceholderValue(v) ? string.Empty : v;
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

    private static bool IsPlaceholderValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var v = value.Trim();
        return v.Equals("REPLACE_ME", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("REPLACE_WITH_BETA_ACCESS_TOKEN", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("REPLACE_MODEL_NAME", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("local-model-name", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("model-name", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("changeme", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("TODO", StringComparison.OrdinalIgnoreCase) ||
               v.Equals(LegacyKeyPlaceholder, StringComparison.OrdinalIgnoreCase) ||
               v.StartsWith("REPLACE_", StringComparison.OrdinalIgnoreCase) ||
               v.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase) ||
               v.StartsWith("PASTE_", StringComparison.OrdinalIgnoreCase) ||
               v.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("sample", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("example", StringComparison.OrdinalIgnoreCase) ||
               v.StartsWith("sample-", StringComparison.OrdinalIgnoreCase) ||
               v.StartsWith("example-", StringComparison.OrdinalIgnoreCase) ||
               v.Contains("REPLACE_ME", StringComparison.OrdinalIgnoreCase) ||
               v.Contains("example.local", StringComparison.OrdinalIgnoreCase);
    }

    // Core
    public static string ForgerEmsEnv => GetString("FORGEREMS_ENV", "Production");
    public static string ReleaseChannel => GetString("FORGEREMS_RELEASE_CHANNEL", "stable");
    public static bool PortableMode => GetBool("FORGEREMS_PORTABLE_MODE", false);
    public static string LogLevel => GetString("FORGEREMS_LOG_LEVEL", "Info");
    public static bool VerboseLiveLogs => GetBool("FORGEREMS_VERBOSE_LIVE_LOGS", false);
    public static string SupportEmail => GetString("FORGEREMS_SUPPORT_EMAIL", "ForgerDigitalSolutions@outlook.com");

    // Updates / GitHub
    public static string GitHubOwner => GetString("FORGEREMS_GITHUB_OWNER", "Forger-Digital-Solutions");
    public static string GitHubRepo => GetString("FORGEREMS_GITHUB_REPO", "ForgerEMS");
    public static string UpdateChannel => GetString("FORGEREMS_UPDATE_CHANNEL", ReleaseChannel);
    public static bool UpdateIncludePrerelease => GetBool("FORGEREMS_UPDATE_INCLUDE_PRERELEASE", false);
    public static string UpdateUserAgent => GetString("FORGEREMS_UPDATE_USER_AGENT", "ForgerEMS");

    /// <summary>Optional PAT for GitHub Releases API (operator/dev only). Never commit; raises rate limits when set.</summary>
    public static string GitHubApiToken
    {
        get
        {
            var v = GetConfigString("FORGEREMS_GITHUB_TOKEN", "");
            return IsPlaceholderValue(v) ? string.Empty : v;
        }
    }

    public static int UpdateTimeoutSeconds => GetInt("FORGEREMS_UPDATE_TIMEOUT_SECONDS", 20, 5, 120);

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
