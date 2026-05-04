namespace VentoyToolkitSetup.Wpf.Services.Licensing;

/// <summary>Honest user-facing maturity labels for screenshots and Kickstarter flows.</summary>
public static class FeatureStatusService
{
    public const string UsbBuilder = "Beta";
    public const string ToolkitManager = "Beta";
    public const string SystemIntelligence = "Beta";
    public const string Kyra = "Preview";
    public const string UsbIntelligence = "Pro Preview";
    public const string MarketplaceValuation = "Planned / experimental";
    public const string InternetSpeedTest = "Experimental";
    public const string TelemetryCrash = "Off unless enabled";

    public static string BuildFeatureMaturityGuide() =>
        """
        Feature maturity (honest labels)
        • USB Builder — Beta
        • Toolkit Manager — Beta
        • System Intelligence — Beta
        • Kyra — Preview (offline/local first; online optional)
        • USB Port Mapping / USB Intelligence — Pro Preview (topology is best-effort on Windows)
        • Marketplace / automated resale valuation — Planned / experimental where stubbed
        • Internet speed tests (if shown) — Experimental
        • Telemetry / crash reporting — Off unless you explicitly enable FORGEREMS_TELEMETRY_ENABLED / FORGEREMS_CRASH_REPORTING_ENABLED

        Pro Preview — available to beta testers during preview. Licensing is local/honor-system until commercial tiers ship.
        """;
}
