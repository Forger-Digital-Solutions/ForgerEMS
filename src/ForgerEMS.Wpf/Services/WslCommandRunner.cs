using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>
/// Beta-safe helper to run user-initiated WSL commands (no elevation, no auto-destructive presets).
/// </summary>
public static class WslCommandRunner
{
    public static string WslExecutablePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe");

    public static bool IsWslInstalled() => File.Exists(WslExecutablePath);

    /// <summary>
    /// Runs <c>wsl.exe</c> with the given arguments as Windows would (e.g. <c>--list --verbose</c>), not inside <c>sh -lc</c>.
    /// </summary>
    public static async Task<(int ExitCode, string CombinedOutput)> RunHostWslArgumentsAsync(
        IReadOnlyList<string> wslArguments,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        IProgress<string>? lineProgress = null)
    {
        if (wslArguments is null || wslArguments.Count == 0)
        {
            return (1, "No WSL arguments supplied.");
        }

        if (!IsWslInstalled())
        {
            return (1, "WSL was not detected. Install WSL/Ubuntu from Microsoft Store or run wsl --install from an elevated prompt.");
        }

        var startInfo = new ProcessStartInfo(WslExecutablePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in wslArguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
                SafeReport(lineProgress, e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
                SafeReport(lineProgress, "[stderr] " + e.Data);
            }
        };

        try
        {
            try
            {
                if (!process.Start())
                {
                    return (1, "Failed to start wsl.exe.");
                }
            }
            catch (Win32Exception ex)
            {
                return (1, "Could not start wsl.exe: " + ex.Message);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);

            var combined = stdout.ToString();
            if (stderr.Length > 0)
            {
                combined += Environment.NewLine + "[stderr]" + Environment.NewLine + stderr;
            }

            return (process.ExitCode, combined.TrimEnd());
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            return (130, "Command was cancelled, timed out, or the session ended.");
        }
        catch (Exception ex)
        {
            TryKillProcessTree(process);
            return (1, "WSL command failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Runs a single-line shell command inside default WSL distro via <c>sh -lc</c>.
    /// </summary>
    public static async Task<(int ExitCode, string CombinedOutput)> RunShellCommandAsync(
        string singleLineCommand,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        IProgress<string>? lineProgress = null)
    {
        if (string.IsNullOrWhiteSpace(singleLineCommand))
        {
            return (1, "Enter a command to run.");
        }

        if (!IsWslInstalled())
        {
            return (1, "WSL was not detected. Install WSL/Ubuntu from Microsoft Store or run wsl --install from an elevated prompt.");
        }

        var startInfo = new ProcessStartInfo(WslExecutablePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add("sh");
        startInfo.ArgumentList.Add("-lc");
        startInfo.ArgumentList.Add(singleLineCommand.Trim());

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
                SafeReport(lineProgress, e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
                SafeReport(lineProgress, "[stderr] " + e.Data);
            }
        };

        try
        {
            try
            {
                if (!process.Start())
                {
                    return (1, "Failed to start wsl.exe.");
                }
            }
            catch (Win32Exception ex)
            {
                return (1, "Could not start wsl.exe: " + ex.Message);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);

            var combined = stdout.ToString();
            if (stderr.Length > 0)
            {
                combined += Environment.NewLine + "[stderr]" + Environment.NewLine + stderr;
            }

            return (process.ExitCode, combined.TrimEnd());
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            return (130, "Command was cancelled, timed out, or the session ended.");
        }
        catch (Exception ex)
        {
            TryKillProcessTree(process);
            return (1, "WSL command failed: " + ex.Message);
        }
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private static void SafeReport(IProgress<string>? lineProgress, string line)
    {
        if (lineProgress is null)
        {
            return;
        }

        try
        {
            lineProgress.Report(line);
        }
        catch (Exception exception)
        {
            StartupDiagnosticLog.AppendException("WslCommandRunner.ProgressCallback", exception);
        }
    }
}
