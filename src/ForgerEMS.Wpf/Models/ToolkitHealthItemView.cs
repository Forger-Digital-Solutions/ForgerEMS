namespace VentoyToolkitSetup.Wpf.Models;

public sealed class ToolkitHealthItemView
{
    public string Tool { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public string ExpectedPath { get; init; } = string.Empty;

    public string ExpectedFoundPath { get; init; } = string.Empty;

    public string MatchedPath { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public string ClassificationReason { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string Verification { get; init; } = string.Empty;

    public string Recommendation { get; init; } = string.Empty;

    public string NormalizedCategoryLabel { get; init; } = string.Empty;

    /// <summary>Grid/UI label — manual-required items are never shown as generic “missing”.</summary>
    public string StatusDisplayUi => Status.Trim().ToUpperInvariant() switch
    {
        "MISSING_REQUIRED" => "Managed missing",
        "MISSING" => "Managed missing",
        "INSTALLED" => "Managed ready",
        "UPDATE_AVAILABLE" => "Update available",
        "HASH_FAILED" => "Verification issue",
        "MANUAL_REQUIRED" => "Manual required",
        "PLACEHOLDER" => "Placeholder",
        "SKIPPED" => "Skipped",
        _ => Status
    };

    public string DetailText =>
        $"{Tool} ({Category}){Environment.NewLine}" +
        $"Classification: {NormalizedCategoryLabel}{Environment.NewLine}" +
        $"Status: {Status}{Environment.NewLine}" +
        $"Type: {Type}{Environment.NewLine}" +
        $"Expected path: {ExpectedPath}{Environment.NewLine}" +
        $"Found path: {(string.IsNullOrWhiteSpace(MatchedPath) ? "UNKNOWN" : MatchedPath)}{Environment.NewLine}" +
        $"Verification: {Verification}{Environment.NewLine}" +
        $"Reason: {(string.IsNullOrWhiteSpace(ClassificationReason) ? "Report did not include a classification reason." : ClassificationReason)}{Environment.NewLine}" +
        $"Next step: {Recommendation}";
}
