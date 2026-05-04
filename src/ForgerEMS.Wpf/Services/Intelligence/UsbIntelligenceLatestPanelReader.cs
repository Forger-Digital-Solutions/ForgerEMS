using System;
using System.Globalization;
using System.Text.Json;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

/// <summary>Safe, path-free strings for the USB Intelligence Pro panel (from usb-intelligence-latest.json).</summary>
public sealed record UsbIntelligencePanelUiState
{
    public string BuilderSummaryLine { get; init; } = string.Empty;

    public string DetectedClassDisplay { get; init; } = "—";

    public string BenchmarkReadWriteDisplay { get; init; } = "—";

    public string RecommendationQualityDisplay { get; init; } = "—";

    public string ConfidenceScoreDisplay { get; init; } = "—";

    public string ConfidenceReasonDisplay { get; init; } = string.Empty;

    public string LastBenchmarkTimeDisplay { get; init; } = "—";

    public string MappingLabelDisplay { get; init; } = "—";

    /// <summary>From embedded USB diagnostics (best ranked port).</summary>
    public string BestKnownPortSummary { get; init; } = string.Empty;

    public string BenchmarkAgeSummary { get; init; } = "—";

    public string RunBenchmarkRecommendedLine { get; init; } = string.Empty;
}

public static class UsbIntelligenceLatestPanelReader
{
    public static UsbIntelligencePanelUiState Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return Parse(doc.RootElement);
        }
        catch
        {
            return UsbIntelligencePanelUiCopy.FinalizeForDisplay(
                new UsbIntelligencePanelUiState(),
                benchmarkSucceeded: false,
                combinedConfidenceScore: 0,
                benchmarkTimestampUtc: null,
                bestKnownPortSummaryFromDiagnostics: null);
        }
    }

    public static UsbIntelligencePanelUiState Parse(JsonElement root)
    {
        var line = string.Empty;
        var quality = "—";
        var confScore = "—";
        var confReason = string.Empty;
        var detected = "—";
        var benchLine = "—";
        var lastBench = "—";
        var mapping = "—";
        var bestPortFromDiag = string.Empty;
        var benchSucceeded = false;
        DateTimeOffset? benchStamp = null;
        var combinedScore = 0;

        if (root.TryGetProperty("usbDiagnostics", out var usbDiag) && usbDiag.ValueKind == JsonValueKind.Object)
        {
            bestPortFromDiag = GetStr(usbDiag, "usbBestKnownPortSummary");
        }

        if (root.TryGetProperty("selectedTargetRecommendation", out var rec) && rec.ValueKind == JsonValueKind.Object)
        {
            var summary = GetStr(rec, "summary");
            var detail = GetStr(rec, "detail");
            var risk = GetStr(rec, "risk");
            var speed = GetStr(rec, "speed");
            var q = GetStr(rec, "quality");
            var classLine = GetStr(rec, "classificationLine");
            var tier = string.IsNullOrWhiteSpace(q) ? string.Empty : $"[{TitleCaseToken(q)}] ";
            var clsLine = string.IsNullOrWhiteSpace(classLine) ? string.Empty : $"{classLine} ";
            line = $"{tier}{clsLine}Risk: {TitleCaseToken(risk)} | Speed class: {FormatSpeedClass(speed)} | {summary} — {detail}".Trim();
            quality = string.IsNullOrWhiteSpace(q) ? "—" : TitleCaseToken(q);
            confScore = rec.TryGetProperty("confidenceScore", out var cs) && cs.TryGetInt32(out var n)
                ? $"{n}%"
                : "—";
            confReason = GetStr(rec, "confidenceReason");
            var measured = GetStr(rec, "measuredClassification");
            detected = string.IsNullOrWhiteSpace(measured)
                ? FormatSpeedClass(speed)
                : FormatMeasurementClass(measured);
            if (string.IsNullOrWhiteSpace(detected))
            {
                detected = UsbIntelligencePanelUiCopy.NotMeasuredClass;
            }
        }

        if (root.TryGetProperty("selectedTargetBenchmark", out var bench) && bench.ValueKind == JsonValueKind.Object &&
            bench.TryGetProperty("succeeded", out var ok) && ok.ValueKind == JsonValueKind.True)
        {
            benchSucceeded = true;
            var w = bench.TryGetProperty("writeSpeedMBps", out var ww) ? ww.GetDouble() : 0;
            var r = bench.TryGetProperty("readSpeedMBps", out var rr) ? rr.GetDouble() : 0;
            benchLine =
                $"{r.ToString("0.0", CultureInfo.InvariantCulture)} MB/s read · {w.ToString("0.0", CultureInfo.InvariantCulture)} MB/s write";

            if (bench.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(ts.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
            {
                benchStamp = dto;
                lastBench = dto.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
            }

            var bCls = GetStr(bench, "classification");
            if (!string.IsNullOrWhiteSpace(bCls))
            {
                detected = FormatMeasurementClass(bCls);
            }
        }
        else if (root.TryGetProperty("selectedTargetBenchmark", out var benchF) &&
                 benchF.ValueKind == JsonValueKind.Object)
        {
            benchLine = "No successful benchmark yet.";
        }

        if (root.TryGetProperty("combinedConfidenceScore", out var ccs) && ccs.TryGetInt32(out var cscore))
        {
            combinedScore = cscore;
            confScore = $"{cscore}%";
            var reason = GetStr(root, "combinedConfidenceReason");
            if (!string.IsNullOrWhiteSpace(reason))
            {
                confReason = reason;
            }
        }

        var label = GetStr(root, "selectedTargetPortUserLabel");
        if (!string.IsNullOrWhiteSpace(label))
        {
            mapping = label.Trim();
        }

        var raw = new UsbIntelligencePanelUiState
        {
            BuilderSummaryLine = line,
            DetectedClassDisplay = detected,
            BenchmarkReadWriteDisplay = benchLine,
            RecommendationQualityDisplay = quality,
            ConfidenceScoreDisplay = confScore,
            ConfidenceReasonDisplay = confReason,
            LastBenchmarkTimeDisplay = lastBench,
            MappingLabelDisplay = mapping,
            BestKnownPortSummary = bestPortFromDiag
        };

        return UsbIntelligencePanelUiCopy.FinalizeForDisplay(
            raw,
            benchSucceeded,
            combinedScore,
            benchStamp,
            bestPortFromDiag);
    }

    private static string GetStr(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? string.Empty
            : string.Empty;

    private static string TitleCaseToken(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var s = raw.Trim();
        return s.Length switch
        {
            0 => string.Empty,
            1 => s.ToUpperInvariant(),
            _ => char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant()
        };
    }

    private static string FormatSpeedClass(string camel) =>
        camel.Trim().ToLowerInvariant() switch
        {
            "usb2" => "USB 2",
            "usb3" => "USB 3",
            "usbc" => "USB-C",
            "unknown" => UsbIntelligencePanelUiCopy.NotMeasuredClass,
            _ => string.IsNullOrWhiteSpace(camel) ? UsbIntelligencePanelUiCopy.NotMeasuredClass : TitleCaseToken(camel)
        };

    /// <summary>Maps JSON enum token to UI label (tests).</summary>
    public static string FormatMeasurementClassForDisplay(string camel) => FormatMeasurementClass(camel);

    private static string FormatMeasurementClass(string camel) =>
        camel.Trim().ToLowerInvariant() switch
        {
            "usb2" => "USB 2",
            "usb3" => "USB 3",
            "usbc" => "USB-C",
            "bottleneck" => "Bottleneck",
            "unknown" => UsbIntelligencePanelUiCopy.NotMeasuredClass,
            _ => string.IsNullOrWhiteSpace(camel) ? UsbIntelligencePanelUiCopy.NotMeasuredClass : TitleCaseToken(camel)
        };
}
