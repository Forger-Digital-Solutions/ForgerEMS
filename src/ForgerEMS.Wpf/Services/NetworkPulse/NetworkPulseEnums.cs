namespace VentoyToolkitSetup.Wpf.Services.NetworkPulse;

public enum NetworkPulseStatus
{
    Good,
    Slow,
    Unstable,
    Offline,
    Unknown,
    Testing,
    Paused
}

public enum NetworkPulseConfidence
{
    High,
    Medium,
    Low
}

/// <summary>How a headline metric should be interpreted in UI and tooltips.</summary>
public enum NetworkPulseMeasurementKind
{
    Measured,
    Estimated,
    Unavailable,
    Paused
}

public enum NetworkPulseConnectionKind
{
    Unknown,
    Ethernet,
    WiFi,
    Other
}

public enum NetworkPulseMode
{
    Light,
    Balanced,
    Full
}
