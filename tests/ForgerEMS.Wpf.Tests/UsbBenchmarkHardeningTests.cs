using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class UsbBenchmarkHardeningTests
{
    [Fact]
    public void IdentitySnapshot_MismatchWhenCapacityChanges()
    {
        var a = UsbTargetIdentitySnapshot.Capture(new UsbTargetInfo
        {
            DriveLetter = "E:",
            Label = "USB",
            TotalBytes = 64L * 1024 * 1024 * 1024,
            FreeBytes = 32L * 1024 * 1024 * 1024,
            DeviceModel = "FlashDrive",
            ClassificationDetails = "bus=USB"
        });

        var b = new UsbTargetInfo
        {
            DriveLetter = "E:",
            Label = "USB",
            TotalBytes = 32L * 1024 * 1024 * 1024,
            FreeBytes = 16L * 1024 * 1024 * 1024,
            DeviceModel = "FlashDrive",
            ClassificationDetails = "bus=USB"
        };

        Assert.False(a.MatchesVolumeIdentity(b, out _), "Different capacity should break identity match.");
    }

    [Fact]
    public void ServiceResult_ShouldPersistSuccessfulHistory_OnlyWhenCompleted()
    {
        var ok = new UsbBenchmarkResult
        {
            Succeeded = true,
            Status = "Complete",
            WriteSpeedMBps = 40,
            ReadSpeedMBps = 120,
            ResultKind = UsbBenchmarkResultKind.Completed
        };
        Assert.True(ok.ShouldPersistSuccessfulHistory);

        var cancelled = new UsbBenchmarkResult
        {
            Succeeded = false,
            Status = "Cancelled",
            ResultKind = UsbBenchmarkResultKind.CancelledByUser
        };
        Assert.False(cancelled.ShouldPersistSuccessfulHistory);

        var blocked = new UsbBenchmarkResult
        {
            Succeeded = false,
            Status = "Blocked",
            ResultKind = UsbBenchmarkResultKind.BlockedBySafety
        };
        Assert.False(blocked.ShouldPersistSuccessfulHistory);
    }

    [Fact]
    public void UiMessages_CompletedFormatsReadWriteOrder()
    {
        var s = UsbBenchmarkUiMessages.BuildUiSummary(UsbBenchmarkResultKind.Completed, 100.2, 40.5);
        Assert.Contains("100.2", s);
        Assert.Contains("40.5", s);
        Assert.Contains("Read", s);
        Assert.Contains("Write", s);
    }
}
