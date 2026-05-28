#pragma warning disable CA1822 // Partial VM helpers can call private static members and stay grouped with Driver Hub state.
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private readonly List<DriverHubEntryView> _driverHubEntryViews = [];
    private string _driverHubSearchText = string.Empty;
    private string _selectedDriverHubFilter = "All";
    private string _driverHubRecommendationSummaryText = "Run System Intelligence to personalize recommendations.";
    private string _driverHubEmptyStateText = string.Empty;
    private string _driverHubStatusText = "Driver Hub is ready. Official links only; no automatic installs or firmware flashing.";

    public ObservableCollection<DriverHubEntryView> DriverHubVisibleEntries { get; } = [];

    public ObservableCollection<DriverHubEntryView> DriverHubRecommendedEntries { get; } = [];

    public ObservableCollection<string> DriverHubFilterChips { get; } = [];

    public RelayCommand<DriverHubEntryView> OpenDriverHubOfficialPageCommand { get; private set; } = null!;

    public RelayCommand<DriverHubEntryView> CopyDriverHubLinkCommand { get; private set; } = null!;

    public RelayCommand<DriverHubEntryView> AddDriverHubShortcutToUsbCommand { get; private set; } = null!;

    public RelayCommand<string> ApplyDriverHubFilterCommand { get; private set; } = null!;

    public string DriverHubSearchText
    {
        get => _driverHubSearchText;
        set
        {
            if (SetProperty(ref _driverHubSearchText, value ?? string.Empty))
            {
                ApplyDriverHubFilters();
            }
        }
    }

    public string SelectedDriverHubFilter
    {
        get => _selectedDriverHubFilter;
        private set => SetProperty(ref _selectedDriverHubFilter, value);
    }

    public string DriverHubRecommendationSummaryText
    {
        get => _driverHubRecommendationSummaryText;
        private set => SetProperty(ref _driverHubRecommendationSummaryText, value);
    }

    public string DriverHubEmptyStateText
    {
        get => _driverHubEmptyStateText;
        private set => SetProperty(ref _driverHubEmptyStateText, value);
    }

    public string DriverHubStatusText
    {
        get => _driverHubStatusText;
        private set => SetProperty(ref _driverHubStatusText, value);
    }

    public string DriverHubUsbTargetStatusText =>
        SelectedUsbTarget is null
            ? "Select a USB target first."
            : $"USB target: {SelectedUsbTarget.RootPath}";

    private void InitializeDriverHub()
    {
        DriverHubFilterChips.Clear();
        foreach (var chip in new[] { "All", "Recommended", "GPU", "OEM", "Network", "Chipset", "BIOS/Firmware", "Linux", "Windows" })
        {
            DriverHubFilterChips.Add(chip);
        }

        _driverHubEntryViews.Clear();
        _driverHubEntryViews.AddRange(DriverHubCatalog.All.Select(entry => new DriverHubEntryView(entry)));

        OpenDriverHubOfficialPageCommand = new RelayCommand<DriverHubEntryView>(OpenDriverHubOfficialPage);
        CopyDriverHubLinkCommand = new RelayCommand<DriverHubEntryView>(CopyDriverHubLink);
        AddDriverHubShortcutToUsbCommand = new RelayCommand<DriverHubEntryView>(
            AddDriverHubShortcutToUsb,
            entry => entry is not null && CanAddDriverHubShortcutToUsb());
        ApplyDriverHubFilterCommand = new RelayCommand<string>(ApplyDriverHubFilter);

        RefreshDriverHubRecommendations();
        ApplyDriverHubFilters();
    }

    private void RefreshDriverHubRecommendations()
    {
        var profile = TryLoadDriverHubSystemProfile();
        var linuxFilterRequested = string.Equals(SelectedDriverHubFilter, "Linux", StringComparison.OrdinalIgnoreCase);
        var recommendations = profile is null && !linuxFilterRequested
            ? Array.Empty<DriverHubRecommendation>()
            : DriverHubRecommendationEngine.Recommend(
                DriverHubCatalog.All,
                profile,
                linuxFilterRequested);
        var recommendationMap = recommendations.ToDictionary(
            item => item.Entry.Id,
            item => item,
            StringComparer.OrdinalIgnoreCase);

        DriverHubRecommendedEntries.Clear();
        foreach (var view in _driverHubEntryViews)
        {
            if (recommendationMap.TryGetValue(view.Id, out var recommendation))
            {
                view.IsRecommended = true;
                view.RecommendationStatusText = recommendation.StatusText;
                DriverHubRecommendedEntries.Add(view);
            }
            else
            {
                view.IsRecommended = false;
                view.RecommendationStatusText = string.Empty;
            }
        }

        DriverHubRecommendationSummaryText = BuildDriverHubRecommendationSummary(profile, recommendations);
        ApplyDriverHubFilters();
    }

    private static string BuildDriverHubRecommendationSummary(
        SystemProfile? profile,
        IReadOnlyList<DriverHubRecommendation> recommendations)
    {
        if (profile is null)
        {
            return "Run System Intelligence to personalize recommendations.";
        }

        if (recommendations.All(item => item.Reason == DriverHubRecommendationReason.UniversalStartingPoint))
        {
            return "No exact vendor/GPU match was found. Showing universal official starting points.";
        }

        return "Recommendations are based on detected vendor/GPU/CPU/OS data only; no driver version comparison is performed.";
    }

    private SystemProfile? TryLoadDriverHubSystemProfile()
    {
        try
        {
            var path = GetSystemIntelligenceJsonPath();
            if (!File.Exists(path))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return SystemProfileMapper.FromJson(document.RootElement);
        }
        catch
        {
            return null;
        }
    }

    private void ApplyDriverHubFilter(string? filter)
    {
        SelectedDriverHubFilter = string.IsNullOrWhiteSpace(filter) ? "All" : filter.Trim();
        RefreshDriverHubRecommendations();
    }

    private void ApplyDriverHubFilters()
    {
        var visible = DriverHubFilterEngine.Filter(_driverHubEntryViews, SelectedDriverHubFilter, DriverHubSearchText);

        DriverHubVisibleEntries.Clear();
        foreach (var entry in visible)
        {
            DriverHubVisibleEntries.Add(entry);
        }

        DriverHubEmptyStateText = DriverHubVisibleEntries.Count == 0
            ? "No Driver Hub cards match your filter."
            : string.Empty;
    }

    private void OpenDriverHubOfficialPage(DriverHubEntryView? view)
    {
        if (view is null)
        {
            return;
        }

        if (!DriverHubUrlSafety.IsSafeOfficialHttpUrl(view.OfficialUrl))
        {
            DriverHubStatusText = "Blocked unsafe or identifier-bearing URL.";
            AppendLog(new LogLine(DateTimeOffset.Now, $"Driver Hub blocked unsafe URL for: {view.Name}", LogSeverity.Warning));
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(view.OfficialUrl)
            {
                UseShellExecute = true
            });

            DriverHubStatusText = $"Opened official page: {view.Name}";
            AppendLog(new LogLine(DateTimeOffset.Now, $"Driver Hub opened official page: {view.Name}", LogSeverity.Info));
        }
        catch (Exception exception)
        {
            DriverHubStatusText = $"Could not open official page: {view.Name}";
            AppendLog(new LogLine(DateTimeOffset.Now, $"Driver Hub failed to open official page: {exception.Message}", LogSeverity.Warning));
        }
    }

    private void CopyDriverHubLink(DriverHubEntryView? view)
    {
        if (view is null)
        {
            return;
        }

        if (!DriverHubUrlSafety.IsSafeOfficialHttpUrl(view.OfficialUrl))
        {
            DriverHubStatusText = "Blocked unsafe or identifier-bearing URL.";
            AppendLog(new LogLine(DateTimeOffset.Now, $"Driver Hub blocked unsafe URL copy for: {view.Name}", LogSeverity.Warning));
            return;
        }

        try
        {
            Clipboard.SetText(view.OfficialUrl);
            DriverHubStatusText = $"Copied official link: {view.Name}";
            AppendLog(new LogLine(DateTimeOffset.Now, $"Driver Hub copied official link: {view.Name}", LogSeverity.Success));
        }
        catch (Exception exception)
        {
            DriverHubStatusText = $"Could not copy link: {view.Name}";
            AppendLog(new LogLine(DateTimeOffset.Now, $"Driver Hub failed to copy link: {exception.Message}", LogSeverity.Warning));
        }
    }

    private void AddDriverHubShortcutToUsb(DriverHubEntryView? view)
    {
        if (view is null)
        {
            return;
        }

        var result = DriverHubUsbShortcutService.CreateShortcut(SelectedUsbTarget?.RootPath, view.Entry);
        DriverHubStatusText = result.Succeeded
            ? $"{result.Message} {result.RelativePath}"
            : result.Message;

        AppendLog(new LogLine(
            DateTimeOffset.Now,
            result.Succeeded
                ? $"Driver Hub USB shortcut created: {view.Name} -> {result.RelativePath}"
                : $"Driver Hub USB shortcut skipped: {view.Name} -> {result.Message}",
            result.Succeeded ? LogSeverity.Success : LogSeverity.Warning));
    }

    private bool CanAddDriverHubShortcutToUsb() =>
        SelectedUsbTarget is { IsSelectable: true, ShouldBlockExecution: false };

    private void RaiseDriverHubCommandStates()
    {
        AddDriverHubShortcutToUsbCommand?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(DriverHubUsbTargetStatusText));
    }
}
