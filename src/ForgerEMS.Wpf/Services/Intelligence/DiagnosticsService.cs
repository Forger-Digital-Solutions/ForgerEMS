using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public sealed class DiagnosticsService : IDiagnosticsService
{
    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public UnifiedDiagnosticsReport BuildReport(
        string? systemIntelligenceJsonPath,
        string? usbIntelligenceJsonPath,
        string? toolkitHealthJsonPath,
        bool wslLikelyAvailable)
    {
        var items = new List<UnifiedDiagnosticItem>();
        var generated = DateTimeOffset.UtcNow;

        TryAddSystemIntelligenceItems(systemIntelligenceJsonPath, items);
        TryAddUsbItems(usbIntelligenceJsonPath, items);
        TryAddToolkitItems(toolkitHealthJsonPath, items);
        AddNetworkHeuristicItems(systemIntelligenceJsonPath, items);

        if (!wslLikelyAvailable)
        {
            items.Add(new UnifiedDiagnosticItem
            {
                Source = "WSL",
                Code = "wsl_not_detected",
                Severity = DiagnosticSeverityLevel.Warning,
                Message = "Windows Subsystem for Linux was not detected on PATH.",
                SuggestedFix = "Optional: install WSL from an elevated prompt only if you need Linux tooling (`wsl --install`)."
            });
        }

        var usbSection = TryReadUsbDiagnosticsSection(usbIntelligenceJsonPath);

        var overall = AggregateSeverity(items);
        var summary =
            $"Diagnostics: {overall}; {items.Count} item(s). " +
            $"Sources: System Intelligence, USB Intelligence, Toolkit, WSL probe.";

        IntelligenceLogWriter.Append("diagnostics.log", summary);

        return new UnifiedDiagnosticsReport
        {
            GeneratedUtc = generated,
            OverallSeverity = overall,
            SummaryLine = summary,
            Items = items,
            Usb = usbSection
        };
    }

    public async Task WriteLatestReportAsync(string reportsDirectory, UnifiedDiagnosticsReport report)
    {
        await Task.Run(() =>
        {
            Directory.CreateDirectory(reportsDirectory);
            var path = Path.Combine(reportsDirectory, "diagnostics-latest.json");
            File.WriteAllText(path, JsonSerializer.Serialize(report, JsonWriteOptions));
        }).ConfigureAwait(false);
    }

    public static DiagnosticSeverityLevel AggregateSeverity(IReadOnlyList<UnifiedDiagnosticItem> items)
    {
        if (items.Count == 0)
        {
            return DiagnosticSeverityLevel.Unknown;
        }

        if (items.Any(i => i.Severity == DiagnosticSeverityLevel.Blocked))
        {
            return DiagnosticSeverityLevel.Blocked;
        }

        if (items.Any(i => i.Severity == DiagnosticSeverityLevel.Warning))
        {
            return DiagnosticSeverityLevel.Warning;
        }

        if (items.All(i => i.Severity == DiagnosticSeverityLevel.Ok))
        {
            return DiagnosticSeverityLevel.Ok;
        }

        return DiagnosticSeverityLevel.Unknown;
    }

    private static void TryAddSystemIntelligenceItems(string? path, List<UnifiedDiagnosticItem> items)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            items.Add(new UnifiedDiagnosticItem
            {
                Source = "SystemIntelligence",
                Code = "no_report",
                Severity = DiagnosticSeverityLevel.Warning,
                Message = "No System Intelligence JSON is available yet.",
                SuggestedFix = "Run System Intelligence from the app or wait for the background scan to finish."
            });
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.TryGetProperty("forgerAutomation", out var auto) &&
                auto.TryGetProperty("issues", out var issues) &&
                issues.ValueKind == JsonValueKind.Array)
            {
                foreach (var issue in issues.EnumerateArray())
                {
                    var sev = ParseSeverity(GetString(issue, "severity"));
                    items.Add(new UnifiedDiagnosticItem
                    {
                        Source = "SystemIntelligence",
                        Code = GetString(issue, "code"),
                        Severity = sev,
                        Message = GetString(issue, "message"),
                        SuggestedFix = GetOptionalString(issue, "suggestedFix")
                    });
                }
            }
            else if (root.TryGetProperty("obviousProblems", out var problems) &&
                     problems.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in problems.EnumerateArray())
                {
                    var text = p.GetString();
                    if (string.IsNullOrWhiteSpace(text) ||
                        text.Contains("No obvious", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    items.Add(new UnifiedDiagnosticItem
                    {
                        Source = "SystemIntelligence",
                        Code = "obvious_problem",
                        Severity = DiagnosticSeverityLevel.Warning,
                        Message = text,
                        SuggestedFix = "Review System Intelligence recommendations and rerun the scan after changes."
                    });
                }
            }
        }
        catch (Exception ex)
        {
            items.Add(new UnifiedDiagnosticItem
            {
                Source = "SystemIntelligence",
                Code = "parse_error",
                Severity = DiagnosticSeverityLevel.Unknown,
                Message = $"Could not parse system intelligence report: {ex.Message}",
                SuggestedFix = "Rerun System Intelligence scan."
            });
        }
    }

    private static UsbDiagnosticsEmbeddedSection? TryReadUsbDiagnosticsSection(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("usbDiagnostics", out var u) || u.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return JsonSerializer.Deserialize<UsbDiagnosticsEmbeddedSection>(
                u.GetRawText(),
                UsbIntelligenceService.UsbJsonReadOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void TryAddUsbItems(string? path, List<UnifiedDiagnosticItem> items)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            items.Add(new UnifiedDiagnosticItem
            {
                Source = "UsbIntelligence",
                Code = "no_report",
                Severity = DiagnosticSeverityLevel.Unknown,
                Message = "USB Intelligence report not generated yet.",
                SuggestedFix = "Select a USB target or wait for the background USB topology pass."
            });
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.TryGetProperty("usbDiagnostics", out var embedded) && embedded.ValueKind == JsonValueKind.Object)
            {
                var suggest = GetOptionalString(embedded, "usbRecommendationLine");
                if (embedded.TryGetProperty("usbIssues", out var issueArr) && issueArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var issue in issueArr.EnumerateArray())
                    {
                        var sev = ParseSeverity(GetString(issue, "severity"));
                        var msg = GetString(issue, "message");
                        if (string.IsNullOrWhiteSpace(msg))
                        {
                            continue;
                        }

                        items.Add(new UnifiedDiagnosticItem
                        {
                            Source = "UsbIntelligence",
                            Code = "usb_diagnostics",
                            Severity = sev,
                            Message = msg,
                            SuggestedFix = sev != DiagnosticSeverityLevel.Ok ? suggest : null
                        });
                    }
                }

                return;
            }

            if (!root.TryGetProperty("selectedTargetRecommendation", out var rec) ||
                rec.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var summary = GetString(rec, "summary");
            var detail = GetString(rec, "detail");
            var risk = GetString(rec, "risk");
            var quality = GetString(rec, "quality");
            if (string.IsNullOrWhiteSpace(summary))
            {
                return;
            }

            var severity = MapLegacyUsbSeverity(risk, summary, quality);

            items.Add(new UnifiedDiagnosticItem
            {
                Source = "UsbIntelligence",
                Code = "builder_hint",
                Severity = severity,
                Message = $"{summary} {detail}".Trim(),
                SuggestedFix =
                    severity != DiagnosticSeverityLevel.Ok
                        ? "Move the USB to a USB 3.x (often blue) or USB-C port on the PC, then rescan."
                        : null
            });
        }
        catch (Exception ex)
        {
            items.Add(new UnifiedDiagnosticItem
            {
                Source = "UsbIntelligence",
                Code = "parse_error",
                Severity = DiagnosticSeverityLevel.Unknown,
                Message = $"USB Intelligence parse error: {ex.Message}",
                SuggestedFix = null
            });
        }
    }

    private static DiagnosticSeverityLevel MapLegacyUsbSeverity(string risk, string summary, string quality)
    {
        if (quality.Equals("Risky", StringComparison.OrdinalIgnoreCase) ||
            quality.Equals("Slow", StringComparison.OrdinalIgnoreCase))
        {
            return DiagnosticSeverityLevel.Warning;
        }

        if (quality.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return DiagnosticSeverityLevel.Unknown;
        }

        var severity = risk.Contains("Medium", StringComparison.OrdinalIgnoreCase)
            ? DiagnosticSeverityLevel.Warning
            : risk.Contains("High", StringComparison.OrdinalIgnoreCase)
                ? DiagnosticSeverityLevel.Blocked
                : risk.Contains("Unknown", StringComparison.OrdinalIgnoreCase)
                    ? DiagnosticSeverityLevel.Unknown
                    : DiagnosticSeverityLevel.Ok;

        if (summary.Contains("USB 2", StringComparison.OrdinalIgnoreCase))
        {
            severity = DiagnosticSeverityLevel.Warning;
        }

        return severity;
    }

    private static void TryAddToolkitItems(string? path, List<UnifiedDiagnosticItem> items)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var missing = 0;
            var failed = 0;
            if (root.TryGetProperty("summary", out var summary))
            {
                missing = 0;
                if (TryGetInt(summary, "missingRequired", out var mr))
                {
                    missing = mr;
                }
                else if (TryGetInt(summary, "missing", out var m2))
                {
                    missing = m2;
                }

                failed = TryGetInt(summary, "failed", out var f) ? f : 0;
            }

            if (failed > 0)
            {
                items.Add(new UnifiedDiagnosticItem
                {
                    Source = "Toolkit",
                    Code = "hash_failed",
                    Severity = DiagnosticSeverityLevel.Warning,
                    Message = $"Toolkit scan reports {failed} failed verification item(s).",
                    SuggestedFix = "Re-run toolkit health scan after revalidate; replace corrupted downloads from official sources."
                });
            }

            if (missing > 0)
            {
                items.Add(new UnifiedDiagnosticItem
                {
                    Source = "Toolkit",
                    Code = "missing_required",
                    Severity = DiagnosticSeverityLevel.Warning,
                    Message = $"Toolkit scan reports {missing} missing required item(s).",
                    SuggestedFix = "Run Setup USB / Update USB on the toolkit drive or add manual tools where licensing requires it."
                });
            }
        }
        catch
        {
            items.Add(new UnifiedDiagnosticItem
            {
                Source = "Toolkit",
                Code = "parse_error",
                Severity = DiagnosticSeverityLevel.Unknown,
                Message = "Toolkit health JSON could not be read.",
                SuggestedFix = "Run Toolkit Manager scan against the USB root."
            });
        }
    }

    private static void AddNetworkHeuristicItems(string? path, List<UnifiedDiagnosticItem> items)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("network", out var net) ||
                !net.TryGetProperty("internetCheck", out var ic))
            {
                return;
            }

            var online = ic.ValueKind == JsonValueKind.True;
            if (!online)
            {
                items.Add(new UnifiedDiagnosticItem
                {
                    Source = "Network",
                    Code = "offline",
                    Severity = DiagnosticSeverityLevel.Warning,
                    Message = "System Intelligence reports the machine is offline or could not verify internet connectivity.",
                    SuggestedFix = "Check Wi-Fi/Ethernet, VPN, and DNS; retry after connectivity returns."
                });
            }
        }
        catch
        {
            // ignore
        }
    }

    private static DiagnosticSeverityLevel ParseSeverity(string raw)
    {
        if (raw.Equals("Blocked", StringComparison.OrdinalIgnoreCase))
        {
            return DiagnosticSeverityLevel.Blocked;
        }

        if (raw.Equals("Warning", StringComparison.OrdinalIgnoreCase))
        {
            return DiagnosticSeverityLevel.Warning;
        }

        if (raw.Equals("Ok", StringComparison.OrdinalIgnoreCase) || raw.Equals("OK", StringComparison.OrdinalIgnoreCase))
        {
            return DiagnosticSeverityLevel.Ok;
        }

        return DiagnosticSeverityLevel.Unknown;
    }

    private static string GetString(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var p))
        {
            return string.Empty;
        }

        return p.ValueKind == JsonValueKind.String ? p.GetString() ?? string.Empty : p.ToString();
    }

    private static string? GetOptionalString(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return p.GetString();
    }

    private static bool TryGetInt(JsonElement e, string name, out int value)
    {
        value = 0;
        if (!e.TryGetProperty(name, out var p))
        {
            return false;
        }

        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out value))
        {
            return true;
        }

        return int.TryParse(p.ToString(), out value);
    }
}
