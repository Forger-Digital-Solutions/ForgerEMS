#pragma warning disable CA1822 // DI-injected service; methods called via instance reference
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

public interface IKyraCommunityIntelligenceClient
{
    Task<bool> SubmitDiagnosticEventAsync(KyraCommunityDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken);

    Task<IReadOnlyList<KyraCommunityInsight>> GetInsightsAsync(KyraCommunityDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken);
}

public sealed class DisabledKyraCommunityIntelligenceClient : IKyraCommunityIntelligenceClient
{
    public bool SubmitAttempted { get; private set; }

    public Task<bool> SubmitDiagnosticEventAsync(KyraCommunityDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken)
    {
        SubmitAttempted = true;
        return Task.FromResult(false);
    }

    public Task<IReadOnlyList<KyraCommunityInsight>> GetInsightsAsync(KyraCommunityDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<KyraCommunityInsight>>(Array.Empty<KyraCommunityInsight>());
}

public sealed class KyraCommunityDiagnosticEvent
{
    public string AppVersion { get; set; } = "unknown";

    public string Channel { get; set; } = "beta";

    public string PrivacyMode { get; set; } = "local-only";

    public string MachineClass { get; set; } = "Unknown";

    public string HardwareCategorySummary { get; set; } = "Unknown";

    public string HealthScoreBand { get; set; } = "Unknown";

    public string AnonymizedModelFamily { get; set; } = "Unknown";

    public string IssueCategory { get; set; } = "General diagnostic";

    public string WarningCategory { get; set; } = "None";

    public string FixOutcome { get; set; } = "unknown";

    public string ToolArea { get; set; } = "Kyra";

    public string UserIntentCategory { get; set; } = "General";

    public string KyraActionCategory { get; set; } = "assist";

    public string OutcomeCategory { get; set; } = "unknown";

    public string SanitizedNotes { get; set; } = "None";

    public string UsbBenchmarkSummary { get; set; } = "Unknown";

    public string UsbTargetSafetyResult { get; set; } = "Unknown";

    public string BestUseRecommendationCategory { get; set; } = "Unknown";

    public string ResalePrepNoteCategory { get; set; } = "Unknown";

    public string ConfidenceLevel { get; set; } = "Medium";

    public DateTimeOffset ScanTimestamp { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class KyraCommunityInsight
{
    public string InsightCategory { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string ConfidenceLevel { get; set; } = "Low";
}

public sealed class KyraCommunityConsentService
{
    public bool CanShare(KyraMemorySettings settings) =>
        settings.CommunitySharingEnabled &&
        (settings.ShareResolvedIssueFixPatterns ||
         settings.ShareHardwareCompatibilityPerformancePatterns ||
         settings.ShareCrashErrorDiagnostics);

    public async Task<bool> TrySubmitAsync(
        IKyraCommunityIntelligenceClient client,
        KyraCommunityDiagnosticEvent diagnosticEvent,
        KyraMemorySettings settings,
        CancellationToken cancellationToken)
    {
        if (!CanShare(settings))
        {
            return false;
        }

        var safeEvent = KyraCommunitySanitizationService.Sanitize(diagnosticEvent);
        return await client.SubmitDiagnosticEventAsync(safeEvent, cancellationToken).ConfigureAwait(false);
    }
}

public static class KyraCommunityPayloadPreviewBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string BuildPreview(
        KyraMachineMemoryProfile profile,
        KyraMemorySettings settings,
        string appVersion,
        string channel,
        KyraMemorySettings? hypotheticalConsent = null)
    {
        var effective = hypotheticalConsent ?? settings;
        var events = profile.Entries
            .OrderByDescending(e => e.ScanTimestamp)
            .Take(10)
            .Select(e => KyraCommunitySanitizationService.Sanitize(FromMemoryEntry(e, appVersion, channel)))
            .ToArray();

        var payload = new
        {
            status = effective.CommunitySharingEnabled
                ? "Preview only - community upload client is disabled in this build."
                : "Local Only - community sharing is off.",
            consent = new
            {
                communitySharing = effective.CommunitySharingEnabled,
                resolvedIssueFixPatterns = effective.ShareResolvedIssueFixPatterns,
                hardwareCompatibilityPerformancePatterns = effective.ShareHardwareCompatibilityPerformancePatterns,
                crashErrorDiagnostics = effective.ShareCrashErrorDiagnostics
            },
            wouldShare = events
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static KyraCommunityDiagnosticEvent FromMemoryEntry(KyraMemoryEntry entry, string appVersion, string channel) => new()
    {
        AppVersion = appVersion,
        Channel = channel,
        PrivacyMode = string.IsNullOrWhiteSpace(entry.PrivacyMode) ? "local-only" : entry.PrivacyMode,
        MachineClass = entry.MachineClass,
        HardwareCategorySummary = entry.HardwareCategorySummary,
        HealthScoreBand = entry.HealthScoreBand,
        AnonymizedModelFamily = entry.AnonymizedModelFamily,
        IssueCategory = entry.IssueCategory,
        WarningCategory = entry.WarningCategory,
        FixOutcome = entry.UserConfirmedFix,
        ToolArea = entry.ToolArea,
        UserIntentCategory = entry.UserIntentCategory,
        KyraActionCategory = entry.KyraActionCategory,
        OutcomeCategory = entry.OutcomeCategory,
        SanitizedNotes = entry.SanitizedNotes,
        UsbBenchmarkSummary = entry.UsbBenchmarkSummary,
        UsbTargetSafetyResult = entry.UsbTargetSafetyResult,
        BestUseRecommendationCategory = entry.BestUseRecommendationCategory,
        ResalePrepNoteCategory = entry.ResalePrepNoteCategory,
        ConfidenceLevel = entry.ConfidenceLevel,
        ScanTimestamp = entry.ScanTimestamp
    };
}

public static class KyraCommunitySanitizationService
{
    public static KyraCommunityDiagnosticEvent Sanitize(KyraCommunityDiagnosticEvent diagnosticEvent) => new()
    {
        AppVersion = KyraMemorySanitizer.SanitizeText(diagnosticEvent.AppVersion, 40),
        Channel = KyraMemorySanitizer.SanitizeText(diagnosticEvent.Channel, 40),
        PrivacyMode = KyraMemorySanitizer.SanitizeText(diagnosticEvent.PrivacyMode, 40),
        MachineClass = KyraMemorySanitizer.SanitizeText(diagnosticEvent.MachineClass, 80),
        HardwareCategorySummary = KyraMemorySanitizer.SanitizeText(diagnosticEvent.HardwareCategorySummary, 160),
        HealthScoreBand = KyraMemorySanitizer.SanitizeText(diagnosticEvent.HealthScoreBand, 60),
        AnonymizedModelFamily = KyraMemorySanitizer.SanitizeText(diagnosticEvent.AnonymizedModelFamily, 120),
        IssueCategory = KyraMemorySanitizer.SanitizeText(diagnosticEvent.IssueCategory, 120),
        WarningCategory = KyraMemorySanitizer.SanitizeText(diagnosticEvent.WarningCategory, 120),
        FixOutcome = SanitizeOutcome(diagnosticEvent.FixOutcome),
        ToolArea = KyraMemorySanitizer.SanitizeText(diagnosticEvent.ToolArea, 80),
        UserIntentCategory = KyraMemorySanitizer.SanitizeText(diagnosticEvent.UserIntentCategory, 120),
        KyraActionCategory = KyraMemorySanitizer.SanitizeText(diagnosticEvent.KyraActionCategory, 80),
        OutcomeCategory = KyraMemorySanitizer.SanitizeText(diagnosticEvent.OutcomeCategory, 120),
        SanitizedNotes = KyraMemorySanitizer.SanitizeText(diagnosticEvent.SanitizedNotes, 400),
        UsbBenchmarkSummary = KyraMemorySanitizer.SanitizeText(diagnosticEvent.UsbBenchmarkSummary, 160),
        UsbTargetSafetyResult = KyraMemorySanitizer.SanitizeText(diagnosticEvent.UsbTargetSafetyResult, 120),
        BestUseRecommendationCategory = KyraMemorySanitizer.SanitizeText(diagnosticEvent.BestUseRecommendationCategory, 120),
        ResalePrepNoteCategory = KyraMemorySanitizer.SanitizeText(diagnosticEvent.ResalePrepNoteCategory, 120),
        ConfidenceLevel = KyraMemorySanitizer.SanitizeText(diagnosticEvent.ConfidenceLevel, 40),
        ScanTimestamp = diagnosticEvent.ScanTimestamp
    };

    private static string SanitizeOutcome(string value)
    {
        var t = (value ?? string.Empty).Trim().ToLowerInvariant();
        return t is "yes" or "no" or "unknown" ? t : "unknown";
    }
}
