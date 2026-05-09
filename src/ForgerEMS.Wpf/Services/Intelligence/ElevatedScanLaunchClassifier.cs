using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public enum ElevatedScanFailureKind
{
    UnknownElevatedLaunchFailure,
    ElevatedScanLaunchFailed,
    UacCancelled,
    UacBlockedOrDenied,
    ElevatedProcessDidNotStart,
    ElevatedProcessStartedNoResult,
    ElevatedProcessTimedOut,
    BackendScriptMissing,
    PowerShellMissingOrBlocked
}

public sealed record ElevatedScanFailureAnalysis(
    ElevatedScanFailureKind Kind,
    string PrimaryUserMessage,
    IReadOnlyList<string> SupplementalActionLines,
    string AdvancedDiagnosticsLine);

public static class ElevatedScanLaunchClassifier
{
    public const int KnownShellElevatedLaunchPseudoExit = -196608;

    /// <summary>Classifies elevated System Intelligence launcher failures (UAC / admin handoff), not backend scan logic.</summary>
    public static ElevatedScanFailureAnalysis Analyze(PowerShellRunResult runResult, bool scanOutputLikelyMissing = false)
    {
        var exitCode = runResult.ExitCode;
        var combined = $"{runResult.StandardOutputText}\n{runResult.StandardErrorText}";
        var native = TryParseFirstNativeErrorCode(combined);
        var kind = runResult.TimedOut
            ? ElevatedScanFailureKind.ElevatedProcessTimedOut
            : Classify(exitCode, native, combined, scanOutputLikelyMissing);
        return BuildAnalysis(kind, exitCode, native);
    }

    public static ElevatedScanFailureAnalysis AnalyzeReasonLine(string reasonLine, int exitCode, string? supplementalText = null)
    {
        var combined = $"{reasonLine}\n{supplementalText ?? string.Empty}";
        var native = TryParseFirstNativeErrorCode(combined);
        var fromReason = TryParseProcessExitCode(reasonLine);
        var effectiveExit = fromReason ?? exitCode;
        var kind = Classify(effectiveExit, native, combined, scanOutputLikelyMissing: false);
        return BuildAnalysis(kind, effectiveExit, native);
    }

    public static ElevatedScanFailureKind Classify(
        int exitCode,
        int? nativeErrorCode,
        string combinedOutput,
        bool scanOutputLikelyMissing)
    {
        if (nativeErrorCode is ElevatedScanDiagnostics.TimeoutExitCode ||
            exitCode is ElevatedScanDiagnostics.TimeoutExitCode ||
            combinedOutput.Contains("timed out", StringComparison.OrdinalIgnoreCase))
        {
            return ElevatedScanFailureKind.ElevatedProcessTimedOut;
        }

        if (nativeErrorCode is 1223)
        {
            return ElevatedScanFailureKind.UacCancelled;
        }

        if (nativeErrorCode is 740 or 5)
        {
            return ElevatedScanFailureKind.UacBlockedOrDenied;
        }

        if (nativeErrorCode is 2 or 3 &&
            combinedOutput.Contains("script", StringComparison.OrdinalIgnoreCase))
        {
            return ElevatedScanFailureKind.BackendScriptMissing;
        }

        if (combinedOutput.Contains("PowerShell not found", StringComparison.OrdinalIgnoreCase) ||
            combinedOutput.Contains("powershell.exe", StringComparison.OrdinalIgnoreCase) &&
            combinedOutput.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return ElevatedScanFailureKind.PowerShellMissingOrBlocked;
        }

        if (combinedOutput.Contains("System Intelligence script missing", StringComparison.OrdinalIgnoreCase) ||
            combinedOutput.Contains("script missing", StringComparison.OrdinalIgnoreCase) &&
            combinedOutput.Contains("Invoke-ForgerEMSSystemScan", StringComparison.OrdinalIgnoreCase))
        {
            return ElevatedScanFailureKind.BackendScriptMissing;
        }

        if (combinedOutput.Contains("Elevation was cancelled before the scan started", StringComparison.OrdinalIgnoreCase))
        {
            return ElevatedScanFailureKind.ElevatedProcessDidNotStart;
        }

        if (scanOutputLikelyMissing && exitCode != 0)
        {
            return ElevatedScanFailureKind.ElevatedProcessStartedNoResult;
        }

        if (exitCode == KnownShellElevatedLaunchPseudoExit || unchecked((uint)exitCode) == 0xFFFD0000)
        {
            return ElevatedScanFailureKind.ElevatedScanLaunchFailed;
        }

        if (exitCode == 1223)
        {
            return ElevatedScanFailureKind.UacCancelled;
        }

        if (exitCode is 740 or 5)
        {
            return ElevatedScanFailureKind.UacBlockedOrDenied;
        }

        if (exitCode is 2 or 3)
        {
            return ElevatedScanFailureKind.BackendScriptMissing;
        }

        return ElevatedScanFailureKind.UnknownElevatedLaunchFailure;
    }

    public static IReadOnlyList<string> BuildSupplementalGuidance(ElevatedScanFailureAnalysis analysis)
    {
        return analysis.SupplementalActionLines;
    }

    private static ElevatedScanFailureAnalysis BuildAnalysis(
        ElevatedScanFailureKind kind,
        int exitCode,
        int? native)
    {
        var advanced = BuildAdvancedLine(exitCode, native, kind);
        var primary = BuildPrimary(kind);
        var supplemental = BuildSupplemental(kind);
        return new ElevatedScanFailureAnalysis(kind, primary, supplemental, advanced);
    }

    private static string BuildPrimary(ElevatedScanFailureKind kind) => kind switch
    {
        ElevatedScanFailureKind.UacCancelled =>
            "Elevated Scan did not start correctly. The UAC prompt was cancelled. Standard Scan results are still available.",
        ElevatedScanFailureKind.UacBlockedOrDenied =>
            "Elevated Scan did not start correctly. Windows blocked or denied administrator elevation. Standard Scan results are still available.",
        ElevatedScanFailureKind.ElevatedProcessDidNotStart =>
            "Elevated Scan did not start correctly. The elevated process did not start (UAC may have been dismissed or blocked). Standard Scan results are still available.",
        ElevatedScanFailureKind.ElevatedProcessStartedNoResult =>
            "Elevated Scan started but did not produce an updated report before exiting. Standard Scan results are still available.",
        ElevatedScanFailureKind.ElevatedProcessTimedOut =>
            "Elevated Scan was requested but did not finish in time. Standard Scan results are still available.",
        ElevatedScanFailureKind.BackendScriptMissing =>
            "Elevated Scan could not run because the System Intelligence backend script was not found. Standard Scan results are still available.",
        ElevatedScanFailureKind.PowerShellMissingOrBlocked =>
            "Elevated Scan could not run because PowerShell could not be started. Standard Scan results are still available.",
        ElevatedScanFailureKind.ElevatedScanLaunchFailed or ElevatedScanFailureKind.UnknownElevatedLaunchFailure =>
            "Elevated Scan did not start correctly. Windows cancelled, blocked, or failed the UAC/admin handoff. Standard Scan results are still available.",
        _ =>
            "Elevated Scan did not start correctly. Windows cancelled, blocked, or failed the UAC/admin handoff. Standard Scan results are still available."
    };

    private static IReadOnlyList<string> BuildSupplemental(ElevatedScanFailureKind kind)
    {
        var common = new List<string>
        {
            "Run ForgerEMS as administrator or retry and approve the UAC prompt.",
            "Use Copy Admin Command (if shown) to run the same scan from an elevated PowerShell window."
        };

        if (kind == ElevatedScanFailureKind.UacCancelled)
        {
            return new[]
            {
                "Retry Elevated Scan and approve the UAC prompt, or start ForgerEMS as administrator.",
                common[1]
            };
        }

        if (kind is ElevatedScanFailureKind.BackendScriptMissing or ElevatedScanFailureKind.PowerShellMissingOrBlocked)
        {
            return new[]
            {
                "Repair or reinstall ForgerEMS so backend files are present, then retry.",
                "If PowerShell is restricted by policy, ask your administrator or run from an elevated session."
            };
        }

        return common;
    }

    private static string BuildAdvancedLine(int exitCode, int? native, ElevatedScanFailureKind kind)
    {
        var hexExit = $"0x{unchecked((uint)exitCode):X8}";
        var nativePart = native is { } n ? $" Win32={n}." : string.Empty;
        return $"Advanced diagnostics: kind={kind}; exitCode={exitCode} ({hexExit});{nativePart}";
    }

    private static int? TryParseFirstNativeErrorCode(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = Regex.Match(text, @"NativeError[=:]\s*(?<n>\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (match.Success && int.TryParse(match.Groups["n"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
        {
            return code;
        }

        match = Regex.Match(text, @"Win32\s+(?<n>\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (match.Success && int.TryParse(match.Groups["n"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out code))
        {
            return code;
        }

        return null;
    }

    public static int? TryParseProcessExitCode(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        var match = Regex.Match(reason, @"code\s+(?<code>-?\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups["code"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)
            ? code
            : null;
    }
}
