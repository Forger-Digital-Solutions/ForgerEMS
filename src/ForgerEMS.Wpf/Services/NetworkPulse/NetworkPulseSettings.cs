namespace VentoyToolkitSetup.Wpf.Services.NetworkPulse;

public sealed class NetworkPulseSettings
{
    public int SettingsSchemaVersion { get; set; } = NetworkPulseSettingsStore.CurrentSchemaVersion;

    public bool Enabled { get; set; } = true;

    public bool ShowInHeader { get; set; } = true;

    /// <summary>When true, periodic small download probes may run (still capped by mode).</summary>
    public bool AutoTestSpeedLightly { get; set; } = true;

    /// <summary>
    /// When true (default), a normal Network Pulse cycle attempts a small HTTPS POST upload sample
    /// alongside the download sample so the header can show a real Up Mbps. Probes remain
    /// bounded and capped; the user can opt out in Settings.
    /// </summary>
    public bool UploadProbesEnabled { get; set; } = true;

    /// <summary>Opt-in: pause probes when Windows reports a costed/metered connection. Off by default because
    /// probes are small and many connections are incorrectly flagged metered by Windows.</summary>
    public bool PauseOnMetered { get; set; }

    /// <summary>When true, pause probes while Windows Battery Saver is on (off by default to avoid surprise “Paused”).</summary>
    public bool PauseOnBatterySaver { get; set; }

    /// <summary>When true, pause probes during heavy in-app network activity (off by default; opt-in).</summary>
    public bool PauseDuringHostHeavyNetwork { get; set; }

    public NetworkPulseMode Mode { get; set; } = NetworkPulseMode.Balanced;

    /// <summary>Minimum seconds between full snapshot refresh cycles (latency, reachability).</summary>
    public int RefreshIntervalSeconds { get; set; }

    /// <summary>Minimum seconds between optional download speed probes.</summary>
    public int SpeedProbeCadenceSeconds { get; set; }
}
