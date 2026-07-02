using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.ViewModels;

// Phase 1 modal Category Builder. Operates on a private clone of the parent
// option's items so Cancel can discard changes without touching the live
// MainViewModel selection. Apply commits the working set back to the parent
// option and raises the CloseRequested event.
public sealed class CategoryBuilderViewModel : ObservableObject
{
    private readonly UsbBuilderProfileOption _option;
    private string _filterText = string.Empty;

    public CategoryBuilderViewModel(UsbBuilderProfileOption option)
    {
        _option = option ?? throw new ArgumentNullException(nameof(option));

        // Clone working set so Cancel restores cleanly.
        foreach (var src in option.Items)
        {
            var clone = new UsbBuilderProfileItem
            {
                Id = src.Id,
                CategoryId = src.CategoryId,
                DisplayName = src.DisplayName,
                Subcategory = src.Subcategory,
                ShortDescription = src.ShortDescription,
                Source = src.Source,
                Kind = src.Kind,
                Tier = src.Tier,
                SpaceEstimate = src.SpaceEstimate,
                ManifestEntryName = src.ManifestEntryName,
                UsbRelativePath = src.UsbRelativePath,
                Notes = src.Notes,
                RequiresUserSuppliedMedia = src.RequiresUserSuppliedMedia,
                VendorPortalOnly = src.VendorPortalOnly,
                LargePayload = src.LargePayload
            };
            clone.DetectedBytes = src.DetectedBytes;
            clone.ExistsOnUsb = src.ExistsOnUsb;
            clone.IsSelected = src.IsSelected;
            clone.PropertyChanged += OnItemChanged;
            WorkingItems.Add(clone);
        }

        RefreshFilteredView();

        SelectRecommendedCommand = new RelayCommand(SelectRecommended);
        SelectAllCommand = new RelayCommand(SelectAll);
        SelectAllManagedDownloadsCommand = SelectAllCommand;
        SelectDocsShortcutsOnlyCommand = new RelayCommand(SelectDocsShortcutsOnly);
        ClearOptionalCommand = new RelayCommand(ClearOptional);
        ClearCategoryCommand = ClearOptionalCommand;
        ApplyCommand = new RelayCommand(() => RaiseClose(accepted: true));
        CancelCommand = new RelayCommand(() => RaiseClose(accepted: false));
    }

    public string CategoryHeader => _option.DisplayName;

    public string WindowTitle => $"{CategoryHeader} item picker";

    public string CategoryPurpose => _option.ShortDescription;

    public ObservableCollection<UsbBuilderProfileItem> WorkingItems { get; } = [];

    public ObservableCollection<UsbBuilderProfileItem> FilteredItems { get; } = [];

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
            {
                RefreshFilteredView();
            }
        }
    }

    public int SelectedCount => WorkingItems.Count(i => i.IsSelected);

    public int TotalCount => WorkingItems.Count;

    public string SelectionSummary => $"{SelectedCount} of {TotalCount} selected";

    public string EstimatedSelectedSize => EstimatedSelectedDownloadSize;

    public string EstimatedSelectedDownloadSize =>
        FormatSelectedManagedDownloads(WorkingItems);

    public string EstimatedSelectedUsbSpace =>
        FormatSelectedUsbSpace(WorkingItems);

    public string AlreadyOnUsbSummary =>
        UsbTargetInfo.FormatBytes(WorkingItems.Where(i => i.ExistsOnUsb).Sum(i => i.DetectedBytes));

    public string NewDownloadSummary =>
        UsbTargetInfo.FormatBytes(WorkingItems.Sum(i => i.ProjectedNewBytes));

    public RelayCommand SelectRecommendedCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectAllManagedDownloadsCommand { get; }
    public RelayCommand SelectDocsShortcutsOnlyCommand { get; }
    public RelayCommand ClearOptionalCommand { get; }
    public RelayCommand ClearCategoryCommand { get; }
    public RelayCommand ApplyCommand { get; }
    public RelayCommand CancelCommand { get; }

    public event EventHandler<bool>? CloseRequested;

    public IReadOnlyList<string> GetSelectedItemIds() =>
        WorkingItems.Where(i => i.IsSelected).Select(i => i.Id).ToList();

    public void CommitTo(UsbBuilderProfileOption option)
    {
        var selectedIds = new HashSet<string>(GetSelectedItemIds(), StringComparer.OrdinalIgnoreCase);
        option.ApplyItemSelection(i => selectedIds.Contains(i.Id));
    }

    private void SelectRecommended() =>
        SetSelectionByPredicate(i => i.Tier == UsbBuilderProfileItemTier.Required || i.Tier == UsbBuilderProfileItemTier.Recommended);

    private void SelectAll() =>
        SetSelectionByPredicate(_ => true);

    private void SelectDocsShortcutsOnly() =>
        SetSelectionByPredicate(i =>
            i.Tier == UsbBuilderProfileItemTier.Required ||
            i.Kind == UsbBuilderProfileItemKind.HtmlGuide ||
            i.Kind == UsbBuilderProfileItemKind.OfficialPage ||
            i.Kind == UsbBuilderProfileItemKind.Shortcut ||
            i.Kind == UsbBuilderProfileItemKind.VendorLink);

    private void ClearOptional() =>
        SetSelectionByPredicate(i => i.Tier == UsbBuilderProfileItemTier.Required);

    private void SetSelectionByPredicate(Func<UsbBuilderProfileItem, bool> predicate)
    {
        foreach (var item in WorkingItems)
        {
            item.IsSelected = item.Tier == UsbBuilderProfileItemTier.Required || predicate(item);
        }
    }

    private void RefreshFilteredView()
    {
        FilteredItems.Clear();
        var query = (_filterText ?? string.Empty).Trim();
        foreach (var item in WorkingItems)
        {
            if (Matches(item, query))
            {
                FilteredItems.Add(item);
            }
        }

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(EstimatedSelectedSize));
        OnPropertyChanged(nameof(EstimatedSelectedDownloadSize));
        OnPropertyChanged(nameof(EstimatedSelectedUsbSpace));
        OnPropertyChanged(nameof(NewDownloadSummary));
    }

    private static bool Matches(UsbBuilderProfileItem item, string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return true;
        }

        return item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               item.Subcategory.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               item.KindLabel.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               item.Notes.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(UsbBuilderProfileItem.IsSelected), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(SelectionSummary));
            OnPropertyChanged(nameof(EstimatedSelectedSize));
            OnPropertyChanged(nameof(EstimatedSelectedDownloadSize));
            OnPropertyChanged(nameof(EstimatedSelectedUsbSpace));
            OnPropertyChanged(nameof(NewDownloadSummary));
        }
    }

    private static string FormatSelectedManagedDownloads(IEnumerable<UsbBuilderProfileItem> items)
    {
        var selected = items.Where(i => i.IsSelected && i.CountsAsManagedDownload).ToList();
        if (selected.Count == 0)
        {
            return "none";
        }

        var knownBytes = selected.Sum(i => i.SpaceEstimate.TypicalBytes ?? 0);
        var unknownCount = selected.Count(i => i.SpaceEstimate.TypicalBytes is null or <= 0);
        if (knownBytes > 0 && unknownCount > 0)
        {
            return $"{UsbTargetInfo.FormatBytes(knownBytes)} + unknown";
        }

        return knownBytes > 0
            ? UsbTargetInfo.FormatBytes(knownBytes)
            : "size unknown";
    }

    private static string FormatSelectedUsbSpace(IEnumerable<UsbBuilderProfileItem> items)
    {
        var selected = items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return "near zero";
        }

        var knownBytes = selected
            .Where(i => !i.CountsAsManualOrUserSupplied)
            .Sum(i => i.SpaceEstimate.TypicalBytes ?? 0);
        var unknownCount = selected.Count(i =>
            !i.CountsAsManualOrUserSupplied &&
            i.SpaceEstimate.TypicalBytes is null or <= 0);
        var hasVariableManual = selected.Any(i =>
            i.CountsAsManualOrUserSupplied &&
            i.SpaceEstimate.TypicalBytes is null);

        if (knownBytes > 0 && (unknownCount > 0 || hasVariableManual))
        {
            var suffix = unknownCount > 0 ? "unknown" : "manual varies";
            return $"{UsbTargetInfo.FormatBytes(knownBytes)} + {suffix}";
        }

        if (knownBytes > 0)
        {
            return UsbTargetInfo.FormatBytes(knownBytes);
        }

        if (unknownCount > 0)
        {
            return "size unknown";
        }

        return hasVariableManual ? "manual varies" : "near zero";
    }

    private void RaiseClose(bool accepted) => CloseRequested?.Invoke(this, accepted);
}
