using System.Text.RegularExpressions;

namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>
/// Extra scrubbing for strings that may be sent to external API providers after CopilotRedactor.
/// Does not replace redactor — tightens patterns that should never leave the device.
/// </summary>
public static partial class KyraSystemContextSanitizer
{
    public static string SanitizeForExternalProviders(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var t = text;

        // Windows paths that might slip through
        t = MyPathRegex().Replace(t, "[path redacted]");
        t = UncPathRegex().Replace(t, "[path redacted]");

        // Likely product keys / OEM markers (simple pattern)
        t = ProductKeyLikeRegex().Replace(t, "[key redacted]");

        // Email addresses
        t = EmailRegex().Replace(t, "[email redacted]");

        // Long hex / base64-ish tokens (potential keys)
        t = LongTokenRegex().Replace(t, "[token redacted]");

        return t.Trim();
    }

    [GeneratedRegex(@"(?i)\b[a-z]:\\(?:[^\\/:*?""<>|\r\n]+\\)*[^\\/:*?""<>|\r\n]+", RegexOptions.Compiled)]
    private static partial Regex MyPathRegex();

    [GeneratedRegex(@"\\\\[^\s]+", RegexOptions.Compiled)]
    private static partial Regex UncPathRegex();

    [GeneratedRegex(@"\b([A-Z0-9]{5}-){4}[A-Z0-9]{5}\b", RegexOptions.Compiled)]
    private static partial Regex ProductKeyLikeRegex();

    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\b[0-9a-f]{32,}\b|\b[A-Za-z0-9+/]{40,}={0,2}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex LongTokenRegex();
}
