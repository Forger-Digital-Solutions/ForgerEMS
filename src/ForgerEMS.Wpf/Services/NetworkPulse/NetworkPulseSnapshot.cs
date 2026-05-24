namespace VentoyToolkitSetup.Wpf.Services.NetworkPulse;

/// <summary>
/// Reason a Network Pulse metric was not measured this cycle. Lets the widget say
/// "Up: probes off" vs "Up: pending" vs "Up: failed" instead of a single "not tested" string
/// that hides whether the cycle actually completed.
/// </summary>
public enum NetworkPulseSkipReason
{
    /// <summary>Metric was measured (or cycle is still in flight) — no skip reason applies.</summary>
    None = 0,
    /// <summary>Probe disabled in settings (e.g. upload probes opt-out).</summary>
    ProbesDisabledInSettings,
    /// <summary>Not due yet this cycle (cadence throttle).</summary>
    NotDueThisCycle,
    /// <summary>Throttled by host activity / pause policy.</summary>
    ThrottledByHostActivity,
    /// <summary>Probe was cancelled before completion.</summary>
    Cancelled,
    /// <summary>Probe timed out before completion.</summary>
    TimedOut,
    /// <summary>Probe ran but the result was implausible/empty/discarded.</summary>
    ImplausibleResult,
    /// <summary>Probe threw an error.</summary>
    ProbeFailed
}

public sealed record NetworkPulseSnapshot(
    NetworkPulseStatus Status,
    NetworkPulseConfidence Confidence,
    bool InternetReachable,
    string AdapterName,
    NetworkPulseConnectionKind ConnectionKind,
    double? AdapterLinkMbpsEstimated,
    double? PingMs,
    double? JitterMs,
    double? PacketLossPercent,
    double? DownloadMbps,
    double? UploadMbps,
    NetworkPulseMeasurementKind DownloadKind,
    NetworkPulseMeasurementKind UploadKind,
    NetworkPulseMeasurementKind LatencyKind,
    double? GatewayPingMs,
    double? DnsLookupMs,
    DateTimeOffset LastCheckedUtc,
    string PauseReason,
    string DataSourceNotes,
    string SafetyNotes,
    NetworkPulseSkipReason DownloadSkipReason = NetworkPulseSkipReason.None,
    NetworkPulseSkipReason UploadSkipReason = NetworkPulseSkipReason.None,
    NetworkPulseSkipReason LatencySkipReason = NetworkPulseSkipReason.None,
    bool CycleComplete = false);
