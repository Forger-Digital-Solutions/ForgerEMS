using VentoyToolkitSetup.Wpf.Services;

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

public sealed class CurrentDataToolStatus
{
    public string ToolName { get; init; } = string.Empty;

    public string Provider { get; init; } = string.Empty;

    public bool Configured { get; init; }

    public KyraProviderCredentialState KeyStatus { get; init; }

    public bool Implemented { get; init; }

    public string LastErrorSanitized { get; init; } = string.Empty;

    public bool RequiresNetwork { get; init; }

    public bool SupportsNoKey { get; init; }
}

/// <summary>Facts from the host about local reports (no secrets).</summary>
public readonly struct KyraToolHostFacts
{
    public bool HasSystemIntelligenceScan { get; init; }

    public bool HasToolkitHealthReport { get; init; }

    /// <summary>Optional home ZIP/city from Kyra live tools settings (never auto-filled from system telemetry).</summary>
    public string? DefaultWeatherLocation { get; init; }
}
