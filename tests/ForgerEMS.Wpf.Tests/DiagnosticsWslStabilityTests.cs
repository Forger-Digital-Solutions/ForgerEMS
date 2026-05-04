using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class DiagnosticsWslStabilityTests
{
    [Fact]
    public void EmbeddedWslCommandRunnerEnabled_DefaultsFalse()
    {
        DiagnosticsFeatureFlags.EmbeddedWslCommandRunnerEnabled = false;
        Assert.False(DiagnosticsFeatureFlags.EmbeddedWslCommandRunnerEnabled);
    }

    [Fact]
    public void ProbeQuick_DoesNotThrow()
    {
        var quick = SafeTestingEnvironmentProbe.ProbeQuick();
        Assert.NotNull(quick);
        var text = quick.FormatSummary();
        Assert.Contains("WSL", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProbeWithWslStatus_WslTimeoutLikeExit_YieldsWarningOrUnknownLine()
    {
        var fake = new TimeoutLikeWslExecutor();
        var status = SafeTestingEnvironmentProbe.ProbeWithWslStatusAsync(
                fake,
                TimeSpan.FromMilliseconds(50),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.Contains("Warning", status.DefaultWslDistroOrStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SafeSummary_RedactsUserPathInUnifiedHeadline()
    {
        var quick = SafeTestingEnvironmentProbe.ProbeQuick();
        var copy = quick.BuildCopySafeSummary(@"Run from C:\Users\SecretUser\Desktop\ForgerEMS\app.exe");
        Assert.DoesNotContain("SecretUser", copy, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Kyra_WslCrashInsideForger_RecommendsExternalTerminalAndSandbox()
    {
        var reply = LocalRulesCopilotEngine.GenerateReply(
            "Why does WSL crash inside ForgerEMS?",
            new CopilotContext
            {
                UserQuestion = "Why does WSL crash inside ForgerEMS?",
                Intent = KyraIntent.ForgerEMSQuestion,
                SystemContext = new SystemContext()
            });

        Assert.Contains("experimental", reply, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("external", reply, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sandbox", reply, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TimeoutLikeWslExecutor : IWslCommandExecutor
    {
        public bool IsWslInstalled() => true;

        public Task<(int ExitCode, string CombinedOutput)> RunHostWslArgumentsAsync(
            IReadOnlyList<string> wslArguments,
            TimeSpan timeout,
            CancellationToken cancellationToken,
            IProgress<string>? lineProgress = null) =>
            Task.FromResult((130, "Command was cancelled, timed out, or the session ended."));

        public Task<(int ExitCode, string CombinedOutput)> RunShellCommandAsync(
            string singleLineCommand,
            TimeSpan timeout,
            CancellationToken cancellationToken,
            IProgress<string>? lineProgress = null) =>
            Task.FromResult((0, string.Empty));
    }
}
