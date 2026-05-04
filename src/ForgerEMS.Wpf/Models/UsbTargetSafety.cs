using System;

namespace VentoyToolkitSetup.Wpf.Models;

public static class UsbTargetSafety
{
    /// <summary>User-facing explanation when the Windows OS volume is excluded from USB/Ventoy actions (Kyra + UI).</summary>
    public const string WindowsOsDriveBlockedExplanation =
        "ForgerEMS blocks the Windows OS drive from USB build actions to prevent wiping the machine.";

    public static string? GetExecutionBlockReason(UsbTargetInfo? target)
    {
        if (target is null)
        {
            return "Select the main USB storage partition before running this action.";
        }

        if (IsProtectedSystemDrive(target))
        {
            return WindowsOsDriveBlockedExplanation;
        }

        var hasVentoyEfiLabel = target.Label.Contains("VTOYEFI", StringComparison.OrdinalIgnoreCase);
        var isTooSmall = target.TotalBytes > 0 && target.TotalBytes < UsbTargetInfo.MinimumTargetBytes;
        var bootOrSystemFlagBlocks = (target.IsSystemDrive || target.IsBootDrive) &&
                                     !target.IsLargeDataPartition &&
                                     !target.IsPreferredUsbTarget;

        if (hasVentoyEfiLabel || target.IsEfiSystemPartition || target.IsUndersizedPartition || isTooSmall)
        {
            return
                "You selected a boot partition, not the main USB storage." + Environment.NewLine + Environment.NewLine +
                $"Target: {target.RootPath} ({target.LabelDisplay})" + Environment.NewLine +
                $"Size: {target.DisplayTotalBytes}" + Environment.NewLine +
                $"Filesystem: {target.FileSystem}" + Environment.NewLine +
                $"IsBoot: {target.IsBootDrive}" + Environment.NewLine +
                $"IsSystem: {target.IsSystemDrive}" + Environment.NewLine +
                $"Partition type: {target.PartitionTypeDisplay}" + Environment.NewLine +
                "Select the largest main USB data partition instead.";
        }

        if (bootOrSystemFlagBlocks)
        {
            return
                "This target is marked as a Windows boot or system partition and is blocked." + Environment.NewLine + Environment.NewLine +
                $"Target: {target.RootPath} ({target.LabelDisplay})" + Environment.NewLine +
                $"IsBoot: {target.IsBootDrive}" + Environment.NewLine +
                $"IsSystem: {target.IsSystemDrive}" + Environment.NewLine +
                $"Partition type: {target.PartitionTypeDisplay}";
        }

        if (!target.IsSelectable)
        {
            return string.IsNullOrWhiteSpace(target.SelectionWarningDisplay)
                ? "This USB target is blocked and cannot be used for Setup USB, Update USB, or Ventoy actions."
                : target.SelectionWarningDisplay;
        }

        return null;
    }

    public static bool IsSafeForBenchmark(UsbTargetInfo? target, out string blockReason)
    {
        var executionBlockReason = GetExecutionBlockReason(target);
        if (!string.IsNullOrWhiteSpace(executionBlockReason))
        {
            blockReason = executionBlockReason;
            return false;
        }

        if (target is null)
        {
            blockReason = "Select a USB target before benchmarking.";
            return false;
        }

        if (!target.IsLikelyUsb)
        {
            blockReason = "Target is not detected as USB media.";
            return false;
        }

        var mediaOkForBenchmark =
            target.IsRemovableMedia ||
            target.IsPreferredUsbTarget ||
            target.IsLargeDataPartition ||
            target.HasVentoyStyleLargeDataPartition;

        if (!mediaOkForBenchmark)
        {
            blockReason =
                "Benchmark runs only on removable USB volumes or a recognized large Ventoy/USB data partition.";
            return false;
        }

        const long benchmarkMinFreeBytes = (128L + 128L) * 1024 * 1024;
        if (target.FreeBytes < benchmarkMinFreeBytes)
        {
            blockReason =
                $"Not enough free space for a USB file benchmark (need about {UsbTargetInfo.FormatBytes(benchmarkMinFreeBytes)} free).";
            return false;
        }

        blockReason = string.Empty;
        return true;
    }

    public static bool IsProtectedSystemDrive(UsbTargetInfo target)
    {
        var driveLetter = string.IsNullOrWhiteSpace(target.DriveLetter)
            ? target.RootPath.Trim().TrimEnd('\\', ':')
            : target.DriveLetter.Trim().TrimEnd(':');

        return string.Equals(driveLetter, "C", StringComparison.OrdinalIgnoreCase);
    }
}
