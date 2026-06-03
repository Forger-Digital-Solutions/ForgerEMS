namespace VentoyToolkitSetup.Wpf.Services.Compatibility;

/// <summary>
/// Stable vocabulary used by probes to classify Wine-related results.
/// Returned to UI/diagnostic layers so they can distinguish a real hardware
/// fault ("Failed") from a compatibility-mode skip ("UnsupportedUnderWine").
/// Confidence scoring should treat the Unsupported/Limited/LinuxHelperRequired
/// outcomes as neutral, not negative.
/// </summary>
public enum WineProbeOutcome
{
    /// <summary>Probe ran successfully on native Windows.</summary>
    NativeOk = 0,

    /// <summary>Probe was skipped because it is a Windows-only call under Wine.</summary>
    UnsupportedUnderWine = 1,

    /// <summary>Probe ran but the result is partial because of Wine limitations.</summary>
    CompatibilityLimited = 2,

    /// <summary>Probe needs the future Linux helper to produce a meaningful answer.</summary>
    LinuxHelperRequired = 3,

    /// <summary>Probe is Windows-only and skipped because we are on Linux directly.</summary>
    WindowsOnlyProbe = 4
}
