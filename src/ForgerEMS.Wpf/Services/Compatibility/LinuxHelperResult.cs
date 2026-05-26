using System;
using System.Collections.Generic;

namespace VentoyToolkitSetup.Wpf.Services.Compatibility;

/// <summary>
/// Result envelope returned by <see cref="LinuxHelperService"/>. Always
/// non-null; <see cref="Availability"/> describes the outcome and
/// <see cref="Snapshot"/> is populated only when the JSON parsed cleanly.
/// </summary>
public sealed class LinuxHelperResult
{
    public LinuxHelperResult(
        LinuxHelperAvailability availability,
        LinuxHelperSnapshot? snapshot,
        string scriptPath,
        TimeSpan elapsed,
        string? failureReason,
        IReadOnlyList<string> diagnostics)
    {
        Availability = availability;
        Snapshot = snapshot;
        ScriptPath = scriptPath;
        Elapsed = elapsed;
        FailureReason = failureReason;
        Diagnostics = diagnostics;
    }

    public LinuxHelperAvailability Availability { get; }

    public LinuxHelperSnapshot? Snapshot { get; }

    /// <summary>Path the helper was located at (empty when ScriptMissing).</summary>
    public string ScriptPath { get; }

    public TimeSpan Elapsed { get; }

    /// <summary>Human-readable reason; empty/null when <see cref="Availability"/> is Available.</summary>
    public string? FailureReason { get; }

    /// <summary>Free-form diagnostic log lines (e.g. captured stderr, missing tools).</summary>
    public IReadOnlyList<string> Diagnostics { get; }

    public bool IsAvailable => Availability == LinuxHelperAvailability.Available && Snapshot is not null;
}
