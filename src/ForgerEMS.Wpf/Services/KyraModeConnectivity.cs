namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>
/// Keeps persisted Kyra UI mode aligned with whether any online provider is actually usable.
/// </summary>
public static class KyraModeConnectivity
{
    public static CopilotMode NormalizeModeForAvailableProviders(
        CopilotMode mode,
        bool anyOnlineConfigured,
        bool localOllamaEnabled,
        bool localLmStudioEnabled)
    {
        if (localOllamaEnabled || localLmStudioEnabled || anyOnlineConfigured)
        {
            return mode;
        }

        return mode switch
        {
            CopilotMode.OnlineWhenAvailable or CopilotMode.OnlineAssisted or CopilotMode.FreeApiPool
                or CopilotMode.ForgerEmsCloudFuture or CopilotMode.BringYourOwnKey => CopilotMode.HybridAuto,
            _ => mode
        };
    }
}
