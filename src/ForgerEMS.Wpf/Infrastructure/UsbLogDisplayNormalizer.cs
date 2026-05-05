using System;

namespace VentoyToolkitSetup.Wpf.Infrastructure;

/// <summary>User-facing normalization for log text (full + session logs). Does not remove technical detail beyond friendly provider labels.</summary>
public static class UsbLogDisplayNormalizer
{
    public static string NormalizeHashProviderLabels(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var normalized = text;

        normalized = ReplaceOrdinalIgnoreCase(normalized, "DotNetFallback", "Built-in .NET (large-file safe)");
        normalized = ReplaceOrdinalIgnoreCase(normalized, "Get-FileHashFailed", "Windows Get-FileHash (unavailable; used built-in fallback)");

        return normalized;
    }

    private static string ReplaceOrdinalIgnoreCase(string haystack, string needle, string replacement)
    {
        var idx = haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        while (idx >= 0)
        {
            haystack = string.Concat(haystack.AsSpan(0, idx), replacement, haystack.AsSpan(idx + needle.Length));
            idx = haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        }

        return haystack;
    }
}
