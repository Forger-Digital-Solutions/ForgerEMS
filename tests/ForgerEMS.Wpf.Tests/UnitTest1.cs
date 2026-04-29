using System;
using System.Collections.Generic;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class ScriptStatusParserTests
{
    [Fact]
    public void ParseVerifyBackendSuccessWithoutWarningsReturnsSuccessSummary()
    {
        var parser = new ScriptStatusParser();
        var runResult = CreateRunResult(
            exitCode: 0,
            new LogLine(DateTimeOffset.UtcNow, "Verification complete.", LogSeverity.Info));

        var result = parser.Parse(ScriptActionType.VerifyBackend, "Verify backend", runResult);

        Assert.True(result.Succeeded);
        Assert.False(result.HasWarnings);
        Assert.Equal("Backend verification passed.", result.Summary);
        Assert.Equal("The command completed successfully.", result.Details);
    }

    [Fact]
    public void ParseSetupUsbPartialReadinessReturnsFailureAndWarnings()
    {
        var parser = new ScriptStatusParser();
        var runResult = CreateRunResult(
            exitCode: 0,
            new LogLine(DateTimeOffset.UtcNow, "USB readiness: PARTIALLY STAGED", LogSeverity.Warning),
            new LogLine(DateTimeOffset.UtcNow, "Downloaded successfully: 3", LogSeverity.Info),
            new LogLine(DateTimeOffset.UtcNow, "Verified successfully: 2", LogSeverity.Info));

        var result = parser.Parse(ScriptActionType.SetupUsb, "Setup USB", runResult);

        Assert.False(result.Succeeded);
        Assert.True(result.HasWarnings);
        Assert.Equal("USB readiness: PARTIALLY STAGED", result.Summary);
        Assert.Contains("Review the log pane for the failed items and fallback coverage.", result.Details);
    }

    private static PowerShellRunResult CreateRunResult(int exitCode, params LogLine[] lines)
    {
        return new PowerShellRunResult
        {
            ExitCode = exitCode,
            OutputLines = new List<LogLine>(lines)
        };
    }
}
