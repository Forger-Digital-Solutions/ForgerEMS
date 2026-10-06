using System.Reflection;

namespace VentoyToolkitSetup.Wpf.Infrastructure;

internal static class AppReleaseInfo
{
    /// <summary>Semantic version for update checks and diagnostics (assembly informational version sourced from VERSION).</summary>
    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    /// <summary>Primary user-facing version line in the shell.</summary>
    public static string DisplayVersion => $"ForgerEMS v{Version}";

    /// <summary>Short footer / welcome subtitle (single line preferred).</summary>
    public static string ReleaseIdentifier =>
        $"ForgerEMS v{Version} — technician USB toolkit, Dr. Forge Intake, Toolkit Manager";

    public const string ProductBannerLine =
        "ForgerEMS — built for technicians, rebuilders, and power users.";
}

internal static class FeatureFlags
{
    public const bool AdvancedPredictiveHealth = false;
    public const bool ToolReputationChecks = false;
    public const bool ScheduledMaintenance = false;
    public const bool BrandedReports = false;
}
