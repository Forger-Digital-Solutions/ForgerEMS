using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private bool _suppressItemRollup;

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
                ApplyCategoryToggleToItems(normalized);
                RefreshPackStatus();
                OnPropertyChanged(nameof(SelectionState));
                OnPropertyChanged(nameof(SelectionStateLabel));
                OnPropertyChanged(nameof(SelectedItemCount));
                OnPropertyChanged(nameof(VisibleItemCount));
                OnPropertyChanged(nameof(CategorySummaryChipText));
                OnPropertyChanged(nameof(SelectedItemSummaryText));
                OnPropertyChanged(nameof(ManagedDownloadsSummaryText));
                OnPropertyChanged(nameof(SelectedUsbFootprintSummaryText));
                OnPropertyChanged(nameof(ManualUserSuppliedSummaryText));
            }
        }
    }

    public ObservableCollection<UsbBuilderProfileItem> Items { get; } = [];

    public int SelectedItemCount => Items.Count(i => i.IsSelected);

    public int VisibleItemCount => Items.Count;

    public long SelectedItemsTotalBytes => Items.Where(i => i.IsSelected)
        .Sum(i => i.SpaceEstimate.TypicalBytes ?? 0);

    public long SelectedManagedDownloadBytes => Items.Sum(i => i.ManagedDownloadEstimateBytes);

    public long SelectedKnownUsbFootprintBytes => Items.Sum(i => i.KnownUsbFootprintEstimateBytes);

    public int SelectedUnknownUsbFootprintCount => Items.Count(i =>
        i.IsSelected &&
        !i.CountsAsManualOrUserSupplied &&
        i.SpaceEstimate.TypicalBytes is null or <= 0);

    public int SelectedManagedDownloadCount => Items.Count(i => i.IsSelected && i.CountsAsManagedDownload);

    public int SelectedUnknownManagedDownloadCount => Items.Count(i =>
        i.IsSelected &&
        i.CountsAsManagedDownload &&
        i.SpaceEstimate.TypicalBytes is null or <= 0);

    public int SelectedManualUserSuppliedCount => Items.Count(i => i.IsSelected && i.CountsAsManualOrUserSupplied);

    public bool HasVariableManualUserSuppliedSize => Items.Any(i =>
        i.IsSelected &&
        i.CountsAsManualOrUserSupplied &&
        i.SpaceEstimate.TypicalBytes is null);

    public string SelectedItemSummaryText =>
        Items.Count == 0
            ? (IsIncluded ? "Selected" : "Not selected")
            : $"Selected: {SelectedItemCount} / {VisibleItemCount} items";

    public string ManagedDownloadsSummaryText =>
        SelectedManagedDownloadCount == 0
            ? "Managed downloads: none"
            : SelectedManagedDownloadBytes > 0 && SelectedUnknownManagedDownloadCount > 0
                ? $"Managed downloads: {UsbTargetInfo.FormatBytes(SelectedManagedDownloadBytes)} + unknown"
                : SelectedManagedDownloadBytes > 0
                    ? $"Managed downloads: {UsbTargetInfo.FormatBytes(SelectedManagedDownloadBytes)}"
                    : "Managed downloads: size unknown";

    public string ManualUserSuppliedSummaryText
    {
        get
        {
            if (SelectedManualUserSuppliedCount == 0)
            {
                return "Manual/user-supplied: none";
            }

            var knownBytes = Items
                .Where(i => i.IsSelected && i.CountsAsManualOrUserSupplied)
                .Sum(i => i.SpaceEstimate.TypicalBytes ?? 0);

            return HasVariableManualUserSuppliedSize || knownBytes <= 0
                ? "Manual/user-supplied: varies"
                : $"Manual/user-supplied: {UsbTargetInfo.FormatBytes(knownBytes)}";
        }
    }

    public string SelectedUsbFootprintSummaryText
    {
        get
        {
            if (SelectedKnownUsbFootprintBytes > 0 &&
                (SelectedUnknownUsbFootprintCount > 0 || HasVariableManualUserSuppliedSize))
            {
                var suffix = SelectedUnknownUsbFootprintCount > 0 ? "unknown" : "manual varies";
                return $"USB space: {UsbTargetInfo.FormatBytes(SelectedKnownUsbFootprintBytes)} + {suffix}";
            }

            if (SelectedKnownUsbFootprintBytes > 0)
            {
                return $"USB space: {UsbTargetInfo.FormatBytes(SelectedKnownUsbFootprintBytes)}";
            }

            if (SelectedUnknownUsbFootprintCount > 0)
            {
                return "USB space: size unknown";
            }

            if (HasVariableManualUserSuppliedSize)
            {
                return "USB space: manual varies";
            }

            return "USB space: near zero";
        }
    }

    public long ProjectedNewDownloadBytes => Items.Sum(i => i.ProjectedNewBytes);

    public int WarningCount => Items.Count(i =>
        i.IsSelected && (i.LargePayload || i.RequiresUserSuppliedMedia || i.VendorPortalOnly || !i.ExistsOnUsb && i.Kind == UsbBuilderProfileItemKind.ManualMediaFolder));

    public UsbBuilderProfileCategorySelectionState SelectionState
    {
        get
        {
            if (Items.Count == 0)
            {
                return IsIncluded ? UsbBuilderProfileCategorySelectionState.Recommended : UsbBuilderProfileCategorySelectionState.None;
            }

            var selectedToggleable = Items.Where(i => i.CanToggle && i.IsSelected).ToList();
            var toggleable = Items.Where(i => i.CanToggle).ToList();
            var allSelected = toggleable.Count > 0 && selectedToggleable.Count == toggleable.Count;
            var noneSelected = selectedToggleable.Count == 0;

            if (allSelected)
            {
                return UsbBuilderProfileCategorySelectionState.Full;
            }

            if (noneSelected)
            {
                return UsbBuilderProfileCategorySelectionState.None;
            }

            var recommended = toggleable.Where(i => i.Tier == UsbBuilderProfileItemTier.Recommended).ToHashSet();
            var selected = selectedToggleable.ToHashSet();
            if (recommended.Count > 0 && selected.SetEquals(recommended))
            {
                return UsbBuilderProfileCategorySelectionState.Recommended;
            }

            return UsbBuilderProfileCategorySelectionState.Partial;
        }
    }

    public string SelectionStateLabel => SelectionState switch
    {
        UsbBuilderProfileCategorySelectionState.None => "None selected",
        UsbBuilderProfileCategorySelectionState.Partial => "Partial",
        UsbBuilderProfileCategorySelectionState.Recommended => "Recommended",
        UsbBuilderProfileCategorySelectionState.Full => "Full category",
        _ => "—"
    };

    public string CategorySummaryChipText
    {
        get
        {
            if (Items.Count == 0)
            {
                return IsIncluded ? "Selected" : "Not selected";
            }

            return $"{SelectedItemCount} of {Items.Count} selected";
        }
    }

    public void LoadItems(IEnumerable<UsbBuilderProfileItem> items, ISet<string>? preselectedItemIds = null)
    {
        _suppressItemRollup = true;
        try
        {
            foreach (var existing in Items)
            {
                existing.PropertyChanged -= OnItemPropertyChanged;
            }

            Items.Clear();
            foreach (var item in items)
            {
                if (preselectedItemIds is not null)
                {
                    item.IsSelected = item.Tier == UsbBuilderProfileItemTier.Required ||
                                      preselectedItemIds.Contains(item.Id);
                }
                else
                {
                    item.IsSelected = item.Tier != UsbBuilderProfileItemTier.Optional;
                }

                item.PropertyChanged += OnItemPropertyChanged;
                Items.Add(item);
            }
        }
        finally
        {
            _suppressItemRollup = false;
        }

        RollupFromItems();
    }

    public void ApplyItemSelection(Func<UsbBuilderProfileItem, bool> predicate)
    {
        _suppressItemRollup = true;
        try
        {
            foreach (var item in Items)
            {
                item.IsSelected = item.Tier == UsbBuilderProfileItemTier.Required || predicate(item);
            }
        }
        finally
        {
            _suppressItemRollup = false;
        }

        RollupFromItems();
    }

    public IEnumerable<string> GetSelectedItemIds() =>
        Items.Where(i => i.IsSelected).Select(i => i.Id);

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressItemRollup)
        {
            return;
        }

        if (!string.Equals(e.PropertyName, nameof(UsbBuilderProfileItem.IsSelected), StringComparison.Ordinal))
        {
            return;
        }

        RollupFromItems();
    }

    private void RollupFromItems()
    {
        if (Items.Count > 0)
        {
            // Category is considered "included" if any toggleable item is selected
            // OR if it has required-tier items. Required-only categories therefore
            // stay included regardless of optional selections.
            var hasSelected = Items.Any(i => i.IsSelected);
            var changed = false;
            var normalized = IsRequired || hasSelected;
            if (_isIncluded != normalized)
            {
                _isIncluded = normalized;
                changed = true;
            }

            if (changed)
            {
                OnPropertyChanged(nameof(IsIncluded));
                RefreshPackStatus();
            }
        }

        OnPropertyChanged(nameof(SelectionState));
        OnPropertyChanged(nameof(SelectionStateLabel));
        OnPropertyChanged(nameof(SelectedItemCount));
        OnPropertyChanged(nameof(VisibleItemCount));
        OnPropertyChanged(nameof(CategorySummaryChipText));
        OnPropertyChanged(nameof(SelectedItemsTotalBytes));
        OnPropertyChanged(nameof(SelectedManagedDownloadBytes));
        OnPropertyChanged(nameof(SelectedKnownUsbFootprintBytes));
        OnPropertyChanged(nameof(SelectedUnknownUsbFootprintCount));
        OnPropertyChanged(nameof(SelectedManagedDownloadCount));
        OnPropertyChanged(nameof(SelectedUnknownManagedDownloadCount));
        OnPropertyChanged(nameof(SelectedManualUserSuppliedCount));
        OnPropertyChanged(nameof(HasVariableManualUserSuppliedSize));
        OnPropertyChanged(nameof(SelectedItemSummaryText));
        OnPropertyChanged(nameof(ManagedDownloadsSummaryText));
        OnPropertyChanged(nameof(SelectedUsbFootprintSummaryText));
        OnPropertyChanged(nameof(ManualUserSuppliedSummaryText));
        OnPropertyChanged(nameof(ProjectedNewDownloadBytes));
        OnPropertyChanged(nameof(WarningCount));
    }

    private void ApplyCategoryToggleToItems(bool included)
    {
        if (_suppressItemRollup || Items.Count == 0)
        {
            return;
        }

        var shouldSeedRecommended = included && !Items.Any(i => i.IsSelected);
        if (included && !shouldSeedRecommended)
        {
            return;
        }

        _suppressItemRollup = true;
        try
        {
            foreach (var item in Items)
            {
                item.IsSelected = included
                    ? item.Tier != UsbBuilderProfileItemTier.Optional
                    : item.Tier == UsbBuilderProfileItemTier.Required;
            }
        }
        finally
        {
            _suppressItemRollup = false;
        }

        RollupFromItems();
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
