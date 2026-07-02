namespace VentoyToolkitSetup.Wpf.Services.Licensing;

/// <summary>Customer-facing summary of what ships in this build (Settings → What's included).</summary>
public static class FeatureStatusService
{
    public static string BuildFeatureMaturityGuide() =>
        """
        • USB Builder — build and maintain Ventoy-based technician USB media on removable targets only.
        • Toolkit Manager — toolkit health, readiness score, managed and manual items, and reports.
        • Driver Hub — vendor-first driver links and guidance; no automatic driver installs.
        • Port / USB Intelligence — Port Mapping Wizard, Drive Validator, and USB Benchmark for safe removable targets.
        • Battery Health & System Specifications — local device summaries from your own scans.
        • Kyra Assistant — local-first help; optional cloud providers only when you configure them.
        """;
}
