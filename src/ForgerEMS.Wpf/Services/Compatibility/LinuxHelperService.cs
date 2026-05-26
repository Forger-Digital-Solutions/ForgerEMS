using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Infrastructure;

namespace VentoyToolkitSetup.Wpf.Services.Compatibility;

/// <summary>
/// Safe, read-only bridge to <c>tools/linux/forgerems-linux-helper.sh</c>.
/// The helper is optional: ForgerEMS starts and runs without it. Even when
/// invoked, no result is allowed to crash the UI thread — every error path
/// returns a typed <see cref="LinuxHelperResult"/> describing what went
/// wrong.
/// </summary>
/// <remarks>
/// Hard rules enforced by this class:
/// <list type="bullet">
/// <item>Never invokes the helper unless compatibility mode is active.</item>
/// <item>Never asks for or requires root.</item>
/// <item>Never executes destructive commands (no dd, mkfs, wipefs, parted, fdisk write, sgdisk write, mount writes).</item>
/// <item>Times out and kills the child process after <see cref="DefaultTimeout"/>.</item>
/// <item>Captures stderr separately and limits its size so an unfriendly host cannot fill memory.</item>
/// </list>
/// </remarks>
public sealed class LinuxHelperService
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(8);
    private const int MaxStdoutBytes = 256 * 1024;
    private const int MaxStderrChars = 4 * 1024;

    private readonly Func<CompatibilityEnvironment?>? _environmentSelector;
    private readonly Func<string?>? _shellResolverOverride;
    private readonly Func<string?>? _scriptLocatorOverride;
    private readonly Func<string, string, TimeSpan, LinuxHelperProcessResult>? _runnerOverride;

    public LinuxHelperService(
        Func<CompatibilityEnvironment?>? environmentSelector = null,
        Func<string?>? shellResolverOverride = null,
        Func<string?>? scriptLocatorOverride = null,
        Func<string, string, TimeSpan, LinuxHelperProcessResult>? runnerOverride = null)
    {
        _environmentSelector = environmentSelector;
        _shellResolverOverride = shellResolverOverride;
        _scriptLocatorOverride = scriptLocatorOverride;
        _runnerOverride = runnerOverride;
    }

    public Task<LinuxHelperResult> ProbeAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ProbeCore(cancellationToken), cancellationToken);
    }

    private LinuxHelperResult ProbeCore(CancellationToken cancellationToken)
    {
        var diagnostics = new List<string>();
        var environment = (_environmentSelector ?? DefaultEnvironmentSelector)();
        if (environment is not { IsCompatibilityMode: true })
        {
            diagnostics.Add("Helper skipped: not in compatibility mode.");
            return new LinuxHelperResult(
                LinuxHelperAvailability.NotApplicable,
                snapshot: null,
                scriptPath: string.Empty,
                elapsed: TimeSpan.Zero,
                failureReason: "Not in compatibility mode.",
                diagnostics: diagnostics);
        }

        var scriptPath = (_scriptLocatorOverride ?? LocateHelperScript)();
        if (string.IsNullOrEmpty(scriptPath) || !File.Exists(scriptPath))
        {
            diagnostics.Add($"Helper script not found. Probed: {scriptPath ?? "(null)"}.");
            return new LinuxHelperResult(
                LinuxHelperAvailability.ScriptMissing,
                snapshot: null,
                scriptPath: scriptPath ?? string.Empty,
                elapsed: TimeSpan.Zero,
                failureReason: "Linux helper script could not be located.",
                diagnostics: diagnostics);
        }

        var shell = (_shellResolverOverride ?? ResolveShell)();
        if (string.IsNullOrEmpty(shell))
        {
            diagnostics.Add("No POSIX shell available in this prefix (bash, sh).");
            return new LinuxHelperResult(
                LinuxHelperAvailability.ShellUnavailable,
                snapshot: null,
                scriptPath: scriptPath,
                elapsed: TimeSpan.Zero,
                failureReason: "Could not locate bash/sh to invoke the helper.",
                diagnostics: diagnostics);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        var runner = _runnerOverride ?? RunHelperProcess;
        LinuxHelperProcessResult process;
        try
        {
            process = runner(shell, scriptPath, DefaultTimeout);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            diagnostics.Add($"Helper process threw safely: {ex.GetType().Name}: {ex.Message}");
            return new LinuxHelperResult(
                LinuxHelperAvailability.Failed,
                snapshot: null,
                scriptPath: scriptPath,
                elapsed: stopwatch.Elapsed,
                failureReason: "Helper process could not be started.",
                diagnostics: diagnostics);
        }
        stopwatch.Stop();

        if (!string.IsNullOrEmpty(process.Stderr))
        {
            diagnostics.Add("stderr: " + Truncate(process.Stderr, MaxStderrChars));
        }

        if (process.TimedOut)
        {
            return new LinuxHelperResult(
                LinuxHelperAvailability.TimedOut,
                snapshot: null,
                scriptPath: scriptPath,
                elapsed: stopwatch.Elapsed,
                failureReason: $"Helper exceeded the {DefaultTimeout.TotalSeconds:0} s timeout.",
                diagnostics: diagnostics);
        }

        if (process.ExitCode != 0)
        {
            return new LinuxHelperResult(
                LinuxHelperAvailability.Failed,
                snapshot: null,
                scriptPath: scriptPath,
                elapsed: stopwatch.Elapsed,
                failureReason: $"Helper exited with code {process.ExitCode}.",
                diagnostics: diagnostics);
        }

        LinuxHelperSnapshot snapshot;
        try
        {
            snapshot = LinuxHelperSnapshot.Parse(process.Stdout ?? string.Empty);
        }
        catch (Exception ex)
        {
            diagnostics.Add($"JSON parse failed: {ex.GetType().Name}: {ex.Message}");
            return new LinuxHelperResult(
                LinuxHelperAvailability.ParseError,
                snapshot: null,
                scriptPath: scriptPath,
                elapsed: stopwatch.Elapsed,
                failureReason: "Helper produced output that did not parse as JSON.",
                diagnostics: diagnostics);
        }

        if (!snapshot.IsSchemaSupported)
        {
            diagnostics.Add($"Helper schema {snapshot.Schema} is not recognised by this build.");
            return new LinuxHelperResult(
                LinuxHelperAvailability.UnsupportedSchema,
                snapshot: snapshot,
                scriptPath: scriptPath,
                elapsed: stopwatch.Elapsed,
                failureReason: "Helper schema is newer or older than expected.",
                diagnostics: diagnostics);
        }

        return new LinuxHelperResult(
            LinuxHelperAvailability.Available,
            snapshot: snapshot,
            scriptPath: scriptPath,
            elapsed: stopwatch.Elapsed,
            failureReason: null,
            diagnostics: diagnostics);
    }

    /// <summary>Default environment selector — pulls from <c>App.CompatibilityEnvironment</c>.</summary>
    private static CompatibilityEnvironment? DefaultEnvironmentSelector() => App.CompatibilityEnvironment;

    /// <summary>
    /// Walks upward from the executable looking for <c>tools/linux/forgerems-linux-helper.sh</c>.
    /// Returns null when not found; never throws.
    /// </summary>
    public static string? LocateHelperScript()
    {
        try
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "tools", "linux", "forgerems-linux-helper.sh"),
                Path.Combine(AppContext.BaseDirectory, "linux", "forgerems-linux-helper.sh"),
            };

            foreach (var direct in candidates)
            {
                if (File.Exists(direct))
                {
                    return direct;
                }
            }

            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                var probe = Path.Combine(dir, "tools", "linux", "forgerems-linux-helper.sh");
                if (File.Exists(probe))
                {
                    return probe;
                }

                dir = Path.GetDirectoryName(dir);
            }
        }
        catch
        {
        }

        return null;
    }

    /// <summary>
    /// Best-effort resolve of bash or sh. Under a Wine prefix the host's
    /// PATH is usually visible via <c>Z:\</c> mapping; we probe the common
    /// fixed locations as a fallback.
    /// </summary>
    public static string? ResolveShell()
    {
        try
        {
            foreach (var name in new[] { "bash", "sh", "bash.exe", "sh.exe" })
            {
                var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                foreach (var dir in pathEnv.Split(Path.PathSeparator))
                {
                    if (string.IsNullOrWhiteSpace(dir))
                    {
                        continue;
                    }

                    var candidate = Path.Combine(dir.Trim(), name);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            foreach (var fixedPath in new[]
                     {
                         @"Z:\usr\bin\bash",
                         @"Z:\bin\bash",
                         @"Z:\usr\bin\sh",
                         @"Z:\bin\sh",
                         "/usr/bin/bash",
                         "/bin/bash",
                         "/usr/bin/sh",
                         "/bin/sh"
                     })
            {
                if (File.Exists(fixedPath))
                {
                    return fixedPath;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    /// <summary>
    /// Spawn the helper synchronously with a hard timeout. Stdout is read
    /// fully into memory but capped at <see cref="MaxStdoutBytes"/> so a
    /// runaway helper cannot exhaust the process heap.
    /// </summary>
    public static LinuxHelperProcessResult RunHelperProcess(string shell, string scriptPath, TimeSpan timeout)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = QuoteArgument(scriptPath),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        if (!process.Start())
        {
            return new LinuxHelperProcessResult(string.Empty, "Process.Start returned false.", -1, TimedOut: false);
        }

        var stdoutTask = ReadStreamWithLimit(process.StandardOutput, MaxStdoutBytes);
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)Math.Min(timeout.TotalMilliseconds, int.MaxValue)))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new LinuxHelperProcessResult(
                SafeAwait(stdoutTask),
                SafeAwait(stderrTask),
                ExitCode: -1,
                TimedOut: true);
        }

        return new LinuxHelperProcessResult(
            SafeAwait(stdoutTask),
            SafeAwait(stderrTask),
            ExitCode: process.ExitCode,
            TimedOut: false);
    }

    private static async Task<string> ReadStreamWithLimit(StreamReader reader, int byteLimit)
    {
        var buffer = new char[4096];
        var bytes = 0;
        var sb = new System.Text.StringBuilder(8192);
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
        {
            bytes += read;
            if (bytes > byteLimit)
            {
                sb.Append(buffer, 0, Math.Max(0, buffer.Length - (bytes - byteLimit)));
                break;
            }

            sb.Append(buffer, 0, read);
        }

        return sb.ToString();
    }

    private static string SafeAwait(Task<string> task)
    {
        try
        {
            return task.Result ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string QuoteArgument(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static string Truncate(string value, int max)
    {
        return value.Length <= max ? value : value[..max] + "…";
    }
}

public readonly record struct LinuxHelperProcessResult(string Stdout, string Stderr, int ExitCode, bool TimedOut);
