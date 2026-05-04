using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

/// <summary>Builds compact, redacted cross-system summaries for Kyra prompts.</summary>
public static class KyraSafeContextBuilder
{
    public const int DefaultMaxCharacters = 3600;

    public static string BuildBriefSummary(
        string? systemIntelligencePath,
        string? usbIntelligencePath,
        string? toolkitHealthPath,
        string? diagnosticsPath,
        bool enableRedaction,
        int maxCharacters = DefaultMaxCharacters)
    {
        var sb = new StringBuilder();
        AppendSystem(sb, systemIntelligencePath, enableRedaction);
        AppendUsb(sb, usbIntelligencePath);
        AppendToolkit(sb, toolkitHealthPath);
        AppendDiagnostics(sb, diagnosticsPath);

        var text = sb.ToString().Trim();
        if (text.Length <= maxCharacters)
        {
            return text;
        }

        return text[..maxCharacters] + Environment.NewLine + "[trimmed]";
    }

    private static void AppendSystem(StringBuilder sb, string? path, bool redact)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            sb.AppendLine("System Intelligence: (no local JSON yet)");
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var line = root.TryGetProperty("forgerAutomation", out var auto) &&
                       auto.TryGetProperty("summaryLine", out var s) &&
                       s.ValueKind == JsonValueKind.String
                ? s.GetString()
                : "System Intelligence loaded (summary not merged yet).";
            line ??= "System Intelligence loaded.";
            if (redact)
            {
                line = RedactLine(line);
            }

            sb.AppendLine("System Intelligence (sanitized):");
            sb.AppendLine(line);
        }
        catch
        {
            sb.AppendLine("System Intelligence: (parse error)");
        }
    }

    private static void AppendUsb(StringBuilder sb, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            sb.AppendLine("USB Intelligence: (no report yet)");
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var summary = root.TryGetProperty("summaryLine", out var s) && s.ValueKind == JsonValueKind.String
                ? s.GetString()
                : string.Empty;
            sb.AppendLine("USB Intelligence:");
            sb.AppendLine(string.IsNullOrWhiteSpace(summary) ? "(empty summary)" : RedactLine(summary!));

            if (root.TryGetProperty("selectedTargetRecommendation", out var rec) &&
                rec.ValueKind == JsonValueKind.Object)
            {
                var q = rec.TryGetProperty("quality", out var rq) ? rq.GetString() : null;
                var cl = rec.TryGetProperty("classificationLine", out var rc) ? rc.GetString() : null;
                var a = rec.TryGetProperty("summary", out var rs) ? rs.GetString() : null;
                var b = rec.TryGetProperty("detail", out var rd) ? rd.GetString() : null;
                if (!string.IsNullOrWhiteSpace(a))
                {
                    var tier = string.IsNullOrWhiteSpace(q) ? "" : $" [{q}]";
                    var head = string.IsNullOrWhiteSpace(cl) ? "" : $"{RedactLine(cl!)} ";
                    sb.AppendLine($"USB builder hint{tier}: {head}{RedactLine($"{a} {b}".Trim())}".Trim());
                }
            }

            if (root.TryGetProperty("topologyDiff", out var diff) && diff.ValueKind == JsonValueKind.Object)
            {
                var ds = diff.TryGetProperty("summaryLine", out var dss) ? dss.GetString() : null;
                if (!string.IsNullOrWhiteSpace(ds))
                {
                    sb.AppendLine($"USB topology diff: {RedactLine(ds!)}");
                }

                var dr = diff.TryGetProperty("recommendationLine", out var dsr) ? dsr.GetString() : null;
                if (!string.IsNullOrWhiteSpace(dr))
                {
                    sb.AppendLine($"USB diff follow-up: {RedactLine(dr!)}");
                }
            }

            if (root.TryGetProperty("selectedTargetBenchmark", out var bench) && bench.ValueKind == JsonValueKind.Object &&
                bench.TryGetProperty("succeeded", out var bok) && bok.ValueKind == JsonValueKind.True)
            {
                var sum = bench.TryGetProperty("summaryLine", out var bs) ? bs.GetString() : null;
                if (!string.IsNullOrWhiteSpace(sum))
                {
                    sb.AppendLine($"USB benchmark: {RedactLine(sum!)}");
                }
            }

            if (root.TryGetProperty("combinedConfidenceReason", out var comb) && comb.ValueKind == JsonValueKind.String)
            {
                var c = comb.GetString();
                if (!string.IsNullOrWhiteSpace(c))
                {
                    sb.AppendLine($"USB confidence: {RedactLine(c)}");
                }
            }
        }
        catch
        {
            sb.AppendLine("USB Intelligence: (parse error)");
        }
    }

    private static void AppendToolkit(StringBuilder sb, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            sb.AppendLine("Toolkit: (no health JSON yet)");
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("healthVerdict", out var v))
            {
                sb.AppendLine("Toolkit: loaded");
                return;
            }

            sb.AppendLine($"Toolkit verdict: {v}");
        }
        catch
        {
            sb.AppendLine("Toolkit: (parse error)");
        }
    }

    private static void AppendDiagnostics(StringBuilder sb, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            sb.AppendLine("Diagnostics: (no unified report yet)");
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var summary = root.TryGetProperty("summaryLine", out var s) ? s.ToString() : "loaded";
            var sev = root.TryGetProperty("overallSeverity", out var o) ? o.ToString() : "?";
            sb.AppendLine($"Unified diagnostics ({sev}): {summary}");
        }
        catch
        {
            sb.AppendLine("Diagnostics: (parse error)");
        }
    }

    private static string RedactLine(string line)
    {
        var builder = new StringBuilder(line.Length);
        var tokens = line.Split(' ');
        foreach (var token in tokens)
        {
            if (LooksSensitive(token))
            {
                _ = builder.Append("[redacted] ");
            }
            else
            {
                _ = builder.Append(token).Append(' ');
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static bool LooksSensitive(string token)
    {
        if (token.Length >= 16 && token.AsSpan().Trim('-').Length >= 12)
        {
            return true;
        }

        if (token.Contains("SERIAL", StringComparison.OrdinalIgnoreCase) ||
            token.Contains("SERVICE", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
