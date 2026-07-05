namespace VentoyToolkitSetup.Wpf.Infrastructure;

internal static class AppReleaseInfo
{
    /// <summary>Semantic version for update checks and diagnostics (matches .csproj InformationalVersion).</summary>
    public const string Version = "1.2.4-preview.3";

    /// <summary>Primary user-facing version line in the shell.</summary>
    public const string DisplayVersion = "ForgerEMS v1.2.4 Public Preview";

    /// <summary>Short footer / welcome subtitle (single line preferred).</summary>
    public const string ReleaseIdentifier =
        "ForgerEMS v1.2.4 Public Preview \u2014 technician USB toolkit, Dr. Forge Intake, Toolkit Manager, Kyra";

    public const string PublicPreviewBannerLine =
        "ForgerEMS Public Preview \u2014 built for technicians, rebuilders, and power users.";
}

internal static class FeatureFlags
{
    public const bool AdvancedPredictiveHealth = false;
    public const bool ToolReputationChecks = false;
    public const bool ScheduledMaintenance = false;
    public const bool BrandedReports = false;
}
