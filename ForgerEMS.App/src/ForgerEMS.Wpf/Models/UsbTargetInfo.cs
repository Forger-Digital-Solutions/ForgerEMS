using System;

namespace VentoyToolkitSetup.Wpf.Models;

public sealed class UsbTargetInfo
{
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

    public string SpeedDisplay { get; init; } = "Not available";

    public bool IsSystemDrive { get; init; }

    public bool IsBootDrive { get; init; }

    public bool IsRemovableMedia { get; init; }

    public bool IsSelectable { get; init; } = true;

    public string SelectionWarning { get; init; } = string.Empty;

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

    public string SpeedDisplayNormalized => string.IsNullOrWhiteSpace(SpeedDisplay) ? "Not available" : SpeedDisplay;

    public string SelectionStatusText =>
        !IsSelectable
            ? "Blocked"
            : IsRemovableMedia
                ? "Removable USB"
                : "Fixed USB";

    public string RoleDisplay =>
        IsSystemDrive || IsBootDrive
            ? "System or boot volume"
            : "Operator target";

    public string SelectionWarningDisplay =>
        string.IsNullOrWhiteSpace(SelectionWarning)
            ? "Double-check the drive letter and label before running destructive actions."
            : SelectionWarning;

    public string DisplayName => $"{RootPath}  {LabelDisplay}  {SelectionStatusText}  {DisplayTotalBytes} total";

    private static string FormatBytes(long bytes)
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
