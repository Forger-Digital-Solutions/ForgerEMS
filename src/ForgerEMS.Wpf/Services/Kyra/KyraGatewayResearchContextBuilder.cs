using System.IO;
using System.Text.Json;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

public static class KyraGatewayResearchContextBuilder
{
    public static KyraGatewayResearchContextDto Build(CopilotSettings settings, string? systemIntelligenceReportPath, string? toolkitReportPath) =>
        Build(settings, systemIntelligenceReportPath, toolkitReportPath, gatewayIntent: null, sanitizedUserPrompt: null);

    public static KyraGatewayResearchContextDto Build(
        CopilotSettings settings,
        string? systemIntelligenceReportPath,
        string? toolkitReportPath,
        string? gatewayIntent,
        string? sanitizedUserPrompt)
    {
        var privacy = KyraGatewayResearchSanitizer.ResolvePrivacyMode(settings);
        if (!settings.KyraUseSanitizedSystemIntelligenceContext)
        {
            return new KyraGatewayResearchContextDto { PrivacyMode = privacy };
        }

        if (string.IsNullOrWhiteSpace(systemIntelligenceReportPath) || !File.Exists(systemIntelligenceReportPath))
        {
            return new KyraGatewayResearchContextDto { PrivacyMode = privacy };
        }

        var allowPartLookupBands =
            string.Equals(gatewayIntent, "hardware_part_lookup", StringComparison.OrdinalIgnoreCase);
        if (privacy == "local-only" && !allowPartLookupBands)
        {
            return new KyraGatewayResearchContextDto { PrivacyMode = privacy };
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(systemIntelligenceReportPath));
            var profile = SystemProfileMapper.FromJson(doc.RootElement);
            var machineClass = MachineClassifier.Classify(profile).PrimaryClass;
            var dto = new KyraGatewayResearchContextDto
            {
                MachineClass = KyraGatewayResearchSanitizer.SanitizeOptionalLabel(machineClass),
                HealthScoreBand = TryHealthBand(doc.RootElement),
                IssueCategory = null,
                UsbState = TryUsbState(toolkitReportPath),
                PrivacyMode = privacy
            };

            if (!string.Equals(gatewayIntent, "hardware_part_lookup", StringComparison.OrdinalIgnoreCase))
            {
                return dto;
            }

            return new KyraGatewayResearchContextDto
            {
                MachineClass = dto.MachineClass,
                HealthScoreBand = dto.HealthScoreBand,
                IssueCategory = dto.IssueCategory,
                UsbState = dto.UsbState,
                PrivacyMode = dto.PrivacyMode,
                Manufacturer = KyraGatewayResearchSanitizer.SanitizeOptionalLabel(profile.Manufacturer, 48),
                ModelFamily = KyraGatewayResearchSanitizer.SanitizeOptionalLabel($"{profile.Manufacturer} {profile.Model}".Trim(), 120),
                PartCategory = KyraHardwarePartGatewaySignals.TryClassifyPartCategory(sanitizedUserPrompt ?? string.Empty),
                KnownLocalFacts = KyraHardwareFactsEngine.BuildGatewayBands(profile)
            };
        }
        catch (Exception exception)
        {
            var note = $"{exception.GetType().Name}: {exception.Message}";
            if (note.Length > 240)
            {
                note = note[..240];
            }

            return new KyraGatewayResearchContextDto
            {
                PrivacyMode = privacy,
                IssueCategory = "context_load_error:" + note
            };
        }
    }

    private static string? TryHealthBand(JsonElement root)
    {
        if (!root.TryGetProperty("health", out var h))
        {
            return null;
        }

        if (h.TryGetProperty("overallScore", out var scoreEl) && scoreEl.TryGetInt32(out var score))
        {
            return score >= 75 ? "good" : score >= 50 ? "fair" : "needs attention";
        }

        return null;
    }

    private static string? TryUsbState(string? toolkitReportPath)
    {
        if (string.IsNullOrWhiteSpace(toolkitReportPath) || !File.Exists(toolkitReportPath))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(toolkitReportPath));
            return doc.RootElement.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.String
                ? KyraGatewayResearchSanitizer.SanitizeOptionalLabel(s.GetString(), 80)
                : "toolkit_report_present";
        }
        catch
        {
            return null;
        }
    }
}
