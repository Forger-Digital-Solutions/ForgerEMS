using System;
using System.Collections.Generic;

namespace VentoyToolkitSetup.Wpf.Models;

public enum PowerShellHeartbeatKind
{
    /// <summary>Long downloads — heartbeat mentions download progress.</summary>
    Download,

    /// <summary>Scripts that mostly scan/verify without byte progress (toolkit health, etc.).</summary>
    LongRunningScan
}

public sealed class PowerShellRunRequest
{
    public string DisplayName { get; init; } = "PowerShell";

    public string WorkingDirectory { get; init; } = string.Empty;

    public string? ScriptPath { get; init; }

    public string? InlineCommand { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    public string? ProgressItemName { get; init; }

    public PowerShellHeartbeatKind HeartbeatKind { get; init; } = PowerShellHeartbeatKind.Download;

    /// <summary>When set (with <see cref="ProgressItemName"/>), replaces the default download heartbeat text. Idle span is time since last script output.</summary>
    public Func<TimeSpan, string?>? BuildDownloadHeartbeatMessage { get; init; }
}

public sealed class PowerShellRunResult
{
    public string DisplayName { get; init; } = string.Empty;

    public int ExitCode { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset FinishedAtUtc { get; init; }

    public string StandardOutputText { get; init; } = string.Empty;

    public string StandardErrorText { get; init; } = string.Empty;

    public IReadOnlyList<LogLine> OutputLines { get; init; } = Array.Empty<LogLine>();

    public bool Succeeded => ExitCode == 0;
}
