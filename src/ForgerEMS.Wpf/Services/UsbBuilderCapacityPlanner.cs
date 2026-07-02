using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services;

public enum UsbBuilderCapacityLevel
{
    // Ample free space (>= 20 GB free after the projected build).
    Ample,
    // Yellow band: under 20 GB free after the projected build.
    Tight,
    // Red band: under 10 GB free after the projected build.
    Critical,
    // Build would not fit on the USB.
    OverCapacity
}

public sealed record UsbBuilderCapacityPlan(
    long TotalBytes,
    long UsedBytes,
    long SelectedNewBytes,
    long ProjectedUsedBytes,
    long ProjectedFreeBytes,
    UsbBuilderCapacityLevel Level,
    string Headline,
    string Detail,
    bool BlocksBuild)
{
    public bool HasUsbContext => TotalBytes > 0;

    public static UsbBuilderCapacityPlan NoUsb { get; } = new(
        TotalBytes: 0,
        UsedBytes: 0,
        SelectedNewBytes: 0,
        ProjectedUsedBytes: 0,
        ProjectedFreeBytes: 0,
        Level: UsbBuilderCapacityLevel.Ample,
        Headline: "Select a USB target to see capacity planning.",
        Detail: string.Empty,
        BlocksBuild: false);
}

// Single source of truth for "would my selection fit on this USB?". The view
// model feeds in the selected profile options + the chosen USB and the planner
// returns thresholds, severity, and copy. Thresholds mirror the spec:
//   - Yellow when projected free < 20 GB
//   - Red when projected free < 10 GB
//   - OverCapacity when projected used > total
public static class UsbBuilderCapacityPlanner
{
    public const long YellowThresholdBytes = 20L * 1024 * 1024 * 1024;
    public const long RedThresholdBytes = 10L * 1024 * 1024 * 1024;

    public static UsbBuilderCapacityPlan Calculate(
        IEnumerable<UsbBuilderProfileOption> options,
        UsbTargetInfo? target)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (target is null || target.TotalBytes <= 0)
        {
            return UsbBuilderCapacityPlan.NoUsb;
        }

        var optionList = options.ToList();
        var totalBytes = target.TotalBytes;
        var freeBytes = target.FreeBytes > 0 ? target.FreeBytes : 0;
        var usedBytes = Math.Max(0, totalBytes - freeBytes);

        // Selected NEW bytes = sum of item.ProjectedNewBytes across included
        // options. Items already present on the USB contribute zero — they are
        // already counted in usedBytes. User-supplied media also contribute zero
        // (we cannot guess the size). The capacity planner therefore reflects
        // the additional download we would add, not the whole selection.
        long selectedNew = 0;
        foreach (var option in optionList.Where(o => o.IsIncluded))
        {
            if (option.Items.Count > 0)
            {
                selectedNew += option.ProjectedNewDownloadBytes;
            }
            else
            {
                // No item drill-down yet — fall back to typical category estimate
                // when the category has nothing already on the USB.
                if (option.DetectedBytes <= 0)
                {
                    selectedNew += option.SpaceEstimate.TypicalBytes ?? 0;
                }
            }
        }

        var projectedUsed = usedBytes + selectedNew;
        var projectedFree = totalBytes - projectedUsed;

        UsbBuilderCapacityLevel level;
        string headline;
        string detail;
        bool blocks;

        if (projectedUsed > totalBytes)
        {
            level = UsbBuilderCapacityLevel.OverCapacity;
            blocks = true;
            headline = $"Selected content would exceed USB capacity by {UsbTargetInfo.FormatBytes(projectedUsed - totalBytes)}.";
            detail = "Remove items, deselect ISOs/installers, or use a larger USB.";
        }
        else if (projectedFree < RedThresholdBytes)
        {
            level = UsbBuilderCapacityLevel.Critical;
            blocks = true;
            headline = $"Projected free space {UsbTargetInfo.FormatBytes(projectedFree)} is below the {UsbTargetInfo.FormatBytes(RedThresholdBytes)} safety floor.";
            detail = "Remove a large ISO or installer before building.";
        }
        else if (projectedFree < YellowThresholdBytes)
        {
            level = UsbBuilderCapacityLevel.Tight;
            blocks = false;
            headline = $"Projected free space {UsbTargetInfo.FormatBytes(projectedFree)} is tight (< {UsbTargetInfo.FormatBytes(YellowThresholdBytes)}).";
            detail = "Consider trimming large ISOs or splitting into a second USB.";
        }
        else
        {
            level = UsbBuilderCapacityLevel.Ample;
            blocks = false;
            headline = $"Projected free space: {UsbTargetInfo.FormatBytes(projectedFree)}.";
            detail = "Comfortable margin for build and updates.";
        }

        return new UsbBuilderCapacityPlan(
            TotalBytes: totalBytes,
            UsedBytes: usedBytes,
            SelectedNewBytes: selectedNew,
            ProjectedUsedBytes: projectedUsed,
            ProjectedFreeBytes: Math.Max(0, projectedFree),
            Level: level,
            Headline: headline,
            Detail: detail,
            BlocksBuild: blocks);
    }
}
