namespace VentoyToolkitSetup.Wpf.Models;

/// <summary>Serializable snapshot of wizard progress for tests and diagnostics (no raw USB IDs).</summary>
public sealed class UsbMappingWizardState
{
    public UsbMappingWizardStep Step { get; init; }

    public string? SelectedTargetRootPath { get; init; }

    public bool BeforeCaptured { get; init; }

    public bool AfterCaptured { get; init; }

    public bool PortChangeDetected { get; init; }

    public string ConfidenceTier { get; init; } = string.Empty;

    public string UserLabel { get; init; } = string.Empty;

    public string? ErrorMessage { get; init; }

    public string DetectionSummary { get; init; } = string.Empty;

    public bool CanContinue { get; init; }

    public bool CanRetry { get; init; }

    public bool CanSaveManualLabel { get; init; }

    public bool UserConfirmedUsbMoved { get; init; }

    public UsbPortMappingSaveMode PendingSaveMode { get; init; }
}
