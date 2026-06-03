using System;
using VentoyToolkitSetup.Wpf.Infrastructure;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Regression coverage for the v1.2.3 dev-smoke fix: managed download progress ticks
/// must not produce one Full Logs entry per network tick. Checkpoint cadence must
/// fire on first tick, every &gt;=30s, every &gt;=10%, and on completion.
/// </summary>
public sealed class ManagedDownloadProgressLogThrottleTests
{
    private static readonly DateTimeOffset Start = new(2026, 5, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NonProgressLines_AreAlwaysKept()
    {
        var throttle = new ManagedDownloadProgressLogThrottle();
        Assert.True(throttle.ShouldKeep("Download start: AlmaLinux 10.2 DVD (x86_64)", Start.UtcDateTime));
        Assert.True(throttle.ShouldKeep("SHA256 hash provider: managed", Start.UtcDateTime));
        Assert.True(throttle.ShouldKeep("Items downloaded: 4", Start.UtcDateTime));
        Assert.True(throttle.ShouldKeep("Verified OK (sha256 match) for kali-linux-2026.1.iso", Start.UtcDateTime));
    }

    [Fact]
    public void FirstProgressTick_PerItem_IsAlwaysKept()
    {
        var throttle = new ManagedDownloadProgressLogThrottle();
        Assert.True(throttle.ShouldKeep(
            "Downloading AlmaLinux 10.2 DVD (x86_64)... 1 MB downloaded | 8.0 MB/s",
            Start.UtcDateTime));
    }

    [Fact]
    public void SubsequentTicksWithinThirtySeconds_AndLessThanTenPercentDelta_AreDropped()
    {
        var throttle = new ManagedDownloadProgressLogThrottle();
        var first = "Downloading AlmaLinux 10.2 DVD (x86_64)... 1.0% | 12 MB / 1200 MB | 8.0 MB/s | ETA 2m 30s";
        Assert.True(throttle.ShouldKeep(first, Start.UtcDateTime));

        var droppedTicks = 0;
        for (var i = 1; i <= 14; i++)
        {
            var line = $"Downloading AlmaLinux 10.2 DVD (x86_64)... {1.0 + i * 0.5:0.0}% | {12 + i * 6} MB / 1200 MB | 8.0 MB/s | ETA 2m 0s";
            if (!throttle.ShouldKeep(line, Start.AddSeconds(2 * i).UtcDateTime))
            {
                droppedTicks++;
            }
        }

        Assert.True(droppedTicks >= 10, $"Expected most intra-window ticks to be dropped; dropped only {droppedTicks}/14");
    }

    [Fact]
    public void Tick_ThirtySecondsAfterLastCheckpoint_IsKept()
    {
        var throttle = new ManagedDownloadProgressLogThrottle();
        Assert.True(throttle.ShouldKeep(
            "Downloading AlmaLinux 10.2 DVD (x86_64)... 1.0% | 12 MB / 1200 MB | 8.0 MB/s | ETA 2m 30s",
            Start.UtcDateTime));
        Assert.False(throttle.ShouldKeep(
            "Downloading AlmaLinux 10.2 DVD (x86_64)... 2.0% | 24 MB / 1200 MB | 8.0 MB/s | ETA 2m 30s",
            Start.AddSeconds(5).UtcDateTime));
        Assert.True(throttle.ShouldKeep(
            "Downloading AlmaLinux 10.2 DVD (x86_64)... 3.5% | 42 MB / 1200 MB | 8.0 MB/s | ETA 2m 30s",
            Start.AddSeconds(31).UtcDateTime));
    }

    [Fact]
    public void Tick_WithTenPercentDelta_IsKept()
    {
        var throttle = new ManagedDownloadProgressLogThrottle();
        Assert.True(throttle.ShouldKeep(
            "Downloading kali-linux-2026.1.iso... 1.0% | 12 MB / 1200 MB | 8.0 MB/s | ETA 2m",
            Start.UtcDateTime));
        Assert.False(throttle.ShouldKeep(
            "Downloading kali-linux-2026.1.iso... 5.0% | 60 MB / 1200 MB | 8.0 MB/s | ETA 2m",
            Start.AddSeconds(4).UtcDateTime));
        Assert.True(throttle.ShouldKeep(
            "Downloading kali-linux-2026.1.iso... 12.0% | 144 MB / 1200 MB | 8.0 MB/s | ETA 2m",
            Start.AddSeconds(8).UtcDateTime));
    }

    [Fact]
    public void HundredPercent_IsAlwaysKept_RegardlessOfThrottle()
    {
        var throttle = new ManagedDownloadProgressLogThrottle();
        Assert.True(throttle.ShouldKeep(
            "Downloading kali-linux-2026.1.iso... 1.0% | 12 MB / 1200 MB | 8.0 MB/s | ETA 2m",
            Start.UtcDateTime));
        Assert.True(throttle.ShouldKeep(
            "Downloading kali-linux-2026.1.iso... 100.0% | 1200 MB / 1200 MB | 8.0 MB/s | ETA 0s",
            Start.AddSeconds(2).UtcDateTime));
    }

    [Fact]
    public void DownloadComplete_AndCancelled_AreAlwaysKept()
    {
        var throttle = new ManagedDownloadProgressLogThrottle();
        Assert.True(throttle.ShouldKeep("Download complete: kali-linux-2026.1.iso", Start.UtcDateTime));
        Assert.True(throttle.ShouldKeep("Download cancelled by user", Start.AddSeconds(1).UtcDateTime));
        Assert.True(throttle.ShouldKeep("Download failed: connection reset", Start.AddSeconds(2).UtcDateTime));
        Assert.True(throttle.ShouldKeep("Download appears stalled; retrying may be required.", Start.AddSeconds(3).UtcDateTime));
    }

    [Fact]
    public void DifferentItems_AreThrottledIndependently()
    {
        var throttle = new ManagedDownloadProgressLogThrottle();
        Assert.True(throttle.ShouldKeep(
            "Downloading AlmaLinux 10.2 DVD (x86_64)... 1.0% | 12 MB / 1200 MB | 8.0 MB/s | ETA 2m",
            Start.UtcDateTime));
        Assert.True(throttle.ShouldKeep(
            "Downloading kali-linux-2026.1.iso... 1.0% | 12 MB / 1200 MB | 8.0 MB/s | ETA 2m",
            Start.AddSeconds(1).UtcDateTime));
    }

    [Fact]
    public void Screenshot_Shape_ThrottlesRepeatedXubuntuTicks()
    {
        // Reproduces the v1.2.3 smoke screenshot: 4 ticks ~2-4s apart, no percent token,
        // each carrying a different MB count. Only the first must reach Full Logs.
        var throttle = new ManagedDownloadProgressLogThrottle();
        Assert.True(throttle.ShouldKeep(
            "[2026-05-27 13:42:15][INFO] Downloading Xubuntu 24.04.4 LTS Desktop (amd64)... 901 MB downloaded | 12.0 MB/s",
            Start.UtcDateTime));
        Assert.False(throttle.ShouldKeep(
            "[2026-05-27 13:42:17][INFO] Downloading Xubuntu 24.04.4 LTS Desktop (amd64)... 905 MB downloaded | 11.7 MB/s",
            Start.AddSeconds(2).UtcDateTime));
        Assert.False(throttle.ShouldKeep(
            "[2026-05-27 13:42:19][INFO] Downloading Xubuntu 24.04.4 LTS Desktop (amd64)... 928 MB downloaded | 11.7 MB/s",
            Start.AddSeconds(4).UtcDateTime));
        Assert.False(throttle.ShouldKeep(
            "[2026-05-27 13:42:22][INFO] Downloading Xubuntu 24.04.4 LTS Desktop (amd64)... 937 MB downloaded | 11.3 MB/s",
            Start.AddSeconds(7).UtcDateTime));
    }

    [Fact]
    public void NewFormatLine_OfTotal_IsThrottled()
    {
        // After this pass the line shape is "Downloading X... N GB of M GB | NN% | X.X MB/s current | ETA ...".
        var throttle = new ManagedDownloadProgressLogThrottle();
        Assert.True(throttle.ShouldKeep(
            "[2026-05-27 13:42:15][INFO] Downloading Xubuntu... 1.2 GB of 6.1 GB | 20% | 11.8 MB/s current | ETA 1m 5s",
            Start.UtcDateTime));
        Assert.False(throttle.ShouldKeep(
            "[2026-05-27 13:42:17][INFO] Downloading Xubuntu... 1.3 GB of 6.1 GB | 21% | 11.5 MB/s current | ETA 1m 4s",
            Start.AddSeconds(2).UtcDateTime));
    }

    [Fact]
    public void DownloadCompleteSummaryLine_IsAlwaysKept()
    {
        var throttle = new ManagedDownloadProgressLogThrottle();
        Assert.True(throttle.ShouldKeep(
            "[2026-05-27 13:42:15][INFO] Downloading Xubuntu... 1.2 GB of 6.1 GB | 20% | 11.8 MB/s current | ETA 1m 5s",
            Start.UtcDateTime));
        Assert.True(throttle.ShouldKeep(
            "[2026-05-27 13:50:57][OK] Download complete: Xubuntu 24.04.4 LTS Desktop (amd64) 6.1 GB in 8m 42s | avg 12.1 MB/s",
            Start.AddMinutes(9).UtcDateTime));
    }

    [Fact]
    public void Simulated_LongDownload_ProducesControlledCheckpointCount()
    {
        var throttle = new ManagedDownloadProgressLogThrottle();
        var kept = 0;
        for (var second = 0; second <= 600; second += 2)
        {
            var percent = Math.Min(100.0, second / 6.0);
            var line = $"Downloading AlmaLinux 10.2 DVD (x86_64)... {percent:0.0}% | {percent * 12:0} MB / 1200 MB | 8.0 MB/s | ETA 1m";
            if (throttle.ShouldKeep(line, Start.AddSeconds(second).UtcDateTime))
            {
                kept++;
            }
        }

        // 300 ticks total. We expect roughly: 1 (start) + ~10 (every 10%) + the 100% checkpoint.
        Assert.InRange(kept, 5, 35);
    }
}
