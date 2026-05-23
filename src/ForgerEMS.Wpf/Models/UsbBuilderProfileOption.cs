using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Models;

public sealed class UsbBuilderProfileOption : ObservableObject
{
    private bool _isIncluded;
    private UsbBuilderProfilePackStatus _packStatus;
    private long _detectedBytes;
    private int _detectedFileCount;
    private string? _mediaScanNote;

    public string CategoryId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string ShortDescription { get; init; } = string.Empty;

    public string Platform { get; init; } = string.Empty;

    public bool IsRequired { get; init; }

    public bool DefaultIncluded { get; init; }

    public bool RequiresManualMedia { get; init; }

    public UsbBuilderPackDownloadMode DownloadMode { get; init; }

    public UsbBuilderProfileSpaceEstimate SpaceEstimate { get; init; } = UsbBuilderProfileSpaceEstimate.UserSupplied("varies");

    public string ManualMediaExplanation { get; init; } = string.Empty;

    public bool CanToggle => !IsRequired;

    public UsbBuilderProfilePackStatus PackStatus
    {
        get => _packStatus;
        private set
        {
            if (SetProperty(ref _packStatus, value))
            {
                OnPropertyChanged(nameof(StatusChipText));
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public long DetectedBytes
    {
        get => _detectedBytes;
        private set
        {
            if (SetProperty(ref _detectedBytes, value))
            {
                RefreshDerivedChips();
            }
        }
    }

    public int DetectedFileCount
    {
        get => _detectedFileCount;
        private set
        {
            if (SetProperty(ref _detectedFileCount, value))
            {
                RefreshDerivedChips();
            }
        }
    }

    public string? MediaScanNote
    {
        get => _mediaScanNote;
        private set => SetProperty(ref _mediaScanNote, value);
    }

    public bool IsIncluded
    {
        get => IsRequired || _isIncluded;
        set
        {
            var normalized = IsRequired || value;
            if (SetProperty(ref _isIncluded, normalized))
            {
                RefreshPackStatus();
            }
        }
    }

    public string StatusChipText => UsbBuilderProfileStatusResolver.ToDisplayLabel(PackStatus);

    public string StatusText => StatusChipText;

    public string SpaceChipText =>
        UsbBuilderProfileEstimateCalculator.FormatSpaceLine(SpaceEstimate, DetectedBytes > 0 ? DetectedBytes : null);

    public string AcquisitionChipText => UsbBuilderProfileStatusResolver.ToAcquisitionChip(DownloadMode);

    public string DetectedMediaChipText =>
        DetectedBytes > 0
            ? $"On USB: {UsbTargetInfo.FormatBytes(DetectedBytes)} ({DetectedFileCount} files)"
            : string.Empty;

    public bool HasDetectedMediaChip => DetectedBytes > 0;

    public string DetailTooltipText =>
        $"{ManualMediaExplanation}{Environment.NewLine}{Environment.NewLine}Acquisition: {AcquisitionChipText}." +
        (string.IsNullOrWhiteSpace(MediaScanNote) ? string.Empty : $"{Environment.NewLine}Scan: {MediaScanNote}");

    public static UsbBuilderProfileOption FromDefinition(UsbBuilderProfileCategoryDefinition definition, bool included) =>
        new()
        {
            CategoryId = definition.CategoryId,
            DisplayName = definition.DisplayName,
            ShortDescription = definition.ShortDescription,
            Platform = definition.Platform,
            IsRequired = definition.IsRequired,
            DefaultIncluded = definition.DefaultIncluded,
            RequiresManualMedia = definition.RequiresManualMedia,
            DownloadMode = definition.DownloadMode,
            SpaceEstimate = definition.SpaceEstimate,
            ManualMediaExplanation = definition.ManualMediaExplanation,
            _isIncluded = definition.IsRequired || included
        };

    public void ApplyMediaScan(UsbBuilderProfileMediaScanResult? result)
    {
        if (result is null)
        {
            DetectedBytes = 0;
            DetectedFileCount = 0;
            MediaScanNote = null;
            RefreshPackStatus();
            return;
        }

        DetectedBytes = result.TotalBytes;
        DetectedFileCount = result.FileCount;
        MediaScanNote = result.Note;
        RefreshPackStatus();
    }

    public void RefreshPackStatus()
    {
        if (!UsbBuilderProfileCatalog.TryGet(CategoryId, out var definition))
        {
            return;
        }

        PackStatus = UsbBuilderProfileStatusResolver.Resolve(definition, IsIncluded, DetectedBytes, DetectedFileCount);
        RefreshDerivedChips();
    }

    private void RefreshDerivedChips()
    {
        OnPropertyChanged(nameof(SpaceChipText));
        OnPropertyChanged(nameof(DetectedMediaChipText));
        OnPropertyChanged(nameof(HasDetectedMediaChip));
        OnPropertyChanged(nameof(DetailTooltipText));
    }
}
