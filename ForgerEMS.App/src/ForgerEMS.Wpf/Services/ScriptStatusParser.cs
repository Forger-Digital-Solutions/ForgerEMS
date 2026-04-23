using System;
using System.Linq;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services;

public interface IScriptStatusParser
{
    ScriptExecutionResult Parse(ScriptActionType action, string displayName, PowerShellRunResult runResult);
}

public sealed class ScriptStatusParser : IScriptStatusParser
{
    public ScriptExecutionResult Parse(ScriptActionType action, string displayName, PowerShellRunResult runResult)
    {
        var hasWarnings = runResult.OutputLines.Any(line => line.Severity == LogSeverity.Warning);
        var hasErrors = runResult.OutputLines.Any(line => line.Severity == LogSeverity.Error);
        var lastError = runResult.OutputLines.LastOrDefault(line => line.Severity == LogSeverity.Error)?.Text;
        var readinessLine = FindLastLine(runResult, "USB readiness:");
        var downloadedLine = FindLastLine(runResult, "Downloaded successfully:");
        var verifiedLine = FindLastLine(runResult, "Verified successfully:");
        var fallbackLine = FindLastLine(runResult, "Failed and covered by fallback shortcut:");
        var skippedLine = FindLastLine(runResult, "Skipped / placeholder only:");

        var indicatesPartial =
            !string.IsNullOrWhiteSpace(readinessLine) &&
            readinessLine.Contains("PARTIALLY STAGED", StringComparison.OrdinalIgnoreCase);

        var succeeded = runResult.Succeeded && !indicatesPartial;

        var summary = succeeded
            ? BuildSuccessSummary(action, displayName, hasWarnings)
            : (!string.IsNullOrWhiteSpace(lastError)
                ? lastError
                : !string.IsNullOrWhiteSpace(readinessLine)
                    ? readinessLine
                    : $"{displayName} exited with code {runResult.ExitCode}.");

        var details = BuildDetails(
            action,
            runResult,
            succeeded,
            hasWarnings || indicatesPartial || (succeeded && hasErrors),
            readinessLine,
            downloadedLine,
            verifiedLine,
            fallbackLine,
            skippedLine);

        return new ScriptExecutionResult
        {
            Action = action,
            DisplayName = displayName,
            Succeeded = succeeded,
            HasWarnings = hasWarnings || indicatesPartial || (succeeded && hasErrors),
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
        string downloadedLine,
        string verifiedLine,
        string fallbackLine,
        string skippedLine)
    {
        if (action is ScriptActionType.SetupUsb or ScriptActionType.UpdateUsb)
        {
            var parts = new[]
            {
                readinessLine,
                downloadedLine,
                verifiedLine,
                fallbackLine,
                skippedLine
            }
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

            if (parts.Length > 0)
            {
                var detail = string.Join(" ", parts);
                if (!succeeded)
                {
                    return detail + " Review the log pane for the failed items and fallback coverage.";
                }

                if (hasWarnings)
                {
                    return detail + " The command completed, but the backend reported warnings that should be reviewed.";
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
