using System;
using System.Globalization;

namespace VentoyToolkitSetup.Wpf.Services.NetworkPulse;

/// <summary>User-facing compact Internet widget strings: status, metrics, then freshness/detail.</summary>
public static class NetworkPulseInternetWidgetText
{
    // Kept for backward-compatibility with existing tests that import this constant.
    // The string itself is no longer rendered as a final cycle state — see
    // <see cref="UploadProbesOff"/>, <see cref="UploadPending"/>, <see cref="UploadFailed"/>,
    // <see cref="UploadTimedOut"/> for the new vocabulary used in user-facing output.
    public const string UploadNotTested = "not tested";

    public const string UploadProbesOff = "probes off";
    public const string UploadPending = "pending";
    public const string UploadFailed = "failed this cycle";
    public const string UploadTimedOut = "timed out";

    // Public surface labels — kept stable so XAML, brushes, and tests can reference them.
    public const string SurfaceOnline = "Online";

    // The previous "Online — checks inconsistent" label has been retired in favour of
    // "Partial check" which honestly says: the current cycle did not finish a full
    // ping + download + upload measurement, but partial evidence of usability exists.
    public const string SurfaceChecksInconsistent = "Partial check";
    public const string SurfacePartialCheck = "Partial check";
    public const string SurfaceLimited = "Limited";
    public const string SurfaceOffline = "Offline";
    public const string SurfaceMeasuring = "Measuring";
    public const string SurfaceStale = "Stale";
    public const string SurfaceUnknown = "Unknown";

    public static (string Line1, string Line2, string Line3) Build(
        bool userEnabled,
        bool showInHeader,
        NetworkPulseSnapshot s,
        NetworkPulseSmoothedHeadline sm,
        NetworkPulseLastKnownGood last,
        bool uploadProbesEnabled,
        int consecutiveHardFailures,
        TimeSpan freshnessStaleAfter,
        DateTimeOffset utcNow,
        int verificationInconsistentStreak = 0)
    {
        if (!userEnabled || !showInHeader)
        {
            return ("Internet: Disabled", "Enable Network Pulse in Settings to monitor this PC.", string.Empty);
        }

        if (s.Status == NetworkPulseStatus.Paused)
        {
            var reason = string.IsNullOrWhiteSpace(s.PauseReason) ? "Policy pause." : s.PauseReason.Trim();
            var shortReason = reason.Length > 72 ? reason[..69] + "…" : reason;
            var isUserOff = reason.Contains("turned off", StringComparison.OrdinalIgnoreCase);
            var head = isUserOff ? "Internet: Disabled" : "Internet: Paused";
            var resume = "Probes resume automatically when conditions clear.";
            var line2Paused = string.IsNullOrEmpty(shortReason) ? resume : $"{shortReason} · {resume}";
            return (head, line2Paused, string.Empty);
        }

        if (s.Status == NetworkPulseStatus.Testing)
        {
            return (
                "Internet: Testing…",
                "Down: testing · Up: testing · Ping: testing",
                "Measuring now");
        }

        var pingMeasured = s.LatencyKind == NetworkPulseMeasurementKind.Measured && s.PingMs is > 0;
        var pingShow = pingMeasured ? s.PingMs : last.PingMs;
        var pingCarried = !pingMeasured && last.PingMs is > 0;
        var pingAge = pingCarried ? utcNow - last.PingUtc : TimeSpan.Zero;

        var downMeasured = s.DownloadKind == NetworkPulseMeasurementKind.Measured &&
                             NetworkPulseSpeedSanity.IsPlausibleMeasuredMbps(s.DownloadMbps);
        double? downDisplay = downMeasured ? s.DownloadMbps : null;
        if (downDisplay is null && NetworkPulseSpeedSanity.IsPlausibleMeasuredMbps(last.DownloadMbps))
        {
            downDisplay = last.DownloadMbps;
        }

        var upMeasured = uploadProbesEnabled &&
                         s.UploadKind == NetworkPulseMeasurementKind.Measured &&
                         NetworkPulseSpeedSanity.IsPlausibleMeasuredMbps(s.UploadMbps);

        var speedFailedThisCycle = IsSpeedFailure(s);
        var surface = ClassifySurface(s, pingShow, downDisplay, consecutiveHardFailures, freshnessStaleAfter, utcNow, last, speedFailedThisCycle, uploadProbesEnabled);

        var pingMetric = pingShow is > 0
            ? (pingCarried && pingAge > TimeSpan.FromSeconds(5)
                ? $"Ping: {pingShow.Value.ToString("0", CultureInfo.InvariantCulture)} ms (last good {FormatAge(pingAge)} ago)"
                : $"Ping: {pingShow.Value.ToString("0", CultureInfo.InvariantCulture)} ms")
            : "Latency unavailable";

        // Only update the "Full check Xs ago" / "Partial check Xs ago" stamp when the snapshot
        // says the current cycle resolved. CycleComplete is true only when ping + download have
        // a measured value AND either upload returned a measured value OR upload probes are
        // intentionally disabled in settings. Anything else is reported as a partial check so
        // the user is not misled by a fresh download timestamp that hides a missing upload.
        //
        // For backward compatibility with older snapshots (and tests) that did not set
        // CycleComplete, fall back to inferring completeness from the measurement kinds and the
        // uploadProbesEnabled flag passed by the caller. Pre-v3 callers always passed
        // uploadProbesEnabled:false, so the fallback treats download+latency-measured as a full
        // check in that case — preserving the historical "Checked Xs ago" wording for the
        // disabled-upload path while honest "Partial check" wording kicks in for the upload-on
        // path where upload actually went missing.
        var uploadCoveredForFullCheck =
            s.UploadKind == NetworkPulseMeasurementKind.Measured ||
            s.UploadSkipReason == NetworkPulseSkipReason.ProbesDisabledInSettings ||
            !uploadProbesEnabled;
        var fullCheck = s.CycleComplete ||
                        (uploadCoveredForFullCheck &&
                         s.DownloadKind == NetworkPulseMeasurementKind.Measured &&
                         s.LatencyKind == NetworkPulseMeasurementKind.Measured &&
                         s.InternetReachable);

        // When upload probes are off (legacy / opt-out path) preserve the original
        // "Checked Xs ago" wording regardless of completeness — that's what existing UI tests
        // and users expect. The cycle-aware "Full check" / "Partial check" wording only
        // activates when upload probes are enabled, which is now the default for v1.2.3+.
        string checkedPart;
        if (s.LastCheckedUtc == DateTimeOffset.MinValue)
        {
            checkedPart = "Starting network checks…";
        }
        else if (!uploadProbesEnabled)
        {
            checkedPart = $"Checked {FormatAge(utcNow - s.LastCheckedUtc)} ago";
        }
        else if (fullCheck)
        {
            checkedPart = $"Full check {FormatAge(utcNow - s.LastCheckedUtc)} ago";
        }
        else
        {
            checkedPart = $"Partial check {FormatAge(utcNow - s.LastCheckedUtc)} ago";
        }

        var line1 = $"Internet: {surface}";

        string downText;
        if (downDisplay is null or <= 0)
        {
            downText = FormatDownSkipText(s.DownloadSkipReason, speedFailedThisCycle);
        }
        else if (!downMeasured)
        {
            var ageNote = "last measured earlier";
            if (last.DownloadUtc != default &&
                NetworkPulseSpeedSanity.IsPlausibleMeasuredMbps(last.DownloadMbps) &&
                Math.Abs(last.DownloadMbps!.Value - downDisplay!.Value) < 0.001)
            {
                var age = utcNow - last.DownloadUtc;
                if (age > freshnessStaleAfter)
                {
                    ageNote = $"stale; last good {FormatAge(age)} ago";
                }
                else
                {
                    ageNote = speedFailedThisCycle
                        ? $"last good {FormatAge(age)} ago"
                        : $"last measured {FormatAge(age)} ago";
                }
            }

            downText = $"Down: {FormatMbps(downDisplay)} Mbps ({ageNote})";
        }
        else
        {
            downText = $"Down: {FormatMbps(downDisplay)} Mbps";
        }

        // Upload text: when probes are off keep the legacy "not tested" string for backwards
        // compatibility with existing UI tests; when probes are on, the new skip-reason
        // vocabulary distinguishes pending / failed / timed out / discarded clearly.
        string upText;
        if (upMeasured)
        {
            upText = $"Up: {FormatMbps(s.UploadMbps)} Mbps";
        }
        else if (!uploadProbesEnabled)
        {
            upText = $"Up: {UploadNotTested}";
        }
        else
        {
            upText = $"Up: {FormatUploadSkipText(s.UploadSkipReason, uploadProbesEnabled)}";
        }
        var line2 = $"{downText} · {upText} · {pingMetric}";
        var detail = BuildFreshnessDetail(surface, checkedPart, speedFailedThisCycle, s, pingShow, downDisplay, verificationInconsistentStreak, uploadProbesEnabled);
        return (line1, line2, detail);
    }

    private static string FormatUploadSkipText(NetworkPulseSkipReason reason, bool uploadProbesEnabled)
    {
        if (!uploadProbesEnabled)
        {
            return UploadProbesOff;
        }

        return reason switch
        {
            NetworkPulseSkipReason.ProbesDisabledInSettings => UploadProbesOff,
            NetworkPulseSkipReason.NotDueThisCycle => UploadPending,
            NetworkPulseSkipReason.ThrottledByHostActivity => "deferred (host activity)",
            NetworkPulseSkipReason.Cancelled => "cancelled this cycle",
            NetworkPulseSkipReason.TimedOut => UploadTimedOut,
            NetworkPulseSkipReason.ImplausibleResult => "discarded (implausible result)",
            NetworkPulseSkipReason.ProbeFailed => UploadFailed,
            _ => UploadPending
        };
    }

    private static string FormatDownSkipText(NetworkPulseSkipReason reason, bool speedFailedThisCycle)
    {
        if (speedFailedThisCycle)
        {
            return "Down: unavailable this cycle";
        }

        return reason switch
        {
            NetworkPulseSkipReason.ProbesDisabledInSettings => "Down: probes off",
            NetworkPulseSkipReason.NotDueThisCycle => "Down: pending",
            NetworkPulseSkipReason.ThrottledByHostActivity => "Down: deferred (host activity)",
            NetworkPulseSkipReason.TimedOut => "Down: timed out",
            NetworkPulseSkipReason.ImplausibleResult => "Down: discarded (implausible result)",
            NetworkPulseSkipReason.ProbeFailed => "Down: failed this cycle",
            _ => "Down: pending"
        };
    }

    private static string ClassifySurface(
        NetworkPulseSnapshot s,
        double? pingShow,
        double? downShow,
        int consecutiveHardFailures,
        TimeSpan freshnessStaleAfter,
        DateTimeOffset utcNow,
        NetworkPulseLastKnownGood last,
        bool speedFailedThisCycle,
        bool uploadProbesEnabled)
    {
        if (s.Status == NetworkPulseStatus.Testing)
        {
            return SurfaceMeasuring;
        }

        if (s.Status == NetworkPulseStatus.Limited)
        {
            return SurfaceLimited;
        }

        if (consecutiveHardFailures >= 6 && !s.InternetReachable && s.LatencyKind != NetworkPulseMeasurementKind.Measured)
        {
            return SurfaceOffline;
        }

        if (s.Status == NetworkPulseStatus.Offline && pingShow is null && downShow is null)
        {
            return SurfaceOffline;
        }

        if (s.Status == NetworkPulseStatus.Offline)
        {
            return SurfaceLimited;
        }

        var newestGood = MaxUtc(last.PingUtc, last.DownloadUtc, last.UploadUtc);
        var stale = newestGood != default && (utcNow - newestGood) > freshnessStaleAfter;

        if (stale && (pingShow is > 0 || downShow is > 0))
        {
            return SurfaceStale;
        }

        // "Partial check": the current cycle is missing measured upload, download, or ping while
        // partial evidence of usability exists. Avoids implying a clean Online state when an
        // upload sample failed, timed out, or has not run yet.
        var probeMismatch = !s.InternetReachable &&
                            s.Status is NetworkPulseStatus.Good or NetworkPulseStatus.Slow or NetworkPulseStatus.Unstable &&
                            (s.LatencyKind == NetworkPulseMeasurementKind.Measured ||
                             pingShow is > 0 ||
                             downShow is > 0 ||
                             s.DnsLookupMs is > 0);
        if (probeMismatch)
        {
            return SurfacePartialCheck;
        }

        if (!s.InternetReachable && (s.LatencyKind == NetworkPulseMeasurementKind.Measured || pingShow is > 0))
        {
            return SurfacePartialCheck;
        }

        // Cycle did not fully resolve but we still have usable evidence: report Partial check
        // instead of a misleading Online label — but only when upload probes are actually
        // expected to run this cycle. If upload probes are disabled (legacy default), a healthy
        // ping + reachability snapshot remains "Online" so we do not surprise existing users.
        var uploadExpected = uploadProbesEnabled;
        var uploadCompletedOrDisabled =
            !uploadExpected ||
            s.UploadKind == NetworkPulseMeasurementKind.Measured ||
            s.UploadSkipReason == NetworkPulseSkipReason.ProbesDisabledInSettings;
        var cycleIncomplete = uploadExpected &&
                              !s.CycleComplete &&
                              s.Status is NetworkPulseStatus.Good or NetworkPulseStatus.Slow &&
                              (!uploadCompletedOrDisabled ||
                               s.DownloadKind != NetworkPulseMeasurementKind.Measured);
        if (cycleIncomplete && (pingShow is > 0 || downShow is > 0))
        {
            return SurfacePartialCheck;
        }

        if (s.LatencyKind != NetworkPulseMeasurementKind.Measured && pingShow is null)
        {
            return s.InternetReachable ? SurfaceOnline : consecutiveHardFailures >= 2 ? SurfaceLimited : SurfaceUnknown;
        }

        if (s.Status is NetworkPulseStatus.Unstable or NetworkPulseStatus.Unknown)
        {
            return s.InternetReachable || pingShow is > 0 || downShow is > 0 ? SurfaceOnline : SurfaceUnknown;
        }

        if (s.Status is NetworkPulseStatus.Slow or NetworkPulseStatus.Good)
        {
            return SurfaceOnline;
        }

        return SurfaceUnknown;
    }

    private static bool IsSpeedFailure(NetworkPulseSnapshot s) =>
        s.DownloadSkipReason is NetworkPulseSkipReason.ProbeFailed or NetworkPulseSkipReason.TimedOut or NetworkPulseSkipReason.ImplausibleResult ||
        s.DataSourceNotes.Contains("speed check failed", StringComparison.OrdinalIgnoreCase) ||
        s.DataSourceNotes.Contains("Download sample failed", StringComparison.OrdinalIgnoreCase);

    private static string BuildFreshnessDetail(
        string surface,
        string checkedPart,
        bool speedFailedThisCycle,
        NetworkPulseSnapshot s,
        double? pingShow,
        double? downShow,
        int verificationInconsistentStreak,
        bool uploadProbesEnabled)
    {
        if (surface == SurfaceMeasuring)
        {
            return "Measuring now";
        }

        if (speedFailedThisCycle && s.InternetReachable)
        {
            return $"{checkedPart} · Speed sample unavailable this cycle; internet probe succeeded.";
        }

        if (surface == SurfaceOnline && pingShow is null && downShow is null)
        {
            return $"{checkedPart} · Internet online; latency and speed pending.";
        }

        if (surface == SurfacePartialCheck)
        {
            // Honest, specific reason for the partial state.
            var missing = DescribePartialReason(s, uploadProbesEnabled);
            if (verificationInconsistentStreak >= 3 && (pingShow is > 0 || downShow is > 0))
            {
                return $"{checkedPart} · {missing} Internet appears usable based on the metrics that did complete.";
            }

            return $"{checkedPart} · {missing}";
        }

        if (surface == SurfaceLimited)
        {
            return $"{checkedPart} · Connection checks have failed for several cycles; some sites or services may be unreachable.";
        }

        if (surface == SurfaceStale)
        {
            return $"{checkedPart} · Last measurement is stale.";
        }

        return checkedPart;
    }

    private static string DescribePartialReason(NetworkPulseSnapshot s, bool uploadProbesEnabled)
    {
        var uploadMissing = uploadProbesEnabled && s.UploadKind != NetworkPulseMeasurementKind.Measured;
        var downloadMissing = s.DownloadKind != NetworkPulseMeasurementKind.Measured;
        var latencyMissing = s.LatencyKind != NetworkPulseMeasurementKind.Measured;
        var reachMissing = !s.InternetReachable;

        if (uploadMissing && s.UploadSkipReason == NetworkPulseSkipReason.TimedOut)
        {
            return "Upload sample timed out this cycle; retrying next cycle.";
        }

        if (uploadMissing && s.UploadSkipReason == NetworkPulseSkipReason.ProbeFailed)
        {
            return "Upload sample failed this cycle; retrying next cycle.";
        }

        if (uploadMissing && s.UploadSkipReason == NetworkPulseSkipReason.NotDueThisCycle)
        {
            return "Upload sample pending — scheduled on the next probe cadence.";
        }

        if (downloadMissing && s.DownloadSkipReason == NetworkPulseSkipReason.TimedOut)
        {
            return "Download sample timed out this cycle; retrying next cycle.";
        }

        if (downloadMissing && s.DownloadSkipReason == NetworkPulseSkipReason.ProbeFailed)
        {
            return "Download sample failed this cycle; retrying next cycle.";
        }

        if (latencyMissing)
        {
            return "Latency sample missing this cycle; retrying next cycle.";
        }

        if (reachMissing)
        {
            return "Reachability probe did not confirm; other usability metrics still indicate the internet is up.";
        }

        return "Some metrics this cycle did not return a measured value.";
    }

    private static DateTimeOffset MaxUtc(DateTimeOffset a, DateTimeOffset b, DateTimeOffset c)
    {
        var m = a;
        if (b > m)
        {
            m = b;
        }

        if (c > m)
        {
            m = c;
        }

        return m;
    }

    private static string FormatMbps(double? m) =>
        m!.Value >= 100 ? m.Value.ToString("0", CultureInfo.InvariantCulture) : m.Value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        if (age.TotalSeconds < 90)
        {
            return $"{(int)Math.Round(age.TotalSeconds)}s";
        }

        if (age.TotalMinutes < 90)
        {
            return $"{(int)Math.Round(age.TotalMinutes)}m";
        }

        return $"{(int)Math.Round(age.TotalHours)}h";
    }
}
