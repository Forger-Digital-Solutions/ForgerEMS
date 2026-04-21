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
        var lastError = runResult.OutputLines.LastOrDefault(line => line.Severity == LogSeverity.Error)?.Text;

        var summary = runResult.Succeeded
            ? BuildSuccessSummary(action, displayName, hasWarnings)
            : (!string.IsNullOrWhiteSpace(lastError) ? lastError : $"{displayName} exited with code {runResult.ExitCode}.");

        var details = runResult.Succeeded
            ? (hasWarnings
                ? "The command completed successfully, but the backend reported warnings that should be reviewed in the log pane."
                : "The command completed successfully.")
            : $"PowerShell exited with code {runResult.ExitCode}. Review the log pane for backend details.";

        return new ScriptExecutionResult
        {
            Action = action,
            DisplayName = displayName,
            Succeeded = runResult.Succeeded,
            HasWarnings = hasWarnings,
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
}
