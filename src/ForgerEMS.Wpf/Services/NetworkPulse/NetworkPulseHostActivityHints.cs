namespace VentoyToolkitSetup.Wpf.Services.NetworkPulse;

/// <summary>Hints from the host app to avoid competing with heavy network/USB work.</summary>
public readonly record struct NetworkPulseHostActivityHints(
    bool AppUpdateDownloadInProgress);
