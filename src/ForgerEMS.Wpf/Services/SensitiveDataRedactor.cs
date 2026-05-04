using System.Text.RegularExpressions;

namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>
/// Central redaction for clipboard/support-safe summaries. Does not replace full log hygiene;
/// use for user-facing copy/share paths in addition to <see cref="KyraSystemContextSanitizer"/>.
/// </summary>
public static partial class SensitiveDataRedactor
{
    /// <summary>Sanitize text before pasting into email, forums, or external support.</summary>
    public static string SanitizeForSupportShare(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var t = KyraSystemContextSanitizer.SanitizeForExternalProviders(text);

        t = SerialLikeRegex().Replace(t, "[id redacted]");
        t = PnpInstanceRegex().Replace(t, "[device id redacted]");

        return t.Trim();
    }

    [GeneratedRegex(@"\b(?:SERIAL|SERVICE\s*TAG|S\/N)[:\s]+[A-Z0-9][A-Z0-9\-_.]{4,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SerialLikeRegex();

    /// <summary>USB-style instance IDs often too specific for casual sharing.</summary>
    [GeneratedRegex(@"\\(?:USB|SWD|HID)#[^\\\s]{8,}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PnpInstanceRegex();
}
