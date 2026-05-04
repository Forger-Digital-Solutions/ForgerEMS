namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

/// <summary>
/// Diagnostics UI feature switches. Embedded WSL output capture stays off by default for beta stability.
/// </summary>
public static class DiagnosticsFeatureFlags
{
    /// <summary>When false (default), the in-app WSL command runner is hidden and must not auto-run.</summary>
    public static bool EmbeddedWslCommandRunnerEnabled { get; set; }
}
