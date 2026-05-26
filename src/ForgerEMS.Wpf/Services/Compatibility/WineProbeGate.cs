using VentoyToolkitSetup.Wpf.Infrastructure;

namespace VentoyToolkitSetup.Wpf.Services.Compatibility;

/// <summary>
/// Central decision point for whether a Windows-only probe (WMI, native
/// sensors, driver enumeration, etc.) should run in the current environment.
/// Under Wine the answer is "no, report Unsupported"; on native Windows the
/// probe runs as normal. Probes call this instead of inventing their own
/// platform checks so the policy stays in one place.
/// </summary>
/// <remarks>
/// Important: a probe that is gated off by this class must NOT lower scan
/// confidence or surface as a failure. Use
/// <see cref="WineProbeOutcome.UnsupportedUnderWine"/> to flag the result as
/// "compatibility limited" rather than "broken".
/// </remarks>
public static class WineProbeGate
{
    /// <summary>
    /// Overrides the ambient environment for testing. Production callers
    /// leave this null and pick up <c>App.CompatibilityEnvironment</c>.
    /// </summary>
    public static CompatibilityEnvironment? OverrideEnvironment { get; set; }

    private static CompatibilityEnvironment? Current => OverrideEnvironment ?? App.CompatibilityEnvironment;

    /// <summary>
    /// True if the host is in compatibility mode (Wine or Linux-likely);
    /// callers should skip native Windows probes and return an Unsupported
    /// outcome instead.
    /// </summary>
    public static bool IsCompatibilityMode => Current?.IsCompatibilityMode == true;

    /// <summary>
    /// True if the probe should run normally. Equivalent to
    /// <c>!IsCompatibilityMode</c>; named affirmatively so the call site
    /// reads as "if the probe is allowed".
    /// </summary>
    public static bool IsWindowsOnlyProbeAllowed => !IsCompatibilityMode;

    /// <summary>
    /// Build the standard "compatibility limited" message for a probe.
    /// Uses neutral language ("limited", not "failed") so downstream
    /// scoring does not penalise the user's hardware.
    /// </summary>
    public static string DescribeUnsupported(string probeName)
    {
        return $"{probeName} is unsupported in Wine compatibility mode; this is a host limitation, not a hardware fault.";
    }
}
