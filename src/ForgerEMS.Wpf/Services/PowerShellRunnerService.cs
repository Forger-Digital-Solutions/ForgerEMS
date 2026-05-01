using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services;

public interface IPowerShellRunnerService
{
    Task<PowerShellRunResult> RunAsync(
        PowerShellRunRequest request,
        Action<LogLine>? onOutput = null,
        CancellationToken cancellationToken = default);
}

public sealed class PowerShellRunnerService : IPowerShellRunnerService
{
    public async Task<PowerShellRunResult> RunAsync(
        PowerShellRunRequest request,
        Action<LogLine>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            throw new InvalidOperationException("A working directory is required.");
        }

        if (!Directory.Exists(request.WorkingDirectory))
        {
            throw new DirectoryNotFoundException($"Working directory not found: {request.WorkingDirectory}");
        }

        if (string.IsNullOrWhiteSpace(request.ScriptPath) && string.IsNullOrWhiteSpace(request.InlineCommand))
        {
            throw new InvalidOperationException("Either ScriptPath or InlineCommand must be supplied.");
        }

        var outputLines = new List<LogLine>();
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();
        var startedAtUtc = DateTimeOffset.UtcNow;
        var lastOutputUtc = DateTimeOffset.UtcNow;
        var sync = new object();

        using var process = new Process
        {
            StartInfo = BuildStartInfo(request),
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                return;
            }

            stdoutBuilder.AppendLine(eventArgs.Data);
            PublishLine(eventArgs.Data, isErrorStream: false);
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                return;
            }

            stderrBuilder.AppendLine(eventArgs.Data);
            PublishLine(eventArgs.Data, isErrorStream: true);
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start PowerShell for {request.DisplayName}.");
        }

        process.StandardInput.Close();

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var registration = cancellationToken.Register(() =>
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
                // Best effort only.
            }
        });

        var heartbeatTask = Task.Run(async () =>
        {
            while (!process.HasExited && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(12), cancellationToken).ConfigureAwait(false);

                if (process.HasExited || cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(request.ProgressItemName))
                {
                    continue;
                }

                if (DateTimeOffset.UtcNow - lastOutputUtc >= TimeSpan.FromSeconds(12))
                {
                    PublishLine($"[INFO] Downloading {request.ProgressItemName}... working (no progress data)", isErrorStream: false);
                }
            }
        }, cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        process.WaitForExit();
        try
        {
            await heartbeatTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        cancellationToken.ThrowIfCancellationRequested();

        return new PowerShellRunResult
        {
            DisplayName = request.DisplayName,
            ExitCode = process.ExitCode,
            StartedAtUtc = startedAtUtc,
            FinishedAtUtc = DateTimeOffset.UtcNow,
            StandardOutputText = stdoutBuilder.ToString(),
            StandardErrorText = stderrBuilder.ToString(),
            OutputLines = outputLines.ToArray()
        };

        void PublishLine(string text, bool isErrorStream)
        {
            var line = new LogLine(DateTimeOffset.Now, text, Classify(text, isErrorStream), isErrorStream);
            lock (sync)
            {
                outputLines.Add(line);
            }

            lastOutputUtc = DateTimeOffset.UtcNow;

            onOutput?.Invoke(line);
        }
    }

    private static ProcessStartInfo BuildStartInfo(PowerShellRunRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = GetPowerShellExecutable(),
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");

        if (!string.IsNullOrWhiteSpace(request.ScriptPath))
        {
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(request.ScriptPath);

            foreach (var argument in request.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }
        else
        {
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(request.InlineCommand ?? string.Empty);
        }

        return startInfo;
    }

    private static string GetPowerShellExecutable()
    {
        var windowsPowerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        return File.Exists(windowsPowerShell) ? windowsPowerShell : "powershell.exe";
    }

    private static LogSeverity Classify(string text, bool isErrorStream)
    {
        if (isErrorStream)
        {
            return LogSeverity.Error;
        }

        var normalized = text.Trim();
        if (normalized.Length == 0)
        {
            return LogSeverity.Info;
        }

        if (normalized.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("[FAIL]", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("verification failed", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("USB readiness: PARTIALLY STAGED", StringComparison.OrdinalIgnoreCase))
        {
            return LogSeverity.Error;
        }

        if (normalized.Contains("[WARN]", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("[ACTION]", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Warnings: 1", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Warnings: 2", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("[online]", StringComparison.OrdinalIgnoreCase))
        {
            return LogSeverity.Warning;
        }

        if (normalized.Contains("[OK]", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("[COMPLETE]", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("[PASS]", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("verification passed", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Restore succeeded", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("USB readiness: READY", StringComparison.OrdinalIgnoreCase))
        {
            return LogSeverity.Success;
        }

        return LogSeverity.Info;
    }
}
