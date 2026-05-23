#pragma warning disable CA1305 // Locale-sensitive calls; text is diagnostic/UI output
using System;
using VentoyToolkitSetup.Wpf.Infrastructure;

namespace VentoyToolkitSetup.Wpf.Models;

public enum DriveValidatorWizardStep
{
    SelectTarget = 0,
    ChooseMode = 1,
    SafetyReview = 2,
    Running = 3,
    Results = 4
}

/// <summary>
/// One row in the wizard's target list. Wraps a <see cref="UsbTargetInfo"/> with display-ready
/// strings and a safety verdict so the wizard can show why a target is unsafe without re-running
/// the safety check from the view.
/// </summary>
public sealed class DriveValidatorWizardTargetOption : ObservableObject
{
    private string _lastValidationSummary = "—";

    public DriveValidatorWizardTargetOption(UsbTargetInfo target, bool isSafe, string safetyReason, string portLabel)
    {
        Target = target;
        IsSafe = isSafe;
        SafetyReason = safetyReason ?? string.Empty;
        PortLabel = string.IsNullOrWhiteSpace(portLabel) ? "unmapped" : portLabel;
    }

    public UsbTargetInfo Target { get; }

    public bool IsSafe { get; }

    public string SafetyReason { get; }

    public string PortLabel { get; }

    public string RootPath => Target.RootPath;

    public string LabelDisplay => Target.LabelDisplay;

    public string CapacityDisplay => Target.DisplayTotalBytes;

    public string FreeSpaceDisplay => Target.DisplayFreeBytes;

    public string FileSystemDisplay =>
        string.IsNullOrWhiteSpace(Target.FileSystem) ? "—" : Target.FileSystem;

    public string BusModelDisplay => $"{Target.BusTypeDisplay} · {Target.DeviceIdentityDisplay}";

    public string SafetyDisplay => IsSafe ? "Safe to validate" : "Blocked: " + SafetyReason;

    public string LastValidationSummary
    {
        get => _lastValidationSummary;
        set => SetProperty(ref _lastValidationSummary, value);
    }

    public string SummaryLine =>
        $"{RootPath} · {LabelDisplay} · {CapacityDisplay} · {(IsSafe ? "safe" : "blocked")}";
}

/// <summary>
/// Display descriptor for one Drive Validator mode in the wizard. The wizard binds an
/// <see cref="System.Collections.Generic.IReadOnlyList{T}"/> of these to a list; selecting one
/// drives mode resolution without leaking enum values into XAML.
/// </summary>
public sealed class DriveValidatorWizardModeOption
{
    public DriveValidatorWizardModeOption(
        DriveValidationMode mode,
        string title,
        string description,
        string heaviness,
        bool requiresConfirmation,
        bool isAvailable,
        string unavailableReason = "")
    {
        Mode = mode;
        Title = title;
        Description = description;
        Heaviness = heaviness;
        RequiresConfirmation = requiresConfirmation;
        IsAvailable = isAvailable;
        UnavailableReason = unavailableReason;
    }

    public DriveValidationMode Mode { get; }

    public string Title { get; }

    public string Description { get; }

    public string Heaviness { get; }

    public bool RequiresConfirmation { get; }

    public bool IsAvailable { get; }

    public string UnavailableReason { get; }

    public string HeaderLine => IsAvailable ? Title : $"{Title} — {UnavailableReason}";
}

/// <summary>
/// Live tile state shown on the Running step. Mirrors a <see cref="DriveValidationRegion"/> but
/// stays an INPC-observable, UI-friendly projection so the wizard can update tiles without
/// re-creating the whole collection on every progress tick.
/// </summary>
public sealed class DriveValidatorRegionTileViewModel : ObservableObject
{
    private DriveValidationRegionStatus _status;
    private DriveValidationRegionSeverity _severity;
    private long _bytesWritten;
    private long _bytesVerified;
    private double _writeMBps;
    private double _readMBps;
    private long _writeMs;
    private long _readMs;
    private string _errorMessage = string.Empty;
    private string _warningReason = string.Empty;
    private string _observedSignatureHash = string.Empty;

    public DriveValidatorRegionTileViewModel(int index, long logicalOffsetHint, long plannedBytes, string expectedSignatureHash)
    {
        Index = index;
        LogicalOffsetHint = logicalOffsetHint;
        PlannedBytes = plannedBytes;
        ExpectedSignatureHash = expectedSignatureHash ?? string.Empty;
        _status = DriveValidationRegionStatus.Planned;
    }

    public int Index { get; }

    public long LogicalOffsetHint { get; }

    public long PlannedBytes { get; }

    public string ExpectedSignatureHash { get; }

    public DriveValidationRegionStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusDisplay));
                OnPropertyChanged(nameof(StatusToken));
                OnPropertyChanged(nameof(ToolTipText));
            }
        }
    }

    public DriveValidationRegionSeverity Severity
    {
        get => _severity;
        set => SetProperty(ref _severity, value);
    }

    public long BytesWritten
    {
        get => _bytesWritten;
        set { if (SetProperty(ref _bytesWritten, value)) OnPropertyChanged(nameof(ToolTipText)); }
    }

    public long BytesVerified
    {
        get => _bytesVerified;
        set { if (SetProperty(ref _bytesVerified, value)) OnPropertyChanged(nameof(ToolTipText)); }
    }

    public double WriteMBps
    {
        get => _writeMBps;
        set { if (SetProperty(ref _writeMBps, value)) OnPropertyChanged(nameof(ToolTipText)); }
    }

    public double ReadMBps
    {
        get => _readMBps;
        set { if (SetProperty(ref _readMBps, value)) OnPropertyChanged(nameof(ToolTipText)); }
    }

    public long WriteMs
    {
        get => _writeMs;
        set { if (SetProperty(ref _writeMs, value)) OnPropertyChanged(nameof(ToolTipText)); }
    }

    public long ReadMs
    {
        get => _readMs;
        set { if (SetProperty(ref _readMs, value)) OnPropertyChanged(nameof(ToolTipText)); }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set { if (SetProperty(ref _errorMessage, value ?? string.Empty)) OnPropertyChanged(nameof(ToolTipText)); }
    }

    public string WarningReason
    {
        get => _warningReason;
        set { if (SetProperty(ref _warningReason, value ?? string.Empty)) OnPropertyChanged(nameof(ToolTipText)); }
    }

    public string ObservedSignatureHash
    {
        get => _observedSignatureHash;
        set { if (SetProperty(ref _observedSignatureHash, value ?? string.Empty)) OnPropertyChanged(nameof(ToolTipText)); }
    }

    public string StatusDisplay => Status switch
    {
        DriveValidationRegionStatus.NotTested => "Not tested",
        DriveValidationRegionStatus.Planned => "Planned",
        DriveValidationRegionStatus.Writing => "Writing",
        DriveValidationRegionStatus.Flushing => "Flushing",
        DriveValidationRegionStatus.Verifying => "Verifying",
        DriveValidationRegionStatus.Passed => "Passed",
        DriveValidationRegionStatus.Warning => "Warning",
        DriveValidationRegionStatus.Mismatch => "Mismatch",
        DriveValidationRegionStatus.AliasSuspected => "Alias suspected",
        DriveValidationRegionStatus.IoError => "I/O error",
        DriveValidationRegionStatus.Cancelled => "Cancelled",
        _ => Status.ToString()
    };

    /// <summary>Coarse status token used by XAML triggers to colorize the tile.</summary>
    public string StatusToken => Status switch
    {
        DriveValidationRegionStatus.NotTested or DriveValidationRegionStatus.Planned => "Planned",
        DriveValidationRegionStatus.Writing or DriveValidationRegionStatus.Flushing or DriveValidationRegionStatus.Verifying => "Active",
        DriveValidationRegionStatus.Passed => "Passed",
        DriveValidationRegionStatus.Warning => "Warning",
        DriveValidationRegionStatus.Mismatch or DriveValidationRegionStatus.AliasSuspected or DriveValidationRegionStatus.IoError => "Failed",
        DriveValidationRegionStatus.Cancelled => "Cancelled",
        _ => "Planned"
    };

    public string ToolTipText
    {
        get
        {
            var lines = new System.Text.StringBuilder();
            lines.AppendLine($"Region {Index + 1}");
            lines.AppendLine($"Status: {StatusDisplay}");
            lines.AppendLine($"Planned: {UsbTargetInfo.FormatBytes(PlannedBytes)}");
            if (LogicalOffsetHint > 0)
            {
                lines.AppendLine($"Logical position (approx): 0x{LogicalOffsetHint:x}");
            }
            if (BytesWritten > 0)
            {
                lines.AppendLine($"Written: {UsbTargetInfo.FormatBytes(BytesWritten)} in {WriteMs} ms ({WriteMBps:0.0} MB/s)");
            }
            if (BytesVerified > 0)
            {
                lines.AppendLine($"Verified: {UsbTargetInfo.FormatBytes(BytesVerified)} in {ReadMs} ms ({ReadMBps:0.0} MB/s)");
            }
            if (!string.IsNullOrWhiteSpace(WarningReason))
            {
                lines.AppendLine($"Warning: {WarningReason}");
            }
            if (!string.IsNullOrWhiteSpace(ErrorMessage))
            {
                lines.AppendLine($"Error: {ErrorMessage}");
            }
            if (!string.IsNullOrWhiteSpace(ExpectedSignatureHash))
            {
                var observed = string.IsNullOrWhiteSpace(ObservedSignatureHash) ? "—" : ObservedSignatureHash;
                lines.AppendLine($"Sig expected/observed: {ExpectedSignatureHash} / {observed}");
            }
            return lines.ToString().TrimEnd();
        }
    }

    public void ApplyRegion(DriveValidationRegion region)
    {
        if (region.Index != Index)
        {
            return;
        }

        BytesWritten = region.BytesWritten;
        BytesVerified = region.BytesVerified;
        WriteMs = region.WriteMs;
        ReadMs = region.ReadMs;
        WriteMBps = region.WriteMBps;
        ReadMBps = region.ReadMBps;
        ErrorMessage = region.ErrorMessage;
        WarningReason = region.WarningReason;
        ObservedSignatureHash = region.ObservedSignatureHash;
        Severity = region.Severity;
        Status = region.Status;
    }
}
