namespace VentoyToolkitSetup.Wpf.Services.Compatibility;

/// <summary>
/// Stable vocabulary for "did the Linux helper run, and how useful is its output?"
/// Used by the UI and diagnostic exporters to describe the helper state
/// without inventing inline strings.
/// </summary>
public enum LinuxHelperAvailability
{
    /// <summary>Compatibility mode is off (native Windows) — helper irrelevant.</summary>
    NotApplicable = 0,

    /// <summary>The helper script could not be located on disk.</summary>
    ScriptMissing = 1,

    /// <summary>Bash or a POSIX shell is not available to execute the script.</summary>
    ShellUnavailable = 2,

    /// <summary>The helper was invoked but exceeded its timeout.</summary>
    TimedOut = 3,

    /// <summary>The helper exited with a non-zero code.</summary>
    Failed = 4,

    /// <summary>The helper produced output but it could not be parsed as JSON.</summary>
    ParseError = 5,

    /// <summary>The helper produced JSON whose schema we do not recognize.</summary>
    UnsupportedSchema = 6,

    /// <summary>The helper produced a valid, parseable snapshot.</summary>
    Available = 7
}
