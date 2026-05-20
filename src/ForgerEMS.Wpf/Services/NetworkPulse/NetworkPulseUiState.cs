using System;
using System.Windows.Media;

namespace VentoyToolkitSetup.Wpf.Services.NetworkPulse;

public sealed record NetworkPulseUiState(
    NetworkPulseSnapshot Snapshot,
    NetworkPulseSmoothedHeadline Smoothed,
    NetworkPulseReliabilityTier ReliabilityTier,
    string ReliabilityExplain,
    string? WifiContextLine,
    Geometry? PingSparklineGeometry,
    string SmoothingNote,
    string ClipboardSummaryLine,
    string InternetWidgetLine1,
    string InternetWidgetLine2,
    string InternetWidgetLine3)
{
    public static NetworkPulseUiState FromSnapshotNeutral(NetworkPulseSnapshot snapshot)
    {
        var head = new NetworkPulseSmoothedHeadline(
            snapshot.Status,
            HeaderChip(snapshot.Status),
            snapshot.DownloadMbps,
            snapshot.UploadMbps,
            Array.Empty<double>(),
            0,
            0);
        var userEnabled = !snapshot.PauseReason.Contains("turned off", StringComparison.OrdinalIgnoreCase);
        var (l1, l2, l3) = NetworkPulseInternetWidgetText.Build(
            userEnabled,
            showInHeader: true,
            snapshot,
            head,
            new NetworkPulseLastKnownGood(),
            uploadProbesEnabled: false,
            consecutiveHardFailures: 0,
            freshnessStaleAfter: TimeSpan.FromMinutes(10),
            DateTimeOffset.UtcNow);

        return new NetworkPulseUiState(
            snapshot,
            head,
            NetworkPulseReliabilityTier.Unknown,
            NetworkPulseReliabilityScorer.Explain(
                NetworkPulseReliabilityTier.Unknown,
                snapshot.InternetReachable,
                snapshot.LatencyKind == NetworkPulseMeasurementKind.Measured,
                snapshot.PacketLossPercent ?? 0),
            null,
            null,
            string.Empty,
            snapshot.Status == NetworkPulseStatus.Paused && !string.IsNullOrWhiteSpace(snapshot.PauseReason)
                ? $"Network Pulse — {snapshot.PauseReason}"
                : "Network Pulse — not active for this view.",
            l1,
            l2,
            l3);
    }

    private static string HeaderChip(NetworkPulseStatus status) =>
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
