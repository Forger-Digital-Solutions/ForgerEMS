using System;
using System.Collections.Generic;
using System.Linq;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public enum ToolkitReadinessLabel
{
    Ready,
    MostlyReady,
    NeedsAttention,
    NotReady,
    UnknownLimitedData
}

public sealed record ToolkitReadinessResult(
    int Score,
    ToolkitReadinessLabel Label,
    string LabelText,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Blockers,
    string NextRecommendedAction,
    string ConfidenceNote);

public static class ToolkitReadinessScorer
{
    /// <param name="omitLiveUsbVentoyContext">
    /// When true, skips USB selection/Ventoy penalties used in the live Toolkit Manager UI.
    /// Use for Kyra/report-only evaluation where those signals are intentionally absent.
    /// </param>
    /// <param name="linkVerification">Optional HTTP metadata verification aligned with toolkit report + USB scope.</param>
    public static ToolkitReadinessResult Evaluate(
        IReadOnlyList<ToolkitHealthItemView> items,
        UsbTargetInfo? selectedTarget,
        string ventoyStatusText,
        bool toolkitReportAvailable,
        bool toolkitLogAvailable,
        int missingRequiredCount,
        int verificationFailedCount,
        int updatesAvailableCount,
        int verificationPendingCount,
        bool omitLiveUsbVentoyContext = false,
        ToolkitLinkVerificationSummaryForReadiness? linkVerification = null)
    {
        if (!toolkitReportAvailable)
        {
            return new ToolkitReadinessResult(
                Score: 0,
                Label: ToolkitReadinessLabel.UnknownLimitedData,
                LabelText: "Unknown / Limited Data",
                Strengths: [],
                Blockers: ["Toolkit report is missing for the selected target."],
                NextRecommendedAction: "Run Refresh Health on the selected USB target before making readiness decisions.",
                ConfidenceNote: "Low confidence: no current toolkit report.");
        }

        var score = 100;
        var strengths = new List<string>();
        var blockers = new List<string>();
        var confidenceExtras = new List<string>();

        if (missingRequiredCount > 0)
        {
            score -= Math.Min(50, missingRequiredCount * 12);
            blockers.Add($"{missingRequiredCount} required toolkit item(s) are missing.");
        }
        else
        {
            strengths.Add("No required toolkit items are missing.");
        }

        if (verificationFailedCount > 0)
        {
            score -= Math.Min(40, verificationFailedCount * 15);
            blockers.Add($"{verificationFailedCount} checksum verification issue(s) detected.");
        }
        else if (items.Any())
        {
            strengths.Add("No checksum mismatches detected in current report.");
        }

        if (updatesAvailableCount > 0)
        {
            score -= Math.Min(20, updatesAvailableCount * 6);
            blockers.Add($"{updatesAvailableCount} managed download update(s) are pending.");
        }

        if (verificationPendingCount > 0)
        {
            score -= Math.Min(15, verificationPendingCount * 3);
            blockers.Add($"{verificationPendingCount} managed file(s) still need verification.");
        }

        var unknownManaged = items.Count(item =>
            item.TypeDisplay.Equals("Managed", StringComparison.OrdinalIgnoreCase) &&
            (item.DownloadStatus.Contains("unknown", StringComparison.OrdinalIgnoreCase) ||
             item.ChecksumStatus.Contains("unknown", StringComparison.OrdinalIgnoreCase) ||
             item.Status.Contains("UNKNOWN", StringComparison.OrdinalIgnoreCase)));
        if (unknownManaged > 0)
        {
            score -= Math.Min(12, unknownManaged * 2);
            blockers.Add($"{unknownManaged} managed item(s) have unknown download/checksum state.");
        }

        if (linkVerification is { HasRun: true })
        {
            if (linkVerification.BrokenCount > 0)
            {
                score -= Math.Min(18, linkVerification.BrokenCount * 6);
                blockers.Add($"{linkVerification.BrokenCount} official toolkit URL(s) failed HTTP metadata checks.");
            }

            if (linkVerification.WarningCount > 0)
            {
                score -= Math.Min(5, linkVerification.WarningCount * 2);
                confidenceExtras.Add(
                    $"{linkVerification.WarningCount} link warning(s) from HTTP metadata (permissions/CDN/redirect signals—not proof downloads installed correctly).");
            }

            var allUnreachableMetadata =
                linkVerification.UrlsChecked > 0 &&
                linkVerification.UnknownOfflineCount == linkVerification.UrlsChecked &&
                linkVerification.BrokenCount == 0 &&
                linkVerification.WarningCount == 0;
            if (allUnreachableMetadata)
            {
                confidenceExtras.Add(
                    "Toolkit link verification ran offline or timed out; official URLs were not confirmed.");
            }
            else if (linkVerification.UnknownOfflineCount > 0)
            {
                score -= Math.Min(2, linkVerification.UnknownOfflineCount);
                confidenceExtras.Add(
                    $"{linkVerification.UnknownOfflineCount} link metadata probe(s) timed out or could not reach the network.");
            }

            if (linkVerification.VerifiedMetadataCount + linkVerification.ReachableCount > 0)
            {
                strengths.Add($"{linkVerification.VerifiedMetadataCount + linkVerification.ReachableCount} toolkit URL(s) reached HTTP metadata checks.");
            }
        }
        else
        {
            var brokenOfficialLinks = items.Count(item =>
                item.OfficialUrl.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ||
                item.OfficialUrl.Contains("broken", StringComparison.OrdinalIgnoreCase) ||
                (item.DownloadStatus.Contains("url", StringComparison.OrdinalIgnoreCase) &&
                 item.DownloadStatus.Contains("broken", StringComparison.OrdinalIgnoreCase)));
            if (brokenOfficialLinks > 0)
            {
                score -= Math.Min(18, brokenOfficialLinks * 6);
                blockers.Add($"{brokenOfficialLinks} official URL reference(s) are marked broken/unavailable.");
            }
        }

        if (!omitLiveUsbVentoyContext)
        {
            if (selectedTarget is not null)
            {
                if (selectedTarget.SafetyStatusText.Equals("BLOCKED", StringComparison.OrdinalIgnoreCase))
                {
                    score -= 45;
                    blockers.Add("Selected target is blocked by USB safety rules.");
                }
                else if (selectedTarget.SafetyStatusText.Equals("WARNING", StringComparison.OrdinalIgnoreCase))
                {
                    score -= 18;
                    blockers.Add("Selected USB target has safety warnings to verify.");
                }
                else
                {
                    strengths.Add($"USB target safety status: {selectedTarget.SafetyStatusText}.");
                }

                if (selectedTarget.FreeBytes > 0 && selectedTarget.FreeBytes < 2L * 1024 * 1024 * 1024)
                {
                    score -= 15;
                    blockers.Add("Low free USB space (<2 GB) may block toolkit updates.");
                }
            }
            else
            {
                score -= 10;
                blockers.Add("No USB target selected for readiness evaluation.");
            }

            if (ventoyStatusText.Contains("ready", StringComparison.OrdinalIgnoreCase) ||
                ventoyStatusText.Contains("installed", StringComparison.OrdinalIgnoreCase))
            {
                strengths.Add("Ventoy readiness appears healthy.");
            }
            else if (ventoyStatusText.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ||
                     ventoyStatusText.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                score -= 20;
                blockers.Add("Ventoy readiness is unavailable or in error state.");
            }
            else if (!string.IsNullOrWhiteSpace(ventoyStatusText) &&
                     !ventoyStatusText.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            {
                score -= 8;
                blockers.Add("Ventoy readiness should be rechecked before technician use.");
            }
        }

        if (!toolkitLogAvailable)
        {
            score -= 7;
            blockers.Add("Toolkit log file is not available yet.");
        }
        else
        {
            strengths.Add("Toolkit log/report trail is available.");
        }

        score = Math.Clamp(score, 0, 100);
        var label = MapLabel(score, missingRequiredCount, verificationFailedCount, selectedTarget, omitLiveUsbVentoyContext);
        var labelText = LabelToText(label);
        var confidence = omitLiveUsbVentoyContext
            ? BuildConfidenceNoteReportOnly(items.Count)
            : BuildConfidenceNote(items.Count, selectedTarget is not null);
        if (confidenceExtras.Count > 0)
        {
            confidence += " " + string.Join(' ', confidenceExtras.Distinct(StringComparer.Ordinal));
        }

        var nextAction = BuildNextAction(label, blockers);
        return new ToolkitReadinessResult(
            Score: score,
            Label: label,
            LabelText: labelText,
            Strengths: strengths.Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray(),
            Blockers: blockers.Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray(),
            NextRecommendedAction: nextAction,
            ConfidenceNote: confidence);
    }

    private static ToolkitReadinessLabel MapLabel(int score, int missing, int hashFailed, UsbTargetInfo? target, bool omitLiveUsbVentoyContext)
    {
        if (!omitLiveUsbVentoyContext && target is null)
        {
            return ToolkitReadinessLabel.UnknownLimitedData;
        }

        var blocked = target?.SafetyStatusText.Equals("BLOCKED", StringComparison.OrdinalIgnoreCase) == true;
        if (missing > 0 || hashFailed > 0 || score < 45 || blocked)
        {
            return ToolkitReadinessLabel.NotReady;
        }

        if (score >= 85)
        {
            return ToolkitReadinessLabel.Ready;
        }

        if (score >= 70)
        {
            return ToolkitReadinessLabel.MostlyReady;
        }

        return ToolkitReadinessLabel.NeedsAttention;
    }

    private static string LabelToText(ToolkitReadinessLabel label) => label switch
    {
        ToolkitReadinessLabel.Ready => "Ready",
        ToolkitReadinessLabel.MostlyReady => "Mostly Ready",
        ToolkitReadinessLabel.NeedsAttention => "Needs Attention",
        ToolkitReadinessLabel.NotReady => "Not Ready",
        _ => "Unknown / Limited Data"
    };

    private static string BuildConfidenceNote(int itemCount, bool hasSelectedTarget)
    {
        if (itemCount == 0 || !hasSelectedTarget)
        {
            return "Low confidence: limited toolkit/target evidence.";
        }

        if (itemCount < 5)
        {
            return "Medium confidence: small toolkit sample in report.";
        }

        return "High confidence: report includes broad toolkit evidence.";
    }

    private static string BuildConfidenceNoteReportOnly(int itemCount)
    {
        if (itemCount == 0)
        {
            return "Low confidence: toolkit report listed no item rows.";
        }

        return "Medium confidence: readiness from toolkit-health JSON only (USB/Ventoy not evaluated in Kyra context).";
    }

    private static string BuildNextAction(ToolkitReadinessLabel label, List<string> blockers) => label switch
    {
        ToolkitReadinessLabel.Ready => "Export a toolkit summary and proceed with technician workflow presets.",
        ToolkitReadinessLabel.MostlyReady => "Resolve top blockers, then rerun Refresh Health to confirm readiness.",
        ToolkitReadinessLabel.NeedsAttention => blockers.Count > 0
            ? $"Address highest blocker first: {blockers[0]}"
            : "Rerun Toolkit Manager health and verify Ventoy + target safety.",
        ToolkitReadinessLabel.NotReady => "Do not rely on this USB for field repair yet; fix blockers and rerun health.",
        _ => "Run Toolkit Manager Refresh Health for current USB target."
    };
}
