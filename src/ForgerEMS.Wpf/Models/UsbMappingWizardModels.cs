using System;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Models;

public enum UsbMappingWizardStep
{
    Welcome = 0,
    SelectDevice = 1,
    ConfirmCurrentPort = 2,
    MoveUsb = 3,
    DetectChange = 4,
    LabelPort = 5,
    Done = 6
}

/// <summary>How aggressively to match before/after topology when saving a port label.</summary>
public enum UsbPortMappingSaveMode
{
    /// <summary>Prefer stable correlation + port-key change; then same-drive-letter / volume heuristics.</summary>
    TopologyInference = 0,

    /// <summary>Save label for the current port heuristic on the after snapshot for the selected drive (no port-move proof).</summary>
    CurrentPortForSelectedTarget = 1
}

public sealed class UsbMappingWizardDeviceOption
{
    public required string RootPath { get; init; }

    public required string DriveLetterDisplay { get; init; }

    public required string VolumeLabelDisplay { get; init; }

    public required string SizeDisplay { get; init; }

    public required string FileSystemDisplay { get; init; }

    public required string DetectedClassDisplay { get; init; }

    public required string LastBenchmarkDisplay { get; init; }

    public required string MappingLabelDisplay { get; init; }

    /// <summary>Internal selection only — not shown in wizard copy.</summary>
    public UsbTargetInfo Target { get; init; } = null!;

    public string SummaryLine =>
        $"{DriveLetterDisplay} · {VolumeLabelDisplay} · {SizeDisplay} · {FileSystemDisplay} · {DetectedClassDisplay}";
}

public sealed class UsbMappingWizardResult
{
    public bool Saved { get; init; }

    public string Label { get; init; } = string.Empty;

    public string ConfidenceTier { get; init; } = string.Empty;

    public string BenchmarkStatus { get; init; } = string.Empty;

    public string Recommendation { get; init; } = string.Empty;

    public UsbTargetInfo? MappedTarget { get; init; }
}

public static class UsbMappingWizardDeviceFilter
{
    public static bool IsEligibleMappingUsb(UsbTargetInfo target) =>
        target.IsRemovableMedia &&
        !target.IsSystemDrive &&
        !target.IsEfiSystemPartition &&
        !target.Label.Contains("VTOYEFI", StringComparison.OrdinalIgnoreCase) &&
        target is { IsUndersizedPartition: false };
}
