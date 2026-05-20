using System;
using System.Globalization;

namespace VentoyToolkitSetup.Wpf.Services.NetworkPulse;

/// <summary>User-facing compact Internet widget strings: status, metrics, then freshness/detail.</summary>
public static class NetworkPulseInternetWidgetText
{
    public const string UploadNotTested = "not tested";

    public static (string Line1, string Line2, string Line3) Build(
        bool userEnabled,
        bool showInHeader,
        NetworkPulseSnapshot s,
        NetworkPulseSmoothedHeadline sm,
        NetworkPulseLastKnownGood last,
        bool uploadProbesEnabled,
        int consecutiveHardFailures,
        TimeSpan freshnessStaleAfter,
        DateTimeOffset utcNow)
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
        var surface = ClassifySurface(s, pingShow, downDisplay, consecutiveHardFailures, freshnessStaleAfter, utcNow, last, speedFailedThisCycle);

        var pingMetric = pingShow is > 0
            ? (pingCarried && pingAge > TimeSpan.FromSeconds(5)
                ? $"Ping: {pingShow.Value.ToString("0", CultureInfo.InvariantCulture)} ms (last good {FormatAge(pingAge)} ago)"
                : $"Ping: {pingShow.Value.ToString("0", CultureInfo.InvariantCulture)} ms")
            : "Latency unavailable";

        var checkedPart = s.LastCheckedUtc == DateTimeOffset.MinValue
            ? "Starting network checks…"
            : $"Checked {FormatAge(utcNow - s.LastCheckedUtc)} ago";
        if (s.Status == NetworkPulseStatus.Testing)
        {
            checkedPart = "Measuring now";
        }

        var line1 = $"Internet: {surface}";

        string downText;
        if (downDisplay is null or <= 0)
        {
            downText = speedFailedThisCycle ? "Down: unavailable this cycle" : "Down: not measured";
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

        var upText = upMeasured ? $"Up: {FormatMbps(s.UploadMbps)} Mbps" : $"Up: {UploadNotTested}";
        var line2 = $"{downText} · {upText} · {pingMetric}";
        var detail = BuildFreshnessDetail(surface, checkedPart, speedFailedThisCycle, s, pingShow, downDisplay);
        return (line1, line2, detail);
    }

    private static string ClassifySurface(
        NetworkPulseSnapshot s,
        double? pingShow,
        double? downShow,
        int consecutiveHardFailures,
        TimeSpan freshnessStaleAfter,
        DateTimeOffset utcNow,
        NetworkPulseLastKnownGood last,
        bool speedFailedThisCycle)
    {
        if (s.Status == NetworkPulseStatus.Testing)
        {
            return "Measuring";
        }

        if (s.Status == NetworkPulseStatus.Limited)
        {
            return "Limited";
        }

        if (consecutiveHardFailures >= 6 && !s.InternetReachable && s.LatencyKind != NetworkPulseMeasurementKind.Measured)
        {
            return "Offline";
        }

        if (s.Status == NetworkPulseStatus.Offline && pingShow is null && downShow is null)
        {
            return "Offline";
        }

        if (s.Status == NetworkPulseStatus.Offline)
        {
            return "Limited";
        }

        var newestGood = MaxUtc(last.PingUtc, last.DownloadUtc, last.UploadUtc);
        var stale = newestGood != default && (utcNow - newestGood) > freshnessStaleAfter;

        if (stale && (pingShow is > 0 || downShow is > 0))
        {
            return "Stale";
        }

        if (!s.InternetReachable && (s.LatencyKind == NetworkPulseMeasurementKind.Measured || pingShow is > 0))
        {
            return "Limited";
        }

        if (s.LatencyKind != NetworkPulseMeasurementKind.Measured && pingShow is null)
        {
            return s.InternetReachable ? "Online" : consecutiveHardFailures >= 2 ? "Limited" : "Unknown";
        }

        if (s.Status is NetworkPulseStatus.Unstable or NetworkPulseStatus.Unknown)
        {
            return s.InternetReachable || pingShow is > 0 || downShow is > 0 ? "Online" : "Unknown";
        }

        if (s.Status is NetworkPulseStatus.Slow or NetworkPulseStatus.Good)
        {
            return "Online";
        }

        return "Unknown";
    }

    private static bool IsSpeedFailure(NetworkPulseSnapshot s) =>
        s.DataSourceNotes.Contains("speed check failed", StringComparison.OrdinalIgnoreCase) ||
        s.DataSourceNotes.Contains("Download sample failed", StringComparison.OrdinalIgnoreCase);

    private static string BuildFreshnessDetail(
        string surface,
        string checkedPart,
        bool speedFailedThisCycle,
        NetworkPulseSnapshot s,
        double? pingShow,
        double? downShow)
    {
        if (surface == "Measuring")
        {
            return "Measuring now";
        }

        if (speedFailedThisCycle && s.InternetReachable)
        {
            return $"{checkedPart} · Speed sample unavailable; internet probe succeeded.";
        }

        if (surface == "Online" && pingShow is null && downShow is null)
        {
            return $"{checkedPart} · Internet online; latency/speed not measured this cycle.";
        }

        if (surface == "Limited")
        {
            return $"{checkedPart} · Route exists but DNS/HTTPS/captive probes are mixed or failing.";
        }

        if (surface == "Stale")
        {
            return $"{checkedPart} · Last measurement is stale.";
        }

        return checkedPart;
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
