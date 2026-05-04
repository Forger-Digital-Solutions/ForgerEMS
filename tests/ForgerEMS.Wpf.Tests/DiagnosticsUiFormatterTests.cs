using System.Text.Json;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class DiagnosticsUiFormatterTests
{
    [Theory]
    [InlineData("ok", "OK")]
    [InlineData("warning", "Warning")]
    [InlineData("blocked", "Blocked")]
    [InlineData("unknown", "Unknown")]
    public void FormatSeverityLabel_Normalizes(string raw, string expected) =>
        Assert.Equal(expected, DiagnosticsUiFormatter.FormatSeverityLabel(raw));

    [Fact]
    public void BuildHealthChecklist_IncludesUsbAndToolkitLines()
    {
        using var doc = JsonDocument.Parse("""
            {
              "generatedUtc": "2026-05-03T10:00:00Z",
              "overallSeverity": "warning",
              "summaryLine": "Diagnostics: smoke test.",
              "items": [
                {
                  "source": "SystemIntelligence",
                  "code": "stale",
                  "severity": "warning",
                  "message": "Scan old",
                  "suggestedFix": "Run System Scan"
                },
                {
                  "source": "UsbIntelligence",
                  "code": "bench",
                  "severity": "ok",
                  "message": "USB OK",
                  "suggestedFix": null
                }
              ]
            }
            """);

        var text = DiagnosticsUiFormatter.BuildHealthChecklist(doc.RootElement);
        Assert.Contains("[Warning]", text, StringComparison.Ordinal);
        Assert.Contains("[OK]", text, StringComparison.Ordinal);
        Assert.Contains("SystemIntelligence", text, StringComparison.Ordinal);
        Assert.Contains("UsbIntelligence", text, StringComparison.Ordinal);
    }
}
