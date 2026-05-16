namespace VentoyToolkitSetup.Wpf.Models;

public sealed class ToolkitHealthItemView
{
    public string Tool { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public string ExpectedPath { get; init; } = string.Empty;

    public string ResolvedExpectedPath { get; init; } = string.Empty;

    public string ExpectedFoundPath { get; init; } = string.Empty;

    public string MatchedPath { get; init; } = string.Empty;

    public bool Exists { get; init; }

    public long SizeBytes { get; init; }

    public string Url { get; init; } = string.Empty;

    public string Purpose { get; init; } = string.Empty;

    public string OfficialUrl { get; init; } = string.Empty;

    public string LicenseRedistributionNote { get; init; } = string.Empty;

    public string DownloadStatus { get; init; } = string.Empty;

    public string ChecksumStatus { get; init; } = string.Empty;

    public string DistributionModel { get; init; } = string.Empty;

    public string BetaSafetyRating { get; init; } = string.Empty;

    public string ClassificationReason { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string Verification { get; init; } = string.Empty;

    public string Recommendation { get; init; } = string.Empty;

    public string NormalizedCategoryLabel { get; init; } = string.Empty;

    public string TypeDisplay => Type.Trim().ToUpperInvariant() switch
    {
        "MANUALDOWNLOAD" => "Manual",
        "MANUAL" => "Manual",
        _ => "Managed"
    };

    /// <summary>Grid/UI label — manual-required items are never shown as generic “missing”.</summary>
    public string StatusDisplayUi => Status.Trim().ToUpperInvariant() switch
    {
        "MISSING_REQUIRED" => "Missing required file",
        "MISSING" => "Missing required file",
        "INSTALLED" => "Installed",
        "UPDATE_AVAILABLE" => "Update available",
        "HASH_FAILED" => "Verification issue",
        "VERIFICATION_PENDING" => "Present / verification pending",
        "COVERED_BY_MANAGED" => "Covered by managed download",
        "MANUAL_REQUIRED" => string.IsNullOrWhiteSpace(MatchedPath)
            ? "Manual shortcut missing"
            : "Manual shortcut present",
        "PLACEHOLDER" => "Placeholder",
        "SKIPPED" => "Skipped",
        _ => Status
    };

    public string LocationDisplay
    {
        get
        {
            var compactExpected = CompactPath(ExpectedPath);
            if (Exists)
            {
                var size = SizeBytes > 0 ? $", {FormatSize(SizeBytes)}" : string.Empty;
                return $"Present{size} | {compactExpected}";
            }

            if (Status.Equals("VERIFICATION_PENDING", StringComparison.OrdinalIgnoreCase))
            {
                return $"Verification pending | {compactExpected}";
            }

            if (Status.Equals("HASH_FAILED", StringComparison.OrdinalIgnoreCase))
            {
                return $"Checksum mismatch | {compactExpected}";
            }

            if (Status.Equals("COVERED_BY_MANAGED", StringComparison.OrdinalIgnoreCase))
            {
                return $"Covered by managed download | {compactExpected}";
            }

            return $"Missing | {compactExpected}";
        }
    }

    public string VerificationDisplay => Status.Trim().ToUpperInvariant() switch
    {
        "INSTALLED" => "Verified",
        "VERIFICATION_PENDING" => "Pending",
        "COVERED_BY_MANAGED" => "Covered",
        "HASH_FAILED" => "Checksum mismatch",
        "MISSING_REQUIRED" => "Not present",
        "MANUAL_REQUIRED" => "Manual",
        _ => string.IsNullOrWhiteSpace(Verification) ? "Unknown" : Verification
    };

    public string ActionDisplay => Status.Trim().ToUpperInvariant() switch
    {
        "INSTALLED" => "No action needed",
        "VERIFICATION_PENDING" => "Revalidate",
        "HASH_FAILED" => "Checksum issue",
        "UPDATE_AVAILABLE" => "Download/update",
        "MISSING_REQUIRED" => "Run Setup USB Toolkit",
        "COVERED_BY_MANAGED" => "No action needed",
        "MANUAL_REQUIRED" => string.IsNullOrWhiteSpace(MatchedPath) ? "Open shortcut" : "No action needed",
        _ => string.IsNullOrWhiteSpace(Recommendation) ? "Review detail" : TruncateSingleLine(Recommendation, 44)
    };

    public string RecommendationShort => TruncateSingleLine(Recommendation, 72);

    public string DetailText =>
        $"{Tool} ({Category}){Environment.NewLine}" +
        $"Purpose: {(string.IsNullOrWhiteSpace(Purpose) ? "Not provided in current report." : Purpose)}{Environment.NewLine}" +
        $"Official URL: {(string.IsNullOrWhiteSpace(OfficialUrl) ? (string.IsNullOrWhiteSpace(Url) ? "Not provided." : Url) : OfficialUrl)}{Environment.NewLine}" +
        $"License / redistribution: {(string.IsNullOrWhiteSpace(LicenseRedistributionNote) ? "Check vendor terms before bundling." : LicenseRedistributionNote)}{Environment.NewLine}" +
        $"Distribution model: {(string.IsNullOrWhiteSpace(DistributionModel) ? TypeDisplay : DistributionModel)}{Environment.NewLine}" +
        $"Beta safety rating: {(string.IsNullOrWhiteSpace(BetaSafetyRating) ? "Needs review" : BetaSafetyRating)}{Environment.NewLine}" +
        $"Download status: {(string.IsNullOrWhiteSpace(DownloadStatus) ? StatusDisplayUi : DownloadStatus)}{Environment.NewLine}" +
        $"Checksum status: {(string.IsNullOrWhiteSpace(ChecksumStatus) ? VerificationDisplay : ChecksumStatus)}{Environment.NewLine}" +
        $"Classification: {NormalizedCategoryLabel}{Environment.NewLine}" +
        $"Status: {StatusDisplayUi}{Environment.NewLine}" +
        $"Type: {TypeDisplay}{Environment.NewLine}" +
        $"Expected path: {(string.IsNullOrWhiteSpace(ResolvedExpectedPath) ? ExpectedPath : ResolvedExpectedPath)}{Environment.NewLine}" +
        $"Found path: {(string.IsNullOrWhiteSpace(MatchedPath) ? "UNKNOWN" : MatchedPath)}{Environment.NewLine}" +
        $"Size: {(SizeBytes > 0 ? FormatSize(SizeBytes) : "unknown")}{Environment.NewLine}" +
        $"Verification: {VerificationDisplay}{Environment.NewLine}" +
        $"Reason: {(string.IsNullOrWhiteSpace(ClassificationReason) ? "Report did not include a classification reason." : ClassificationReason)}{Environment.NewLine}" +
        $"Next step: {Recommendation}";

    private static string CompactPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "Path not reported";
        }

        var normalized = path.Replace('/', '\\').Trim();
        var parts = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 3)
        {
            return normalized;
        }

        return $"{parts[0]}\\{parts[1]}\\...\\{parts[^1]}";
    }

    private static string FormatSize(long sizeBytes)
    {
        if (sizeBytes <= 0)
        {
            return "0 B";
        }

        var mb = sizeBytes / (1024d * 1024d);
        return mb >= 100d ? $"{Math.Round(mb):0} MB" : $"{mb:0.#} MB";
    }

    private static string TruncateSingleLine(string text, int maxLength)
    {
        var normalized = (text ?? string.Empty).Replace(Environment.NewLine, " ").Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..(maxLength - 3)] + "...";
    }
}
