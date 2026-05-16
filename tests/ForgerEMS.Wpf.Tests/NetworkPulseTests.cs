using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using VentoyToolkitSetup.Wpf.Services.NetworkPulse;

namespace ForgerEMS.Wpf.Tests;

public sealed class NetworkPulseSettingsStoreTests
{
    private static readonly double[] SparklineTestSamples = { 0d, 1d, 0.4 };

    [Fact]
    public void Load_AppliesSafeDefaultsForIntervals()
    {
        var path = Path.Combine(Path.GetTempPath(), $"npulse-{Guid.NewGuid():N}.json");
        try
        {
            var store = new NetworkPulseSettingsStore(path);
            var s = store.Load();
            Assert.True(s.RefreshIntervalSeconds >= 5);
            Assert.True(s.SpeedProbeCadenceSeconds >= 30);
            Assert.False(s.UploadProbesEnabled);
            Assert.False(s.PauseOnMetered); // PauseOnMetered now defaults off — opt-in only.
            Assert.False(s.PauseOnBatterySaver);
            Assert.False(s.PauseDuringHostHeavyNetwork);
        }
        finally
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void ClassifyStatus_IcmpBlockedButReachable_IsUnknown()
    {
        var latency = new LatencyMonitorResult(null, null, 0, null, null, AnyIcmpSuccess: false);
        var status = NetworkPulseService.ClassifyStatusForTests(
            reach: true,
            latency,
            downloadMbps: null,
            latencyKind: NetworkPulseMeasurementKind.Unavailable);
        Assert.Equal(NetworkPulseStatus.Unknown, status);
    }

    [Fact]
    public void ClassifyStatus_NoReachNoIcmp_IsOffline()
    {
        var latency = new LatencyMonitorResult(null, null, 0, null, null, AnyIcmpSuccess: false);
        var status = NetworkPulseService.ClassifyStatusForTests(
            reach: false,
            latency,
            downloadMbps: null,
            latencyKind: NetworkPulseMeasurementKind.Unavailable);
        Assert.Equal(NetworkPulseStatus.Offline, status);
    }

    [Fact]
    public void ClassifyStatus_HighLoss_IsUnstable()
    {
        var latency = new LatencyMonitorResult(20, 5, 10, 2, 10, AnyIcmpSuccess: true);
        var status = NetworkPulseService.ClassifyStatusForTests(
            reach: true,
            latency,
            downloadMbps: 50,
            latencyKind: NetworkPulseMeasurementKind.Measured);
        Assert.Equal(NetworkPulseStatus.Unstable, status);
    }

    [Fact]
    public void Stabilizer_OfflinePath_DebouncesBeforeOfflineChip()
    {
        var s = new NetworkPulseStabilizer();
        var last = s.Smooth(
            NetworkPulseStatus.Offline,
            reach: false,
            icmpOk: false,
            pingMs: null,
            jitterMs: null,
            lossPercent: 0,
            downloadMbps: null,
            uploadMbps: null,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Unavailable);
        Assert.Equal("Reconnecting…", last.StatusChipText);

        for (var i = 0; i < 2; i++)
        {
            last = s.Smooth(
                NetworkPulseStatus.Offline,
                reach: false,
                icmpOk: false,
                pingMs: null,
                jitterMs: null,
                lossPercent: 0,
                downloadMbps: null,
                uploadMbps: null,
                NetworkPulseMeasurementKind.Unavailable,
                NetworkPulseMeasurementKind.Unavailable);
            Assert.Equal("Reconnecting…", last.StatusChipText);
        }

        last = s.Smooth(
            NetworkPulseStatus.Offline,
            reach: false,
            icmpOk: false,
            pingMs: null,
            jitterMs: null,
            lossPercent: 0,
            downloadMbps: null,
            uploadMbps: null,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Unavailable);
        Assert.Equal("Offline", last.StatusChipText);
    }

    [Fact]
    public void Stabilizer_UnstableRaw_HoldsOneSampleBeforeChipFlips()
    {
        var s = new NetworkPulseStabilizer();
        _ = s.Smooth(
            NetworkPulseStatus.Good,
            reach: true,
            icmpOk: true,
            pingMs: 20,
            jitterMs: 2,
            lossPercent: 0,
            downloadMbps: 100,
            uploadMbps: 10,
            NetworkPulseMeasurementKind.Measured,
            NetworkPulseMeasurementKind.Measured);

        var u1 = s.Smooth(
            NetworkPulseStatus.Unstable,
            reach: true,
            icmpOk: true,
            pingMs: 20,
            jitterMs: 90,
            lossPercent: 10,
            downloadMbps: 50,
            uploadMbps: 10,
            NetworkPulseMeasurementKind.Measured,
            NetworkPulseMeasurementKind.Measured);
        Assert.Equal("Stable", u1.StatusChipText);

        var u2 = s.Smooth(
            NetworkPulseStatus.Unstable,
            reach: true,
            icmpOk: true,
            pingMs: 20,
            jitterMs: 90,
            lossPercent: 10,
            downloadMbps: 50,
            uploadMbps: 10,
            NetworkPulseMeasurementKind.Measured,
            NetworkPulseMeasurementKind.Measured);
        Assert.Equal("Unstable", u2.StatusChipText);
    }

    [Fact]
    public void Stabilizer_SparkPingHistory_IsNormalizedAndWindowed()
    {
        var s = new NetworkPulseStabilizer();
        for (var i = 0; i < 30; i++)
        {
            _ = s.Smooth(
                NetworkPulseStatus.Good,
                reach: true,
                icmpOk: true,
                pingMs: 10 + i,
                jitterMs: 2,
                lossPercent: 0,
                downloadMbps: 100,
                uploadMbps: 10,
                NetworkPulseMeasurementKind.Measured,
                NetworkPulseMeasurementKind.Measured);
        }

        var last = s.Smooth(
            NetworkPulseStatus.Good,
            reach: true,
            icmpOk: true,
            pingMs: 40,
            jitterMs: 2,
            lossPercent: 0,
            downloadMbps: 100,
            uploadMbps: 10,
            NetworkPulseMeasurementKind.Measured,
            NetworkPulseMeasurementKind.Measured);
        Assert.InRange(last.SparkPingNormalized.Count, 2, 24);
        Assert.All(last.SparkPingNormalized, v => Assert.InRange(v, 0, 1));
    }

    [Fact]
    public void Reliability_Tier_IsConservativeForHardFailures()
    {
        Assert.Equal(NetworkPulseReliabilityTier.Excellent,
            NetworkPulseReliabilityScorer.Compute(true, true, 18, 3, 0, 0, 0));
        Assert.Equal(NetworkPulseReliabilityTier.Poor,
            NetworkPulseReliabilityScorer.Compute(false, false, null, null, 0, 3, 0));
    }

    [Fact]
    public void QualityVocabulary_IncludesCalmLabels()
    {
        Assert.Contains("Excellent", NetworkPulseQualityVocabulary.FormatPingLine(18, NetworkPulseMeasurementKind.Measured), StringComparison.Ordinal);
        Assert.Contains("Excellent", NetworkPulseQualityVocabulary.FormatJitterLine(3), StringComparison.Ordinal);
        Assert.Contains("Healthy", NetworkPulseQualityVocabulary.FormatLossLine(0), StringComparison.Ordinal);
    }

    [Fact]
    public void SparklineGeometry_BuildsForTwoOrMorePoints()
    {
        var g = NetworkPulseSparklineGeometryBuilder.BuildPingSparkline(SparklineTestSamples, 100, 28);
        Assert.NotNull(g);
        Assert.IsType<StreamGeometry>(g);
    }

    [Fact]
    public async Task WifiEnricher_NonWifi_ReturnsNull()
    {
        Assert.Null(await NetworkPulseWifiEnricher.TryGetContextLineAsync(NetworkPulseConnectionKind.Ethernet, CancellationToken.None));
    }

    [Fact]
    public void ReportWriter_TryAppendQuickReadLines_AppendsWhenFileExists()
    {
        var dir = Path.Combine(Path.GetTempPath(), "npulse-qr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "network-pulse-latest.json");
            File.WriteAllText(path, """{"summaryLine":"Network Pulse: Ethernet · Good · ping 18 ms · measured 300↓ / 20↑"}""");

            var lines = new List<string> { "Line1", "Line2" };
            NetworkPulseReportWriter.TryAppendQuickReadLines(lines, dir);

            Assert.Equal(4, lines.Count);
            Assert.Contains("Network Pulse:", lines[^1], StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
            }
        }
    }
}

public sealed class NetworkPulseInternetWidgetTextTests
{
    private static NetworkPulseSmoothedHeadline Sm(NetworkPulseStatus st = NetworkPulseStatus.Unknown) =>
        new(st, "Stable", null, null, Array.Empty<double>(), 0, 0);

    [Fact]
    public void FreshOnline_ShowsMeasuredDownload_AndUploadNotTested()
    {
        var snap = new NetworkPulseSnapshot(
            NetworkPulseStatus.Good,
            NetworkPulseConfidence.High,
            true,
            "A",
            NetworkPulseConnectionKind.Ethernet,
            null,
            18,
            2,
            0,
            120,
            null,
            NetworkPulseMeasurementKind.Measured,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Measured,
            1,
            1,
            DateTimeOffset.UtcNow,
            string.Empty,
            "n",
            "s");
        var (l1, l2, _) = NetworkPulseInternetWidgetText.Build(
            true,
            true,
            snap,
            Sm(NetworkPulseStatus.Good),
            new NetworkPulseLastKnownGood(),
            uploadProbesEnabled: false,
            consecutiveHardFailures: 0,
            TimeSpan.FromMinutes(10),
            DateTimeOffset.UtcNow);
        Assert.Contains("Online", l1, StringComparison.Ordinal);
        Assert.Contains("18 ms", l1, StringComparison.Ordinal);
        Assert.Contains("ping checked", l1, StringComparison.Ordinal);
        Assert.Contains("↓ 120 Mbps", l2, StringComparison.Ordinal);
        Assert.Contains(NetworkPulseInternetWidgetText.UploadNotTested, l2, StringComparison.Ordinal);
    }

    [Fact]
    public void SingleFailedDownload_KeepsLastGoodDownload_InLine2()
    {
        var last = new NetworkPulseLastKnownGood();
        last.AcceptDownload(94, DateTimeOffset.UtcNow.AddMinutes(-2));
        var snap = new NetworkPulseSnapshot(
            NetworkPulseStatus.Good,
            NetworkPulseConfidence.Medium,
            true,
            "A",
            NetworkPulseConnectionKind.Ethernet,
            null,
            20,
            2,
            0,
            null,
            null,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Measured,
            1,
            1,
            DateTimeOffset.UtcNow,
            string.Empty,
            "n",
            "s");
        var (_, l2, _) = NetworkPulseInternetWidgetText.Build(
            true,
            true,
            snap,
            Sm(NetworkPulseStatus.Good),
            last,
            false,
            consecutiveHardFailures: 0,
            TimeSpan.FromMinutes(10),
            DateTimeOffset.UtcNow);
        Assert.Contains("94", l2, StringComparison.Ordinal);
        Assert.Contains("last tested", l2, StringComparison.Ordinal);
        Assert.DoesNotContain("prior", l2, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DisabledUserSetting_ShowsDisabled_NotPaused()
    {
        var snap = new NetworkPulseSnapshot(
            NetworkPulseStatus.Paused,
            NetworkPulseConfidence.Low,
            false,
            "—",
            NetworkPulseConnectionKind.Unknown,
            null,
            null,
            null,
            null,
            null,
            null,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Unavailable,
            null,
            null,
            DateTimeOffset.UtcNow,
            "Network Pulse is turned off in Settings.",
            "x",
            "y");
        var (l1, _, _) = NetworkPulseInternetWidgetText.Build(
            false,
            true,
            snap,
            Sm(NetworkPulseStatus.Paused),
            new NetworkPulseLastKnownGood(),
            false,
            0,
            TimeSpan.FromMinutes(10),
            DateTimeOffset.UtcNow);
        Assert.Contains("Disabled", l1, StringComparison.Ordinal);
        Assert.DoesNotContain("Paused", l1, StringComparison.Ordinal);
    }

    [Fact]
    public void SpeedSanity_RejectsAbsurdGbps()
    {
        Assert.False(NetworkPulseSpeedSanity.IsPlausibleMeasuredMbps(50_000));
        Assert.True(NetworkPulseSpeedSanity.IsPlausibleMeasuredMbps(120));
    }

    [Fact]
    public void Startup_IsUnknownOrTesting_NotPaused()
    {
        // Default snapshot used at ViewModel init must not be Paused status.
        var snap = new NetworkPulseSnapshot(
            NetworkPulseStatus.Unknown,
            NetworkPulseConfidence.Low,
            false,
            "—",
            NetworkPulseConnectionKind.Unknown,
            null, null, null, null, null, null,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Unavailable,
            null, null,
            DateTimeOffset.UtcNow,
            string.Empty,
            "Starting…",
            "Lightweight probes only.");
        var (l1, _, _) = NetworkPulseInternetWidgetText.Build(
            true, true, snap,
            new NetworkPulseSmoothedHeadline(NetworkPulseStatus.Unknown, "Unknown", null, null, Array.Empty<double>(), 0, 0),
            new NetworkPulseLastKnownGood(),
            false, 0, TimeSpan.FromMinutes(10), DateTimeOffset.UtcNow);
        Assert.DoesNotContain("Paused", l1, StringComparison.Ordinal);
        Assert.DoesNotContain("Disabled", l1, StringComparison.Ordinal);
    }

    [Fact]
    public void SuccessfulPingAndDownload_ShowsOnline()
    {
        var snap = new NetworkPulseSnapshot(
            NetworkPulseStatus.Good,
            NetworkPulseConfidence.High,
            true,
            "A",
            NetworkPulseConnectionKind.Ethernet,
            null, 15, 2, 0, 80, null,
            NetworkPulseMeasurementKind.Measured,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Measured,
            1, 1,
            DateTimeOffset.UtcNow,
            string.Empty, "n", "s");
        var (l1, _, _) = NetworkPulseInternetWidgetText.Build(
            true, true, snap,
            new NetworkPulseSmoothedHeadline(NetworkPulseStatus.Good, "Stable", 80, null, Array.Empty<double>(), 0, 0),
            new NetworkPulseLastKnownGood(),
            false, 0, TimeSpan.FromMinutes(10), DateTimeOffset.UtcNow);
        Assert.Contains("Online", l1, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_MigratesOldAutoCreatedPauseOnMeteredDefault_ToFalse()
    {
        var path = Path.Combine(Path.GetTempPath(), $"npulse-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """
            {
              "Enabled": true,
              "ShowInHeader": true,
              "AutoTestSpeedLightly": true,
              "UploadProbesEnabled": false,
              "PauseOnMetered": true,
              "PauseOnBatterySaver": false,
              "PauseDuringHostHeavyNetwork": false,
              "Mode": 1,
              "RefreshIntervalSeconds": 30,
              "SpeedProbeCadenceSeconds": 300
            }
            """);

            var store = new NetworkPulseSettingsStore(path);
            var s = store.Load();

            Assert.False(s.PauseOnMetered);
            Assert.Equal(NetworkPulseSettingsStore.CurrentSchemaVersion, s.SettingsSchemaVersion);
        }
        finally
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void Load_DoesNotMigrateSchemaVersionedPauseOnMeteredChoice()
    {
        var path = Path.Combine(Path.GetTempPath(), $"npulse-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, $$"""
            {
              "SettingsSchemaVersion": {{NetworkPulseSettingsStore.CurrentSchemaVersion}},
              "Enabled": true,
              "ShowInHeader": true,
              "AutoTestSpeedLightly": true,
              "UploadProbesEnabled": false,
              "PauseOnMetered": true,
              "PauseOnBatterySaver": false,
              "PauseDuringHostHeavyNetwork": false,
              "Mode": 1,
              "RefreshIntervalSeconds": 30,
              "SpeedProbeCadenceSeconds": 300
            }
            """);

            var store = new NetworkPulseSettingsStore(path);
            var s = store.Load();

            Assert.True(s.PauseOnMetered);
        }
        finally
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void HealthyFreshPingWithPreviousSpeed_ShowsOnline_NotLimited()
    {
        var now = DateTimeOffset.UtcNow;
        var last = new NetworkPulseLastKnownGood();
        last.AcceptDownload(20.3, now.AddSeconds(-30));
        var snap = new NetworkPulseSnapshot(
            NetworkPulseStatus.Good,
            NetworkPulseConfidence.Medium,
            true,
            "A",
            NetworkPulseConnectionKind.Ethernet,
            null, 5, 1, 0, null, null,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Measured,
            1, 1,
            now,
            string.Empty, "Download throughput: not sampled this cycle (disabled, paused, or not yet due).", "s");
        var (l1, l2, _) = NetworkPulseInternetWidgetText.Build(
            true, true, snap,
            new NetworkPulseSmoothedHeadline(NetworkPulseStatus.Good, "Stable", 20.3, null, Array.Empty<double>(), 0, 0),
            last, false, 0, TimeSpan.FromMinutes(10), now);
        Assert.Contains("Online", l1, StringComparison.Ordinal);
        Assert.DoesNotContain("Limited", l1, StringComparison.Ordinal);
        Assert.Contains("ping checked 0s ago", l1, StringComparison.Ordinal);
        Assert.Contains("20.3 Mbps last tested 30s ago", l2, StringComparison.Ordinal);
    }

    [Fact]
    public void SpeedFailureWithPingOk_ShowsLimited_AndLastGoodSpeed()
    {
        var now = DateTimeOffset.UtcNow;
        var last = new NetworkPulseLastKnownGood();
        last.AcceptDownload(20.3, now.AddMinutes(-2));
        var snap = new NetworkPulseSnapshot(
            NetworkPulseStatus.Good,
            NetworkPulseConfidence.Medium,
            true,
            "A",
            NetworkPulseConnectionKind.Ethernet,
            null, 5, 1, 0, null, null,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Measured,
            1, 1,
            now,
            string.Empty, "Download sample failed this cycle; previous last-good values are preserved when available.", "s");
        var (l1, l2, _) = NetworkPulseInternetWidgetText.Build(
            true, true, snap,
            new NetworkPulseSmoothedHeadline(NetworkPulseStatus.Good, "Stable", null, null, Array.Empty<double>(), 0, 0),
            last, false, 0, TimeSpan.FromMinutes(10), now);
        Assert.Contains("Limited", l1, StringComparison.Ordinal);
        Assert.Contains("speed check failed", l1, StringComparison.Ordinal);
        Assert.Contains("20.3 Mbps last good 2m ago", l2, StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatedHardFailures_OfflineSurfaceAfterThreshold()
    {
        var snap = new NetworkPulseSnapshot(
            NetworkPulseStatus.Offline,
            NetworkPulseConfidence.Low,
            false,
            "A",
            NetworkPulseConnectionKind.Unknown,
            null, null, null, 0, null, null,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Unavailable,
            null, null,
            DateTimeOffset.UtcNow,
            string.Empty, "n", "s");
        var (l1, _, _) = NetworkPulseInternetWidgetText.Build(
            true, true, snap,
            new NetworkPulseSmoothedHeadline(NetworkPulseStatus.Offline, "Offline", null, null, Array.Empty<double>(), 6, 0),
            new NetworkPulseLastKnownGood(),
            false, consecutiveHardFailures: 6, TimeSpan.FromMinutes(10), DateTimeOffset.UtcNow);
        Assert.Contains("Offline", l1, StringComparison.Ordinal);
    }

    [Fact]
    public void PausePolicy_ShowsPaused_NotOffline()
    {
        var snap = new NetworkPulseSnapshot(
            NetworkPulseStatus.Paused,
            NetworkPulseConfidence.Low,
            false,
            "A",
            NetworkPulseConnectionKind.Unknown,
            null, null, null, null, null, null,
            NetworkPulseMeasurementKind.Paused,
            NetworkPulseMeasurementKind.Paused,
            NetworkPulseMeasurementKind.Paused,
            null, null,
            DateTimeOffset.UtcNow,
            "Paused while ForgerEMS is busy.",
            "n", "s");
        var (l1, _, _) = NetworkPulseInternetWidgetText.Build(
            true, true, snap,
            new NetworkPulseSmoothedHeadline(NetworkPulseStatus.Paused, "Paused", null, null, Array.Empty<double>(), 0, 0),
            new NetworkPulseLastKnownGood(),
            false, 0, TimeSpan.FromMinutes(10), DateTimeOffset.UtcNow);
        Assert.Contains("Paused", l1, StringComparison.Ordinal);
        Assert.DoesNotContain("Offline", l1, StringComparison.Ordinal);
        Assert.DoesNotContain("Disabled", l1, StringComparison.Ordinal);
    }

    [Fact]
    public void DisabledSetting_ShowsDisabled_NotPaused_AndNotOffline()
    {
        // userEnabled=false → "Internet: Disabled" regardless of snapshot status.
        var snap = new NetworkPulseSnapshot(
            NetworkPulseStatus.Paused,
            NetworkPulseConfidence.Low,
            false,
            "—",
            NetworkPulseConnectionKind.Unknown,
            null, null, null, null, null, null,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Unavailable,
            null, null,
            DateTimeOffset.UtcNow,
            "Network Pulse is turned off in Settings.",
            "x", "y");
        var (l1, _, _) = NetworkPulseInternetWidgetText.Build(
            false, true, snap,
            new NetworkPulseSmoothedHeadline(NetworkPulseStatus.Paused, "Paused", null, null, Array.Empty<double>(), 0, 0),
            new NetworkPulseLastKnownGood(),
            false, 0, TimeSpan.FromMinutes(10), DateTimeOffset.UtcNow);
        Assert.Contains("Disabled", l1, StringComparison.Ordinal);
        Assert.DoesNotContain("Paused", l1, StringComparison.Ordinal);
        Assert.DoesNotContain("Offline", l1, StringComparison.Ordinal);
    }

    [Fact]
    public void UploadNotTested_WhenProbesDisabled_Line2ContainsNotTested()
    {
        var snap = new NetworkPulseSnapshot(
            NetworkPulseStatus.Good,
            NetworkPulseConfidence.High,
            true,
            "A",
            NetworkPulseConnectionKind.Ethernet,
            null, 18, 2, 0, 100, null,
            NetworkPulseMeasurementKind.Measured,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Measured,
            1, 1,
            DateTimeOffset.UtcNow,
            string.Empty, "n", "s");
        var (_, l2, _) = NetworkPulseInternetWidgetText.Build(
            true, true, snap,
            new NetworkPulseSmoothedHeadline(NetworkPulseStatus.Good, "Stable", 100, null, Array.Empty<double>(), 0, 0),
            new NetworkPulseLastKnownGood(),
            uploadProbesEnabled: false,
            consecutiveHardFailures: 0,
            TimeSpan.FromMinutes(10), DateTimeOffset.UtcNow);
        Assert.Contains(NetworkPulseInternetWidgetText.UploadNotTested, l2, StringComparison.Ordinal);
        Assert.DoesNotContain("Mbps", l2.Split('·')[1], StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidThroughput_NaN_IsRejected()
    {
        Assert.False(NetworkPulseSpeedSanity.IsPlausibleMeasuredMbps(double.NaN));
        Assert.False(NetworkPulseSpeedSanity.IsPlausibleMeasuredMbps(double.PositiveInfinity));
        Assert.False(NetworkPulseSpeedSanity.IsPlausibleMeasuredMbps(0));
        Assert.False(NetworkPulseSpeedSanity.IsPlausibleMeasuredMbps(-1));
    }

    [Fact]
    public void SingleFailedCycle_KeepsLastGoodPing_InLine1()
    {
        var last = new NetworkPulseLastKnownGood();
        last.AcceptPing(22, DateTimeOffset.UtcNow.AddSeconds(-5));
        // Current cycle: ICMP unavailable (transient failure), but reachable.
        var snap = new NetworkPulseSnapshot(
            NetworkPulseStatus.Unknown,
            NetworkPulseConfidence.Low,
            true,
            "A",
            NetworkPulseConnectionKind.Ethernet,
            null, null, null, 0, null, null,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Unavailable,
            null, null,
            DateTimeOffset.UtcNow,
            string.Empty, "n", "s");
        var (l1, _, _) = NetworkPulseInternetWidgetText.Build(
            true, true, snap,
            new NetworkPulseSmoothedHeadline(NetworkPulseStatus.Unknown, "Unknown", null, null, Array.Empty<double>(), 0, 0),
            last, false, 0, TimeSpan.FromMinutes(10), DateTimeOffset.UtcNow);
        Assert.Contains("22", l1, StringComparison.Ordinal);
    }

    [Fact]
    public void StaleFreshnessWindow_ShowsStaleLabel()
    {
        var last = new NetworkPulseLastKnownGood();
        var oldTime = DateTimeOffset.UtcNow.AddMinutes(-20);
        last.AcceptPing(30, oldTime);
        last.AcceptDownload(50, oldTime);
        var snap = new NetworkPulseSnapshot(
            NetworkPulseStatus.Good,
            NetworkPulseConfidence.Low,
            true,
            "A",
            NetworkPulseConnectionKind.Ethernet,
            null, null, null, 0, null, null,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Unavailable,
            null, null,
            DateTimeOffset.UtcNow,
            string.Empty, "n", "s");
        var (l1, _, _) = NetworkPulseInternetWidgetText.Build(
            true, true, snap,
            new NetworkPulseSmoothedHeadline(NetworkPulseStatus.Good, "Stable", null, null, Array.Empty<double>(), 0, 0),
            last, false, 0,
            freshnessStaleAfter: TimeSpan.FromMinutes(5),
            utcNow: DateTimeOffset.UtcNow);
        Assert.Contains("Stale", l1, StringComparison.Ordinal);
    }

    [Fact]
    public void WidgetText_DoesNotExposeUnavailableOrPrior()
    {
        var now = DateTimeOffset.UtcNow;
        var snap = new NetworkPulseSnapshot(
            NetworkPulseStatus.Good,
            NetworkPulseConfidence.High,
            true,
            "A",
            NetworkPulseConnectionKind.Ethernet,
            null, 18, 2, 0, 100, null,
            NetworkPulseMeasurementKind.Measured,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Measured,
            1, 1,
            now,
            string.Empty, "n", "s");
        var (l1, l2, l3) = NetworkPulseInternetWidgetText.Build(
            true, true, snap,
            new NetworkPulseSmoothedHeadline(NetworkPulseStatus.Good, "Stable", 100, null, Array.Empty<double>(), 0, 0),
            new NetworkPulseLastKnownGood(),
            false, 0, TimeSpan.FromMinutes(10), now);
        var text = l1 + l2 + l3;
        Assert.DoesNotContain("Unavailable", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prior", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stabilizer_PauseThenResume_ConsecutiveFailuresResetIndependently()
    {
        // Verify that resetting the stabilizer (as done on pause) zeroes internal streak state.
        var s = new NetworkPulseStabilizer();
        // Simulate 3 offline cycles to build up fail streak.
        for (var i = 0; i < 3; i++)
        {
            s.Smooth(NetworkPulseStatus.Offline, false, false, null, null, 0,
                null, null, NetworkPulseMeasurementKind.Unavailable, NetworkPulseMeasurementKind.Unavailable);
        }
        // Reset (happens on pause) then a good cycle — should produce "Stable" immediately.
        s.Reset();
        var result = s.Smooth(NetworkPulseStatus.Good, true, true, 20, 2, 0,
            100, null, NetworkPulseMeasurementKind.Measured, NetworkPulseMeasurementKind.Unavailable);
        Assert.Equal("Stable", result.StatusChipText);
    }

    [Fact]
    public void LastKnownGood_AcceptPing_RejectsImpossibleValues()
    {
        var last = new NetworkPulseLastKnownGood();
        last.AcceptPing(-1, DateTimeOffset.UtcNow);
        last.AcceptPing(0, DateTimeOffset.UtcNow);
        last.AcceptPing(6000, DateTimeOffset.UtcNow);
        Assert.Null(last.PingMs);
    }

    [Fact]
    public void LastKnownGood_AcceptDownload_RejectsZeroAndNaN()
    {
        var last = new NetworkPulseLastKnownGood();
        last.AcceptDownload(0, DateTimeOffset.UtcNow);
        last.AcceptDownload(double.NaN, DateTimeOffset.UtcNow);
        last.AcceptDownload(double.PositiveInfinity, DateTimeOffset.UtcNow);
        Assert.Null(last.DownloadMbps);
    }

    [Fact]
    public void PauseOnMetered_DefaultIsFalse()
    {
        var s = new NetworkPulseSettings();
        Assert.False(s.PauseOnMetered);
    }

    [Fact]
    public void DefaultSettings_PauseOnBatterySaver_AndPauseDuringHost_AreFalse()
    {
        var s = new NetworkPulseSettings();
        Assert.False(s.PauseOnBatterySaver);
        Assert.False(s.PauseDuringHostHeavyNetwork);
    }

    [Fact]
    public void DefaultSettings_UploadProbesDisabled_AutoTestEnabled()
    {
        var s = new NetworkPulseSettings();
        Assert.False(s.UploadProbesEnabled);
        Assert.True(s.AutoTestSpeedLightly);
    }

    [Fact]
    public void EmaSmoothing_DoesNotUpdateOnNonMeasuredCycle()
    {
        var s = new NetworkPulseStabilizer();
        var first = s.Smooth(NetworkPulseStatus.Good, true, true, 20, 2, 0,
            100, null, NetworkPulseMeasurementKind.Measured, NetworkPulseMeasurementKind.Unavailable);
        var emaAfterFirst = first.SmoothedDownMbps;
        Assert.True(emaAfterFirst is > 0);

        // Second cycle: download not measured (Unavailable). EMA must not change to null or 0.
        var second = s.Smooth(NetworkPulseStatus.Good, true, true, 20, 2, 0,
            null, null, NetworkPulseMeasurementKind.Unavailable, NetworkPulseMeasurementKind.Unavailable);
        Assert.Equal(emaAfterFirst, second.SmoothedDownMbps);
    }
}
