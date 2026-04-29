using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private static readonly Brush ReadyBackground = new SolidColorBrush(Color.FromRgb(232, 250, 239));
    private static readonly Brush ReadyBorder = new SolidColorBrush(Color.FromRgb(134, 239, 172));
    private static readonly Brush ReadyForeground = new SolidColorBrush(Color.FromRgb(22, 101, 52));
    private static readonly Brush RunningBackground = new SolidColorBrush(Color.FromRgb(224, 247, 255));
    private static readonly Brush RunningBorder = new SolidColorBrush(Color.FromRgb(103, 183, 232));
    private static readonly Brush RunningForeground = new SolidColorBrush(Color.FromRgb(12, 74, 110));
    private static readonly Brush WarningBackground = new SolidColorBrush(Color.FromRgb(255, 247, 237));
    private static readonly Brush WarningBorder = new SolidColorBrush(Color.FromRgb(251, 191, 36));
    private static readonly Brush WarningForeground = new SolidColorBrush(Color.FromRgb(146, 64, 14));
    private static readonly Brush ErrorBackground = new SolidColorBrush(Color.FromRgb(254, 242, 242));
    private static readonly Brush ErrorBorder = new SolidColorBrush(Color.FromRgb(248, 113, 113));
    private static readonly Brush ErrorForeground = new SolidColorBrush(Color.FromRgb(153, 27, 27));

    private readonly IBackendDiscoveryService _backendDiscoveryService;
    private readonly IPowerShellRunnerService _powerShellRunnerService;
    private readonly IUsbDetectionService _usbDetectionService;
    private readonly IManagedDownloadSummaryService _managedDownloadSummaryService;
    private readonly IScriptStatusParser _scriptStatusParser;
    private readonly IUserPromptService _userPromptService;
    private readonly IVentoyIntegrationService _ventoyIntegrationService;
    private readonly IAppRuntimeService _appRuntimeService;
    private readonly IUsbBenchmarkService _usbBenchmarkService;
    private readonly ICopilotService _copilotService;
    private readonly ICopilotProviderRegistry _copilotProviderRegistry;
    private readonly Dictionary<string, UsbBenchmarkResult> _benchmarkResultsByRoot = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _benchmarksInProgress = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _benchmarkCachePath;
    private readonly string _copilotConfigPath;
    private CancellationTokenSource? _usbMonitorCancellation;
    private CancellationTokenSource? _copilotGenerationCancellation;
    private CopilotSettings _copilotSettings = new();

    private BackendContext _backendContext = BackendContext.Unavailable("Backend discovery has not run yet.");
    private UsbTargetInfo? _selectedUsbTarget;
    private bool _isBusy;
    private bool _initialized;
    private bool _usbMonitorStarted;
    private bool _autoScrollLogs = true;
    private bool _suppressSelectionRefresh;
    private bool _refreshingUsbTargets;
    private int _ventoyStatusRequestId;
    private int _benchmarkRequestId;
    private string _knownUsbSignature = string.Empty;
    private string _usbOwnerName = string.Empty;
    private string _statusText = "Starting up";
    private string _statusDetail = "Discovering the backend and checking for likely USB targets.";
    private string _currentTaskState = "WORKING";
    private string _currentTaskText = "Verifying backend";
    private double _currentProgressValue;
    private bool _isProgressIndeterminate;
    private Visibility _progressVisibility = Visibility.Collapsed;
    private Brush _statusBackground = RunningBackground;
    private Brush _statusBorderBrush = RunningBorder;
    private Brush _statusForeground = RunningForeground;
    private string _lastCommandText = "No command has been run yet.";
    private string _managedSummaryText = "No managed-download summary has been loaded yet.";
    private string _managedSummaryPathText = "Summary source: not detected";
    private string _managedSummaryUpdatedText = "Updated: n/a";
    private string _managedSummaryStatusText = "No snapshot";
    private string _logsText = string.Empty;
    private string _recentLogsText = "No log output yet.";
    private string _selectedLogLevelFilter = "All";
    private Brush _managedSummaryStatusBackground = WarningBackground;
    private Brush _managedSummaryStatusBorderBrush = WarningBorder;
    private Brush _managedSummaryStatusForeground = WarningForeground;
    private string _targetWarningText = "Select a USB target to review safety notes.";
    private Brush _targetWarningBackground = RunningBackground;
    private Brush _targetWarningBorderBrush = RunningBorder;
    private Brush _targetWarningForeground = RunningForeground;
    private string _actionWarningText = "Setup USB, Update USB, and Ventoy actions stay disabled until a valid USB target is selected.";
    private string _ventoyStatusText = "Select a USB target";
    private string _ventoyDetailText = "Choose a USB target to inspect whether Ventoy is already present on the device.";
    private string _ventoyPackageText = "Official Ventoy package source not loaded yet.";
    private Brush _ventoyStatusBackground = RunningBackground;
    private Brush _ventoyStatusBorderBrush = RunningBorder;
    private Brush _ventoyStatusForeground = RunningForeground;
    private string _systemIntelligenceStatusText = "Not scanned";
    private string _systemIntelligenceSummaryText = "Run a system scan to collect technician-ready health details.";
    private string _systemIntelligenceDiskHealthText = "Disk health has not been scanned yet.";
    private string _systemIntelligenceBatteryText = "Battery has not been scanned yet.";
    private string _systemIntelligenceNetworkText = "Network has not been scanned yet.";
    private string _systemIntelligenceSecurityText = "Security has not been scanned yet.";
    private string _systemIntelligenceReportPathText = "Report: not generated";
    private string _systemIntelligenceLastScanText = "Last scan: never";
    private string _systemIntelligenceSystemCardText = "UNKNOWN";
    private string _systemIntelligenceComputeCardText = "UNKNOWN";
    private string _systemIntelligenceStorageCardText = "UNKNOWN";
    private string _systemIntelligenceBatteryCardText = "UNKNOWN";
    private string _systemIntelligenceNetworkCardText = "UNKNOWN";
    private string _systemIntelligenceSecurityCardText = "UNKNOWN";
    private string _systemIntelligenceFlipValueCardText = "Run a system scan to generate local flip-value guidance.";
    private Brush _systemIntelligenceStatusBackground = RunningBackground;
    private Brush _systemIntelligenceStatusBorderBrush = RunningBorder;
    private Brush _systemIntelligenceStatusForeground = RunningForeground;
    private string _toolkitStatusText = "Not scanned";
    private string _toolkitReportPathText = "Report: not generated";
    private string _toolkitInstalledCountText = "Installed 0";
    private string _toolkitMissingCountText = "Missing Required 0";
    private string _toolkitUpdatesCountText = "Updates 0";
    private string _toolkitFailedCountText = "Failed 0";
    private string _toolkitManualCountText = "Manual 0";
    private string _toolkitPlaceholderCountText = "Skipped/Placeholder 0";
    private string _toolkitHealthVerdictText = "Health Verdict: not scanned";
    private string _toolkitManualExplanationText = "Manual items are download pages or licensed/gated tools that ForgerEMS does not auto-download.";
    private string _selectedToolkitFilter = "All";
    private string _selectedToolkitCategoryFilter = "All categories";
    private string _toolkitSearchText = string.Empty;
    private string _toolkitLastScanText = "Last scan: never";
    private ToolkitHealthItemView? _selectedToolkitHealthItem;
    private Brush _toolkitStatusBackground = RunningBackground;
    private Brush _toolkitStatusBorderBrush = RunningBorder;
    private Brush _toolkitStatusForeground = RunningForeground;
    private readonly List<ToolkitHealthItemView> _allToolkitHealthItems = [];
    private string _copilotInput = string.Empty;
    private string _copilotContextText = "Run a system scan and select a USB target to load Copilot context.";
    private string _selectedCopilotMode = "Offline Only";
    private bool _useLatestSystemScanContext = true;
    private bool _isCopilotGenerating;
    private string _copilotOnlineStatusText = "Offline Only - no data leaves this machine.";
    private Brush _copilotOnlineStatusBackground = ReadyBackground;
    private Brush _copilotOnlineStatusBorderBrush = ReadyBorder;
    private Brush _copilotOnlineStatusForeground = ReadyForeground;

    public MainViewModel(
        IBackendDiscoveryService backendDiscoveryService,
        IPowerShellRunnerService powerShellRunnerService,
        IUsbDetectionService usbDetectionService,
        IManagedDownloadSummaryService managedDownloadSummaryService,
        IScriptStatusParser scriptStatusParser,
        IUserPromptService userPromptService,
        IVentoyIntegrationService ventoyIntegrationService,
        IAppRuntimeService appRuntimeService,
        IUsbBenchmarkService usbBenchmarkService,
        ICopilotService copilotService,
        ICopilotProviderRegistry copilotProviderRegistry)
    {
        _backendDiscoveryService = backendDiscoveryService;
        _powerShellRunnerService = powerShellRunnerService;
        _usbDetectionService = usbDetectionService;
        _managedDownloadSummaryService = managedDownloadSummaryService;
        _scriptStatusParser = scriptStatusParser;
        _userPromptService = userPromptService;
        _ventoyIntegrationService = ventoyIntegrationService;
        _appRuntimeService = appRuntimeService;
        _usbBenchmarkService = usbBenchmarkService;
        _copilotService = copilotService;
        _copilotProviderRegistry = copilotProviderRegistry;
        _benchmarkCachePath = Path.Combine(_appRuntimeService.RuntimeRoot, "cache", "usb-benchmarks.json");
        _copilotConfigPath = Path.Combine(_appRuntimeService.RuntimeRoot, "config", "copilot-settings.json");
        LoadBenchmarkCache();
        LoadCopilotSettings();

        RefreshAllCommand = new AsyncRelayCommand(RefreshAllAsync, () => !IsBusy);
        RefreshUsbTargetsCommand = new AsyncRelayCommand(RefreshUsbTargetsAsync, () => !IsBusy);
        VerifyCommand = new AsyncRelayCommand(RunVerifyAsync, CanRunBackendOnlyActions);
        RevalidateManagedDownloadsCommand = new AsyncRelayCommand(RunRevalidateManagedDownloadsAsync, CanRunBackendOnlyActions);
        SetupUsbCommand = new AsyncRelayCommand(RunSetupUsbAsync, CanRunTargetedActions);
        UpdateUsbCommand = new AsyncRelayCommand(RunUpdateUsbAsync, CanRunTargetedActions);
        RenameUsbCommand = new AsyncRelayCommand(RunRenameUsbAsync, CanRunTargetedActions);
        InstallOrUpdateVentoyCommand = new AsyncRelayCommand(RunInstallOrUpdateVentoyAsync, CanRunTargetedActions);
        RunSystemScanCommand = new AsyncRelayCommand(RunSystemScanAsync, CanRunBackendOnlyActions);
        RefreshToolkitHealthCommand = new AsyncRelayCommand(RunToolkitHealthScanAsync, CanRunToolkitScan);
        UpdateToolkitCommand = new AsyncRelayCommand(RunToolkitUpdateAsync, CanRunTargetedActions);
        OpenSystemReportFolderCommand = new RelayCommand(OpenSystemReportFolder);
        CopySystemSummaryCommand = new RelayCommand(CopySystemSummary);
        OpenToolkitUsbReportsCommand = new RelayCommand(OpenToolkitUsbReports, () => SelectedUsbTarget is not null);
        OpenToolkitLocalReportsCommand = new RelayCommand(OpenToolkitLocalReports);
        RecheckSelectedToolCommand = new AsyncRelayCommand(RunToolkitHealthScanAsync, () => CanRunToolkitScan() && SelectedToolkitHealthItem is not null);
        OpenSelectedToolLocationCommand = new RelayCommand(OpenSelectedToolLocation, () => SelectedToolkitHealthItem is not null);
        OpenManualDownloadShortcutCommand = new RelayCommand(OpenManualDownloadShortcut, () => SelectedToolkitHealthItem is not null);
        CopyLogsCommand = new RelayCommand(CopyLogs, () => !string.IsNullOrWhiteSpace(LogsText));
        ClearLogsCommand = new RelayCommand(ClearLogs, () => Logs.Count > 0);
        ShowAboutCommand = new RelayCommand(ShowAbout);
        ShowFaqCommand = new RelayCommand(ShowFaq);
        ShowLegalCommand = new RelayCommand(ShowLegal);
        OpenUbuntuTerminalCommand = new AsyncRelayCommand(OpenUbuntuTerminalAsync, () => !IsBusy);
        CheckWslInstalledCommand = new AsyncRelayCommand(() => RunSafeExternalCommandAsync("Check WSL installed", "wsl.exe", "--status"), () => !IsBusy);
        ShowWslDistrosCommand = new AsyncRelayCommand(() => RunSafeExternalCommandAsync("Show WSL distros", "wsl.exe", "-l", "-v"), () => !IsBusy);
        SendCopilotMessageCommand = new AsyncRelayCommand(SendCopilotMessageAsync, () => !IsCopilotGenerating && !string.IsNullOrWhiteSpace(CopilotInput));
        AskCopilotValueCommand = new AsyncRelayCommand(() => AskCopilotAsync("What is this laptop worth?"), () => !IsCopilotGenerating);
        AskCopilotUpgradeCommand = new AsyncRelayCommand(() => AskCopilotAsync("What should I upgrade before selling?"), () => !IsCopilotGenerating);
        AskCopilotLagCommand = new AsyncRelayCommand(() => AskCopilotAsync("Why is my computer lagging?"), () => !IsCopilotGenerating);
        AskCopilotOsCommand = new AsyncRelayCommand(() => AskCopilotAsync("Best OS for this machine?"), () => !IsCopilotGenerating);
        AskCopilotUsbCommand = new AsyncRelayCommand(() => AskCopilotAsync("Best USB toolkit for this job?"), () => !IsCopilotGenerating);
        AskCopilotWarningCommand = new AsyncRelayCommand(() => AskCopilotAsync("What does this warning mean?"), () => !IsCopilotGenerating);
        ClearCopilotHistoryCommand = new RelayCommand(ClearCopilotHistoryAndCache);
        StopCopilotGenerationCommand = new RelayCommand(StopCopilotGeneration, () => IsCopilotGenerating);
        UseLatestSystemScanContextCommand = new RelayCommand(UseLatestSystemScanContextNow);

        CopilotMessages.Add(new CopilotChatMessage
        {
            Role = "Kyra",
            Text = "I work offline from the latest local System Intelligence report and selected USB state. No private data is sent anywhere."
        });
    }

    public ObservableCollection<UsbTargetInfo> UsbTargets { get; } = [];

    public ObservableCollection<LogLine> Logs { get; } = [];

    public ObservableCollection<string> SystemIntelligenceRecommendations { get; } = [];

    public ObservableCollection<ToolkitHealthItemView> ToolkitHealthItems { get; } = [];

    public ObservableCollection<CopilotChatMessage> CopilotMessages { get; } = [];

    public ObservableCollection<CopilotProviderSettingView> CopilotProviderSettings { get; } = [];

    public IReadOnlyList<string> LogLevelFilterOptions { get; } = ["All", "Info", "Success", "Warning", "Error"];

    public IReadOnlyList<string> ToolkitFilterOptions { get; } = ["All", "Installed", "Required Missing", "Manual", "Failed", "Updates", "Skipped/Placeholder"];

    public IReadOnlyList<string> ToolkitCategoryFilterOptions { get; } = ["All categories", "Windows", "Linux", "Recovery", "Diagnostics", "USB Builders"];

    public IReadOnlyList<string> CopilotModeOptions { get; } = ["Offline Only", "Online Assisted", "Hybrid Auto"];

    public AsyncRelayCommand RefreshAllCommand { get; }

    public AsyncRelayCommand RefreshUsbTargetsCommand { get; }

    public AsyncRelayCommand VerifyCommand { get; }

    public AsyncRelayCommand RevalidateManagedDownloadsCommand { get; }

    public AsyncRelayCommand SetupUsbCommand { get; }

    public AsyncRelayCommand UpdateUsbCommand { get; }

    public AsyncRelayCommand RenameUsbCommand { get; }

    public AsyncRelayCommand InstallOrUpdateVentoyCommand { get; }

    public AsyncRelayCommand RunSystemScanCommand { get; }

    public AsyncRelayCommand RefreshToolkitHealthCommand { get; }

    public AsyncRelayCommand UpdateToolkitCommand { get; }

    public RelayCommand OpenSystemReportFolderCommand { get; }

    public RelayCommand CopySystemSummaryCommand { get; }

    public RelayCommand OpenToolkitUsbReportsCommand { get; }

    public RelayCommand OpenToolkitLocalReportsCommand { get; }

    public AsyncRelayCommand RecheckSelectedToolCommand { get; }

    public RelayCommand OpenSelectedToolLocationCommand { get; }

    public RelayCommand OpenManualDownloadShortcutCommand { get; }

    public RelayCommand CopyLogsCommand { get; }

    public RelayCommand ClearLogsCommand { get; }

    public RelayCommand ShowAboutCommand { get; }

    public RelayCommand ShowFaqCommand { get; }

    public RelayCommand ShowLegalCommand { get; }

    public AsyncRelayCommand OpenUbuntuTerminalCommand { get; }

    public AsyncRelayCommand CheckWslInstalledCommand { get; }

    public AsyncRelayCommand ShowWslDistrosCommand { get; }

    public AsyncRelayCommand SendCopilotMessageCommand { get; }

    public AsyncRelayCommand AskCopilotValueCommand { get; }

    public AsyncRelayCommand AskCopilotUpgradeCommand { get; }

    public AsyncRelayCommand AskCopilotLagCommand { get; }

    public AsyncRelayCommand AskCopilotOsCommand { get; }

    public AsyncRelayCommand AskCopilotUsbCommand { get; }

    public AsyncRelayCommand AskCopilotWarningCommand { get; }

    public RelayCommand ClearCopilotHistoryCommand { get; }

    public RelayCommand StopCopilotGenerationCommand { get; }

    public RelayCommand UseLatestSystemScanContextCommand { get; }

    public UsbTargetInfo? SelectedUsbTarget
    {
        get => _selectedUsbTarget;
        set
        {
            if (SetProperty(ref _selectedUsbTarget, value))
            {
                UpdateTargetWarnings();
                RaiseCommandStates();
                OnPropertyChanged(nameof(HeaderUsbTargetText));
                OnPropertyChanged(nameof(LogStatusLineText));
                RefreshCopilotContextText();

                if (!_suppressSelectionRefresh)
                {
                    _ = RefreshVentoyStatusSafeAsync();
                    _ = AutoBenchmarkSelectedUsbSafeAsync();
                }
            }
        }
    }

    public string UsbOwnerName
    {
        get => _usbOwnerName;
        set => SetProperty(ref _usbOwnerName, value);
    }

    public bool AutoScrollLogs
    {
        get => _autoScrollLogs;
        set => SetProperty(ref _autoScrollLogs, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                RaiseCommandStates();
            }
        }
    }

    public bool IsIdle => !IsBusy;

    public string BackendModeText => _backendContext.ModeLabel;

    public string BackendRootText =>
        _backendContext.IsAvailable
            ? _backendContext.RootPath
            : _backendContext.DiagnosticMessage;

    public string BackendDiagnosticText =>
        string.IsNullOrWhiteSpace(_backendContext.DiagnosticMessage)
            ? "No backend details are available."
            : _backendContext.DiagnosticMessage;

    public string BackendVersionText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_backendContext.FrontendVersion) &&
                string.IsNullOrWhiteSpace(_backendContext.BackendVersion))
            {
                return _backendContext.IsAvailable
                    ? "Backend version not detected."
                    : "Frontend version is unavailable.";
            }

            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(_backendContext.FrontendVersion))
            {
                parts.Add($"Frontend {_backendContext.FrontendVersion}");
            }

            if (!string.IsNullOrWhiteSpace(_backendContext.BackendVersion))
            {
                parts.Add($"Backend {_backendContext.BackendVersion}");
            }

            parts.Add($"Status: {GetBackendCompatibilityStatus()}");

            return string.Join(" | ", parts);
        }
    }

    public string AppVersionText => AppReleaseInfo.DisplayVersion;

    public string HeaderUsbTargetText => SelectedUsbTarget is null ? "USB: none" : $"USB: {SelectedUsbTarget.RootPath}";

    public string LogStatusLineText =>
        $"STATUS: {CurrentTaskState} | {CurrentTaskText} | {BackendModeText} | USB Detected: {(SelectedUsbTarget?.RootPath ?? "none")} | {Logs.Count(item => item.Severity == LogSeverity.Error)} errors";

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string StatusDetail
    {
        get => _statusDetail;
        private set => SetProperty(ref _statusDetail, value);
    }

    public string CurrentTaskState
    {
        get => _currentTaskState;
        private set => SetProperty(ref _currentTaskState, value);
    }

    public string CurrentTaskText
    {
        get => _currentTaskText;
        private set => SetProperty(ref _currentTaskText, value);
    }

    public double CurrentProgressValue
    {
        get => _currentProgressValue;
        private set => SetProperty(ref _currentProgressValue, value);
    }

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => SetProperty(ref _isProgressIndeterminate, value);
    }

    public Visibility ProgressVisibility
    {
        get => _progressVisibility;
        private set => SetProperty(ref _progressVisibility, value);
    }

    public Brush StatusBackground
    {
        get => _statusBackground;
        private set => SetProperty(ref _statusBackground, value);
    }

    public Brush StatusBorderBrush
    {
        get => _statusBorderBrush;
        private set => SetProperty(ref _statusBorderBrush, value);
    }

    public Brush StatusForeground
    {
        get => _statusForeground;
        private set => SetProperty(ref _statusForeground, value);
    }

    public string LastCommandText
    {
        get => _lastCommandText;
        private set => SetProperty(ref _lastCommandText, value);
    }

    public string ManagedSummaryText
    {
        get => _managedSummaryText;
        private set => SetProperty(ref _managedSummaryText, value);
    }

    public string ManagedSummaryPathText
    {
        get => _managedSummaryPathText;
        private set => SetProperty(ref _managedSummaryPathText, value);
    }

    public string ManagedSummaryUpdatedText
    {
        get => _managedSummaryUpdatedText;
        private set => SetProperty(ref _managedSummaryUpdatedText, value);
    }

    public string ManagedSummaryStatusText
    {
        get => _managedSummaryStatusText;
        private set => SetProperty(ref _managedSummaryStatusText, value);
    }

    public string LogsText
    {
        get => _logsText;
        private set => SetProperty(ref _logsText, value);
    }

    public string RecentLogsText
    {
        get => _recentLogsText;
        private set => SetProperty(ref _recentLogsText, value);
    }

    public string SelectedLogLevelFilter
    {
        get => _selectedLogLevelFilter;
        set
        {
            if (SetProperty(ref _selectedLogLevelFilter, value))
            {
                RefreshLogsText();
            }
        }
    }

    public Brush ManagedSummaryStatusBackground
    {
        get => _managedSummaryStatusBackground;
        private set => SetProperty(ref _managedSummaryStatusBackground, value);
    }

    public Brush ManagedSummaryStatusBorderBrush
    {
        get => _managedSummaryStatusBorderBrush;
        private set => SetProperty(ref _managedSummaryStatusBorderBrush, value);
    }

    public Brush ManagedSummaryStatusForeground
    {
        get => _managedSummaryStatusForeground;
        private set => SetProperty(ref _managedSummaryStatusForeground, value);
    }

    public string TargetWarningText
    {
        get => _targetWarningText;
        private set => SetProperty(ref _targetWarningText, value);
    }

    public Brush TargetWarningBackground
    {
        get => _targetWarningBackground;
        private set => SetProperty(ref _targetWarningBackground, value);
    }

    public Brush TargetWarningBorderBrush
    {
        get => _targetWarningBorderBrush;
        private set => SetProperty(ref _targetWarningBorderBrush, value);
    }

    public Brush TargetWarningForeground
    {
        get => _targetWarningForeground;
        private set => SetProperty(ref _targetWarningForeground, value);
    }

    public string ActionWarningText
    {
        get => _actionWarningText;
        private set => SetProperty(ref _actionWarningText, value);
    }

    public string VentoyStatusText
    {
        get => _ventoyStatusText;
        private set => SetProperty(ref _ventoyStatusText, value);
    }

    public string VentoyDetailText
    {
        get => _ventoyDetailText;
        private set => SetProperty(ref _ventoyDetailText, value);
    }

    public string VentoyPackageText
    {
        get => _ventoyPackageText;
        private set => SetProperty(ref _ventoyPackageText, value);
    }

    public Brush VentoyStatusBackground
    {
        get => _ventoyStatusBackground;
        private set => SetProperty(ref _ventoyStatusBackground, value);
    }

    public Brush VentoyStatusBorderBrush
    {
        get => _ventoyStatusBorderBrush;
        private set => SetProperty(ref _ventoyStatusBorderBrush, value);
    }

    public Brush VentoyStatusForeground
    {
        get => _ventoyStatusForeground;
        private set => SetProperty(ref _ventoyStatusForeground, value);
    }

    public string AppVersionFooterText => AppReleaseInfo.ReleaseIdentifier;

    public string SystemIntelligenceStatusText
    {
        get => _systemIntelligenceStatusText;
        private set => SetProperty(ref _systemIntelligenceStatusText, value);
    }

    public string SystemIntelligenceSummaryText
    {
        get => _systemIntelligenceSummaryText;
        private set => SetProperty(ref _systemIntelligenceSummaryText, value);
    }

    public string SystemIntelligenceDiskHealthText
    {
        get => _systemIntelligenceDiskHealthText;
        private set => SetProperty(ref _systemIntelligenceDiskHealthText, value);
    }

    public string SystemIntelligenceBatteryText
    {
        get => _systemIntelligenceBatteryText;
        private set => SetProperty(ref _systemIntelligenceBatteryText, value);
    }

    public string SystemIntelligenceNetworkText
    {
        get => _systemIntelligenceNetworkText;
        private set => SetProperty(ref _systemIntelligenceNetworkText, value);
    }

    public string SystemIntelligenceSecurityText
    {
        get => _systemIntelligenceSecurityText;
        private set => SetProperty(ref _systemIntelligenceSecurityText, value);
    }

    public string SystemIntelligenceReportPathText
    {
        get => _systemIntelligenceReportPathText;
        private set => SetProperty(ref _systemIntelligenceReportPathText, value);
    }

    public string SystemIntelligenceLastScanText
    {
        get => _systemIntelligenceLastScanText;
        private set => SetProperty(ref _systemIntelligenceLastScanText, value);
    }

    public string SystemIntelligenceSystemCardText
    {
        get => _systemIntelligenceSystemCardText;
        private set => SetProperty(ref _systemIntelligenceSystemCardText, value);
    }

    public string SystemIntelligenceComputeCardText
    {
        get => _systemIntelligenceComputeCardText;
        private set => SetProperty(ref _systemIntelligenceComputeCardText, value);
    }

    public string SystemIntelligenceStorageCardText
    {
        get => _systemIntelligenceStorageCardText;
        private set => SetProperty(ref _systemIntelligenceStorageCardText, value);
    }

    public string SystemIntelligenceBatteryCardText
    {
        get => _systemIntelligenceBatteryCardText;
        private set => SetProperty(ref _systemIntelligenceBatteryCardText, value);
    }

    public string SystemIntelligenceNetworkCardText
    {
        get => _systemIntelligenceNetworkCardText;
        private set => SetProperty(ref _systemIntelligenceNetworkCardText, value);
    }

    public string SystemIntelligenceSecurityCardText
    {
        get => _systemIntelligenceSecurityCardText;
        private set => SetProperty(ref _systemIntelligenceSecurityCardText, value);
    }

    public string SystemIntelligenceFlipValueCardText
    {
        get => _systemIntelligenceFlipValueCardText;
        private set => SetProperty(ref _systemIntelligenceFlipValueCardText, value);
    }

    public Brush SystemIntelligenceStatusBackground
    {
        get => _systemIntelligenceStatusBackground;
        private set => SetProperty(ref _systemIntelligenceStatusBackground, value);
    }

    public Brush SystemIntelligenceStatusBorderBrush
    {
        get => _systemIntelligenceStatusBorderBrush;
        private set => SetProperty(ref _systemIntelligenceStatusBorderBrush, value);
    }

    public Brush SystemIntelligenceStatusForeground
    {
        get => _systemIntelligenceStatusForeground;
        private set => SetProperty(ref _systemIntelligenceStatusForeground, value);
    }

    public string CopilotInput
    {
        get => _copilotInput;
        set
        {
            if (SetProperty(ref _copilotInput, value))
            {
                SendCopilotMessageCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string CopilotContextText
    {
        get => _copilotContextText;
        private set => SetProperty(ref _copilotContextText, value);
    }

    public bool UseLatestSystemScanContext
    {
        get => _useLatestSystemScanContext;
        set
        {
            if (SetProperty(ref _useLatestSystemScanContext, value))
            {
                RefreshCopilotContextText();
                SaveCopilotSettings();
            }
        }
    }

    public bool IsCopilotGenerating
    {
        get => _isCopilotGenerating;
        private set
        {
            if (SetProperty(ref _isCopilotGenerating, value))
            {
                SendCopilotMessageCommand.RaiseCanExecuteChanged();
                AskCopilotValueCommand.RaiseCanExecuteChanged();
                AskCopilotUpgradeCommand.RaiseCanExecuteChanged();
                AskCopilotLagCommand.RaiseCanExecuteChanged();
                AskCopilotOsCommand.RaiseCanExecuteChanged();
                AskCopilotUsbCommand.RaiseCanExecuteChanged();
                AskCopilotWarningCommand.RaiseCanExecuteChanged();
                StopCopilotGenerationCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SelectedCopilotMode
    {
        get => _selectedCopilotMode;
        set
        {
            if (SetProperty(ref _selectedCopilotMode, value))
            {
                UpdateCopilotOnlineIndicator();
                SaveCopilotSettings();
            }
        }
    }

    public string CopilotOnlineStatusText
    {
        get => _copilotOnlineStatusText;
        private set => SetProperty(ref _copilotOnlineStatusText, value);
    }

    public Brush CopilotOnlineStatusBackground
    {
        get => _copilotOnlineStatusBackground;
        private set => SetProperty(ref _copilotOnlineStatusBackground, value);
    }

    public Brush CopilotOnlineStatusBorderBrush
    {
        get => _copilotOnlineStatusBorderBrush;
        private set => SetProperty(ref _copilotOnlineStatusBorderBrush, value);
    }

    public Brush CopilotOnlineStatusForeground
    {
        get => _copilotOnlineStatusForeground;
        private set => SetProperty(ref _copilotOnlineStatusForeground, value);
    }

    public string ToolkitStatusText
    {
        get => _toolkitStatusText;
        private set => SetProperty(ref _toolkitStatusText, value);
    }

    public string ToolkitReportPathText
    {
        get => _toolkitReportPathText;
        private set => SetProperty(ref _toolkitReportPathText, value);
    }

    public string ToolkitInstalledCountText
    {
        get => _toolkitInstalledCountText;
        private set => SetProperty(ref _toolkitInstalledCountText, value);
    }

    public string ToolkitMissingCountText
    {
        get => _toolkitMissingCountText;
        private set => SetProperty(ref _toolkitMissingCountText, value);
    }

    public string ToolkitUpdatesCountText
    {
        get => _toolkitUpdatesCountText;
        private set => SetProperty(ref _toolkitUpdatesCountText, value);
    }

    public string ToolkitFailedCountText
    {
        get => _toolkitFailedCountText;
        private set => SetProperty(ref _toolkitFailedCountText, value);
    }

    public string ToolkitManualCountText
    {
        get => _toolkitManualCountText;
        private set => SetProperty(ref _toolkitManualCountText, value);
    }

    public string ToolkitPlaceholderCountText
    {
        get => _toolkitPlaceholderCountText;
        private set => SetProperty(ref _toolkitPlaceholderCountText, value);
    }

    public string ToolkitHealthVerdictText
    {
        get => _toolkitHealthVerdictText;
        private set => SetProperty(ref _toolkitHealthVerdictText, value);
    }

    public string ToolkitLastScanText
    {
        get => _toolkitLastScanText;
        private set => SetProperty(ref _toolkitLastScanText, value);
    }

    public string ToolkitManualExplanationText
    {
        get => _toolkitManualExplanationText;
        private set => SetProperty(ref _toolkitManualExplanationText, value);
    }

    public string SelectedToolkitFilter
    {
        get => _selectedToolkitFilter;
        set
        {
            if (SetProperty(ref _selectedToolkitFilter, value))
            {
                ApplyToolkitFilter();
            }
        }
    }

    public string SelectedToolkitCategoryFilter
    {
        get => _selectedToolkitCategoryFilter;
        set
        {
            if (SetProperty(ref _selectedToolkitCategoryFilter, value))
            {
                ApplyToolkitFilter();
            }
        }
    }

    public string ToolkitSearchText
    {
        get => _toolkitSearchText;
        set
        {
            if (SetProperty(ref _toolkitSearchText, value))
            {
                ApplyToolkitFilter();
            }
        }
    }

    public ToolkitHealthItemView? SelectedToolkitHealthItem
    {
        get => _selectedToolkitHealthItem;
        set
        {
            if (SetProperty(ref _selectedToolkitHealthItem, value))
            {
                OnPropertyChanged(nameof(SelectedToolkitDetailText));
                RecheckSelectedToolCommand.RaiseCanExecuteChanged();
                OpenSelectedToolLocationCommand.RaiseCanExecuteChanged();
                OpenManualDownloadShortcutCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SelectedToolkitDetailText => SelectedToolkitHealthItem?.DetailText ?? "Select a toolkit item to see its status, expected path, and next step.";

    public Brush ToolkitStatusBackground
    {
        get => _toolkitStatusBackground;
        private set => SetProperty(ref _toolkitStatusBackground, value);
    }

    public Brush ToolkitStatusBorderBrush
    {
        get => _toolkitStatusBorderBrush;
        private set => SetProperty(ref _toolkitStatusBorderBrush, value);
    }

    public Brush ToolkitStatusForeground
    {
        get => _toolkitStatusForeground;
        private set => SetProperty(ref _toolkitStatusForeground, value);
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await RefreshAllAsync();
        StartUsbAutoDetectionMonitor();
    }

    private bool CanRunBackendOnlyActions()
    {
        return !_isBusy && _backendContext.IsAvailable;
    }

    private bool CanRunTargetedActions()
    {
        return !_isBusy &&
               _backendContext.IsAvailable &&
               SelectedUsbTarget is not null &&
               UsbTargetSafety.GetExecutionBlockReason(SelectedUsbTarget) is null;
    }

    private bool CanRunToolkitScan()
    {
        return !_isBusy &&
               _backendContext.IsAvailable &&
               SelectedUsbTarget is not null;
    }

    private async Task RefreshAllAsync()
    {
        SetStatus(
            "Refreshing controller state",
            "Checking the backend location, refreshing USB targets, and loading the latest managed-download summary.",
            RunningBackground,
            RunningBorder,
            RunningForeground);

        _backendContext = _backendDiscoveryService.Discover();
        NotifyBackendChanged();

        await RefreshUsbTargetsAsync();
        await LoadManagedSummaryAsync();
        await RefreshVentoyStatusAsync();
        LoadSystemIntelligenceReport();
        LoadToolkitHealthReport();

        if (_backendContext.IsAvailable)
        {
            SetStatus(
                "Backend ready",
                _backendContext.Mode == BackendMode.Bundled
                    ? "Bundled backend detected. Installed mode is self-contained and ready to verify or operate against a selected USB."
                    : $"{_backendContext.ModeLabel} detected. You can verify immediately and run setup or update after selecting a target.",
                ReadyBackground,
                ReadyBorder,
                ReadyForeground);
        }
        else
        {
            SetStatus(
                "Backend not found",
                _backendContext.DiagnosticMessage,
                ErrorBackground,
                ErrorBorder,
                ErrorForeground);
        }
    }

    private async Task RefreshUsbTargetsAsync()
    {
        if (_refreshingUsbTargets)
        {
            return;
        }

        _refreshingUsbTargets = true;
        var previousSelection = SelectedUsbTarget?.RootPath;
        try
        {
            var detectionResult = await _usbDetectionService.GetUsbTargetsAsync();
            var targets = detectionResult.Targets;
            var previousSelectionStillPresent = !string.IsNullOrWhiteSpace(previousSelection) &&
                                                targets.Any(item => string.Equals(item.RootPath, previousSelection, StringComparison.OrdinalIgnoreCase));

            UsbTargets.Clear();
            foreach (var target in targets)
            {
                UsbTargets.Add(ApplyCachedBenchmarkResult(target));
            }

            _knownUsbSignature = BuildUsbSignature(UsbTargets);

            _suppressSelectionRefresh = true;
            try
            {
                SelectedUsbTarget = UsbTargets.FirstOrDefault(item =>
                    item.IsSelectable &&
                    string.Equals(item.RootPath, previousSelection, StringComparison.OrdinalIgnoreCase));

                if (SelectedUsbTarget is null)
                {
                    SelectedUsbTarget = UsbTargets.FirstOrDefault(item => item.IsSelectable);
                }
            }
            finally
            {
                _suppressSelectionRefresh = false;
            }

            UpdateTargetWarnings();
            AppendUsbDetectionDiagnostics(detectionResult.Diagnostics);

            if (!string.IsNullOrWhiteSpace(previousSelection) && !previousSelectionStillPresent)
            {
                AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] USB target removed: {previousSelection}", LogSeverity.Warning));
            }

            if (UsbTargets.Count == 0)
            {
                AppendLog(new LogLine(DateTimeOffset.Now, "No likely USB targets were detected.", LogSeverity.Warning));
            }
            else
            {
                AppendLog(new LogLine(DateTimeOffset.Now, $"Detected {UsbTargets.Count} likely USB target(s).", LogSeverity.Info));
            }

            await RefreshVentoyStatusAsync();
            _ = AutoBenchmarkSelectedUsbSafeAsync();
        }
        finally
        {
            _refreshingUsbTargets = false;
        }
    }

    private async Task RunVerifyAsync()
    {
        await RunScriptAsync(
            ScriptActionType.VerifyBackend,
            new PowerShellRunRequest
            {
                DisplayName = "Verify backend",
                WorkingDirectory = _backendContext.WorkingDirectory,
                ScriptPath = _backendContext.VerifyScriptPath
            });
    }

    private async Task RunRevalidateManagedDownloadsAsync()
    {
        await RunScriptAsync(
            ScriptActionType.RevalidateManagedDownloads,
            new PowerShellRunRequest
            {
                DisplayName = "Revalidate managed downloads",
                WorkingDirectory = _backendContext.WorkingDirectory,
                ScriptPath = _backendContext.VerifyScriptPath,
                Arguments = ["-RevalidateManagedDownloads"],
                ProgressItemName = "managed download revalidation"
            });
    }

    private async Task RunSetupUsbAsync()
    {
        if (!TryGetValidatedSelectedTarget("Setup USB", out var selectedUsbTarget))
        {
            return;
        }

        if (!ConfirmTargetedAction(
                "Setup USB",
                selectedUsbTarget,
                "This will create or refresh toolkit folders on the selected target, seed the manifest, and run in real mode."))
        {
            return;
        }

        var arguments = new System.Collections.Generic.List<string>
        {
            "-UsbRoot",
            selectedUsbTarget.RootPath,
            "-SeedManifest",
            "-NonInteractive"
        };

        if (!string.IsNullOrWhiteSpace(UsbOwnerName))
        {
            arguments.Add("-OwnerName");
            arguments.Add(UsbOwnerName.Trim());
        }

        arguments.Add("-WaitForManagedDownloads");

        await RunScriptAsync(
            ScriptActionType.SetupUsb,
            new PowerShellRunRequest
            {
                DisplayName = "Setup USB + managed downloads",
                WorkingDirectory = _backendContext.WorkingDirectory,
                ScriptPath = _backendContext.SetupScriptPath,
                Arguments = arguments,
                ProgressItemName = "managed downloads"
            });
    }

    private async Task RunUpdateUsbAsync()
    {
        if (!TryGetValidatedSelectedTarget("Update USB", out var selectedUsbTarget))
        {
            return;
        }

        if (!ConfirmTargetedAction(
                "Update USB",
                selectedUsbTarget,
                "This can archive and replace managed files on the selected target and runs in real mode."))
        {
            return;
        }

        var arguments = new System.Collections.Generic.List<string>
        {
            "-UsbRoot",
            selectedUsbTarget.RootPath
        };

        await RunScriptAsync(
            ScriptActionType.UpdateUsb,
            new PowerShellRunRequest
            {
                DisplayName = "Update USB",
                WorkingDirectory = _backendContext.WorkingDirectory,
                ScriptPath = _backendContext.UpdateScriptPath,
                Arguments = arguments,
                ProgressItemName = "managed downloads"
            });
    }

    private async Task RunSystemScanAsync()
    {
        var scriptPath = ResolveBackendScriptPath(Path.Combine("SystemIntelligence", "Invoke-ForgerEMSSystemScan.ps1"));
        if (!File.Exists(scriptPath))
        {
            SetStatus(
                "System scan unavailable",
                $"System Intelligence script was not found: {scriptPath}",
                ErrorBackground,
                ErrorBorder,
                ErrorForeground);
            return;
        }

        await RunScriptAsync(
            ScriptActionType.SystemIntelligence,
            new PowerShellRunRequest
            {
                DisplayName = "System Intelligence scan",
                WorkingDirectory = _backendContext.WorkingDirectory,
                ScriptPath = scriptPath
            });

        LoadSystemIntelligenceReport();
    }

    private async Task RunToolkitHealthScanAsync()
    {
        if (SelectedUsbTarget is null)
        {
            _userPromptService.ShowMessage("Toolkit Manager", "Select a USB or target root before scanning toolkit health.", MessageBoxImage.Warning);
            return;
        }

        var scriptPath = ResolveBackendScriptPath(Path.Combine("ToolkitManager", "Get-ForgerEMSToolkitHealth.ps1"));
        if (!File.Exists(scriptPath))
        {
            SetStatus(
                "Toolkit scan unavailable",
                $"Toolkit Manager script was not found: {scriptPath}",
                ErrorBackground,
                ErrorBorder,
                ErrorForeground);
            return;
        }

        await RunScriptAsync(
            ScriptActionType.ToolkitHealth,
            new PowerShellRunRequest
            {
                DisplayName = "Toolkit health scan",
                WorkingDirectory = _backendContext.WorkingDirectory,
                ScriptPath = scriptPath,
                Arguments =
                [
                    "-TargetRoot",
                    SelectedUsbTarget.RootPath,
                    "-ManifestPath",
                    ResolveManifestPath()
                ],
                ProgressItemName = "toolkit health scan"
            });

        LoadToolkitHealthReport();
    }

    private async Task RunToolkitUpdateAsync()
    {
        if (SelectedUsbTarget is null)
        {
            return;
        }

        if (IsRootPath(SelectedUsbTarget.RootPath, "C:\\"))
        {
            const string blockReason = "Toolkit updates are blocked on C:\\. ForgerEMS never writes toolkit changes to the system drive.";
            AppendLog(new LogLine(DateTimeOffset.Now, $"[ERROR] {blockReason}", LogSeverity.Error, isErrorStream: true));
            _userPromptService.ShowMessage("Toolkit update blocked", blockReason, MessageBoxImage.Error);
            return;
        }

        await RunUpdateUsbAsync();
        LoadToolkitHealthReport();
    }

    private void OpenSystemReportFolder()
    {
        OpenFolder(GetRuntimeReportsDirectory(), "System Intelligence reports", createIfMissing: true);
    }

    private void CopySystemSummary()
    {
        var summary = string.Join(
            Environment.NewLine,
            new[]
            {
                $"Status: {SystemIntelligenceStatusText}",
                SystemIntelligenceLastScanText,
                SystemIntelligenceSystemCardText,
                SystemIntelligenceComputeCardText,
                SystemIntelligenceStorageCardText,
                SystemIntelligenceBatteryCardText,
                SystemIntelligenceNetworkCardText,
                SystemIntelligenceSecurityCardText,
                "Recommendations:",
                string.Join(Environment.NewLine, SystemIntelligenceRecommendations.Select(item => "- " + item))
            });

        Clipboard.SetText(summary);
        AppendLog(new LogLine(DateTimeOffset.Now, "[OK] System Intelligence summary copied to clipboard.", LogSeverity.Success));
    }

    private async Task AskCopilotAsync(string prompt)
    {
        CopilotInput = prompt;
        await SendCopilotMessageAsync();
    }

    private async Task SendCopilotMessageAsync()
    {
        var prompt = CopilotInput.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        CopilotMessages.Add(new CopilotChatMessage
        {
            Role = "You",
            Text = prompt
        });

        var reportPath = Path.Combine(GetRuntimeReportsDirectory(), "system-intelligence-latest.json");
        var toolkitReportPath = Path.Combine(GetRuntimeReportsDirectory(), "toolkit-health-latest.json");
        CopilotResponse response;
        _copilotGenerationCancellation?.Dispose();
        _copilotGenerationCancellation = new CancellationTokenSource();
        IsCopilotGenerating = true;
        try
        {
            response = await _copilotService.GenerateReplyAsync(new CopilotRequest
            {
                Prompt = prompt,
                SystemIntelligenceReportPath = reportPath,
                ToolkitHealthReportPath = toolkitReportPath,
                AppVersion = GetType().Assembly.GetName().Version?.ToString() ?? "unknown",
                RecentLogLines = Logs.Select(line => line.DisplayText).TakeLast(24).ToArray(),
                SelectedUsbTarget = SelectedUsbTarget,
                Settings = BuildCopilotSettingsFromUi()
            }, _copilotGenerationCancellation.Token);
        }
        finally
        {
            IsCopilotGenerating = false;
        }

        CopilotMessages.Add(new CopilotChatMessage
        {
            Role = "Kyra",
            Text = response.Text
        });

        if (response.ProviderNotes.Count > 0)
        {
            CopilotMessages.Add(new CopilotChatMessage
            {
                Role = "Kyra Providers",
                Text = string.Join(Environment.NewLine, response.ProviderNotes)
            });
        }

        CopilotInput = string.Empty;
        ApplyCopilotOnlineIndicator(response);
        SaveCopilotSettings();
        AppendLog(new LogLine(DateTimeOffset.Now, response.UsedOnlineData ? "[INFO] Copilot answered with sanitized online provider data." : "[INFO] Copilot answered from local/offline fallback context.", LogSeverity.Info));
    }

    private void StopCopilotGeneration()
    {
        _copilotGenerationCancellation?.Cancel();
        AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] Copilot stop requested.", LogSeverity.Info));
    }

    private void UseLatestSystemScanContextNow()
    {
        UseLatestSystemScanContext = true;
        LoadSystemIntelligenceReport();
        RefreshCopilotContextText();
        AppendLog(new LogLine(DateTimeOffset.Now, "[OK] Copilot context refreshed from latest local System Intelligence report.", LogSeverity.Success));
    }

    private void ClearCopilotHistoryAndCache()
    {
        CopilotMessages.Clear();
        CopilotMessages.Add(new CopilotChatMessage
        {
            Role = "Kyra",
            Text = "Kyra history and local provider cache were cleared. Offline rules remain available."
        });

        try
        {
            var cacheRoot = Path.Combine(_appRuntimeService.RuntimeRoot, "cache", "copilot");
            if (Directory.Exists(cacheRoot))
            {
                Directory.Delete(cacheRoot, recursive: true);
            }
        }
        catch (Exception exception)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Copilot cache cleanup skipped: {exception.Message}", LogSeverity.Warning));
        }

        AppendLog(new LogLine(DateTimeOffset.Now, "[OK] Copilot history/cache cleared.", LogSeverity.Success));
    }

    private void OpenToolkitUsbReports()
    {
        if (SelectedUsbTarget is null)
        {
            return;
        }

        var reportFolder = Path.Combine(SelectedUsbTarget.RootPath, "_reports");
        if (!Directory.Exists(reportFolder))
        {
            AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] USB reports folder was not found: {reportFolder}", LogSeverity.Warning));
            return;
        }

        OpenFolder(reportFolder, "USB reports", createIfMissing: false);
    }

    private void OpenToolkitLocalReports()
    {
        OpenFolder(GetRuntimeReportsDirectory(), "local reports", createIfMissing: true);
    }

    private void OpenSelectedToolLocation()
    {
        var item = SelectedToolkitHealthItem;
        if (item is null)
        {
            return;
        }

        var path = item.MatchedPath;
        if (string.IsNullOrWhiteSpace(path) && SelectedUsbTarget is not null && !string.IsNullOrWhiteSpace(item.ExpectedPath))
        {
            path = Path.Combine(SelectedUsbTarget.RootPath, item.ExpectedPath);
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            AppendLog(new LogLine(DateTimeOffset.Now, "[WARN] Selected toolkit item does not include a path to open.", LogSeverity.Warning));
            return;
        }

        var folderPath = File.Exists(path) ? Path.GetDirectoryName(path) : path;
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Toolkit item location was not found: {path}", LogSeverity.Warning));
            return;
        }

        OpenFolder(folderPath, "tool location", createIfMissing: false);
    }

    private void OpenManualDownloadShortcut()
    {
        var item = SelectedToolkitHealthItem;
        if (item is null)
        {
            return;
        }

        var candidates = new[]
        {
            item.MatchedPath,
            SelectedUsbTarget is null || string.IsNullOrWhiteSpace(item.ExpectedPath)
                ? string.Empty
                : Path.Combine(SelectedUsbTarget.RootPath, item.ExpectedPath),
            item.Url
        };

        foreach (var candidate in candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate)))
        {
            try
            {
                if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
                    (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                     uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
                {
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                    return;
                }

                if (File.Exists(candidate))
                {
                    Process.Start(new ProcessStartInfo(candidate) { UseShellExecute = true });
                    return;
                }
            }
            catch (Exception exception)
            {
                AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Could not open manual shortcut: {exception.Message}", LogSeverity.Warning));
                return;
            }
        }

        AppendLog(new LogLine(DateTimeOffset.Now, "[WARN] No manual download shortcut or URL was available for the selected item.", LogSeverity.Warning));
    }

    private void OpenFolder(string folderPath, string displayName, bool createIfMissing)
    {
        try
        {
            if (createIfMissing)
            {
                Directory.CreateDirectory(folderPath);
            }

            Process.Start(new ProcessStartInfo(folderPath)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Could not open {displayName}: {exception.Message}", LogSeverity.Warning));
        }
    }

    private async Task RunRenameUsbAsync()
    {
        if (!TryGetValidatedSelectedTarget("Rename USB", out var selectedUsbTarget))
        {
            return;
        }

        var currentLabel = selectedUsbTarget.LabelDisplay == "(no label)" ? string.Empty : selectedUsbTarget.LabelDisplay;
        var newLabel = _userPromptService.PromptText(
            "Rename USB",
            $"Enter a new label for {selectedUsbTarget.RootPath}. Keep it short and recognizable.",
            currentLabel);

        if (newLabel is null)
        {
            return;
        }

        newLabel = newLabel.Trim();
        if (!TryValidateVolumeLabel(newLabel, out var validationError))
        {
            _userPromptService.ShowMessage("Rename USB", validationError, MessageBoxImage.Warning);
            return;
        }

        if (!ConfirmTargetedAction(
                "Rename USB",
                selectedUsbTarget,
                $"This will rename only the selected USB volume from '{selectedUsbTarget.LabelDisplay}' to '{newLabel}'. It will not format or benchmark the drive."))
        {
            return;
        }

        await RunScriptAsync(
            ScriptActionType.RenameUsb,
            new PowerShellRunRequest
            {
                DisplayName = "Rename USB",
                WorkingDirectory = AppContext.BaseDirectory,
                InlineCommand = BuildRenameUsbCommand(selectedUsbTarget.RootPath, newLabel)
            });

        await RefreshUsbTargetsAsync();
    }

    private async Task RunInstallOrUpdateVentoyAsync()
    {
        if (!TryGetValidatedSelectedTarget("Install / Update Ventoy", out var selectedUsbTarget))
        {
            return;
        }

        if (!ConfirmTargetedAction(
                "Install / Update Ventoy",
                selectedUsbTarget,
                "This downloads the official Ventoy package from the manifest-defined source, verifies the pinned SHA-256, extracts it to a local operator cache, and launches Ventoy2Disk. The actual install/update still happens manually inside Ventoy2Disk and may repartition the selected USB."))
        {
            return;
        }

        ClearLogs();
        LastCommandText = "Install / Update Ventoy -> official package + Ventoy2Disk";
        var startedAt = DateTimeOffset.Now;
        AppendLifecycleStart("Install / Update Ventoy", selectedUsbTarget);
        AppendLog(new LogLine(DateTimeOffset.Now, $"[INFO] Working directory: {_backendContext.WorkingDirectory}", LogSeverity.Info));
        AppendLog(new LogLine(DateTimeOffset.Now, $"[INFO] Target USB: {selectedUsbTarget.RootPath} ({selectedUsbTarget.LabelDisplay})", LogSeverity.Info));
        AppendLog(new LogLine(DateTimeOffset.Now, "[WARN] Ventoy install/update may modify partitions", LogSeverity.Warning));
        AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] Preparing USB for Ventoy...", LogSeverity.Info));
        AppendLog(new LogLine(DateTimeOffset.Now, VentoyPackageText, LogSeverity.Info));

        SetStatus(
            "Preparing official Ventoy package",
            "Downloading, verifying, and extracting the official Ventoy package before launching Ventoy2Disk.",
            RunningBackground,
            RunningBorder,
            RunningForeground);

        IsBusy = true;
        try
        {
            var result = await _ventoyIntegrationService.InstallOrUpdateAsync(_backendContext, selectedUsbTarget, AppendLog);
            await RefreshVentoyStatusAsync();

            if (result.Succeeded)
            {
                AppendLog(new LogLine(DateTimeOffset.Now, "[OK] Ventoy install/update complete", LogSeverity.Success));
                AppendLifecycleComplete("Install / Update Ventoy", startedAt);
                SetStatus(
                    result.Summary,
                    result.Details,
                    WarningBackground,
                    WarningBorder,
                    WarningForeground);
            }
            else
            {
                AppendLifecycleFailure("Install / Update Ventoy", result.Details);
                SetStatus(
                    result.Summary,
                    result.Details,
                    ErrorBackground,
                    ErrorBorder,
                    ErrorForeground);
            }
        }
        catch (Exception exception)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, exception.Message, LogSeverity.Error, isErrorStream: true));
            AppendLifecycleFailure("Install / Update Ventoy", exception.Message);
            SetStatus(
                "Ventoy package preparation failed",
                exception.Message,
                ErrorBackground,
                ErrorBorder,
                ErrorForeground);
        }
        finally
        {
            IsBusy = false;
            ResetProgressSoon();
        }
    }

    private async Task AutoBenchmarkSelectedUsbSafeAsync()
    {
        var target = SelectedUsbTarget;
        _benchmarkRequestId++;

        if (target is null)
        {
            return;
        }

        var benchmarkKey = GetBenchmarkCacheKey(target.RootPath);
        if (!_benchmarksInProgress.Add(benchmarkKey))
        {
            return;
        }

        if (!UsbTargetSafety.IsSafeForBenchmark(target, out var blockReason))
        {
            ApplyBenchmarkResult(target, new UsbBenchmarkResult
            {
                Succeeded = false,
                Summary = "Benchmark skipped",
                Details = blockReason,
                ReadSpeedDisplay = "Skipped (unsafe)",
                WriteSpeedDisplay = "Skipped (unsafe)"
            });
            _benchmarksInProgress.Remove(benchmarkKey);
            return;
        }

        RaiseCommandStates();
        var cachedReadSpeed = string.IsNullOrWhiteSpace(target.ReadSpeedDisplay) || target.ReadSpeedDisplay.Equals("Not tested", StringComparison.OrdinalIgnoreCase)
            ? "Testing..."
            : target.ReadSpeedDisplay;
        var cachedWriteSpeed = string.IsNullOrWhiteSpace(target.WriteSpeedDisplay) || target.WriteSpeedDisplay.Equals("Not tested", StringComparison.OrdinalIgnoreCase)
            ? "Testing..."
            : target.WriteSpeedDisplay;
        ApplyBenchmarkResult(target, new UsbBenchmarkResult
        {
            Succeeded = false,
            Status = "Testing",
            Summary = "Benchmark testing",
            Details = "USB speed test is running.",
            ReadSpeedDisplay = cachedReadSpeed,
            WriteSpeedDisplay = cachedWriteSpeed,
            TestSizeMb = target.BenchmarkTestSizeMb,
            LastTestedAt = target.BenchmarkLastTestedAt
        });
        AppendLog(new LogLine(DateTimeOffset.Now, $"[INFO] Automatic USB speed check started for {target.RootPath}.", LogSeverity.Info));

        try
        {
            var result = await _usbBenchmarkService.RunSequentialBenchmarkAsync(target, AppendLog);
            ApplyBenchmarkResult(target, result);
            if (result.Succeeded)
            {
                AppendLog(new LogLine(DateTimeOffset.Now, $"[OK] Automatic USB speed check finished for {target.RootPath}: write {result.WriteSpeedDisplay}, read {result.ReadSpeedDisplay}.", LogSeverity.Success));
            }
            else if (result.Summary.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                     result.Status.Contains("failed", StringComparison.OrdinalIgnoreCase))
            {
                AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Automatic USB speed check failed for {target.RootPath}.", LogSeverity.Warning));
            }
        }
        catch (Exception exception)
        {
            ApplyBenchmarkResult(target, new UsbBenchmarkResult
            {
                Succeeded = false,
                Status = "Failed",
                Summary = "Benchmark failed",
                Details = exception.Message,
                ReadSpeedDisplay = "Failed",
                WriteSpeedDisplay = "Failed",
                LastTestedAt = DateTimeOffset.Now
            });
            AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Automatic USB speed check failed for {target.RootPath}.", LogSeverity.Warning));
        }
        finally
        {
            _benchmarksInProgress.Remove(benchmarkKey);
            RaiseCommandStates();
        }
    }

    private async Task<ScriptExecutionResult?> RunScriptAsync(ScriptActionType action, PowerShellRunRequest request)
    {
        if (!_backendContext.IsAvailable)
        {
            SetStatus(
                "Backend unavailable",
                "The scripts could not be discovered, so the command cannot run.",
                ErrorBackground,
                ErrorBorder,
                ErrorForeground);
            return null;
        }

        ClearLogs();
        LastCommandText = $"{request.DisplayName} -> {Path.GetFileName(request.ScriptPath ?? "inline command")}";

        var startedAt = DateTimeOffset.Now;
        AppendLifecycleStart(request.DisplayName, SelectedUsbTarget);

        AppendLog(new LogLine(DateTimeOffset.Now, $"[INFO] Working directory: {request.WorkingDirectory}", LogSeverity.Info));
        if (!string.IsNullOrWhiteSpace(request.ScriptPath))
        {
            AppendLog(new LogLine(DateTimeOffset.Now, $"[INFO] Script: {request.ScriptPath}", LogSeverity.Info));
        }

        SetStatus(
            $"Running {request.DisplayName}",
            "Streaming live PowerShell output below.",
            RunningBackground,
            RunningBorder,
            RunningForeground);

        IsBusy = true;
        try
        {
            var runResult = await _powerShellRunnerService.RunAsync(request, AppendLog);
            var parsed = _scriptStatusParser.Parse(action, request.DisplayName, runResult);

            await LoadManagedSummaryAsync();
            await RefreshVentoyStatusAsync();

            if (parsed.Succeeded)
            {
                SetStatus(
                    parsed.Summary,
                    parsed.Details,
                    parsed.HasWarnings ? WarningBackground : ReadyBackground,
                    parsed.HasWarnings ? WarningBorder : ReadyBorder,
                    parsed.HasWarnings ? WarningForeground : ReadyForeground);
                AppendLifecycleComplete(request.DisplayName, startedAt);
            }
            else
            {
                AppendLifecycleFailure(request.DisplayName, parsed.Summary);
                SetStatus(
                    parsed.Summary,
                    parsed.Details,
                    ErrorBackground,
                    ErrorBorder,
                    ErrorForeground);
            }

            return parsed;
        }
        catch (Exception exception)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, exception.Message, LogSeverity.Error, isErrorStream: true));
            AppendLifecycleFailure(request.DisplayName, exception.Message);
            SetStatus(
                $"{request.DisplayName} failed to start",
                exception.Message,
                ErrorBackground,
                ErrorBorder,
                ErrorForeground);
            return null;
        }
        finally
        {
            IsBusy = false;
            ResetProgressSoon();
        }
    }

    private void LoadSystemIntelligenceReport()
    {
        var reportPath = Path.Combine(GetRuntimeReportsDirectory(), "system-intelligence-latest.json");
        if (!File.Exists(reportPath))
        {
            SystemIntelligenceReportPathText = $"Report: not found at {reportPath}";
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            var root = document.RootElement;
            var overallStatus = GetJsonString(root, "overallStatus", "UNKNOWN");
            SystemIntelligenceStatusText = overallStatus;
            SystemIntelligenceLastScanText = $"Last scan: {FormatGeneratedUtc(GetJsonString(root, "generatedUtc", string.Empty))}";
            ApplyStatusBrushes(
                overallStatus,
                (background, border, foreground) =>
                {
                    SystemIntelligenceStatusBackground = background;
                    SystemIntelligenceStatusBorderBrush = border;
                    SystemIntelligenceStatusForeground = foreground;
                });

            if (root.TryGetProperty("summary", out var summary))
            {
                var computerName = GetJsonString(summary, "computerName", "Unknown PC");
                var model = $"{GetJsonString(summary, "manufacturer", "Unknown")} {GetJsonString(summary, "model", string.Empty)}".Trim();
                var os = GetJsonString(summary, "os", "Unknown OS");
                var osBuild = GetJsonString(summary, "osBuild", "UNKNOWN");
                var bios = GetJsonString(summary, "bios", "UNKNOWN");
                var biosDate = GetJsonString(summary, "biosDate", "UNKNOWN");
                var secureBoot = FormatNullableBool(GetJsonNullableBool(summary, "secureBoot"));
                var tpmPresent = FormatNullableBool(GetJsonNullableBool(summary, "tpmPresent"));
                var tpmReady = FormatNullableBool(GetJsonNullableBool(summary, "tpmReady"));
                var serviceTag = GetJsonString(summary, "serviceTag", "UNKNOWN");
                var licenseChannel = GetJsonString(summary, "windowsLicenseChannel", "UNKNOWN");
                var uptime = GetJsonString(summary, "uptime", "UNKNOWN");
                var lastBoot = GetJsonString(summary, "lastBoot", "UNKNOWN");
                var cpu = GetJsonString(summary, "cpu", "Unknown CPU");
                var cores = GetJsonString(summary, "cpuCores", "UNKNOWN");
                var logicalProcessors = GetJsonString(summary, "cpuLogicalProcessors", "UNKNOWN");
                var baseClock = GetJsonString(summary, "cpuBaseClockMhz", "UNKNOWN");
                var maxClock = GetJsonString(summary, "cpuMaxClockMhz", "UNKNOWN");
                var ram = $"{GetJsonString(summary, "ramTotal", "Unknown RAM")} total, {GetJsonString(summary, "ramFree", "unknown")} free";
                var ramUsed = GetJsonString(summary, "ramUsed", "UNKNOWN");
                var ramUsedPercent = GetJsonString(summary, "ramUsedPercent", "UNKNOWN");
                var ramSpeed = GetJsonString(summary, "ramSpeed", "UNKNOWN");
                var ramSlots = $"{GetJsonString(summary, "ramSlotsUsed", "UNKNOWN")}/{GetJsonString(summary, "ramSlotsTotal", "UNKNOWN")}";
                var ramUpgradePath = GetJsonString(summary, "ramUpgradePath", "UNKNOWN");
                var gpus = GetJsonGpuDisplayArray(summary, "gpus");
                SystemIntelligenceSummaryText = $"{computerName} | {model} | {os} | {cpu} | RAM: {ram} | GPU: {FormatList(gpus, "Unknown GPU")}";
                SystemIntelligenceSystemCardText =
                    $"PC: {computerName}{Environment.NewLine}" +
                    $"Model: {model}{Environment.NewLine}" +
                    $"Service tag / serial: {serviceTag}{Environment.NewLine}" +
                    $"Windows: {os} (build {osBuild}){Environment.NewLine}" +
                    $"License channel: {licenseChannel}{Environment.NewLine}" +
                    $"BIOS: {bios} ({biosDate}){Environment.NewLine}" +
                    $"Secure Boot: {secureBoot}{Environment.NewLine}" +
                    $"TPM: Present {tpmPresent}, Ready {tpmReady}{Environment.NewLine}" +
                    $"Last boot: {lastBoot}{Environment.NewLine}" +
                    $"Uptime: {uptime}";
                SystemIntelligenceComputeCardText =
                    $"CPU: {cpu}{Environment.NewLine}" +
                    $"Cores / threads: {cores} / {logicalProcessors}{Environment.NewLine}" +
                    $"Clock: base {baseClock} MHz, max {maxClock} MHz{Environment.NewLine}" +
                    $"RAM: {ram}; used {ramUsed} ({ramUsedPercent}%); speed {ramSpeed}{Environment.NewLine}" +
                    $"RAM slots: {ramSlots}; {ramUpgradePath}{Environment.NewLine}" +
                    $"GPU: {FormatList(gpus, "UNKNOWN")}";
            }

            SystemIntelligenceDiskHealthText = BuildDiskHealthSummary(root);
            SystemIntelligenceBatteryText = BuildBatterySummary(root);
            SystemIntelligenceNetworkText = BuildNetworkSummary(root);
            SystemIntelligenceSecurityText = BuildSecuritySummary(root);
            SystemIntelligenceStorageCardText = SystemIntelligenceDiskHealthText;
            SystemIntelligenceBatteryCardText = SystemIntelligenceBatteryText;
            SystemIntelligenceNetworkCardText = SystemIntelligenceNetworkText;
            SystemIntelligenceSecurityCardText = SystemIntelligenceSecurityText;
            SystemIntelligenceFlipValueCardText = BuildFlipValueSummary(root);
            SystemIntelligenceReportPathText = $"Report: {reportPath}";
            RefreshCopilotContextText(root);

            SystemIntelligenceRecommendations.Clear();
            if (root.TryGetProperty("recommendations", out var recommendations) &&
                recommendations.ValueKind == JsonValueKind.Array)
            {
                foreach (var recommendation in recommendations.EnumerateArray())
                {
                    var value = recommendation.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        SystemIntelligenceRecommendations.Add(value);
                    }
                }
            }

            if (SystemIntelligenceRecommendations.Count == 0)
            {
                SystemIntelligenceRecommendations.Add("No urgent issues found.");
            }
        }
        catch (Exception exception)
        {
            SystemIntelligenceStatusText = "UNKNOWN";
            SystemIntelligenceReportPathText = $"Report parse failed: {exception.Message}";
            ApplyStatusBrushes(
                "UNKNOWN",
                (background, border, foreground) =>
                {
                    SystemIntelligenceStatusBackground = background;
                    SystemIntelligenceStatusBorderBrush = border;
                    SystemIntelligenceStatusForeground = foreground;
                });
        }
    }

    private void LoadToolkitHealthReport()
    {
        var reportPath = Path.Combine(GetRuntimeReportsDirectory(), "toolkit-health-latest.json");
        if (!File.Exists(reportPath))
        {
            ToolkitReportPathText = $"Report: not found at {reportPath}";
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            var root = document.RootElement;

            var installed = 0;
            var missing = 0;
            var updates = 0;
            var failed = 0;
            var manual = 0;
            var placeholder = 0;
            var skipped = 0;
            var unknown = 0;
            if (root.TryGetProperty("summary", out var summary))
            {
                installed = GetJsonInt(summary, "installed");
                missing = summary.TryGetProperty("missingRequired", out _)
                    ? GetJsonInt(summary, "missingRequired")
                    : GetJsonInt(summary, "missing");
                updates = GetJsonInt(summary, "updates");
                failed = GetJsonInt(summary, "failed");
                manual = GetJsonInt(summary, "manual");
                placeholder = GetJsonInt(summary, "placeholder");
                skipped = GetJsonInt(summary, "skipped");
                unknown = GetJsonInt(summary, "unknown");
            }

            ToolkitInstalledCountText = $"Installed {installed}";
            ToolkitMissingCountText = $"Missing Required {missing}";
            ToolkitUpdatesCountText = $"Updates {updates}";
            ToolkitFailedCountText = $"Failed {failed}";
            ToolkitManualCountText = $"Manual {manual}";
            ToolkitPlaceholderCountText = $"Skipped/Placeholder {skipped + placeholder}";
            var healthVerdict = GetJsonString(root, "healthVerdict", "UNKNOWN");
            ToolkitHealthVerdictText = $"Health Verdict: {healthVerdict}";
            ToolkitLastScanText = $"Last scan: {FormatGeneratedUtc(GetJsonString(root, "generatedUtc", string.Empty))}";
            ToolkitManualExplanationText = GetJsonString(root, "manualItemsExplanation", ToolkitManualExplanationText);

            var status = healthVerdict;
            ToolkitStatusText = status;
            ApplyStatusBrushes(
                status,
                (background, border, foreground) =>
                {
                    ToolkitStatusBackground = background;
                    ToolkitStatusBorderBrush = border;
                    ToolkitStatusForeground = foreground;
                });

            _allToolkitHealthItems.Clear();
            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    var expectedPath = GetJsonString(item, "expectedPath", GetJsonString(item, "destination", string.Empty));
                    var matchedPath = GetJsonString(item, "matchedPath", GetJsonString(item, "path", string.Empty));
                    var expectedFoundPath = string.IsNullOrWhiteSpace(matchedPath)
                        ? expectedPath
                        : $"{expectedPath} | Found: {matchedPath}";

                    _allToolkitHealthItems.Add(new ToolkitHealthItemView
                    {
                        Tool = GetJsonString(item, "tool", "Unknown tool"),
                        Category = GetJsonString(item, "category", "General"),
                        Status = GetJsonString(item, "status", "UNKNOWN"),
                        Type = GetJsonString(item, "type", "UNKNOWN"),
                        ExpectedPath = expectedPath,
                        ExpectedFoundPath = expectedFoundPath,
                        MatchedPath = matchedPath,
                        Url = GetJsonString(item, "url", string.Empty),
                        ClassificationReason = GetJsonString(item, "classificationReason", string.Empty),
                        Version = GetJsonString(item, "version", "Unknown"),
                        Verification = GetJsonString(item, "verification", string.Empty),
                        Recommendation = GetJsonString(item, "recommendation", string.Empty)
                    });
                }
            }

            SelectedToolkitHealthItem = _allToolkitHealthItems.FirstOrDefault();
            ApplyToolkitFilter();
            ToolkitReportPathText = $"Report: {reportPath}";
        }
        catch (Exception exception)
        {
            ToolkitStatusText = "UNKNOWN";
            ToolkitReportPathText = $"Report parse failed: {exception.Message}";
            ApplyStatusBrushes(
                "UNKNOWN",
                (background, border, foreground) =>
                {
                    ToolkitStatusBackground = background;
                    ToolkitStatusBorderBrush = border;
                    ToolkitStatusForeground = foreground;
                });
        }
    }

    private void ApplyToolkitFilter()
    {
        ToolkitHealthItems.Clear();
        foreach (var item in _allToolkitHealthItems.Where(ShouldShowToolkitItem))
        {
            ToolkitHealthItems.Add(item);
        }

        if (SelectedToolkitHealthItem is null || !ToolkitHealthItems.Contains(SelectedToolkitHealthItem))
        {
            SelectedToolkitHealthItem = ToolkitHealthItems.FirstOrDefault();
        }
    }

    private bool ShouldShowToolkitItem(ToolkitHealthItemView item)
    {
        var statusMatches = SelectedToolkitFilter switch
        {
            "Installed" => string.Equals(item.Status, "INSTALLED", StringComparison.OrdinalIgnoreCase),
            "Required Missing" => string.Equals(item.Status, "MISSING_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(item.Status, "MISSING", StringComparison.OrdinalIgnoreCase),
            "Updates" => string.Equals(item.Status, "UPDATE_AVAILABLE", StringComparison.OrdinalIgnoreCase),
            "Failed" => string.Equals(item.Status, "HASH_FAILED", StringComparison.OrdinalIgnoreCase),
            "Manual" => string.Equals(item.Status, "MANUAL_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(item.Status, "PLACEHOLDER", StringComparison.OrdinalIgnoreCase),
            "Skipped/Placeholder" => string.Equals(item.Status, "SKIPPED", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(item.Status, "PLACEHOLDER", StringComparison.OrdinalIgnoreCase),
            _ => true
        };

        if (!statusMatches)
        {
            return false;
        }

        if (!string.Equals(SelectedToolkitCategoryFilter, "All categories", StringComparison.OrdinalIgnoreCase) &&
            !item.Category.Contains(SelectedToolkitCategoryFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(ToolkitSearchText))
        {
            var haystack = $"{item.Tool} {item.Category} {item.Status} {item.Type} {item.ExpectedFoundPath} {item.Verification} {item.Recommendation}";
            if (!haystack.Contains(ToolkitSearchText, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private string ResolveBackendScriptPath(string relativeScriptPath)
    {
        if (string.IsNullOrWhiteSpace(_backendContext.RootPath))
        {
            return relativeScriptPath;
        }

        var repoModePath = Path.Combine(_backendContext.RootPath, "backend", relativeScriptPath);
        if (File.Exists(repoModePath))
        {
            return repoModePath;
        }

        return Path.Combine(_backendContext.RootPath, relativeScriptPath);
    }

    private string ResolveManifestPath()
    {
        foreach (var path in new[]
        {
            _backendContext.RepoManifestPath,
            _backendContext.PrimaryManifestPath,
            Path.Combine(_backendContext.RootPath, "manifests", "ForgerEMS.updates.json")
        })
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                return path;
            }
        }

        return _backendContext.RepoManifestPath;
    }

    private static string GetRuntimeReportsDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.GetTempPath();
        }

        return Path.Combine(localAppData, "ForgerEMS", "Runtime", "reports");
    }

    private static bool IsRootPath(string path, string expectedRoot)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return string.Equals(root, expectedRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyStatusBrushes(string status, Action<Brush, Brush, Brush> apply)
    {
        switch (status.ToUpperInvariant())
        {
            case "READY":
            case "INSTALLED":
                apply(ReadyBackground, ReadyBorder, ReadyForeground);
                break;
            case "WARNING":
            case "HASH_FAILED":
            case "PARTIAL":
                apply(ErrorBackground, ErrorBorder, ErrorForeground);
                break;
            case "WATCH":
            case "MISSING_REQUIRED":
            case "MISSING":
            case "UPDATE_AVAILABLE":
            case "MANUAL_REQUIRED":
            case "MANUAL ACTION NEEDED":
                apply(WarningBackground, WarningBorder, WarningForeground);
                break;
            default:
                apply(RunningBackground, RunningBorder, RunningForeground);
                break;
        }
    }

    private static string BuildDiskHealthSummary(JsonElement root)
    {
        var status = GetJsonString(root, "diskStatus", "UNKNOWN");
        if (!root.TryGetProperty("disks", out var disks) || disks.ValueKind != JsonValueKind.Array || disks.GetArrayLength() == 0)
        {
            return $"Disk health: {status}. No physical disk health counters were available.";
        }

        var parts = disks.EnumerateArray()
            .Select(disk => $"{GetJsonString(disk, "name", "Disk")} | {GetJsonString(disk, "interfaceType", "UNKNOWN")} {GetJsonString(disk, "mediaType", "UNKNOWN")} | {GetJsonString(disk, "size", "UNKNOWN")} | health {GetJsonString(disk, "health", "Unknown")} | temp {GetJsonString(disk, "temperatureC", "UNKNOWN")} C | wear {GetJsonString(disk, "wearPercent", "UNKNOWN")}% ({GetJsonString(disk, "status", "UNKNOWN")})")
            .ToArray();
        var volumeParts = root.TryGetProperty("volumes", out var volumes) && volumes.ValueKind == JsonValueKind.Array
            ? volumes.EnumerateArray()
                .Select(volume => $"{GetJsonString(volume, "drive", "Volume")} {GetJsonString(volume, "free", "UNKNOWN")} free of {GetJsonString(volume, "size", "UNKNOWN")} ({GetJsonString(volume, "status", "UNKNOWN")})")
                .ToArray()
            : [];
        return $"Disk health: {status}. {string.Join("; ", parts.Concat(volumeParts))}";
    }

    private static string BuildBatterySummary(JsonElement root)
    {
        var present = GetJsonBool(root, "batteryPresent");
        if (!present)
        {
            return "Battery: not present.";
        }

        if (!root.TryGetProperty("batteries", out var batteries) || batteries.ValueKind != JsonValueKind.Array)
        {
            return "Battery: present, details unavailable.";
        }

        var parts = batteries.EnumerateArray()
            .Select(battery => $"{GetJsonString(battery, "name", "Battery")} {GetJsonInt(battery, "estimatedChargeRemaining")}% charge, design {GetJsonString(battery, "designCapacity", "UNKNOWN")}, full {GetJsonString(battery, "fullChargeCapacity", "UNKNOWN")}, wear {GetJsonString(battery, "wearPercent", "UNKNOWN")}%, cycles {GetJsonString(battery, "cycleCount", "UNKNOWN")}, AC {FormatNullableBool(GetJsonNullableBool(battery, "acConnected"))} ({GetJsonString(battery, "status", "UNKNOWN")})")
            .ToArray();
        return $"Battery: {FormatList(parts, "present, details unavailable")}";
    }

    private static string BuildNetworkSummary(JsonElement root)
    {
        if (!root.TryGetProperty("network", out var network))
        {
            return "Network: UNKNOWN.";
        }

        var status = GetJsonString(network, "status", "UNKNOWN");
        var internet = GetJsonBool(network, "internetCheck") ? "Pass" : "Check failed";
        if (!network.TryGetProperty("adapters", out var adapters) || adapters.ValueKind != JsonValueKind.Array || adapters.GetArrayLength() == 0)
        {
            return $"Network: {status}. Internet: {internet}. No active adapter detected.";
        }

        var parts = adapters.EnumerateArray()
            .Select(adapter =>
            {
                var ips = adapter.TryGetProperty("ipAddresses", out var ipArray) && ipArray.ValueKind == JsonValueKind.Array
                    ? FormatList(ipArray.EnumerateArray().Select(item => item.GetString() ?? string.Empty), "no IP")
                    : "no IP";
                var gateways = adapter.TryGetProperty("gateways", out var gatewayArray) && gatewayArray.ValueKind == JsonValueKind.Array
                    ? FormatList(gatewayArray.EnumerateArray().Select(item => item.GetString() ?? string.Empty), "no gateway")
                    : "no gateway";
                var dns = adapter.TryGetProperty("dnsServers", out var dnsArray) && dnsArray.ValueKind == JsonValueKind.Array
                    ? FormatList(dnsArray.EnumerateArray().Select(item => item.GetString() ?? string.Empty), "no DNS")
                    : "no DNS";
                var wifiSignal = GetJsonString(adapter, "wifiSignalPercent", "UNKNOWN");
                var apipa = FormatNullableBool(GetJsonNullableBool(adapter, "apipaDetected"));
                return $"{GetJsonString(adapter, "description", "Adapter")} | IP {ips} | GW {gateways} | DNS {dns} | Wi-Fi signal {wifiSignal}% | APIPA {apipa}";
            })
            .Take(3)
            .ToArray();
        return $"Network: {status}. Internet: {internet}. {FormatList(parts, "No active adapter detected")}";
    }

    private static string BuildSecuritySummary(JsonElement root)
    {
        if (!root.TryGetProperty("security", out var security))
        {
            return "Security: UNKNOWN.";
        }

        var status = GetJsonString(security, "status", "UNKNOWN");
        var avEnabled = GetJsonNullableBool(security, "antivirusEnabled");
        var realtime = GetJsonNullableBool(security, "realTimeProtectionEnabled");
        var firewall = GetJsonNullableBool(security, "firewallEnabled");
        var products = GetJsonStringArray(security, "avProducts");
        var bitLocker = security.TryGetProperty("bitLockerVolumes", out var bitLockerVolumes) && bitLockerVolumes.ValueKind == JsonValueKind.Array
            ? FormatList(bitLockerVolumes.EnumerateArray().Select(volume => $"{GetJsonString(volume, "mountPoint", "Volume")} {GetJsonString(volume, "protectionStatus", "UNKNOWN")}"), "unavailable")
            : "unavailable";
        return $"Security: {status}. Defender AV: {FormatNullableBool(avEnabled)}. Real-time: {FormatNullableBool(realtime)}. Firewall: {FormatNullableBool(firewall)}. Registered AV: {FormatList(products, "none detected")}. BitLocker: {bitLocker}";
    }

    private static string BuildFlipValueSummary(JsonElement root)
    {
        if (!root.TryGetProperty("flipValue", out var flipValue))
        {
            return "Flip Value: run the updated System Scan to generate local resale guidance.";
        }

        var estimateType = GetJsonString(flipValue, "estimateType", "local estimate only");
        var range = GetJsonString(flipValue, "estimatedResaleRange", "UNKNOWN");
        var list = GetJsonString(flipValue, "recommendedListPrice", "UNKNOWN");
        var quick = GetJsonString(flipValue, "quickSalePrice", "UNKNOWN");
        var parts = GetJsonString(flipValue, "partsRepairPrice", "UNKNOWN");
        var confidence = GetJsonString(flipValue, "confidenceScore", "UNKNOWN");
        var providerStatus = GetJsonString(flipValue, "providerStatus", "Pricing provider not configured");
        var title = GetJsonString(flipValue, "suggestedListingTitle", "UNKNOWN");
        var drivers = flipValue.TryGetProperty("valueDrivers", out var driverArray) && driverArray.ValueKind == JsonValueKind.Array
            ? FormatList(driverArray.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Take(3), "none")
            : "none";
        var reducers = flipValue.TryGetProperty("valueReducers", out var reducerArray) && reducerArray.ValueKind == JsonValueKind.Array
            ? FormatList(reducerArray.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Take(3), "none")
            : "none";

        return
            $"Estimate type: {estimateType}{Environment.NewLine}" +
            $"Range: {range}; list {list}; quick-sale {quick}; parts/repair {parts}{Environment.NewLine}" +
            $"Confidence: {confidence}; providers: {providerStatus}{Environment.NewLine}" +
            $"Drivers: {drivers}{Environment.NewLine}" +
            $"Reducers: {reducers}{Environment.NewLine}" +
            $"Listing title: {title}";
    }

    private void RefreshCopilotContextText()
    {
        var reportPath = Path.Combine(GetRuntimeReportsDirectory(), "system-intelligence-latest.json");
        if (!File.Exists(reportPath))
        {
            CopilotContextText = BuildCopilotUsbContext("System scan: not loaded");
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            RefreshCopilotContextText(document.RootElement);
        }
        catch
        {
            CopilotContextText = BuildCopilotUsbContext("System scan: parse failed");
        }
    }

    private void RefreshCopilotContextText(JsonElement root)
    {
        var summaryText = "System scan: loaded";
        if (root.TryGetProperty("summary", out var summary))
        {
            summaryText =
                $"Model: {GetJsonString(summary, "manufacturer", "Unknown")} {GetJsonString(summary, "model", string.Empty)}{Environment.NewLine}" +
                $"CPU: {GetJsonString(summary, "cpu", "Unknown CPU")}{Environment.NewLine}" +
                $"RAM: {GetJsonString(summary, "ramTotal", "Unknown RAM")} @ {GetJsonString(summary, "ramSpeed", "UNKNOWN")}{Environment.NewLine}" +
                $"GPU: {FormatList(GetJsonGpuDisplayArray(summary, "gpus"), "Unknown GPU")}{Environment.NewLine}" +
                $"Battery: {SystemIntelligenceBatteryText}{Environment.NewLine}" +
                $"Storage: {SystemIntelligenceDiskHealthText}";
        }

        CopilotContextText = BuildCopilotUsbContext(summaryText);
    }

    private string BuildCopilotUsbContext(string systemContext)
    {
        var usbContext = SelectedUsbTarget is null
            ? "Selected USB target: none"
            : $"Selected USB target: {SelectedUsbTarget.RootPath} {SelectedUsbTarget.LabelDisplay}; {SelectedUsbTarget.DisplayTotalBytes}; write {SelectedUsbTarget.WriteSpeedDisplayNormalized}; read {SelectedUsbTarget.ReadSpeedDisplayNormalized}; benchmark {SelectedUsbTarget.BenchmarkStatusDisplay}";

        return $"{systemContext}{Environment.NewLine}{usbContext}";
    }

    private static string GetJsonString(JsonElement element, string propertyName, string fallback)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return fallback;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? fallback,
            JsonValueKind.Number => property.ToString(),
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            _ => fallback
        };
    }

    private static int GetJsonInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
        {
            return value;
        }

        return 0;
    }

    private static bool GetJsonBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind == JsonValueKind.True ||
               (property.ValueKind == JsonValueKind.String && bool.TryParse(property.GetString(), out var parsed) && parsed);
    }

    private static bool? GetJsonNullableBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        return null;
    }

    private static string[] GetJsonStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToArray();
    }

    private static string[] GetJsonGpuDisplayArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Select(item =>
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    return item.GetString() ?? string.Empty;
                }

                if (item.ValueKind == JsonValueKind.Object)
                {
                    var name = GetJsonString(item, "name", "UNKNOWN GPU");
                    var driver = GetJsonString(item, "driverVersion", "UNKNOWN");
                    return $"{name} (driver {driver})";
                }

                return string.Empty;
            })
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }

    private static string FormatGeneratedUtc(string value)
    {
        return DateTimeOffset.TryParse(value, out var parsed)
            ? $"{parsed.LocalDateTime:yyyy-MM-dd HH:mm:ss}"
            : "UNKNOWN";
    }

    private static string FormatList(IEnumerable<string> values, string emptyText)
    {
        var normalized = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return normalized.Length == 0 ? emptyText : string.Join("; ", normalized);
    }

    private static string FormatNullableBool(bool? value)
    {
        return value.HasValue ? (value.Value ? "Enabled" : "Disabled") : "Unknown";
    }

    private async Task LoadManagedSummaryAsync()
    {
        var summary = await _managedDownloadSummaryService.TryLoadAsync(_backendContext);
        ManagedSummaryText = string.IsNullOrWhiteSpace(summary.Text)
            ? "Managed-download summary is empty."
            : summary.Text;
        ManagedSummaryPathText = summary.IsAvailable
            ? $"Summary source: {summary.SummaryPath}"
            : "Summary source: not detected";
        ManagedSummaryUpdatedText = summary.LastUpdatedUtc.HasValue
            ? $"Updated: {summary.LastUpdatedUtc.Value:yyyy-MM-dd HH:mm:ss} UTC"
            : "Updated: n/a";

        var normalized = summary.Text ?? string.Empty;
        if (!summary.IsAvailable)
        {
            SetManagedSummaryStatus("No snapshot", WarningBackground, WarningBorder, WarningForeground);
        }
        else if (normalized.Contains("DRIFT", StringComparison.OrdinalIgnoreCase))
        {
            SetManagedSummaryStatus("DRIFT", ErrorBackground, ErrorBorder, ErrorForeground);
        }
        else if (normalized.Contains("OK-LIMITED", StringComparison.OrdinalIgnoreCase))
        {
            SetManagedSummaryStatus("OK-LIMITED", WarningBackground, WarningBorder, WarningForeground);
        }
        else if (normalized.Contains("OK", StringComparison.OrdinalIgnoreCase))
        {
            SetManagedSummaryStatus("OK", ReadyBackground, ReadyBorder, ReadyForeground);
        }
        else
        {
            SetManagedSummaryStatus("Loaded", RunningBackground, RunningBorder, RunningForeground);
        }
    }

    private async Task RefreshVentoyStatusAsync()
    {
        var requestId = ++_ventoyStatusRequestId;
        var status = await _ventoyIntegrationService.GetStatusAsync(_backendContext, SelectedUsbTarget);
        if (requestId != _ventoyStatusRequestId)
        {
            return;
        }

        ApplyVentoyStatus(status);
    }

    private async Task RefreshVentoyStatusSafeAsync()
    {
        try
        {
            await RefreshVentoyStatusAsync();
        }
        catch
        {
            ApplyVentoyStatus(new VentoyStatusInfo
            {
                HasTarget = SelectedUsbTarget is not null,
                StatusText = "Ventoy status unavailable",
                DetailText = "Ventoy detection could not be refreshed for the selected USB.",
                PackageText = "Official Ventoy package source status is unavailable."
            });
        }
    }

    private bool ConfirmTargetedAction(string actionName, UsbTargetInfo target, string actionWarning)
    {
        var executionBlockReason = UsbTargetSafety.GetExecutionBlockReason(target);
        if (!string.IsNullOrWhiteSpace(executionBlockReason))
        {
            SetStatus(
                "USB target blocked",
                executionBlockReason,
                ErrorBackground,
                ErrorBorder,
                ErrorForeground);

            AppendLog(new LogLine(DateTimeOffset.Now, executionBlockReason, LogSeverity.Error, isErrorStream: true));
            _userPromptService.ShowMessage("USB target blocked", executionBlockReason, MessageBoxImage.Error);
            return false;
        }

        var message =
            $"{actionName} for {target.RootPath} ({target.LabelDisplay})?{Environment.NewLine}{Environment.NewLine}" +
            $"Drive type: {target.DriveType} / {target.BusTypeDisplay}{Environment.NewLine}" +
            $"Device: {target.DeviceIdentityDisplay}{Environment.NewLine}" +
            $"Role: {target.RoleDisplay}{Environment.NewLine}" +
            $"Total size: {target.DisplayTotalBytes}{Environment.NewLine}" +
            $"Free space: {target.DisplayFreeBytes}{Environment.NewLine}{Environment.NewLine}" +
            $"{actionWarning}";

        return _userPromptService.Confirm(actionName, message);
    }

    private bool TryGetValidatedSelectedTarget(string actionName, out UsbTargetInfo target)
    {
        target = SelectedUsbTarget!;
        if (SelectedUsbTarget is null)
        {
            return false;
        }

        var executionBlockReason = UsbTargetSafety.GetExecutionBlockReason(SelectedUsbTarget);
        if (string.IsNullOrWhiteSpace(executionBlockReason))
        {
            target = SelectedUsbTarget;
            return true;
        }

        SetStatus(
            "USB target blocked",
            executionBlockReason,
            ErrorBackground,
            ErrorBorder,
            ErrorForeground);

        AppendLog(new LogLine(DateTimeOffset.Now, $"{actionName} blocked: {executionBlockReason}", LogSeverity.Error, isErrorStream: true));
        _userPromptService.ShowMessage("USB target blocked", executionBlockReason, MessageBoxImage.Error);
        return false;
    }

    private void ApplyVentoyStatus(VentoyStatusInfo status)
    {
        VentoyStatusText = status.StatusText;
        VentoyPackageText = status.PackageText;

        var detail = status.DetailText;
        if (status.IsInstalled)
        {
            detail = string.IsNullOrWhiteSpace(status.InstalledVersion) || string.Equals(status.InstalledVersion, "Unknown", StringComparison.OrdinalIgnoreCase)
                ? $"{detail} Installed version was not detectable from the media."
                : $"{detail} Installed version: {status.InstalledVersion}.";

            VentoyDetailText = detail.Trim();
            SetVentoyStatus(status.StatusText, ReadyBackground, ReadyBorder, ReadyForeground);
            return;
        }

        if (!status.HasTarget)
        {
            VentoyDetailText = detail;
            SetVentoyStatus(status.StatusText, RunningBackground, RunningBorder, RunningForeground);
            return;
        }

        var packageVersionNote = string.IsNullOrWhiteSpace(status.PackageVersion)
            ? "Package version: unavailable."
            : $"Package version available: {status.PackageVersion}.";
        VentoyDetailText = $"{detail} {packageVersionNote}".Trim();

        if (status.PackageAvailable)
        {
            SetVentoyStatus(status.StatusText, WarningBackground, WarningBorder, WarningForeground);
        }
        else
        {
            SetVentoyStatus(status.StatusText, ErrorBackground, ErrorBorder, ErrorForeground);
        }
    }

    private void UpdateTargetWarnings()
    {
        if (SelectedUsbTarget is null)
        {
            TargetWarningText = "Select a USB target to review safety notes and Ventoy status.";
            ActionWarningText = "Setup USB, Update USB, and Ventoy actions stay disabled until a valid USB target is selected.";
            SetTargetWarningVisuals(RunningBackground, RunningBorder, RunningForeground);
            return;
        }

        if (!SelectedUsbTarget.IsSelectable)
        {
            TargetWarningText = SelectedUsbTarget.SelectionWarningDisplay;
            ActionWarningText = "This target is blocked. ForgerEMS will not run target-specific actions against an EFI or other clearly unsafe USB partition.";
            SetTargetWarningVisuals(ErrorBackground, ErrorBorder, ErrorForeground);
            return;
        }

        var executionBlockReason = UsbTargetSafety.GetExecutionBlockReason(SelectedUsbTarget);
        if (!string.IsNullOrWhiteSpace(executionBlockReason))
        {
            TargetWarningText = executionBlockReason;
            ActionWarningText = "This target is blocked. You selected a boot partition, not the main USB storage.";
            SetTargetWarningVisuals(ErrorBackground, ErrorBorder, ErrorForeground);
            return;
        }

        if (!SelectedUsbTarget.IsRemovableMedia)
        {
            TargetWarningText = SelectedUsbTarget.SelectionWarningDisplay;
            ActionWarningText = "Selected target is a fixed USB disk. Treat Setup USB, Update USB, and Install / Update Ventoy as destructive operations and confirm the drive letter carefully.";
            SetTargetWarningVisuals(WarningBackground, WarningBorder, WarningForeground);
            return;
        }

        TargetWarningText = SelectedUsbTarget.SelectionWarningDisplay;
        ActionWarningText = "Setup USB, Update USB, and Install / Update Ventoy can change the selected USB. Confirm the drive letter and label before continuing.";
        SetTargetWarningVisuals(WarningBackground, WarningBorder, WarningForeground);
    }

    private void AppendUsbDetectionDiagnostics(IReadOnlyList<string> diagnostics)
    {
        foreach (var diagnostic in diagnostics.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            var severity = diagnostic.Contains("excluded", StringComparison.OrdinalIgnoreCase)
                ? LogSeverity.Warning
                : LogSeverity.Info;

            AppendLog(new LogLine(DateTimeOffset.Now, NormalizeLogPrefix(diagnostic, severity), severity));
        }
    }

    private void AppendLifecycleStart(string actionName, UsbTargetInfo? target)
    {
        AppendLog(new LogLine(DateTimeOffset.Now, $"[INIT] ForgerEMS action started: {actionName}", LogSeverity.Info));
        AppendLog(new LogLine(DateTimeOffset.Now, $"[INFO] Frontend version: {_backendContext.FrontendVersion}", LogSeverity.Info));
        AppendLog(new LogLine(DateTimeOffset.Now, $"[INFO] Backend version: {GetBackendVersionDisplay()}", LogSeverity.Info));
        AppendLog(new LogLine(DateTimeOffset.Now, $"[INFO] Backend compatibility: {GetBackendCompatibilityStatus()}", LogSeverity.Info));
        if (target is not null)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, $"[INFO] Target drive: {target.RootPath} {target.LabelDisplay} | {target.DisplayTotalBytes} | {target.DriveType} | {target.BusTypeDisplay}", LogSeverity.Info));
        }
    }

    private void AppendLifecycleComplete(string actionName, DateTimeOffset startedAt)
    {
        AppendLog(new LogLine(DateTimeOffset.Now, $"[COMPLETE] {actionName} completed in {FormatDuration(DateTimeOffset.Now - startedAt)}", LogSeverity.Success));
    }

    private void AppendLifecycleFailure(string actionName, string reason)
    {
        AppendLog(new LogLine(DateTimeOffset.Now, $"[ERROR] {actionName} failed: {reason}", LogSeverity.Error, isErrorStream: true));
        AppendLog(new LogLine(DateTimeOffset.Now, "[ACTION] Review log, verify network, and retry.", LogSeverity.Warning));
    }

    private string GetBackendVersionDisplay()
    {
        return string.IsNullOrWhiteSpace(_backendContext.BackendVersion)
            ? "not detected"
            : _backendContext.BackendVersion;
    }

    private string GetBackendCompatibilityStatus()
    {
        if (!_backendContext.IsAvailable)
        {
            return "Error";
        }

        if (string.IsNullOrWhiteSpace(_backendContext.BackendVersion))
        {
            return "Warning";
        }

        if (_backendContext.DiagnosticMessage.Contains("Status: Warning", StringComparison.OrdinalIgnoreCase))
        {
            return "Warning";
        }

        return "Compatible";
    }

    private void ApplyBenchmarkResult(UsbTargetInfo target, UsbBenchmarkResult result)
    {
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(() => ApplyBenchmarkResult(target, result));
            return;
        }

        if (!string.Equals(result.Status, "Testing", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(result.Status, "Queued", StringComparison.OrdinalIgnoreCase))
        {
            _benchmarkResultsByRoot[GetBenchmarkCacheKey(target.RootPath)] = result;
            SaveBenchmarkCache();
        }
        var replacement = WithBenchmarkResult(target, result);

        var index = UsbTargets.IndexOf(target);
        if (index < 0)
        {
            index = UsbTargets
                .Select((item, itemIndex) => new { item, itemIndex })
                .FirstOrDefault(candidate => string.Equals(candidate.item.RootPath, target.RootPath, StringComparison.OrdinalIgnoreCase))
                ?.itemIndex ?? -1;
        }

        if (index >= 0)
        {
            UsbTargets[index] = replacement;
            SetSelectedUsbTargetWithoutRefresh(replacement);
        }
        else if (SelectedUsbTarget is not null &&
                 string.Equals(SelectedUsbTarget.RootPath, target.RootPath, StringComparison.OrdinalIgnoreCase))
        {
            SetSelectedUsbTargetWithoutRefresh(replacement);
        }

        RefreshCopilotContextText();
    }

    private void SetSelectedUsbTargetWithoutRefresh(UsbTargetInfo target)
    {
        _suppressSelectionRefresh = true;
        try
        {
            SelectedUsbTarget = target;
        }
        finally
        {
            _suppressSelectionRefresh = false;
        }
    }

    private void LoadCopilotSettings()
    {
        CopilotProviderSettings.Clear();
        var settings = new CopilotSettingsStore(_copilotConfigPath, _copilotProviderRegistry).Load();
        _copilotSettings = settings;

        _selectedCopilotMode = ToModeDisplayName(settings.Mode);
        _useLatestSystemScanContext = settings.UseLatestSystemScanContext;
        foreach (var provider in _copilotProviderRegistry.Providers)
        {
            if (!settings.Providers.TryGetValue(provider.Id, out var providerConfig))
            {
                providerConfig = new CopilotProviderConfiguration
                {
                    IsEnabled = provider.EnabledByDefault,
                    BaseUrl = provider.DefaultBaseUrl,
                    ModelName = provider.DefaultModelName,
                    ApiKeyEnvironmentVariable = provider.DefaultApiKeyEnvironmentVariable
                };
            }

            CopilotProviderSettings.Add(new CopilotProviderSettingView
            {
                Id = provider.Id,
                DisplayName = provider.DisplayName,
                Category = provider.Category,
                Status = provider.StatusText,
                IsEnabled = providerConfig.IsEnabled,
                IsConfigured = provider.IsConfigured(providerConfig),
                IsPaidProvider = provider.IsPaidProvider
            });
        }

        UpdateCopilotOnlineIndicator();
    }

    private CopilotSettings BuildCopilotSettingsFromUi()
    {
        var settings = _copilotSettings ?? new CopilotSettings();
        settings.Mode = ToCopilotMode(SelectedCopilotMode);
        settings.ProviderType = CopilotProviderType.LocalOffline;
        settings.TimeoutSeconds = settings.TimeoutSeconds <= 0 ? 12 : settings.TimeoutSeconds;
        settings.OfflineFallbackEnabled = true;
        settings.RedactContextEnabled = true;
        settings.MaxContextCharacters = settings.MaxContextCharacters <= 0 ? 6000 : settings.MaxContextCharacters;
        settings.UseLatestSystemScanContext = UseLatestSystemScanContext;

        foreach (var provider in _copilotProviderRegistry.Providers)
        {
            var view = CopilotProviderSettings.FirstOrDefault(item => string.Equals(item.Id, provider.Id, StringComparison.OrdinalIgnoreCase));
            var isEnabled = view?.IsEnabled == true;
            if (isEnabled && provider.ProviderType != CopilotProviderType.LocalOffline && settings.ProviderType == CopilotProviderType.LocalOffline)
            {
                settings.ProviderType = provider.ProviderType;
            }

            if (!settings.Providers.TryGetValue(provider.Id, out var providerConfig))
            {
                providerConfig = new CopilotProviderConfiguration
                {
                    BaseUrl = provider.DefaultBaseUrl,
                    ModelName = provider.DefaultModelName,
                    ApiKeyEnvironmentVariable = provider.DefaultApiKeyEnvironmentVariable,
                    TimeoutSeconds = settings.TimeoutSeconds,
                    MaxRequestsPerMinute = 12,
                    MaxRetries = provider.IsOnlineProvider ? 1 : 0
                };
                settings.Providers[provider.Id] = providerConfig;
            }

            providerConfig.IsEnabled = isEnabled;
            providerConfig.BaseUrl = string.IsNullOrWhiteSpace(providerConfig.BaseUrl) ? provider.DefaultBaseUrl : providerConfig.BaseUrl;
            providerConfig.ModelName = string.IsNullOrWhiteSpace(providerConfig.ModelName) ? provider.DefaultModelName : providerConfig.ModelName;
            providerConfig.ApiKeyEnvironmentVariable = string.IsNullOrWhiteSpace(providerConfig.ApiKeyEnvironmentVariable)
                ? provider.DefaultApiKeyEnvironmentVariable
                : providerConfig.ApiKeyEnvironmentVariable;
            providerConfig.TimeoutSeconds = providerConfig.TimeoutSeconds <= 0 ? settings.TimeoutSeconds : providerConfig.TimeoutSeconds;
            providerConfig.MaxRequestsPerMinute = providerConfig.MaxRequestsPerMinute <= 0 ? 12 : providerConfig.MaxRequestsPerMinute;
            providerConfig.MaxRetries = providerConfig.MaxRetries < 0 ? 0 : providerConfig.MaxRetries;
        }

        _copilotSettings = settings;
        return settings;
    }

    private void SaveCopilotSettings()
    {
        try
        {
            new CopilotSettingsStore(_copilotConfigPath, _copilotProviderRegistry).Save(BuildCopilotSettingsFromUi());
        }
        catch
        {
            // Copilot preferences are best effort.
        }
    }

    private void UpdateCopilotOnlineIndicator()
    {
        var mode = ToCopilotMode(SelectedCopilotMode);
        if (mode == CopilotMode.OfflineOnly)
        {
            CopilotOnlineStatusText = "Offline Only - no data leaves this machine.";
            CopilotOnlineStatusBackground = ReadyBackground;
            CopilotOnlineStatusBorderBrush = ReadyBorder;
            CopilotOnlineStatusForeground = ReadyForeground;
            return;
        }

        var enabledConfigured = CopilotProviderSettings.Any(item => item.IsEnabled && item.IsConfigured);
        var localOllamaEnabled = CopilotProviderSettings.Any(item => item.IsEnabled && string.Equals(item.Id, "ollama-local", StringComparison.OrdinalIgnoreCase));
        CopilotOnlineStatusText = localOllamaEnabled
            ? "Local Ollama selected - reachability checked on send; offline fallback active."
            : enabledConfigured
                ? "Online lookup enabled - sanitized provider context only."
                : "Online lookup enabled - no configured providers yet; offline fallback active.";
        CopilotOnlineStatusBackground = enabledConfigured ? WarningBackground : RunningBackground;
        CopilotOnlineStatusBorderBrush = enabledConfigured ? WarningBorder : RunningBorder;
        CopilotOnlineStatusForeground = enabledConfigured ? WarningForeground : RunningForeground;
    }

    private void ApplyCopilotOnlineIndicator(CopilotResponse response)
    {
        CopilotOnlineStatusText = response.OnlineStatus;
        if (response.OnlineStatus.Contains("Error", StringComparison.OrdinalIgnoreCase))
        {
            CopilotOnlineStatusBackground = ErrorBackground;
            CopilotOnlineStatusBorderBrush = ErrorBorder;
            CopilotOnlineStatusForeground = ErrorForeground;
            return;
        }

        if (response.UsedOnlineData)
        {
            CopilotOnlineStatusBackground = WarningBackground;
            CopilotOnlineStatusBorderBrush = WarningBorder;
            CopilotOnlineStatusForeground = WarningForeground;
            return;
        }

        UpdateCopilotOnlineIndicator();
        CopilotOnlineStatusText = response.OnlineStatus;
    }

    private static CopilotMode ToCopilotMode(string mode)
    {
        return mode switch
        {
            "Online Assisted" => CopilotMode.OnlineAssisted,
            "Hybrid Auto" => CopilotMode.HybridAuto,
            _ => CopilotMode.OfflineOnly
        };
    }

    private static string ToModeDisplayName(CopilotMode mode)
    {
        return mode switch
        {
            CopilotMode.OnlineAssisted => "Online Assisted",
            CopilotMode.HybridAuto => "Hybrid Auto",
            _ => "Offline Only"
        };
    }

    private UsbTargetInfo ApplyCachedBenchmarkResult(UsbTargetInfo target)
    {
        return _benchmarkResultsByRoot.TryGetValue(GetBenchmarkCacheKey(target.RootPath), out var result)
            ? WithBenchmarkResult(target, result)
            : target;
    }

    private static UsbTargetInfo WithBenchmarkResult(UsbTargetInfo target, UsbBenchmarkResult result)
    {
        return new UsbTargetInfo
        {
            DriveLetter = target.DriveLetter,
            RootPath = target.RootPath,
            Label = target.Label,
            FileSystem = target.FileSystem,
            TotalBytes = target.TotalBytes,
            FreeBytes = target.FreeBytes,
            DriveType = target.DriveType,
            BusType = target.BusType,
            IsLikelyUsb = target.IsLikelyUsb,
            DeviceBrand = target.DeviceBrand,
            DeviceModel = target.DeviceModel,
            ReadSpeedDisplay = result.ReadSpeedDisplay,
            WriteSpeedDisplay = result.WriteSpeedDisplay,
            BenchmarkStatus = string.IsNullOrWhiteSpace(result.Status) ? (result.Succeeded ? "Complete" : "Failed") : result.Status,
            BenchmarkTestSizeMb = result.TestSizeMb,
            BenchmarkLastTestedAt = result.LastTestedAt,
            PartitionType = target.PartitionType,
            IsSystemDrive = target.IsSystemDrive,
            IsBootDrive = target.IsBootDrive,
            IsRemovableMedia = target.IsRemovableMedia,
            IsEfiSystemPartition = target.IsEfiSystemPartition,
            IsUndersizedPartition = target.IsUndersizedPartition,
            HasVentoyCompanionEfiPartition = target.HasVentoyCompanionEfiPartition,
            IsLargeDataPartition = target.IsLargeDataPartition,
            IsPreferredUsbTarget = target.IsPreferredUsbTarget,
            IsSelectable = target.IsSelectable,
            SelectionWarning = target.SelectionWarning,
            ClassificationDetails = target.ClassificationDetails
        };
    }

    private static string GetBenchmarkCacheKey(string rootPath)
    {
        return string.IsNullOrWhiteSpace(rootPath)
            ? string.Empty
            : rootPath.Trim().TrimEnd('\\').ToUpperInvariant();
    }

    private void LoadBenchmarkCache()
    {
        try
        {
            if (!File.Exists(_benchmarkCachePath))
            {
                return;
            }

            var cached = JsonSerializer.Deserialize<Dictionary<string, UsbBenchmarkResult>>(File.ReadAllText(_benchmarkCachePath));
            if (cached is null)
            {
                return;
            }

            foreach (var pair in cached)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) &&
                    pair.Value.LastTestedAt.HasValue &&
                    DateTimeOffset.Now - pair.Value.LastTestedAt.Value < TimeSpan.FromDays(14))
                {
                    _benchmarkResultsByRoot[pair.Key] = pair.Value;
                }
            }
        }
        catch
        {
            // Cache loading should never block USB detection.
        }
    }

    private void SaveBenchmarkCache()
    {
        try
        {
            var stableResults = _benchmarkResultsByRoot
                .Where(pair => pair.Value.LastTestedAt.HasValue &&
                               !string.Equals(pair.Value.Status, "Testing", StringComparison.OrdinalIgnoreCase) &&
                               !string.Equals(pair.Value.Status, "Queued", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

            Directory.CreateDirectory(Path.GetDirectoryName(_benchmarkCachePath)!);
            File.WriteAllText(
                _benchmarkCachePath,
                JsonSerializer.Serialize(stableResults, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Benchmark cache persistence is best effort.
        }
    }

    private void StartUsbAutoDetectionMonitor()
    {
        if (_usbMonitorStarted)
        {
            return;
        }

        _usbMonitorStarted = true;
        _usbMonitorCancellation = new CancellationTokenSource();
        _ = MonitorUsbTargetsAsync(_usbMonitorCancellation.Token);
        AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] Automatic USB detection monitor started.", LogSeverity.Info));
    }

    private async Task MonitorUsbTargetsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested || _isBusy || _refreshingUsbTargets)
                {
                    continue;
                }

                var detectionResult = await _usbDetectionService.GetUsbTargetsAsync(cancellationToken).ConfigureAwait(false);
                var signature = BuildUsbSignature(detectionResult.Targets);
                if (string.Equals(signature, _knownUsbSignature, StringComparison.Ordinal))
                {
                    continue;
                }

                var oldRoots = ParseUsbSignature(_knownUsbSignature);
                var newRoots = ParseUsbSignature(signature);
                var added = newRoots.Except(oldRoots, StringComparer.OrdinalIgnoreCase).ToArray();
                var removed = oldRoots.Except(newRoots, StringComparer.OrdinalIgnoreCase).ToArray();

                if (added.Length > 0)
                {
                    AppendLog(new LogLine(DateTimeOffset.Now, $"[INFO] USB device added: {string.Join(", ", added)}. Waiting for Windows mount to settle.", LogSeverity.Info));
                    await Task.Delay(TimeSpan.FromMilliseconds(1600), cancellationToken).ConfigureAwait(false);
                }

                if (removed.Length > 0)
                {
                    AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] USB device removed: {string.Join(", ", removed)}.", LogSeverity.Warning));
                }

                var refreshTask = await Application.Current.Dispatcher.InvokeAsync(() => RefreshUsbTargetsAsync());
                await refreshTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Automatic USB detection skipped one cycle: {exception.Message}", LogSeverity.Warning));
            }
        }
    }

    private static string BuildUsbSignature(IEnumerable<UsbTargetInfo> targets)
    {
        return string.Join(
            "|",
            targets
                .Where(target => target.IsSelectable)
                .Select(target => $"ROOT={GetBenchmarkCacheKey(target.RootPath)},SIZE={target.TotalBytes},LABEL={target.Label}")
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase));
    }

    private static string[] ParseUsbSignature(string signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return [];
        }

        return signature
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.StartsWith("ROOT=", StringComparison.OrdinalIgnoreCase) ? part[5..].Split(',', 2)[0] : part)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();
    }

    private static string NormalizeLogPrefix(string text, LogSeverity severity)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            return trimmed;
        }

        return severity switch
        {
            LogSeverity.Success => "[OK] " + trimmed,
            LogSeverity.Warning => "[WARN] " + trimmed,
            LogSeverity.Error => "[ERROR] " + trimmed,
            _ => "[INFO] " + trimmed
        };
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";
        }

        if (duration.TotalMinutes >= 1)
        {
            return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        }

        return $"{Math.Max(0, (int)Math.Round(duration.TotalSeconds))}s";
    }

    private static bool TryValidateVolumeLabel(string label, out string error)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            error = "Drive label cannot be blank.";
            return false;
        }

        if (label.Length > 32)
        {
            error = "Drive label must be 32 characters or fewer.";
            return false;
        }

        var invalidCharacters = new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|', ';' };
        if (label.IndexOfAny(invalidCharacters) >= 0 || label.Any(char.IsControl))
        {
            error = "Drive label contains characters Windows does not allow in volume labels.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string BuildRenameUsbCommand(string rootPath, string newLabel)
    {
        return $$"""
            $ErrorActionPreference = 'Stop'
            $root = {{ToSingleQuotedPowerShellLiteral(rootPath)}}
            $newLabel = {{ToSingleQuotedPowerShellLiteral(newLabel)}}
            $driveLetter = ([System.IO.Path]::GetPathRoot($root)).TrimEnd('\', ':')
            if ([string]::IsNullOrWhiteSpace($driveLetter)) {
                throw 'Could not resolve a drive letter for the selected USB target.'
            }

            Write-Host ('[INFO] Renaming USB volume ' + $driveLetter + ':\ to "' + $newLabel + '"')
            $volume = Get-Volume -DriveLetter $driveLetter -ErrorAction Stop
            Set-Volume -DriveLetter $driveLetter -NewFileSystemLabel $newLabel -ErrorAction Stop
            Write-Host ('[OK] USB volume renamed: ' + $driveLetter + ':\ -> ' + $newLabel)
            """;
    }

    private static string ToSingleQuotedPowerShellLiteral(string value)
    {
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private void ShowAbout()
    {
        _userPromptService.ShowMessage(
            "About ForgerEMS",
            "ForgerEMS is a native Windows controller for the existing PowerShell backend.\n\n" +
            "It discovers a bundled backend for installed mode, or falls back to repo mode and external release-bundle mode, letting an operator verify the backend, prepare or convert a selected USB to Ventoy, download or update the toolkit, inspect live logs, and surface managed-download status without rewriting backend rules in C#.",
            MessageBoxImage.Information);
    }

    private void ShowFaq()
    {
        _userPromptService.ShowMessage(
            "ForgerEMS FAQ",
            "1. Verify uses the existing backend verification script.\n" +
            "2. Select a USB target, then prepare or convert it to Ventoy inside the app.\n" +
            "3. Setup USB and Download / Update Toolkit call the backend setup/update scripts only.\n" +
            "4. Use 64GB or larger for complete downloads because Medicat.USB is large; 32GB can work for minimal or base setups.\n" +
            "5. Dry-run applies where the backend supports -WhatIf.\n" +
            "6. Installed mode prefers the bundled backend under the app folder.\n" +
            "7. Repo mode and external release-bundle mode still work for advanced operators.\n" +
            "8. Managed-download summaries combine the live manifest snapshot with the newest revalidation snapshot when one is available.\n" +
            "9. Ventoy install/update still happens manually inside the official Ventoy2Disk tool after ForgerEMS prepares and launches it.",
            MessageBoxImage.Information);
    }

    private void ShowLegal()
    {
        _userPromptService.ShowMessage(
            "ForgerEMS Legal",
            "ForgerEMS is a frontend controller and does not claim ownership of third-party tools.\n\n" +
            "Third-party downloads remain subject to each vendor or project's license, trademark, and distribution terms. The app should only retrieve payloads from official sources defined by the backend manifest, and the operator remains responsible for confirming target devices before destructive actions.",
            MessageBoxImage.Warning);
    }

    private async Task OpenUbuntuTerminalAsync()
    {
        LastCommandText = "Open Ubuntu / WSL Terminal -> wt.exe -p Ubuntu";
        AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] Opening Ubuntu terminal helper.", LogSeverity.Info));

        if (TryStartDetachedProcess("wt.exe", "-p", "Ubuntu"))
        {
            AppendLog(new LogLine(DateTimeOffset.Now, "[OK] Requested Windows Terminal Ubuntu profile.", LogSeverity.Success));
            return;
        }

        LastCommandText = "Open Ubuntu / WSL Terminal -> wsl.exe";
        AppendLog(new LogLine(DateTimeOffset.Now, "[WARN] Windows Terminal Ubuntu profile was not available. Falling back to wsl.exe.", LogSeverity.Warning));

        if (TryStartDetachedProcess("wsl.exe"))
        {
            AppendLog(new LogLine(DateTimeOffset.Now, "[OK] Requested default WSL shell.", LogSeverity.Success));
            return;
        }

        AppendLog(new LogLine(DateTimeOffset.Now, "[ERROR] Unable to start wt.exe or wsl.exe. Check whether WSL is installed.", LogSeverity.Error, isErrorStream: true));
        await Task.CompletedTask;
    }

    private async Task RunSafeExternalCommandAsync(string displayName, string fileName, params string[] arguments)
    {
        LastCommandText = $"{displayName} -> {fileName} {string.Join(" ", arguments)}";
        AppendLog(new LogLine(DateTimeOffset.Now, $"[INFO] Running safe local diagnostic: {LastCommandText}", LogSeverity.Info));

        try
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = new Process { StartInfo = startInfo };
            process.OutputDataReceived += (_, eventArgs) =>
            {
                if (!string.IsNullOrWhiteSpace(eventArgs.Data))
                {
                    AppendLog(new LogLine(DateTimeOffset.Now, eventArgs.Data, LogSeverity.Info));
                }
            };
            process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (!string.IsNullOrWhiteSpace(eventArgs.Data))
                {
                    AppendLog(new LogLine(DateTimeOffset.Now, eventArgs.Data, LogSeverity.Warning, isErrorStream: true));
                }
            };

            if (!process.Start())
            {
                AppendLog(new LogLine(DateTimeOffset.Now, $"[ERROR] {fileName} did not start.", LogSeverity.Error, isErrorStream: true));
                return;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            var severity = process.ExitCode == 0 ? LogSeverity.Success : LogSeverity.Warning;
            var prefix = process.ExitCode == 0 ? "[OK]" : "[WARN]";
            AppendLog(new LogLine(DateTimeOffset.Now, $"{prefix} {displayName} exited with code {process.ExitCode}.", severity, process.ExitCode != 0));
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, $"[ERROR] Unable to run {fileName}: {exception.Message}", LogSeverity.Error, isErrorStream: true));
        }
    }

    private static bool TryStartDetachedProcess(string fileName, params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                UseShellExecute = true
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            return Process.Start(startInfo) is not null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private void ClearLogs()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Logs.Clear();
            RefreshLogsText();
            OnPropertyChanged(nameof(LogStatusLineText));
            CopyLogsCommand.RaiseCanExecuteChanged();
            ClearLogsCommand.RaiseCanExecuteChanged();
        });
    }

    private void CopyLogs()
    {
        if (string.IsNullOrWhiteSpace(LogsText))
        {
            return;
        }

        Clipboard.SetText(LogsText);
    }

    private void NotifyBackendChanged()
    {
        OnPropertyChanged(nameof(BackendModeText));
        OnPropertyChanged(nameof(BackendRootText));
        OnPropertyChanged(nameof(BackendDiagnosticText));
        OnPropertyChanged(nameof(BackendVersionText));
        OnPropertyChanged(nameof(LogStatusLineText));
        RaiseCommandStates();
    }

    private void RaiseCommandStates()
    {
        RefreshAllCommand.RaiseCanExecuteChanged();
        RefreshUsbTargetsCommand.RaiseCanExecuteChanged();
        VerifyCommand.RaiseCanExecuteChanged();
        RevalidateManagedDownloadsCommand.RaiseCanExecuteChanged();
        SetupUsbCommand.RaiseCanExecuteChanged();
        UpdateUsbCommand.RaiseCanExecuteChanged();
        RenameUsbCommand.RaiseCanExecuteChanged();
        InstallOrUpdateVentoyCommand.RaiseCanExecuteChanged();
        RunSystemScanCommand.RaiseCanExecuteChanged();
        RefreshToolkitHealthCommand.RaiseCanExecuteChanged();
        UpdateToolkitCommand.RaiseCanExecuteChanged();
        OpenToolkitUsbReportsCommand.RaiseCanExecuteChanged();
        RecheckSelectedToolCommand.RaiseCanExecuteChanged();
        OpenSelectedToolLocationCommand.RaiseCanExecuteChanged();
        OpenManualDownloadShortcutCommand.RaiseCanExecuteChanged();
        OpenUbuntuTerminalCommand.RaiseCanExecuteChanged();
        CheckWslInstalledCommand.RaiseCanExecuteChanged();
        ShowWslDistrosCommand.RaiseCanExecuteChanged();
        CopyLogsCommand.RaiseCanExecuteChanged();
        ClearLogsCommand.RaiseCanExecuteChanged();
        SendCopilotMessageCommand.RaiseCanExecuteChanged();
        StopCopilotGenerationCommand.RaiseCanExecuteChanged();
    }

    private void AppendLog(LogLine line)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            ApplyProgressFromLog(line.Text);
            Logs.Add(line);

            if (Logs.Count > 600)
            {
                Logs.RemoveAt(0);
            }

            RefreshLogsText();
            OnPropertyChanged(nameof(LogStatusLineText));
            CopyLogsCommand.RaiseCanExecuteChanged();
            ClearLogsCommand.RaiseCanExecuteChanged();
        });

        try
        {
            _appRuntimeService.AppendSessionLog(line);
        }
        catch
        {
            // Session log persistence is best effort only.
        }
    }

    private void SetManagedSummaryStatus(string text, Brush background, Brush borderBrush, Brush foreground)
    {
        ManagedSummaryStatusText = text;
        ManagedSummaryStatusBackground = background;
        ManagedSummaryStatusBorderBrush = borderBrush;
        ManagedSummaryStatusForeground = foreground;
    }

    private void RefreshLogsText()
    {
        var visibleLines = Logs.Where(IsVisibleLogLine).Select(item => item.DisplayText).ToArray();
        LogsText = string.Join(Environment.NewLine, visibleLines);
        RecentLogsText = visibleLines.Length == 0
            ? "No log output yet."
            : string.Join(Environment.NewLine, visibleLines.TakeLast(5));
        CopyLogsCommand.RaiseCanExecuteChanged();
    }

    private bool IsVisibleLogLine(LogLine line)
    {
        return SelectedLogLevelFilter switch
        {
            "Info" => line.Severity == LogSeverity.Info,
            "Success" => line.Severity == LogSeverity.Success,
            "Warning" => line.Severity == LogSeverity.Warning,
            "Error" => line.Severity == LogSeverity.Error,
            _ => true
        };
    }

    private void SetTargetWarningVisuals(Brush background, Brush borderBrush, Brush foreground)
    {
        TargetWarningBackground = background;
        TargetWarningBorderBrush = borderBrush;
        TargetWarningForeground = foreground;
    }

    private void SetVentoyStatus(string text, Brush background, Brush borderBrush, Brush foreground)
    {
        VentoyStatusText = text;
        VentoyStatusBackground = background;
        VentoyStatusBorderBrush = borderBrush;
        VentoyStatusForeground = foreground;
    }

    private void SetStatus(string text, string detail, Brush background, Brush borderBrush, Brush foreground)
    {
        StatusText = text;
        StatusDetail = detail;
        StatusBackground = background;
        StatusBorderBrush = borderBrush;
        StatusForeground = foreground;

        CurrentTaskText = text;
        var currentTaskState =
            ReferenceEquals(background, ErrorBackground) ? "ERROR" :
            ReferenceEquals(background, WarningBackground) ? "WARNING" :
            ReferenceEquals(background, ReadyBackground) && text.Contains("complete", StringComparison.OrdinalIgnoreCase) ? "COMPLETE" :
            ReferenceEquals(background, ReadyBackground) ? "READY" :
            "WORKING";
        CurrentTaskState = currentTaskState;

        if (currentTaskState == "WORKING")
        {
            SetProgress(CurrentProgressValue, indeterminate: true, visible: true);
        }
        else
        {
            ResetProgressSoon();
        }

        OnPropertyChanged(nameof(LogStatusLineText));
    }

    private void SetProgress(double value, bool indeterminate, bool visible = true)
    {
        CurrentProgressValue = Math.Clamp(value, 0, 100);
        IsProgressIndeterminate = indeterminate;
        ProgressVisibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ResetProgressSoon()
    {
        SetProgress(0, false, visible: false);
    }

    private void ApplyProgressFromLog(string text)
    {
        var percentMatch = System.Text.RegularExpressions.Regex.Match(text, @"(?<percent>\d{1,3}(?:\.\d+)?)%");
        if (percentMatch.Success &&
            double.TryParse(percentMatch.Groups["percent"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var percent))
        {
            SetProgress(percent, indeterminate: false);
            if (text.Contains("Downloading", StringComparison.OrdinalIgnoreCase))
            {
                CurrentTaskState = "WORKING";
                CurrentTaskText = text;
                OnPropertyChanged(nameof(LogStatusLineText));
            }
            return;
        }

        if (text.Contains("Downloading", StringComparison.OrdinalIgnoreCase))
        {
            SetProgress(0, indeterminate: true);
            CurrentTaskState = "WORKING";
            CurrentTaskText = text;
            OnPropertyChanged(nameof(LogStatusLineText));
        }
    }
}
