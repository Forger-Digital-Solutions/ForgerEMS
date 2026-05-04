using System;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public static class ToolkitDisplayClassification
{
    public static string BuildNormalizedLabel(string status, string type, string verification)
    {
        var s = status.ToUpperInvariant();
        var v = verification.ToUpperInvariant();
        var t = type.ToUpperInvariant();

        if (s.Contains("INSTALLED", StringComparison.Ordinal) || s.Contains("READY", StringComparison.Ordinal))
        {
            return "Managed Ready";
        }

        if (s.Contains("MISSING_REQUIRED", StringComparison.Ordinal) || s == "MISSING")
        {
            return "Managed Missing";
        }

        if (s.Contains("MANUAL", StringComparison.Ordinal) || t.Contains("MANUAL", StringComparison.Ordinal))
        {
            return "Manual Required";
        }

        if (s.Contains("HASH_FAILED", StringComparison.Ordinal) ||
            s.Contains("VERIFY", StringComparison.Ordinal) && v.Contains("FAIL", StringComparison.Ordinal))
        {
            return "Verification Issues";
        }

        if (s.Contains("UPDATE", StringComparison.Ordinal))
        {
            return "Managed Update Available";
        }

        if (s.Contains("PLACEHOLDER", StringComparison.Ordinal) || s.Contains("SKIPPED", StringComparison.Ordinal))
        {
            return "Skipped / Placeholder";
        }

        return "Other / Review";
    }
}
