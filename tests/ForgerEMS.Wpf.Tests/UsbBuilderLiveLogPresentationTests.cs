using System.Globalization;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Models;

namespace ForgerEMS.Wpf.Tests;

public sealed class UsbBuilderLiveLogPresentationTests
{
    [Theory]
    [InlineData("[2026-01-01 12:00:00][INFO] Download start: Windows 11 ISO", false, "Downloading…")]
    [InlineData("[2026-01-01 12:00:00][INFO] SHA256 hash provider: DotNetFallback file=C:\\redacted\\x.iso", false, "Hashing / verifying large file…")]
    [InlineData("[2026-01-01 12:00:00][INFO] Up-to-date (sha256 match). Skipping.", false, "Already up to date")]
    [InlineData("[2026-01-01 12:00:00][OK] Shortcut updated: Drivers\\foo.url", false, "Shortcut updated")]
    [InlineData("[2026-01-01 12:00:00][OK] Items downloaded: 2", false, "Items downloaded: 2")]
    [InlineData("[2026-01-01 12:00:00][INFO] Working directory: C:\\apps\\ForgerEMS", false, null)]
    [InlineData("[2026-01-01 12:00:00][INFO] Working directory: C:\\apps\\ForgerEMS", true, null)]
    public void TryGetConciseSidebarLine_MapsOrHides(string text, bool verbose, string? expectedBody)
    {
        var line = new LogLine(DateTimeOffset.Parse("2026-01-01T12:00:00Z", CultureInfo.InvariantCulture), text, LogSeverity.Info);
        var ok = UsbBuilderLiveLogPresentation.TryGetConciseSidebarLine(line, verbose, out var sidebar);
        if (verbose)
        {
            Assert.True(ok);
            Assert.Equal(line.DisplayText, sidebar);
            return;
        }

        if (expectedBody is null)
        {
            Assert.False(ok);
            Assert.Equal(string.Empty, sidebar);
            return;
        }

        Assert.True(ok);
        Assert.EndsWith(expectedBody, sidebar, StringComparison.Ordinal);
    }

    [Fact]
    public void InferHeartbeatPhase_UsesLastMeaningfulOperation()
    {
        Assert.Equal(UsbManagedHeartbeatPhase.Downloading,
            UsbBuilderLiveLogPresentation.InferHeartbeatPhase("Download start: item"));
        Assert.Equal(UsbManagedHeartbeatPhase.HashingLargeFile,
            UsbBuilderLiveLogPresentation.InferHeartbeatPhase("SHA256 hash provider: X file=Y"));
        Assert.Equal(UsbManagedHeartbeatPhase.VerifyingChecksum,
            UsbBuilderLiveLogPresentation.InferHeartbeatPhase("Checksum expected vs actual: expected=a actual=b"));
    }

    [Fact]
    public void NormalizeHashProviderLabels_ReplacesDotNetFallbackLabel()
    {
        var s = UsbLogDisplayNormalizer.NormalizeHashProviderLabels("SHA256 hash provider: DotNetFallback file=test");
        Assert.Contains("Built-in .NET (large-file safe)", s, StringComparison.Ordinal);
        Assert.DoesNotContain("DotNetFallback", s, StringComparison.Ordinal);
    }
}
