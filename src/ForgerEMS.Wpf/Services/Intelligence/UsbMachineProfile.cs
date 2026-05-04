using System;
using System.Collections.Generic;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

/// <summary>Placeholder feature flags for future Pro USB mapping (no enforcement).</summary>
public sealed class UsbProFeatureFlags
{
    public bool EnableGuidedMappingPro { get; set; }

    public bool EnablePortLevelMappingPro { get; set; }
}

public sealed class UsbKnownDeviceRecord
{
    public DateTimeOffset FirstSeenUtc { get; set; }

    public DateTimeOffset LastSeenUtc { get; set; }

    public int SeenCount { get; set; }
}

/// <summary>Local machine USB learning store (hashed identities only).</summary>
public sealed class UsbMachineProfile
{
    public string MachineFingerprintHash { get; set; } = string.Empty;

    public List<string> KnownControllerKeys { get; set; } = [];

    public List<string> KnownStablePortKeys { get; set; } = [];

    public List<UsbKnownPortRecord> KnownPorts { get; set; } = [];

    /// <summary>Benchmarks keyed by drive letter (e.g. "E") until merged into <see cref="KnownPorts"/> on next topology pass.</summary>
    public Dictionary<string, UsbIntelligenceBenchmarkResult> PendingBenchmarkByDriveLetter { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, UsbKnownDeviceRecord> KnownDevicesByStableKey { get; set; } = new(StringComparer.Ordinal);

    public string? UserLabelsPlaceholder { get; set; }

    public string? BestKnownBuilderPortPlaceholder { get; set; }

    public string? LastBenchmarkPlaceholder { get; set; }

    public DateTimeOffset LastUpdatedUtc { get; set; }

    public UsbProFeatureFlags ProFeatureFlags { get; set; } = new();
}
