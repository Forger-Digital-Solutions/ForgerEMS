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
            // v1.2.3-preview.1: UploadProbesEnabled flipped to default-on so a normal cycle
            // attempts a real upload sample (the widget can no longer honestly report
            // "Up: not tested" as a normal end-of-cycle state).
            Assert.True(s.UploadProbesEnabled);
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
    public void ClassifyStatus_IcmpBlockedButReachable_IsGood()
    {
        var latency = new LatencyMonitorResult(null, null, 0, null, null, AnyIcmpSuccess: false);
        var status = NetworkPulseService.ClassifyStatusForTests(
            reach: true,
            latency,
            downloadMbps: null,
            latencyKind: NetworkPulseMeasurementKind.Unavailable);
        Assert.Equal(NetworkPulseStatus.Good, status);
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
    public void ClassifyStatus_DnsFailureButHttpsSuccess_IsGood()
    {
        var latency = new LatencyMonitorResult(null, null, 0, null, null, AnyIcmpSuccess: false);
        var status = NetworkPulseService.ClassifyStatusForTests(
            hasUsableRoute: true,
            httpsReachable: true,
            dnsSucceeded: false,
            latency,
            downloadMbps: null,
            latencyKind: NetworkPulseMeasurementKind.Measured);
        Assert.Equal(NetworkPulseStatus.Good, status);
    }

    [Fact]
    public void ClassifyStatus_HttpsFailureWithRoute_IsLimited()
    {
        var latency = new LatencyMonitorResult(null, null, 0, null, null, AnyIcmpSuccess: false);
        var status = NetworkPulseService.ClassifyStatusForTests(
            hasUsableRoute: true,
            httpsReachable: false,
            dnsSucceeded: false,
            latency,
            downloadMbps: null,
            latencyKind: NetworkPulseMeasurementKind.Unavailable);
        Assert.Equal(NetworkPulseStatus.Limited, status);
    }

    [Fact]
    public void EnvironmentReader_ApipaAndVirtualHelpers_AreConservative()
    {
        Assert.False(NetworkPulseEnvironmentReader.IsUsableIpv4Address(System.Net.IPAddress.Parse("169.254.10.20")));
        Assert.True(NetworkPulseEnvironmentReader.IsUsableIpv4Address(System.Net.IPAddress.Parse("192.168.1.20")));
        Assert.True(NetworkPulseEnvironmentReader.IsVirtualAdapter(
            "vEthernet (WSL)",
            "Hyper-V Virtual Ethernet Adapter",
            System.Net.NetworkInformation.NetworkInterfaceType.Ethernet));
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
    public void FreshOnline_UploadProbesOff_ShowsMeasuredDownload_AndLegacyUploadNotTested()
    {
        // Legacy compatibility: when callers pass uploadProbesEnabled:false (the pre-v1.2.3
        // default), the widget still emits the "Up: not tested" string so existing
        // settings/UX behaviour is preserved for users who opted out of upload probes.
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
        var (l1, l2, l3) = NetworkPulseInternetWidgetText.Build(
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
        Assert.Contains("18 ms", l2, StringComparison.Ordinal);
        Assert.Contains("Checked", l3, StringComparison.Ordinal);
        Assert.Contains("Down: 120 Mbps", l2, StringComparison.Ordinal);
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
        Assert.Contains("last measured", l2, StringComparison.Ordinal);
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
        var (l1, l2, l3) = NetworkPulseInternetWidgetText.Build(
            true, true, snap,
            new NetworkPulseSmoothedHeadline(NetworkPulseStatus.Good, "Stable", 20.3, null, Array.Empty<double>(), 0, 0),
            last, false, 0, TimeSpan.FromMinutes(10), now);
        Assert.Contains("Online", l1, StringComparison.Ordinal);
        Assert.DoesNotContain("Limited", l1, StringComparison.Ordinal);
        Assert.Contains("Checked 0s ago", l3, StringComparison.Ordinal);
        Assert.Contains("20.3 Mbps (last measured 30s ago)", l2, StringComparison.Ordinal);
    }

    [Fact]
    public void SpeedFailureWithPingOk_ShowsOnline_AndLastGoodSpeed()
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
        var (l1, l2, l3) = NetworkPulseInternetWidgetText.Build(
            true, true, snap,
            new NetworkPulseSmoothedHeadline(NetworkPulseStatus.Good, "Stable", null, null, Array.Empty<double>(), 0, 0),
            last, false, 0, TimeSpan.FromMinutes(10), now);
        Assert.Contains("Online", l1, StringComparison.Ordinal);
        Assert.DoesNotContain("Limited", l1, StringComparison.Ordinal);
        Assert.Contains("Speed sample unavailable", l3, StringComparison.Ordinal);
        Assert.Contains("20.3 Mbps (last good 2m ago)", l2, StringComparison.Ordinal);
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
    public void LimitedStatus_ShowsLimitedOnlyForProbeSupportedIssue()
    {
        var snap = new NetworkPulseSnapshot(
            NetworkPulseStatus.Limited,
            NetworkPulseConfidence.Low,
            false,
            "Ethernet",
            NetworkPulseConnectionKind.Ethernet,
            null, null, null, 0, null, null,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Unavailable,
            null, null,
            DateTimeOffset.UtcNow,
            string.Empty,
            "HTTPS reachability failed while an active route exists.",
            "s");
        var (l1, _, l3) = NetworkPulseInternetWidgetText.Build(
            true, true, snap,
            new NetworkPulseSmoothedHeadline(NetworkPulseStatus.Limited, "Limited", null, null, Array.Empty<double>(), 0, 0),
            new NetworkPulseLastKnownGood(),
            false, consecutiveHardFailures: 0, TimeSpan.FromMinutes(10), DateTimeOffset.UtcNow);
        Assert.Contains("Limited", l1, StringComparison.Ordinal);
        // New softened wording — sustained Limited shows a calmer, user-facing reason without
        // raw probe internals. Diagnostics block in the popup still carries technical detail.
        Assert.Contains("failed for several cycles", l3, StringComparison.Ordinal);
        Assert.DoesNotContain("mixed or failing", l3, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewModel_ApplyTestingThenOnline_TogglesIsMeasuringAndObservableValues()
    {
        var path = Path.Combine(Path.GetTempPath(), $"npulse-vm-{Guid.NewGuid():N}.json");
        try
        {
            var vm = new VentoyToolkitSetup.Wpf.ViewModels.NetworkPulseViewModel(new NetworkPulseSettingsStore(path));
            vm.ApplyTestingStatus(NetworkPulseStatus.Testing);
            Assert.True(vm.IsMeasuring);
            Assert.Equal("Measuring", vm.Status);

            var now = DateTimeOffset.UtcNow;
            var snap = new NetworkPulseSnapshot(
                NetworkPulseStatus.Good,
                NetworkPulseConfidence.High,
                true,
                "Ethernet",
                NetworkPulseConnectionKind.Ethernet,
                null, 18, 2, 0, 100, null,
                NetworkPulseMeasurementKind.Measured,
                NetworkPulseMeasurementKind.Unavailable,
                NetworkPulseMeasurementKind.Measured,
                1, 1,
                now,
                string.Empty,
                "n",
                "s");
            var (l1, l2, l3) = NetworkPulseInternetWidgetText.Build(
                true, true, snap,
                new NetworkPulseSmoothedHeadline(NetworkPulseStatus.Good, "Stable", 100, null, Array.Empty<double>(), 0, 0),
                new NetworkPulseLastKnownGood(),
                false, 0, TimeSpan.FromMinutes(10), now);
            vm.Apply(new NetworkPulseUiState(
                snap,
                new NetworkPulseSmoothedHeadline(NetworkPulseStatus.Good, "Stable", 100, null, Array.Empty<double>(), 0, 0),
                NetworkPulseReliabilityTier.Good,
                "Healthy",
                null,
                null,
                "No smoothing issue.",
                "summary",
                l1,
                l2,
                l3));

            Assert.False(vm.IsMeasuring);
            Assert.Equal("Online", vm.Status);
            Assert.Equal(18, vm.LatencyMs);
            Assert.Equal(100, vm.DownloadMbps);
            Assert.Null(vm.UploadMbps);
            Assert.Equal(now, vm.LastCheckedAt);
            Assert.Equal(string.Empty, vm.LastErrorCategory);
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
    public void SingleFailedCycle_KeepsLastGoodPing_InMetricsLine()
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
        var (_, l2, _) = NetworkPulseInternetWidgetText.Build(
            true, true, snap,
            new NetworkPulseSmoothedHeadline(NetworkPulseStatus.Unknown, "Unknown", null, null, Array.Empty<double>(), 0, 0),
            last, false, 0, TimeSpan.FromMinutes(10), DateTimeOffset.UtcNow);
        Assert.Contains("22", l2, StringComparison.Ordinal);
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
    public void DefaultSettings_UploadProbesEnabledByDefault_AutoTestEnabled()
    {
        // v1.2.3-preview.1: a normal Network Pulse cycle now attempts ping + download + upload
        // by default so the header can show a real Up Mbps reading.
        var s = new NetworkPulseSettings();
        Assert.True(s.UploadProbesEnabled);
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

// New v1.2.1 UX hardening tests: HTTPS verification mismatch must not be presented as alarming
// when ICMP / throughput / DNS still indicate the internet is usable. Includes VPN / custom-DNS
// scenarios that previously over-triggered the "Limited" / "Fair" wording.
public sealed class NetworkPulseHumanPriorityModelTests
{
    private static LatencyMonitorResult HealthyLatency(double ping = 18) =>
        new(ping, JitterMs: 2, PacketLossPercent: 0, GatewayPingMs: 1, DnsLookupMs: 1, AnyIcmpSuccess: true);

    [Fact]
    public void ClassifyStatus_HttpsFailButIcmpAndThroughputOk_StaysGood_OnFirstCycle()
    {
        // VPN / custom-DNS / content-filter scenario: HTTPS verification probe fails this cycle,
        // but ICMP works and a throughput sample exists. Status must NOT immediately downgrade.
        var status = NetworkPulseService.ClassifyStatusForTests(
            hasUsableRoute: true,
            httpsReachable: false,
            dnsSucceeded: true,
            HealthyLatency(),
            downloadMbps: 80,
            latencyKind: NetworkPulseMeasurementKind.Measured,
            httpsOnlyFailureStreak: 0);

        Assert.Equal(NetworkPulseStatus.Good, status);
    }

    [Fact]
    public void ClassifyStatus_HttpsFailButIcmpOk_DoesNotDowngradeBeforeStreakThreshold()
    {
        var status = NetworkPulseService.ClassifyStatusForTests(
            hasUsableRoute: true,
            httpsReachable: false,
            dnsSucceeded: true,
            HealthyLatency(),
            downloadMbps: 80,
            latencyKind: NetworkPulseMeasurementKind.Measured,
            httpsOnlyFailureStreak: 2);

        Assert.Equal(NetworkPulseStatus.Good, status);
    }

    [Fact]
    public void ClassifyStatus_HttpsFailSustained_DowngradesToLimitedAtThreshold()
    {
        // Only after sustained HTTPS-only failure (>= 3 cycles) do we accept the Limited verdict.
        var status = NetworkPulseService.ClassifyStatusForTests(
            hasUsableRoute: true,
            httpsReachable: false,
            dnsSucceeded: true,
            HealthyLatency(),
            downloadMbps: 80,
            latencyKind: NetworkPulseMeasurementKind.Measured,
            httpsOnlyFailureStreak: 3);

        Assert.Equal(NetworkPulseStatus.Limited, status);
    }

    [Fact]
    public void ClassifyStatus_NoUsabilityEvidenceAtAll_IsLimitedImmediately()
    {
        // Route exists but nothing actually responds — even a single cycle is enough to call this
        // Limited because there is no other usability evidence to lean on.
        var dead = new LatencyMonitorResult(null, null, 0, null, null, AnyIcmpSuccess: false);
        var status = NetworkPulseService.ClassifyStatusForTests(
            hasUsableRoute: true,
            httpsReachable: false,
            dnsSucceeded: false,
            dead,
            downloadMbps: null,
            latencyKind: NetworkPulseMeasurementKind.Unavailable,
            httpsOnlyFailureStreak: 0);

        Assert.Equal(NetworkPulseStatus.Limited, status);
    }

    [Fact]
    public void Surface_HttpsFailWithIcmpOk_ShowsPartialCheck_NotLimited()
    {
        // v1.2.3-preview.1: was "Online — checks inconsistent" — now rendered as "Partial check".
        // The cycle is honestly incomplete (HTTPS reachability didn't confirm) and the user
        // should not be misled by a clean Online label.
        var snap = new NetworkPulseSnapshot(
            NetworkPulseStatus.Good,
            NetworkPulseConfidence.Medium,
            InternetReachable: false,             // HTTPS probe failed
            "Ethernet",
            NetworkPulseConnectionKind.Ethernet,
            null,
            PingMs: 6,
            JitterMs: 1,
            PacketLossPercent: 0,
            DownloadMbps: 31,
            UploadMbps: null,
            NetworkPulseMeasurementKind.Measured,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Measured,
            GatewayPingMs: 1,
            DnsLookupMs: 1,
            DateTimeOffset.UtcNow,
            string.Empty,
            "n",
            "s");
        var (l1, _, l3) = NetworkPulseInternetWidgetText.Build(
            userEnabled: true,
            showInHeader: true,
            snap,
            new NetworkPulseSmoothedHeadline(NetworkPulseStatus.Good, "Stable", 31, null, Array.Empty<double>(), 0, 0),
            new NetworkPulseLastKnownGood(),
            uploadProbesEnabled: false,
            consecutiveHardFailures: 0,
            TimeSpan.FromMinutes(10),
            DateTimeOffset.UtcNow);

        Assert.Contains("Partial check", l1, StringComparison.Ordinal);
        Assert.DoesNotContain("Internet: Limited", l1, StringComparison.Ordinal);
        Assert.DoesNotContain("checks inconsistent", l1, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mixed or failing", l3, StringComparison.Ordinal);
    }

    [Fact]
    public void Reliability_HttpsFailWithLowLossAndCalmLatency_StaysGoodOrExcellent_NotFair()
    {
        // 6ms ping, 0% loss, ICMP works, HTTPS probe failed once — must NOT collapse to Fair.
        var tier = NetworkPulseReliabilityScorer.Compute(
            reach: false,
            icmpOk: true,
            pingMs: 6,
            jitterMs: 1,
            lossPercent: 0,
            recentReachFailures: 1,
            recentUnstableRawStreak: 0);

        Assert.True(
            tier is NetworkPulseReliabilityTier.Excellent or NetworkPulseReliabilityTier.Good,
            $"Expected Excellent/Good but got {tier}");
    }

    [Fact]
    public void Reliability_SustainedHardFailure_StillReportsPoor()
    {
        var tier = NetworkPulseReliabilityScorer.Compute(
            reach: false,
            icmpOk: false,
            pingMs: null,
            jitterMs: null,
            lossPercent: 0,
            recentReachFailures: 3,
            recentUnstableRawStreak: 0);

        Assert.Equal(NetworkPulseReliabilityTier.Poor, tier);
    }

    [Fact]
    public void Reliability_TierLabel_RemapsFairToCalmerLanguage()
    {
        // Fair enum value is retained for stability, but the user-facing label is softer.
        Assert.Equal("Usable", NetworkPulseReliabilityScorer.TierLabel(NetworkPulseReliabilityTier.Fair));
        Assert.Equal("Degraded", NetworkPulseReliabilityScorer.TierLabel(NetworkPulseReliabilityTier.Poor));
    }

    [Fact]
    public void Stabilizer_TransientLimitedAfterGood_HoldsPreviousChip()
    {
        var s = new NetworkPulseStabilizer();
        _ = s.Smooth(NetworkPulseStatus.Good, reach: true, icmpOk: true, pingMs: 18, jitterMs: 2, lossPercent: 0,
            downloadMbps: 80, uploadMbps: null, NetworkPulseMeasurementKind.Measured, NetworkPulseMeasurementKind.Unavailable);

        // First Limited cycle while ICMP still works → must NOT flash the alarming chip.
        var first = s.Smooth(NetworkPulseStatus.Limited, reach: false, icmpOk: true, pingMs: 18, jitterMs: 2, lossPercent: 0,
            downloadMbps: 80, uploadMbps: null, NetworkPulseMeasurementKind.Measured, NetworkPulseMeasurementKind.Unavailable);

        Assert.NotEqual("Limited", first.StatusChipText);
    }

    [Fact]
    public void Stabilizer_SustainedLimited_EventuallyShowsLimitedChip()
    {
        var s = new NetworkPulseStabilizer();
        _ = s.Smooth(NetworkPulseStatus.Good, true, true, 18, 2, 0, 80, null,
            NetworkPulseMeasurementKind.Measured, NetworkPulseMeasurementKind.Unavailable);

        NetworkPulseSmoothedHeadline last = default!;
        for (var i = 0; i < 3; i++)
        {
            last = s.Smooth(NetworkPulseStatus.Limited, reach: false, icmpOk: true, pingMs: 18, jitterMs: 2,
                lossPercent: 0, downloadMbps: 80, uploadMbps: null,
                NetworkPulseMeasurementKind.Measured, NetworkPulseMeasurementKind.Unavailable);
        }

        Assert.Equal("Limited", last.StatusChipText);
    }

    [Fact]
    public void Surface_CaptiveSuspectedWithHealthySignals_ShowsPartialCheck()
    {
        // v1.2.3-preview.1: was "Online — checks inconsistent" — now rendered as "Partial check".
        // captiveSuspected currently sets InternetReachable=false on the snapshot — same shape
        // as a content-filter dropping the probe.
        var snap = new NetworkPulseSnapshot(
            NetworkPulseStatus.Good,
            NetworkPulseConfidence.Medium,
            InternetReachable: false,
            "Wi-Fi",
            NetworkPulseConnectionKind.WiFi,
            null,
            PingMs: 22,
            JitterMs: 3,
            PacketLossPercent: 0,
            DownloadMbps: 45,
            UploadMbps: null,
            NetworkPulseMeasurementKind.Measured,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Measured,
            1, 1,
            DateTimeOffset.UtcNow,
            string.Empty,
            "HTTPS verification probe returned unexpected content (common with VPNs, custom DNS, or content filters); other usability signals still indicate the internet is reachable.",
            "s");

        var (l1, _, _) = NetworkPulseInternetWidgetText.Build(
            true, true, snap,
            new NetworkPulseSmoothedHeadline(NetworkPulseStatus.Good, "Stable", 45, null, Array.Empty<double>(), 0, 0),
            new NetworkPulseLastKnownGood(),
            false, 0, TimeSpan.FromMinutes(10), DateTimeOffset.UtcNow);

        Assert.Contains("Partial check", l1, StringComparison.Ordinal);
        Assert.DoesNotContain("Internet: Limited", l1, StringComparison.Ordinal);
        Assert.DoesNotContain("checks inconsistent", l1, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Surface_TrueOffline_StillReportsOffline()
    {
        // Regression guard: the softening must NOT mask a real outage.
        var snap = new NetworkPulseSnapshot(
            NetworkPulseStatus.Offline,
            NetworkPulseConfidence.Low,
            InternetReachable: false,
            "—",
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
}

// v1.2.3 hardening: strong throughput + healthy ping + low loss must veto the streak-based
// downgrade to "Limited" even when Windows verification probes fail for many cycles. Mirrors
// the user-reported v1.2.3-preview.1 scenario: 18.5 Mbps measured, 6 ms ping, 0% loss, but the
// header dropped to "Internet: Limited" because NCSI / captive-portal checks were inconsistent.
public sealed class NetworkPulseStrongEvidenceTests
{
    private static LatencyMonitorResult HealthyLatency(double ping = 6, double loss = 0) =>
        new(ping, JitterMs: 1, PacketLossPercent: loss, GatewayPingMs: 1, DnsLookupMs: 1, AnyIcmpSuccess: true);

    [Fact]
    public void ClassifyStatus_StrongEvidence_VetoesLimitedDowngrade_AtThreshold()
    {
        // download 18.5 Mbps measured this cycle, 6 ms ping, 0 loss, HTTPS verification failing
        // for 3 cycles — must NOT escalate to Limited.
        var status = NetworkPulseService.ClassifyStatusForTests(
            hasUsableRoute: true,
            httpsReachable: false,
            dnsSucceeded: true,
            HealthyLatency(),
            downloadMbps: 18.5,
            latencyKind: NetworkPulseMeasurementKind.Measured,
            httpsOnlyFailureStreak: 3,
            hasStrongUsabilityEvidence: true);

        Assert.NotEqual(NetworkPulseStatus.Limited, status);
        Assert.Equal(NetworkPulseStatus.Good, status);
    }

    [Fact]
    public void ClassifyStatus_StrongEvidence_VetoesLimitedDowngrade_AtLargeStreak()
    {
        // Even after many cycles of HTTPS-only failure, strong throughput must hold the line.
        var status = NetworkPulseService.ClassifyStatusForTests(
            hasUsableRoute: true,
            httpsReachable: false,
            dnsSucceeded: true,
            HealthyLatency(),
            downloadMbps: 25,
            latencyKind: NetworkPulseMeasurementKind.Measured,
            httpsOnlyFailureStreak: 12,
            hasStrongUsabilityEvidence: true);

        Assert.NotEqual(NetworkPulseStatus.Limited, status);
    }

    [Fact]
    public void ClassifyStatus_WeakEvidence_StillDowngradesAtThreshold()
    {
        // ICMP only, no fresh throughput, HTTPS failing for 3 cycles — Limited path stays open.
        var status = NetworkPulseService.ClassifyStatusForTests(
            hasUsableRoute: true,
            httpsReachable: false,
            dnsSucceeded: true,
            HealthyLatency(),
            downloadMbps: null,
            latencyKind: NetworkPulseMeasurementKind.Measured,
            httpsOnlyFailureStreak: 3,
            hasStrongUsabilityEvidence: false);

        Assert.Equal(NetworkPulseStatus.Limited, status);
    }

    [Fact]
    public void StrongEvidence_FreshDownloadAndHealthyLatency_IsTrue()
    {
        var now = DateTimeOffset.UtcNow;
        var ok = NetworkPulseService.HasStrongUsabilityEvidenceForTests(
            currentDownloadMbps: 18.5,
            downloadKind: NetworkPulseMeasurementKind.Measured,
            lastGoodDownloadMbps: null,
            lastGoodDownloadUtc: default,
            effectivePingMs: 6,
            latencyKind: NetworkPulseMeasurementKind.Measured,
            latency: HealthyLatency(),
            utcNow: now);

        Assert.True(ok);
    }

    [Fact]
    public void StrongEvidence_LastGoodDownloadWithinFreshness_IsTrue()
    {
        var now = DateTimeOffset.UtcNow;
        var ok = NetworkPulseService.HasStrongUsabilityEvidenceForTests(
            currentDownloadMbps: null,
            downloadKind: NetworkPulseMeasurementKind.Unavailable,
            lastGoodDownloadMbps: 18.5,
            lastGoodDownloadUtc: now.AddMinutes(-2),
            effectivePingMs: 6,
            latencyKind: NetworkPulseMeasurementKind.Measured,
            latency: HealthyLatency(),
            utcNow: now);

        Assert.True(ok);
    }

    [Fact]
    public void StrongEvidence_StaleLastGoodDownload_IsFalse()
    {
        var now = DateTimeOffset.UtcNow;
        var ok = NetworkPulseService.HasStrongUsabilityEvidenceForTests(
            currentDownloadMbps: null,
            downloadKind: NetworkPulseMeasurementKind.Unavailable,
            lastGoodDownloadMbps: 18.5,
            lastGoodDownloadUtc: now.AddMinutes(-30),
            effectivePingMs: 6,
            latencyKind: NetworkPulseMeasurementKind.Measured,
            latency: HealthyLatency(),
            utcNow: now);

        Assert.False(ok);
    }

    [Fact]
    public void StrongEvidence_HighPacketLoss_IsFalse()
    {
        var now = DateTimeOffset.UtcNow;
        var ok = NetworkPulseService.HasStrongUsabilityEvidenceForTests(
            currentDownloadMbps: 18.5,
            downloadKind: NetworkPulseMeasurementKind.Measured,
            lastGoodDownloadMbps: null,
            lastGoodDownloadUtc: default,
            effectivePingMs: 6,
            latencyKind: NetworkPulseMeasurementKind.Measured,
            latency: HealthyLatency(loss: 8),
            utcNow: now);

        Assert.False(ok);
    }

    [Fact]
    public void StrongEvidence_HighPing_IsFalse()
    {
        var now = DateTimeOffset.UtcNow;
        var ok = NetworkPulseService.HasStrongUsabilityEvidenceForTests(
            currentDownloadMbps: 18.5,
            downloadKind: NetworkPulseMeasurementKind.Measured,
            lastGoodDownloadMbps: null,
            lastGoodDownloadUtc: default,
            effectivePingMs: 400,
            latencyKind: NetworkPulseMeasurementKind.Measured,
            latency: HealthyLatency(ping: 400),
            utcNow: now);

        Assert.False(ok);
    }

    [Fact]
    public void Surface_StrongEvidenceMultiCycle_RendersPartialCheck_NotLimited()
    {
        // Status stays Good thanks to the strong-evidence override. InternetReachable=false signals
        // the HTTPS verification mismatch — the v1.2.3 widget surfaces this as "Partial check"
        // (was "Online — checks inconsistent"). It must NOT downgrade to "Limited".
        var snap = new NetworkPulseSnapshot(
            NetworkPulseStatus.Good,
            NetworkPulseConfidence.Medium,
            InternetReachable: false,
            "Ethernet",
            NetworkPulseConnectionKind.Ethernet,
            null,
            PingMs: 6,
            JitterMs: 1,
            PacketLossPercent: 0,
            DownloadMbps: 18.5,
            UploadMbps: null,
            NetworkPulseMeasurementKind.Measured,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Measured,
            GatewayPingMs: 1,
            DnsLookupMs: 1,
            DateTimeOffset.UtcNow,
            string.Empty,
            "n",
            "s");
        var (l1, _, l3) = NetworkPulseInternetWidgetText.Build(
            userEnabled: true,
            showInHeader: true,
            snap,
            new NetworkPulseSmoothedHeadline(NetworkPulseStatus.Good, "Stable", 18.5, null, Array.Empty<double>(), 0, 0),
            new NetworkPulseLastKnownGood(),
            uploadProbesEnabled: false,
            consecutiveHardFailures: 0,
            TimeSpan.FromMinutes(10),
            DateTimeOffset.UtcNow,
            verificationInconsistentStreak: 5);

        Assert.Contains("Partial check", l1, StringComparison.Ordinal);
        Assert.DoesNotContain("Internet: Limited", l1, StringComparison.Ordinal);
        Assert.DoesNotContain("checks inconsistent", l1, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Internet appears usable", l3, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FullCycle_AllMeasured_RendersFullCheckTimestamp_NotPartial()
    {
        // When upload probes are on AND all three (latency/download/upload) measured this cycle,
        // the freshness line uses "Full check Xs ago" wording so the user knows the timestamp
        // represents a complete cycle rather than a download-only refresh.
        var snap = new NetworkPulseSnapshot(
            NetworkPulseStatus.Good,
            NetworkPulseConfidence.High,
            InternetReachable: true,
            "Ethernet",
            NetworkPulseConnectionKind.Ethernet,
            null,
            PingMs: 12,
            JitterMs: 2,
            PacketLossPercent: 0,
            DownloadMbps: 95.3,
            UploadMbps: 19.8,
            NetworkPulseMeasurementKind.Measured,
            NetworkPulseMeasurementKind.Measured,
            NetworkPulseMeasurementKind.Measured,
            GatewayPingMs: 1,
            DnsLookupMs: 1,
            DateTimeOffset.UtcNow,
            string.Empty,
            "n",
            "s",
            DownloadSkipReason: NetworkPulseSkipReason.None,
            UploadSkipReason: NetworkPulseSkipReason.None,
            LatencySkipReason: NetworkPulseSkipReason.None,
            CycleComplete: true);
        var (l1, l2, l3) = NetworkPulseInternetWidgetText.Build(
            userEnabled: true,
            showInHeader: true,
            snap,
            new NetworkPulseSmoothedHeadline(NetworkPulseStatus.Good, "Stable", 95.3, 19.8, Array.Empty<double>(), 0, 0),
            new NetworkPulseLastKnownGood(),
            uploadProbesEnabled: true,
            consecutiveHardFailures: 0,
            TimeSpan.FromMinutes(10),
            DateTimeOffset.UtcNow);

        Assert.Contains("Online", l1, StringComparison.Ordinal);
        Assert.Contains("Up: 19.8 Mbps", l2, StringComparison.Ordinal);
        Assert.Contains("Full check", l3, StringComparison.Ordinal);
        Assert.DoesNotContain("Partial check", l3, StringComparison.Ordinal);
        Assert.DoesNotContain("not tested", l2, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not measured this cycle", l2, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UploadTimedOut_RendersPartialCheck_AndExplainsReason()
    {
        // Upload probes on, upload timed out, download + ping measured: this is a Partial check
        // — not Online/Full check — and the subtitle must explain why.
        var snap = new NetworkPulseSnapshot(
            NetworkPulseStatus.Good,
            NetworkPulseConfidence.Medium,
            InternetReachable: true,
            "Ethernet",
            NetworkPulseConnectionKind.Ethernet,
            null,
            PingMs: 12,
            JitterMs: 2,
            PacketLossPercent: 0,
            DownloadMbps: 95.3,
            UploadMbps: null,
            NetworkPulseMeasurementKind.Measured,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Measured,
            GatewayPingMs: 1,
            DnsLookupMs: 1,
            DateTimeOffset.UtcNow,
            string.Empty,
            "n",
            "s",
            DownloadSkipReason: NetworkPulseSkipReason.None,
            UploadSkipReason: NetworkPulseSkipReason.TimedOut,
            LatencySkipReason: NetworkPulseSkipReason.None,
            CycleComplete: false);
        var (l1, l2, l3) = NetworkPulseInternetWidgetText.Build(
            userEnabled: true,
            showInHeader: true,
            snap,
            new NetworkPulseSmoothedHeadline(NetworkPulseStatus.Good, "Stable", 95.3, null, Array.Empty<double>(), 0, 0),
            new NetworkPulseLastKnownGood(),
            uploadProbesEnabled: true,
            consecutiveHardFailures: 0,
            TimeSpan.FromMinutes(10),
            DateTimeOffset.UtcNow);

        Assert.Contains("Partial check", l1, StringComparison.Ordinal);
        Assert.Contains("Up: timed out", l2, StringComparison.Ordinal);
        Assert.Contains("timed out", l3, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Up: not tested", l2, StringComparison.Ordinal);
        Assert.DoesNotContain("Full check", l3, StringComparison.Ordinal);
    }

    [Fact]
    public void UploadProbeOff_v1_2_3_NewSnapshot_ShowsUpProbesOff_WhenCallerEnabled()
    {
        // New snapshot vocabulary: when caller passes uploadProbesEnabled:true but the snapshot
        // reports the probe was disabled in settings, render "Up: probes off" instead of the
        // ambiguous legacy "not tested" string.
        var snap = new NetworkPulseSnapshot(
            NetworkPulseStatus.Good,
            NetworkPulseConfidence.Medium,
            InternetReachable: true,
            "Ethernet",
            NetworkPulseConnectionKind.Ethernet,
            null,
            PingMs: 12,
            JitterMs: 2,
            PacketLossPercent: 0,
            DownloadMbps: 95.3,
            UploadMbps: null,
            NetworkPulseMeasurementKind.Measured,
            NetworkPulseMeasurementKind.Unavailable,
            NetworkPulseMeasurementKind.Measured,
            GatewayPingMs: 1,
            DnsLookupMs: 1,
            DateTimeOffset.UtcNow,
            string.Empty,
            "n",
            "s",
            DownloadSkipReason: NetworkPulseSkipReason.None,
            UploadSkipReason: NetworkPulseSkipReason.ProbesDisabledInSettings,
            LatencySkipReason: NetworkPulseSkipReason.None,
            CycleComplete: true);
        var (_, l2, _) = NetworkPulseInternetWidgetText.Build(
            userEnabled: true,
            showInHeader: true,
            snap,
            new NetworkPulseSmoothedHeadline(NetworkPulseStatus.Good, "Stable", 95.3, null, Array.Empty<double>(), 0, 0),
            new NetworkPulseLastKnownGood(),
            uploadProbesEnabled: true,
            consecutiveHardFailures: 0,
            TimeSpan.FromMinutes(10),
            DateTimeOffset.UtcNow);

        Assert.Contains("Up: probes off", l2, StringComparison.Ordinal);
    }

    [Fact]
    public void Surface_PingOnly_DnsAndHttpsBothFailMultiCycle_IsLimited()
    {
        // No DNS, no HTTPS for sustained streak — Limited is the right call even though ICMP works.
        var pingOnly = new LatencyMonitorResult(20, 2, 0, 1, null, AnyIcmpSuccess: true);
        var status = NetworkPulseService.ClassifyStatusForTests(
            hasUsableRoute: true,
            httpsReachable: false,
            dnsSucceeded: false,
            pingOnly,
            downloadMbps: null,
            latencyKind: NetworkPulseMeasurementKind.Measured,
            httpsOnlyFailureStreak: 3,
            hasStrongUsabilityEvidence: false);

        Assert.Equal(NetworkPulseStatus.Limited, status);
    }
}
