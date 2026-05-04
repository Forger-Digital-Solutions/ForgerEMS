using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public enum UsbSpeedClassification
{
    Unknown = 0,
    Usb2 = 1,
    Usb3 = 2,
    UsbC = 3
}

public enum UsbPortRiskLevel
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

/// <summary>USB Builder target quality tier for hints and Kyra (no license gating yet).</summary>
public enum UsbBuilderQuality
{
    Unknown = 0,
    Ideal = 1,
    Good = 2,
    Slow = 3,
    Risky = 4
}

/// <summary>Measurement-derived class from sequential file I/O (distinct from WMI <see cref="UsbSpeedClassification"/>).</summary>
public enum UsbSpeedMeasurementClass
{
    Unknown = 0,
    Usb2 = 1,
    Usb3 = 2,
    UsbC = 3,
    Bottleneck = 4
}

/// <summary>Persisted USB speed sample for Intelligence, diagnostics, and machine profile.</summary>
public sealed class UsbIntelligenceBenchmarkResult
{
    public bool Succeeded { get; init; }

    public double WriteSpeedMBps { get; init; }

    public double ReadSpeedMBps { get; init; }

    public int DurationMs { get; init; }

    public int TestSizeMb { get; init; }

    public UsbSpeedMeasurementClass Classification { get; init; }

    public int ConfidenceScore { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    public string SummaryLine { get; init; } = string.Empty;

    public string DetailReason { get; init; } = string.Empty;

    public static UsbIntelligenceBenchmarkResult Failed(string message) =>
        new()
        {
            Succeeded = false,
            Classification = UsbSpeedMeasurementClass.Unknown,
            ConfidenceScore = 15,
            Timestamp = DateTimeOffset.UtcNow,
            SummaryLine = message,
            DetailReason = "Benchmark did not complete."
        };
}

public sealed class UsbKnownPortRecord
{
    public string StablePortKey { get; set; } = string.Empty;

    public string? UserLabel { get; set; }

    public UsbIntelligenceBenchmarkResult? LastBenchmark { get; set; }

    public DateTimeOffset? LastSeenUtc { get; set; }

    public int Confidence { get; set; }

    public string? LastMappingSuggestion { get; set; }

    public int MappingConfidenceScore { get; set; }
}

public sealed class UsbControllerInfo
{
    public string Name { get; init; } = string.Empty;

    /// <summary>Stable hashed controller identity for persistence (never raw PNP in JSON).</summary>
    public string ControllerKey { get; init; } = string.Empty;

    public string HubKey { get; init; } = string.Empty;

    public string FriendlyLocation { get; init; } = string.Empty;

    public UsbSpeedClassification InferredSpeed { get; init; }

    public string SpeedRationale { get; init; } = string.Empty;

    public DateTimeOffset? FirstSeenUtc { get; init; }

    public DateTimeOffset? LastSeenUtc { get; init; }

    public int SeenCount { get; init; }

    public int ConfidenceScore { get; init; }

    public string ConfidenceReason { get; init; } = string.Empty;
}

public sealed class UsbPortInfo
{
    public string PortLabel { get; init; } = string.Empty;

    public string StablePortKey { get; init; } = string.Empty;

    public string ControllerKey { get; init; } = string.Empty;

    public string HubKey { get; init; } = string.Empty;

    public string ParentDeviceIdHash { get; init; } = string.Empty;

    public string FriendlyLocation { get; init; } = string.Empty;

    public UsbSpeedClassification InferredSpeed { get; init; }

    public bool DeviceAttached { get; init; }

    public UsbPortRiskLevel Risk { get; init; }

    public DateTimeOffset? FirstSeenUtc { get; init; }

    public DateTimeOffset? LastSeenUtc { get; init; }

    public int SeenCount { get; init; }

    public int ConfidenceScore { get; init; }

    public string ConfidenceReason { get; init; } = string.Empty;
}

public sealed class UsbDeviceInfo
{
    public string FriendlyName { get; init; } = string.Empty;

    public string? DriveLetter { get; set; }

    public bool IsRemovableMassStorage { get; init; }

    public UsbSpeedClassification InferredSpeed { get; init; }

    /// <summary>Raw PNP — never written to disk or Kyra context.</summary>
    [JsonIgnore]
    public string? PnpDeviceId { get; init; }

    /// <summary>Win32_DiskDrive.DeviceID — excluded from public JSON.</summary>
    [JsonIgnore]
    public string? WmiDeviceId { get; init; }

    [JsonIgnore]
    public string? LinkedControllerName { get; set; }

    public string StableDeviceKey { get; set; } = string.Empty;

    public string ControllerKey { get; set; } = string.Empty;

    public string HubKey { get; set; } = string.Empty;

    public string ParentDeviceIdHash { get; set; } = string.Empty;

    public string DeviceInstanceIdHash { get; set; } = string.Empty;

    public string LocationPathHash { get; set; } = string.Empty;

    public string VolumeIdentityHash { get; set; } = string.Empty;

    public string FriendlyLocation { get; set; } = string.Empty;

    public DateTimeOffset? FirstSeenUtc { get; set; }

    public DateTimeOffset? LastSeenUtc { get; set; }

    public int SeenCount { get; set; }

    public int ConfidenceScore { get; set; }

    public string ConfidenceReason { get; set; } = string.Empty;

    public string StablePortKey { get; set; } = string.Empty;
}

public sealed class UsbBuilderRecommendation
{
    public string Summary { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public UsbPortRiskLevel Risk { get; init; }

    public UsbSpeedClassification Speed { get; init; }

    public UsbBuilderQuality Quality { get; init; }

    /// <summary>Single-line tier label for UI (e.g. "Quality: Ideal").</summary>
    public string ClassificationLine { get; init; } = string.Empty;

    public int ConfidenceScore { get; init; }

    public string ConfidenceReason { get; init; } = string.Empty;

    public UsbSpeedMeasurementClass? MeasuredClassification { get; init; }
}

public sealed class UsbTopologyDeviceChange
{
    public string DeviceSignatureShort { get; init; } = string.Empty;

    public string ChangeKind { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;
}

public sealed class UsbTopologyDiffResult
{
    public IReadOnlyList<string> AddedDevices { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RemovedDevices { get; init; } = Array.Empty<string>();

    public IReadOnlyList<UsbTopologyDeviceChange> ChangedDevices { get; init; } = Array.Empty<UsbTopologyDeviceChange>();

    public string SummaryLine { get; init; } = string.Empty;

    public string RecommendationLine { get; init; } = string.Empty;

    public int DiffConfidenceScore { get; init; } = 50;

    public string DiffConfidenceReason { get; init; } = string.Empty;
}

public sealed class UsbDiagnosticIssue
{
    public DiagnosticSeverityLevel Severity { get; init; }

    public string Message { get; init; } = string.Empty;
}

/// <summary>Embedded in usb-intelligence-latest.json for diagnostics merge.</summary>
public sealed class UsbDiagnosticsEmbeddedSection
{
    public string UsbSummaryLine { get; init; } = string.Empty;

    public string UsbRecommendationLine { get; init; } = string.Empty;

    public string UsbConfidence { get; init; } = string.Empty;

    public IReadOnlyList<UsbDiagnosticIssue> UsbIssues { get; init; } = Array.Empty<UsbDiagnosticIssue>();

    public bool UsbChangedSinceLastScan { get; init; }

    public int UsbProfileKnownPortsCount { get; init; }

    public DiagnosticSeverityLevel UsbOverallSeverity { get; init; }

    public UsbIntelligenceBenchmarkResult? LastBenchmark { get; init; }

    public int MappingConfidenceScore { get; init; }

    public string MappingConfidenceSummary { get; init; } = string.Empty;

    public int CombinedConfidenceScore { get; init; }

    public string CombinedConfidenceSummary { get; init; } = string.Empty;

    /// <summary>Human-readable risk for the current USB builder target.</summary>
    public string UsbCurrentTargetRiskSummary { get; init; } = string.Empty;

    /// <summary>Best ranked labeled port by measured write speed, if any.</summary>
    public string UsbBestKnownPortSummary { get; init; } = string.Empty;
}

public sealed class KyraUsbNarrative
{
    public string ShortAnswer { get; init; } = string.Empty;

    public string LikelyCause { get; init; } = string.Empty;

    public string NextStep { get; init; } = string.Empty;
}

public sealed class UsbTopologySnapshot
{
    public DateTimeOffset GeneratedUtc { get; init; }

    public IReadOnlyList<UsbControllerInfo> Controllers { get; init; } = Array.Empty<UsbControllerInfo>();

    public IReadOnlyList<UsbPortInfo> Ports { get; init; } = Array.Empty<UsbPortInfo>();

    public IReadOnlyList<UsbDeviceInfo> Devices { get; init; } = Array.Empty<UsbDeviceInfo>();

    public UsbBuilderRecommendation? SelectedTargetRecommendation { get; init; }

    public string SummaryLine { get; init; } = string.Empty;

    public UsbTopologyDiffResult? TopologyDiff { get; init; }

    public UsbDiagnosticsEmbeddedSection? UsbDiagnostics { get; init; }

    public KyraUsbNarrative? KyraUsbNarrative { get; init; }

    public string MachineProfileFingerprint { get; init; } = string.Empty;

    /// <summary>Latest measurement for the selected USB target (if any).</summary>
    public UsbIntelligenceBenchmarkResult? SelectedTargetBenchmark { get; init; }

    public string? SelectedTargetStablePortKey { get; init; }

    /// <summary>User-confirmed label for the current port (safe for Kyra JSON).</summary>
    public string? SelectedTargetPortUserLabel { get; init; }

    public int SelectedTargetMappingConfidence { get; init; }

    public int CombinedConfidenceScore { get; init; }

    public string CombinedConfidenceReason { get; init; } = string.Empty;
}

public sealed class UsbBuilderPreflightResult
{
    public bool ShouldWarn { get; init; }

    public string Message { get; init; } = string.Empty;

    public UsbSpeedClassification Speed { get; init; }

    public UsbPortRiskLevel Risk { get; init; }

    public UsbBuilderQuality Quality { get; init; }
}

/// <summary>Optional inputs when building a topology snapshot (previous file + machine profile).</summary>
public sealed class UsbTopologyBuildOptions
{
    public UsbTopologySnapshot? PreviousSnapshot { get; init; }

    public UsbMachineProfile? MachineProfile { get; init; }
}
