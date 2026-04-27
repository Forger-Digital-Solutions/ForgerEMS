using System;
using System.Collections.Generic;

namespace VentoyToolkitSetup.Wpf.Models;

public sealed class PowerShellRunRequest
{
    public string DisplayName { get; init; } = "PowerShell";

    public string WorkingDirectory { get; init; } = string.Empty;

    public string? ScriptPath { get; init; }

    public string? InlineCommand { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    public string? ProgressItemName { get; init; }
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
