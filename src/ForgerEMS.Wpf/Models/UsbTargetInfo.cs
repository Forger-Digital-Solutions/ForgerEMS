using System;
using System.Globalization;

namespace VentoyToolkitSetup.Wpf.Models;

public sealed class UsbTargetInfo
{
    public const long MinimumTargetBytes = 1L * 1024 * 1024 * 1024;
    public const long LargeDataPartitionBytes = 4L * 1024 * 1024 * 1024;
    public const long PreferredVentoyDataPartitionBytes = 10L * 1024 * 1024 * 1024;

    public string DriveLetter { get; init; } = string.Empty;

    public string RootPath { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string FileSystem { get; init; } = string.Empty;

    public long TotalBytes { get; init; }

    public long FreeBytes { get; init; }

    public string DriveType { get; init; } = string.Empty;

    public string BusType { get; init; } = string.Empty;

    public bool IsLikelyUsb { get; init; }

    public string DeviceBrand { get; init; } = string.Empty;

    public string DeviceModel { get; init; } = string.Empty;

    public string ReadSpeedDisplay { get; init; } = "Not tested";

    public string WriteSpeedDisplay { get; init; } = "Not tested";

    public string BenchmarkStatus { get; init; } = "Not tested";

    public int BenchmarkTestSizeMb { get; init; }

    public DateTimeOffset? BenchmarkLastTestedAt { get; init; }

    public string PartitionType { get; init; } = string.Empty;

    public bool IsSystemDrive { get; init; }

    public bool IsBootDrive { get; init; }

    public bool IsRemovableMedia { get; init; }

    public bool IsEfiSystemPartition { get; init; }

    public bool IsUndersizedPartition { get; init; }

    public bool HasVentoyCompanionEfiPartition { get; init; }

    public bool IsLargeDataPartition { get; init; }

    public bool IsPreferredUsbTarget { get; init; }

    public bool IsSelectable { get; init; } = true;

    public string SelectionWarning { get; init; } = string.Empty;

    public string ClassificationDetails { get; init; } = string.Empty;

    public string LabelDisplay => string.IsNullOrWhiteSpace(Label) ? "(no label)" : Label;

    public string DisplayTotalBytes => FormatBytes(TotalBytes);

    public string DisplayFreeBytes => FormatBytes(FreeBytes);

    public string DeviceIdentityDisplay
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(DeviceBrand) && !string.IsNullOrWhiteSpace(DeviceModel))
            {
                return $"{DeviceBrand} {DeviceModel}";
            }

            if (!string.IsNullOrWhiteSpace(DeviceModel))
            {
                return DeviceModel;
            }

            if (!string.IsNullOrWhiteSpace(DeviceBrand))
            {
                return DeviceBrand;
            }

            return "Unknown device";
        }
    }

    public string BusTypeDisplay => string.IsNullOrWhiteSpace(BusType) ? "Unknown" : BusType;

    public string ReadSpeedDisplayNormalized => string.IsNullOrWhiteSpace(ReadSpeedDisplay) ? "Not tested" : ReadSpeedDisplay;

    public string WriteSpeedDisplayNormalized => string.IsNullOrWhiteSpace(WriteSpeedDisplay) ? "Not tested" : WriteSpeedDisplay;

    public string BenchmarkStatusDisplay => string.IsNullOrWhiteSpace(BenchmarkStatus) ? "Not tested" : BenchmarkStatus;

    public string BenchmarkTestSizeDisplay => BenchmarkTestSizeMb > 0 ? $"{BenchmarkTestSizeMb} MB" : "Not tested";

    public string BenchmarkLastTestedDisplay => BenchmarkLastTestedAt.HasValue ? BenchmarkLastTestedAt.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture) : "Never";

    /// <summary>Ventoy data volume heuristic: companion EFI + large non-EFI data partition.</summary>
    public bool HasVentoyStyleLargeDataPartition =>
        HasVentoyCompanionEfiPartition &&
        TotalBytes >= PreferredVentoyDataPartitionBytes &&
        !IsEfiSystemPartition &&
        !IsUndersizedPartition;

    public string SafetyStatusText
    {
        get
        {
            if (!IsSelectable || ShouldBlockExecution)
            {
                return "BLOCKED";
            }

            if (IsPreferredUsbTarget || HasVentoyStyleLargeDataPartition)
            {
                return "PREFERRED";
            }

            if (!IsLikelyUsb)
            {
                return "WARNING";
            }

            if (UsbTargetSafety.IsProtectedSystemDrive(this))
            {
                return "BLOCKED";
            }

            if (IsRemovableMedia)
            {
                return "SAFE";
            }

            if (IsLargeDataPartition || HasVentoyCompanionEfiPartition)
            {
                return "SAFE";
            }

            return "WARNING";
        }
    }

    public string SafetyReasonText
    {
        get
        {
            var blockReason = UsbTargetSafety.GetExecutionBlockReason(this);
            if (!string.IsNullOrWhiteSpace(blockReason))
            {
                return blockReason.Replace(Environment.NewLine, " ", StringComparison.Ordinal);
            }

            if (!IsSelectable)
            {
                return SelectionWarningDisplay;
            }

            if (IsEfiSystemPartition || IsUndersizedPartition)
            {
                return "Blocked: small EFI/VTOYEFI or utility partition";
            }

            if (IsPreferredUsbTarget || HasVentoyStyleLargeDataPartition)
            {
                return "Ventoy data partition detected. This is the correct target.";
            }

            if (!IsLikelyUsb)
            {
                return "Check USB selection before continuing.";
            }

            if (IsRemovableMedia)
            {
                return "Ready — removable USB detected";
            }

            if (IsLargeDataPartition)
            {
                return "Ready — USB storage partition detected (fixed-type volume).";
            }

            return "Check USB selection before continuing.";
        }
    }

    public string SelectionStatusText =>
        !IsSelectable
            ? "Blocked"
            : IsPreferredUsbTarget
                ? "Preferred USB"
            : IsRemovableMedia
                ? "Removable USB"
                : "Fixed USB";

    public string RoleDisplay =>
        IsEfiSystemPartition
            ? "Boot / EFI partition"
            : IsPreferredUsbTarget
                ? "Ventoy data partition"
            : IsLargeDataPartition
                ? "Main USB data partition"
            : IsUndersizedPartition
                ? "Small utility partition"
                : "Operator target";

    public string PartitionTypeDisplay =>
        string.IsNullOrWhiteSpace(PartitionType)
            ? "Unknown"
            : PartitionType;

    public bool ShouldBlockExecution =>
        UsbTargetSafety.GetExecutionBlockReason(this) is not null;

    public string SelectionWarningDisplay =>
        string.IsNullOrWhiteSpace(SelectionWarning)
            ? "Double-check the drive letter and label before running destructive actions."
            : SelectionWarning;

    public string ClassificationDetailsDisplay =>
        string.IsNullOrWhiteSpace(ClassificationDetails)
            ? $"size={DisplayTotalBytes}; filesystem={FileSystem}; IsBoot={IsBootDrive}; IsSystem={IsSystemDrive}; partitionType={PartitionTypeDisplay}"
            : ClassificationDetails;

    public string DisplayName => $"{RootPath}  {LabelDisplay}  {SelectionStatusText}  {DisplayTotalBytes} total";

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.#} {units[unitIndex]}";
    }
}
