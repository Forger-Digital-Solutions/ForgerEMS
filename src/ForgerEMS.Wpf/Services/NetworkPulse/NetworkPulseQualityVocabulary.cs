using System;
using System.Globalization;

namespace VentoyToolkitSetup.Wpf.Services.NetworkPulse;

public static class NetworkPulseQualityVocabulary
{
    public static string PingQuality(double? pingMs) =>
        pingMs switch
        {
            null or <= 0 => "—",
            < 25 => "Excellent",
            < 55 => "Good",
            < 120 => "Moderate",
            _ => "Poor"
        };

    public static string JitterQuality(double? jitterMs) =>
        jitterMs switch
        {
            null or <= 0 => "—",
            < 8 => "Excellent",
            < 20 => "Good",
            < 45 => "Moderate",
            _ => "Poor"
        };

    public static string LossQuality(double lossPercent)
    {
        if (lossPercent <= 0.0001)
        {
            return "Healthy";
        }

        if (lossPercent < 2)
        {
            return "Minor loss";
        }

        if (lossPercent < 8)
        {
            return "Unstable";
        }

        return "Severe loss";
    }

    public static string FormatPingLine(double? pingMs, NetworkPulseMeasurementKind kind)
    {
        if (kind != NetworkPulseMeasurementKind.Measured || pingMs is not > 0)
        {
            return $"— ({KindWord(kind)})";
        }

        var q = PingQuality(pingMs);
        return $"{pingMs.Value.ToString("0.0", CultureInfo.InvariantCulture)} ms ({q})";
    }

    public static string FormatJitterLine(double? jitterMs)
    {
        if (jitterMs is not > 0)
        {
            return "— (not measured this cycle)";
        }

        var q = JitterQuality(jitterMs);
        return $"{jitterMs.Value.ToString("0.0", CultureInfo.InvariantCulture)} ms ({q})";
    }

    public static string FormatLossLine(double? lossPercent)
    {
        var v = lossPercent ?? 0;
        var q = LossQuality(v);
        return $"{v.ToString("0.0", CultureInfo.InvariantCulture)}% ({q})";
    }

    private static string KindWord(NetworkPulseMeasurementKind k) =>
        k switch
        {
            NetworkPulseMeasurementKind.Measured => "measured",
            NetworkPulseMeasurementKind.Estimated => "estimated",
            NetworkPulseMeasurementKind.Unavailable => "not measured this cycle",
            NetworkPulseMeasurementKind.Paused => "paused",
            _ => "not measured this cycle"
        };
}
