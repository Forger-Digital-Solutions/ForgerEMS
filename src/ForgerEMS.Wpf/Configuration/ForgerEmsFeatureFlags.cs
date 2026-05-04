namespace VentoyToolkitSetup.Wpf.Configuration;

/// <summary>Feature flags driven by safe defaults and environment (see docs/ENVIRONMENT.md).</summary>
public static class ForgerEmsFeatureFlags
{
    public static bool TelemetryEnabled => ForgerEmsEnvironmentConfiguration.TelemetryEnabled;

    public static bool CrashReportingEnabled => ForgerEmsEnvironmentConfiguration.CrashReportingEnabled;

    public static bool MarketplaceEnabled => ForgerEmsEnvironmentConfiguration.MarketplaceEnabled;

    public static bool EbayIntegrationEnabled => ForgerEmsEnvironmentConfiguration.EbayEnabled;
}
