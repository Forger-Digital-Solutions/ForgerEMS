using System;
using System.Globalization;

namespace VentoyToolkitSetup.Wpf.Services.NetworkPulse;

/// <summary>User-facing compact Internet widget strings (Line1 includes freshness; Line2 speeds; Line3 unused).</summary>
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
        var upLine = upMeasured ? $"↑ {FormatMbps(s.UploadMbps)} Mbps" : $"↑ {UploadNotTested}";

        var speedFailedThisCycle = IsSpeedFailure(s);
        var surface = ClassifySurface(s, pingShow, downDisplay, consecutiveHardFailures, freshnessStaleAfter, utcNow, last, speedFailedThisCycle);

        var pingPart = pingShow is > 0
            ? (pingCarried && pingAge > TimeSpan.FromSeconds(5)
                ? $"{pingShow.Value.ToString("0", CultureInfo.InvariantCulture)} ms (last good {FormatAge(pingAge)} ago)"
                : $"{pingShow.Value.ToString("0", CultureInfo.InvariantCulture)} ms")
            : "ping —";

        var checkedPart = s.LastCheckedUtc == DateTimeOffset.MinValue
            ? "ping starting…"
            : $"ping checked {FormatAge(utcNow - s.LastCheckedUtc)} ago";
        if (speedFailedThisCycle && surface == "Limited")
        {
            checkedPart = "speed check failed";
        }

        var line1 = $"Internet: {surface} · {pingPart} · {checkedPart}";

        string downText;
        if (downDisplay is null or <= 0)
        {
            downText = speedFailedThisCycle ? "↓ speed check failed" : "↓ not tested";
        }
        else if (!downMeasured)
        {
            var ageNote = "last tested earlier";
            if (last.DownloadUtc != default &&
                NetworkPulseSpeedSanity.IsPlausibleMeasuredMbps(last.DownloadMbps) &&
                Math.Abs(last.DownloadMbps!.Value - downDisplay!.Value) < 0.001)
            {
                var age = utcNow - last.DownloadUtc;
                if (age > freshnessStaleAfter)
                {
                    ageNote = $"speed stale; last good {FormatAge(age)} ago";
                }
                else
                {
                    ageNote = speedFailedThisCycle
                        ? $"last good {FormatAge(age)} ago"
                        : $"last tested {FormatAge(age)} ago";
                }
            }

            downText = $"↓ {FormatMbps(downDisplay)} Mbps {ageNote}";
        }
        else
        {
            downText = $"↓ {FormatMbps(downDisplay)} Mbps";
        }

        var line2 = $"{downText} · {upLine}";
        return (line1, line2, string.Empty);
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

        if (speedFailedThisCycle && (s.LatencyKind == NetworkPulseMeasurementKind.Measured || pingShow is > 0))
        {
            return "Limited";
        }

        if (s.LatencyKind != NetworkPulseMeasurementKind.Measured && pingShow is null)
        {
            return consecutiveHardFailures >= 2 ? "Limited" : "Unknown";
        }

        if (s.Status is NetworkPulseStatus.Unstable or NetworkPulseStatus.Unknown)
        {
            return pingShow is > 0 || downShow is > 0 ? "Limited" : "Unknown";
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
