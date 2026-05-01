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
    public void UsbSafetyAllowsLargeRemovableDataPartition()
    {
        var target = new UsbTargetInfo
        {
            RootPath = "F:\\",
            Label = "Ventoy",
            TotalBytes = 64L * 1024 * 1024 * 1024,
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
}
