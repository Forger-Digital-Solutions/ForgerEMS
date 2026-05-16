using System;
using System.Collections.Generic;
using System.Linq;

namespace VentoyToolkitSetup.Wpf.Services.NetworkPulse;

/// <summary>
/// Bounded history + hysteresis to avoid header status flapping on brief Wi‑Fi blips.
/// </summary>
public sealed class NetworkPulseStabilizer
{
    private const int PingHistoryCap = 36;
    private const int OfflineConfirmSamples = 3;
    private const int UnstableConfirmSamples = 2;

    private readonly List<double?> _pingHistory = new(PingHistoryCap);
    private int _reachFailStreak;
    private int _reachOkStreak;
    private int _unstableConfirmCounter;
    private NetworkPulseStatus _lastNonUnstableRaw = NetworkPulseStatus.Unknown;
    private double? _emaDownMbps;
    private double? _emaUpMbps;
    private int _reconnectHoldTicks;

    public void Reset()
    {
        _pingHistory.Clear();
        _reachFailStreak = 0;
        _reachOkStreak = 0;
        _unstableConfirmCounter = 0;
        _lastNonUnstableRaw = NetworkPulseStatus.Unknown;
        _emaDownMbps = null;
        _emaUpMbps = null;
        _reconnectHoldTicks = 0;
    }

    public NetworkPulseSmoothedHeadline Smooth(
        NetworkPulseStatus raw,
        bool reach,
        bool icmpOk,
        double? pingMs,
        double? jitterMs,
        double lossPercent,
        double? downloadMbps,
        double? uploadMbps,
        NetworkPulseMeasurementKind downKind,
        NetworkPulseMeasurementKind upKind)
    {
        PushPing(pingMs);

        if (reach && icmpOk)
        {
            _reachOkStreak++;
            _reachFailStreak = 0;
        }
        else
        {
            _reachFailStreak++;
            _reachOkStreak = 0;
        }

        var emaDown = SmoothMbps(_emaDownMbps, downloadMbps, downKind);
        var emaUp = SmoothMbps(_emaUpMbps, uploadMbps, upKind);
        _emaDownMbps = emaDown;
        _emaUpMbps = emaUp;

        if (raw == NetworkPulseStatus.Unstable)
        {
            _unstableConfirmCounter++;
        }
        else
        {
            if (raw is NetworkPulseStatus.Good or NetworkPulseStatus.Slow)
            {
                _lastNonUnstableRaw = raw;
            }

            _unstableConfirmCounter = 0;
        }

        var (display, chip) = Resolve(raw, reach, icmpOk);

        if (display != NetworkPulseStatus.Offline && display != NetworkPulseStatus.Unknown)
        {
            _reconnectHoldTicks = 0;
        }

        var spark = BuildSparkPing();

        return new NetworkPulseSmoothedHeadline(
            display,
            chip,
            emaDown,
            emaUp,
            spark,
            _reachFailStreak,
            _unstableConfirmCounter);
    }

    private void PushPing(double? pingMs)
    {
        if (_pingHistory.Count >= PingHistoryCap)
        {
            _pingHistory.RemoveAt(0);
        }

        _pingHistory.Add(pingMs is > 0 ? pingMs : null);
    }

    private static double? SmoothMbps(double? ema, double? sample, NetworkPulseMeasurementKind kind)
    {
        if (kind != NetworkPulseMeasurementKind.Measured || sample is not > 0)
        {
            return ema;
        }

        const double alpha = 0.35;
        return ema.HasValue ? (ema.Value * (1 - alpha) + sample.Value * alpha) : sample;
    }

    private (NetworkPulseStatus Display, string Chip) Resolve(NetworkPulseStatus raw, bool reach, bool icmpOk)
    {
        if (raw is NetworkPulseStatus.Paused or NetworkPulseStatus.Testing)
        {
            return (raw, Chip(raw, null));
        }

        if (!reach && !icmpOk)
        {
            if (_reachFailStreak < OfflineConfirmSamples)
            {
                _reconnectHoldTicks = Math.Max(_reconnectHoldTicks, 2);
            }

            if (_reachFailStreak < OfflineConfirmSamples || _reconnectHoldTicks > 0)
            {
                if (_reconnectHoldTicks > 0)
                {
                    _reconnectHoldTicks--;
                }

                return (NetworkPulseStatus.Unknown, "Reconnecting…");
            }

            return (NetworkPulseStatus.Offline, Chip(NetworkPulseStatus.Offline, null));
        }

        if (raw == NetworkPulseStatus.Unstable && _unstableConfirmCounter < UnstableConfirmSamples)
        {
            var held = _lastNonUnstableRaw is NetworkPulseStatus.Good or NetworkPulseStatus.Slow
                ? _lastNonUnstableRaw
                : NetworkPulseStatus.Good;
            return (held, Chip(held, "Calm"));
        }

        if (raw == NetworkPulseStatus.Unstable)
        {
            return (NetworkPulseStatus.Unstable, Chip(NetworkPulseStatus.Unstable, null));
        }

        return (raw, Chip(raw, null));
    }

    private static string Chip(NetworkPulseStatus status, string? note)
    {
        if (!string.IsNullOrEmpty(note) && note.Equals("Calm", StringComparison.Ordinal))
        {
            return "Stable";
        }

        return status switch
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

    private double[] BuildSparkPing()
    {
        var vals = _pingHistory.Where(v => v is > 0).Select(v => v!.Value).ToArray();
        if (vals.Length < 2)
        {
            return Array.Empty<double>();
        }

        var window = vals.Length > 24 ? vals.AsSpan(vals.Length - 24).ToArray() : vals;
        var min = window.Min();
        var max = window.Max();
        var span = Math.Max(1e-6, max - min);
        return window.Select(v => (v - min) / span).ToArray();
    }
}

public sealed record NetworkPulseSmoothedHeadline(
    NetworkPulseStatus DisplayStatus,
    string StatusChipText,
    double? SmoothedDownMbps,
    double? SmoothedUpMbps,
    IReadOnlyList<double> SparkPingNormalized,
    int RecentReachFailures,
    int RecentUnstableRawStreak);
