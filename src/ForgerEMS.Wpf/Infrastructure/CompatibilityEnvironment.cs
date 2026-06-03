using System.Collections.Generic;

namespace VentoyToolkitSetup.Wpf.Infrastructure;

/// <summary>
/// Immutable snapshot of the detected runtime environment plus the
/// compatibility decisions ForgerEMS derives from it. Constructed once
/// during startup by <see cref="RuntimeCompatibilityService"/>.
/// </summary>
public sealed class CompatibilityEnvironment
{
    public CompatibilityEnvironment(
        RuntimePlatformKind platform,
        bool isWine,
        string? wineVersion,
        string? hostKernel,
        string? linuxDistro,
        bool isCompatibilityMode,
        bool forceSoftwareRendering,
        IReadOnlyList<string> unsupportedFeatures,
        IReadOnlyList<string> limitedFeatures,
        IReadOnlyList<string> detectionSignals)
    {
        Platform = platform;
        IsWine = isWine;
        WineVersion = wineVersion;
        HostKernel = hostKernel;
        LinuxDistro = linuxDistro;
        IsCompatibilityMode = isCompatibilityMode;
        ForceSoftwareRendering = forceSoftwareRendering;
        UnsupportedFeatures = unsupportedFeatures;
        LimitedFeatures = limitedFeatures;
        DetectionSignals = detectionSignals;
    }

    public RuntimePlatformKind Platform { get; }

    public bool IsWine { get; }

    public string? WineVersion { get; }

    public string? HostKernel { get; }

    public string? LinuxDistro { get; }

    /// <summary>
    /// True when ForgerEMS should run in degraded mode (today: only under Wine).
    /// Native Windows always reports false even if other signals are present.
    /// </summary>
    public bool IsCompatibilityMode { get; }

    /// <summary>
    /// True when WPF must use <c>RenderMode.SoftwareOnly</c> to avoid a
    /// wpfgfx_cor3 / wined3d crash during window initialization.
    /// </summary>
    public bool ForceSoftwareRendering { get; }

    /// <summary>
    /// Features that ForgerEMS will refuse to attempt under this environment
    /// (e.g. WMI providers known to fault under Wine).
    /// </summary>
    public IReadOnlyList<string> UnsupportedFeatures { get; }

    /// <summary>
    /// Features that may work but are degraded or partial under compatibility mode.
    /// </summary>
    public IReadOnlyList<string> LimitedFeatures { get; }

    /// <summary>
    /// Raw signal strings describing how detection arrived at its decision.
    /// Used for diagnostics; never shown to end users verbatim.
    /// </summary>
    public IReadOnlyList<string> DetectionSignals { get; }
}
