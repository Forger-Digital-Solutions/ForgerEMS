using System;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

/// <summary>Human-readable diagnostics checklist lines for the Diagnostics tab and tests.</summary>
public static class DiagnosticsUiFormatter
{
    public static string FormatSeverityLabel(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "Unknown";
        }

        var t = raw.Trim();
        return t.ToLowerInvariant() switch
        {
            "ok" => "OK",
            "warning" => "Warning",
            "blocked" => "Blocked",
            "unknown" => "Unknown",
            _ => t.Length == 1 ? t.ToUpperInvariant() : char.ToUpperInvariant(t[0]) + t[1..].ToLowerInvariant()
        };
    }

    public static string BuildHealthChecklist(JsonElement root)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Diagnostics health checklist (read-only)");
        if (root.TryGetProperty("generatedUtc", out var gen) && gen.ValueKind == JsonValueKind.String)
        {
            var g = gen.GetString();
            if (DateTime.TryParse(
                    g,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var dto))
            {
                sb.AppendLine($"Report freshness: {dto.ToLocalTime():g} (local)");
            }
            else if (!string.IsNullOrWhiteSpace(g))
            {
                sb.AppendLine($"Report freshness: {g}");
            }
        }

        if (root.TryGetProperty("overallSeverity", out var os))
        {
            var sev = os.ValueKind == JsonValueKind.String ? os.GetString() : null;
            sb.AppendLine($"Overall severity: {FormatSeverityLabel(sev)}");
        }

        if (root.TryGetProperty("summaryLine", out var sl) && sl.ValueKind == JsonValueKind.String)
        {
            var line = sl.GetString();
            if (!string.IsNullOrWhiteSpace(line))
            {
                sb.AppendLine(line);
            }
        }

        sb.AppendLine();
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            sb.AppendLine("No checklist items were stored in this report.");
            return sb.ToString().TrimEnd();
        }

        var n = 0;
        foreach (var it in items.EnumerateArray())
        {
            n++;
            var severity = FormatSeverityLabel(
                it.TryGetProperty("severity", out var sevEl) && sevEl.ValueKind == JsonValueKind.String
                    ? sevEl.GetString()
                    : "unknown");
            var source = it.TryGetProperty("source", out var src) && src.ValueKind == JsonValueKind.String
                ? src.GetString()
                : string.Empty;
            var msg = it.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString()
                : string.Empty;
            sb.AppendLine($"{n}. [{severity}] {source}: {msg}");
            if (it.TryGetProperty("suggestedFix", out var fix) && fix.ValueKind == JsonValueKind.String)
            {
                var fx = fix.GetString();
                if (!string.IsNullOrWhiteSpace(fx))
                {
                    sb.AppendLine($"   Suggestion: {fx}");
                }
            }
        }

        return sb.ToString().TrimEnd();
    }
}
