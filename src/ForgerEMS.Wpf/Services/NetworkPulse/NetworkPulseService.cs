using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
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
    // Tracks consecutive cycles where HTTPS verification probe failed while other usability evidence
    // (ICMP / route / DNS / measured throughput) was present. Used to suppress an instant "Limited"
    // downgrade on a single failed Microsoft/captive probe.
    private int _httpsOnlyFailureStreak;

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
            _httpsOnlyFailureStreak = 0;
            _stabilizer.Reset();
            _lastGood.Clear();
            PostUi(NetworkPulseUiState.FromSnapshotNeutral(BuildDisabledSnapshot()));
            return;
        }

        var pause = TryBuildPauseSnapshot(settings, out var pausedSnapshot);
        if (pause)
        {
            _consecutiveHardFailures = 0;
            _httpsOnlyFailureStreak = 0;
            _stabilizer.Reset();
            PostUi(BuildUiState(settings, pausedSnapshot, smoothed: null, wifi: null, spark: null, reliability: NetworkPulseReliabilityTier.Unknown, reliabilityExplain: NetworkPulseReliabilityScorer.Explain(NetworkPulseReliabilityTier.Unknown, false, false, 0), now: DateTimeOffset.UtcNow));
            return;
        }

        PostStatus(NetworkPulseStatus.Testing);
        var adapter = NetworkPulseEnvironmentReader.TryGetActiveAdapter();
        var httpReach = await _speedProbeRunner.TryHttpReachableAsync(ct).ConfigureAwait(false);
        var captiveSuspected = httpReach.Succeeded &&
                               httpReach.ErrorCategory.Equals("UnexpectedProbeBody", StringComparison.OrdinalIgnoreCase);
        var reach = httpReach.Succeeded && !captiveSuspected;
        var icmpN = NetworkPulseCadence.LatencySampleCount(settings.Mode);
        var latency = await NetworkLatencyMonitor.MeasureAsync(icmpN, ct).ConfigureAwait(false);
        var dnsOk = latency.DnsLookupMs is > 0;
        var hasUsableRoute = adapter.HasUsableRoute || httpReach.Succeeded || dnsOk || latency.AnyIcmpSuccess;

        var hardFail = !hasUsableRoute && !reach && !latency.AnyIcmpSuccess && !dnsOk;
        if (hardFail)
        {
            _consecutiveHardFailures++;
        }
        else
        {
            _consecutiveHardFailures = 0;
        }

        // HTTPS-only failure: verification probe failed (or captive content suspected) while other
        // real usability signals are healthy. Tracked separately from hard failures so a single
        // bad Microsoft probe does not flip the UI to "Limited" when the user can actually browse.
        var httpsVerificationFailed = !reach || captiveSuspected;
        var hasOtherUsabilityEvidence =
            latency.AnyIcmpSuccess ||
            dnsOk ||
            (hasUsableRoute && !hardFail);
        if (httpsVerificationFailed && hasOtherUsabilityEvidence)
        {
            _httpsOnlyFailureStreak++;
        }
        else
        {
            _httpsOnlyFailureStreak = 0;
        }

        var downloadKind = NetworkPulseMeasurementKind.Unavailable;
        var uploadKind = NetworkPulseMeasurementKind.Unavailable;
        double? downloadMbps = null;
        double? uploadMbps = null;
        var speedProbeAttempted = false;
        var speedProbeFailed = false;
        var downloadSkipReason = NetworkPulseSkipReason.None;
        var uploadSkipReason = NetworkPulseSkipReason.None;

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
                    downloadSkipReason = down.Succeeded ? NetworkPulseSkipReason.ImplausibleResult : NetworkPulseSkipReason.ProbeFailed;
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
                    try
                    {
                        var up = await _speedProbeRunner.RunUploadSampleAsync(Math.Max(4096, upBytes), upCt.Token).ConfigureAwait(false);
                        if (up.Succeeded && NetworkPulseSpeedSanity.IsPlausibleMeasuredMbps(up.Mbps))
                        {
                            uploadMbps = up.Mbps;
                            uploadKind = NetworkPulseMeasurementKind.Measured;
                        }
                        else
                        {
                            uploadKind = NetworkPulseMeasurementKind.Unavailable;
                            uploadSkipReason = up.Succeeded ? NetworkPulseSkipReason.ImplausibleResult : NetworkPulseSkipReason.ProbeFailed;
                        }
                    }
                    catch (OperationCanceledException) when (upCt.IsCancellationRequested && !ct.IsCancellationRequested)
                    {
                        uploadKind = NetworkPulseMeasurementKind.Unavailable;
                        uploadSkipReason = NetworkPulseSkipReason.TimedOut;
                        _diag.Emit("Upload sample timed out this cycle.", LogSeverity.Info);
                    }
                }
                else
                {
                    uploadKind = NetworkPulseMeasurementKind.Unavailable;
                    uploadSkipReason = NetworkPulseSkipReason.ProbesDisabledInSettings;
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
                downloadSkipReason = NetworkPulseSkipReason.ProbeFailed;
                if (settings.UploadProbesEnabled && uploadSkipReason == NetworkPulseSkipReason.None)
                {
                    uploadSkipReason = NetworkPulseSkipReason.ProbeFailed;
                }
                _diag.Emit($"Speed probe failed ({ex.GetType().Name}).", LogSeverity.Warning);
            }
        }
        else if (!settings.AutoTestSpeedLightly)
        {
            downloadKind = NetworkPulseMeasurementKind.Unavailable;
            uploadKind = NetworkPulseMeasurementKind.Unavailable;
            downloadSkipReason = NetworkPulseSkipReason.ProbesDisabledInSettings;
            uploadSkipReason = NetworkPulseSkipReason.ProbesDisabledInSettings;
        }
        else
        {
            downloadKind = NetworkPulseMeasurementKind.Unavailable;
            uploadKind = NetworkPulseMeasurementKind.Unavailable;
            downloadSkipReason = NetworkPulseSkipReason.NotDueThisCycle;
            uploadSkipReason = settings.UploadProbesEnabled
                ? NetworkPulseSkipReason.NotDueThisCycle
                : NetworkPulseSkipReason.ProbesDisabledInSettings;
        }

        // Make sure uploadSkipReason is set whenever upload is not measured.
        if (uploadKind != NetworkPulseMeasurementKind.Measured && uploadSkipReason == NetworkPulseSkipReason.None)
        {
            uploadSkipReason = settings.UploadProbesEnabled
                ? NetworkPulseSkipReason.NotDueThisCycle
                : NetworkPulseSkipReason.ProbesDisabledInSettings;
        }

        var fallbackHttpLatency = !latency.AnyIcmpSuccess && httpReach.ElapsedMs is > 0 ? httpReach.ElapsedMs : null;
        var effectivePingMs = latency.IcmpMedianMs ?? fallbackHttpLatency;
        var latencyKind = effectivePingMs is > 0 ? NetworkPulseMeasurementKind.Measured : NetworkPulseMeasurementKind.Unavailable;
        var confidence = ClassifyConfidence(
            latency,
            downloadKind == NetworkPulseMeasurementKind.Measured,
            verificationMismatch: httpsVerificationFailed && hasOtherUsabilityEvidence);

        // "Strong usability evidence" gate: a recent successful throughput sample with healthy
        // ping and low packet loss is direct proof the user can browse. We refuse to escalate
        // an HTTPS-only verification failure to "Limited" while this holds — Windows NCSI /
        // captive-portal probes can fail persistently due to VPNs, custom DNS (Pi-hole, NextDNS),
        // content filters, or transient Microsoft endpoint issues without breaking real traffic.
        var hasStrongUsabilityEvidence = HasStrongUsabilityEvidence(
            downloadMbps,
            downloadKind,
            _lastGood,
            effectivePingMs,
            latencyKind,
            latency,
            now);

        var rawStatus = ClassifyStatus(
            hasUsableRoute,
            reach,
            dnsOk,
            latency,
            downloadMbps,
            latencyKind,
            _httpsOnlyFailureStreak,
            hasStrongUsabilityEvidence);
        var smoothed = _stabilizer.Smooth(
            rawStatus,
            reach,
            latencyKind == NetworkPulseMeasurementKind.Measured,
            effectivePingMs,
            latency.JitterMs,
            latency.PacketLossPercent,
            downloadMbps,
            uploadMbps,
            downloadKind,
            uploadKind);

        var wifiLine = await NetworkPulseWifiEnricher.TryGetContextLineAsync(adapter.Kind, ct).ConfigureAwait(false);

        var reliability = NetworkPulseReliabilityScorer.Compute(
            reach,
            latencyKind == NetworkPulseMeasurementKind.Measured,
            effectivePingMs,
            latency.JitterMs,
            latency.PacketLossPercent,
            smoothed.RecentReachFailures,
            smoothed.RecentUnstableRawStreak);
        var reliabilityExplain = NetworkPulseReliabilityScorer.Explain(
            reliability,
            reach,
            latencyKind == NetworkPulseMeasurementKind.Measured,
            latency.PacketLossPercent);

        var spark = NetworkPulseSparklineGeometryBuilder.BuildPingSparkline(smoothed.SparkPingNormalized, 120, 26);

        var probeMismatchNote = (!reach || captiveSuspected) && hasOtherUsabilityEvidence;
        var notes =
            "Latency: ICMP to a public DNS hostname + optional default-gateway ping + DNS lookup timing (lookup may be cached). " +
            (!latency.AnyIcmpSuccess && fallbackHttpLatency is > 0
                ? "ICMP ping was unavailable; latency shown uses HTTPS probe timing. "
                : string.Empty) +
            "Reachability: HTTPS GET to the Windows captive-portal probe host. " +
            (!adapter.HasUsableRoute ? "No active default route was reported by Windows networking APIs. " : adapter.RouteDetail + " ") +
            (!dnsOk && reach ? "DNS lookup did not return in time; HTTPS reachability still succeeded. " : string.Empty) +
            (captiveSuspected
                ? (probeMismatchNote
                    ? "HTTPS verification probe returned unexpected content (common with VPNs, custom DNS, or content filters); other usability signals still indicate the internet is reachable. "
                    : "HTTPS probe returned unexpected content; captive portal or interception is suspected. ")
                : string.Empty) +
            (!reach && !captiveSuspected && adapter.HasUsableRoute
                ? (probeMismatchNote
                    ? "Windows verification probe failed this cycle while ICMP, DNS, or throughput still indicate working internet — treated as a verification mismatch, not an outage. "
                    : "HTTPS reachability failed while an active route exists. ")
                : string.Empty) +
            (!string.IsNullOrWhiteSpace(httpReach.ErrorCategory) && (!reach || captiveSuspected) ? $"HTTPS probe category: {httpReach.ErrorCategory}. " : string.Empty) +
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

        var latencySkipReason = latencyKind == NetworkPulseMeasurementKind.Measured
            ? NetworkPulseSkipReason.None
            : NetworkPulseSkipReason.ProbeFailed;

        // A "full cycle" requires latency + download + upload to all resolve to a measured value
        // OR for upload probes to be intentionally disabled in settings (in which case download +
        // latency are enough for a "full check" by the user's policy). If upload probes are
        // enabled but upload did not return measured this cycle, the cycle is partial.
        var uploadCoveredForFullCheck =
            uploadKind == NetworkPulseMeasurementKind.Measured ||
            uploadSkipReason == NetworkPulseSkipReason.ProbesDisabledInSettings;
        var cycleComplete = latencyKind == NetworkPulseMeasurementKind.Measured &&
                            downloadKind == NetworkPulseMeasurementKind.Measured &&
                            uploadCoveredForFullCheck &&
                            reach;

        var snapshot = new NetworkPulseSnapshot(
            rawStatus,
            confidence,
            reach,
            adapter.Name,
            adapter.Kind,
            adapter.LinkSpeedMbpsEstimated,
            effectivePingMs,
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
            "Uses small network checks only. Does not run a full Ookla-style saturation test. Never treats adapter link speed as internet speed.",
            downloadSkipReason,
            uploadSkipReason,
            latencySkipReason,
            cycleComplete);

        if (effectivePingMs is > 0)
        {
            _lastGood.AcceptPing(effectivePingMs, now);
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
            "Header chip uses brief hold times so quick Wi‑Fi blips or single failed verification probes don't flash an alarming label. Instant classification is still shown in details.";
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
            now,
            verificationInconsistentStreak: _httpsOnlyFailureStreak);

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
            hints.AppUpdateDownloadInProgress)
        {
            snapshot = BuildPausedSnapshot("Paused during an app update download because that pause policy is enabled.");
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

    private static NetworkPulseConfidence ClassifyConfidence(
        LatencyMonitorResult latency,
        bool hadDownloadSample,
        bool verificationMismatch = false)
    {
        if (hadDownloadSample && latency.AnyIcmpSuccess && latency.PacketLossPercent <= 1)
        {
            // High confidence requires verification probes to agree as well — if they disagree,
            // ratchet down to Medium so the UI/popup can explain the inconsistency honestly.
            return verificationMismatch ? NetworkPulseConfidence.Medium : NetworkPulseConfidence.High;
        }

        if (latency.AnyIcmpSuccess)
        {
            return latency.PacketLossPercent <= 5 ? NetworkPulseConfidence.Medium : NetworkPulseConfidence.Low;
        }

        return NetworkPulseConfidence.Low;
    }

    internal static NetworkPulseStatus ClassifyStatusForTests(bool reach, LatencyMonitorResult latency, double? downloadMbps, NetworkPulseMeasurementKind latencyKind) =>
        ClassifyStatus(
            hasUsableRoute: reach || latency.AnyIcmpSuccess || latency.DnsLookupMs is > 0,
            httpsReachable: reach,
            dnsSucceeded: latency.DnsLookupMs is > 0,
            latency,
            downloadMbps,
            latencyKind,
            httpsOnlyFailureStreak: 0,
            hasStrongUsabilityEvidence: false);

    internal static NetworkPulseStatus ClassifyStatusForTests(
        bool hasUsableRoute,
        bool httpsReachable,
        bool dnsSucceeded,
        LatencyMonitorResult latency,
        double? downloadMbps,
        NetworkPulseMeasurementKind latencyKind) =>
        ClassifyStatus(hasUsableRoute, httpsReachable, dnsSucceeded, latency, downloadMbps, latencyKind, httpsOnlyFailureStreak: 0, hasStrongUsabilityEvidence: false);

    internal static NetworkPulseStatus ClassifyStatusForTests(
        bool hasUsableRoute,
        bool httpsReachable,
        bool dnsSucceeded,
        LatencyMonitorResult latency,
        double? downloadMbps,
        NetworkPulseMeasurementKind latencyKind,
        int httpsOnlyFailureStreak) =>
        ClassifyStatus(hasUsableRoute, httpsReachable, dnsSucceeded, latency, downloadMbps, latencyKind, httpsOnlyFailureStreak, hasStrongUsabilityEvidence: false);

    internal static NetworkPulseStatus ClassifyStatusForTests(
        bool hasUsableRoute,
        bool httpsReachable,
        bool dnsSucceeded,
        LatencyMonitorResult latency,
        double? downloadMbps,
        NetworkPulseMeasurementKind latencyKind,
        int httpsOnlyFailureStreak,
        bool hasStrongUsabilityEvidence) =>
        ClassifyStatus(hasUsableRoute, httpsReachable, dnsSucceeded, latency, downloadMbps, latencyKind, httpsOnlyFailureStreak, hasStrongUsabilityEvidence);

    // HTTPS verification probe failures must be sustained AND lack strong throughput evidence
    // before they downgrade visible status. A single failed Microsoft NCSI / captive probe with
    // healthy ICMP+route+throughput is not a user-visible outage — that is what
    // HttpsOnlyLimitedThreshold (with the strong-evidence override) enforces here.
    private const int HttpsOnlyLimitedThreshold = 3;

    // Strong usability evidence freshness window: a successful download sample older than this
    // is no longer considered proof of current usability.
    private static readonly TimeSpan StrongEvidenceFreshness = TimeSpan.FromMinutes(5);

    internal static bool HasStrongUsabilityEvidenceForTests(
        double? currentDownloadMbps,
        NetworkPulseMeasurementKind downloadKind,
        double? lastGoodDownloadMbps,
        DateTimeOffset lastGoodDownloadUtc,
        double? effectivePingMs,
        NetworkPulseMeasurementKind latencyKind,
        LatencyMonitorResult latency,
        DateTimeOffset utcNow)
    {
        var hasFreshDownload =
            (downloadKind == NetworkPulseMeasurementKind.Measured &&
             NetworkPulseSpeedSanity.IsPlausibleMeasuredMbps(currentDownloadMbps) &&
             currentDownloadMbps > 1) ||
            (NetworkPulseSpeedSanity.IsPlausibleMeasuredMbps(lastGoodDownloadMbps) &&
             lastGoodDownloadMbps > 1 &&
             lastGoodDownloadUtc != default &&
             (utcNow - lastGoodDownloadUtc) <= StrongEvidenceFreshness);

        if (!hasFreshDownload)
        {
            return false;
        }

        // Latency must look healthy: a usable ping reading <= 150 ms and low loss.
        var pingHealthy = latencyKind == NetworkPulseMeasurementKind.Measured &&
                          effectivePingMs is > 0 and <= 150;
        var lossHealthy = (latency.PacketLossPercent) <= 2.0;
        return pingHealthy && lossHealthy;
    }

    private static bool HasStrongUsabilityEvidence(
        double? currentDownloadMbps,
        NetworkPulseMeasurementKind downloadKind,
        NetworkPulseLastKnownGood lastGood,
        double? effectivePingMs,
        NetworkPulseMeasurementKind latencyKind,
        LatencyMonitorResult latency,
        DateTimeOffset utcNow) =>
        HasStrongUsabilityEvidenceForTests(
            currentDownloadMbps,
            downloadKind,
            lastGood.DownloadMbps,
            lastGood.DownloadUtc,
            effectivePingMs,
            latencyKind,
            latency,
            utcNow);

    private static NetworkPulseStatus ClassifyStatus(
        bool hasUsableRoute,
        bool httpsReachable,
        bool dnsSucceeded,
        LatencyMonitorResult latency,
        double? downloadMbps,
        NetworkPulseMeasurementKind latencyKind,
        int httpsOnlyFailureStreak,
        bool hasStrongUsabilityEvidence)
    {
        if (!hasUsableRoute && !httpsReachable && !dnsSucceeded && !latency.AnyIcmpSuccess)
        {
            return NetworkPulseStatus.Offline;
        }

        var hasOtherUsabilityEvidence =
            latency.AnyIcmpSuccess ||
            dnsSucceeded ||
            (downloadMbps is > 0 && latencyKind == NetworkPulseMeasurementKind.Measured);

        if (!httpsReachable)
        {
            if (!hasOtherUsabilityEvidence)
            {
                return hasUsableRoute ? NetworkPulseStatus.Limited : NetworkPulseStatus.Offline;
            }

            // HTTPS-only failure with strong throughput + healthy ping + low loss: do NOT downgrade
            // to Limited even if the verification streak is long. The user can browse; surface
            // wording will say "Online — checks inconsistent" and confidence drops to Medium.
            // Stronger negative evidence (high loss, DNS+HTTPS both failing, no ICMP, captive
            // portal strongly detected) takes its own paths above/below.
            if (hasStrongUsabilityEvidence)
            {
                // fall through to latency-based classification
            }
            else if (httpsOnlyFailureStreak >= HttpsOnlyLimitedThreshold)
            {
                return NetworkPulseStatus.Limited;
            }
        }

        if (latency.AnyIcmpSuccess && (latency.PacketLossPercent >= 8 || (latency.JitterMs ?? 0) >= 80))
        {
            return NetworkPulseStatus.Unstable;
        }

        var ping = latency.IcmpMedianMs ?? 999;
        if (latency.AnyIcmpSuccess && ping >= 220)
        {
            return NetworkPulseStatus.Slow;
        }

        if (downloadMbps is < 3 && latencyKind == NetworkPulseMeasurementKind.Measured && httpsReachable)
        {
            return NetworkPulseStatus.Slow;
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
        void SafeAction()
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                StartupDiagnosticLog.AppendException("NetworkPulseService.Post", exception);
            }
        }

        if (_ui is null)
        {
            SafeAction();
            return;
        }

        _ui.Post(_ => SafeAction(), null);
    }

    private static string ChipForSnapshot(NetworkPulseStatus status) =>
        status switch
        {
            NetworkPulseStatus.Good => "Stable",
            NetworkPulseStatus.Slow => "Slow",
            NetworkPulseStatus.Unstable => "Unstable",
            NetworkPulseStatus.Limited => "Limited",
            NetworkPulseStatus.Offline => "Offline",
            NetworkPulseStatus.Unknown => "Unknown",
            NetworkPulseStatus.Testing => "Testing…",
            NetworkPulseStatus.Paused => "Paused",
            _ => "Unknown"
        };
}
