using System.Text.RegularExpressions;
using VentoyToolkitSetup.Wpf.Configuration;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Extra-tight sanitization for gateway research payloads (paths, PII patterns).</summary>
public static class KyraGatewayResearchSanitizer
{
    public static string SanitizePrompt(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return string.Empty;
        }

        var s = CopilotRedactor.Redact(prompt.Trim(), enabled: true);
        s = Regex.Replace(s, @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b", "[email redacted]", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\b(?:(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\.){3}(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\b", "[ip redacted]");
        s = Regex.Replace(s, @"\beyJ[a-zA-Z0-9_-]+\.[a-zA-Z0-9_-]+\.[a-zA-Z0-9_-]+\b", "[jwt redacted]");
        s = Regex.Replace(s, @"\bsk-[A-Za-z0-9]{10,}\b", "[api key pattern redacted]", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\b[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}\b", "[product key pattern redacted]", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\b(?:[0-9A-Z]{4}-){3,7}[0-9A-Z]{4,}\b", "[serial-like redacted]", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"[A-Za-z]:\\[^\n]{0,240}", "[path redacted]", RegexOptions.IgnoreCase);
        return s.Length > 8000 ? s[..8000] + "\n[trimmed]" : s;
    }

    public static string? SanitizeOptionalLabel(string? value, int maxLen = 120)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var s = CopilotRedactor.Redact(value.Trim(), enabled: true);
        return s.Length > maxLen ? s[..maxLen] : s;
    }

    public static string ResolvePrivacyMode(CopilotSettings settings)
    {
        var share =
            settings.AllowOnlineSystemContextSharing &&
            ForgerEmsEnvironmentConfiguration.KyraGatewayShareSystemContext;
        return share ? "sanitized-gateway" : "local-only";
    }
}
