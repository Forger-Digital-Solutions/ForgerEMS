using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Dev Smoke regression coverage for the 2026-05-22 cache-pollution report:
/// a re-run that comes back as cache-suspected (e.g. raw read 2830 MB/s on a USB drive
/// whose ceiling is ~1200 MB/s) must never overwrite a previously verified believable
/// read in any layer the UI / Intelligence consumes.
/// </summary>
public sealed class UsbBenchmarkCachePollutionTests
{
    [Fact]
    public void CacheSuspectedResult_DoesNotPersistToHistory()
    {
        var cached = new UsbBenchmarkResult
        {
            Succeeded = true,
            Status = "Complete",
            WriteSpeedMBps = 60.9,
            ReadSpeedMBps = 2830.8,
            ReadLikelyCached = true,
            ReadIsEstimate = true,
            IsReadCacheSuspected = true
        };

        Assert.False(cached.ShouldPersistSuccessfulHistory);
    }

    [Fact]
    public void VerifiedResult_DoesPersistToHistory()
    {
        var verified = new UsbBenchmarkResult
        {
            Succeeded = true,
            Status = "Complete",
            WriteSpeedMBps = 60.9,
            ReadSpeedMBps = 78.4,
            ReadLikelyCached = false,
            ReadIsEstimate = false,
            IsReadCacheSuspected = false
        };

        Assert.True(verified.ShouldPersistSuccessfulHistory);
    }

    [Fact]
    public void Accuracy_ImpossibleReadAboveUsbCeiling_IsFlaggedCached()
    {
        var assessment = VentoyToolkitSetup.Wpf.Services.Intelligence.UsbBenchmarkAccuracy.Assess(
            writeMbps: 60.9,
            readMbps: 2830.8,
            speedHint: null,
            target: new UsbTargetInfo
            {
                RootPath = "D:\\",
                BusType = "USB",
                DeviceModel = "Generic USB stick" // not an SSD, so ceiling is the conservative 1200
            });

        Assert.True(assessment.ReadLikelyCached);
        Assert.True(assessment.ReadIsEstimate);
        // The reason can mention either the impossible-ceiling branch or the ratio-based branch.
        var reasonLower = assessment.Reason.ToLowerInvariant();
        Assert.True(
            reasonLower.Contains("cache") || reasonLower.Contains("ceiling") || reasonLower.Contains("plausible"),
            $"Cache-suspected reason should explain WHY the read is suspect. Got: '{assessment.Reason}'.");
    }

    [Fact]
    public void Accuracy_BelievableUsbRead_IsNotFlaggedCached()
    {
        var assessment = VentoyToolkitSetup.Wpf.Services.Intelligence.UsbBenchmarkAccuracy.Assess(
            writeMbps: 60.9,
            readMbps: 78.4,
            speedHint: VentoyToolkitSetup.Wpf.Services.Intelligence.UsbSpeedClassification.Usb3,
            target: null);

        Assert.False(assessment.ReadLikelyCached);
        Assert.False(assessment.ReadIsEstimate);
    }

    [Fact]
    public void AutomaticBenchmarkPolicy_BlocksRepeatStartsForSameRoot()
    {
        var policy = new VentoyToolkitSetup.Wpf.Services.Intelligence.UsbAutomaticBenchmarkPolicy();
        var now = System.DateTimeOffset.UtcNow;
        Assert.True(policy.TryRegisterAutomaticStart("D:\\", now));
        Assert.False(policy.TryRegisterAutomaticStart("D:\\", now.AddSeconds(5)));
        Assert.False(policy.TryRegisterAutomaticStart("D:\\", now.AddSeconds(20)));
    }
}
