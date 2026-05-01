namespace VentoyToolkitSetup.Wpf.Services.KyraTools;

/// <summary>How a tool is surfaced in Kyra /provider and Advanced panels.</summary>
public enum KyraToolSurfaceCategory
{
    LiveData,
    LocalContext,
    CodeAssist,
    Marketplace
}

public enum KyraToolOperationalStatus
{
    Ready,
    NotConfigured,
    Disabled,
    Failed,
    TimedOut,
    MissingScan,
    Available
}

/// <summary>Facts from the host about local reports (no secrets).</summary>
public readonly struct KyraToolHostFacts
{
    public bool HasSystemIntelligenceScan { get; init; }

    public bool HasToolkitHealthReport { get; init; }

    /// <summary>Optional home ZIP/city from Kyra live tools settings (never auto-filled from system telemetry).</summary>
    public string? DefaultWeatherLocation { get; init; }
}
