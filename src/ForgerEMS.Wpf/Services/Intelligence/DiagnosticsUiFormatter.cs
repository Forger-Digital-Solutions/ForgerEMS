using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

/// <summary>Human-readable diagnostics checklist lines for the Diagnostics tab and tests.</summary>
public static class DiagnosticsUiFormatter
{
    private const int TopActionableLimit = 3;

    private sealed record DiagnosticLine(
        string Category,
        string Source,
        string Severity,
        string Message,
        string? SuggestedFix);

    public sealed record ActionCenterItem(
        string Severity,
        string Category,
        string Reason,
        string SuggestedAction,
        string Source);

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
            "info" => "Info",
            "warning" => "Warning",
            "blocked" => "Blocked",
            "unknown" => "Unknown",
            _ => t.Length == 1 ? t.ToUpperInvariant() : char.ToUpperInvariant(t[0]) + t[1..].ToLowerInvariant()
        };
    }

    public static string BuildHealthChecklist(JsonElement root, bool includeFullDetails = false)
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

        var lines = new List<DiagnosticLine>();
        foreach (var it in items.EnumerateArray())
        {
            var msg = SanitizeMessage(it.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString() ?? string.Empty
                : string.Empty);
            var severity = NormalizeSeverityForDisplay(FormatSeverityLabel(
                it.TryGetProperty("severity", out var sevEl) && sevEl.ValueKind == JsonValueKind.String
                    ? sevEl.GetString()
                    : "unknown"),
                msg);
            var source = it.TryGetProperty("source", out var src) && src.ValueKind == JsonValueKind.String
                ? src.GetString() ?? string.Empty
                : string.Empty;
            var fix = it.TryGetProperty("suggestedFix", out var fixEl) && fixEl.ValueKind == JsonValueKind.String
                ? fixEl.GetString()
                : null;
            lines.Add(new DiagnosticLine(
                Category: ClassifyCategory(source, msg),
                Source: source,
                Severity: severity,
                Message: msg,
                SuggestedFix: string.IsNullOrWhiteSpace(fix) ? null : fix.Trim()));
        }

        var grouped = GroupDiagnostics(lines);
        var actionable = grouped
            .Where(x => x.Severity is "Blocked" or "Warning")
            .Take(TopActionableLimit)
            .ToList();
        var additionalCount = Math.Max(0, grouped.Count - actionable.Count);

        sb.AppendLine();
        sb.AppendLine("Top actionable issues:");
        if (actionable.Count == 0)
        {
            sb.AppendLine("- None.");
        }
        else
        {
            foreach (var item in actionable)
            {
                sb.AppendLine($"- [{item.Severity}] {item.Category}: {item.Message}");
                if (!string.IsNullOrWhiteSpace(item.SuggestedFix))
                {
                    sb.AppendLine($"  Suggestion: {item.SuggestedFix}");
                }

                sb.AppendLine($"  Sources: {item.SourceSummary}");
            }
        }

        sb.AppendLine($"Additional diagnostic details: {additionalCount}");
        if (!includeFullDetails)
        {
            sb.AppendLine("Show full diagnostic detail to view all grouped entries.");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine();
        sb.AppendLine("Full diagnostic detail:");
        var n = 0;
        foreach (var item in grouped)
        {
            n++;
            sb.AppendLine($"{n}. [{item.Severity}] {item.Category}: {item.Message}");
            if (!string.IsNullOrWhiteSpace(item.SuggestedFix))
            {
                sb.AppendLine($"   Suggestion: {item.SuggestedFix}");
            }

            sb.AppendLine($"   Sources: {item.SourceSummary}");
        }

        return sb.ToString().TrimEnd();
    }

    public static string BuildWarningReason(JsonElement root)
    {
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return "Warning reason unavailable.";
        }

        var lines = new List<DiagnosticLine>();
        foreach (var it in items.EnumerateArray())
        {
            var message = SanitizeMessage(it.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString() ?? string.Empty
                : string.Empty);
            var severity = NormalizeSeverityForDisplay(FormatSeverityLabel(
                it.TryGetProperty("severity", out var sevEl) && sevEl.ValueKind == JsonValueKind.String
                    ? sevEl.GetString()
                    : "unknown"),
                message);
            var source = it.TryGetProperty("source", out var src) && src.ValueKind == JsonValueKind.String
                ? src.GetString() ?? string.Empty
                : string.Empty;
            lines.Add(new DiagnosticLine(ClassifyCategory(source, message), source, severity, message, null));
        }

        var grouped = GroupDiagnostics(lines)
            .Where(x => x.Severity is "Blocked" or "Warning")
            .Take(2)
            .Select(x => x.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return grouped.Length == 0
            ? "No high-priority warnings."
            : "Warning: " + string.Join(" + ", grouped) + " signals";
    }

    public static IReadOnlyList<ActionCenterItem> BuildActionCenterItems(JsonElement root, int limit = 5)
    {
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var lines = new List<DiagnosticLine>();
        foreach (var it in items.EnumerateArray())
        {
            var message = SanitizeMessage(it.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString() ?? string.Empty
                : string.Empty);
            var severity = NormalizeSeverityForDisplay(FormatSeverityLabel(
                it.TryGetProperty("severity", out var sevEl) && sevEl.ValueKind == JsonValueKind.String
                    ? sevEl.GetString()
                    : "unknown"),
                message);
            var source = it.TryGetProperty("source", out var src) && src.ValueKind == JsonValueKind.String
                ? src.GetString() ?? string.Empty
                : string.Empty;
            var fix = it.TryGetProperty("suggestedFix", out var fixEl) && fixEl.ValueKind == JsonValueKind.String
                ? fixEl.GetString()
                : null;
            lines.Add(new DiagnosticLine(ClassifyCategory(source, message), source, severity, message, fix));
        }

        return GroupDiagnostics(lines)
            .Where(x => x.Severity is "Blocked" or "Warning" or "Info")
            .Take(Math.Max(1, limit))
            .Select(x => new ActionCenterItem(
                Severity: x.Severity,
                Category: x.Category,
                Reason: x.Message,
                SuggestedAction: string.IsNullOrWhiteSpace(x.SuggestedFix) ? "Review related section and rerun checks." : x.SuggestedFix!,
                Source: x.SourceSummary))
            .ToArray();
    }

    private static List<(string Category, string Severity, string Message, string? SuggestedFix, string SourceSummary)> GroupDiagnostics(
        IEnumerable<DiagnosticLine> lines)
    {
        return lines
            .GroupBy(x => BuildGroupKey(x), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var severity = group.Any(x => x.Severity == "Blocked")
                    ? "Blocked"
                    : group.Any(x => x.Severity == "Warning")
                        ? "Warning"
                        : group.Any(x => x.Severity == "Info")
                            ? "Info"
                            : group.Any(x => x.Severity == "OK")
                                ? "OK"
                                : "Unknown";
                var sources = group
                    .Select(x => string.IsNullOrWhiteSpace(x.Source) ? "Unknown source" : x.Source.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var sourceSummary = sources.Length == 0 ? "Unknown source" : string.Join(", ", sources);
                if (group.Count() > 1)
                {
                    sourceSummary += $" ({group.Count()} related checks)";
                }

                return (
                    Category: first.Category,
                    Severity: severity,
                    Message: first.Message,
                    SuggestedFix: first.SuggestedFix,
                    SourceSummary: sourceSummary);
            })
            .OrderByDescending(x => x.Severity == "Blocked")
            .ThenByDescending(x => x.Severity == "Warning")
            .ThenBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeSeverityForDisplay(string severity, string message)
    {
        if (severity is not "Warning" and not "Blocked" &&
            IsPermissionLimitedOrOptionalMessage(message))
        {
            return "Info";
        }

        return severity;
    }

    private static bool IsPermissionLimitedOrOptionalMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("access denied", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("not exposed", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("optional", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("wsl was not detected", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("sandbox", StringComparison.OrdinalIgnoreCase);
    }

    private static string ClassifyCategory(string source, string message)
    {
        var haystack = $"{source} {message}";
        if (ContainsAny(haystack, "battery", "wear", "cycle"))
        {
            return "Battery";
        }

        if (ContainsAny(haystack, "tpm", "secure boot", "windows readiness", "firmware"))
        {
            return "Security / Windows readiness";
        }

        if (ContainsAny(haystack, "usb", "ventoy", "benchmark", "port"))
        {
            return "USB / Ventoy";
        }

        if (ContainsAny(haystack, "toolkit", "managed", "checksum", "hash"))
        {
            return "Toolkit";
        }

        if (ContainsAny(haystack, "kyra", "copilot", "provider"))
        {
            return "Kyra provider";
        }

        if (ContainsAny(haystack, "backend", "manifest", "script", "powershell"))
        {
            return "Backend";
        }

        if (ContainsAny(haystack, "wsl", "sandbox", "virtualization"))
        {
            return "WSL / sandbox";
        }

        return "General";
    }

    private static string SanitizeMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "No details provided.";
        }

        var sanitized = message.Trim();
        sanitized = sanitized.Replace("Generic failure", "Provider unavailable", StringComparison.OrdinalIgnoreCase);
        sanitized = sanitized.Replace("Access denied", "Permission required", StringComparison.OrdinalIgnoreCase);
        sanitized = sanitized.Replace("CIM resource was not available to the client", "Windows blocked optional low-level detail", StringComparison.OrdinalIgnoreCase);
        return sanitized;
    }

    private static bool ContainsAny(string value, params string[] tokens) =>
        tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeMessageForGroup(string value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();

    private static string BuildGroupKey(DiagnosticLine line)
    {
        var normalizedMessage = NormalizeMessageForGroup(line.Message);
        if (line.Category.Equals("Battery", StringComparison.OrdinalIgnoreCase) &&
            ContainsAny(normalizedMessage, "wear", "battery health", "cycle"))
        {
            return "battery|wear-high";
        }

        if (line.Category.Equals("Security / Windows readiness", StringComparison.OrdinalIgnoreCase) &&
            ContainsAny(normalizedMessage, "tpm", "secure boot", "readiness"))
        {
            return "windows-readiness|tpm-secure-boot";
        }

        return $"{line.Category}|{normalizedMessage}|{NormalizeMessageForGroup(line.SuggestedFix ?? string.Empty)}";
    }
}
