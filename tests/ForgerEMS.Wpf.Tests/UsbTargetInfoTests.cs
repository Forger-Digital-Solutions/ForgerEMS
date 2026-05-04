using System;
using VentoyToolkitSetup.Wpf.Models;

namespace ForgerEMS.Wpf.Tests;

public sealed class UsbTargetInfoTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1024 * 1024, "1 MB")]
    [InlineData(1024L * 1024 * 1024, "1 GB")]
    public void FormatBytesReturnsExpectedDisplay(long bytes, string expected)
    {
        var actual = UsbTargetInfo.FormatBytes(bytes);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void UsbSafetyBlocksTinyVtoyefiPartition()
    {
        var target = new UsbTargetInfo
        {
            RootPath = "E:\\",
            Label = "VTOYEFI",
            TotalBytes = 128L * 1024 * 1024,
            FileSystem = "FAT32",
            IsSelectable = false,
            IsEfiSystemPartition = true,
            IsUndersizedPartition = true
        };

        var reason = UsbTargetSafety.GetExecutionBlockReason(target);

        Assert.NotNull(reason);
        Assert.Contains("boot partition", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("main USB storage", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UsbSafetyBlocksBenchmarkWhenFreeSpaceTooLow()
    {
        var target = new UsbTargetInfo
        {
            RootPath = "F:\\",
            Label = "Ventoy",
            TotalBytes = 64L * 1024 * 1024 * 1024,
            FreeBytes = 64L * 1024 * 1024,
            FileSystem = "exFAT",
            IsLikelyUsb = true,
            IsRemovableMedia = true,
            IsSelectable = true,
            IsLargeDataPartition = true,
            IsPreferredUsbTarget = false
        };

        Assert.False(UsbTargetSafety.IsSafeForBenchmark(target, out var reason));
        Assert.Contains("free space", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UsbSafetyBlocksBenchmarkOnProtectedSystemDriveC()
    {
        var target = new UsbTargetInfo
        {
            RootPath = "C:\\",
            Label = "OS",
            TotalBytes = 256L * 1024 * 1024 * 1024,
            FileSystem = "NTFS",
            IsLikelyUsb = false,
            IsRemovableMedia = false,
            IsSelectable = false,
            IsSystemDrive = true,
            IsBootDrive = true
        };

        Assert.False(UsbTargetSafety.IsSafeForBenchmark(target, out var benchmarkReason));
        Assert.False(string.IsNullOrWhiteSpace(benchmarkReason));
        Assert.Contains("Windows OS drive", benchmarkReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UsbSafetyAllowsLargeRemovableDataPartition()
    {
        var target = new UsbTargetInfo
        {
            RootPath = "F:\\",
            Label = "Ventoy",
            TotalBytes = 64L * 1024 * 1024 * 1024,
            FreeBytes = 512L * 1024 * 1024,
            FileSystem = "exFAT",
            IsLikelyUsb = true,
            IsRemovableMedia = true,
            IsSelectable = true,
            IsLargeDataPartition = true,
            IsPreferredUsbTarget = true
        };

        var reason = UsbTargetSafety.GetExecutionBlockReason(target);
        var safeForBenchmark = UsbTargetSafety.IsSafeForBenchmark(target, out var benchmarkReason);

        Assert.Null(reason);
        Assert.True(safeForBenchmark);
        Assert.True(string.IsNullOrWhiteSpace(benchmarkReason));
    }

    [Fact]
    public void VentoyFixedDataPartition_IsPreferredSafetyCopy_NotSystemDiskWarning()
    {
        var target = new UsbTargetInfo
        {
            RootPath = "D:\\",
            Label = "Ventoy",
            TotalBytes = 64L * 1024 * 1024 * 1024,
            FreeBytes = 512L * 1024 * 1024,
            FileSystem = "exFAT",
            DriveType = "Fixed USB",
            IsLikelyUsb = true,
            IsRemovableMedia = false,
            IsSelectable = true,
            IsSystemDrive = true,
            IsBootDrive = true,
            IsEfiSystemPartition = false,
            IsUndersizedPartition = false,
            HasVentoyCompanionEfiPartition = true,
            IsLargeDataPartition = true,
            IsPreferredUsbTarget = true
        };

        Assert.Equal("PREFERRED", target.SafetyStatusText);
        Assert.Contains("Ventoy data partition detected", target.SafetyReasonText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("system disk metadata", target.SafetyReasonText, StringComparison.OrdinalIgnoreCase);
        Assert.True(UsbTargetSafety.IsSafeForBenchmark(target, out var benchReason));
        Assert.True(string.IsNullOrWhiteSpace(benchReason));
    }

    [Fact]
    public void RemovableNonVentoy_IsSafeStatus()
    {
        var target = new UsbTargetInfo
        {
            RootPath = "E:\\",
            Label = "FORGER",
            TotalBytes = 32L * 1024 * 1024 * 1024,
            FreeBytes = 512L * 1024 * 1024,
            FileSystem = "NTFS",
            IsLikelyUsb = true,
            IsRemovableMedia = true,
            IsSelectable = true,
            IsSystemDrive = false,
            IsBootDrive = false,
            IsLargeDataPartition = true,
            IsPreferredUsbTarget = false
        };

        Assert.Equal("SAFE", target.SafetyStatusText);
        Assert.Contains("Ready — removable USB detected", target.SafetyReasonText, StringComparison.OrdinalIgnoreCase);
    }
}
