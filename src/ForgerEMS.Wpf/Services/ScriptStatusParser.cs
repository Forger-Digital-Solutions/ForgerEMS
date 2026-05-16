using System;
using System.Linq;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services.Intelligence;

namespace VentoyToolkitSetup.Wpf.Services;

public interface IScriptStatusParser
{
    ScriptExecutionResult Parse(
        ScriptActionType action,
        string displayName,
        PowerShellRunResult runResult,
        bool elevatedScanOutputLikelyMissing = false);
}

public sealed class ScriptStatusParser : IScriptStatusParser
{
    public ScriptExecutionResult Parse(
        ScriptActionType action,
        string displayName,
        PowerShellRunResult runResult,
        bool elevatedScanOutputLikelyMissing = false)
    {
        var hasWarnings = runResult.OutputLines.Any(line => line.Severity == LogSeverity.Warning);
        var hasErrors = runResult.OutputLines.Any(line => line.Severity == LogSeverity.Error);
        var lastError = runResult.OutputLines.LastOrDefault(line => line.Severity == LogSeverity.Error)?.Text;
        var readinessLine = FindLastLine(runResult, "USB readiness:");
        var backendReadinessLine = FindLastLine(runResult, "Backend readiness:");
        var itemsDownloadedLine = FindLastLine(runResult, "Items downloaded:");
        var itemsUpToDateLine = FindLastLine(runResult, "Items already up to date:");
        var shortcutsUpdatedLine = FindLastLine(runResult, "Shortcuts updated:");
        var failuresLine = FindLastLine(runResult, "Failures:");
        var warningsLine = FindLastLine(runResult, "Warnings:");
        var checksLine = FindLastLine(runResult, "Checks passed:");

        var indicatesPartial =
            !string.IsNullOrWhiteSpace(readinessLine) &&
            readinessLine.Contains("PARTIALLY STAGED", StringComparison.OrdinalIgnoreCase);

        var succeeded = runResult.Succeeded;
        var partialStaged = indicatesPartial && runResult.Succeeded;

        ElevatedScanFailureAnalysis? elevatedAnalysis = null;
        if (!succeeded &&
            action is ScriptActionType.SystemIntelligence &&
            displayName.Contains("elevated", StringComparison.OrdinalIgnoreCase))
        {
            elevatedAnalysis = ElevatedScanLaunchClassifier.Analyze(runResult, elevatedScanOutputLikelyMissing);
        }

        var summary = !runResult.Succeeded
            ? elevatedAnalysis is not null
                ? elevatedAnalysis.PrimaryUserMessage
                : (!string.IsNullOrWhiteSpace(lastError)
                    ? lastError
                    : !string.IsNullOrWhiteSpace(readinessLine)
                        ? readinessLine
                        : $"{displayName} exited with code {runResult.ExitCode}.")
            : partialStaged
                ? "USB built; managed downloads partially staged — use Retry Failed Downloads or fallback shortcuts; manual/info items are not failed downloads."
                : BuildSuccessSummary(action, displayName, hasWarnings || hasErrors);

        var details = BuildDetails(
            action,
            runResult,
            succeeded,
            hasWarnings || partialStaged || (succeeded && hasErrors),
            readinessLine,
            backendReadinessLine,
            itemsDownloadedLine,
            itemsUpToDateLine,
            shortcutsUpdatedLine,
            failuresLine,
            warningsLine,
            checksLine);

        if (elevatedAnalysis is not null)
        {
            details = string.IsNullOrWhiteSpace(details)
                ? elevatedAnalysis.AdvancedDiagnosticsLine
                : $"{details} {elevatedAnalysis.AdvancedDiagnosticsLine}";
        }

        return new ScriptExecutionResult
        {
            Action = action,
            DisplayName = displayName,
            Succeeded = succeeded,
            HasWarnings = hasWarnings || partialStaged || (succeeded && hasErrors),
            PartiallyStaged = partialStaged,
            Summary = summary,
            Details = details,
            RawRun = runResult
        };
    }

    private static string BuildSuccessSummary(ScriptActionType action, string displayName, bool hasWarnings)
    {
        if (hasWarnings)
        {
            return $"{displayName} finished with warnings.";
        }

        return action switch
        {
            ScriptActionType.VerifyBackend => "Backend verification passed.",
            ScriptActionType.RevalidateManagedDownloads => "Managed download revalidation passed.",
            ScriptActionType.SetupUsb => $"{displayName} completed.",
            ScriptActionType.UpdateUsb => $"{displayName} completed.",
            _ => $"{displayName} completed."
        };
    }

    private static string BuildDetails(
        ScriptActionType action,
        PowerShellRunResult runResult,
        bool succeeded,
        bool hasWarnings,
        string readinessLine,
        string backendReadinessLine,
        string itemsDownloadedLine,
        string itemsUpToDateLine,
        string shortcutsUpdatedLine,
        string failuresLine,
        string warningsLine,
        string checksLine)
    {
        if (action is ScriptActionType.VerifyBackend or ScriptActionType.RevalidateManagedDownloads)
        {
            var parts = new[] { checksLine, backendReadinessLine, warningsLine }
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToArray();
            if (parts.Length > 0)
            {
                return string.Join(" · ", parts);
            }
        }

        if (action is ScriptActionType.SetupUsb or ScriptActionType.UpdateUsb)
        {
            var parts = new[]
                {
                    itemsDownloadedLine,
                    itemsUpToDateLine,
                    shortcutsUpdatedLine,
                    failuresLine,
                    warningsLine,
                    readinessLine
                }
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToArray();

            if (parts.Length > 0)
            {
                var detail = string.Join(" · ", parts);
                if (!succeeded)
                {
                    return detail + " Review View Full Logs for per-item errors and fallback coverage.";
                }

                if (hasWarnings)
                {
                    return detail + (readinessLine.Contains("PARTIALLY STAGED", StringComparison.OrdinalIgnoreCase)
                        ? " Partially staged: the stick is usually still usable — retry managed items that need attention or open fallback shortcuts."
                        : readinessLine.Contains("FAILED", StringComparison.OrdinalIgnoreCase)
                            ? " Strict mode failed: fix managed download errors or relax strict settings, then retry."
                            : " The command completed, but the backend reported warnings that should be reviewed.");
                }

                return detail;
            }
        }

        return succeeded
            ? (hasWarnings
                ? "The command completed successfully, but the backend reported warnings that should be reviewed in the log pane."
                : "The command completed successfully.")
            : $"PowerShell exited with code {runResult.ExitCode}. Review the log pane for backend details.";
    }

    private static string FindLastLine(PowerShellRunResult runResult, string containsText)
    {
        return runResult.OutputLines
            .Select(line => line.Text)
            .LastOrDefault(text => text.Contains(containsText, StringComparison.OrdinalIgnoreCase))
            ?? string.Empty;
    }
}
