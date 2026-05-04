namespace VentoyToolkitSetup.Wpf.Infrastructure;

internal static class AppReleaseInfo
{
    public const string Version = "1.1.12-rc.3";
    public const string DisplayVersion = "v1.1.12-rc.3 (Beta RC)";
    public const string ReleaseIdentifier = "ForgerEMS Beta v1.1.12-rc.3 \u2014 USB mapping wizard, quieter auto-benchmark, mapping fallbacks";
}

internal static class FeatureFlags
{
    public const bool AdvancedPredictiveHealth = false;
    public const bool ToolReputationChecks = false;
    public const bool ScheduledMaintenance = false;
    public const bool BrandedReports = false;
}
