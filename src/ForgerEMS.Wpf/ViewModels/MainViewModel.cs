using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.KyraTools;

namespace VentoyToolkitSetup.Wpf.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private static readonly string[] WslHostListVerboseArgs = { "--list", "--verbose" };
    private static readonly string[] WslHostStatusArgs = { "--status" };

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
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

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
    private readonly IWslCommandExecutor _wslExecutor;
    private readonly Dictionary<string, UsbBenchmarkResult> _benchmarkResultsByRoot = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _benchmarksInProgress = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _benchmarkCachePath;
    private readonly string _copilotConfigPath;
    private readonly string _betaConfigPath;
    private readonly string _updateConfigPath;
    private readonly AppUpdateSettingsStore _updateSettingsStore;
    private readonly GitHubReleaseUpdateCheckService _updateCheckService;
    private AppUpdateSettings _appUpdateSettings = new();
    private bool _updateCheckInProgress;
    private bool _updateDownloadInProgress;
    private string _pendingInstallerUrl = string.Empty;
    private string _pendingReleaseNotesUrl = string.Empty;
    private string _pendingVersionLabel = string.Empty;
    private string _appUpdateLatestChannelText = "Latest release: —";
    private Visibility _appUpdateDownloadButtonVisibility = Visibility.Collapsed;
    private Visibility _appUpdateIgnoreButtonVisibility = Visibility.Visible;
    private Visibility _appUpdateViewReleaseNotesVisibility = Visibility.Visible;
    private Visibility _appUpdateDiagnosticsHintVisibility = Visibility.Collapsed;
    private bool _verboseLiveLogs;
    private CancellationTokenSource? _usbMonitorCancellation;
    private CancellationTokenSource? _copilotGenerationCancellation;
    private CopilotSettings _copilotSettings = new();
    private readonly string _kyraMemoryPath;
    private string _kyraSanitizedContextPreviewText = string.Empty;
    private string _kyraAssistantStatusSummary = string.Empty;
    private bool _disposed;

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
    private string _usbProgressStageText = "Stage: idle";
    private string _usbProgressItemText = "Current item: none";
    private string _usbProgressPercentText = "Percent: unknown";
    private string _usbProgressTransferText = "Transferred: unknown";
    private string _usbProgressSpeedText = "Speed: unknown";
    private string _usbProgressHeartbeatText = "Waiting for USB/build activity.";
    private Visibility _betaWelcomeVisibility = Visibility.Collapsed;
    private bool _betaTesterEntitlement;
    private Visibility _betaTesterEntitlementVisibility = Visibility.Collapsed;
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
    private string _toolkitManualExplanationText = "Manual means ForgerEMS cannot legally auto-download this item. Use the provided link/instructions and verify the file path afterward.";
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
    private string _kyraActivityStatusText = string.Empty;
    private bool _kyraSlashPopupOpen;
    private bool _kyraHasSystemScanReport;
    private bool _kyraHasRecentWarningLog;
    private bool _kyraShowLiveToolsQuickButton;
    private int _kyraSlashSelectedIndex = -1;
    private DateTime _kyraSlashPopupQuietUntilUtc = DateTime.MinValue;
    private string _copilotContextText = "Run a system scan and select a USB target to load Kyra context.";
    private string _copilotContextSummaryText = "System Context\n- Device: run System Intelligence\n- CPU: unknown\n- RAM: unknown\n- GPU: unknown\n- Storage: unknown\n- Battery: unknown\n- USB: none selected";
    private string _copilotProviderSummaryText = "Local Offline Rules: Ready\nOnline AI: Not configured\nLocal AI: Not configured\nPricing Lookup: Not configured";
    private string _copilotProviderBadgeText = "Offline Ready";
    private string _copilotPrivacyBadgeText = "Local Only";
    private string _copilotActiveProviderText = "Provider: Local Kyra";
    private string _copilotDiagnosticsSummaryText = "Enabled providers: 0 | Configured providers: 0 | Fallback: Local Kyra active";
    private string _copilotLastProviderFailureText = "Last provider failure: none";
    private Visibility _copilotTechnicalContextVisibility = Visibility.Collapsed;
    private string _copilotTechnicalContextButtonText = "View technical context";
    private string _copilotRoutingPolicyText = string.Empty;
    private string _selectedCopilotMode = "Offline Local";
    private bool _allowOnlineSystemContextSharing;
    private bool _enableFreeProviderPool = true;
    private bool _enableByokProviders;
    private bool _useLatestSystemScanContext = true;
    private bool _isCopilotGenerating;
    private string _copilotOnlineStatusText = "Offline Only - no data leaves this machine.";
    private Brush _copilotOnlineStatusBackground = ReadyBackground;
    private Brush _copilotOnlineStatusBorderBrush = ReadyBorder;
    private Brush _copilotOnlineStatusForeground = ReadyForeground;
    private string _wslRunnerSummaryText = string.Empty;
    private string _wslRunnerOutputText = string.Empty;
    private string _wslRunnerCommandInput = string.Empty;
    private bool _isWslRunnerBusy;
    private CancellationTokenSource? _wslRunnerCancellation;
    private readonly ConcurrentQueue<string> _wslPendingOutputLines = new();
    private DispatcherTimer? _wslOutputFlushTimer;
    private string _windowsSandboxStatusText = string.Empty;
    private string _linkSafetyUrlInput = string.Empty;
    private string _linkSafetyResultText =
        "Paste an https URL, tap Analyze for local heuristics, then optionally HTTPS HEAD. Quarantine download never runs the file.";
    private string _localFileSafetyPath = string.Empty;
    private string _localFileSafetyResultText =
        "Pick a downloaded file for a read-only check (SHA256 + heuristics). ForgerEMS never executes the selected file.";
    private string _lastLocalSafetySha256 = string.Empty;
    private Visibility _appUpdateBannerVisibility = Visibility.Collapsed;
    private string _appUpdateBannerTitle = string.Empty;
    private string _appUpdateBannerDetail = string.Empty;
    private string _appUpdateStateDisplay = "Updates: not checked yet.";

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
        ICopilotProviderRegistry copilotProviderRegistry,
        IWslCommandExecutor? wslExecutor = null)
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
        _wslExecutor = wslExecutor ?? DefaultWslCommandExecutor.Instance;
        _benchmarkCachePath = Path.Combine(_appRuntimeService.RuntimeRoot, "cache", "usb-benchmarks.json");
        _copilotConfigPath = Path.Combine(_appRuntimeService.RuntimeRoot, "config", "copilot-settings.json");
        _kyraMemoryPath = Path.Combine(_appRuntimeService.RuntimeRoot, "config", "kyra-memory.json");
        _betaConfigPath = Path.Combine(_appRuntimeService.RuntimeRoot, "config", "beta-settings.json");
        _updateConfigPath = Path.Combine(_appRuntimeService.RuntimeRoot, "config", "update-settings.json");
        _updateSettingsStore = new AppUpdateSettingsStore(_updateConfigPath);
        _updateCheckService = new GitHubReleaseUpdateCheckService();
        LoadBenchmarkCache();
        LoadCopilotSettings();
        LoadBetaSettings();
        LoadUpdateSettings();

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
        RunWslRunnerCommand = new AsyncRelayCommand(RunWslRunnerAsync, () => !IsBusy && !_isWslRunnerBusy && _wslExecutor.IsWslInstalled());
        StopWslRunnerCommand = new RelayCommand(StopWslRunner);
        CopyWslRunnerOutputCommand = new RelayCommand(CopyWslRunnerOutput, () => !string.IsNullOrWhiteSpace(_wslRunnerOutputText));
        ClearWslRunnerOutputCommand = new RelayCommand(ClearWslRunnerOutputPane, () => !string.IsNullOrWhiteSpace(_wslRunnerOutputText));
        InsertWslRunnerPresetCommand = new RelayCommand<string>(preset =>
        {
            if (!string.IsNullOrWhiteSpace(preset))
            {
                WslRunnerCommandInput = preset;
            }
        });
        RunWslHostListVerboseRunnerCommand = new AsyncRelayCommand(
            () => RunWslHostArgumentsUiAsync(WslHostListVerboseArgs, "wsl.exe --list --verbose"),
            () => !IsBusy && !_isWslRunnerBusy && _wslExecutor.IsWslInstalled());
        RunWslHostStatusRunnerCommand = new AsyncRelayCommand(
            () => RunWslHostArgumentsUiAsync(WslHostStatusArgs, "wsl.exe --status"),
            () => !IsBusy && !_isWslRunnerBusy && _wslExecutor.IsWslInstalled());
        AnalyzeLinkSafetyCommand = new RelayCommand(RunLinkSafetyAnalyze, () => !IsBusy);
        FetchLinkSafetyHeadersCommand = new AsyncRelayCommand(RunLinkSafetyHeadAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(_linkSafetyUrlInput));
        DownloadLinkToQuarantineCommand = new AsyncRelayCommand(DownloadLinkToQuarantineAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(_linkSafetyUrlInput));
        BrowseLocalFileSafetyCommand = new RelayCommand(BrowseLocalFileSafety, () => !IsBusy);
        AnalyzeLocalFileSafetyCommand = new RelayCommand(RunLocalFileSafetyAnalyze, () => !IsBusy && !string.IsNullOrWhiteSpace(_localFileSafetyPath));
        CopyLocalFileSafetyShaCommand = new RelayCommand(CopyLocalFileSafetySha, () => !string.IsNullOrWhiteSpace(_lastLocalSafetySha256));
        CopyLocalFileSafetyReportCommand = new RelayCommand(CopyLocalFileSafetyReport, () => !string.IsNullOrWhiteSpace(_localFileSafetyResultText));
        OpenLocalSafetyQuarantineFolderCommand = new RelayCommand(OpenLocalSafetyQuarantineFolder);
        CopyLocalFileToQuarantineCommand = new RelayCommand(CopyLocalFileToQuarantine, () => !IsBusy && !string.IsNullOrWhiteSpace(_localFileSafetyPath));
        SendCopilotMessageCommand = new AsyncRelayCommand(SendCopilotMessageAsync, () => !IsCopilotGenerating && !string.IsNullOrWhiteSpace(CopilotInput));
        AskCopilotValueCommand = new AsyncRelayCommand(() => AskCopilotAsync("/resale"), () => !IsCopilotGenerating);
        AskCopilotUpgradeCommand = new AsyncRelayCommand(() => AskCopilotAsync("/resale"), () => !IsCopilotGenerating);
        AskCopilotLagCommand = new AsyncRelayCommand(() => AskCopilotAsync("/diagnose lag"), () => !IsCopilotGenerating);
        AskCopilotOsCommand = new AsyncRelayCommand(() => AskCopilotAsync("/os"), () => !IsCopilotGenerating);
        AskCopilotUsbCommand = new AsyncRelayCommand(() => AskCopilotAsync("/usb"), () => !IsCopilotGenerating);
        AskCopilotWarningCommand = new AsyncRelayCommand(() => AskCopilotAsync("/warning"), () => !IsCopilotGenerating);
        AskCopilotListingCommand = new AsyncRelayCommand(() => AskCopilotAsync("/listing facebook"), () => !IsCopilotGenerating);
        AskCopilotLiveToolsCommand = new AsyncRelayCommand(() => AskCopilotAsync("/provider"), () => !IsCopilotGenerating);
        AskCopilotFixCodeCommand = new AsyncRelayCommand(() => AskCopilotAsync("/fixcode"), () => !IsCopilotGenerating);
        ClearCopilotHistoryCommand = new RelayCommand(ClearCopilotHistoryAndCache);
        StopCopilotGenerationCommand = new RelayCommand(StopCopilotGeneration, () => IsCopilotGenerating);
        UseLatestSystemScanContextCommand = new RelayCommand(UseLatestSystemScanContextNow);
        ToggleCopilotTechnicalContextCommand = new RelayCommand(ToggleCopilotTechnicalContext);
        OpenKyraAdvancedSettingsCommand = new RelayCommand(OpenKyraAdvancedSettings);
        TestCopilotConnectionCommand = new AsyncRelayCommand(TestCopilotConnectionAsync, () => !IsCopilotGenerating);
        ClearProviderSessionKeysCommand = new RelayCommand(ClearProviderSessionKeys);
        RefreshCopilotProviderStatusCommand = new RelayCommand(RefreshCopilotProviderStatus);
        SaveKyraLiveToolsSettingsCommand = new RelayCommand(SaveCopilotSettings);
        ExportKyraMemoryCommand = new RelayCommand(ExportKyraMemory);
        ClearKyraMemoryCommand = new RelayCommand(ClearKyraMemory);
        ViewKyraMemoryCommand = new RelayCommand(ViewKyraMemory);
        OpenLogsFolderCommand = new RelayCommand(() => OpenFolder(_appRuntimeService.LogsRoot, "logs folder", createIfMissing: true));
        CopySupportEmailCommand = new RelayCommand(CopySupportEmail);
        OpenSupportEmailCommand = new RelayCommand(OpenSupportEmail);
        CopyBetaReportTemplateCommand = new RelayCommand(CopyBetaReportTemplate);
        CheckForUpdatesNowCommand = new AsyncRelayCommand(() => RequestUpdateCheckAsync(manual: true), () => !_updateCheckInProgress);
        AppUpdateRemindLaterCommand = new RelayCommand(HideAppUpdateBanner);
        AppUpdateIgnoreVersionCommand = new RelayCommand(IgnorePendingAppUpdateVersion);
        AppUpdateViewReleaseNotesCommand = new RelayCommand(OpenPendingReleaseNotes);
        AppUpdateDownloadInstallerCommand = new AsyncRelayCommand(DownloadPendingInstallerAsync, CanDownloadPendingInstaller);
        ClearIgnoredAppUpdateVersionCommand = new RelayCommand(ClearIgnoredAppUpdateVersion, CanClearIgnoredAppUpdateVersion);
        ClearIgnoredAppUpdateVersionCommand.RaiseCanExecuteChanged();

        CopilotMessages.Add(new CopilotChatMessage
        {
            Role = "Kyra",
            Text =
                "Hi — I’m Kyra, your ForgerEMS tech buddy. Ask in plain English or use slash commands like `/help`, `/diagnose`, `/usb`, `/resale`, `/fixcode`. I’ll keep it practical.",
            SourceLabel = "Answered by Local Kyra"
        });

        Logs.CollectionChanged += (_, _) => RefreshKyraQuickPromptVisibilities();

        RefreshWslRunnerSummary();
        RefreshDiagnosticsAuxiliaryText();
        RefreshKyraQuickPromptVisibilities();
        ScheduleBackgroundUpdateCheck();
    }

    public ObservableCollection<UsbTargetInfo> UsbTargets { get; } = [];

    public ObservableCollection<LogLine> Logs { get; } = [];

    public ObservableCollection<string> SystemIntelligenceRecommendations { get; } = [];

    public ObservableCollection<ToolkitHealthItemView> ToolkitHealthItems { get; } = [];

    public ObservableCollection<CopilotChatMessage> CopilotMessages { get; } = [];

    public ObservableCollection<string> KyraSlashSuggestions { get; } = [];

    public ObservableCollection<KyraToolStatusRowView> KyraToolStatusRows { get; } = [];

    /// <summary>Bind Kyra Advanced → Live APIs fields; same instance persisted with copilot settings.</summary>
    public KyraLiveToolsSettings KyraLiveToolsForBinding
    {
        get
        {
            _copilotSettings ??= new CopilotSettings();
            _copilotSettings.LiveTools ??= new KyraLiveToolsSettings();
            return _copilotSettings.LiveTools;
        }
    }

    public ObservableCollection<CopilotProviderSettingView> CopilotProviderSettings { get; } = [];

    public IReadOnlyList<string> LogLevelFilterOptions { get; } = ["All", "Info", "Success", "Warning", "Error"];

    public IReadOnlyList<string> ToolkitFilterOptions { get; } = ["All", "Installed", "Required Missing", "Manual", "Failed", "Updates", "Skipped/Placeholder"];

    public IReadOnlyList<string> ToolkitCategoryFilterOptions { get; } = ["All categories", "Windows", "Linux", "Recovery", "Diagnostics", "USB Builders"];

    public IReadOnlyList<string> CopilotModeOptions { get; } = ["Offline Local", "Free API Pool", "Hybrid", "Online/API", "BYOK", "Ask First"];

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

    public AsyncRelayCommand RunWslRunnerCommand { get; }

    public RelayCommand StopWslRunnerCommand { get; }

    public RelayCommand CopyWslRunnerOutputCommand { get; }

    public RelayCommand ClearWslRunnerOutputCommand { get; }

    public RelayCommand<string> InsertWslRunnerPresetCommand { get; }

    public AsyncRelayCommand RunWslHostListVerboseRunnerCommand { get; }

    public AsyncRelayCommand RunWslHostStatusRunnerCommand { get; }

    public RelayCommand AnalyzeLinkSafetyCommand { get; }

    public AsyncRelayCommand FetchLinkSafetyHeadersCommand { get; }

    public AsyncRelayCommand DownloadLinkToQuarantineCommand { get; }

    public RelayCommand BrowseLocalFileSafetyCommand { get; }

    public RelayCommand AnalyzeLocalFileSafetyCommand { get; }

    public RelayCommand CopyLocalFileSafetyShaCommand { get; }

    public RelayCommand CopyLocalFileSafetyReportCommand { get; }

    public RelayCommand OpenLocalSafetyQuarantineFolderCommand { get; }

    public RelayCommand CopyLocalFileToQuarantineCommand { get; }

    public AsyncRelayCommand SendCopilotMessageCommand { get; }

    public AsyncRelayCommand AskCopilotValueCommand { get; }

    public AsyncRelayCommand AskCopilotUpgradeCommand { get; }

    public AsyncRelayCommand AskCopilotLagCommand { get; }

    public AsyncRelayCommand AskCopilotOsCommand { get; }

    public AsyncRelayCommand AskCopilotUsbCommand { get; }

    public AsyncRelayCommand AskCopilotWarningCommand { get; }

    public AsyncRelayCommand AskCopilotListingCommand { get; }

    public AsyncRelayCommand AskCopilotLiveToolsCommand { get; }

    public AsyncRelayCommand AskCopilotFixCodeCommand { get; }

    public RelayCommand ClearCopilotHistoryCommand { get; }

    public RelayCommand StopCopilotGenerationCommand { get; }

    public RelayCommand UseLatestSystemScanContextCommand { get; }

    public RelayCommand ToggleCopilotTechnicalContextCommand { get; }

    public RelayCommand OpenKyraAdvancedSettingsCommand { get; }

    public AsyncRelayCommand TestCopilotConnectionCommand { get; }

    public RelayCommand ClearProviderSessionKeysCommand { get; }

    public RelayCommand RefreshCopilotProviderStatusCommand { get; }

    public RelayCommand SaveKyraLiveToolsSettingsCommand { get; }

    public RelayCommand ExportKyraMemoryCommand { get; }

    public RelayCommand ClearKyraMemoryCommand { get; }

    public RelayCommand ViewKyraMemoryCommand { get; }

    public RelayCommand CopySupportEmailCommand { get; }

    public RelayCommand OpenSupportEmailCommand { get; }

    public RelayCommand CopyBetaReportTemplateCommand { get; }

    public AsyncRelayCommand CheckForUpdatesNowCommand { get; }

    public RelayCommand AppUpdateRemindLaterCommand { get; }

    public RelayCommand AppUpdateIgnoreVersionCommand { get; }

    public RelayCommand AppUpdateViewReleaseNotesCommand { get; }

    public AsyncRelayCommand AppUpdateDownloadInstallerCommand { get; }

    public RelayCommand ClearIgnoredAppUpdateVersionCommand { get; }

    public RelayCommand OpenLogsFolderCommand { get; }

    /// <summary>Assigned by MainWindow to open the Kyra Advanced Settings dialog.</summary>
    public Action? OpenKyraAdvancedSettingsAction { get; set; }

    /// <summary>Navigates main window tab when header contains the given substring (e.g. "Settings").</summary>
    public Action<string>? MainTabNavigationAction { get; set; }

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

    public string AppVersionText { get; } = AppReleaseInfo.DisplayVersion;

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

    public string UsbProgressStageText
    {
        get => _usbProgressStageText;
        private set => SetProperty(ref _usbProgressStageText, value);
    }

    public string UsbProgressItemText
    {
        get => _usbProgressItemText;
        private set => SetProperty(ref _usbProgressItemText, value);
    }

    public string UsbProgressPercentText
    {
        get => _usbProgressPercentText;
        private set => SetProperty(ref _usbProgressPercentText, value);
    }

    public string UsbProgressTransferText
    {
        get => _usbProgressTransferText;
        private set => SetProperty(ref _usbProgressTransferText, value);
    }

    public string UsbProgressSpeedText
    {
        get => _usbProgressSpeedText;
        private set => SetProperty(ref _usbProgressSpeedText, value);
    }

    public string UsbProgressHeartbeatText
    {
        get => _usbProgressHeartbeatText;
        private set => SetProperty(ref _usbProgressHeartbeatText, value);
    }

    public Visibility BetaWelcomeVisibility
    {
        get => _betaWelcomeVisibility;
        private set => SetProperty(ref _betaWelcomeVisibility, value);
    }

    public bool BetaTesterEntitlement
    {
        get => _betaTesterEntitlement;
        private set
        {
            if (SetProperty(ref _betaTesterEntitlement, value))
            {
                BetaTesterEntitlementVisibility = value ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    public Visibility BetaTesterEntitlementVisibility
    {
        get => _betaTesterEntitlementVisibility;
        private set => SetProperty(ref _betaTesterEntitlementVisibility, value);
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

    public string AppVersionFooterText { get; } = AppReleaseInfo.ReleaseIdentifier;

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
                OnPropertyChanged(nameof(CopilotInputPlaceholderVisibility));
                SendCopilotMessageCommand.RaiseCanExecuteChanged();
                RefreshKyraSlashSuggestions();
            }
        }
    }

    public string CopilotContextText
    {
        get => _copilotContextText;
        private set => SetProperty(ref _copilotContextText, value);
    }

    public string CopilotContextSummaryText
    {
        get => _copilotContextSummaryText;
        private set => SetProperty(ref _copilotContextSummaryText, value);
    }

    public string CopilotProviderSummaryText
    {
        get => _copilotProviderSummaryText;
        private set => SetProperty(ref _copilotProviderSummaryText, value);
    }

    public string CopilotProviderBadgeText
    {
        get => _copilotProviderBadgeText;
        private set => SetProperty(ref _copilotProviderBadgeText, value);
    }

    public string CopilotActiveProviderText
    {
        get => _copilotActiveProviderText;
        private set => SetProperty(ref _copilotActiveProviderText, value);
    }

    public string CopilotDiagnosticsSummaryText
    {
        get => _copilotDiagnosticsSummaryText;
        private set => SetProperty(ref _copilotDiagnosticsSummaryText, value);
    }

#pragma warning disable CA1822 // Instance properties consumed by WPF bindings.
    public string CopilotProviderEnvironmentHelpText => CopilotProviderEnvironmentVariableNames.UxHowToConfigure;

    public string BetaIssueSupportLineText => BetaSupportInfo.BetaIssueSupportLine;

    public string SupportEmailAddress => BetaSupportInfo.SupportEmail;

    public Uri SupportMailtoUri => new(BetaSupportInfo.MailtoUri);

    public string SupportEmailDoNotSecretsText => BetaSupportInfo.DoNotEmailSecretsWarning;

    public string AppUpdateSettingsVersionSummary =>
        $"Installed version: ForgerEMS v{AppReleaseInfo.Version} ({AppReleaseInfo.DisplayVersion})";

    public string AppUpdateSettingsSourceLine =>
        "Update source: public GitHub Releases API (Forger-Digital-Solutions/ForgerEMS).";
#pragma warning restore CA1822

    public Visibility AppUpdateBannerVisibility
    {
        get => _appUpdateBannerVisibility;
        private set => SetProperty(ref _appUpdateBannerVisibility, value);
    }

    public string AppUpdateBannerTitle
    {
        get => _appUpdateBannerTitle;
        private set => SetProperty(ref _appUpdateBannerTitle, value);
    }

    public string AppUpdateBannerDetail
    {
        get => _appUpdateBannerDetail;
        private set => SetProperty(ref _appUpdateBannerDetail, value);
    }

    public string AppUpdateStateDisplay
    {
        get => _appUpdateStateDisplay;
        private set => SetProperty(ref _appUpdateStateDisplay, value);
    }

    public Visibility AppUpdateDownloadButtonVisibility
    {
        get => _appUpdateDownloadButtonVisibility;
        private set => SetProperty(ref _appUpdateDownloadButtonVisibility, value);
    }

    public Visibility AppUpdateIgnoreButtonVisibility
    {
        get => _appUpdateIgnoreButtonVisibility;
        private set => SetProperty(ref _appUpdateIgnoreButtonVisibility, value);
    }

    public Visibility AppUpdateViewReleaseNotesVisibility
    {
        get => _appUpdateViewReleaseNotesVisibility;
        private set => SetProperty(ref _appUpdateViewReleaseNotesVisibility, value);
    }

    public Visibility AppUpdateDiagnosticsHintVisibility
    {
        get => _appUpdateDiagnosticsHintVisibility;
        private set => SetProperty(ref _appUpdateDiagnosticsHintVisibility, value);
    }

    public string AppUpdateSettingsLatestSummary => _appUpdateLatestChannelText;

    public string AppUpdateSettingsIgnoredSummary =>
        string.IsNullOrWhiteSpace(_appUpdateSettings.IgnoredVersion)
            ? string.Empty
            : $"Ignored update prompt for: v{ReleaseVersionParser.NormalizeLabel(_appUpdateSettings.IgnoredVersion)}";

    public Visibility AppUpdateSettingsIgnoredVisibility =>
        string.IsNullOrWhiteSpace(_appUpdateSettings.IgnoredVersion) ? Visibility.Collapsed : Visibility.Visible;

    public bool VerboseLiveLogs
    {
        get => _verboseLiveLogs;
        set
        {
            if (SetProperty(ref _verboseLiveLogs, value))
            {
                SaveBetaSettings();
                RefreshLogsText();
            }
        }
    }

    public bool CheckForUpdatesAutomatically
    {
        get => _appUpdateSettings.CheckAutomatically;
        set
        {
            if (_appUpdateSettings.CheckAutomatically == value)
            {
                return;
            }

            _appUpdateSettings.CheckAutomatically = value;
            SaveUpdateSettings();
            OnPropertyChanged(nameof(CheckForUpdatesAutomatically));
        }
    }

    public string LastUpdateCheckDisplayText =>
        _appUpdateSettings.LastCheckedUtc is { } utc
            ? $"Last checked: {utc.ToLocalTime():g}"
            : "Last checked: never";

    public string CopilotLastProviderFailureText
    {
        get => _copilotLastProviderFailureText;
        private set => SetProperty(ref _copilotLastProviderFailureText, value);
    }

    public string CopilotPrivacyBadgeText
    {
        get => _copilotPrivacyBadgeText;
        private set => SetProperty(ref _copilotPrivacyBadgeText, value);
    }

    public Visibility CopilotTechnicalContextVisibility
    {
        get => _copilotTechnicalContextVisibility;
        private set => SetProperty(ref _copilotTechnicalContextVisibility, value);
    }

    public string CopilotTechnicalContextButtonText
    {
        get => _copilotTechnicalContextButtonText;
        private set => SetProperty(ref _copilotTechnicalContextButtonText, value);
    }

    public string CopilotRoutingPolicyText
    {
        get => _copilotRoutingPolicyText;
        private set
        {
            if (SetProperty(ref _copilotRoutingPolicyText, value))
            {
                OnPropertyChanged(nameof(CopilotRoutingPolicyVisibility));
            }
        }
    }

    public Visibility CopilotRoutingPolicyVisibility =>
        string.IsNullOrWhiteSpace(_copilotRoutingPolicyText) ? Visibility.Collapsed : Visibility.Visible;

    public string KyraSanitizedContextPreviewText
    {
        get => _kyraSanitizedContextPreviewText;
        private set => SetProperty(ref _kyraSanitizedContextPreviewText, value);
    }

    public string KyraAssistantStatusSummary
    {
        get => _kyraAssistantStatusSummary;
        private set => SetProperty(ref _kyraAssistantStatusSummary, value);
    }

    public bool KyraApiFirstRouting
    {
        get => _copilotSettings.ApiFirstRouting;
        set
        {
            if (_copilotSettings.ApiFirstRouting != value)
            {
                _copilotSettings.ApiFirstRouting = value;
                OnPropertyChanged();
                SaveCopilotSettings();
            }
        }
    }

    public bool KyraOfflineFallbackEnabled
    {
        get => _copilotSettings.OfflineFallbackEnabled;
        set
        {
            if (_copilotSettings.OfflineFallbackEnabled != value)
            {
                _copilotSettings.OfflineFallbackEnabled = value;
                OnPropertyChanged();
                SaveCopilotSettings();
            }
        }
    }

    public bool KyraPersistentMemoryEnabled
    {
        get => _copilotSettings.KyraPersistentMemoryEnabled;
        set
        {
            if (_copilotSettings.KyraPersistentMemoryEnabled != value)
            {
                _copilotSettings.KyraPersistentMemoryEnabled = value;
                try
                {
                    var store = new KyraPersistentMemoryStore(_kyraMemoryPath);
                    var doc = store.Load();
                    doc.Enabled = value;
                    KyraPersistentMemoryStore.SanitizeInPlace(doc);
                    store.Save(doc);
                }
                catch
                {
                }

                OnPropertyChanged();
                SaveCopilotSettings();
            }
        }
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

    public bool AllowOnlineSystemContextSharing
    {
        get => _allowOnlineSystemContextSharing;
        set
        {
            if (SetProperty(ref _allowOnlineSystemContextSharing, value))
            {
                SaveCopilotSettings();
            }
        }
    }

    public bool EnableFreeProviderPool
    {
        get => _enableFreeProviderPool;
        set
        {
            if (SetProperty(ref _enableFreeProviderPool, value))
            {
                SaveCopilotSettings();
            }
        }
    }

    public bool EnableByokProviders
    {
        get => _enableByokProviders;
        set
        {
            if (SetProperty(ref _enableByokProviders, value))
            {
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
                OnPropertyChanged(nameof(CopilotThinkingVisibility));
                OnPropertyChanged(nameof(StopCopilotGenerationVisibility));
                SendCopilotMessageCommand.RaiseCanExecuteChanged();
                AskCopilotValueCommand.RaiseCanExecuteChanged();
                AskCopilotUpgradeCommand.RaiseCanExecuteChanged();
                AskCopilotLagCommand.RaiseCanExecuteChanged();
                AskCopilotOsCommand.RaiseCanExecuteChanged();
                AskCopilotUsbCommand.RaiseCanExecuteChanged();
                AskCopilotWarningCommand.RaiseCanExecuteChanged();
                AskCopilotListingCommand.RaiseCanExecuteChanged();
                AskCopilotLiveToolsCommand.RaiseCanExecuteChanged();
                AskCopilotFixCodeCommand.RaiseCanExecuteChanged();
                StopCopilotGenerationCommand.RaiseCanExecuteChanged();
                TestCopilotConnectionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public Visibility CopilotThinkingVisibility => IsCopilotGenerating ? Visibility.Visible : Visibility.Collapsed;

    public Visibility StopCopilotGenerationVisibility => IsCopilotGenerating ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CopilotInputPlaceholderVisibility => string.IsNullOrWhiteSpace(CopilotInput) ? Visibility.Visible : Visibility.Collapsed;

    public string KyraActivityStatusText
    {
        get => _kyraActivityStatusText;
        private set => SetProperty(ref _kyraActivityStatusText, value);
    }

    public bool KyraSlashPopupOpen
    {
        get => _kyraSlashPopupOpen;
        set => SetProperty(ref _kyraSlashPopupOpen, value);
    }

    public Visibility KyraListingQuickButtonVisibility =>
        _kyraHasSystemScanReport ? Visibility.Visible : Visibility.Collapsed;

    public Visibility KyraWarningQuickButtonVisibility =>
        _kyraHasRecentWarningLog ? Visibility.Visible : Visibility.Collapsed;

    public Visibility KyraLiveToolsQuickButtonVisibility =>
        _kyraShowLiveToolsQuickButton ? Visibility.Visible : Visibility.Collapsed;

    public int KyraSlashSelectedIndex
    {
        get => _kyraSlashSelectedIndex;
        set
        {
            var max = KyraSlashSuggestions.Count - 1;
            var v = max < 0 ? -1 : Math.Clamp(value, 0, max);
            SetProperty(ref _kyraSlashSelectedIndex, v);
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

    public string WslRunnerSummaryText
    {
        get => _wslRunnerSummaryText;
        private set => SetProperty(ref _wslRunnerSummaryText, value);
    }

    public string WslRunnerOutputText
    {
        get => _wslRunnerOutputText;
        set
        {
            if (SetProperty(ref _wslRunnerOutputText, value))
            {
                CopyWslRunnerOutputCommand.RaiseCanExecuteChanged();
                ClearWslRunnerOutputCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string WslRunnerCommandInput
    {
        get => _wslRunnerCommandInput;
        set => SetProperty(ref _wslRunnerCommandInput, value);
    }

    public bool IsWslRunnerRunning => _isWslRunnerBusy;

    public string WindowsSandboxStatusText
    {
        get => _windowsSandboxStatusText;
        private set => SetProperty(ref _windowsSandboxStatusText, value);
    }

    public string LinkSafetyUrlInput
    {
        get => _linkSafetyUrlInput;
        set
        {
            if (SetProperty(ref _linkSafetyUrlInput, value))
            {
                FetchLinkSafetyHeadersCommand.RaiseCanExecuteChanged();
                DownloadLinkToQuarantineCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string LinkSafetyResultText
    {
        get => _linkSafetyResultText;
        private set => SetProperty(ref _linkSafetyResultText, value);
    }

    public string LocalFileSafetyPath
    {
        get => _localFileSafetyPath;
        set
        {
            if (SetProperty(ref _localFileSafetyPath, value))
            {
                AnalyzeLocalFileSafetyCommand.RaiseCanExecuteChanged();
                CopyLocalFileToQuarantineCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string LocalFileSafetyResultText
    {
        get => _localFileSafetyResultText;
        private set => SetProperty(ref _localFileSafetyResultText, value);
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
                ProgressItemName = "toolkit health scan",
                HeartbeatKind = PowerShellHeartbeatKind.LongRunningScan
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
        var userText = CopilotInput.Trim();
        if (string.IsNullOrWhiteSpace(userText))
        {
            return;
        }

        CopilotMessages.Add(new CopilotChatMessage
        {
            Role = "You",
            Text = userText
        });

        CopilotInput = string.Empty;
        KyraSlashPopupOpen = false;
        KyraSlashSuggestions.Clear();

        var reportPath = Path.Combine(GetRuntimeReportsDirectory(), "system-intelligence-latest.json");
        var toolkitReportPath = Path.Combine(GetRuntimeReportsDirectory(), "toolkit-health-latest.json");
        CopilotResponse response;
        _copilotGenerationCancellation?.Dispose();
        _copilotGenerationCancellation = new CancellationTokenSource();
        IsCopilotGenerating = true;
        KyraActivityStatusText = "Working…";
        try
        {
            var parse = KyraSlashCommandParser.Parse(userText);
            if (parse.IsSlashCommand)
            {
                ReportKyraActivity("Reading command…");
                if (KyraLiveSlashCoordinator.IsLiveDataSlash(parse.MatchedCommand))
                {
                    ReportKyraActivity("Checking configured tool…");
                    var uiSettings = BuildCopilotSettingsFromUi();
                    var liveFacts = BuildKyraToolHostFacts(reportPath, toolkitReportPath, uiSettings);
                    var liveRoute = await KyraLiveSlashCoordinator.ExecuteLiveAsync(
                        parse,
                        uiSettings,
                        liveFacts,
                        _copilotGenerationCancellation.Token);
                    var liveResp = liveRoute.ToCopilotResponse();
                    if (liveResp is not null)
                    {
                        ReportKyraActivity("Formatting Kyra response…");
                        response = liveResp;
                        ReportKyraActivity("Done.");
                    }
                    else
                    {
                        response = new CopilotResponse
                        {
                            Text = "Live tool returned no text. Try `/provider`.",
                            ProviderType = CopilotProviderType.LocalOffline,
                            OnlineStatus = "Local live tool",
                            SourceLabel = "Kyra · live tool"
                        };
                        ReportKyraActivity("Done.");
                    }
                }
                else
                {
                    var route = KyraSlashCommandRouter.Handle(parse, BuildKyraSlashHostSnapshot());
                    var inline = route.ToCopilotResponse();
                    if (inline is not null)
                    {
                        ReportKyraActivity("Formatting Kyra response…");
                        response = inline;
                        ReportKyraActivity("Done.");
                    }
                    else if (!string.IsNullOrWhiteSpace(route.ForwardPrompt))
                    {
                        ReportKyraActivity(DescribeKyraLlmPhase(route.ForwardPrompt));
                        var req = CreateKyraCopilotRequest(route.ForwardPrompt, reportPath, toolkitReportPath);
                        response = await _copilotService.GenerateReplyAsync(req, _copilotGenerationCancellation.Token);
                    }
                    else
                    {
                        response = new CopilotResponse
                        {
                            Text = "That command didn’t produce a response. Try `/help`.",
                            ProviderType = CopilotProviderType.LocalOffline,
                            OnlineStatus = "Local command routing",
                            SourceLabel = "Kyra · command"
                        };
                        ReportKyraActivity("Done.");
                    }
                }
            }
            else
            {
                ReportKyraActivity(DescribeKyraLlmPhase(userText));
                var req = CreateKyraCopilotRequest(userText, reportPath, toolkitReportPath);
                response = await _copilotService.GenerateReplyAsync(req, _copilotGenerationCancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
            response = new CopilotResponse
            {
                Text = "Stopped. Kyra did not change anything.",
                ProviderType = CopilotProviderType.LocalOffline,
                OnlineStatus = "Offline fallback - stopped by user."
            };
        }
        catch (Exception exception)
        {
            response = new CopilotResponse
            {
                Text = $"Kyra hit an error and fell back safely: {exception.Message}",
                ProviderType = CopilotProviderType.LocalOffline,
                OnlineStatus = "Error - offline fallback available."
            };
        }
        finally
        {
            IsCopilotGenerating = false;
            KyraActivityStatusText = string.Empty;
        }

        CopilotMessages.Add(new CopilotChatMessage
        {
            Role = "Kyra",
            Text = FormatKyraResponseText(response),
            SourceLabel = response.SourceLabel
        });

        ApplyCopilotOnlineIndicator(response);
        SaveCopilotSettings();
        if (VerboseLiveLogs)
        {
            foreach (var note in response.ProviderNotes)
            {
                if (note.StartsWith("Kyra routing:", StringComparison.OrdinalIgnoreCase))
                {
                    AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] " + note, LogSeverity.Info, channel: LiveLogChannel.KyraDetail));
                }
            }
        }

        AppendLog(new LogLine(
            DateTimeOffset.Now,
            response.UsedOnlineData ? "[INFO] Kyra answered with sanitized online provider data." : "[INFO] Kyra answered from local/offline fallback context.",
            LogSeverity.Info,
            channel: LiveLogChannel.KyraDetail));
    }

    private static string FormatKyraResponseText(CopilotResponse response)
    {
        var t = response.Text ?? string.Empty;
        if (response.ActionSuggestions is not { Count: > 0 })
        {
            return t;
        }

        var sb = new StringBuilder(t);
        sb.AppendLine().AppendLine("Suggested next steps:");
        var n = 1;
        foreach (var a in response.ActionSuggestions)
        {
            var line = string.IsNullOrWhiteSpace(a.Description) ? a.Title : $"{a.Title} — {a.Description}";
            var safety = a.SafetyLevel switch
            {
                KyraActionSafetyLevel.Caution => " (caution)",
                KyraActionSafetyLevel.Destructive =>
                    a.RequiresConfirmation ? " (needs confirmation)" : " (destructive)",
                _ => string.Empty
            };
            var cat = string.IsNullOrWhiteSpace(a.Category) ? string.Empty : $" [{a.Category}]";
            sb.AppendLine($"{n}. {line}{cat}{safety}");
            n++;
        }

        return sb.ToString().TrimEnd();
    }

    private void ReportKyraActivity(string message)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null)
        {
            KyraActivityStatusText = message;
            return;
        }

        _ = d.BeginInvoke(() => KyraActivityStatusText = message, DispatcherPriority.Background);
    }

    private CopilotRequest CreateKyraCopilotRequest(string prompt, string reportPath, string toolkitReportPath) =>
        new()
        {
            Prompt = prompt,
            SystemIntelligenceReportPath = reportPath,
            ToolkitHealthReportPath = toolkitReportPath,
            AppVersion = GetType().Assembly.GetName().Version?.ToString() ?? "unknown",
            RecentLogLines = Logs.Select(line => line.DisplayText).TakeLast(24).ToArray(),
            SelectedUsbTarget = SelectedUsbTarget,
            Settings = BuildCopilotSettingsFromUi(),
            VerboseDiagnosticNotes = VerboseLiveLogs,
            KyraMemorySummaryForPrompt = BuildKyraMemorySummaryForPrompt(),
            KyraActivityStatusCallback = ReportKyraActivity
        };

    private string DescribeKyraLlmPhase(string forwardPrompt)
    {
        if (forwardPrompt.Contains("weather", StringComparison.OrdinalIgnoreCase) ||
            forwardPrompt.Contains("Latest news", StringComparison.OrdinalIgnoreCase) ||
            forwardPrompt.Contains("Stock price", StringComparison.OrdinalIgnoreCase) ||
            forwardPrompt.Contains("Crypto price", StringComparison.OrdinalIgnoreCase) ||
            forwardPrompt.Contains("Sports", StringComparison.OrdinalIgnoreCase))
        {
            return "Checking configured tools…";
        }

        if (forwardPrompt.Contains("System Intelligence", StringComparison.OrdinalIgnoreCase))
        {
            return "Checking system context…";
        }

        if (_copilotSettings.ApiFirstRouting)
        {
            return "Asking API provider…";
        }

        return "Thinking locally…";
    }

    public void InsertKyraSlashSuggestion(string commandLine)
    {
        _kyraSlashPopupQuietUntilUtc = DateTime.UtcNow.AddMilliseconds(400);
        CopilotInput = string.IsNullOrWhiteSpace(commandLine) ? "/" : commandLine.TrimEnd() + " ";
        KyraSlashSuggestions.Clear();
        KyraSlashSelectedIndex = -1;
        KyraSlashPopupOpen = false;
    }

    public void ApplyKyraSlashSelection()
    {
        if (KyraSlashSelectedIndex >= 0 && KyraSlashSelectedIndex < KyraSlashSuggestions.Count)
        {
            InsertKyraSlashSuggestion(KyraSlashSuggestions[KyraSlashSelectedIndex]);
        }
    }

    private void RefreshKyraSlashSuggestions()
    {
        if (DateTime.UtcNow < _kyraSlashPopupQuietUntilUtc)
        {
            return;
        }

        KyraSlashSuggestions.Clear();
        var t = CopilotInput ?? string.Empty;
        if (!t.StartsWith('/'))
        {
            KyraSlashPopupOpen = false;
            KyraSlashSelectedIndex = -1;
            return;
        }

        var firstToken = t.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "/";
        var filter = firstToken.Length > 1 ? firstToken[1..] : string.Empty;

        foreach (var c in KyraSlashCommandRegistry.All.OrderBy(x => x.Name))
        {
            if (string.IsNullOrEmpty(filter) ||
                c.Name.StartsWith(filter, StringComparison.OrdinalIgnoreCase) ||
                c.Aliases.Any(a => a.StartsWith(filter, StringComparison.OrdinalIgnoreCase)))
            {
                KyraSlashSuggestions.Add("/" + c.Name);
            }
        }

        if (KyraSlashSuggestions.Count == 0 && !string.IsNullOrEmpty(filter))
        {
            KyraSlashSuggestions.Add("/help");
        }

        KyraSlashPopupOpen = KyraSlashSuggestions.Count > 0;
        KyraSlashSelectedIndex = KyraSlashSuggestions.Count > 0 ? 0 : -1;
    }

    private void RefreshKyraQuickPromptVisibilities()
    {
        var scan = File.Exists(Path.Combine(GetRuntimeReportsDirectory(), "system-intelligence-latest.json"));
        if (_kyraHasSystemScanReport != scan)
        {
            _kyraHasSystemScanReport = scan;
            OnPropertyChanged(nameof(KyraListingQuickButtonVisibility));
        }

        var warn = Logs.Any(l => l.Severity is LogSeverity.Warning or LogSeverity.Error);
        if (_kyraHasRecentWarningLog != warn)
        {
            _kyraHasRecentWarningLog = warn;
            OnPropertyChanged(nameof(KyraWarningQuickButtonVisibility));
        }

        var toolkitReport = Path.Combine(GetRuntimeReportsDirectory(), "toolkit-health-latest.json");
        var hasToolkit = File.Exists(toolkitReport);
        var loc = _copilotSettings?.LiveTools?.DefaultWeatherLocation?.Trim();
        var facts = new KyraToolHostFacts
        {
            HasSystemIntelligenceScan = scan,
            HasToolkitHealthReport = hasToolkit,
            DefaultWeatherLocation = string.IsNullOrEmpty(loc) ? null : loc
        };
        var liveOk = new KyraToolRegistry().HasConfiguredLiveDataCapability(_copilotSettings ?? new CopilotSettings(), facts);
        if (_kyraShowLiveToolsQuickButton != liveOk)
        {
            _kyraShowLiveToolsQuickButton = liveOk;
            OnPropertyChanged(nameof(KyraLiveToolsQuickButtonVisibility));
        }
    }

    private KyraSlashHostSnapshot BuildKyraSlashHostSnapshot()
    {
        var reportPath = Path.Combine(GetRuntimeReportsDirectory(), "system-intelligence-latest.json");
        var toolkitReportPath = Path.Combine(GetRuntimeReportsDirectory(), "toolkit-health-latest.json");
        var profile = CopilotService.TryLoadSystemProfileFromReport(reportPath);
        var health = SystemHealthEvaluator.Evaluate(profile);

        var usbLine = SelectedUsbTarget is { } u
            ? $"{u.DisplayName}; safety={u.SafetyStatusText}; {u.SafetyReasonText}"
            : "No USB target selected.";

        var missing = ToolkitHealthItems.Count(x =>
            x.Status.Contains("MISSING", StringComparison.OrdinalIgnoreCase));
        var manual = ToolkitHealthItems.Count(x =>
            x.Status.Contains("MANUAL", StringComparison.OrdinalIgnoreCase));
        var installed = ToolkitHealthItems.Count(x =>
            x.Status.Contains("INSTALLED", StringComparison.OrdinalIgnoreCase) ||
            x.Status.Contains("READY", StringComparison.OrdinalIgnoreCase));
        var toolkitLine =
            $"{ToolkitLastScanText}; tracked={ToolkitHealthItems.Count}; installed/ready≈{installed}; missing≈{missing}; manual≈{manual}; {ToolkitHealthVerdictText}";

        var warn = Logs.LastOrDefault(l => l.Severity is LogSeverity.Warning or LogSeverity.Error);

        return new KyraSlashHostSnapshot
        {
            LogsRoot = _appRuntimeService.LogsRoot,
            RuntimeRoot = _appRuntimeService.RuntimeRoot,
            ApiFirstRouting = KyraApiFirstRouting,
            OfflineFallbackEnabled = KyraOfflineFallbackEnabled,
            ModeDisplayName = SelectedCopilotMode,
            ActiveProviderSummary = CopilotActiveProviderText + Environment.NewLine + CopilotProviderSummaryText,
            ToolStatusSummary = new KyraToolRegistry().BuildStatusSummary(),
            MemoryEnabled = KyraPersistentMemoryEnabled,
            VerboseLiveLogs = VerboseLiveLogs,
            HasSystemIntelligenceScan = File.Exists(reportPath),
            HasToolkitHealthReport = File.Exists(toolkitReportPath),
            ToolSettings = BuildCopilotSettingsFromUi(),
            UsbSummaryLine = usbLine,
            ToolkitSummaryLine = toolkitLine,
            LatestWarningSnippet = warn?.DisplayText ?? string.Empty,
            SystemProfile = profile,
            Health = health,
            OpenLogsFolder = () => OpenFolder(_appRuntimeService.LogsRoot, "logs folder", createIfMissing: true),
            NavigateToSettingsTab = () => MainTabNavigationAction?.Invoke("Settings"),
            NavigateToSystemIntelligenceTab = () => MainTabNavigationAction?.Invoke("System Intelligence"),
            ClearChatHistory = () =>
            {
                _copilotService.ClearMemory();
                CopilotMessages.Clear();
            },
            ClearKyraMemoryConfirmed = () =>
            {
                try
                {
                    new KyraPersistentMemoryStore(_kyraMemoryPath).Clear();
                    AppendLog(new LogLine(DateTimeOffset.Now, "[OK] Kyra memory cleared (slash command).", LogSeverity.Success));
                }
                catch (Exception ex)
                {
                    AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Kyra memory clear failed: {ex.Message}", LogSeverity.Warning));
                }
            },
            ExportKyraMemory = ExportKyraMemory,
            SetKyraMemoryEnabled = on =>
            {
                if (KyraPersistentMemoryEnabled != on)
                {
                    KyraPersistentMemoryEnabled = on;
                }
            },
            BuildSanitizedMemoryPreview = () =>
            {
                var store = new KyraPersistentMemoryStore(_kyraMemoryPath);
                var doc = store.Load();
                KyraPersistentMemoryStore.SanitizeInPlace(doc);
                return JsonSerializer.Serialize(doc, IndentedJsonOptions);
            }
        };
    }

    private static KyraToolHostFacts BuildKyraToolHostFacts(string reportPath, string toolkitReportPath, CopilotSettings settings)
    {
        var loc = settings.LiveTools?.DefaultWeatherLocation?.Trim();
        return new KyraToolHostFacts
        {
            HasSystemIntelligenceScan = File.Exists(reportPath),
            HasToolkitHealthReport = File.Exists(toolkitReportPath),
            DefaultWeatherLocation = string.IsNullOrEmpty(loc) ? null : loc
        };
    }

    private void StopCopilotGeneration()
    {
        _copilotGenerationCancellation?.Cancel();
        AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] Kyra stop requested.", LogSeverity.Info));
    }

    private void ToggleCopilotTechnicalContext()
    {
        var expanded = CopilotTechnicalContextVisibility != Visibility.Visible;
        CopilotTechnicalContextVisibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        CopilotTechnicalContextButtonText = expanded ? "Hide technical context" : "View technical context";
    }

    private void OpenKyraAdvancedSettings()
    {
        OpenKyraAdvancedSettingsAction?.Invoke();
    }

    public void RefreshKyraAssistantPanel()
    {
        var reportPath = Path.Combine(GetRuntimeReportsDirectory(), "system-intelligence-latest.json");
        var toolkitPath = Path.Combine(GetRuntimeReportsDirectory(), "toolkit-health-latest.json");
        var ctx = new CopilotContextBuilder().Build(new CopilotRequest
        {
            Prompt = ".",
            SystemIntelligenceReportPath = reportPath,
            ToolkitHealthReportPath = toolkitPath,
            SelectedUsbTarget = SelectedUsbTarget,
            Settings = new CopilotSettings
            {
                UseLatestSystemScanContext = true,
                RedactContextEnabled = true,
                KyraPersistentMemoryEnabled = _copilotSettings.KyraPersistentMemoryEnabled
            }
        });
        KyraSanitizedContextPreviewText = KyraPrivacyGate.BuildSanitizedProviderSummary(ctx);
        var toolStatus = new KyraToolRegistry().BuildStatusSummary();
        var sb = new StringBuilder();
        sb.AppendLine(_copilotSettings.ApiFirstRouting ? "API-first routing: on (online before Local Kyra when allowed)." : "API-first routing: off (local draft first when polish mode applies).");
        sb.AppendLine(_copilotSettings.OfflineFallbackEnabled ? "Local fallback: enabled." : "Local fallback: disabled.");
        sb.AppendLine(_copilotSettings.AllowOnlineSystemContextSharing ? "System context to APIs: on (sanitized summary only)." : "System context to APIs: off.");
        sb.AppendLine(ctx.SystemProfile is not null ? "System context: available from last scan." : "System context: run System Intelligence for machine-specific answers.");
        sb.AppendLine(_copilotSettings.KyraPersistentMemoryEnabled ? "Kyra memory: enabled (local disk, user-controlled)." : "Kyra memory: off.");
        sb.AppendLine(VerboseLiveLogs ? "Verbose Kyra notes: on." : "Verbose Kyra notes: off (routing noise hidden in chat footnotes).");
        sb.AppendLine(toolStatus);
        KyraAssistantStatusSummary = sb.ToString().TrimEnd();

        var locPanel = _copilotSettings?.LiveTools?.DefaultWeatherLocation?.Trim();
        var factsPanel = new KyraToolHostFacts
        {
            HasSystemIntelligenceScan = File.Exists(reportPath),
            HasToolkitHealthReport = File.Exists(toolkitPath),
            DefaultWeatherLocation = string.IsNullOrEmpty(locPanel) ? null : locPanel
        };
        var reg = new KyraToolRegistry();
        KyraToolStatusRows.Clear();
        foreach (var row in reg.BuildStatusGridRows(BuildCopilotSettingsFromUi(), factsPanel))
        {
            KyraToolStatusRows.Add(row);
        }

        RefreshKyraQuickPromptVisibilities();
    }

    private string? BuildKyraMemorySummaryForPrompt()
    {
        if (!_copilotSettings.KyraPersistentMemoryEnabled)
        {
            return null;
        }

        var store = new KyraPersistentMemoryStore(_kyraMemoryPath);
        var doc = store.Load();
        doc.Enabled = _copilotSettings.KyraPersistentMemoryEnabled;
        return store.BuildPromptHint(doc);
    }

    private void ExportKyraMemory()
    {
        try
        {
            var store = new KyraPersistentMemoryStore(_kyraMemoryPath);
            var doc = store.Load();
            var dlg = new SaveFileDialog
            {
                Filter = "JSON (*.json)|*.json",
                FileName = "kyra-memory-export.json"
            };
            if (dlg.ShowDialog() == true)
            {
                KyraPersistentMemoryStore.SanitizeInPlace(doc);
                File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(doc, IndentedJsonOptions));
                AppendLog(new LogLine(DateTimeOffset.Now, "[OK] Exported Kyra memory (sanitized).", LogSeverity.Success));
            }
        }
        catch (Exception exception)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Kyra memory export failed: {exception.Message}", LogSeverity.Warning));
        }
    }

    private void ClearKyraMemory()
    {
        try
        {
            new KyraPersistentMemoryStore(_kyraMemoryPath).Clear();
            AppendLog(new LogLine(DateTimeOffset.Now, "[OK] Kyra memory cleared from disk.", LogSeverity.Success));
        }
        catch (Exception exception)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Kyra memory clear failed: {exception.Message}", LogSeverity.Warning));
        }
    }

    private void ViewKyraMemory()
    {
        try
        {
            var doc = new KyraPersistentMemoryStore(_kyraMemoryPath).Load();
            KyraPersistentMemoryStore.SanitizeInPlace(doc);
            var json = JsonSerializer.Serialize(doc, IndentedJsonOptions);
            MessageBox.Show(
                string.IsNullOrWhiteSpace(json) ? "{}" : json,
                "Kyra memory (sanitized view)",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Kyra memory", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CopyBetaReportTemplate()
    {
        var text =
            $"Version: {AppVersionText}{Environment.NewLine}" +
            "What happened:" +
            Environment.NewLine +
            Environment.NewLine +
            "Steps:" +
            Environment.NewLine +
            Environment.NewLine +
            "Screenshot/logs attached:" +
            Environment.NewLine;
        Clipboard.SetText(text);
        AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] Copied beta report template to clipboard.", LogSeverity.Info));
    }

    private async Task TestCopilotConnectionAsync()
    {
        RefreshCopilotProviderStatus();
        var settings = _copilotSettings ?? BuildCopilotSettingsFromUi();
        var ollama = settings.Providers.TryGetValue("ollama-local", out var ollamaConfig) ? ollamaConfig : null;
        if (ollama?.IsEnabled == true)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                using var response = await client.GetAsync($"{ollama.BaseUrl.TrimEnd('/')}/api/tags").ConfigureAwait(true);
                CopilotOnlineStatusText = response.IsSuccessStatusCode
                    ? "Ollama Available: local model endpoint responded."
                    : "Ollama selected, but the local endpoint did not respond successfully.";
                UpdateCopilotOnlineIndicator();
                return;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                CopilotOnlineStatusText = "Ollama not reachable. Offline Kyra is still ready.";
                UpdateCopilotOnlineIndicator();
                return;
            }
        }

        var lmStudio = settings.Providers.TryGetValue("lm-studio-local", out var lmStudioConfig) ? lmStudioConfig : null;
        if (lmStudio?.IsEnabled == true)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                using var response = await client.GetAsync($"{lmStudio.BaseUrl.TrimEnd('/')}/models").ConfigureAwait(true);
                CopilotOnlineStatusText = response.IsSuccessStatusCode
                    ? "LM Studio Available: local model endpoint responded."
                    : "LM Studio selected, but the local endpoint did not respond successfully.";
                UpdateCopilotOnlineIndicator();
                return;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                CopilotOnlineStatusText = "LM Studio not reachable. Offline Kyra is still ready.";
                UpdateCopilotOnlineIndicator();
                return;
            }
        }

        var openAi = settings.Providers.TryGetValue("openai-compatible", out var openAiConfig) ? openAiConfig : null;
        if (openAi?.IsEnabled == true)
        {
            var envVar = string.IsNullOrWhiteSpace(openAi.ApiKeyEnvironmentVariable)
                ? "OPENAI_API_KEY"
                : openAi.ApiKeyEnvironmentVariable;
            var hasKey = !string.IsNullOrWhiteSpace(KyraApiKeyStore.ResolveApiKey("openai-compatible", openAi));
            CopilotOnlineStatusText = hasKey
                ? "OpenAI-compatible provider: key present (session or environment). Kyra exercises the endpoint on send."
                : $"OpenAI-compatible: key not found. Enter a session key or set {envVar}.";
            AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] " + CopilotOnlineStatusText, LogSeverity.Info));
            UpdateCopilotOnlineIndicator();
            return;
        }

        AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] Kyra connection test — scanning enabled providers…", LogSeverity.Info));
        var lines = new List<string>();
        foreach (var provider in _copilotProviderRegistry.Providers)
        {
            if (!settings.Providers.TryGetValue(provider.Id, out var cfg) || !cfg.IsEnabled)
            {
                continue;
            }

            if (CopilotProviderStatusFormatter.IsPlaceholderProvider(provider))
            {
                lines.Add($"{provider.DisplayName}: placeholder / future — not active for live API.");
                continue;
            }

            if (!provider.IsOnlineProvider)
            {
                lines.Add($"{provider.DisplayName}: local/offline — no API key required.");
                continue;
            }

            var env = string.IsNullOrWhiteSpace(cfg.ApiKeyEnvironmentVariable)
                ? provider.DefaultApiKeyEnvironmentVariable
                : cfg.ApiKeyEnvironmentVariable;
            var hasSession = !string.IsNullOrWhiteSpace(KyraApiKeyStore.GetSessionKey(provider.Id));
            var hasEnv = !string.IsNullOrWhiteSpace(env) &&
                         !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(env));

            if (!provider.IsConfigured(cfg))
            {
                lines.Add(string.IsNullOrWhiteSpace(env)
                    ? $"{provider.DisplayName}: not configured — check Base URL / model / session key."
                    : $"{provider.DisplayName}: key not found. Enter a session key or set {env}.");
                continue;
            }

            if (provider is OpenAiStyleCopilotProvider)
            {
                lines.Add(hasSession
                    ? $"{provider.DisplayName}: configured for this session; Kyra will attempt chat on send."
                    : hasEnv
                        ? $"{provider.DisplayName}: configured via environment variable {env}; Kyra will attempt chat on send."
                        : $"{provider.DisplayName}: configured; Kyra will attempt chat on send.");
            }
            else
            {
                lines.Add($"{provider.DisplayName}: configured.");
            }
        }

        foreach (var line in lines)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] " + line, LogSeverity.Info));
        }

        CopilotOnlineStatusText = lines.Count == 0
            ? "No enabled online providers to test. Local Kyra is active. Online providers are optional."
            : "Connection test finished — see Full Logs for each provider line.";
        UpdateCopilotOnlineIndicator();
        await Task.CompletedTask.ConfigureAwait(true);
    }

    private void UseLatestSystemScanContextNow()
    {
        UseLatestSystemScanContext = true;
        LoadSystemIntelligenceReport();
        RefreshCopilotContextText();
        AppendLog(new LogLine(DateTimeOffset.Now, "[OK] Kyra context refreshed from latest local System Intelligence report.", LogSeverity.Success));
    }

    private void ClearCopilotHistoryAndCache()
    {
        _copilotService.ClearMemory();
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
            AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Kyra cache cleanup skipped: {exception.Message}", LogSeverity.Warning));
        }

        AppendLog(new LogLine(DateTimeOffset.Now, "[OK] Kyra history/cache cleared.", LogSeverity.Success));
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
            RefreshCopilotContextText();
            RefreshKyraQuickPromptVisibilities();
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
                var secureBoot = GetJsonProviderDisplay(summary, "secureBootInfo", FormatNullableBool(GetJsonNullableBool(summary, "secureBoot")));
                var tpm = GetJsonProviderDisplay(summary, "tpmInfo", $"Present {FormatNullableBool(GetJsonNullableBool(summary, "tpmPresent"))}, Ready {FormatNullableBool(GetJsonNullableBool(summary, "tpmReady"))}");
                var serviceTag = GetJsonString(summary, "serviceTag", "UNKNOWN");
                var licenseChannel = GetJsonProviderDisplay(summary, "windowsLicense", GetJsonString(summary, "windowsLicenseChannel", "UNKNOWN"));
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
                var ramInstalled = GetJsonString(summary, "ramInstalledDisplay", ram);
                var ramConfiguredSpeed = GetJsonString(summary, "ramConfiguredSpeedDisplay", GetJsonString(summary, "ramSpeed", "Configured speed not reported"));
                var ramRatedSpeed = GetJsonString(summary, "ramModuleRatedSpeedDisplay", "Module rated speed not reported");
                var ramSlots = GetJsonString(summary, "ramSlotsDisplay", $"{GetJsonString(summary, "ramSlotsUsed", "UNKNOWN")}/{GetJsonString(summary, "ramSlotsTotal", "UNKNOWN")}");
                var ramUpgradePath = GetJsonString(summary, "ramUpgradePath", "UNKNOWN");
                var gpus = GetJsonGpuDisplayArray(summary, "gpus");
                SystemIntelligenceSummaryText = $"{computerName} | {model} | {os} | {cpu} | RAM: {ramInstalled} | GPU: {FormatList(gpus, "Unknown GPU")}";
                SystemIntelligenceSystemCardText =
                    $"PC: {computerName}{Environment.NewLine}" +
                    $"Model: {model}{Environment.NewLine}" +
                    $"Service tag / serial: {serviceTag}{Environment.NewLine}" +
                    $"Windows: {os} (build {osBuild}){Environment.NewLine}" +
                    $"License channel: {licenseChannel}{Environment.NewLine}" +
                    $"BIOS: {bios} ({biosDate}){Environment.NewLine}" +
                    $"Secure Boot: {secureBoot}{Environment.NewLine}" +
                    $"TPM: {tpm}{Environment.NewLine}" +
                    $"Last boot: {lastBoot}{Environment.NewLine}" +
                    $"Uptime: {uptime}";
                SystemIntelligenceComputeCardText =
                    $"CPU: {cpu}{Environment.NewLine}" +
                    $"Cores / threads: {cores} / {logicalProcessors}{Environment.NewLine}" +
                    $"Clock: base {baseClock} MHz, max {maxClock} MHz{Environment.NewLine}" +
                    $"RAM: {ramInstalled}; used {ramUsed} ({ramUsedPercent}%){Environment.NewLine}" +
                    $"Configured speed: {ramConfiguredSpeed}; rated speed: {ramRatedSpeed}{Environment.NewLine}" +
                    $"{ramSlots}; {ramUpgradePath}{Environment.NewLine}" +
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

            RefreshKyraQuickPromptVisibilities();
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
            RefreshKyraQuickPromptVisibilities();
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
            ToolkitLastScanText = $"Last scan: {FormatGeneratedUtc(GetJsonString(root, "generatedUtc", string.Empty))}";
            ToolkitManualExplanationText = GetJsonString(root, "manualItemsExplanation", ToolkitManualExplanationText);

            var functionalHealthy = missing == 0 && failed == 0 && updates == 0;
            var verdictUpper = healthVerdict.ToUpperInvariant();
            if (functionalHealthy && verdictUpper.Contains("MANUAL", StringComparison.Ordinal))
            {
                ToolkitHealthVerdictText =
                    "Health Verdict: MANUAL ACTION NEEDED — Toolkit is usable. Some optional/manual tools still require user-provided downloads or placeholders.";
                ToolkitStatusText = healthVerdict;
                ApplyStatusBrushes(
                    "READY",
                    (background, border, foreground) =>
                    {
                        ToolkitStatusBackground = background;
                        ToolkitStatusBorderBrush = border;
                        ToolkitStatusForeground = foreground;
                    });
            }
            else
            {
                ToolkitHealthVerdictText = $"Health Verdict: {healthVerdict}";
                ToolkitStatusText = healthVerdict;
                ApplyStatusBrushes(
                    healthVerdict,
                    (background, border, foreground) =>
                    {
                        ToolkitStatusBackground = background;
                        ToolkitStatusBorderBrush = border;
                        ToolkitStatusForeground = foreground;
                    });
            }

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
            .Select(disk => $"{GetJsonString(disk, "name", "Disk")} | {GetJsonString(disk, "interfaceType", "UNKNOWN")} {GetJsonString(disk, "mediaType", "UNKNOWN")} | {GetJsonString(disk, "size", "UNKNOWN")} | health {GetJsonString(disk, "healthDisplay", GetJsonString(disk, "health", "Health not reported"))} | temp {GetJsonString(disk, "temperatureDisplay", "Temp unavailable - drive does not expose sensor")} | wear {GetJsonString(disk, "wearDisplay", "Wear unavailable - drive does not expose life counter")} ({GetJsonString(disk, "status", "UNKNOWN")})")
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
            .Select(battery => $"{GetJsonString(battery, "name", "Battery")} {GetJsonInt(battery, "estimatedChargeRemaining")}% charge, design {GetJsonString(battery, "designCapacityDisplay", "Design capacity not reported")}, full {GetJsonString(battery, "fullChargeCapacityDisplay", "Full charge capacity not reported")}, wear {GetJsonString(battery, "wearDisplay", "Wear unavailable")}, cycles {GetJsonString(battery, "cycleCountDisplay", "Cycle count not reported")}, AC {FormatNullableBool(GetJsonNullableBool(battery, "acConnected"))} ({GetJsonString(battery, "healthDisplay", GetJsonString(battery, "status", "UNKNOWN"))})")
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
        var internet = GetJsonString(network, "internetDisplay", GetJsonBool(network, "internetCheck") ? "Internet: Working" : "Internet: Check failed");
        var defaultRoute = GetJsonProviderDisplay(network, "defaultRoute", "Default route: not detected");
        var virtualIgnored = GetJsonString(network, "virtualAdaptersIgnored", "Virtual adapters ignored: none");
        var adapterProperty = network.TryGetProperty("physicalAdapters", out var physicalAdapters) && physicalAdapters.ValueKind == JsonValueKind.Array
            ? physicalAdapters
            : network.TryGetProperty("adapters", out var allAdapters)
                ? allAdapters
                : default;
        if (adapterProperty.ValueKind != JsonValueKind.Array || adapterProperty.GetArrayLength() == 0)
        {
            return $"Network: {status}. {internet}. {defaultRoute}. No active physical adapter detected. {virtualIgnored}.";
        }

        var parts = adapterProperty.EnumerateArray()
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
                var wifi = GetJsonString(adapter, "wifiDisplay", "Wi-Fi not connected");
                var apipa = FormatNullableBool(GetJsonNullableBool(adapter, "apipaDetected"));
                return $"{GetJsonString(adapter, "name", GetJsonString(adapter, "description", "Adapter"))} | {GetJsonString(adapter, "adapterRole", "Physical adapter")} | IP {ips} | GW {gateways} | DNS {dns} | Wi-Fi {wifi} | APIPA {apipa}";
            })
            .Take(3)
            .ToArray();
        return $"Network: {status}. {internet}. {defaultRoute}. {FormatList(parts, "No active physical adapter detected")}. {virtualIgnored}.";
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
        var bitLocker = security.TryGetProperty("bitLockerSummary", out _)
            ? GetJsonProviderDisplay(security, "bitLockerSummary", "unavailable")
            : security.TryGetProperty("bitLockerVolumes", out var bitLockerVolumes) && bitLockerVolumes.ValueKind == JsonValueKind.Array
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
        var missingInfo = flipValue.TryGetProperty("missingInfoNeeded", out var missingInfoArray) && missingInfoArray.ValueKind == JsonValueKind.Array
            ? FormatList(missingInfoArray.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Take(3), "none")
            : "none";
        var apiStatus = providerStatus.Contains("not configured", StringComparison.OrdinalIgnoreCase)
            ? "Offline estimate only"
            : "Comps provider configured";
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
            $"Comps/API status: {apiStatus}; missing info: {missingInfo}{Environment.NewLine}" +
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
            CopilotContextSummaryText = BuildCopilotFriendlyContextSummary(null);
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
            CopilotContextSummaryText = BuildCopilotFriendlyContextSummary(null);
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
                $"RAM: {GetJsonString(summary, "ramInstalledDisplay", GetJsonString(summary, "ramTotal", "Unknown RAM"))} @ {GetJsonString(summary, "ramConfiguredSpeedDisplay", GetJsonString(summary, "ramSpeed", "Configured speed not reported"))}{Environment.NewLine}" +
                $"GPU: {FormatList(GetJsonGpuDisplayArray(summary, "gpus"), "Unknown GPU")}{Environment.NewLine}" +
                $"Battery: {SystemIntelligenceBatteryText}{Environment.NewLine}" +
                $"Storage: {SystemIntelligenceDiskHealthText}";
        }

        CopilotContextText = BuildCopilotUsbContext(summaryText);
        CopilotContextSummaryText = root.TryGetProperty("summary", out var friendlySummary)
            ? BuildCopilotFriendlyContextSummary(friendlySummary)
            : BuildCopilotFriendlyContextSummary(null);
    }

    private string BuildCopilotFriendlyContextSummary(JsonElement? summary)
    {
        var device = summary.HasValue
            ? $"{GetJsonString(summary.Value, "manufacturer", "Unknown")} {GetJsonString(summary.Value, "model", string.Empty)}".Trim()
            : "Run System Intelligence";
        var cpu = summary.HasValue ? GetJsonString(summary.Value, "cpu", "Unknown") : "Unknown";
        var ram = summary.HasValue
            ? GetJsonString(summary.Value, "ramInstalledDisplay", GetJsonString(summary.Value, "ramTotal", "Unknown"))
            : "Unknown";
        var gpu = summary.HasValue ? FormatList(GetJsonGpuDisplayArray(summary.Value, "gpus"), "Unknown") : "Unknown";
        var storage = SystemIntelligenceStorageCardText.StartsWith("UNKNOWN", StringComparison.OrdinalIgnoreCase)
            ? "Run scan for storage health"
            : ShortenForSummary(SystemIntelligenceStorageCardText);
        var battery = SystemIntelligenceBatteryCardText.StartsWith("UNKNOWN", StringComparison.OrdinalIgnoreCase)
            ? "Run scan for battery health"
            : ShortenForSummary(SystemIntelligenceBatteryCardText);
        var usb = SelectedUsbTarget is null
            ? "none selected"
            : $"{SelectedUsbTarget.RootPath} {SelectedUsbTarget.LabelDisplay}; {SelectedUsbTarget.DisplayTotalBytes}; {SelectedUsbTarget.SelectionStatusText}";

        return
            $"System Context{Environment.NewLine}" +
            $"- Device: {device}{Environment.NewLine}" +
            $"- CPU: {cpu}{Environment.NewLine}" +
            $"- RAM: {ram}{Environment.NewLine}" +
            $"- GPU: {gpu}{Environment.NewLine}" +
            $"- Storage: {storage}{Environment.NewLine}" +
            $"- Battery: {battery}{Environment.NewLine}" +
            $"- USB: {usb}";
    }

    private string BuildCopilotUsbContext(string systemContext)
    {
        var usbContext = SelectedUsbTarget is null
            ? "Selected USB target: none"
            : $"Selected USB target: {SelectedUsbTarget.RootPath} {SelectedUsbTarget.LabelDisplay}; {SelectedUsbTarget.DisplayTotalBytes}; write {SelectedUsbTarget.WriteSpeedDisplayNormalized}; read {SelectedUsbTarget.ReadSpeedDisplayNormalized}; benchmark {SelectedUsbTarget.BenchmarkStatusDisplay}";

        return $"{systemContext}{Environment.NewLine}{usbContext}";
    }

    private static string ShortenForSummary(string value)
    {
        var normalized = value.Replace(Environment.NewLine, " ", StringComparison.Ordinal);
        return normalized.Length <= 130 ? normalized : normalized[..127] + "...";
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

    private static string GetJsonProviderDisplay(JsonElement element, string propertyName, string fallback)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Object)
        {
            return fallback;
        }

        return GetJsonString(property, "friendlyDisplayText", fallback);
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
                    var type = GetJsonString(item, "type", "GPU");
                    var driver = GetJsonString(item, "driverVersion", "UNKNOWN");
                    return $"{type}: {name} (driver {driver})";
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
            ActionWarningText = "This target is blocked. Do not select the small VTOYEFI/EFI partition. Select the large removable USB data partition.";
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
        ActionWarningText = "Only use a USB drive you are willing to modify. Do not select the small VTOYEFI partition. Double-check drive letter and size before continuing.";
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
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(DispatcherPriority.Normal, () => ApplyBenchmarkResult(target, result));
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

        _useLatestSystemScanContext = settings.UseLatestSystemScanContext;
        _allowOnlineSystemContextSharing = settings.AllowOnlineSystemContextSharing;
        _enableFreeProviderPool = settings.EnableFreeProviderPool;
        _enableByokProviders = settings.EnableByokProviders;
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
                IsPaidProvider = provider.IsPaidProvider,
                IsPlaceholder = CopilotProviderStatusFormatter.IsPlaceholderProvider(provider),
                BaseUrl = providerConfig.BaseUrl,
                ModelName = providerConfig.ModelName,
                ApiKeyEnvironmentVariable = providerConfig.ApiKeyEnvironmentVariable,
                MaskedApiKey = KyraApiKeyStore.Mask(KyraApiKeyStore.GetSessionKey(provider.Id)),
                ProviderStatusLabel = CopilotProviderStatusFormatter.BuildStatusLabel(provider, providerConfig),
                CredentialSourceText = CopilotProviderStatusFormatter.BuildCredentialSourceLine(provider, providerConfig)
            });
        }

        var localOllamaEnabled = CopilotProviderSettings.Any(item => item.IsEnabled && string.Equals(item.Id, "ollama-local", StringComparison.OrdinalIgnoreCase));
        var localLmStudioEnabled = CopilotProviderSettings.Any(item => item.IsEnabled && string.Equals(item.Id, "lm-studio-local", StringComparison.OrdinalIgnoreCase));
        var anyOnlineConfigured = CopilotProviderSettings.Any(item => item.IsEnabled &&
                                                                      item.IsConfigured &&
                                                                      item.Id != "local-offline" &&
                                                                      item.Id != "ollama-local" &&
                                                                      item.Id != "lm-studio-local");
        var normalizedMode = KyraModeConnectivity.NormalizeModeForAvailableProviders(settings.Mode, anyOnlineConfigured, localOllamaEnabled, localLmStudioEnabled);
        if (normalizedMode != settings.Mode)
        {
            settings.Mode = normalizedMode;
            _copilotSettings = settings;
            try
            {
                new CopilotSettingsStore(_copilotConfigPath, _copilotProviderRegistry).Save(settings);
            }
            catch
            {
            }
        }

        _selectedCopilotMode = ToModeDisplayName(settings.Mode);
        OnPropertyChanged(nameof(SelectedCopilotMode));

        UpdateCopilotOnlineIndicator();
        UpdateProviderDiagnosticsSummary();

        try
        {
            var memStore = new KyraPersistentMemoryStore(_kyraMemoryPath);
            var memDoc = memStore.Load();
            if (memDoc.Enabled != settings.KyraPersistentMemoryEnabled)
            {
                memDoc.Enabled = settings.KyraPersistentMemoryEnabled;
                KyraPersistentMemoryStore.SanitizeInPlace(memDoc);
                memStore.Save(memDoc);
            }
        }
        catch
        {
        }

        OnPropertyChanged(nameof(KyraApiFirstRouting));
        OnPropertyChanged(nameof(KyraOfflineFallbackEnabled));
        OnPropertyChanged(nameof(KyraPersistentMemoryEnabled));
        OnPropertyChanged(nameof(KyraLiveToolsForBinding));
    }

    private void LoadBetaSettings()
    {
        var welcomeDismissed = false;
        var entitlement = false;
        var verboseLogs = false;

        try
        {
            if (File.Exists(_betaConfigPath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(_betaConfigPath));
                var root = document.RootElement;
                welcomeDismissed = GetJsonBool(root, "welcomeDismissed");
                entitlement = GetJsonBool(root, "betaTesterEntitlement");
                verboseLogs = GetJsonBool(root, "verboseLiveLogs");
            }
        }
        catch
        {
            welcomeDismissed = false;
            entitlement = false;
            verboseLogs = false;
        }

        BetaTesterEntitlement = entitlement;
        BetaWelcomeVisibility = welcomeDismissed ? Visibility.Collapsed : Visibility.Visible;
        _verboseLiveLogs = verboseLogs;
        OnPropertyChanged(nameof(VerboseLiveLogs));
    }

    public void DismissBetaWelcome()
    {
        BetaWelcomeVisibility = Visibility.Collapsed;
        SaveBetaSettings();
        AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] Beta welcome dismissed for this Windows user.", LogSeverity.Info));
    }

    private void SaveBetaSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_betaConfigPath)!);
            var welcomeDismissed = BetaWelcomeVisibility != Visibility.Visible;
            var payload = new
            {
                welcomeDismissed,
                betaTesterEntitlement = BetaTesterEntitlement,
                verboseLiveLogs = _verboseLiveLogs,
                // TODO: replace this placeholder with signed license verification before enforcing Pro access.
                licenseVerification = "placeholder"
            };
            File.WriteAllText(_betaConfigPath, JsonSerializer.Serialize(payload, IndentedJsonOptions));
        }
        catch (Exception exception)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Beta settings could not be saved: {exception.Message}", LogSeverity.Warning));
        }
    }

    private CopilotSettings BuildCopilotSettingsFromUi()
    {
        var settings = _copilotSettings ?? new CopilotSettings();
        settings.LiveTools ??= new KyraLiveToolsSettings();
        settings.Mode = ToCopilotMode(SelectedCopilotMode);
        settings.ProviderType = CopilotProviderType.LocalOffline;
        settings.TimeoutSeconds = settings.TimeoutSeconds <= 0 ? 12 : settings.TimeoutSeconds;
        settings.OfflineFallbackEnabled = _copilotSettings?.OfflineFallbackEnabled ?? settings.OfflineFallbackEnabled;
        settings.RedactContextEnabled = true;
        settings.MaxContextCharacters = settings.MaxContextCharacters <= 0 ? 6000 : settings.MaxContextCharacters;
        settings.UseLatestSystemScanContext = UseLatestSystemScanContext;
        settings.AllowOnlineSystemContextSharing = AllowOnlineSystemContextSharing;
        settings.EnableFreeProviderPool = EnableFreeProviderPool;
        settings.EnableByokProviders = EnableByokProviders;

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
            providerConfig.BaseUrl = string.IsNullOrWhiteSpace(view?.BaseUrl) ? provider.DefaultBaseUrl : view!.BaseUrl;
            providerConfig.ModelName = string.IsNullOrWhiteSpace(view?.ModelName) ? provider.DefaultModelName : view!.ModelName;
            providerConfig.ApiKeyEnvironmentVariable = string.IsNullOrWhiteSpace(view?.ApiKeyEnvironmentVariable)
                ? provider.DefaultApiKeyEnvironmentVariable
                : view!.ApiKeyEnvironmentVariable;
            providerConfig.TimeoutSeconds = providerConfig.TimeoutSeconds <= 0 ? settings.TimeoutSeconds : providerConfig.TimeoutSeconds;
            providerConfig.MaxRequestsPerMinute = providerConfig.MaxRequestsPerMinute <= 0 ? 12 : providerConfig.MaxRequestsPerMinute;
            providerConfig.MaxRetries = providerConfig.MaxRetries < 0 ? 0 : providerConfig.MaxRetries;
            providerConfig.DailyRequestCap = providerConfig.DailyRequestCap <= 0 ? (provider.IsOnlineProvider ? 60 : int.MaxValue) : providerConfig.DailyRequestCap;
            providerConfig.MaxInputCharacters = providerConfig.MaxInputCharacters <= 0 ? settings.MaxInputCharactersOnline : providerConfig.MaxInputCharacters;
            providerConfig.MaxOutputTokens = providerConfig.MaxOutputTokens <= 0 ? settings.MaxOutputTokensOnline : providerConfig.MaxOutputTokens;

            if (!string.IsNullOrWhiteSpace(view?.SessionApiKey))
            {
                KyraApiKeyStore.SetSessionKey(provider.Id, view.SessionApiKey);
                view.MaskedApiKey = KyraApiKeyStore.Mask(view.SessionApiKey);
                view.SessionApiKey = string.Empty;
            }

            if (view is not null)
            {
                view.IsConfigured = provider.IsConfigured(providerConfig);
                view.ProviderStatusLabel = CopilotProviderStatusFormatter.BuildStatusLabel(provider, providerConfig);
                view.CredentialSourceText = CopilotProviderStatusFormatter.BuildCredentialSourceLine(provider, providerConfig);
            }
        }

        _copilotSettings = settings;
        UpdateProviderDiagnosticsSummary();
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
        var localOllamaEnabled = CopilotProviderSettings.Any(item => item.IsEnabled && string.Equals(item.Id, "ollama-local", StringComparison.OrdinalIgnoreCase));
        var localLmStudioEnabled = CopilotProviderSettings.Any(item => item.IsEnabled && string.Equals(item.Id, "lm-studio-local", StringComparison.OrdinalIgnoreCase));
        var openAiConfigured = CopilotProviderSettings.Any(item => item.IsEnabled && item.IsConfigured && string.Equals(item.Id, "openai-compatible", StringComparison.OrdinalIgnoreCase));
        var anyOnlineConfigured = CopilotProviderSettings.Any(item => item.IsEnabled &&
                                                                      item.IsConfigured &&
                                                                      item.Id != "local-offline" &&
                                                                      item.Id != "ollama-local" &&
                                                                      item.Id != "lm-studio-local");
        var anyPricingConfigured = CopilotProviderSettings.Any(item => item.IsEnabled && item.IsConfigured && item.Category.Contains("Pricing", StringComparison.OrdinalIgnoreCase));
        CopilotProviderBadgeText = KyraProviderStatusPresenter.GetProviderBadge(mode, localOllamaEnabled, localLmStudioEnabled, openAiConfigured, anyOnlineConfigured);
        CopilotPrivacyBadgeText = KyraProviderStatusPresenter.GetPrivacyBadge(mode);
        CopilotProviderSummaryText = KyraProviderStatusPresenter.GetOnlineSummary(
            localOllamaEnabled,
            localLmStudioEnabled,
            openAiConfigured,
            anyPricingConfigured,
            anyOnlineConfigured);

        CopilotRoutingPolicyText = mode == CopilotMode.HybridAuto
            ? "Hybrid: local for device/USB/toolkit tasks; API for normal chat when providers are ready."
            : string.Empty;

        if (mode == CopilotMode.OfflineOnly)
        {
            CopilotOnlineStatusText = "Kyra Mode: Offline Local - using local Kyra rules and local system context.";
            CopilotOnlineStatusBackground = ReadyBackground;
            CopilotOnlineStatusBorderBrush = ReadyBorder;
            CopilotOnlineStatusForeground = ReadyForeground;
            return;
        }

        CopilotOnlineStatusText = mode switch
        {
            CopilotMode.FreeApiPool => anyOnlineConfigured
                ? "Kyra Mode: Free API Pool - using configured free-tier providers with local fallback."
                : "Online provider not configured. Local Kyra is active. (Free API Pool selected but no provider is configured yet.)",
            CopilotMode.BringYourOwnKey => anyOnlineConfigured
                ? "Kyra Mode: BYOK - only configured BYOK providers will be used; Local Kyra fallback stays enabled."
                : "Online provider not configured. Local Kyra is active. (BYOK selected but no paid provider is configured yet.)",
            CopilotMode.AskFirst => "Kyra Mode: Hybrid (Ask First) - Kyra stays local/offline unless you explicitly choose an online lookup.",
            CopilotMode.OnlineWhenAvailable => anyOnlineConfigured
                ? "Kyra Mode: Online/API - Kyra can use sanitized provider context when you enable providers."
                : "Online provider not configured. Local Kyra is active. (Online/API mode will use providers only after you configure one.)",
            CopilotMode.OnlineAssisted => anyOnlineConfigured
                ? "Kyra Mode: Online Assisted - providers may be used when configured."
                : "Online provider not configured. Local Kyra is active.",
            _ => anyOnlineConfigured || localOllamaEnabled || localLmStudioEnabled
                ? "Hybrid: local for device/USB/toolkit tasks; API for normal chat when providers are ready."
                : "Online provider not configured. Local Kyra is active."
        };
        var hasReachableProvider = anyOnlineConfigured || localOllamaEnabled || localLmStudioEnabled;
        CopilotOnlineStatusBackground = hasReachableProvider ? WarningBackground : ReadyBackground;
        CopilotOnlineStatusBorderBrush = hasReachableProvider ? WarningBorder : ReadyBorder;
        CopilotOnlineStatusForeground = hasReachableProvider ? WarningForeground : ReadyForeground;
        UpdateProviderDiagnosticsSummary();
    }

    private void ApplyCopilotOnlineIndicator(CopilotResponse response)
    {
        CopilotOnlineStatusText = response.OnlineStatus;
        CopilotActiveProviderText = $"Provider: {GetProviderDisplayName(response.ProviderType)}";
        var lastFailure = response.ProviderNotes.LastOrDefault(note => note.Contains("failed", StringComparison.OrdinalIgnoreCase) || note.Contains("timeout", StringComparison.OrdinalIgnoreCase) || note.Contains("rate limit", StringComparison.OrdinalIgnoreCase));
        CopilotLastProviderFailureText = string.IsNullOrWhiteSpace(lastFailure) ? "Last provider failure: none" : $"Last provider failure: {lastFailure}";
        UpdateProviderDiagnosticsSummary();
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

    private void ClearProviderSessionKeys()
    {
        foreach (var providerView in CopilotProviderSettings)
        {
            KyraApiKeyStore.ClearSessionKey(providerView.Id);
            providerView.SessionApiKey = string.Empty;
            providerView.MaskedApiKey = string.Empty;
        }

        SaveCopilotSettings();
    }

    private void UpdateProviderDiagnosticsSummary()
    {
        var enabledCount = CopilotProviderSettings.Count(item => item.IsEnabled);
        var configuredCount = CopilotProviderSettings.Count(item =>
            item.IsEnabled && item.IsConfigured && !item.IsPlaceholder);
        var coolingCount = CopilotProviderSettings.Count(item => item.ProviderStatusLabel.Contains("Rate limited", StringComparison.OrdinalIgnoreCase) || item.ProviderStatusLabel.Contains("Cooling", StringComparison.OrdinalIgnoreCase));
        var fallback = CopilotOnlineStatusText.Contains("Local", StringComparison.OrdinalIgnoreCase) || CopilotOnlineStatusText.Contains("offline", StringComparison.OrdinalIgnoreCase)
            ? "Fallback: Local Kyra active"
            : "Fallback: not active";
        CopilotDiagnosticsSummaryText = $"Enabled providers: {enabledCount} | Configured providers: {configuredCount} | Cooling down: {coolingCount} | {fallback}";
    }

    private string GetProviderDisplayName(CopilotProviderType providerType)
    {
        return _copilotProviderRegistry.FindByType(providerType)?.DisplayName ?? providerType.ToString();
    }

    private static CopilotMode ToCopilotMode(string mode)
    {
        return mode switch
        {
            "Offline Local" => CopilotMode.OfflineOnly,
            "Free API Pool" => CopilotMode.FreeApiPool,
            "Hybrid" => CopilotMode.HybridAuto,
            "Online/API" => CopilotMode.OnlineWhenAvailable,
            "BYOK" => CopilotMode.BringYourOwnKey,
            "Online Assisted" => CopilotMode.OnlineAssisted,
            "Online When Available" => CopilotMode.OnlineWhenAvailable,
            "Hybrid Auto" => CopilotMode.HybridAuto,
            "Ask First" => CopilotMode.AskFirst,
            _ => CopilotMode.OfflineOnly
        };
    }

    private static string ToModeDisplayName(CopilotMode mode)
    {
        return mode switch
        {
            CopilotMode.FreeApiPool => "Free API Pool",
            CopilotMode.BringYourOwnKey => "BYOK",
            CopilotMode.ForgerEmsCloudFuture => "Online/API",
            CopilotMode.OnlineAssisted => "Online Assisted",
            CopilotMode.OnlineWhenAvailable => "Online/API",
            CopilotMode.HybridAuto => "Hybrid",
            CopilotMode.AskFirst => "Ask First",
            _ => "Offline Local"
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
                JsonSerializer.Serialize(stableResults, IndentedJsonOptions));
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
        if (trimmed.StartsWith('['))
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

    private static void RunOnUi(Action action)
    {
        void SafeAction()
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                StartupDiagnosticLog.AppendException("RunOnUi.Action", exception);
            }
        }

        try
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.HasShutdownStarted)
            {
                return;
            }

            if (dispatcher.CheckAccess())
            {
                SafeAction();
                return;
            }

            _ = dispatcher.BeginInvoke(DispatcherPriority.Normal, SafeAction);
        }
        catch (Exception exception)
        {
            StartupDiagnosticLog.AppendException("RunOnUi.Dispatch", exception);
        }
    }

    private void EnsureWslOutputFlushTimer()
    {
        if (_wslOutputFlushTimer is not null)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        _wslOutputFlushTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(175)
        };
        _wslOutputFlushTimer.Tick += OnWslOutputFlushTick;
    }

    private void OnWslOutputFlushTick(object? sender, EventArgs e)
    {
        try
        {
            var processed = 0;
            while (processed++ < 128 && _wslPendingOutputLines.TryDequeue(out var line))
            {
                try
                {
                    AppendWslRunnerLine(line);
                }
                catch (Exception exception)
                {
                    StartupDiagnosticLog.AppendException("WslFlush.AppendWslRunnerLine", exception);
                }
            }

            if (_wslPendingOutputLines.IsEmpty)
            {
                _wslOutputFlushTimer?.Stop();
            }
        }
        catch (Exception exception)
        {
            StartupDiagnosticLog.AppendException("WslFlushTick", exception);
        }
    }

    private void ScheduleWslOutputFlush()
    {
        RunOnUi(() =>
        {
            try
            {
                EnsureWslOutputFlushTimer();
                if (_wslOutputFlushTimer is not null && !_wslOutputFlushTimer.IsEnabled)
                {
                    _wslOutputFlushTimer.Start();
                }
            }
            catch (Exception exception)
            {
                StartupDiagnosticLog.AppendException("ScheduleWslOutputFlush", exception);
            }
        });
    }

    private void SafeAppendWslLine(string line)
    {
        try
        {
            var safe = CopilotRedactor.Redact(line ?? string.Empty, enabled: true);
            _wslPendingOutputLines.Enqueue(safe);
            ScheduleWslOutputFlush();
        }
        catch (Exception exception)
        {
            StartupDiagnosticLog.AppendException("SafeAppendWslLine", exception);
        }
    }

    private void RefreshDiagnosticsAuxiliaryText()
    {
        var sandboxPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsSandbox.exe");
        WindowsSandboxStatusText = File.Exists(sandboxPath)
            ? "Windows Sandbox appears present (System32\\WindowsSandbox.exe). ForgerEMS does not launch it or run unknown files automatically."
            : "Windows Sandbox was not detected as installed. You can still use Hyper-V, VMware, VirtualBox, or another VM manually. ForgerEMS will not auto-run downloads.";
    }

    private void RefreshWslRunnerSummary()
    {
        WslRunnerSummaryText = _wslExecutor.IsWslInstalled()
            ? "WSL is available (wsl.exe found). Commands in the box below run in your default distro via sh -lc unless you use the host quick actions. Nothing here runs elevated."
            : "WSL was not detected. Install Ubuntu/WSL from the Microsoft Store or run wsl --install once from an Administrator PowerShell window, then restart the PC if prompted.";
    }

    private void SetWslRunnerBusy(bool value)
    {
        if (_isWslRunnerBusy == value)
        {
            return;
        }

        _isWslRunnerBusy = value;
        OnPropertyChanged(nameof(IsWslRunnerRunning));
        RunWslRunnerCommand.RaiseCanExecuteChanged();
        StopWslRunnerCommand.RaiseCanExecuteChanged();
        RunWslHostListVerboseRunnerCommand.RaiseCanExecuteChanged();
        RunWslHostStatusRunnerCommand.RaiseCanExecuteChanged();
    }

    private void AppendWslRunnerLine(string line)
    {
        try
        {
            var safe = CopilotRedactor.Redact(line, enabled: true);
            var next = string.IsNullOrEmpty(WslRunnerOutputText)
                ? safe
                : WslRunnerOutputText + Environment.NewLine + safe;
            if (next.Length > 250_000)
            {
                next = "...[truncated]" + Environment.NewLine + next[^200_000..];
            }

            WslRunnerOutputText = next;
        }
        catch (Exception exception)
        {
            StartupDiagnosticLog.AppendException("AppendWslRunnerLine", exception);
        }
    }

    private async Task RunWslHostArgumentsUiAsync(string[] arguments, string displayLine)
    {
        if (!_wslExecutor.IsWslInstalled())
        {
            SafeAppendWslLine("WSL was not detected. Install WSL/Ubuntu from Microsoft Store or run wsl --install.");
            return;
        }

        _wslRunnerCancellation = new CancellationTokenSource();
        SetWslRunnerBusy(true);
        try
        {
            SafeAppendWslLine("$ " + displayLine);

            var linesReceived = 0;
            var progress = new Progress<string>(line =>
            {
                try
                {
                    Interlocked.Increment(ref linesReceived);
                    SafeAppendWslLine(line);
                }
                catch (Exception exception)
                {
                    StartupDiagnosticLog.AppendException("RunWslHostArgumentsUiAsync.Progress", exception);
                }
            });

            var (code, combined) = await _wslExecutor.RunHostWslArgumentsAsync(
                arguments,
                TimeSpan.FromSeconds(60),
                _wslRunnerCancellation.Token,
                progress).ConfigureAwait(false);

            RunOnUi(() =>
            {
                try
                {
                    if (linesReceived == 0 && !string.IsNullOrWhiteSpace(combined))
                    {
                        AppendWslRunnerLine(combined);
                    }

                    AppendWslRunnerLine(FormattableString.Invariant($"[exit {code}]"));
                }
                catch (Exception exception)
                {
                    StartupDiagnosticLog.AppendException("RunWslHostArgumentsUiAsync.ExitUi", exception);
                }
            });
        }
        catch (OperationCanceledException)
        {
            SafeAppendWslLine("[INFO] WSL command was cancelled, stopped, or timed out.");
        }
        catch (Exception ex)
        {
            SafeAppendWslLine("WSL panel error: " + ex.Message);
            StartupDiagnosticLog.AppendException(
                "RunWslHostArgumentsUiAsync",
                ex,
                new Dictionary<string, string>
                {
                    ["source"] = "wsl_host_args",
                    ["display"] = CopilotRedactor.Redact(displayLine, enabled: true)
                });
        }
        finally
        {
            SetWslRunnerBusy(false);
            try
            {
                _wslRunnerCancellation?.Dispose();
            }
            catch
            {
            }

            _wslRunnerCancellation = null;
        }
    }

    private async Task RunWslRunnerAsync()
    {
        if (!_wslExecutor.IsWslInstalled())
        {
            SafeAppendWslLine("WSL was not detected. Install WSL/Ubuntu from Microsoft Store or run wsl --install.");
            return;
        }

        var cmd = WslRunnerCommandInput.Trim();
        if (string.IsNullOrEmpty(cmd))
        {
            SafeAppendWslLine("Enter a command before Run.");
            return;
        }

        _wslRunnerCancellation = new CancellationTokenSource();
        SetWslRunnerBusy(true);
        try
        {
            SafeAppendWslLine("$ " + cmd);

            var linesReceived = 0;
            var progress = new Progress<string>(line =>
            {
                try
                {
                    Interlocked.Increment(ref linesReceived);
                    SafeAppendWslLine(line);
                }
                catch (Exception exception)
                {
                    StartupDiagnosticLog.AppendException("RunWslRunnerAsync.Progress", exception);
                }
            });

            var (code, combined) = await _wslExecutor.RunShellCommandAsync(
                cmd,
                TimeSpan.FromSeconds(90),
                _wslRunnerCancellation.Token,
                progress).ConfigureAwait(false);

            RunOnUi(() =>
            {
                try
                {
                    if (linesReceived == 0 && !string.IsNullOrWhiteSpace(combined))
                    {
                        AppendWslRunnerLine(combined);
                    }

                    AppendWslRunnerLine(FormattableString.Invariant($"[exit {code}]"));
                }
                catch (Exception exception)
                {
                    StartupDiagnosticLog.AppendException("RunWslRunnerAsync.ExitUi", exception);
                }
            });
        }
        catch (OperationCanceledException)
        {
            SafeAppendWslLine("[INFO] WSL command was cancelled, stopped, or timed out.");
        }
        catch (Exception ex)
        {
            SafeAppendWslLine("WSL panel error: " + ex.Message);
            StartupDiagnosticLog.AppendException(
                "RunWslRunnerAsync",
                ex,
                new Dictionary<string, string>
                {
                    ["source"] = "wsl_shell_run",
                    ["command"] = CopilotRedactor.Redact(cmd, enabled: true)
                });
        }
        finally
        {
            SetWslRunnerBusy(false);
            try
            {
                _wslRunnerCancellation?.Dispose();
            }
            catch
            {
            }

            _wslRunnerCancellation = null;
        }
    }

    private void StopWslRunner()
    {
        try
        {
            if (_wslRunnerCancellation is null)
            {
                SafeAppendWslLine("[INFO] WSL Stop: nothing is running.");
                return;
            }

            if (_wslRunnerCancellation.IsCancellationRequested)
            {
                SafeAppendWslLine("[INFO] WSL Stop: cancellation was already requested.");
                return;
            }

            _wslRunnerCancellation.Cancel();
            SafeAppendWslLine("[INFO] WSL Stop: cancellation requested.");
        }
        catch (Exception exception)
        {
            StartupDiagnosticLog.AppendException("StopWslRunner", exception);
            SafeAppendWslLine("[INFO] WSL Stop: " + exception.Message);
        }
    }

    private void CopyWslRunnerOutput()
    {
        try
        {
            Clipboard.SetDataObject(WslRunnerOutputText ?? string.Empty, copy: true);
        }
        catch
        {
        }
    }

    private void ClearWslRunnerOutputPane()
    {
        WslRunnerOutputText = string.Empty;
    }

    private void RunLinkSafetyAnalyze()
    {
        var report = LinkSafetyAnalyzer.Analyze(LinkSafetyUrlInput);
        LinkSafetyResultText = LinkSafetyAnalyzer.FormatReport(report);
    }

    private void BrowseLocalFileSafety()
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select a file to inspect (read-only; never executed)",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            LocalFileSafetyPath = dialog.FileName;
        }
        catch (Exception exception)
        {
            StartupDiagnosticLog.AppendException("BrowseLocalFileSafety", exception);
            LocalFileSafetyResultText = "Could not open file picker: " + exception.Message;
        }
    }

    private void RunLocalFileSafetyAnalyze()
    {
        try
        {
            var report = DownloadedFileSafetyAnalyzer.Analyze(LocalFileSafetyPath.Trim(), out var error);
            if (report is null)
            {
                _lastLocalSafetySha256 = string.Empty;
                CopyLocalFileSafetyShaCommand.RaiseCanExecuteChanged();
                LocalFileSafetyResultText = error ?? "Analysis failed.";
                return;
            }

            _lastLocalSafetySha256 = report.Sha256Hex;
            CopyLocalFileSafetyShaCommand.RaiseCanExecuteChanged();
            CopyLocalFileSafetyReportCommand.RaiseCanExecuteChanged();
            LocalFileSafetyResultText = DownloadedFileSafetyAnalyzer.FormatReport(report);
        }
        catch (Exception exception)
        {
            StartupDiagnosticLog.AppendException("RunLocalFileSafetyAnalyze", exception);
            LocalFileSafetyResultText = "Analysis failed: " + exception.Message;
        }
    }

    private void CopyLocalFileSafetySha()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_lastLocalSafetySha256))
            {
                return;
            }

            Clipboard.SetDataObject(_lastLocalSafetySha256, copy: true);
            AppendLog(new LogLine(DateTimeOffset.Now, "[OK] Copied file SHA256 to clipboard.", LogSeverity.Success));
        }
        catch
        {
        }
    }

    private void CopyLocalFileSafetyReport()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(LocalFileSafetyResultText))
            {
                return;
            }

            Clipboard.SetDataObject(LocalFileSafetyResultText, copy: true);
            AppendLog(new LogLine(DateTimeOffset.Now, "[OK] Copied file safety report to clipboard.", LogSeverity.Success));
        }
        catch
        {
        }
    }

    private void OpenLocalSafetyQuarantineFolder()
    {
        OpenFolder(DownloadedFileSafetyAnalyzer.GetQuarantineRoot(), "quarantine folder", createIfMissing: true);
    }

    private void CopyLocalFileToQuarantine()
    {
        try
        {
            var path = LocalFileSafetyPath.Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                LocalFileSafetyResultText = "Pick an existing file before copying to quarantine.";
                return;
            }

            DownloadedFileSafetyAnalyzer.CopyToQuarantine(path, DownloadedFileSafetyAnalyzer.GetQuarantineRoot(), out var dest, out var error);
            if (!string.IsNullOrWhiteSpace(error))
            {
                LocalFileSafetyResultText = "Copy to quarantine failed: " + error;
                return;
            }

            AppendLog(new LogLine(DateTimeOffset.Now, "[OK] Copied file to quarantine (not executed): " + CopilotRedactor.Redact(dest, enabled: true), LogSeverity.Success));
            LocalFileSafetyResultText = (LocalFileSafetyResultText ?? string.Empty) + Environment.NewLine + Environment.NewLine +
                                        "Copied to quarantine (read-only copy; original untouched):" + Environment.NewLine + CopilotRedactor.Redact(dest, enabled: true);
        }
        catch (Exception exception)
        {
            StartupDiagnosticLog.AppendException("CopyLocalFileToQuarantine", exception);
            LocalFileSafetyResultText = "Copy to quarantine failed: " + exception.Message;
        }
    }

    private async Task RunLinkSafetyHeadAsync()
    {
        var report = LinkSafetyAnalyzer.Analyze(LinkSafetyUrlInput);
        var baseText = LinkSafetyAnalyzer.FormatReport(report);
        if (!Uri.TryCreate(LinkSafetyUrlInput.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            RunOnUi(() => LinkSafetyResultText = baseText + Environment.NewLine + Environment.NewLine +
                                                "HTTPS HEAD was skipped (needs a valid https:// URL).");
            return;
        }

        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "ForgerEMS/1.1.4 (beta link checker; no execute)");
            using var request = new HttpRequestMessage(HttpMethod.Head, uri);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            var sb = new StringBuilder(baseText);
            sb.AppendLine().AppendLine("--- HTTPS HEAD (informational only; servers may omit headers) ---");
            sb.AppendLine(FormattableString.Invariant($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}"));
            if (response.Headers.Location is not null)
            {
                sb.AppendLine("Location: " + response.Headers.Location);
            }

            foreach (var key in new[] { "Content-Type", "Content-Length", "Content-Disposition", "Last-Modified" })
            {
                if (response.Content.Headers.TryGetValues(key, out var values))
                {
                    sb.AppendLine(key + ": " + string.Join(", ", values));
                }
            }

            RunOnUi(() => LinkSafetyResultText = sb.ToString());
        }
        catch (Exception ex)
        {
            RunOnUi(() => LinkSafetyResultText = baseText + Environment.NewLine + Environment.NewLine +
                                                "HEAD request failed (network, TLS, or server policy): " + ex.Message);
        }
    }

    private async Task DownloadLinkToQuarantineAsync()
    {
        if (!Uri.TryCreate(LinkSafetyUrlInput.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            RunOnUi(() => LinkSafetyResultText = "Quarantine download requires a valid https:// URL.");
            return;
        }

        var quarantineRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForgerEMS", "Quarantine");
        Directory.CreateDirectory(quarantineRoot);
        var name = Path.GetFileName(uri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "download.bin";
        }

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        if (name.Length > 120)
        {
            name = name[..120];
        }

        var targetPath = Path.Combine(quarantineRoot, $"{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{name}");
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(3);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "ForgerEMS/1.1.4 (beta quarantine download; no execute)");
            await using var network = await client.GetStreamAsync(uri).ConfigureAwait(false);
            await using var file = File.Create(targetPath);
            using var incremental = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long totalBytes = 0;
            int read;
            while ((read = await network.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false)) > 0)
            {
                totalBytes += read;
                if (totalBytes > 200L * 1024 * 1024)
                {
                    throw new IOException("Download exceeds 200 MB beta quarantine limit.");
                }

                incremental.AppendData(new ReadOnlySpan<byte>(buffer, 0, read));
                await file.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
            }

            var hash = Convert.ToHexString(incremental.GetHashAndReset());
            RunOnUi(() => LinkSafetyResultText =
                "Saved bytes only (not executed) to:\n" + targetPath +
                "\n\nSHA256: " + hash +
                "\n\nScan with Windows Defender or upload the hash to VirusTotal manually if you choose. Delete the file when done.");
        }
        catch (Exception ex)
        {
            RunOnUi(() => LinkSafetyResultText = "Quarantine download failed: " + ex.Message);
        }
    }

    private void LoadUpdateSettings()
    {
        _appUpdateSettings = _updateSettingsStore.Load();
        OnPropertyChanged(nameof(CheckForUpdatesAutomatically));
        OnPropertyChanged(nameof(LastUpdateCheckDisplayText));
        OnPropertyChanged(nameof(AppUpdateSettingsIgnoredSummary));
        OnPropertyChanged(nameof(AppUpdateSettingsIgnoredVisibility));
    }

    private void SaveUpdateSettings()
    {
        try
        {
            _updateSettingsStore.Save(_appUpdateSettings);
        }
        catch
        {
            // best effort
        }

        OnPropertyChanged(nameof(LastUpdateCheckDisplayText));
        OnPropertyChanged(nameof(AppUpdateSettingsIgnoredSummary));
        OnPropertyChanged(nameof(AppUpdateSettingsIgnoredVisibility));
        ClearIgnoredAppUpdateVersionCommand.RaiseCanExecuteChanged();
    }

    private void ScheduleBackgroundUpdateCheck()
    {
        if (!_appUpdateSettings.CheckAutomatically)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1500).ConfigureAwait(false);
                await RequestUpdateCheckAsync(manual: false).ConfigureAwait(false);
            }
            catch
            {
                // never crash startup
            }
        });
    }

    private async Task RequestUpdateCheckAsync(bool manual)
    {
        if (_updateCheckInProgress)
        {
            return;
        }

        _updateCheckInProgress = true;
        RunOnUi(() =>
        {
            CheckForUpdatesNowCommand.RaiseCanExecuteChanged();
            AppUpdateStateDisplay = manual ? "Checking for updates…" : "Checking for updates in background…";
            if (manual)
            {
                AppUpdateBannerVisibility = Visibility.Visible;
                AppUpdateBannerTitle = "Checking for updates…";
                AppUpdateBannerDetail = string.Empty;
                AppUpdateDiagnosticsHintVisibility = Visibility.Collapsed;
            }
        });

        var result = new UpdateCheckResult
        {
            Succeeded = false,
            Outcome = UpdateCheckOutcome.Failed,
            FailureKind = UpdateCheckFailureKind.Unknown,
            ErrorMessage = "Update check did not complete."
        };

        try
        {
            var ignored = string.IsNullOrWhiteSpace(_appUpdateSettings.IgnoredVersion)
                ? null
                : _appUpdateSettings.IgnoredVersion;
            var installedLabel = AppReleaseInfo.Version;

            AppendLog(new LogLine(
                DateTimeOffset.Now,
                $"[INFO] Update check started. Installed={installedLabel} Source=GitHub Releases Manual={manual}",
                LogSeverity.Info,
                channel: LiveLogChannel.Update));

            try
            {
                result = await _updateCheckService
                    .CheckForNewerReleaseAsync(installedLabel, ignored, CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(45), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                result = new UpdateCheckResult
                {
                    Succeeded = false,
                    Outcome = UpdateCheckOutcome.Failed,
                    FailureKind = UpdateCheckFailureKind.Timeout,
                    ErrorMessage = "Update check timed out. Try again later.",
                    DiagnosticDetail = "Overall update-check deadline exceeded."
                };
                AppendLog(new LogLine(
                    DateTimeOffset.Now,
                    "[WARN] Update check timed out.",
                    LogSeverity.Warning,
                    channel: LiveLogChannel.Update));
            }

            result = ReconcileIgnoredInFlightUpdatePrompt(result, _appUpdateSettings.IgnoredVersion);

            if (result.Succeeded)
            {
                if (!string.IsNullOrWhiteSpace(result.LatestVersionLabel) &&
                    result.Outcome != UpdateCheckOutcome.NoPublishedRelease)
                {
                    AppendLog(new LogLine(
                        DateTimeOffset.Now,
                        $"[INFO] Latest release found. Latest={ReleaseVersionParser.NormalizeLabel(result.LatestVersionLabel)} Tag={result.LatestVersionLabel}",
                        LogSeverity.Info,
                        channel: LiveLogChannel.Update));
                }

                if (result.Outcome == UpdateCheckOutcome.UpdateAvailable)
                {
                    AppendLog(new LogLine(
                        DateTimeOffset.Now,
                        $"[OK] Update available. Installed={ReleaseVersionParser.NormalizeLabel(installedLabel)} Latest={ReleaseVersionParser.NormalizeLabel(result.LatestVersionLabel)}",
                        LogSeverity.Info,
                        channel: LiveLogChannel.Update));
                }
                else if (result.Outcome == UpdateCheckOutcome.AlreadyLatest)
                {
                    AppendLog(new LogLine(
                        DateTimeOffset.Now,
                        "[OK] App is already up to date.",
                        LogSeverity.Info,
                        channel: LiveLogChannel.Update));
                }
                else if (result.Outcome == UpdateCheckOutcome.InstalledNewerThanLatestPublic)
                {
                    AppendLog(new LogLine(
                        DateTimeOffset.Now,
                        "[OK] Installed build is newer than latest public release.",
                        LogSeverity.Info,
                        channel: LiveLogChannel.Update));
                }
                else if (result.Outcome == UpdateCheckOutcome.NoPublishedRelease)
                {
                    AppendLog(new LogLine(
                        DateTimeOffset.Now,
                        "[INFO] No published GitHub release found for this repo.",
                        LogSeverity.Info,
                        channel: LiveLogChannel.Update));
                }
                else if (result.Outcome == UpdateCheckOutcome.IgnoredVersion)
                {
                    AppendLog(new LogLine(
                        DateTimeOffset.Now,
                        "[INFO] Update check complete; latest version matches ignored setting.",
                        LogSeverity.Info,
                        channel: LiveLogChannel.Update));
                }
            }
            else
            {
                AppendLog(new LogLine(
                    DateTimeOffset.Now,
                    $"[WARN] Update check failed: {result.FailureKind}.",
                    LogSeverity.Warning,
                    channel: LiveLogChannel.Update));
            }
        }
        catch (Exception exception)
        {
            result = new UpdateCheckResult
            {
                Succeeded = false,
                Outcome = UpdateCheckOutcome.Failed,
                FailureKind = UpdateCheckFailureKind.Unknown,
                ErrorMessage = "Update check failed unexpectedly.",
                DiagnosticDetail = exception.Message
            };
            AppendLog(new LogLine(
                DateTimeOffset.Now,
                $"[WARN] Update check failed: {exception.Message}",
                LogSeverity.Warning,
                channel: LiveLogChannel.Update));
        }
        finally
        {
            _appUpdateSettings.LastCheckedUtc = DateTimeOffset.UtcNow;
            SaveUpdateSettings();

            var applyResult = result;
            RunOnUi(() => ApplyUpdateCheckResultToUi(applyResult, manual));

            _updateCheckInProgress = false;
            RunOnUi(() =>
            {
                CheckForUpdatesNowCommand.RaiseCanExecuteChanged();
                AppUpdateDownloadInstallerCommand.RaiseCanExecuteChanged();
            });
        }
    }

    private static UpdateCheckResult ReconcileIgnoredInFlightUpdatePrompt(UpdateCheckResult result, string? ignoredVersionFromSettings)
    {
        if (!result.Succeeded || !result.UpdateAvailable)
        {
            return result;
        }

        var ign = ReleaseVersionParser.NormalizeIgnored(ignoredVersionFromSettings);
        if (string.IsNullOrEmpty(ign))
        {
            return result;
        }

        if (!string.Equals(ReleaseVersionParser.NormalizeLabel(result.LatestVersionLabel), ign, StringComparison.OrdinalIgnoreCase))
        {
            return result;
        }

        var norm = ReleaseVersionParser.NormalizeLabel(result.LatestVersionLabel);
        return new UpdateCheckResult
        {
            Succeeded = true,
            Outcome = UpdateCheckOutcome.IgnoredVersion,
            UpdateAvailable = false,
            LatestVersion = result.LatestVersion,
            LatestVersionLabel = result.LatestVersionLabel,
            ReleaseNotesUrl = result.ReleaseNotesUrl,
            InstallerAssetName = result.InstallerAssetName,
            InstallerDownloadUrl = result.InstallerDownloadUrl,
            ErrorMessage = UpdateCheckDisplay.FormatIgnoredVersion(norm)
        };
    }

    private void ApplyUpdateCheckResultToUi(UpdateCheckResult result, bool manual)
    {
        OnPropertyChanged(nameof(LastUpdateCheckDisplayText));

        var installedNorm = ReleaseVersionParser.NormalizeLabel(AppReleaseInfo.Version);

        if (result.Succeeded && !string.IsNullOrWhiteSpace(result.LatestVersionLabel))
        {
            _appUpdateLatestChannelText = $"Latest release: v{ReleaseVersionParser.NormalizeLabel(result.LatestVersionLabel)}";
        }
        else if (result.Succeeded)
        {
            _appUpdateLatestChannelText = "Latest release: —";
        }

        OnPropertyChanged(nameof(AppUpdateSettingsLatestSummary));

        if (!result.Succeeded)
        {
            _pendingInstallerUrl = string.Empty;
            _pendingReleaseNotesUrl = string.Empty;
            _pendingVersionLabel = string.Empty;

            AppUpdateStateDisplay = result.FailureKind switch
            {
                UpdateCheckFailureKind.Network => "Update check: offline or network issue.",
                UpdateCheckFailureKind.Timeout => "Update check timed out. Try again later.",
                UpdateCheckFailureKind.ReleaseEndpointNotFound => "Update check: GitHub release endpoint not found.",
                UpdateCheckFailureKind.AccessDeniedOrRateLimited => "Update check: access denied or rate limited.",
                UpdateCheckFailureKind.ReleaseMetadataInvalid => "Update check: invalid release metadata.",
                UpdateCheckFailureKind.Cancelled => "Update check was cancelled.",
                UpdateCheckFailureKind.HttpError => "Update check: GitHub returned an error.",
                _ => string.IsNullOrWhiteSpace(result.ErrorMessage) ? "Update check failed." : result.ErrorMessage!
            };

            if (manual)
            {
                AppUpdateBannerVisibility = Visibility.Visible;
                AppUpdateBannerTitle = "Update check failed";
                AppUpdateBannerDetail = result.ErrorMessage ?? result.DiagnosticDetail ?? "Unknown error.";
                AppUpdateDiagnosticsHintVisibility = Visibility.Visible;
            }
            else
            {
                AppUpdateBannerVisibility = Visibility.Collapsed;
                AppUpdateDiagnosticsHintVisibility = Visibility.Collapsed;
            }

            if (!string.IsNullOrWhiteSpace(result.DiagnosticDetail))
            {
                AppendLog(new LogLine(
                    DateTimeOffset.Now,
                    $"[Update] {result.FailureKind}: {result.DiagnosticDetail}",
                    LogSeverity.Warning,
                    channel: LiveLogChannel.Diagnostics));
            }

            AppUpdateDownloadButtonVisibility = Visibility.Collapsed;
            AppUpdateIgnoreButtonVisibility = Visibility.Collapsed;
            AppUpdateViewReleaseNotesVisibility = Visibility.Collapsed;
            AppUpdateDownloadInstallerCommand.RaiseCanExecuteChanged();
            return;
        }

        AppUpdateDiagnosticsHintVisibility = Visibility.Collapsed;

        if (result.Outcome == UpdateCheckOutcome.UpdateAvailable && result.UpdateAvailable)
        {
            AppUpdateStateDisplay = $"Update available: v{ReleaseVersionParser.NormalizeLabel(result.LatestVersionLabel)}";
            _pendingReleaseNotesUrl = result.ReleaseNotesUrl;
            _pendingInstallerUrl = result.InstallerDownloadUrl ?? string.Empty;
            _pendingVersionLabel = result.LatestVersionLabel;

            var safeReleasePage = Uri.TryCreate(result.ReleaseNotesUrl, UriKind.Absolute, out var notesUri) &&
                                  string.Equals(notesUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(notesUri.Host, "github.com", StringComparison.OrdinalIgnoreCase);

            AppUpdateBannerVisibility = Visibility.Visible;
            AppUpdateBannerTitle = UpdateNotificationTextBuilder.BuildHeadline(result.LatestVersionLabel);
            var extras = new List<string>();
            if (!string.IsNullOrWhiteSpace(result.InstallerAssetName))
            {
                extras.Add($"Installer asset: {result.InstallerAssetName}");
            }

            if (!result.HasActionableInstaller)
            {
                extras.Add("No verified .exe installer URL on this release — open release notes or retry after publishing.");
            }

            AppUpdateBannerDetail = extras.Count == 0 ? string.Empty : string.Join(Environment.NewLine, extras);
            AppUpdateDownloadButtonVisibility = result.HasActionableInstaller ? Visibility.Visible : Visibility.Collapsed;
            AppUpdateIgnoreButtonVisibility = Visibility.Visible;
            AppUpdateViewReleaseNotesVisibility = safeReleasePage ? Visibility.Visible : Visibility.Collapsed;
            AppUpdateDownloadInstallerCommand.RaiseCanExecuteChanged();
            return;
        }

        _pendingInstallerUrl = string.Empty;
        _pendingReleaseNotesUrl = string.Empty;
        _pendingVersionLabel = string.Empty;

        AppUpdateDownloadButtonVisibility = Visibility.Collapsed;
        AppUpdateIgnoreButtonVisibility = Visibility.Collapsed;
        AppUpdateViewReleaseNotesVisibility = Visibility.Collapsed;

        AppUpdateBannerVisibility = Visibility.Collapsed;
        AppUpdateDiagnosticsHintVisibility = Visibility.Collapsed;

        switch (result.Outcome)
        {
            case UpdateCheckOutcome.NoPublishedRelease:
                AppUpdateStateDisplay = result.ErrorMessage ?? "No public release found.";
                _appUpdateLatestChannelText = "Latest release: —";
                OnPropertyChanged(nameof(AppUpdateSettingsLatestSummary));
                break;
            case UpdateCheckOutcome.IgnoredVersion:
                AppUpdateStateDisplay = result.ErrorMessage ?? UpdateCheckDisplay.FormatIgnoredVersion(ReleaseVersionParser.NormalizeLabel(result.LatestVersionLabel));
                break;
            case UpdateCheckOutcome.AlreadyLatest:
                AppUpdateStateDisplay = UpdateCheckDisplay.FormatInstalledAlreadyLatest(installedNorm);
                break;
            case UpdateCheckOutcome.InstalledNewerThanLatestPublic:
                AppUpdateStateDisplay = UpdateCheckDisplay.FormatInstalledNewerThanPublic(
                    installedNorm,
                    ReleaseVersionParser.NormalizeLabel(result.LatestVersionLabel));
                break;
            case UpdateCheckOutcome.None:
                AppUpdateStateDisplay = !string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? result.ErrorMessage!
                    : "Update check completed with no comparable version.";
                if (string.IsNullOrWhiteSpace(result.LatestVersionLabel))
                {
                    _appUpdateLatestChannelText = "Latest release: —";
                    OnPropertyChanged(nameof(AppUpdateSettingsLatestSummary));
                }

                break;
            default:
                AppUpdateStateDisplay = !string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? result.ErrorMessage!
                    : UpdateCheckDisplay.FormatInstalledAlreadyLatest(installedNorm);
                break;
        }

        AppUpdateDownloadInstallerCommand.RaiseCanExecuteChanged();
    }

    private void HideAppUpdateBanner()
    {
        AppUpdateBannerVisibility = Visibility.Collapsed;
        AppUpdateDiagnosticsHintVisibility = Visibility.Collapsed;
    }

    private bool CanClearIgnoredAppUpdateVersion()
        => !string.IsNullOrWhiteSpace(_appUpdateSettings.IgnoredVersion);

    private void ClearIgnoredAppUpdateVersion()
    {
        _appUpdateSettings.IgnoredVersion = string.Empty;
        SaveUpdateSettings();
        AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] Cleared ignored app update version in Settings.", LogSeverity.Info, channel: LiveLogChannel.Update));
    }

    private void IgnorePendingAppUpdateVersion()
    {
        if (!string.IsNullOrWhiteSpace(_pendingVersionLabel))
        {
            _appUpdateSettings.IgnoredVersion = ReleaseVersionParser.NormalizeLabel(_pendingVersionLabel);
            SaveUpdateSettings();
        }

        _pendingInstallerUrl = string.Empty;
        _pendingReleaseNotesUrl = string.Empty;
        _pendingVersionLabel = string.Empty;

        HideAppUpdateBanner();
        AppUpdateDownloadButtonVisibility = Visibility.Collapsed;
        AppUpdateIgnoreButtonVisibility = Visibility.Collapsed;
        AppUpdateViewReleaseNotesVisibility = Visibility.Collapsed;
        AppUpdateDownloadInstallerCommand.RaiseCanExecuteChanged();

        AppUpdateStateDisplay = "Latest update prompt ignored. You can reset this under Settings → App updates.";
        AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] Update prompt hidden for this version (change under Settings → App updates).", LogSeverity.Info, channel: LiveLogChannel.Update));
    }

    private void OpenPendingReleaseNotes()
    {
        if (string.IsNullOrWhiteSpace(_pendingReleaseNotesUrl))
        {
            _userPromptService.ShowMessage("Release notes", "No release notes link is available yet.", MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_pendingReleaseNotesUrl) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Could not open release notes: {exception.Message}", LogSeverity.Warning));
        }
    }

    private bool CanDownloadPendingInstaller()
    {
        if (_updateDownloadInProgress || string.IsNullOrWhiteSpace(_pendingInstallerUrl))
        {
            return false;
        }

        return Uri.TryCreate(_pendingInstallerUrl, UriKind.Absolute, out var uri) &&
               string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               uri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    }

    private async Task DownloadPendingInstallerAsync()
    {
        if (string.IsNullOrWhiteSpace(_pendingInstallerUrl))
        {
            return;
        }

        _updateDownloadInProgress = true;
        RunOnUi(() =>
        {
            AppUpdateDownloadInstallerCommand.RaiseCanExecuteChanged();
            AppUpdateBannerDetail = "Downloading installer (not running it)…";
            AppUpdateStateDisplay = "Downloading…";
        });

        try
        {
            var updatesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForgerEMS", "Updates");
            Directory.CreateDirectory(updatesDir);
            var uri = new Uri(_pendingInstallerUrl);
            var fileName = Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                fileName = "ForgerEMS-Update.exe";
            }

            foreach (var c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c, '_');
            }

            var targetPath = Path.Combine(updatesDir, $"{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{fileName}");

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(12);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "ForgerEMS-UpdateDownload/1.1.4");
            await using var stream = await client.GetStreamAsync(_pendingInstallerUrl).ConfigureAwait(false);
            await using var file = File.Create(targetPath);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > 280L * 1024 * 1024)
                {
                    throw new IOException("Installer download exceeds beta size limit.");
                }

                hash.AppendData(new ReadOnlySpan<byte>(buffer, 0, read));
                await file.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
            }

            var sha = Convert.ToHexString(hash.GetHashAndReset());
            _appUpdateSettings.LastDownloadPath = targetPath;
            _appUpdateSettings.LastDownloadSha256 = sha;
            SaveUpdateSettings();

            RunOnUi(() =>
            {
                AppUpdateBannerDetail =
                    "Download complete (installer was not run).\n" + targetPath + "\nSHA256: " + sha;
                AppUpdateStateDisplay = "Download complete.";
                AppendLog(new LogLine(DateTimeOffset.Now, "[OK] Update installer saved under local Updates folder (not executed).", LogSeverity.Success));
            });
        }
        catch (Exception exception)
        {
            RunOnUi(() =>
            {
                AppUpdateBannerDetail = "Download failed: " + exception.Message;
                AppUpdateStateDisplay = "Download failed.";
            });
        }
        finally
        {
            _updateDownloadInProgress = false;
            RunOnUi(() => AppUpdateDownloadInstallerCommand.RaiseCanExecuteChanged());
        }
    }

    private void CopySupportEmail()
    {
        try
        {
            Clipboard.SetText(BetaSupportInfo.SupportEmail);
            AppendLog(new LogLine(DateTimeOffset.Now, "[OK] Support email copied to clipboard.", LogSeverity.Success));
        }
        catch (Exception exception)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Clipboard copy failed: {exception.Message}", LogSeverity.Warning));
        }
    }

    private void OpenSupportEmail()
    {
        try
        {
            Process.Start(new ProcessStartInfo(BetaSupportInfo.MailtoUri) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Could not open mail client: {exception.Message}", LogSeverity.Warning));
        }
    }

    private void RefreshCopilotProviderStatus()
    {
        var settings = BuildCopilotSettingsFromUi();
        foreach (var provider in _copilotProviderRegistry.Providers)
        {
            if (!settings.Providers.TryGetValue(provider.Id, out var providerConfig))
            {
                continue;
            }

            var view = CopilotProviderSettings.FirstOrDefault(item => string.Equals(item.Id, provider.Id, StringComparison.OrdinalIgnoreCase));
            if (view is null)
            {
                continue;
            }

            view.IsConfigured = provider.IsConfigured(providerConfig);
            view.ProviderStatusLabel = CopilotProviderStatusFormatter.BuildStatusLabel(provider, providerConfig);
            view.CredentialSourceText = CopilotProviderStatusFormatter.BuildCredentialSourceLine(provider, providerConfig);
            view.MaskedApiKey = KyraApiKeyStore.Mask(KyraApiKeyStore.GetSessionKey(provider.Id));
        }

        UpdateCopilotOnlineIndicator();
        AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] Kyra provider status refreshed from environment and session (keys never logged).", LogSeverity.Info));
    }

    private void ShowAbout()
    {
        ScrollableInfoWindow.Show(
            Application.Current?.MainWindow,
            "About ForgerEMS",
            InfoDocumentTexts.BuildAbout(
                AppReleaseInfo.Version,
                AppReleaseInfo.DisplayVersion,
                string.IsNullOrWhiteSpace(_backendContext.FrontendVersion) ? "n/a" : _backendContext.FrontendVersion,
                GetBackendVersionDisplay()));
    }

    private void ShowFaq()
    {
        ScrollableInfoWindow.Show(
            Application.Current?.MainWindow,
            "ForgerEMS FAQ (Beta)",
            InfoDocumentTexts.BuildFaq());
    }

    private void ShowLegal()
    {
        ScrollableInfoWindow.Show(
            Application.Current?.MainWindow,
            "ForgerEMS Legal (Beta)",
            InfoDocumentTexts.BuildLegal());
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
                try
                {
                    if (!string.IsNullOrWhiteSpace(eventArgs.Data))
                    {
                        AppendLog(new LogLine(DateTimeOffset.Now, eventArgs.Data, LogSeverity.Info));
                    }
                }
                catch (Exception exception)
                {
                    StartupDiagnosticLog.AppendException("RunSafeExternalCommandAsync.Stdout", exception);
                }
            };
            process.ErrorDataReceived += (_, eventArgs) =>
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(eventArgs.Data))
                    {
                        AppendLog(new LogLine(DateTimeOffset.Now, eventArgs.Data, LogSeverity.Warning, isErrorStream: true));
                    }
                }
                catch (Exception exception)
                {
                    StartupDiagnosticLog.AppendException("RunSafeExternalCommandAsync.Stderr", exception);
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
        catch (Exception exception)
        {
            StartupDiagnosticLog.AppendException("RunSafeExternalCommandAsync", exception);
            AppendLog(new LogLine(DateTimeOffset.Now, $"[ERROR] {displayName} failed: {exception.Message}", LogSeverity.Error, isErrorStream: true));
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
        void ApplyOnUi()
        {
            try
            {
                Logs.Clear();
                RefreshLogsText();
                OnPropertyChanged(nameof(LogStatusLineText));
                CopyLogsCommand.RaiseCanExecuteChanged();
                ClearLogsCommand.RaiseCanExecuteChanged();
            }
            catch (Exception exception)
            {
                StartupDiagnosticLog.AppendException("ClearLogs.ApplyOnUi", exception);
            }
        }

        try
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.HasShutdownStarted)
            {
                return;
            }

            if (dispatcher.CheckAccess())
            {
                ApplyOnUi();
            }
            else
            {
                _ = dispatcher.BeginInvoke(DispatcherPriority.Normal, ApplyOnUi);
            }
        }
        catch (Exception exception)
        {
            StartupDiagnosticLog.AppendException("ClearLogs.Dispatch", exception);
        }
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
        RunWslRunnerCommand.RaiseCanExecuteChanged();
        StopWslRunnerCommand.RaiseCanExecuteChanged();
        CopyWslRunnerOutputCommand.RaiseCanExecuteChanged();
        ClearWslRunnerOutputCommand.RaiseCanExecuteChanged();
        RunWslHostListVerboseRunnerCommand.RaiseCanExecuteChanged();
        RunWslHostStatusRunnerCommand.RaiseCanExecuteChanged();
        AnalyzeLinkSafetyCommand.RaiseCanExecuteChanged();
        FetchLinkSafetyHeadersCommand.RaiseCanExecuteChanged();
        DownloadLinkToQuarantineCommand.RaiseCanExecuteChanged();
        BrowseLocalFileSafetyCommand.RaiseCanExecuteChanged();
        AnalyzeLocalFileSafetyCommand.RaiseCanExecuteChanged();
        CopyLocalFileSafetyShaCommand.RaiseCanExecuteChanged();
        CopyLocalFileSafetyReportCommand.RaiseCanExecuteChanged();
        OpenLocalSafetyQuarantineFolderCommand.RaiseCanExecuteChanged();
        CopyLocalFileToQuarantineCommand.RaiseCanExecuteChanged();
        CopyLogsCommand.RaiseCanExecuteChanged();
        ClearLogsCommand.RaiseCanExecuteChanged();
        SendCopilotMessageCommand.RaiseCanExecuteChanged();
        StopCopilotGenerationCommand.RaiseCanExecuteChanged();
    }

    private void AppendLog(LogLine line)
    {
        var sanitized = new LogLine(
            line.Timestamp,
            CopilotRedactor.Redact(line.Text, enabled: true),
            line.Severity,
            line.IsErrorStream,
            line.Channel);

        void ApplyOnUi()
        {
            try
            {
                ApplyProgressFromLog(sanitized.Text);
                Logs.Add(sanitized);

                if (Logs.Count > 600)
                {
                    Logs.RemoveAt(0);
                }

                RefreshLogsText();
                OnPropertyChanged(nameof(LogStatusLineText));
                CopyLogsCommand.RaiseCanExecuteChanged();
                ClearLogsCommand.RaiseCanExecuteChanged();
            }
            catch (Exception exception)
            {
                StartupDiagnosticLog.AppendException("AppendLog.ApplyOnUi", exception);
            }
        }

        try
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.HasShutdownStarted)
            {
                return;
            }

            if (dispatcher.CheckAccess())
            {
                ApplyOnUi();
            }
            else
            {
                _ = dispatcher.BeginInvoke(DispatcherPriority.Normal, ApplyOnUi);
            }
        }
        catch (Exception exception)
        {
            StartupDiagnosticLog.AppendException("AppendLog.Dispatch", exception);
        }

        try
        {
            _appRuntimeService.AppendSessionLog(sanitized);
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
            : string.Join(Environment.NewLine, visibleLines.TakeLast(12));
        CopyLogsCommand.RaiseCanExecuteChanged();
    }

    private bool IsVisibleLogLine(LogLine line)
    {
        if (!VerboseLiveLogs &&
            (line.Channel == LiveLogChannel.KyraDetail || line.Channel == LiveLogChannel.Diagnostics))
        {
            return false;
        }

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
            UsbProgressStageText = $"Stage: {text}";
            UsbProgressHeartbeatText = "Working. Live logs are still updating.";
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
        UsbProgressStageText = "Stage: idle";
        UsbProgressItemText = "Current item: none";
        UsbProgressPercentText = "Percent: unknown";
        UsbProgressTransferText = "Transferred: unknown";
        UsbProgressSpeedText = "Speed: unknown";
        UsbProgressHeartbeatText = "Waiting for USB/build activity.";
    }

    private void ApplyProgressFromLog(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var normalized = text.Trim();
        UpdateProgressStage(normalized);
        UpdateProgressItem(normalized);
        UpdateProgressTransfer(normalized);

        var scanProgress = Regex.Match(normalized, @"Scanned\s+(?<cur>\d+)\s*/\s*(?<tot>\d+)\s+toolkit items", RegexOptions.IgnoreCase);
        if (scanProgress.Success)
        {
            UsbProgressHeartbeatText = $"Scanned {scanProgress.Groups["cur"].Value}/{scanProgress.Groups["tot"].Value} toolkit items…";
            SetProgress(0, indeterminate: true);
            CurrentTaskState = "WORKING";
            CurrentTaskText = "Toolkit health scan";
            OnPropertyChanged(nameof(LogStatusLineText));
        }

        var percentMatch = Regex.Match(normalized, @"(?<percent>\d{1,3}(?:\.\d+)?)%");
        if (percentMatch.Success &&
            double.TryParse(percentMatch.Groups["percent"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            SetProgress(percent, indeterminate: false);
            UsbProgressPercentText = $"Percent: {Math.Clamp(percent, 0, 100):0.#}%";
            UsbProgressHeartbeatText = $"Updated: {DateTime.Now:HH:mm:ss}";
            if (normalized.Contains("Downloading", StringComparison.OrdinalIgnoreCase))
            {
                CurrentTaskState = "WORKING";
                CurrentTaskText = normalized;
                OnPropertyChanged(nameof(LogStatusLineText));
            }
            return;
        }

        if (normalized.Contains("Downloading", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("still in progress (no byte progress reported yet)", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Toolkit health scan still running", StringComparison.OrdinalIgnoreCase))
        {
            SetProgress(0, indeterminate: true);
            UsbProgressPercentText = "Percent: unknown";
            if (!normalized.Contains("Scanned", StringComparison.OrdinalIgnoreCase))
            {
                UsbProgressHeartbeatText = normalized.Contains("Toolkit health scan still running", StringComparison.OrdinalIgnoreCase)
                    ? "Toolkit health scan still running…"
                    : $"Still working: {DateTime.Now:HH:mm:ss}";
            }

            CurrentTaskState = "WORKING";
            CurrentTaskText = normalized;
            OnPropertyChanged(nameof(LogStatusLineText));
        }
    }

    private void UpdateProgressStage(string text)
    {
        var stage = text.Contains("Downloading", StringComparison.OrdinalIgnoreCase) ? "Downloading" :
            text.Contains("toolkit items", StringComparison.OrdinalIgnoreCase) && text.Contains("Scanned", StringComparison.OrdinalIgnoreCase) ? "Toolkit health scan" :
            text.Contains("Toolkit health scan still running", StringComparison.OrdinalIgnoreCase) ? "Toolkit health scan" :
            text.Contains("Verifying", StringComparison.OrdinalIgnoreCase) ? "Verifying" :
            text.Contains("Extract", StringComparison.OrdinalIgnoreCase) ? "Extracting" :
            text.Contains("USB benchmark writing", StringComparison.OrdinalIgnoreCase) ? "Benchmark write test" :
            text.Contains("USB benchmark reading", StringComparison.OrdinalIgnoreCase) ? "Benchmark read test" :
            text.Contains("Setup USB", StringComparison.OrdinalIgnoreCase) ? "Setup USB" :
            text.Contains("Update USB", StringComparison.OrdinalIgnoreCase) ? "Update USB" :
            string.Empty;

        if (!string.IsNullOrWhiteSpace(stage))
        {
            UsbProgressStageText = $"Stage: {stage}";
        }
    }

    private void UpdateProgressItem(string text)
    {
        var downloadMatch = Regex.Match(text, @"Downloading\s+(?<item>.+?)(?:\.\.\.|\s+\d{1,3}(?:\.\d+)?%|$)", RegexOptions.IgnoreCase);
        if (downloadMatch.Success)
        {
            var item = downloadMatch.Groups["item"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(item))
            {
                UsbProgressItemText = $"Current item: {item}";
            }
        }
    }

    private void UpdateProgressTransfer(string text)
    {
        var transferMatch = Regex.Match(
            text,
            @"(?<done>\d+(?:\.\d+)?)\s*(?<doneUnit>KB|MB|GB)\s*/\s*(?<total>\d+(?:\.\d+)?)\s*(?<totalUnit>KB|MB|GB)",
            RegexOptions.IgnoreCase);
        if (transferMatch.Success)
        {
            UsbProgressTransferText =
                $"Transferred: {transferMatch.Groups["done"].Value} {transferMatch.Groups["doneUnit"].Value.ToUpperInvariant()} / {transferMatch.Groups["total"].Value} {transferMatch.Groups["totalUnit"].Value.ToUpperInvariant()}";
        }

        var speedMatch = Regex.Match(text, @"(?<speed>\d+(?:\.\d+)?)\s*(?<unit>KB/s|MB/s|GB/s)", RegexOptions.IgnoreCase);
        if (speedMatch.Success)
        {
            UsbProgressSpeedText = $"Speed: {speedMatch.Groups["speed"].Value} {speedMatch.Groups["unit"].Value.ToUpperInvariant()}";
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _usbMonitorCancellation?.Cancel();
        _copilotGenerationCancellation?.Cancel();
        _usbMonitorCancellation?.Dispose();
        _copilotGenerationCancellation?.Dispose();
        try
        {
            _wslRunnerCancellation?.Cancel();
        }
        catch
        {
        }

        _wslRunnerCancellation?.Dispose();
        _updateCheckService.Dispose();
        try
        {
            _wslOutputFlushTimer?.Stop();
            while (_wslPendingOutputLines.TryDequeue(out _))
            {
            }
        }
        catch
        {
        }
    }
}
