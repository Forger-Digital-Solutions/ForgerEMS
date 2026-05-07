using System.Text.Json;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class DiagnosticsUiFormatterTests
{
    [Theory]
    [InlineData("ok", "OK")]
    [InlineData("info", "Info")]
    [InlineData("warning", "Warning")]
    [InlineData("blocked", "Blocked")]
    [InlineData("unknown", "Unknown")]
    public void FormatSeverityLabel_Normalizes(string raw, string expected) =>
        Assert.Equal(expected, DiagnosticsUiFormatter.FormatSeverityLabel(raw));

    [Fact]
    public void BuildHealthChecklist_GroupsAndShowsTopActionable()
    {
        using var doc = JsonDocument.Parse("""
            {
              "generatedUtc": "2026-05-03T10:00:00Z",
              "overallSeverity": "warning",
              "summaryLine": "Diagnostics: smoke test.",
              "items": [
                {
                  "source": "SystemIntelligence",
                  "code": "battery_wear",
                  "severity": "warning",
                  "message": "Battery wear is high at 40.2%",
                  "suggestedFix": "Plan replacement"
                },
                {
                  "source": "SystemIntelligence",
                  "code": "battery_health",
                  "severity": "warning",
                  "message": "Battery wear is high at 40.2%",
                  "suggestedFix": "Plan replacement"
                }
              ]
            }
            """);

        var text = DiagnosticsUiFormatter.BuildHealthChecklist(doc.RootElement, includeFullDetails: false);
        Assert.Contains("[Warning]", text, StringComparison.Ordinal);
        Assert.Contains("Top actionable issues:", text, StringComparison.Ordinal);
        Assert.Contains("Additional diagnostic details:", text, StringComparison.Ordinal);
        Assert.Contains("related checks", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SystemIntelligence", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildHealthChecklist_DedupesBatteryAndWindowsReadiness()
    {
        using var doc = JsonDocument.Parse("""
            {
              "items": [
                { "source":"SystemIntelligence", "severity":"warning", "message":"Battery status needs attention", "suggestedFix":"Replace/disclose battery if resale or runtime matters." },
                { "source":"SystemIntelligence", "severity":"warning", "message":"Battery wear is high at 40.2%", "suggestedFix":"Replace/disclose battery if resale or runtime matters." },
                { "source":"SystemIntelligence", "severity":"warning", "message":"TPM was not detected by Windows", "suggestedFix":"Run elevated scan or check BIOS/UEFI TPM/PTT and Secure Boot settings." },
                { "source":"SystemIntelligence", "severity":"warning", "message":"Secure Boot state is unknown", "suggestedFix":"Run elevated scan or check BIOS/UEFI TPM/PTT and Secure Boot settings." }
              ]
            }
            """);

        var text = DiagnosticsUiFormatter.BuildHealthChecklist(doc.RootElement, includeFullDetails: false);
        Assert.Contains("Battery:", text, StringComparison.Ordinal);
        Assert.Contains("Security / Windows readiness:", text, StringComparison.Ordinal);
        Assert.Equal(1, CountContains(text, "related checks"));
    }

    [Fact]
    public void BuildWarningReason_UsesHighestPriorityGroupedCategories()
    {
        using var doc = JsonDocument.Parse("""
            {
              "items": [
                {
                  "source": "SystemIntelligence",
                  "severity": "warning",
                  "message": "TPM was not detected by Windows"
                },
                {
                  "source": "SystemIntelligence",
                  "severity": "warning",
                  "message": "Secure Boot state is unknown"
                }
              ]
            }
            """);
        var reason = DiagnosticsUiFormatter.BuildWarningReason(doc.RootElement);
        Assert.Contains("Warning:", reason, StringComparison.Ordinal);
        Assert.Contains("Security / Windows readiness", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildActionCenterItems_IncludesBatteryAndWindowsReadiness()
    {
        using var doc = JsonDocument.Parse("""
            {
              "items": [
                { "source":"SystemIntelligence", "severity":"warning", "message":"Battery wear is high at 40.2%", "suggestedFix":"Replace/disclose battery if resale or runtime matters." },
                { "source":"SystemIntelligence", "severity":"warning", "message":"TPM was not detected by Windows", "suggestedFix":"Run elevated scan." },
                { "source":"SystemIntelligence", "severity":"warning", "message":"Secure Boot state is unknown", "suggestedFix":"Run elevated scan." }
              ]
            }
            """);

        var items = DiagnosticsUiFormatter.BuildActionCenterItems(doc.RootElement, limit: 5);
        Assert.Contains(items, i => i.Category.Contains("Battery", StringComparison.Ordinal));
        Assert.Contains(items, i => i.Category.Contains("Windows readiness", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildActionCenterItems_PermissionLimitedOptionalProvider_IsInfoNotError()
    {
        using var doc = JsonDocument.Parse("""
            {
              "items": [
                { "source":"SystemIntelligence", "severity":"info", "message":"Permission required for optional provider", "suggestedFix":"Run elevated scan if needed." }
              ]
            }
            """);

        var items = DiagnosticsUiFormatter.BuildActionCenterItems(doc.RootElement, limit: 5);
        Assert.Single(items);
        Assert.Equal("Info", items[0].Severity);
    }

    private static int CountContains(string source, string token)
        => source.Split(Environment.NewLine).Count(line => line.Contains(token, StringComparison.OrdinalIgnoreCase));
}
