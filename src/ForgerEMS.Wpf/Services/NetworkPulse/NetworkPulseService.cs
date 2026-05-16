using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.ViewModels;

namespace VentoyToolkitSetup.Wpf.Services.NetworkPulse;

public sealed class NetworkPulseService : IDisposable
{
    private readonly NetworkPulseViewModel _viewModel;
    private readonly Func<NetworkPulseSettings> _settings;
    private readonly Func<NetworkPulseHostActivityHints> _hostHints;
    private readonly Func<string> _reportsDirectory;
    private readonly SynchronizationContext? _ui;
    private readonly NetworkSpeedProbeRunner _speedProbeRunner = new();
    private readonly NetworkPulseStabilizer _stabilizer = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly NetworkPulseLastKnownGood _lastGood = new();
    private readonly NetworkPulseDiagnostics _diag;
    private Task? _loop;
    private bool _disposed;
    private DateTimeOffset _lastSpeedProbeUtc = DateTimeOffset.MinValue;
    private int _consecutiveHardFailures;

    public NetworkPulseService(
        NetworkPulseViewModel viewModel,
        Func<NetworkPulseSettings> settings,
        Func<NetworkPulseHostActivityHints> hostHints,
        SynchronizationContext? uiContext,
        Func<string> reportsDirectory,
        Action<LogLine>? diagnosticsLog = null)
    {
        _viewModel = viewModel;
        _settings = settings;
        _hostHints = hostHints;
        _ui = uiContext;
        _reportsDirectory = reportsDirectory;
        _diag = new NetworkPulseDiagnostics(diagnosticsLog);
    }

    public void Start() => _loop ??= Task.Run(() => RunLoopAsync(_cts.Token));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _cts.Cancel();
        }
        catch
        {
        }

        _speedProbeRunner.Dispose();
        _cts.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var settings = _settings();
                await PulseOnceAsync(settings, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _consecutiveHardFailures++;
                _diag.Emit($"Measurement cycle failed ({ex.GetType().Name}: {ex.Message}). Prior readings kept; retry continues.", LogSeverity.Warning);
            }

            var settingsAfter = _settings();
            var baseDelay = Math.Max(3000, settingsAfter.RefreshIntervalSeconds * 1000 - (int)sw.ElapsedMilliseconds);
            var backoff = Math.Min(60_000, 1500 * (1 << Math.Min(_consecutiveHardFailures, 5)));
            var delayMs = baseDelay + (_consecutiveHardFailures > 0 ? backoff : 0);
            try
            {
                await Task.Delay(delayMs, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task PulseOnceAsync(NetworkPulseSettings settings, CancellationToken ct)
    {
        if (!settings.Enabled)
        {
            _consecutiveHardFailures = 0;
            _stabilizer.Reset();
            _lastGood.Clear();
            PostUi(NetworkPulseUiState.FromSnapshotNeutral(BuildDisabledSnapshot()));
            return;
        }

        var pause = TryBuildPauseSnapshot(settings, out var pausedSnapshot);
        if (pause)
        {
            _consecutiveHardFailures = 0;
            _stabilizer.Reset();
            PostUi(BuildUiState(settings, pausedSnapshot, smoothed: null, wifi: null, spark: null, reliability: NetworkPulseReliabilityTier.Unknown, reliabilityExplain: NetworkPulseReliabilityScorer.Explain(NetworkPulseReliabilityTier.Unknown, false, false, 0), now: DateTimeOffset.UtcNow));
            return;
        }

        PostStatus(NetworkPulseStatus.Testing);
        var adapter = NetworkPulseEnvironmentReader.TryGetActiveAdapter();
        var reach = await _speedProbeRunner.TryHeadReachableAsync(ct).ConfigureAwait(false);
        var icmpN = NetworkPulseCadence.LatencySampleCount(settings.Mode);
        var latency = await NetworkLatencyMonitor.MeasureAsync(icmpN, ct).ConfigureAwait(false);

        var hardFail = !reach && !latency.AnyIcmpSuccess;
        if (hardFail)
        {
            _consecutiveHardFailures++;
        }
        else
        {
            _consecutiveHardFailures = 0;
        }

        var downloadKind = NetworkPulseMeasurementKind.Unavailable;
        var uploadKind = NetworkPulseMeasurementKind.Unavailable;
        double? downloadMbps = null;
        double? uploadMbps = null;
        var speedProbeAttempted = false;
        var speedProbeFailed = false;

        var now = DateTimeOffset.UtcNow;
        var probeDue = settings.AutoTestSpeedLightly &&
                       (now - _lastSpeedProbeUtc).TotalSeconds >= settings.SpeedProbeCadenceSeconds;

        if (probeDue && settings.AutoTestSpeedLightly)
        {
            speedProbeAttempted = true;
            _lastSpeedProbeUtc = now;
            try
            {
                var bytes = NetworkPulseCadence.ProbeMaxBytes(settings.Mode);
                using var probeCt = CancellationTokenSource.CreateLinkedTokenSource(ct);
                probeCt.CancelAfter(TimeSpan.FromSeconds(14));
                var down = await _speedProbeRunner.RunDownloadSampleAsync(bytes, probeCt.Token).ConfigureAwait(false);
                if (down.Succeeded && NetworkPulseSpeedSanity.IsPlausibleMeasuredMbps(down.Mbps))
                {
                    downloadMbps = down.Mbps;
                    downloadKind = NetworkPulseMeasurementKind.Measured;
                }
                else
                {
                    downloadKind = NetworkPulseMeasurementKind.Unavailable;
                    speedProbeFailed = true;
                    if (down.Succeeded)
                    {
                        _diag.Emit("Download sample discarded (implausible Mbps or empty read).", LogSeverity.Info);
                    }
                }

                if (settings.UploadProbesEnabled)
                {
                    using var upCt = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    upCt.CancelAfter(TimeSpan.FromSeconds(14));
                    var upBytes = Math.Min(16_384, bytes / 8);
                    var up = await _speedProbeRunner.RunUploadSampleAsync(Math.Max(4096, upBytes), upCt.Token).ConfigureAwait(false);
                    if (up.Succeeded && NetworkPulseSpeedSanity.IsPlausibleMeasuredMbps(up.Mbps))
                    {
                        uploadMbps = up.Mbps;
                        uploadKind = NetworkPulseMeasurementKind.Measured;
                    }
                    else
                    {
                        uploadKind = NetworkPulseMeasurementKind.Unavailable;
                    }
                }
                else
                {
                    uploadKind = NetworkPulseMeasurementKind.Unavailable;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                downloadKind = NetworkPulseMeasurementKind.Unavailable;
                uploadKind = NetworkPulseMeasurementKind.Unavailable;
                speedProbeFailed = true;
                _diag.Emit($"Speed probe failed ({ex.GetType().Name}).", LogSeverity.Warning);
            }
        }
        else if (!settings.AutoTestSpeedLightly)
        {
            downloadKind = NetworkPulseMeasurementKind.Unavailable;
            uploadKind = NetworkPulseMeasurementKind.Unavailable;
        }
        else
        {
            downloadKind = NetworkPulseMeasurementKind.Unavailable;
            uploadKind = NetworkPulseMeasurementKind.Unavailable;
        }

        var latencyKind = latency.AnyIcmpSuccess ? NetworkPulseMeasurementKind.Measured : NetworkPulseMeasurementKind.Unavailable;
        var confidence = ClassifyConfidence(latency, downloadKind == NetworkPulseMeasurementKind.Measured);
        var rawStatus = ClassifyStatus(reach, latency, downloadMbps, latencyKind);
        var smoothed = _stabilizer.Smooth(
            rawStatus,
            reach,
            latency.AnyIcmpSuccess,
            latency.IcmpMedianMs,
            latency.JitterMs,
            latency.PacketLossPercent,
            downloadMbps,
            uploadMbps,
            downloadKind,
            uploadKind);

        var wifiLine = await NetworkPulseWifiEnricher.TryGetContextLineAsync(adapter.Kind, ct).ConfigureAwait(false);

        var reliability = NetworkPulseReliabilityScorer.Compute(
            reach,
            latency.AnyIcmpSuccess,
            latency.IcmpMedianMs,
            latency.JitterMs,
            latency.PacketLossPercent,
            smoothed.RecentReachFailures,
            smoothed.RecentUnstableRawStreak);
        var reliabilityExplain = NetworkPulseReliabilityScorer.Explain(
            reliability,
            reach,
            latency.AnyIcmpSuccess,
            latency.PacketLossPercent);

        var spark = NetworkPulseSparklineGeometryBuilder.BuildPingSparkline(smoothed.SparkPingNormalized, 120, 26);

        var notes =
            "Latency: ICMP to a public DNS hostname + optional default-gateway ping + DNS lookup timing (lookup may be cached). " +
            "Reachability: HTTPS HEAD to the Windows captive-portal probe host. " +
            (downloadKind == NetworkPulseMeasurementKind.Measured
                ? "Download sample: short HTTPS fetch capped by Network Pulse mode (lightweight measured throughput, not a full speed test)."
                : speedProbeFailed
                    ? "Download sample failed this cycle; previous last-good values are preserved when available."
                    : speedProbeAttempted
                        ? "Download throughput: no valid sample this cycle."
                        : "Download throughput: not sampled this cycle (disabled, paused, or not yet due).") +
            (settings.UploadProbesEnabled
                ? " Upload sample: small HTTPS POST when enabled."
                : " Upload probes: off (default).");

        var snapshot = new NetworkPulseSnapshot(
            rawStatus,
            confidence,
            reach,
            adapter.Name,
            adapter.Kind,
            adapter.LinkSpeedMbpsEstimated,
            latency.IcmpMedianMs,
            latency.JitterMs,
            latency.PacketLossPercent,
            downloadMbps,
            uploadMbps,
            downloadKind,
            uploadKind,
            latencyKind,
            latency.GatewayPingMs,
            latency.DnsLookupMs,
            now,
            string.Empty,
            notes,
            "Uses small network checks only. Does not run a full Ookla-style saturation test. Never treats adapter link speed as internet speed.");

        if (latency.AnyIcmpSuccess && latency.IcmpMedianMs is > 0)
        {
            _lastGood.AcceptPing(latency.IcmpMedianMs, now);
        }

        if (downloadKind == NetworkPulseMeasurementKind.Measured)
        {
            _lastGood.AcceptDownload(downloadMbps, now);
        }

        if (uploadKind == NetworkPulseMeasurementKind.Measured)
        {
            _lastGood.AcceptUpload(uploadMbps, now);
        }

        PostUi(BuildUiState(settings, snapshot, smoothed, wifiLine, spark, reliability, reliabilityExplain, now));

        NetworkPulseReportWriter.TryWriteIfDue(
            _reportsDirectory(),
            snapshot,
            smoothed,
            reliability,
            reliabilityExplain,
            wifiLine,
            TimeSpan.FromSeconds(50),
            now);
    }

    private NetworkPulseUiState BuildUiState(
        NetworkPulseSettings settings,
        NetworkPulseSnapshot snapshot,
        NetworkPulseSmoothedHeadline? smoothed,
        string? wifi,
        Geometry? spark,
        NetworkPulseReliabilityTier reliability,
        string reliabilityExplain,
        DateTimeOffset now)
    {
        var sm = smoothed ?? new NetworkPulseSmoothedHeadline(
            snapshot.Status,
            ChipForSnapshot(snapshot.Status),
            snapshot.DownloadMbps,
            snapshot.UploadMbps,
            Array.Empty<double>(),
            0,
            0);

        var smoothing =
            "Header chip uses brief hold times so quick Wi‑Fi blips do not flash Offline. Instant classification is still shown in details.";
        var clipboard = BuildClipboardSummaryLine(snapshot, reliability, sm.StatusChipText);
        var fresh = TimeSpan.FromSeconds(Math.Max(90, settings.RefreshIntervalSeconds * 4));
        var (w1, w2, w3) = NetworkPulseInternetWidgetText.Build(
            settings.Enabled,
            settings.ShowInHeader,
            snapshot,
            sm,
            _lastGood,
            settings.UploadProbesEnabled,
            _consecutiveHardFailures,
            fresh,
            now);

        return new NetworkPulseUiState(
            snapshot,
            sm,
            reliability,
            reliabilityExplain,
            wifi,
            spark,
            smoothing,
            clipboard,
            w1,
            w2,
            w3);
    }

    private bool TryBuildPauseSnapshot(NetworkPulseSettings settings, out NetworkPulseSnapshot snapshot)
    {
        snapshot = default!;
        var hints = _hostHints();
        if (settings.PauseDuringHostHeavyNetwork &&
            (hints.AppGlobalBusy || hints.AppUpdateDownloadInProgress || hints.UsbBenchmarkInProgress))
        {
            snapshot = BuildPausedSnapshot("Paused while ForgerEMS is busy with network, update, or USB benchmark activity.");
            return true;
        }

        if (settings.PauseOnMetered &&
            NetworkPulseEnvironmentReader.TryGetMeteredConnection(out var metered) &&
            metered)
        {
            snapshot = BuildPausedSnapshot("Paused on metered/costed network (Windows connection cost).");
            return true;
        }

        if (settings.PauseOnBatterySaver &&
            NetworkPulseEnvironmentReader.TryGetEnergySaverEnabled(out var saver) &&
            saver)
        {
            snapshot = BuildPausedSnapshot("Paused while Windows Battery Saver is on.");
            return true;
        }

        return false;
    }

    private static NetworkPulseSnapshot BuildDisabledSnapshot() =>
        new(
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
            "Monitoring disabled by user.",
            "No probes are scheduled while disabled.");

    private static NetworkPulseSnapshot BuildPausedSnapshot(string reason)
    {
        var adapter = NetworkPulseEnvironmentReader.TryGetActiveAdapter();
        return new NetworkPulseSnapshot(
            NetworkPulseStatus.Paused,
            NetworkPulseConfidence.Low,
            false,
            adapter.Name,
            adapter.Kind,
            adapter.LinkSpeedMbpsEstimated,
            null,
            null,
            null,
            null,
            null,
            NetworkPulseMeasurementKind.Paused,
            NetworkPulseMeasurementKind.Paused,
            NetworkPulseMeasurementKind.Paused,
            null,
            null,
            DateTimeOffset.UtcNow,
            reason,
            reason,
            "No bandwidth saturation; pauses are conservative.");
    }

    private static NetworkPulseSnapshot BuildUnknownSnapshot(string note)
    {
        var adapter = NetworkPulseEnvironmentReader.TryGetActiveAdapter();
        return new NetworkPulseSnapshot(
            NetworkPulseStatus.Unknown,
            NetworkPulseConfidence.Low,
            false,
            adapter.Name,
            adapter.Kind,
            adapter.LinkSpeedMbpsEstimated,
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
            string.Empty,
            note,
            "If checks fail, Network Pulse shows unknown/offline rather than implying the whole app failed.");
    }

    private static NetworkPulseConfidence ClassifyConfidence(LatencyMonitorResult latency, bool hadDownloadSample)
    {
        if (hadDownloadSample && latency.AnyIcmpSuccess && latency.PacketLossPercent <= 1)
        {
            return NetworkPulseConfidence.High;
        }

        if (latency.AnyIcmpSuccess)
        {
            return latency.PacketLossPercent <= 5 ? NetworkPulseConfidence.Medium : NetworkPulseConfidence.Low;
        }

        return NetworkPulseConfidence.Low;
    }

    internal static NetworkPulseStatus ClassifyStatusForTests(bool reach, LatencyMonitorResult latency, double? downloadMbps, NetworkPulseMeasurementKind latencyKind) =>
        ClassifyStatus(reach, latency, downloadMbps, latencyKind);

    private static NetworkPulseStatus ClassifyStatus(bool reach, LatencyMonitorResult latency, double? downloadMbps, NetworkPulseMeasurementKind latencyKind)
    {
        if (!reach && !latency.AnyIcmpSuccess)
        {
            return NetworkPulseStatus.Offline;
        }

        if (latencyKind == NetworkPulseMeasurementKind.Unavailable && !reach)
        {
            return NetworkPulseStatus.Offline;
        }

        if (reach && !latency.AnyIcmpSuccess)
        {
            return NetworkPulseStatus.Unknown;
        }

        if (latency.PacketLossPercent >= 8 || (latency.JitterMs ?? 0) >= 80)
        {
            return NetworkPulseStatus.Unstable;
        }

        var ping = latency.IcmpMedianMs ?? 999;
        if (ping >= 220)
        {
            return NetworkPulseStatus.Slow;
        }

        if (downloadMbps is < 3 && latency.AnyIcmpSuccess && reach)
        {
            return NetworkPulseStatus.Slow;
        }

        if (!reach && latency.AnyIcmpSuccess)
        {
            return NetworkPulseStatus.Unstable;
        }

        return NetworkPulseStatus.Good;
    }

    private void PostStatus(NetworkPulseStatus status) =>
        Post(() => _viewModel.ApplyTestingStatus(status));

    private static string BuildClipboardSummaryLine(NetworkPulseSnapshot s, NetworkPulseReliabilityTier reliability, string chip)
    {
        var conn = s.ConnectionKind switch
        {
            NetworkPulseConnectionKind.Ethernet => "Ethernet",
            NetworkPulseConnectionKind.WiFi => "Wi-Fi",
            NetworkPulseConnectionKind.Other => "Other",
            _ => "Unknown"
        };

        var ping = s.PingMs is > 0 ? $"{s.PingMs.Value:0} ms" : "—";
        var down = s.DownloadKind == NetworkPulseMeasurementKind.Measured && s.DownloadMbps is > 0
            ? (s.DownloadMbps.Value >= 100 ? $"{s.DownloadMbps.Value:0}" : $"{s.DownloadMbps.Value:0.#}")
            : "—";
        var up = s.UploadKind == NetworkPulseMeasurementKind.Measured && s.UploadMbps is > 0
            ? (s.UploadMbps!.Value >= 100 ? $"{s.UploadMbps.Value:0}" : $"{s.UploadMbps.Value:0.#}")
            : "—";

        return $"Network Pulse: {conn} · {NetworkPulseReliabilityScorer.TierLabel(reliability)} · {chip} · ping {ping} · measured {down}↓ / {up}↑ Mbps";
    }

    private void PostUi(NetworkPulseUiState state) =>
        Post(() => _viewModel.Apply(state));

    private void Post(Action action)
    {
        if (_ui is null)
        {
            action();
            return;
        }

        _ui.Post(_ => action(), null);
    }

    private static string ChipForSnapshot(NetworkPulseStatus status) =>
        status switch
        {
            NetworkPulseStatus.Good => "Stable",
            NetworkPulseStatus.Slow => "Slow",
            NetworkPulseStatus.Unstable => "Unstable",
            NetworkPulseStatus.Offline => "Offline",
            NetworkPulseStatus.Unknown => "Unknown",
            NetworkPulseStatus.Testing => "Testing…",
            NetworkPulseStatus.Paused => "Paused",
            _ => "Unknown"
        };
}
