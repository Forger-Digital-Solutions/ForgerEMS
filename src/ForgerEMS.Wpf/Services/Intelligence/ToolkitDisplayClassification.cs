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

        if (s.Contains("COVERED_BY_MANAGED", StringComparison.Ordinal))
        {
            return "Covered / Suppressed";
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

        if (s.Contains("VERIFICATION_PENDING", StringComparison.Ordinal) ||
            s.Contains("PENDING", StringComparison.Ordinal))
        {
            return "Verification Pending";
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

    /// <summary>
    /// Catalog-aware classification tag, surfaced from manifest metadata.
    /// Returns one of the technician-facing chip labels ("Legacy / Lab Only", "Paid - vendor licence",
    /// "Manual ISO Required", "Community source", "Official source") or null when none apply.
    /// Falls through to <see cref="BuildNormalizedLabel(string,string,string)"/> when the catalog has no
    /// metadata for the entry — preserves behaviour for legacy reports.
    /// </summary>
    public static string? BuildCatalogStatusTag(
        string? legacyWarning,
        string? licenseNote,
        bool manualOnly,
        string? type,
        string? sourceTrust)
    {
        if (!string.IsNullOrWhiteSpace(legacyWarning))
        {
            return "Legacy / Lab Only";
        }

        if (!string.IsNullOrWhiteSpace(licenseNote) &&
            licenseNote.Contains("Paid", StringComparison.OrdinalIgnoreCase))
        {
            return "Paid - vendor licence";
        }

        var normalizedType = (type ?? string.Empty).Trim();
        if (manualOnly || string.Equals(normalizedType, "manualDownload", StringComparison.OrdinalIgnoreCase))
        {
            return "Manual ISO Required";
        }

        var normalizedTrust = (sourceTrust ?? string.Empty).Trim();
        if (string.Equals(normalizedTrust, "community", StringComparison.OrdinalIgnoreCase))
        {
            return "Community source";
        }

        if (string.Equals(normalizedTrust, "official", StringComparison.OrdinalIgnoreCase))
        {
            return "Official source";
        }

        return null;
    }
}
