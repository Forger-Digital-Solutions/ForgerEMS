using System;
using System.Collections.Generic;
using System.Linq;

namespace VentoyToolkitSetup.Wpf.Models;

public enum DriveValidationMode
{
    QuickSafeCheck = 0,
    SampledCapacityCheck = 1,
    FullFreeSpaceValidation = 2,
    DestructiveFullMediaValidation = 3
}

public enum DriveValidationStatus
{
    NotRun = 0,
    Running = 1,
    Passed = 2,
    PassedWithWarnings = 3,
    Failed = 4,
    Cancelled = 5,
    UnsafeTargetBlocked = 6,
    InsufficientFreeSpace = 7,
    CleanupWarning = 8
}

/// <summary>Port-mapping card status for a saved USB port (distinct from drive failure).</summary>
public enum DriveValidationPortStatus
{
    NotValidated = 0,
    ValidatedOk = 1,
    Warnings = 2,
    FailedValidation = 3
}

public enum DriveValidationPhase
{
    Preparing = 0,
    SafetyCheckingTarget = 1,
    PlanningSamples = 2,
    WritingSample = 3,
    Flushing = 4,
    ReadingSample = 5,
    Verifying = 6,
    CleaningUp = 7,
    Complete = 8,
    Failed = 9,
    Cancelled = 10
}

public sealed class DriveValidationOptions
{
    public DriveValidationMode Mode { get; init; } = DriveValidationMode.QuickSafeCheck;

    /// <summary>Required for destructive mode; must match <see cref="DriveValidationTargetSafety.DestructiveConfirmationPhrase"/>.</summary>
    public string? DestructiveConfirmationText { get; init; }

    /// <summary>Full free-space mode: fraction of free bytes to test (0.05–0.95).</summary>
    public double FullModeFreeSpaceFraction { get; init; } = 0.25;

    public int BlockSizeBytes { get; init; } = 256 * 1024;
}

public sealed class DriveValidationSample
{
    public int Index { get; init; }

    public string RelativePath { get; init; } = string.Empty;

    public long ByteLength { get; init; }

    public int Seed { get; init; }

    public string ExpectedSignatureHex { get; init; } = string.Empty;
}

public sealed record DriveValidationEvidence
{
    public int SamplesPlanned { get; init; }

    public int SamplesWritten { get; init; }

    public int SamplesVerified { get; init; }

    public long BytesWritten { get; init; }

    public long BytesVerified { get; init; }

    public double WriteSpeedMBps { get; init; }

    public double ReadSpeedMBps { get; init; }

    public int MismatchCount { get; init; }

    public int IoErrorCount { get; init; }

    public int SuspiciousAliasCount { get; init; }

    public string TempFolder { get; init; } = string.Empty;

    public string CleanupStatus { get; init; } = string.Empty;

    public IReadOnlyList<string> LeftoverTempPaths { get; init; } = Array.Empty<string>();

    public string TargetVolume { get; init; } = string.Empty;

    public string TargetDriveModel { get; init; } = string.Empty;

    public string TargetSerial { get; init; } = string.Empty;

    public string BusType { get; init; } = string.Empty;

    public string PortPath { get; init; } = string.Empty;

    /// <summary>Total reported bytes at validation time. Used to detect drive swap on the same letter.</summary>
    public long TargetTotalBytes { get; init; }

    /// <summary>Best-effort volume label snapshot at validation time.</summary>
    public string TargetLabel { get; init; } = string.Empty;

    /// <summary>Best-effort volume serial (GetVolumeInformationW); may be empty when unavailable.</summary>
    public string VolumeSerial { get; init; } = string.Empty;

    /// <summary>Composite identity fingerprint used by the validation cache so a different drive on the same letter is not trusted.</summary>
    public string IdentityFingerprint { get; init; } = string.Empty;

    /// <summary>Confidence of the cached identity (Strong = volume serial present; Partial = identifiers but no serial; Weak = root-path only).</summary>
    public string IdentityConfidence { get; init; } = string.Empty;

    /// <summary>Per-region map snapshot (empty for legacy/cached results without region tracking).</summary>
    public IReadOnlyList<DriveValidationRegion> Regions { get; init; } = Array.Empty<DriveValidationRegion>();

    public DriveValidationMapSummary MapSummary { get; init; } = new();

    /// <summary>Set when at least one region read MBps is significantly slower than the median — promotes Passed → PassedWithWarnings.</summary>
    public bool SpeedCollapseSuspected { get; init; }
}

public sealed class DriveValidationResult
{
    public Guid RunId { get; init; }

    public DriveValidationStatus Status { get; init; } = DriveValidationStatus.NotRun;

    public DriveValidationMode Mode { get; init; }

    public DriveValidationPhase Phase { get; init; } = DriveValidationPhase.Preparing;

    public string Summary { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public DriveValidationEvidence Evidence { get; init; } = new();

    public DateTimeOffset? StartedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public string TargetRootPath { get; init; } = string.Empty;

    public bool IsSuccessfulForUsbBuilder =>
        Status is DriveValidationStatus.Passed or DriveValidationStatus.PassedWithWarnings;

    public bool ShouldWarnUsbBuilder =>
        Status is DriveValidationStatus.Failed
            or DriveValidationStatus.CleanupWarning
            or DriveValidationStatus.InsufficientFreeSpace;

    public DriveValidationPortStatus ToPortStatus() =>
        Status switch
        {
            DriveValidationStatus.Passed => DriveValidationPortStatus.ValidatedOk,
            DriveValidationStatus.PassedWithWarnings or DriveValidationStatus.CleanupWarning =>
                DriveValidationPortStatus.Warnings,
            DriveValidationStatus.Failed => DriveValidationPortStatus.FailedValidation,
            _ => DriveValidationPortStatus.NotValidated
        };

    public static DriveValidationResult Blocked(
        DriveValidationStatus status,
        string summary,
        string detail,
        UsbTargetInfo? target = null,
        DriveValidationMode mode = DriveValidationMode.QuickSafeCheck) =>
        new()
        {
            RunId = Guid.NewGuid(),
            Status = status,
            Mode = mode,
            Phase = DriveValidationPhase.Failed,
            Summary = summary,
            Detail = detail,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            TargetRootPath = target?.RootPath ?? string.Empty,
            Evidence = new DriveValidationEvidence
            {
                TargetVolume = target?.RootPath ?? string.Empty,
                TargetDriveModel = target?.DeviceModel ?? string.Empty,
                BusType = target?.BusType ?? string.Empty
            }
        };
}

public sealed class DriveValidationProgress
{
    public DriveValidationPhase Phase { get; init; }

    public string Message { get; init; } = string.Empty;

    public int SampleIndex { get; init; }

    public int SampleCount { get; init; }

    public double ProgressFraction { get; init; }

    /// <summary>Optional region snapshot at this progress tick (region map UI). Null until the run starts.</summary>
    public DriveValidationMap? MapSnapshot { get; init; }

    /// <summary>Index of the region whose status just changed (or -1 for phase-only events).</summary>
    public int ChangedRegionIndex { get; init; } = -1;
}

public sealed class DriveValidationPlan
{
    public IReadOnlyList<DriveValidationSample> Samples { get; init; } = Array.Empty<DriveValidationSample>();

    public long ReservedBytes { get; init; }

    public string? BlockReason { get; init; }
}

/// <summary>
/// Per-region status surfaced to the future region-map tile UI. Region == one sample location in
/// the validation plan. Statuses progress NotTested → Planned → Writing → Flushing → Verifying →
/// (Passed | Warning | Mismatch | AliasSuspected | IoError | Cancelled). Each region keeps its own
/// evidence (timings, observed signature, error reason) so the result panel can describe exactly
/// which region misbehaved instead of only an aggregate count.
/// </summary>
public enum DriveValidationRegionStatus
{
    NotTested = 0,
    Planned = 1,
    Writing = 2,
    Flushing = 3,
    Verifying = 4,
    Passed = 5,
    Warning = 6,
    Mismatch = 7,
    AliasSuspected = 8,
    IoError = 9,
    Cancelled = 10
}

public enum DriveValidationRegionSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2
}

/// <summary>
/// Mutable region record. The validator service builds a list of these from the plan, transitions
/// statuses as work happens, and snapshots them into <see cref="DriveValidationEvidence.Regions"/>
/// when a terminal result is produced. The tile UI binds to the snapshot, not the live list.
/// </summary>
public sealed class DriveValidationRegion
{
    public int Index { get; init; }

    public long LogicalOffsetHint { get; init; }

    public long PlannedBytes { get; init; }

    public long BytesWritten { get; set; }

    public long BytesVerified { get; set; }

    public long WriteMs { get; set; }

    public long ReadMs { get; set; }

    public double WriteMBps { get; set; }

    public double ReadMBps { get; set; }

    public string ExpectedSignatureHash { get; init; } = string.Empty;

    public string ObservedSignatureHash { get; set; } = string.Empty;

    public DriveValidationRegionStatus Status { get; set; } = DriveValidationRegionStatus.Planned;

    public DriveValidationRegionSeverity Severity { get; set; } = DriveValidationRegionSeverity.Info;

    public string ErrorMessage { get; set; } = string.Empty;

    public string WarningReason { get; set; } = string.Empty;

    public DriveValidationRegion Snapshot() => new()
    {
        Index = Index,
        LogicalOffsetHint = LogicalOffsetHint,
        PlannedBytes = PlannedBytes,
        BytesWritten = BytesWritten,
        BytesVerified = BytesVerified,
        WriteMs = WriteMs,
        ReadMs = ReadMs,
        WriteMBps = WriteMBps,
        ReadMBps = ReadMBps,
        ExpectedSignatureHash = ExpectedSignatureHash,
        ObservedSignatureHash = ObservedSignatureHash,
        Status = Status,
        Severity = Severity,
        ErrorMessage = ErrorMessage,
        WarningReason = WarningReason
    };
}

public sealed class DriveValidationMap
{
    public IReadOnlyList<DriveValidationRegion> Regions { get; init; } = Array.Empty<DriveValidationRegion>();

    public DriveValidationMapSummary Summary { get; init; } = new();

    public static DriveValidationMap Snapshot(IReadOnlyList<DriveValidationRegion> live) =>
        new()
        {
            Regions = live.Select(r => r.Snapshot()).ToList(),
            Summary = DriveValidationMapSummary.FromRegions(live)
        };
}

public sealed class DriveValidationMapSummary
{
    public int Planned { get; init; }

    public int Tested { get; init; }

    public int Passed { get; init; }

    public int Warning { get; init; }

    public int Mismatch { get; init; }

    public int AliasSuspected { get; init; }

    public int IoError { get; init; }

    public int Cancelled { get; init; }

    public double FastestReadMBps { get; init; }

    public double SlowestReadMBps { get; init; }

    public static DriveValidationMapSummary FromRegions(IReadOnlyList<DriveValidationRegion> regions)
    {
        if (regions.Count == 0)
        {
            return new DriveValidationMapSummary();
        }

        var tested = 0;
        var passed = 0;
        var warning = 0;
        var mismatch = 0;
        var alias = 0;
        var ioErr = 0;
        var cancelled = 0;
        var fastest = 0.0;
        var slowest = double.MaxValue;
        var anySpeed = false;

        foreach (var r in regions)
        {
            switch (r.Status)
            {
                case DriveValidationRegionStatus.Passed: tested++; passed++; break;
                case DriveValidationRegionStatus.Warning: tested++; warning++; break;
                case DriveValidationRegionStatus.Mismatch: tested++; mismatch++; break;
                case DriveValidationRegionStatus.AliasSuspected: tested++; alias++; break;
                case DriveValidationRegionStatus.IoError: tested++; ioErr++; break;
                case DriveValidationRegionStatus.Cancelled: cancelled++; break;
            }

            if (r.ReadMBps > 0)
            {
                anySpeed = true;
                if (r.ReadMBps > fastest) fastest = r.ReadMBps;
                if (r.ReadMBps < slowest) slowest = r.ReadMBps;
            }
        }

        return new DriveValidationMapSummary
        {
            Planned = regions.Count,
            Tested = tested,
            Passed = passed,
            Warning = warning,
            Mismatch = mismatch,
            AliasSuspected = alias,
            IoError = ioErr,
            Cancelled = cancelled,
            FastestReadMBps = anySpeed ? fastest : 0,
            SlowestReadMBps = anySpeed ? slowest : 0
        };
    }
}
