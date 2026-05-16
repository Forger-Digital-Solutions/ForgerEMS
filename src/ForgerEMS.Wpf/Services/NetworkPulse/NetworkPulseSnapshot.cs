namespace VentoyToolkitSetup.Wpf.Services.NetworkPulse;

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
    string SafetyNotes);
