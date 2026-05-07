using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Maps CopilotSettings community consent to Kyra chat metadata chips (no secrets).</summary>
public static class KyraCommunityMetadataFormatter
{
    /// <summary>
    /// When true, metadata may say “Community sharing enabled”. Kept false until a reviewed upload client ships.
    /// </summary>
    public static bool UploadEndpointConfigured => false;

    public static bool IsCommunityPreviewOptedIn(CopilotSettings? settings)
    {
        if (settings is null)
        {
            return false;
        }

        return settings.KyraCommunitySharingEnabled ||
               settings.KyraShareResolvedIssueFixPatterns ||
               settings.KyraShareHardwareCompatibilityPerformancePatterns ||
               settings.KyraShareCrashErrorDiagnostics;
    }

    /// <summary>Compact segment for <see cref="MainViewModel"/> metadata summary line.</summary>
    public static string SummaryChip(CopilotSettings? settings)
    {
        if (!IsCommunityPreviewOptedIn(settings))
        {
            return "Community sharing off";
        }

        if (UploadEndpointConfigured)
        {
            return "Community sharing enabled";
        }

        return "Community preview only";
    }

    public static string DetailsParagraph(CopilotSettings? settings)
    {
        if (!IsCommunityPreviewOptedIn(settings))
        {
            return "Community intelligence sharing: off (local-only by default).";
        }

        if (UploadEndpointConfigured)
        {
            return "Community intelligence sharing: enabled for this build (upload path active).";
        }

        return "Community intelligence sharing: opted in — preview/export only; no upload endpoint in this build.";
    }
}
