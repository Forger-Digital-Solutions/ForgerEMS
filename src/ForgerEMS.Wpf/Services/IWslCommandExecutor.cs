using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VentoyToolkitSetup.Wpf.Services;

public interface IWslCommandExecutor
{
    bool IsWslInstalled();

    Task<(int ExitCode, string CombinedOutput)> RunHostWslArgumentsAsync(
        IReadOnlyList<string> wslArguments,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        IProgress<string>? lineProgress = null);

    Task<(int ExitCode, string CombinedOutput)> RunShellCommandAsync(
        string singleLineCommand,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        IProgress<string>? lineProgress = null);
}

public sealed class DefaultWslCommandExecutor : IWslCommandExecutor
{
    public static DefaultWslCommandExecutor Instance { get; } = new();

    private DefaultWslCommandExecutor()
    {
    }

    public bool IsWslInstalled() => WslCommandRunner.IsWslInstalled();

    public Task<(int ExitCode, string CombinedOutput)> RunHostWslArgumentsAsync(
        IReadOnlyList<string> wslArguments,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        IProgress<string>? lineProgress = null) =>
        WslCommandRunner.RunHostWslArgumentsAsync(wslArguments, timeout, cancellationToken, lineProgress);

    public Task<(int ExitCode, string CombinedOutput)> RunShellCommandAsync(
        string singleLineCommand,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        IProgress<string>? lineProgress = null) =>
        WslCommandRunner.RunShellCommandAsync(singleLineCommand, timeout, cancellationToken, lineProgress);
}
