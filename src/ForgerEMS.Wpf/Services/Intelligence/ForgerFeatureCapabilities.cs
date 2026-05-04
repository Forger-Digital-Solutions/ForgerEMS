namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

/// <summary>Feature capability flags (prep only — no licensing enforcement).</summary>
public static class ForgerFeatureCapabilities
{
    public static bool FreeBasicUsbDetection => true;

    public static bool FreeSystemScan => true;

    public static bool FreeBasicDiagnostics => true;

    public static bool ProUsbMappingWorkflow => false;

    public static bool ProPerPortBenchmarking => false;

    public static bool ProTopologyVisualization => false;

    public static bool ProAdvancedDiagnosticsReasoning => false;

    public static bool ProSavedProfiles => false;
}
