using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VentoyToolkitSetup.Wpf.Services.NetworkPulse;

public static class NetworkPulseReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static DateTimeOffset _lastWriteUtc = DateTimeOffset.MinValue;

    public static void TryAppendQuickReadLines(List<string> lines, string? reportsDirectory)
    {
        if (string.IsNullOrWhiteSpace(reportsDirectory))
        {
            return;
        }

        try
        {
            var path = Path.Combine(reportsDirectory, "network-pulse-latest.json");
            if (!File.Exists(path))
            {
                return;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (!root.TryGetProperty("summaryLine", out var sum) || sum.ValueKind != JsonValueKind.String)
            {
                return;
            }

            var text = sum.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            lines.Add(string.Empty);
            lines.Add(text);
        }
        catch
        {
        }
    }

    public static void TryWriteIfDue(
        string reportsDirectory,
        NetworkPulseSnapshot snapshot,
        NetworkPulseSmoothedHeadline smoothed,
        NetworkPulseReliabilityTier reliability,
        string reliabilityExplain,
        string? wifiContext,
        TimeSpan minInterval,
        DateTimeOffset nowUtc)
    {
        try
        {
            if ((nowUtc - _lastWriteUtc) < minInterval)
            {
                return;
            }

            _lastWriteUtc = nowUtc;
            Directory.CreateDirectory(reportsDirectory);
            var path = Path.Combine(reportsDirectory, "network-pulse-latest.json");

            var down = FormatMbps(snapshot.DownloadMbps, snapshot.DownloadKind);
            var up = FormatMbps(snapshot.UploadMbps, snapshot.UploadKind);
            var conn = snapshot.ConnectionKind switch
            {
                NetworkPulseConnectionKind.Ethernet => "Ethernet",
                NetworkPulseConnectionKind.WiFi => "Wi-Fi",
                NetworkPulseConnectionKind.Other => "Other",
                _ => "Unknown"
            };

            var stability = NetworkPulseReliabilityScorer.TierLabel(reliability);
            var ping = snapshot.PingMs is > 0
                ? $"{snapshot.PingMs.Value.ToString("0", CultureInfo.InvariantCulture)} ms"
                : "—";

            var summary =
                $"Network Pulse: {conn} · {stability} · ping {ping} · measured {down}↓ / {up}↑";

            var dto = new NetworkPulseReportDto
            {
                GeneratedUtc = nowUtc,
                SummaryLine = summary,
                Connection = conn,
                Stability = stability,
                PingMs = snapshot.PingMs,
                MeasuredDownloadMbps = snapshot.DownloadKind == NetworkPulseMeasurementKind.Measured ? snapshot.DownloadMbps : null,
                MeasuredUploadMbps = snapshot.UploadKind == NetworkPulseMeasurementKind.Measured ? snapshot.UploadMbps : null,
                HeaderChip = smoothed.StatusChipText,
                ReliabilityExplain = reliabilityExplain,
                WifiContext = wifiContext,
                AdapterName = snapshot.AdapterName
            };

            File.WriteAllText(path, JsonSerializer.Serialize(dto, JsonOptions));
        }
        catch
        {
        }
    }

    private static string FormatMbps(double? mbps, NetworkPulseMeasurementKind kind)
    {
        if (kind != NetworkPulseMeasurementKind.Measured || mbps is not > 0)
        {
            return "—";
        }

        return mbps.Value >= 100
            ? mbps.Value.ToString("0", CultureInfo.InvariantCulture)
            : mbps.Value.ToString("0.#", CultureInfo.InvariantCulture);
    }

    private sealed class NetworkPulseReportDto
    {
        public DateTimeOffset GeneratedUtc { get; set; }
        public string SummaryLine { get; set; } = string.Empty;
        public string Connection { get; set; } = string.Empty;
        public string Stability { get; set; } = string.Empty;
        public double? PingMs { get; set; }
        public double? MeasuredDownloadMbps { get; set; }
        public double? MeasuredUploadMbps { get; set; }
        public string HeaderChip { get; set; } = string.Empty;
        public string ReliabilityExplain { get; set; } = string.Empty;
        public string? WifiContext { get; set; }
        public string AdapterName { get; set; } = string.Empty;
    }
}
