using System.Collections.Generic;

namespace VentoyToolkitSetup.Wpf.Services;

public sealed class UsbBuilderProfileFullManagedDownloadPlan
{
    public static readonly UsbBuilderProfileFullManagedDownloadPlan Empty = new();

    public string ManifestPath { get; init; } = string.Empty;

    public int EligibleManagedCount { get; init; }

    public int AlreadyPresentCount { get; init; }

    public int MissingCount { get; init; }

    public int ExcludedManualOrVendorCount { get; init; }

    public int ExcludedBySafetyCount { get; init; }

    public int ExcludedByProfileCount { get; init; }

    public long EstimatedDownloadBytes { get; init; }

    public int UnknownSizeCount { get; init; }

    public bool HasUsbRoot { get; init; }

    public IReadOnlyList<string> EligibleNames { get; init; } = [];

    public IReadOnlyList<string> AlreadyPresentNames { get; init; } = [];

    public IReadOnlyList<string> MissingNames { get; init; } = [];

    public string EstimatedDownloadDisplay =>
        EstimatedDownloadBytes > 0 ? FormatBytes(EstimatedDownloadBytes) : "estimate unavailable";

    public string ShortSummaryLine =>
        HasUsbRoot
            ? $"Managed downloads: {AlreadyPresentCount} ready / {EligibleManagedCount} eligible (missing {MissingCount})"
            : $"Managed downloads: {EligibleManagedCount} eligible for this profile";

    public string ManualLinkLine =>
        $"Manual/vendor links: {ExcludedManualOrVendorCount} guided shortcuts";

    public string ProfileExclusionLine =>
        ExcludedByProfileCount > 0
            ? $"Profile filter excludes {ExcludedByProfileCount} managed item(s) from off categories."
            : string.Empty;

    private static string FormatBytes(long bytes)
    {
        const long KB = 1024;
        const long MB = KB * 1024;
        const long GB = MB * 1024;

        return bytes switch
        {
            >= GB => $"~{(bytes / (double)GB):0.##} GB",
            >= MB => $"~{(bytes / (double)MB):0.##} MB",
            >= KB => $"~{(bytes / (double)KB):0.##} KB",
            _ => $"{bytes} B"
        };
    }
}
