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
using VentoyToolkitSetup.Wpf;
using VentoyToolkitSetup.Wpf.Configuration;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using VentoyToolkitSetup.Wpf.Services.Kyra;
using VentoyToolkitSetup.Wpf.Services.KyraTools;
using VentoyToolkitSetup.Wpf.Services.Licensing;

namespace VentoyToolkitSetup.Wpf.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }

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
    private readonly IUsbIntelligenceService _usbIntelligenceService;
    private readonly IAutoIntelligenceOrchestrator _autoIntelligenceOrchestrator;
    private readonly IWslCommandExecutor _wslExecutor;
    private readonly Dictionary<string, UsbBenchmarkResult> _benchmarkResultsByRoot = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _benchmarksInProgress = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _usbBuilderActionGate = new(1, 1);
    private readonly UsbMachineProfileStore _usbMachineProfileStore;
    private readonly UsbGuidedMappingWorkflow _usbGuidedMappingWorkflow = new();
    private string _usbMappingWorkflowStatus = string.Empty;
    private string _usbMappingLabelDraft = string.Empty;
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
    private string _pendingAdvancedInstallerUrl = string.Empty;
    private string _pendingReleaseNotesUrl = string.Empty;
    private string _pendingVersionLabel = string.Empty;
    private string _pendingZipUrlForClipboard = string.Empty;
    private string _checksumInstructionsClipboardText = string.Empty;
    private string _appUpdateLatestChannelText = "Latest release: —";
    private UpdateCheckResult? _lastAppliedUpdateCheckResult;
    private UpdateCheckMachineState _appUpdateMachineState = UpdateCheckMachineState.IdleNotChecked;
    private Visibility _appUpdateDownloadButtonVisibility = Visibility.Collapsed;
    private Visibility _appUpdateAdvancedDownloadButtonVisibility = Visibility.Collapsed;
    private Visibility _appUpdateCopyZipLinkVisibility = Visibility.Collapsed;
    private Visibility _appUpdateCopyChecksumInstructionsVisibility = Visibility.Collapsed;
    private Visibility _appUpdateIgnoreButtonVisibility = Visibility.Visible;
    private Visibility _appUpdateViewReleaseNotesVisibility = Visibility.Visible;
    private Visibility _appUpdateDiagnosticsHintVisibility = Visibility.Collapsed;
    private bool _verboseLiveLogs;
    private UsbManagedHeartbeatPhase _usbManagedHeartbeatPhase = UsbManagedHeartbeatPhase.Unknown;
    private CancellationTokenSource? _usbMonitorCancellation;
    private CancellationTokenSource? _manualUsbBenchmarkCts;
    private CancellationTokenSource? _autoUsbBenchmarkCts;
    private CancellationTokenSource? _autoUsbBenchmarkDebounceCts;
    private readonly UsbAutomaticBenchmarkPolicy _usbAutomaticBenchmarkPolicy = new();
    private DispatcherTimer? _usbIntelligenceDebounceTimer;
    private int _deferredOrchestrationVersion;
    private CancellationTokenSource? _copilotGenerationCancellation;
    private CopilotSettings _copilotSettings = new();
    private readonly string _kyraMemoryPath;
    private readonly string _kyraMachineMemoryPath;
    private string _kyraSanitizedContextPreviewText = string.Empty;
    private string _kyraAssistantStatusSummary = string.Empty;
    private bool _disposed;

    private enum UsbBenchmarkHostInterruptKind
    {
        None,
        UserRequested,
        SelectionChanged,
        AppShutdown
    }

    private UsbBenchmarkHostInterruptKind _usbBenchmarkHostInterruptKind;

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
    private string _managedDownloadPartialBannerText = string.Empty;
    private Visibility _managedDownloadRetryPanelVisibility = Visibility.Collapsed;
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
    private string _systemIntelligenceDeviceFitCardText = "Run a system scan to estimate best-use/device fit.";
    private string _systemIntelligenceHardwareXrayCardText = "Run a system scan to build machine class and sensor exposure coverage.";
    private string _systemIntelligenceScanModeHintText = "Standard scan: safe non-admin scan. Elevated scan unlocks deeper hardware/security detail.";
    private string _systemIntelligenceStaleBannerText = string.Empty;
    private string _systemIntelligenceAutomationLineText = string.Empty;
    private string _systemIntelligenceWarningReasonText = "Warning reason: none.";
    private string _systemIntelligenceScanStatusText = "Scan status: Not scanned";
    private string _systemIntelligenceHealthStatusText = "Health status: Unknown";
    private string _systemIntelligenceWindowsReadinessText = "Windows readiness: Unknown";
    private string _systemIntelligenceNetworkTechnicalDetailsText = "Technical network details are hidden.";
    private bool _systemIntelligenceShowNetworkTechnicalDetails;
    private string _systemIntelligenceReportSafePathText = @"Runtime\reports\system-intelligence-latest.json";
    private int _deepSensorModeSelectedIndex;
    private string _deepSensorModeSourceSummary = string.Empty;
    private string _deepSensorModeConsentNotice = string.Empty;
    private bool _isLoadingDeepSensorModeSetting = true;
    private Brush _systemIntelligenceStatusBackground = RunningBackground;
    private Brush _systemIntelligenceStatusBorderBrush = RunningBorder;
    private Brush _systemIntelligenceStatusForeground = RunningForeground;
    private string _toolkitStatusText = "Not scanned";
    private string _toolkitReportPathText = "Report: not generated";
    private string _toolkitInstalledCountText = "Managed Ready: 0";
    private string _toolkitMissingCountText = "Managed Missing: 0";
    private string _toolkitUpdatesCountText = "Managed updates available: 0";
    private string _toolkitFailedCountText = "Verification issues: 0";
    private string _toolkitManualCountText = "Manual 0";
    private string _toolkitPlaceholderCountText = "Skipped/Placeholder 0";
    private string _toolkitHealthVerdictText = "Health Verdict: not scanned";
    private string _toolkitManualExplanationText =
        "Manual Required means ForgerEMS cannot legally or safely auto-download this item (licensing, vendor gating, or verification limits). Use the provided link or instructions, place files where the manifest expects, then re-run Refresh Health.";
    private string _selectedToolkitFilter = "All";
    private string _selectedToolkitCategoryFilter = "All categories";
    private string _toolkitSearchText = string.Empty;
    private string _toolkitLastScanText = "Last scan: never";
    private string _toolkitClassificationSummaryText = string.Empty;
    private ToolkitHealthItemView? _selectedToolkitHealthItem;
    private Brush _toolkitStatusBackground = RunningBackground;
    private Brush _toolkitStatusBorderBrush = RunningBorder;
    private Brush _toolkitStatusForeground = RunningForeground;
    private readonly List<ToolkitHealthItemView> _allToolkitHealthItems = [];
    private string _copilotInput = string.Empty;
    private string _kyraActivityStatusText = string.Empty;

    private string _kyraGatewayProviderStatusSummary =
        "Tap “Check gateway status” for server-side provider readiness. The app never logs your gateway token.";
    private bool _kyraSlashPopupOpen;
    private bool _kyraHasSystemScanReport;
    private bool _kyraHasRecentWarningLog;
    private bool _kyraShowLiveToolsQuickButton;
    private int _kyraSlashSelectedIndex = -1;
    private DateTime _kyraSlashPopupQuietUntilUtc = DateTime.MinValue;
    private bool _betaWelcomeKyraShareRepair;
    private bool _betaWelcomeKyraShareHardware;
    private bool _betaWelcomeKyraShareResolved;
    private bool _betaWelcomeKyraShareCrash;
    private string _copilotContextText = "Run a system scan and select a USB target to load Kyra context.";
    private string _copilotContextSummaryText = "System Context\n- Device: run System Intelligence\n- CPU: unknown\n- RAM: unknown\n- GPU: unknown\n- Storage: unknown\n- Battery: unknown\n- USB: none selected";
    private string _copilotProviderSummaryText = "Local Offline Rules: Ready\nOnline AI: Not configured\nLocal AI: Not configured\nPricing Lookup: Not configured";
    private string _copilotProviderBadgeText = "Offline Ready";
    private string _copilotPrivacyBadgeText = "Local Only";
    private string _copilotActiveProviderText = "Provider: Local Kyra";
    private string _copilotDiagnosticsSummaryText = "Kyra online assistants enabled: 0 | configured: 0 | Fallback: Local Kyra";
    private string _copilotLastProviderFailureText = "Last provider failure: none";
    private Visibility _copilotTechnicalContextVisibility = Visibility.Collapsed;
    private string _copilotTechnicalContextButtonText = "View technical context";
    private string _usbIntelligenceBuilderHintText =
        "USB Intelligence: select a USB target to classify the port speed and builder readiness.";

    private string _usbIntelligencePanelTargetDisplay = "No USB target selected";

    private string _usbIntelligenceDetectedClassDisplay = "—";

    private string _usbIntelligenceBenchmarkReadWriteDisplay = "—";

    private string _usbIntelligenceRecommendationQualityDisplay = "—";

    private string _usbIntelligenceConfidenceScoreDisplay = "—";

    private string _usbIntelligenceConfidenceReasonDisplay = string.Empty;

    private string _usbIntelligenceLastBenchmarkTimeDisplay = "—";

    private string _usbIntelligenceMappingLabelDisplay = "—";

    private string _usbIntelligenceBestKnownPortDisplay = "—";

    private string _usbIntelligenceBenchmarkAgeDisplay = "—";

    private string _usbIntelligenceRunBenchmarkHintDisplay = string.Empty;

    private string _unifiedDiagnosticsSummaryText = "Unified diagnostics: not generated yet.";

    private string _diagnosticsHealthChecklistText =
        "Diagnostics checklist: generate a session report from the intelligence refresh, then re-open this tab.";
    private string _diagnosticsWarningReasonText = "Warning reason: unavailable.";
    private bool _diagnosticsShowFullDetail;
    private string _diagnosticsAppActionStatusText = "App action status: COMPLETE";
    private string _diagnosticsHealthStatusText = "Diagnostics health: Unknown";
    private string _diagnosticsBackendChipText = "Backend: unknown";
    private string _diagnosticsUsbChipText = "USB: none";
    private string _diagnosticsSystemChipText = "System Intelligence: unknown";
    private string _diagnosticsToolkitChipText = "Toolkit: unknown";
    private string _diagnosticsKyraChipText = "Kyra: unknown";
    private string _diagnosticsUpdateChipText = "Update: unknown";
    private string _diagnosticsLogSearchText = string.Empty;
    private DateTimeOffset? _lastCommandStartedAt;
    private DateTimeOffset? _lastCommandFinishedAt;
    private int? _lastCommandExitCode;
    private string _lastCommandStatusText = "Not started";
    private string _lastCommandSummaryText = "No command summary yet.";

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
    private readonly ConcurrentQueue<LogLine> _pendingLiveLogs = new();
    private DispatcherTimer? _wslOutputFlushTimer;
    private DispatcherTimer? _liveLogFlushTimer;
    private string _windowsSandboxStatusText = string.Empty;
    private string _safeTestingEnvironmentSummaryText = string.Empty;
    private SafeTestingEnvironmentStatus _cachedSafeTestingStatus = SafeTestingEnvironmentProbe.ProbeQuick();
    private bool _experimentalEmbeddedWslRunner;
    private CancellationTokenSource? _safeTestingEnvironmentRefreshCts;
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
        IWslCommandExecutor? wslExecutor = null,
        IUsbIntelligenceService? usbIntelligenceService = null,
        IAutoIntelligenceOrchestrator? autoIntelligenceOrchestrator = null)
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
        _usbIntelligenceService = usbIntelligenceService ?? new UsbIntelligenceService();
        _autoIntelligenceOrchestrator = autoIntelligenceOrchestrator ?? new AutoIntelligenceOrchestrator(
            _appRuntimeService,
            _powerShellRunnerService,
            _usbIntelligenceService,
            new DiagnosticsService(),
            ResolveSystemIntelligenceScriptForBackend,
            MarshalIntelligenceRefreshAsync);
        _wslExecutor = wslExecutor ?? DefaultWslCommandExecutor.Instance;
        _usbMachineProfileStore = new UsbMachineProfileStore(_appRuntimeService.RuntimeRoot);
        _benchmarkCachePath = Path.Combine(_appRuntimeService.RuntimeRoot, "cache", "usb-benchmarks.json");
        _copilotConfigPath = Path.Combine(_appRuntimeService.RuntimeRoot, "config", "copilot-settings.json");
        _kyraMemoryPath = Path.Combine(_appRuntimeService.RuntimeRoot, "config", "kyra-memory.json");
        _kyraMachineMemoryPath = Path.Combine(_appRuntimeService.RuntimeRoot, "config", "kyra-machine-memory.json");
        _betaConfigPath = Path.Combine(_appRuntimeService.RuntimeRoot, "config", "beta-settings.json");
        _updateConfigPath = Path.Combine(_appRuntimeService.RuntimeRoot, "config", "update-settings.json");
        _updateSettingsStore = new AppUpdateSettingsStore(_updateConfigPath);
        _updateCheckService = new GitHubReleaseUpdateCheckService();
        LoadBenchmarkCache();
        LoadCopilotSettings();
        LoadBetaSettings();
        LoadUpdateSettings();
        RefreshDeepSensorModeSettingsProperties();
        _isLoadingDeepSensorModeSetting = false;

        RefreshAllCommand = new AsyncRelayCommand(RefreshAllAsync, () => !IsBusy);
        RefreshUsbTargetsCommand = new AsyncRelayCommand(RefreshUsbTargetsAsync, () => !IsBusy);
        VerifyCommand = new AsyncRelayCommand(RunVerifyAsync, CanRunBackendOnlyActions);
        RevalidateManagedDownloadsCommand = new AsyncRelayCommand(RunRevalidateManagedDownloadsAsync, CanRunBackendOnlyActions);
        SetupUsbCommand = new AsyncRelayCommand(RunSetupUsbAsync, CanRunTargetedActions);
        UpdateUsbCommand = new AsyncRelayCommand(RunUpdateUsbAsync, CanRunTargetedActions);
        RetryFailedManagedDownloadsCommand = new AsyncRelayCommand(RunRetryFailedManagedDownloadsAsync, CanRetryFailedManagedDownloads);
        RenameUsbCommand = new AsyncRelayCommand(RunRenameUsbAsync, CanRunTargetedActions);
        InstallOrUpdateVentoyCommand = new AsyncRelayCommand(RunInstallOrUpdateVentoyAsync, CanRunTargetedActions);
        RunSystemScanCommand = new AsyncRelayCommand(RunSystemScanAsync, CanRunBackendOnlyActions);
        RunElevatedSystemScanCommand = new AsyncRelayCommand(RunElevatedSystemScanAsync, CanRunBackendOnlyActions);
        RefreshToolkitHealthCommand = new AsyncRelayCommand(RunToolkitHealthScanAsync, CanRunToolkitScan);
        UpdateToolkitCommand = new AsyncRelayCommand(RunToolkitUpdateAsync, CanRunTargetedActions);
        OpenSystemReportFolderCommand = new RelayCommand(OpenSystemReportFolder);
        OpenSystemJsonReportCommand = new RelayCommand(OpenSystemJsonReport);
        OpenSystemMarkdownReportCommand = new RelayCommand(OpenSystemMarkdownReport);
        CopySystemReportSafePathCommand = new RelayCommand(CopySystemReportSafePath);
        CopySystemSummaryCommand = new RelayCommand(CopySystemSummary);
        OpenToolkitUsbReportsCommand = new RelayCommand(OpenToolkitUsbReports, () => SelectedUsbTarget is not null);
        OpenToolkitLocalReportsCommand = new RelayCommand(OpenToolkitLocalReports);
        RecheckSelectedToolCommand = new AsyncRelayCommand(RunToolkitHealthScanAsync, () => CanRunToolkitScan() && SelectedToolkitHealthItem is not null);
        OpenSelectedToolLocationCommand = new RelayCommand(OpenSelectedToolLocation, () => SelectedToolkitHealthItem is not null);
        OpenManualDownloadShortcutCommand = new RelayCommand(OpenManualDownloadShortcut, () => SelectedToolkitHealthItem is not null);
        CopySelectedToolkitExpectedPathCommand = new RelayCommand(CopySelectedToolkitExpectedPath, () => SelectedToolkitHealthItem is not null);
        CopySelectedToolkitDetectedPathCommand = new RelayCommand(CopySelectedToolkitDetectedPath, () => SelectedToolkitHealthItem is not null);
        CopyLogsCommand = new RelayCommand(CopyLogs, () => !string.IsNullOrWhiteSpace(LogsText));
        ClearLogsCommand = new RelayCommand(ClearLogs, () => Logs.Count > 0);
        ShowAboutCommand = new RelayCommand(ShowAbout);
        ShowFaqCommand = new RelayCommand(ShowFaq);
        ShowLegalCommand = new RelayCommand(ShowLegal);
        ShowPrivacyCommand = new RelayCommand(ShowPrivacy);
        OpenUbuntuTerminalCommand = new AsyncRelayCommand(OpenUbuntuTerminalAsync, () => !IsBusy);
        RefreshSafeTestingEnvironmentCommand = new AsyncRelayCommand(RefreshSafeTestingEnvironmentAsync, () => !IsBusy);
        CopySafeTestingSummaryCommand = new RelayCommand(CopySafeTestingSummary);
        OpenWindowsSandboxHelpCommand = new RelayCommand(OpenWindowsSandboxHelp);
        CheckWslInstalledCommand = new AsyncRelayCommand(() => RunSafeExternalCommandAsync("Check WSL installed", "wsl.exe", "--status"), () => !IsBusy);
        ShowWslDistrosCommand = new AsyncRelayCommand(() => RunSafeExternalCommandAsync("Show WSL distros", "wsl.exe", "-l", "-v"), () => !IsBusy);
        CheckPowerShellVersionCommand = new AsyncRelayCommand(() => RunSafeExternalCommandAsync("Check PowerShell version", "powershell", "-NoProfile", "-Command", "$PSVersionTable.PSVersion.ToString()"), () => !IsBusy);
        CheckBackendFilesCommand = new RelayCommand(RunBackendFilesReadOnlyCheck);
        CheckReleaseIdentityCommand = new RelayCommand(RunReleaseIdentityReadOnlyCheck);
        CheckNetworkDnsCommand = new AsyncRelayCommand(() => RunSafeExternalCommandAsync("Check network/DNS", "powershell", "-NoProfile", "-Command", "Get-DnsClientServerAddress -AddressFamily IPv4 | Select-Object -First 5 InterfaceAlias,ServerAddresses"), () => !IsBusy);
        CopyDiagnosticsCommandSummaryCommand = new RelayCommand(CopyDiagnosticsCommandSummary);
        CopyLast200LogsCommand = new RelayCommand(CopyLast200Logs);
        OpenReportsFolderCommand = new RelayCommand(() => OpenFolder(GetRuntimeReportsDirectory(), "reports folder", createIfMissing: true));
        RunWslRunnerCommand = new AsyncRelayCommand(
            RunWslRunnerAsync,
            () => !IsBusy && !_isWslRunnerBusy && DiagnosticsFeatureFlags.EmbeddedWslCommandRunnerEnabled && _wslExecutor.IsWslInstalled());
        StopWslRunnerCommand = new RelayCommand(StopWslRunner);
        CopyWslRunnerOutputCommand = new RelayCommand(CopyWslRunnerOutput, () => !string.IsNullOrWhiteSpace(_wslRunnerOutputText));
        ClearWslRunnerOutputCommand = new RelayCommand(ClearWslRunnerOutputPane, () => !string.IsNullOrWhiteSpace(_wslRunnerOutputText));
        InsertWslRunnerPresetCommand = new RelayCommand<string>(
            preset =>
            {
                if (!string.IsNullOrWhiteSpace(preset))
                {
                    WslRunnerCommandInput = preset;
                }
            },
            _ => DiagnosticsFeatureFlags.EmbeddedWslCommandRunnerEnabled);
        RunWslHostListVerboseRunnerCommand = new AsyncRelayCommand(
            () => RunWslHostArgumentsUiAsync(WslHostListVerboseArgs, "wsl.exe --list --verbose"),
            () => !IsBusy && !_isWslRunnerBusy && DiagnosticsFeatureFlags.EmbeddedWslCommandRunnerEnabled && _wslExecutor.IsWslInstalled());
        RunWslHostStatusRunnerCommand = new AsyncRelayCommand(
            () => RunWslHostArgumentsUiAsync(WslHostStatusArgs, "wsl.exe --status"),
            () => !IsBusy && !_isWslRunnerBusy && DiagnosticsFeatureFlags.EmbeddedWslCommandRunnerEnabled && _wslExecutor.IsWslInstalled());
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
        StartUsbPortMappingWorkflowCommand = new RelayCommand(StartUsbPortMappingWorkflow, () => SelectedUsbTarget is not null);
        CaptureUsbMappingBeforeCommand = new RelayCommand(CaptureUsbMappingBefore, () => SelectedUsbTarget is not null);
        CaptureUsbMappingAfterCommand = new RelayCommand(CaptureUsbMappingAfter, () => SelectedUsbTarget is not null);
        SaveUsbMappingLabelCommand = new RelayCommand(SaveUsbMappingLabel, () => SelectedUsbTarget is not null && !string.IsNullOrWhiteSpace(UsbMappingLabelDraft));
        OpenUsbMappingWizardCommand = new RelayCommand(OpenUsbMappingWizard);
        RunUsbIntelligenceBenchmarkCommand =
            new AsyncRelayCommand(RunUsbIntelligenceBenchmarkAsync, CanRunUsbIntelligenceBenchmark);
        CancelUsbIntelligenceBenchmarkCommand =
            new RelayCommand(CancelActiveUsbBenchmark, IsAnyUsbBenchmarkActive);
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
        CheckKyraGatewayStatusCommand = new AsyncRelayCommand(CheckKyraGatewayStatusAsync, () => !IsCopilotGenerating);
        ClearProviderSessionKeysCommand = new RelayCommand(ClearProviderSessionKeys);
        RefreshCopilotProviderStatusCommand = new RelayCommand(RefreshCopilotProviderStatus);
        SaveKyraLiveToolsSettingsCommand = new RelayCommand(SaveCopilotSettings);
        ExportKyraMemoryCommand = new RelayCommand(ExportKyraMemory);
        ClearKyraMemoryCommand = new RelayCommand(ClearKyraMemory);
        ViewKyraMemoryCommand = new RelayCommand(ViewKyraMemory);
        ViewKyraCommunityPayloadPreviewCommand = new RelayCommand(ViewKyraCommunityPayloadPreview);
        ExportKyraIntelligenceMemoryCommand = new RelayCommand(ExportKyraIntelligenceMemory);
        DeleteKyraIntelligenceMemoryCommand = new RelayCommand(DeleteKyraIntelligenceMemory);
        KeepKyraLocalOnlyCommand = new RelayCommand(KeepKyraLocalOnly);
        EnableKyraCommunityLearningCommand = new RelayCommand(EnableKyraCommunityLearning);
        LearnMoreKyraIntelligenceCommand = new RelayCommand(ShowPrivacy);
        KyraFeedbackThisFixedItCommand = new RelayCommand<CopilotChatMessage>(ApplyKyraFeedbackThisFixedIt);
        KyraFeedbackStillBrokenCommand = new RelayCommand<CopilotChatMessage>(ApplyKyraFeedbackStillBroken);
        KyraFeedbackNotSureCommand = new RelayCommand<CopilotChatMessage>(ApplyKyraFeedbackNotSure);
        KyraFeedbackSaveRepairNoteCommand = new RelayCommand<CopilotChatMessage>(ApplyKyraFeedbackSaveRepairNote);
        BetaWelcomeKyraKeepLocalOnlyCommand = new RelayCommand(BetaWelcomeKyraKeepLocalOnly);
        BetaWelcomeKyraHelpImproveCommand = new RelayCommand(BetaWelcomeKyraHelpImprove);
        BetaWelcomeKyraViewSharingPreviewCommand = new RelayCommand(BetaWelcomeKyraViewSharingPreview);
        ResetKyraMachineLearningCommand = new RelayCommand(ResetKyraMachineLearning);
        OpenLogsFolderCommand = new RelayCommand(() => OpenFolder(_appRuntimeService.LogsRoot, "logs folder", createIfMissing: true));
        CopySupportEmailCommand = new RelayCommand(CopySupportEmail);
        OpenSupportEmailCommand = new RelayCommand(OpenSupportEmail);
        CopyBetaReportTemplateCommand = new RelayCommand(CopyBetaReportTemplate);
        CheckForUpdatesNowCommand = new AsyncRelayCommand(() => RequestUpdateCheckAsync(manual: true), () => !_updateCheckInProgress);
        CopyUpdateDiagnosticsCommand = new RelayCommand(CopyUpdateCheckDiagnostics, CanCopyUpdateCheckDiagnostics);
        ExportSupportBundleCommand = new AsyncRelayCommand(ExportSupportBundleAsync, () => !IsBusy && ForgerEmsEnvironmentConfiguration.EnableDiagnosticBundle);
        AppUpdateRemindLaterCommand = new RelayCommand(HideAppUpdateBanner);
        AppUpdateIgnoreVersionCommand = new RelayCommand(IgnorePendingAppUpdateVersion);
        AppUpdateViewReleaseNotesCommand = new RelayCommand(OpenPendingReleaseNotes);
        AppUpdateDownloadInstallerCommand = new AsyncRelayCommand(DownloadPendingInstallerAsync, CanDownloadPendingInstaller);
        AppUpdateDownloadAdvancedInstallerCommand =
            new AsyncRelayCommand(DownloadPendingAdvancedInstallerAsync, CanDownloadPendingAdvancedInstaller);
        CopyUpdateZipLinkCommand = new RelayCommand(CopyPendingZipLink, CanCopyPendingZipLink);
        CopyUpdateChecksumInstructionsCommand =
            new RelayCommand(CopyPendingChecksumInstructions, CanCopyPendingChecksumInstructions);
        CopyUpdateDiagnosticsCommand.RaiseCanExecuteChanged();
        ClearIgnoredAppUpdateVersionCommand = new RelayCommand(ClearIgnoredAppUpdateVersion, CanClearIgnoredAppUpdateVersion);
        ClearIgnoredAppUpdateVersionCommand.RaiseCanExecuteChanged();
        CheckForUpdatesNowCommand.RaiseCanExecuteChanged();

        CopilotMessages.Add(new CopilotChatMessage
        {
            Role = "Kyra",
            Text = KyraOnboardingCopy.BuildInitialWelcomeMessage(_copilotSettings),
            SourceLabel = "Kyra"
        });

        Logs.CollectionChanged += (_, _) => RefreshKyraQuickPromptVisibilities();

        RefreshWslRunnerSummary();
        RefreshDiagnosticsAuxiliaryText();
        RefreshEmbeddedWslDiagnosticsBindings();
        RefreshKyraQuickPromptVisibilities();
        ScheduleBackgroundUpdateCheck();
    }

    public ObservableCollection<UsbTargetInfo> UsbTargets { get; } = [];

    public ObservableCollection<LogLine> Logs { get; } = [];

    public ObservableCollection<string> SystemIntelligenceRecommendations { get; } = [];
    public ObservableCollection<string> SystemIntelligenceNextActions { get; } = [];
    public ObservableCollection<string> DiagnosticsActionCenterItems { get; } = [];

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

    public IReadOnlyList<string> ToolkitFilterOptions { get; } =
    [
        "All",
        "Installed",
        "Managed Missing",
        "Manual / Info",
        "Manual Required",
        "Verification Issues",
        "Managed Updates",
        "Skipped/Placeholder"
    ];

    public IReadOnlyList<string> ToolkitCategoryFilterOptions { get; } = ["All categories", "Windows", "Linux", "Recovery", "Diagnostics", "USB Builders"];

    public IReadOnlyList<string> CopilotModeOptions { get; } = ["ForgerEMS Beta Gateway", "BYOK", "Local Only", "Offline Only", "Free API Pool", "Hybrid", "Online/API", "Ask First"];

    public AsyncRelayCommand RefreshAllCommand { get; }

    public AsyncRelayCommand RefreshUsbTargetsCommand { get; }

    public AsyncRelayCommand VerifyCommand { get; }

    public AsyncRelayCommand RevalidateManagedDownloadsCommand { get; }

    public AsyncRelayCommand SetupUsbCommand { get; }

    public AsyncRelayCommand UpdateUsbCommand { get; }

    public AsyncRelayCommand RetryFailedManagedDownloadsCommand { get; }

    public AsyncRelayCommand RenameUsbCommand { get; }

    public AsyncRelayCommand InstallOrUpdateVentoyCommand { get; }

    public AsyncRelayCommand RunSystemScanCommand { get; }

    public AsyncRelayCommand RunElevatedSystemScanCommand { get; }

    public AsyncRelayCommand RefreshToolkitHealthCommand { get; }

    public AsyncRelayCommand UpdateToolkitCommand { get; }

    public RelayCommand OpenSystemReportFolderCommand { get; }
    public RelayCommand OpenSystemJsonReportCommand { get; }
    public RelayCommand OpenSystemMarkdownReportCommand { get; }
    public RelayCommand CopySystemReportSafePathCommand { get; }

    public RelayCommand CopySystemSummaryCommand { get; }

    public RelayCommand OpenToolkitUsbReportsCommand { get; }

    public RelayCommand OpenToolkitLocalReportsCommand { get; }

    public AsyncRelayCommand RecheckSelectedToolCommand { get; }

    public RelayCommand OpenSelectedToolLocationCommand { get; }

    public RelayCommand OpenManualDownloadShortcutCommand { get; }
    public RelayCommand CopySelectedToolkitExpectedPathCommand { get; }
    public RelayCommand CopySelectedToolkitDetectedPathCommand { get; }

    public RelayCommand CopyLogsCommand { get; }

    public RelayCommand ClearLogsCommand { get; }

    public RelayCommand ShowAboutCommand { get; }

    public RelayCommand ShowFaqCommand { get; }

    public RelayCommand ShowLegalCommand { get; }

    public RelayCommand ShowPrivacyCommand { get; }

    public AsyncRelayCommand OpenUbuntuTerminalCommand { get; }

    public AsyncRelayCommand RefreshSafeTestingEnvironmentCommand { get; }

    public RelayCommand CopySafeTestingSummaryCommand { get; }

    public RelayCommand OpenWindowsSandboxHelpCommand { get; }

    public AsyncRelayCommand CheckWslInstalledCommand { get; }

    public AsyncRelayCommand ShowWslDistrosCommand { get; }

    public AsyncRelayCommand CheckPowerShellVersionCommand { get; }

    public RelayCommand CheckBackendFilesCommand { get; }

    public RelayCommand CheckReleaseIdentityCommand { get; }

    public AsyncRelayCommand CheckNetworkDnsCommand { get; }

    public AsyncRelayCommand RunWslRunnerCommand { get; }

    public RelayCommand StopWslRunnerCommand { get; }

    public RelayCommand CopyWslRunnerOutputCommand { get; }

    public RelayCommand ClearWslRunnerOutputCommand { get; }

    public RelayCommand CopyDiagnosticsCommandSummaryCommand { get; }

    public RelayCommand CopyLast200LogsCommand { get; }

    public RelayCommand OpenReportsFolderCommand { get; }

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

    public RelayCommand StartUsbPortMappingWorkflowCommand { get; }

    public RelayCommand CaptureUsbMappingBeforeCommand { get; }

    public RelayCommand CaptureUsbMappingAfterCommand { get; }

    public RelayCommand SaveUsbMappingLabelCommand { get; }

    public RelayCommand OpenUsbMappingWizardCommand { get; }

    public AsyncRelayCommand RunUsbIntelligenceBenchmarkCommand { get; }

    public RelayCommand CancelUsbIntelligenceBenchmarkCommand { get; }

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

    public AsyncRelayCommand CheckKyraGatewayStatusCommand { get; }

    public RelayCommand ClearProviderSessionKeysCommand { get; }

    public RelayCommand RefreshCopilotProviderStatusCommand { get; }

    public RelayCommand SaveKyraLiveToolsSettingsCommand { get; }

    public RelayCommand ExportKyraMemoryCommand { get; }

    public RelayCommand ClearKyraMemoryCommand { get; }

    public RelayCommand ViewKyraMemoryCommand { get; }

    public RelayCommand ViewKyraCommunityPayloadPreviewCommand { get; }

    public RelayCommand ExportKyraIntelligenceMemoryCommand { get; }

    public RelayCommand DeleteKyraIntelligenceMemoryCommand { get; }

    public RelayCommand KeepKyraLocalOnlyCommand { get; }

    public RelayCommand EnableKyraCommunityLearningCommand { get; }

    public RelayCommand LearnMoreKyraIntelligenceCommand { get; }

    public RelayCommand<CopilotChatMessage> KyraFeedbackThisFixedItCommand { get; }

    public RelayCommand<CopilotChatMessage> KyraFeedbackStillBrokenCommand { get; }

    public RelayCommand<CopilotChatMessage> KyraFeedbackNotSureCommand { get; }

    public RelayCommand<CopilotChatMessage> KyraFeedbackSaveRepairNoteCommand { get; }

    public RelayCommand BetaWelcomeKyraKeepLocalOnlyCommand { get; }

    public RelayCommand BetaWelcomeKyraHelpImproveCommand { get; }

    public RelayCommand BetaWelcomeKyraViewSharingPreviewCommand { get; }

    public RelayCommand ResetKyraMachineLearningCommand { get; }

    public RelayCommand CopySupportEmailCommand { get; }

    public RelayCommand OpenSupportEmailCommand { get; }

    public RelayCommand CopyBetaReportTemplateCommand { get; }

    public AsyncRelayCommand CheckForUpdatesNowCommand { get; }

    public RelayCommand AppUpdateRemindLaterCommand { get; }

    public RelayCommand AppUpdateIgnoreVersionCommand { get; }

    public RelayCommand AppUpdateViewReleaseNotesCommand { get; }

    public AsyncRelayCommand AppUpdateDownloadInstallerCommand { get; }

    public AsyncRelayCommand AppUpdateDownloadAdvancedInstallerCommand { get; }

    public RelayCommand CopyUpdateZipLinkCommand { get; }

    public RelayCommand CopyUpdateChecksumInstructionsCommand { get; }

    public RelayCommand CopyUpdateDiagnosticsCommand { get; }

    public AsyncRelayCommand ExportSupportBundleCommand { get; }

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
            var priorRoot = _selectedUsbTarget?.RootPath;
            if (!SetProperty(ref _selectedUsbTarget, value))
            {
                return;
            }

            var newRoot = value?.RootPath;
            if (!UsbRootPathsEqual(priorRoot, newRoot))
            {
                CancelUsbBenchmarksForSelectionChange();
                InvalidateToolkitHealthForSelectionChange(priorRoot, newRoot);
            }

            UpdateTargetWarnings();
            RaiseCommandStates();
            OnPropertyChanged(nameof(HeaderUsbTargetText));
            OnPropertyChanged(nameof(LogStatusLineText));
            RefreshCopilotContextText();

            if (!_suppressSelectionRefresh)
            {
                _ = RefreshVentoyStatusSafeAsync();
                ScheduleAutomaticUsbBenchmark();
                ScheduleDebouncedUsbIntelligenceRefresh();
            }

            RefreshUsbIntelligenceFromDisk();
            RefreshManagedDownloadRunArtifactFromSelectedUsb();
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

    public string PublicPreviewBannerText { get; } = AppReleaseInfo.PublicPreviewBannerLine;

    public string FeatureMaturityGuideText => FeatureStatusService.BuildFeatureMaturityGuide();

    public string DeepSensorModeSettingsSummary
    {
        get
        {
            var resolution = ForgerEmsEnvironmentConfiguration.DeepSensorModeResolution;
            return
                $"Deep Sensor Mode: {resolution.Mode}. Current source: {resolution.DisplaySource}. " +
                "Read-only local sensors may improve Hardware X-Ray sensor coverage while ForgerEMS is running or scanning. " +
                "ForgerEMS does not control fans, voltages, clocks, BIOS, or firmware.";
        }
    }

    public int DeepSensorModeSelectedIndex
    {
        get => _deepSensorModeSelectedIndex;
        set
        {
            if (!SetProperty(ref _deepSensorModeSelectedIndex, value))
            {
                return;
            }

            if (_isLoadingDeepSensorModeSetting)
            {
                return;
            }

            var mode = value == 1 ? DeepSensorModeValues.ReadOnly : DeepSensorModeValues.Off;
            DeepSensorModeResolver.SaveUserMode(mode);
            RefreshDeepSensorModeSettingsProperties();
            AppendLog(new LogLine(
                DateTimeOffset.Now,
                mode.Equals(DeepSensorModeValues.ReadOnly, StringComparison.OrdinalIgnoreCase)
                    ? "[INFO] ForgerEMS Deep Sensor Mode enabled for this user: local read-only hardware sensors only."
                    : "[INFO] ForgerEMS Deep Sensor Mode set to Off for this user.",
                LogSeverity.Info));
        }
    }

    public string DeepSensorModeSourceSummary
    {
        get => _deepSensorModeSourceSummary;
        private set => SetProperty(ref _deepSensorModeSourceSummary, value);
    }

    public string DeepSensorModeConsentNotice
    {
        get => _deepSensorModeConsentNotice;
        private set => SetProperty(ref _deepSensorModeConsentNotice, value);
    }

    public string KyraProviderHubConfigHealthSummary => KyraProviderHubConfigHealthFormatter.BuildSummary();

    public string HeaderUsbTargetText => SelectedUsbTarget is null ? "USB: none" : $"USB: {SelectedUsbTarget.RootPath}";

    public string UsbBuilderTargetStatusBanner =>
        SelectedUsbTarget is null
            ? "Selected USB target: none. Choose a removable volume in the list — use the large data partition, not the tiny EFI or VTOYEFI boot slice."
            : $"Selected USB target: {(string.IsNullOrWhiteSpace(SelectedUsbTarget.DriveLetter) ? "—" : SelectedUsbTarget.DriveLetter.TrimEnd('\\'))} · {SelectedUsbTarget.LabelDisplay} — {SelectedUsbTarget.SafetyStatusText}. {SelectedUsbTarget.SelectionStatusText}";

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
        private set
        {
            if (SetProperty(ref _lastCommandText, value))
            {
                OnPropertyChanged(nameof(LastCommandNameText));
                OnPropertyChanged(nameof(LastCommandToolText));
            }
        }
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

    public ObservableCollection<ManagedDownloadFailedItemRecord> ManagedDownloadFailedRows { get; } = new();

    public string ManagedDownloadPartialBannerText
    {
        get => _managedDownloadPartialBannerText;
        private set => SetProperty(ref _managedDownloadPartialBannerText, value);
    }

    public Visibility ManagedDownloadRetryPanelVisibility
    {
        get => _managedDownloadRetryPanelVisibility;
        private set => SetProperty(ref _managedDownloadRetryPanelVisibility, value);
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

    public string UsbIntelligenceBuilderHintText
    {
        get => _usbIntelligenceBuilderHintText;
        private set => SetProperty(ref _usbIntelligenceBuilderHintText, value);
    }

    public string UsbIntelligenceProPreviewBadge => "Pro feature preview — licensing not enforced in beta.";

    public string UsbIntelligencePanelTargetDisplay
    {
        get => _usbIntelligencePanelTargetDisplay;
        private set => SetProperty(ref _usbIntelligencePanelTargetDisplay, value);
    }

    public string UsbIntelligenceDetectedClassDisplay
    {
        get => _usbIntelligenceDetectedClassDisplay;
        private set => SetProperty(ref _usbIntelligenceDetectedClassDisplay, value);
    }

    public string UsbIntelligenceBenchmarkReadWriteDisplay
    {
        get => _usbIntelligenceBenchmarkReadWriteDisplay;
        private set => SetProperty(ref _usbIntelligenceBenchmarkReadWriteDisplay, value);
    }

    public string UsbIntelligenceRecommendationQualityDisplay
    {
        get => _usbIntelligenceRecommendationQualityDisplay;
        private set => SetProperty(ref _usbIntelligenceRecommendationQualityDisplay, value);
    }

    public string UsbIntelligenceConfidenceScoreDisplay
    {
        get => _usbIntelligenceConfidenceScoreDisplay;
        private set => SetProperty(ref _usbIntelligenceConfidenceScoreDisplay, value);
    }

    public string UsbIntelligenceConfidenceReasonDisplay
    {
        get => _usbIntelligenceConfidenceReasonDisplay;
        private set => SetProperty(ref _usbIntelligenceConfidenceReasonDisplay, value);
    }

    public string UsbIntelligenceLastBenchmarkTimeDisplay
    {
        get => _usbIntelligenceLastBenchmarkTimeDisplay;
        private set => SetProperty(ref _usbIntelligenceLastBenchmarkTimeDisplay, value);
    }

    public string UsbIntelligenceMappingLabelDisplay
    {
        get => _usbIntelligenceMappingLabelDisplay;
        private set => SetProperty(ref _usbIntelligenceMappingLabelDisplay, value);
    }

    public string UsbIntelligenceGuidanceIntro => UsbIntelligencePanelUiCopy.GuidanceIntro;

    public string UsbIntelligenceWorkflowHelp => UsbIntelligencePanelUiCopy.WorkflowNumbered;

    public string UsbIntelligenceBestKnownPortDisplay
    {
        get => _usbIntelligenceBestKnownPortDisplay;
        private set => SetProperty(ref _usbIntelligenceBestKnownPortDisplay, value);
    }

    public string UsbIntelligenceBenchmarkAgeDisplay
    {
        get => _usbIntelligenceBenchmarkAgeDisplay;
        private set => SetProperty(ref _usbIntelligenceBenchmarkAgeDisplay, value);
    }

    public string UsbIntelligenceRunBenchmarkHintDisplay
    {
        get => _usbIntelligenceRunBenchmarkHintDisplay;
        private set => SetProperty(ref _usbIntelligenceRunBenchmarkHintDisplay, value);
    }

    public string UsbMappingWorkflowStatus
    {
        get => _usbMappingWorkflowStatus;
        set => SetProperty(ref _usbMappingWorkflowStatus, value);
    }

    public string UsbMappingLabelDraft
    {
        get => _usbMappingLabelDraft;
        set
        {
            if (SetProperty(ref _usbMappingLabelDraft, value))
            {
                SaveUsbMappingLabelCommand.RaiseCanExecuteChanged();
            }
        }
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

    public string SystemIntelligenceStaleBannerText
    {
        get => _systemIntelligenceStaleBannerText;
        private set => SetProperty(ref _systemIntelligenceStaleBannerText, value);
    }

    public string SystemIntelligenceAutomationLineText
    {
        get => _systemIntelligenceAutomationLineText;
        private set => SetProperty(ref _systemIntelligenceAutomationLineText, value);
    }

    public string SystemIntelligenceWarningReasonText
    {
        get => _systemIntelligenceWarningReasonText;
        private set => SetProperty(ref _systemIntelligenceWarningReasonText, value);
    }

    public string SystemIntelligenceScanStatusText
    {
        get => _systemIntelligenceScanStatusText;
        private set => SetProperty(ref _systemIntelligenceScanStatusText, value);
    }

    public string SystemIntelligenceHealthStatusText
    {
        get => _systemIntelligenceHealthStatusText;
        private set => SetProperty(ref _systemIntelligenceHealthStatusText, value);
    }

    public string SystemIntelligenceWindowsReadinessText
    {
        get => _systemIntelligenceWindowsReadinessText;
        private set => SetProperty(ref _systemIntelligenceWindowsReadinessText, value);
    }

    public string SystemIntelligenceNetworkTechnicalDetailsText
    {
        get => _systemIntelligenceNetworkTechnicalDetailsText;
        private set => SetProperty(ref _systemIntelligenceNetworkTechnicalDetailsText, value);
    }

    public string SystemIntelligenceReportSafePathText
    {
        get => _systemIntelligenceReportSafePathText;
        private set => SetProperty(ref _systemIntelligenceReportSafePathText, value);
    }

    public bool SystemIntelligenceShowNetworkTechnicalDetails
    {
        get => _systemIntelligenceShowNetworkTechnicalDetails;
        set
        {
            if (SetProperty(ref _systemIntelligenceShowNetworkTechnicalDetails, value))
            {
                OnPropertyChanged(nameof(SystemIntelligenceNetworkTechnicalDetailsVisibility));
            }
        }
    }

    public Visibility SystemIntelligenceNetworkTechnicalDetailsVisibility =>
        SystemIntelligenceShowNetworkTechnicalDetails ? Visibility.Visible : Visibility.Collapsed;

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

    public string SystemIntelligenceDeviceFitCardText
    {
        get => _systemIntelligenceDeviceFitCardText;
        private set => SetProperty(ref _systemIntelligenceDeviceFitCardText, value);
    }

    public string SystemIntelligenceHardwareXrayCardText
    {
        get => _systemIntelligenceHardwareXrayCardText;
        private set => SetProperty(ref _systemIntelligenceHardwareXrayCardText, value);
    }

    public string SystemIntelligenceScanModeHintText
    {
        get => _systemIntelligenceScanModeHintText;
        private set => SetProperty(ref _systemIntelligenceScanModeHintText, value);
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
        "Update source: GitHub Releases on Forger-Digital-Solutions/ForgerEMS. Latest release is chosen by GitHub release publish date, then assets are inspected.";

    public string AppUpdateSettingsChannelLine =>
        IncludeBetaRcChannels
            ? "Channel: Beta / RC allowed (prerelease GitHub releases are included)."
            : "Channel: Stable only (prerelease releases are ignored).";
#pragma warning restore CA1822

    public string AppUpdateIncludePrereleasesValueText => IncludeBetaRcChannels ? "true" : "false";

    public string AppUpdateMachineStateDisplay => UpdateCheckMachineStateResolver.Describe(_appUpdateMachineState);

    public string AppUpdateLatestReleaseTagDisplay =>
        _lastAppliedUpdateCheckResult switch
        {
            null => "—",
            { SelectedReleaseTagRaw: var t } when !string.IsNullOrWhiteSpace(t) => t,
            { LatestVersionLabel: var l } when !string.IsNullOrWhiteSpace(l) => l,
            _ => "—"
        };

    public string AppUpdateLatestPublishedDisplay =>
        _lastAppliedUpdateCheckResult?.SelectedReleasePublishedAt is { } u
            ? u.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
            : "—";

    public string AppUpdateAssetFoundDisplay =>
        _lastAppliedUpdateCheckResult switch
        {
            null => "—",
            { Succeeded: true } r => r.SuitablePrimaryAssetFound ? "Found (HTTPS ZIP or ForgerEMS EXE)" : "Missing",
            _ => "—"
        };

    /// <summary>User-facing failure hint without raw HTTP bodies (those stay in Diagnostics when logged).</summary>
    public string AppUpdateSafeFailureReasonDisplay =>
        _lastAppliedUpdateCheckResult switch
        {
            null => "—",
            { Succeeded: true, Outcome: UpdateCheckOutcome.NoSuitableAssets } r =>
                string.IsNullOrWhiteSpace(r.ErrorMessage) ? "No downloadable assets on the latest release." : r.ErrorMessage!,
            { Succeeded: true } => "—",
            { ErrorMessage: { Length: > 0 } m } => m,
            { FailureKind: var k } => $"Failure: {k}"
        };

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

    public Visibility AppUpdateAdvancedDownloadButtonVisibility
    {
        get => _appUpdateAdvancedDownloadButtonVisibility;
        private set => SetProperty(ref _appUpdateAdvancedDownloadButtonVisibility, value);
    }

    public Visibility AppUpdateCopyZipLinkVisibility
    {
        get => _appUpdateCopyZipLinkVisibility;
        private set => SetProperty(ref _appUpdateCopyZipLinkVisibility, value);
    }

    public Visibility AppUpdateCopyChecksumInstructionsVisibility
    {
        get => _appUpdateCopyChecksumInstructionsVisibility;
        private set => SetProperty(ref _appUpdateCopyChecksumInstructionsVisibility, value);
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

    public bool IncludeBetaRcChannels
    {
        get => _appUpdateSettings.IncludeBetaRcChannels;
        set
        {
            if (_appUpdateSettings.IncludeBetaRcChannels == value)
            {
                return;
            }

            _appUpdateSettings.IncludeBetaRcChannels = value;
            SaveUpdateSettings();
            OnPropertyChanged(nameof(IncludeBetaRcChannels));
            OnPropertyChanged(nameof(AppUpdateSettingsChannelLine));
            OnPropertyChanged(nameof(AppUpdateIncludePrereleasesValueText));
        }
    }

    private void RefreshDeepSensorModeSettingsProperties()
    {
        var resolution = ForgerEmsEnvironmentConfiguration.DeepSensorModeResolution;
        var selectedIndex = resolution.Mode.Equals(DeepSensorModeValues.ReadOnly, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _isLoadingDeepSensorModeSetting = true;
        try
        {
            _deepSensorModeSelectedIndex = selectedIndex;
            OnPropertyChanged(nameof(DeepSensorModeSelectedIndex));
        }
        finally
        {
            _isLoadingDeepSensorModeSetting = false;
        }

        DeepSensorModeSourceSummary =
            $"Current source: {resolution.DisplaySource}. Resolved value: {resolution.Mode}. " +
            (resolution.IsInvalid ? "Invalid configured value was ignored; using Off." : resolution.TechnicianNote);
        DeepSensorModeConsentNotice =
            "Read-only local sensors may reveal CPU/GPU/storage temperatures, clocks, loads, fan RPM, and storage wear when supported. " +
            "Some sensors require admin access, vendor drivers, or firmware support. No fan, voltage, clock, BIOS, firmware, cloud, or telemetry control is used.";
        OnPropertyChanged(nameof(DeepSensorModeSettingsSummary));
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

    /// <summary>When false (beta default), Kyra Advanced hides BYOK/session key editing and treats providers as operator-configured.</summary>
    public bool KyraDeveloperManagedProviderUi =>
        _copilotSettings.ProviderConfigurationMode == KyraProviderConfigurationMode.DeveloperManaged;

    public bool KyraTesterEditableProviders =>
        _copilotSettings.ProviderConfigurationMode == KyraProviderConfigurationMode.UserManagedFuture;

    /// <summary>Expose raw session API key fields only for tester mode or when FORGEREMS_DEV_PROVIDER_SETTINGS=1.</summary>
    public bool KyraShowDeveloperProviderPlumbing =>
        KyraTesterEditableProviders ||
        string.Equals(Environment.GetEnvironmentVariable("FORGEREMS_DEV_PROVIDER_SETTINGS"), "1", StringComparison.OrdinalIgnoreCase);

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

    public bool KyraLocalRepairMemoryEnabled
    {
        get => _copilotSettings.KyraLocalRepairMemoryEnabled;
        set
        {
            if (_copilotSettings.KyraLocalRepairMemoryEnabled != value)
            {
                _copilotSettings.KyraLocalRepairMemoryEnabled = value;
                OnPropertyChanged();
                SaveCopilotSettings();
            }
        }
    }

    public bool KyraCommunitySharingEnabled
    {
        get => _copilotSettings.KyraCommunitySharingEnabled;
        set
        {
            if (_copilotSettings.KyraCommunitySharingEnabled != value)
            {
                _copilotSettings.KyraCommunitySharingEnabled = value;
                if (!value)
                {
                    _copilotSettings.KyraShareResolvedIssueFixPatterns = false;
                    _copilotSettings.KyraShareHardwareCompatibilityPerformancePatterns = false;
                    _copilotSettings.KyraShareCrashErrorDiagnostics = false;
                    OnPropertyChanged(nameof(KyraShareResolvedIssueFixPatterns));
                    OnPropertyChanged(nameof(KyraShareHardwareCompatibilityPerformancePatterns));
                    OnPropertyChanged(nameof(KyraShareCrashErrorDiagnostics));
                }

                OnPropertyChanged();
                SaveCopilotSettings();
            }
        }
    }

    public bool KyraShareResolvedIssueFixPatterns
    {
        get => _copilotSettings.KyraShareResolvedIssueFixPatterns;
        set
        {
            if (value && !_copilotSettings.KyraCommunitySharingEnabled)
            {
                _copilotSettings.KyraCommunitySharingEnabled = true;
                OnPropertyChanged(nameof(KyraCommunitySharingEnabled));
            }

            var effective = value && _copilotSettings.KyraCommunitySharingEnabled;
            if (_copilotSettings.KyraShareResolvedIssueFixPatterns == effective)
            {
                return;
            }

            _copilotSettings.KyraShareResolvedIssueFixPatterns = effective;
            OnPropertyChanged();
            SaveCopilotSettings();
        }
    }

    public bool KyraShareHardwareCompatibilityPerformancePatterns
    {
        get => _copilotSettings.KyraShareHardwareCompatibilityPerformancePatterns;
        set
        {
            if (value && !_copilotSettings.KyraCommunitySharingEnabled)
            {
                _copilotSettings.KyraCommunitySharingEnabled = true;
                OnPropertyChanged(nameof(KyraCommunitySharingEnabled));
            }

            var effective = value && _copilotSettings.KyraCommunitySharingEnabled;
            if (_copilotSettings.KyraShareHardwareCompatibilityPerformancePatterns == effective)
            {
                return;
            }

            _copilotSettings.KyraShareHardwareCompatibilityPerformancePatterns = effective;
            OnPropertyChanged();
            SaveCopilotSettings();
        }
    }

    public bool KyraShareCrashErrorDiagnostics
    {
        get => _copilotSettings.KyraShareCrashErrorDiagnostics;
        set
        {
            if (value && !_copilotSettings.KyraCommunitySharingEnabled)
            {
                _copilotSettings.KyraCommunitySharingEnabled = true;
                OnPropertyChanged(nameof(KyraCommunitySharingEnabled));
            }

            var effective = value && _copilotSettings.KyraCommunitySharingEnabled;
            if (_copilotSettings.KyraShareCrashErrorDiagnostics == effective)
            {
                return;
            }

            _copilotSettings.KyraShareCrashErrorDiagnostics = effective;
            OnPropertyChanged();
            SaveCopilotSettings();
        }
    }

    public string KyraGatewayProviderStatusSummary
    {
        get => _kyraGatewayProviderStatusSummary;
        private set => SetProperty(ref _kyraGatewayProviderStatusSummary, value);
    }

    public bool KyraRealtimeGatewayEnabled
    {
        get => _copilotSettings.KyraRealtimeGatewayEnabled;
        set
        {
            if (_copilotSettings.KyraRealtimeGatewayEnabled != value)
            {
                _copilotSettings.KyraRealtimeGatewayEnabled = value;
                OnPropertyChanged();
                SaveCopilotSettings();
            }
        }
    }

    public bool KyraRealtimeGatewayResearchEnabled
    {
        get => _copilotSettings.KyraRealtimeGatewayResearchEnabled;
        set
        {
            if (_copilotSettings.KyraRealtimeGatewayResearchEnabled != value)
            {
                _copilotSettings.KyraRealtimeGatewayResearchEnabled = value;
                OnPropertyChanged();
                SaveCopilotSettings();
            }
        }
    }

    public bool KyraRealtimeGatewayResearchConsent
    {
        get => _copilotSettings.KyraRealtimeGatewayResearchConsent;
        set
        {
            if (_copilotSettings.KyraRealtimeGatewayResearchConsent != value)
            {
                _copilotSettings.KyraRealtimeGatewayResearchConsent = value;
                OnPropertyChanged();
                SaveCopilotSettings();
            }
        }
    }

    public bool KyraUseSanitizedSystemIntelligenceContext
    {
        get => _copilotSettings.KyraUseSanitizedSystemIntelligenceContext;
        set
        {
            if (_copilotSettings.KyraUseSanitizedSystemIntelligenceContext != value)
            {
                _copilotSettings.KyraUseSanitizedSystemIntelligenceContext = value;
                OnPropertyChanged();
                SaveCopilotSettings();
            }
        }
    }

    public bool BetaWelcomeKyraShareRepairIntelligence
    {
        get => _betaWelcomeKyraShareRepair;
        set => SetProperty(ref _betaWelcomeKyraShareRepair, value);
    }

    public bool BetaWelcomeKyraShareHardwarePatterns
    {
        get => _betaWelcomeKyraShareHardware;
        set => SetProperty(ref _betaWelcomeKyraShareHardware, value);
    }

    public bool BetaWelcomeKyraShareResolvedCategories
    {
        get => _betaWelcomeKyraShareResolved;
        set => SetProperty(ref _betaWelcomeKyraShareResolved, value);
    }

    public bool BetaWelcomeKyraShareCrashDiagnostics
    {
        get => _betaWelcomeKyraShareCrash;
        set => SetProperty(ref _betaWelcomeKyraShareCrash, value);
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
                CheckKyraGatewayStatusCommand.RaiseCanExecuteChanged();
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

    public string UnifiedDiagnosticsSummaryText
    {
        get => _unifiedDiagnosticsSummaryText;
        private set => SetProperty(ref _unifiedDiagnosticsSummaryText, value);
    }

    public string DiagnosticsHealthChecklistText
    {
        get => _diagnosticsHealthChecklistText;
        private set => SetProperty(ref _diagnosticsHealthChecklistText, value);
    }

    public string DiagnosticsWarningReasonText
    {
        get => _diagnosticsWarningReasonText;
        private set => SetProperty(ref _diagnosticsWarningReasonText, value);
    }

    public string DiagnosticsAppActionStatusText
    {
        get => _diagnosticsAppActionStatusText;
        private set => SetProperty(ref _diagnosticsAppActionStatusText, value);
    }

    public string DiagnosticsHealthStatusText
    {
        get => _diagnosticsHealthStatusText;
        private set => SetProperty(ref _diagnosticsHealthStatusText, value);
    }

    public string DiagnosticsBackendChipText
    {
        get => _diagnosticsBackendChipText;
        private set => SetProperty(ref _diagnosticsBackendChipText, value);
    }

    public string DiagnosticsUsbChipText
    {
        get => _diagnosticsUsbChipText;
        private set => SetProperty(ref _diagnosticsUsbChipText, value);
    }

    public string DiagnosticsSystemChipText
    {
        get => _diagnosticsSystemChipText;
        private set => SetProperty(ref _diagnosticsSystemChipText, value);
    }

    public string DiagnosticsToolkitChipText
    {
        get => _diagnosticsToolkitChipText;
        private set => SetProperty(ref _diagnosticsToolkitChipText, value);
    }

    public string DiagnosticsKyraChipText
    {
        get => _diagnosticsKyraChipText;
        private set => SetProperty(ref _diagnosticsKyraChipText, value);
    }

    public string DiagnosticsUpdateChipText
    {
        get => _diagnosticsUpdateChipText;
        private set => SetProperty(ref _diagnosticsUpdateChipText, value);
    }

    public bool DiagnosticsShowFullDetail
    {
        get => _diagnosticsShowFullDetail;
        set
        {
            if (SetProperty(ref _diagnosticsShowFullDetail, value))
            {
                ApplyDiagnosticsFromDisk();
            }
        }
    }

    public string DiagnosticsLogSearchText
    {
        get => _diagnosticsLogSearchText;
        set
        {
            if (SetProperty(ref _diagnosticsLogSearchText, value))
            {
                RefreshLogsText();
            }
        }
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

    public string LastCommandNameText => LastCommandText.Split("->", 2, StringSplitOptions.TrimEntries)[0];

    public string LastCommandToolText => LastCommandText.Contains("->", StringComparison.Ordinal)
        ? LastCommandText.Split("->", 2, StringSplitOptions.TrimEntries)[1]
        : LastCommandText;

    public string LastCommandStatusText
    {
        get => _lastCommandStatusText;
        private set => SetProperty(ref _lastCommandStatusText, value);
    }

    public string LastCommandStartedText => _lastCommandStartedAt.HasValue
        ? _lastCommandStartedAt.Value.ToLocalTime().ToString("g")
        : "n/a";

    public string LastCommandFinishedText => _lastCommandFinishedAt.HasValue
        ? _lastCommandFinishedAt.Value.ToLocalTime().ToString("g")
        : "n/a";

    public string LastCommandDurationText =>
        _lastCommandStartedAt.HasValue && _lastCommandFinishedAt.HasValue
            ? $"{Math.Max(0, (_lastCommandFinishedAt.Value - _lastCommandStartedAt.Value).TotalSeconds):0.#}s"
            : "n/a";

    public string LastCommandExitCodeText => _lastCommandExitCode.HasValue ? _lastCommandExitCode.Value.ToString(CultureInfo.InvariantCulture) : "n/a";

    public string LastCommandSummaryText
    {
        get => _lastCommandSummaryText;
        private set => SetProperty(ref _lastCommandSummaryText, value);
    }

    public string WindowsSandboxStatusText
    {
        get => _windowsSandboxStatusText;
        private set => SetProperty(ref _windowsSandboxStatusText, value);
    }

    public string SafeTestingEnvironmentSummaryText
    {
        get => _safeTestingEnvironmentSummaryText;
        private set => SetProperty(ref _safeTestingEnvironmentSummaryText, value);
    }

    public Visibility DiagnosticsEmbeddedWslRunnerContentVisibility =>
        DiagnosticsFeatureFlags.EmbeddedWslCommandRunnerEnabled ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DiagnosticsEmbeddedWslDisabledBannerVisibility =>
        DiagnosticsFeatureFlags.EmbeddedWslCommandRunnerEnabled ? Visibility.Collapsed : Visibility.Visible;

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

    public string ToolkitClassificationSummaryText
    {
        get => _toolkitClassificationSummaryText;
        private set => SetProperty(ref _toolkitClassificationSummaryText, value);
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
                OnPropertyChanged(nameof(SelectedToolkitExpectedFullPath));
                OnPropertyChanged(nameof(SelectedToolkitDetectedFullPath));
                RecheckSelectedToolCommand.RaiseCanExecuteChanged();
                OpenSelectedToolLocationCommand.RaiseCanExecuteChanged();
                OpenManualDownloadShortcutCommand.RaiseCanExecuteChanged();
                CopySelectedToolkitExpectedPathCommand.RaiseCanExecuteChanged();
                CopySelectedToolkitDetectedPathCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SelectedToolkitDetailText => SelectedToolkitHealthItem?.DetailText ?? "Select a toolkit item to see its status, expected path, and next step.";

    public string SelectedToolkitExpectedFullPath
    {
        get
        {
            var item = SelectedToolkitHealthItem;
            if (item is null)
            {
                return "Not selected";
            }

            return string.IsNullOrWhiteSpace(item.ResolvedExpectedPath) ? item.ExpectedPath : item.ResolvedExpectedPath;
        }
    }

    public string SelectedToolkitDetectedFullPath =>
        string.IsNullOrWhiteSpace(SelectedToolkitHealthItem?.MatchedPath) ? "Not detected" : SelectedToolkitHealthItem!.MatchedPath;

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
        HydrateFromCachedReportsEarly();
        await RefreshAllAsync();
        StartUsbAutoDetectionMonitor();
    }

    private void HydrateFromCachedReportsEarly()
    {
        try
        {
            LoadSystemIntelligenceReport();
            LoadToolkitHealthReport();
            ApplyDiagnosticsFromDisk();
            RefreshUsbIntelligenceFromDisk();
        }
        catch (Exception ex)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Could not load cached reports: {ex.Message}", LogSeverity.Warning));
        }
    }

    private static bool UsbRootPathsEqual(string? a, string? b) =>
        string.Equals(
            string.IsNullOrWhiteSpace(a) ? string.Empty : a.TrimEnd('\\'),
            string.IsNullOrWhiteSpace(b) ? string.Empty : b.TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);

    private bool IsManualUsbBenchmarkActive() =>
        _manualUsbBenchmarkCts is { Token.IsCancellationRequested: false };

    private bool IsAnyUsbBenchmarkActive() =>
        _manualUsbBenchmarkCts is { Token.IsCancellationRequested: false } ||
        _autoUsbBenchmarkCts is { Token.IsCancellationRequested: false };

    private void CancelManualUsbBenchmarkCtsOnly()
    {
        try
        {
            _manualUsbBenchmarkCts?.Cancel();
        }
        catch
        {
            // ignored
        }

        try
        {
            _manualUsbBenchmarkCts?.Dispose();
        }
        catch
        {
            // ignored
        }

        _manualUsbBenchmarkCts = null;
    }

    private void CancelAutoUsbBenchmarkCtsOnly()
    {
        try
        {
            _autoUsbBenchmarkCts?.Cancel();
        }
        catch
        {
            // ignored
        }

        try
        {
            _autoUsbBenchmarkCts?.Dispose();
        }
        catch
        {
            // ignored
        }

        _autoUsbBenchmarkCts = null;
    }

    private CancellationTokenSource CreateFreshUsbBenchmarkCts(bool isAutomatic, string targetRootPath)
    {
        var cts = new CancellationTokenSource();
        if (cts.Token.IsCancellationRequested)
        {
            AppendLog(new LogLine(
                DateTimeOffset.Now,
                $"[WARN] USB benchmark CTS was pre-cancelled before start for {targetRootPath}; replacing it.",
                LogSeverity.Warning,
                channel: LiveLogChannel.Diagnostics));
            cts.Dispose();
            cts = new CancellationTokenSource();
        }

        if (isAutomatic)
        {
            _autoUsbBenchmarkCts = cts;
        }
        else
        {
            _manualUsbBenchmarkCts = cts;
        }

        return cts;
    }

    private void CancelUsbBenchmarksForSelectionChange()
    {
        _usbBenchmarkHostInterruptKind = UsbBenchmarkHostInterruptKind.SelectionChanged;
        CancelManualUsbBenchmarkCtsOnly();
        CancelAutoUsbBenchmarkCtsOnly();
    }

    private bool CanRunUsbIntelligenceBenchmark()
    {
        if (SelectedUsbTarget is null)
        {
            return false;
        }

        if (IsAnyUsbBenchmarkActive())
        {
            return false;
        }

        if (string.Equals(SelectedUsbTarget.BenchmarkStatus, "Testing", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return UsbTargetSafety.IsSafeForBenchmark(SelectedUsbTarget, out _);
    }

    private void CancelActiveUsbBenchmark()
    {
        _usbBenchmarkHostInterruptKind = UsbBenchmarkHostInterruptKind.UserRequested;
        var cancelled = false;
        try
        {
            if (_manualUsbBenchmarkCts is { Token.IsCancellationRequested: false })
            {
                _manualUsbBenchmarkCts.Cancel();
                cancelled = true;
            }
            else if (_autoUsbBenchmarkCts is { Token.IsCancellationRequested: false })
            {
                _autoUsbBenchmarkCts.Cancel();
                cancelled = true;
            }
        }
        catch (ObjectDisposedException)
        {
            // The run finished while the cancel button was being processed.
        }

        AppendLog(new LogLine(
            DateTimeOffset.Now,
            cancelled ? "[INFO] Benchmark cancel requested." : "[INFO] Benchmark cancel ignored: no benchmark is running.",
            LogSeverity.Info));
        RaiseCommandStates();
    }

    private void CancelScheduledAutomaticUsbBenchmark()
    {
        try
        {
            _autoUsbBenchmarkDebounceCts?.Cancel();
        }
        catch
        {
            // ignore
        }

        _autoUsbBenchmarkDebounceCts?.Dispose();
        _autoUsbBenchmarkDebounceCts = null;
    }

    private void ScheduleAutomaticUsbBenchmark()
    {
        var scheduledPath = SelectedUsbTarget?.RootPath;
        CancelScheduledAutomaticUsbBenchmark();
        _autoUsbBenchmarkDebounceCts = new CancellationTokenSource();
        var token = _autoUsbBenchmarkDebounceCts.Token;
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await Task.Delay(6500, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher is null || dispatcher.HasShutdownStarted)
                {
                    return;
                }

                await dispatcher.InvokeAsync(
                    () =>
                    {
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        if (SelectedUsbTarget is null || string.IsNullOrWhiteSpace(scheduledPath))
                        {
                            return;
                        }

                        if (!string.Equals(SelectedUsbTarget.RootPath, scheduledPath, StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }

                        _ = AutoBenchmarkSelectedUsbSafeAsync(isAutomatic: true);
                    },
                    DispatcherPriority.Background);
            },
            token);
    }

    private void EnsureUsbIntelligenceDebounceTimer()
    {
        if (_usbIntelligenceDebounceTimer is not null)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _usbIntelligenceDebounceTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(420)
        };
        _usbIntelligenceDebounceTimer.Tick += (_, _) =>
        {
            _usbIntelligenceDebounceTimer.Stop();
            _autoIntelligenceOrchestrator.ScheduleUsbSelectionRefresh(_backendContext, SelectedUsbTarget);
        };
    }

    private void ScheduleDebouncedUsbIntelligenceRefresh()
    {
        EnsureUsbIntelligenceDebounceTimer();
        if (_usbIntelligenceDebounceTimer is null)
        {
            _autoIntelligenceOrchestrator.ScheduleUsbSelectionRefresh(_backendContext, SelectedUsbTarget);
            return;
        }

        _usbIntelligenceDebounceTimer.Stop();
        _usbIntelligenceDebounceTimer.Start();
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

    private async Task<IDisposable> EnterUsbBuilderActionGateAsync(string actionLabel)
    {
        if (_usbBuilderActionGate.CurrentCount == 0)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, $"[INFO] USB Builder is busy: {actionLabel.ToLowerInvariant()} queued until current USB action completes.", LogSeverity.Info));
        }

        await _usbBuilderActionGate.WaitAsync().ConfigureAwait(true);
        if (_autoUsbBenchmarkCts is { Token.IsCancellationRequested: false })
        {
            _usbBenchmarkHostInterruptKind = UsbBenchmarkHostInterruptKind.SelectionChanged;
            _autoUsbBenchmarkCts.Cancel();
            AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] Auto benchmark paused while USB Builder action is running.", LogSeverity.Info));
        }

        SetStatus(
            $"USB Builder is busy: {actionLabel.ToLowerInvariant()}…",
            "Conflicting USB actions are serialized. Navigation and full beta logging stay available.",
            RunningBackground,
            RunningBorder,
            RunningForeground);
        return new Releaser(_usbBuilderActionGate);
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
        ApplyDiagnosticsFromDisk();
        RefreshUsbIntelligenceFromDisk();

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

        var orchestrationVersion = Interlocked.Increment(ref _deferredOrchestrationVersion);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(450).ConfigureAwait(false);
                if (Volatile.Read(ref _deferredOrchestrationVersion) != orchestrationVersion)
                {
                    return;
                }

                _autoIntelligenceOrchestrator.ScheduleManualIntelligenceRefresh(_backendContext);
            }
            catch
            {
                // ignored
            }
        });
    }

    private async Task RefreshUsbTargetsAsync()
    {
        if (_refreshingUsbTargets)
        {
            return;
        }

        _refreshingUsbTargets = true;
        var previousSelection = SelectedUsbTarget?.RootPath;
        var phaseTimer = Stopwatch.StartNew();
        long enumerateMs = 0;
        long uiApplyMs = 0;
        try
        {
            var detectionResult = await _usbDetectionService.GetUsbTargetsAsync();
            enumerateMs = phaseTimer.ElapsedMilliseconds;
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
                UsbPortLabelResolver.MarkDriveRemoved(previousSelection);
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
            uiApplyMs = phaseTimer.ElapsedMilliseconds;
            AppendLog(new LogLine(
                DateTimeOffset.Now,
                $"[INFO] Refresh USB targets timing: enumerate drives={enumerateMs}ms, ui update={Math.Max(0, uiApplyMs - enumerateMs)}ms",
                LogSeverity.Info,
                channel: LiveLogChannel.Diagnostics));
            ScheduleAutomaticUsbBenchmark();
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

    private bool CanRetryFailedManagedDownloads() =>
        !IsBusy &&
        _backendContext.IsAvailable &&
        SelectedUsbTarget is not null &&
        UsbTargetSafety.IsSafeForBenchmark(SelectedUsbTarget, out _) &&
        ManagedDownloadRetryPanelVisibility == Visibility.Visible;

    private async Task RunRetryFailedManagedDownloadsAsync()
    {
        if (!TryGetValidatedSelectedTarget("Retry failed managed downloads", out var selectedUsbTarget))
        {
            return;
        }

        if (!ConfirmTargetedAction(
                "Retry failed managed downloads",
                selectedUsbTarget,
                "This retries only items marked retryable in ForgerEMS-managed-download-result.json on the USB root. Already staged files are left intact."))
        {
            return;
        }

        var arguments = new System.Collections.Generic.List<string>
        {
            "-UsbRoot",
            selectedUsbTarget.RootPath,
            "-RetryFailedManagedDownloads"
        };

        await RunScriptAsync(
            ScriptActionType.UpdateUsb,
            new PowerShellRunRequest
            {
                DisplayName = "Retry failed managed downloads",
                WorkingDirectory = _backendContext.WorkingDirectory,
                ScriptPath = _backendContext.UpdateScriptPath,
                Arguments = arguments,
                ProgressItemName = "managed download retry"
            });
    }

    private void RefreshManagedDownloadRunArtifactFromSelectedUsb()
    {
        ManagedDownloadFailedRows.Clear();
        var artifact = ManagedDownloadRunArtifact.TryLoadFromUsbRoot(SelectedUsbTarget?.RootPath);
        if (artifact is null)
        {
            ManagedDownloadPartialBannerText = string.Empty;
            ManagedDownloadRetryPanelVisibility = Visibility.Collapsed;
            RetryFailedManagedDownloadsCommand.RaiseCanExecuteChanged();
            return;
        }

        ManagedDownloadPartialBannerText = string.Equals(artifact.Readiness, "PARTIALLY_STAGED", StringComparison.OrdinalIgnoreCase)
            ? "USB is usable, but one or more managed items need attention. Fallback shortcuts may exist — use Retry Failed Downloads when available."
            : string.Equals(artifact.Readiness, "READY", StringComparison.OrdinalIgnoreCase)
                ? "USB is READY."
                : $"Managed download readiness: {artifact.Readiness}";

        foreach (var row in artifact.FailedItems)
        {
            ManagedDownloadFailedRows.Add(row);
        }

        var showRetry = string.Equals(artifact.Readiness, "PARTIALLY_STAGED", StringComparison.OrdinalIgnoreCase) &&
                        artifact.HasRetryableFailures;
        ManagedDownloadRetryPanelVisibility = showRetry ? Visibility.Visible : Visibility.Collapsed;
        RetryFailedManagedDownloadsCommand.RaiseCanExecuteChanged();
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
        TryRecordKyraSystemScanLearning();
    }

    private async Task RunElevatedSystemScanAsync()
    {
        var scriptPath = ResolveBackendScriptPath(Path.Combine("SystemIntelligence", "Invoke-ForgerEMSSystemScan.ps1"));
        if (!File.Exists(scriptPath))
        {
            SetStatus(
                "Elevated scan unavailable",
                $"System Intelligence script was not found: {scriptPath}",
                ErrorBackground,
                ErrorBorder,
                ErrorForeground);
            return;
        }

        using var gate = await EnterUsbBuilderActionGateAsync("Running elevated scan").ConfigureAwait(true);
        ClearLogs();
        LastCommandText = "System Intelligence elevated scan";
        AppendLifecycleStart("System Intelligence elevated scan", null);
        AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] Elevated scan: unlocks additional hardware/security detail where Windows requires admin permission.", LogSeverity.Info));
        AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] Standard scan remains safe and useful; elevated scan does not bypass Windows security.", LogSeverity.Info));

        var reportsDir = Path.Combine(_appRuntimeService.RuntimeRoot, "reports");
        Directory.CreateDirectory(reportsDir);
        var launchCommand = $$"""
            $ErrorActionPreference = 'Stop'
            $ps = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
            if (-not (Test-Path -LiteralPath $ps)) { $ps = 'powershell.exe' }
            $script = {{ToSingleQuotedPowerShellLiteral(scriptPath)}}
            $outDir = {{ToSingleQuotedPowerShellLiteral(reportsDir)}}
            $args = @('-NoProfile','-ExecutionPolicy','Bypass','-File',$script,'-OutputDirectory',$outDir)
            $p = Start-Process -FilePath $ps -ArgumentList $args -Verb RunAs -Wait -PassThru
            if ($null -eq $p) { throw 'Elevation was cancelled before the scan started.' }
            if ($p.ExitCode -ne 0) { throw ('Elevated scan exited with code ' + $p.ExitCode + '.') }
            Write-Host '[OK] Elevated System Intelligence scan completed.'
            """;

        var result = await RunScriptAsync(
            ScriptActionType.SystemIntelligence,
            new PowerShellRunRequest
            {
                DisplayName = "System Intelligence elevated scan",
                WorkingDirectory = _backendContext.WorkingDirectory,
                InlineCommand = launchCommand,
                HeartbeatKind = PowerShellHeartbeatKind.LongRunningScan
            });

        if (result is not null)
        {
            LoadSystemIntelligenceReport();
            TryRecordKyraSystemScanLearning();
        }
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

    private void OpenSystemJsonReport()
    {
        OpenPathIfExists(GetSystemIntelligenceJsonPath(), "System Intelligence JSON report");
    }

    private void OpenSystemMarkdownReport()
    {
        OpenPathIfExists(GetSystemIntelligenceMarkdownPath(), "System Intelligence Markdown report");
    }

    private void CopySystemReportSafePath()
    {
        try
        {
            Clipboard.SetText(SystemIntelligenceReportSafePathText);
            AppendLog(new LogLine(DateTimeOffset.Now, "[OK] Copied safe report path.", LogSeverity.Success));
        }
        catch (Exception exception)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Could not copy safe report path: {exception.Message}", LogSeverity.Warning));
        }
    }

    private void CopySystemSummary()
    {
        var reportPath = Path.Combine(GetRuntimeReportsDirectory(), "system-intelligence-latest.json");
        string summary;
        try
        {
            if (File.Exists(reportPath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
                summary = SystemIntelligenceQuickReadBuilder.Build(document.RootElement);
            }
            else
            {
                summary = "ForgerEMS System Intelligence — Quick Read" + Environment.NewLine +
                          "Machine: Run System Intelligence first" + Environment.NewLine +
                          "Health: Unknown | Scan Confidence: Low" + Environment.NewLine +
                          "Best Use: Unknown until scan completes" + Environment.NewLine +
                          "Flip Value: Unknown | Basis: scan not available" + Environment.NewLine +
                          "Key Strengths: scan not available" + Environment.NewLine +
                          "Watch-outs: no current report loaded" + Environment.NewLine +
                          "Next Action: Run System Scan, then copy the quick read again.";
            }
        }
        catch
        {
            summary = "ForgerEMS System Intelligence — Quick Read" + Environment.NewLine +
                      "Machine: Report parse failed" + Environment.NewLine +
                      "Health: Unknown | Scan Confidence: Low" + Environment.NewLine +
                      "Best Use: Unknown until report is regenerated" + Environment.NewLine +
                      "Flip Value: Unknown | Basis: report parse failed" + Environment.NewLine +
                      "Key Strengths: unavailable" + Environment.NewLine +
                      "Watch-outs: saved report could not be parsed" + Environment.NewLine +
                      "Next Action: Rerun System Scan, then copy the quick read again.";
        }

        summary += Environment.NewLine +
                   "Review before sharing. Reports may include hardware, network adapter, USB device, and diagnostic details. Do not send passwords, product keys, API keys, tokens, private documents, or sensitive files.";

        Clipboard.SetText(SensitiveDataRedactor.SanitizeForSupportShare(summary));
        AppendLog(new LogLine(DateTimeOffset.Now, "[OK] Safe summary copied to clipboard (sanitized for sharing).", LogSeverity.Success));
    }

    private string GetSystemIntelligenceJsonPath() =>
        Path.Combine(GetRuntimeReportsDirectory(), "system-intelligence-latest.json");

    private string GetSystemIntelligenceMarkdownPath() =>
        Path.Combine(GetRuntimeReportsDirectory(), "flip-report-latest.md");

    private void OpenPathIfExists(string path, string displayName)
    {
        if (!File.Exists(path))
        {
            AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] {displayName} not found yet.", LogSeverity.Warning));
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Could not open {displayName}: {exception.Message}", LogSeverity.Warning));
        }
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

        // Flush Settings / Kyra Intelligence bindings into _copilotSettings before routing (avoids stale metadata).
        BuildCopilotSettingsFromUi();

        CopilotMessages.Add(new CopilotChatMessage
        {
            Role = "You",
            Text = userText
        });

        CopilotInput = string.Empty;
        KyraSlashPopupOpen = false;
        KyraSlashSuggestions.Clear();

        var routedIntent = KyraIntentRouter.DetectIntent(userText);
        var reportPath = Path.Combine(GetRuntimeReportsDirectory(), "system-intelligence-latest.json");
        var toolkitReportPath = Path.Combine(GetRuntimeReportsDirectory(), "toolkit-health-latest.json");
        CopilotResponse response;
        _copilotGenerationCancellation?.Dispose();
        _copilotGenerationCancellation = new CancellationTokenSource();
        IsCopilotGenerating = true;
        KyraActivityStatusText = KyraResponseComposer.KyraThinkingStatus;
        try
        {
            var parse = KyraSlashCommandParser.Parse(userText);
            if (parse.IsSlashCommand)
            {
                ReportKyraActivity("Reading command…");
                if (KyraLiveSlashCoordinator.IsLiveDataSlash(parse.MatchedCommand))
                {
                    ReportKyraActivity("Researching live data…");
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
                        response = new CopilotResponse
                        {
                            Text = liveResp.Text,
                            UsedOnlineData = liveResp.UsedOnlineData,
                            ProviderType = liveResp.ProviderType,
                            ProviderNotes = liveResp.ProviderNotes,
                            ResponseSource = liveResp.ResponseSource,
                            SourceLabel = liveResp.UsedOnlineData ? "Live research" : "Live tool unavailable",
                            OnlineStatus = "Live tool result",
                            FallbackUsed = liveResp.FallbackUsed,
                            OnlineEnhancementApplied = liveResp.OnlineEnhancementApplied,
                            GroundedInSystemIntelligence = liveResp.GroundedInSystemIntelligence,
                            ActionSuggestions = liveResp.ActionSuggestions
                        };
                        ReportKyraActivity("Done.");
                    }
                    else
                    {
                        response = new CopilotResponse
                        {
                            Text = "Live tool returned no text. Try `/provider`.",
                            ProviderType = CopilotProviderType.LocalOffline,
                            OnlineStatus = "Local live tool",
                            SourceLabel = "Live tool"
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
                            SourceLabel = "Command"
                        };
                        ReportKyraActivity("Done.");
                    }
                }
            }
            else
            {
                if (KyraInlineLivePromptRouter.TryBuildWeatherParse(userText, out var weatherParse))
                {
                    response = await ExecuteInlineLiveToolAsync(weatherParse, reportPath, toolkitReportPath);
                }
                else
                {
                    var appVer = GetType().Assembly.GetName().Version?.ToString() ?? "unknown";
                    var uiForResearch = BuildCopilotSettingsFromUi();
                    var researchResp = await KyraGatewayResearchCoordinator.TryRealtimeResearchAsync(
                        userText,
                        uiForResearch,
                        reportPath,
                        toolkitReportPath,
                        appVer,
                        client: null,
                        _copilotGenerationCancellation.Token);
                    if (researchResp is not null)
                    {
                        response = researchResp;
                    }
                    else if (TryBuildLiveToolParseForPrompt(userText, out var liveParse))
                    {
                        response = await ExecuteInlineLiveToolAsync(liveParse, reportPath, toolkitReportPath);
                    }
                    else
                    {
                        ReportKyraActivity(DescribeKyraLlmPhase(userText));
                        var req = CreateKyraCopilotRequest(userText, reportPath, toolkitReportPath);
                        response = await _copilotService.GenerateReplyAsync(req, _copilotGenerationCancellation.Token);
                    }
                }
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

        var localMemoryUsed = KyraLocalRepairMemoryWouldApply(userText);
        BuildCopilotSettingsFromUi();
        var showFixFeedback = KyraMemorySanitizer.ShouldOfferFixFeedback(routedIntent, userText, response.Text ?? string.Empty);
        CopilotMessages.Add(new CopilotChatMessage
        {
            Role = "Kyra",
            Text = FormatKyraResponseText(response),
            SourceLabel = response.SourceLabel,
            OnlineEnhancementApplied = response.OnlineEnhancementApplied,
            MetadataSummary = BuildKyraMetadataSummary(response, localMemoryUsed, _copilotSettings),
            MetadataDetails = BuildKyraMetadataDetails(response, localMemoryUsed, _copilotSettings),
            LearningUserPrompt = userText,
            LearningKyraResponsePlain = response.Text?.Trim() ?? string.Empty,
            LearningIntent = routedIntent,
            ShowTroubleshootingFeedback = showFixFeedback
        });

        ApplyCopilotOnlineIndicator(response);
        TryRecordKyraIntelligenceMemory(userText, response, reportPath, routedIntent);
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
        var t = (response.Text ?? string.Empty).TrimEnd();

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

    private static string BuildKyraMetadataSummary(
        CopilotResponse response,
        bool localMemoryUsed = false,
        CopilotSettings? sharingSettings = null)
    {
        var parts = new List<string>();
        var sourceLabel = response.SourceLabel ?? string.Empty;
        var communityChip = KyraCommunityMetadataFormatter.SummaryChip(sharingSettings);

        if (sourceLabel.Contains("Code assist", StringComparison.OrdinalIgnoreCase) ||
            response.ProviderNotes.Any(static n => n.Contains("Intent detected: CodeAssist", StringComparison.OrdinalIgnoreCase)))
        {
            parts.Add("Local tool");
            parts.Add("Code assist");
            parts.Add("Private");
            parts.Add(communityChip);
            return string.Join(" • ", parts);
        }

        if (response.ProviderNotes.Any(static n =>
                n.Contains("local deterministic calculator", StringComparison.OrdinalIgnoreCase)) ||
            sourceLabel.Contains("Calculator", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add("Local tool");
            parts.Add("Calculator");
            parts.Add("Private");
            parts.Add(communityChip);
            return string.Join(" • ", parts);
        }

        if (response.ResponseSource == KyraResponseSource.ForgerEmsGateway && response.UsedOnlineData)
        {
            parts.Add("Live research");
            parts.Add("Sanitized context");
            parts.Add("Private");
            parts.Add(communityChip);
            return string.Join(" • ", parts);
        }

        if (sourceLabel.Contains("unavailable", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add("Live tool unavailable");
        }
        else if (sourceLabel.Contains("live research", StringComparison.OrdinalIgnoreCase) ||
                 (response.UsedOnlineData && sourceLabel.Contains("live", StringComparison.OrdinalIgnoreCase)))
        {
            parts.Add("Live research");
        }
        else if (response.UsedOnlineData || response.ProviderType is not CopilotProviderType.LocalOffline)
        {
            parts.Add("Online");
        }
        else if (sourceLabel.Contains("live tool", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add("Live tool");
        }
        else if (sourceLabel.Contains("command", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add("Command");
        }
        else
        {
            parts.Add("Local");
        }

        if (response.GroundedInSystemIntelligence)
        {
            parts.Add("System scan");
        }

        if (response.ProviderNotes.Any(static n =>
                n.Contains("hardware facts -> local System Intelligence", StringComparison.OrdinalIgnoreCase)))
        {
            parts.Add("Local hardware facts");
        }

        if (localMemoryUsed)
        {
            parts.Add("Local memory");
        }

        if (response.OnlineEnhancementApplied)
        {
            parts.Add("Online assist");
        }

        parts.Add(response.UsedOnlineData ? "Sanitized context" : "Private");
        parts.Add(communityChip);

        return string.Join(" • ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string BuildKyraMetadataDetails(
        CopilotResponse response,
        bool localMemoryUsed = false,
        CopilotSettings? sharingSettings = null)
    {
        var details = new List<string>();
        var sourceLabel = response.SourceLabel ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(sourceLabel))
        {
            details.Add("Source: " + sourceLabel.Trim());
        }

        if (!string.IsNullOrWhiteSpace(response.KyraTransparencySummary))
        {
            details.Add("Why this answer: " + response.KyraTransparencySummary.Trim());
        }

        if (!string.IsNullOrWhiteSpace(response.OnlineStatus))
        {
            details.Add(response.OnlineStatus.Trim());
        }

        if (localMemoryUsed)
        {
            details.Add("Local Kyra repair memory was used for this machine-scoped answer.");
        }

        details.Add(KyraCommunityMetadataFormatter.DetailsParagraph(sharingSettings));

        if (response.ProviderNotes is { Count: > 0 })
        {
            details.AddRange(response.ProviderNotes.Where(n => !string.IsNullOrWhiteSpace(n)));
        }

        if (details.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(Environment.NewLine, details.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static bool TryBuildLiveToolParseForPrompt(string prompt, out KyraSlashCommandParseResult parse)
    {
        return KyraInlineLivePromptRouter.TryBuildParse(prompt, out parse);
    }

    private async Task<CopilotResponse> ExecuteInlineLiveToolAsync(
        KyraSlashCommandParseResult liveParse,
        string reportPath,
        string toolkitReportPath)
    {
        ReportKyraActivity("Researching live data…");
        var uiSettings = BuildCopilotSettingsFromUi();
        var liveFacts = BuildKyraToolHostFacts(reportPath, toolkitReportPath, uiSettings);
        var liveRoute = await KyraLiveSlashCoordinator.ExecuteLiveAsync(
            liveParse,
            uiSettings,
            liveFacts,
            _copilotGenerationCancellation?.Token ?? CancellationToken.None);
        var liveResp = liveRoute.ToCopilotResponse();
        if (liveResp is not null)
        {
            return new CopilotResponse
            {
                Text = liveResp.Text,
                UsedOnlineData = liveResp.UsedOnlineData,
                ProviderType = liveResp.ProviderType,
                ProviderNotes = liveResp.ProviderNotes,
                ResponseSource = liveResp.ResponseSource,
                SourceLabel = liveResp.UsedOnlineData ? "Live research" : "Live tool unavailable",
                OnlineStatus = liveResp.UsedOnlineData ? "Live tool result" : "Live data unavailable",
                FallbackUsed = liveResp.FallbackUsed,
                OnlineEnhancementApplied = liveResp.OnlineEnhancementApplied,
                GroundedInSystemIntelligence = liveResp.GroundedInSystemIntelligence,
                ActionSuggestions = liveResp.ActionSuggestions
            };
        }

        return new CopilotResponse
        {
            Text = "Live tool is unavailable right now. Try `/provider` to check status.",
            ProviderType = CopilotProviderType.LocalOffline,
            SourceLabel = "Live tool unavailable",
            OnlineStatus = "Live data unavailable"
        };
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

    private CopilotRequest CreateKyraCopilotRequest(string prompt, string reportPath, string toolkitReportPath)
    {
        var ui = BuildCopilotSettingsFromUi();
        var cross = ui.KyraUseSanitizedSystemIntelligenceContext
            ? KyraSafeContextBuilder.BuildBriefSummary(
                reportPath,
                Path.Combine(GetRuntimeReportsDirectory(), "usb-intelligence-latest.json"),
                toolkitReportPath,
                Path.Combine(GetRuntimeReportsDirectory(), "diagnostics-latest.json"),
                ui.RedactContextEnabled)
            : string.Empty;
        return new CopilotRequest
        {
            Prompt = prompt,
            SystemIntelligenceReportPath = reportPath,
            ToolkitHealthReportPath = toolkitReportPath,
            AppVersion = GetType().Assembly.GetName().Version?.ToString() ?? "unknown",
            RecentLogLines = Logs.Select(line => line.DisplayText).TakeLast(24).ToArray(),
            SelectedUsbTarget = SelectedUsbTarget,
            Settings = ui,
            VerboseDiagnosticNotes = VerboseLiveLogs,
            KyraMemorySummaryForPrompt = BuildKyraMemorySummaryForPrompt(prompt),
            KyraActivityStatusCallback = ReportKyraActivity,
            KyraSafeCrossSystemSummary = cross,
            KyraMachineMemoryStorePath = _kyraMachineMemoryPath
        };
    }

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
            return "Kyra is thinking…";
        }

        return "Kyra is thinking…";
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
        sb.AppendLine(_copilotSettings.ApiFirstRouting
            ? "Kyra Mode: API-first hybrid — configured providers may answer first when mode and privacy settings allow."
            : "Online assist: off — Local Kyra drafts first when polish mode applies.");
        sb.AppendLine(_copilotSettings.OfflineFallbackEnabled ? "Local fallback: enabled." : "Local fallback: disabled.");
        sb.AppendLine(_copilotSettings.AllowOnlineSystemContextSharing ? "System context to online providers: on (sanitized summary only)." : "System context to online providers: off.");
        sb.AppendLine($"Provider priority: {_copilotSettings.ProviderPriorityCsv}");
        sb.AppendLine($"Memory: {_copilotSettings.MaxContextTurns} turns / {_copilotSettings.MemoryMode}; personality: {_copilotSettings.PersonalityProfile}.");
        sb.AppendLine("Live tools: weather/crypto only when enabled under Kyra Advanced live APIs; news/sports/stocks/search need operator keys.");
        sb.AppendLine(_copilotSettings.ProviderConfigurationMode == KyraProviderConfigurationMode.DeveloperManaged
            ? "Provider configuration mode: developer-managed (beta testers are not prompted for BYOK)."
            : "Provider configuration mode: user-managed (advanced).");
        sb.AppendLine(ctx.SystemProfile is not null ? "System context: available from last scan." : "System context: run System Intelligence for machine-specific answers.");
        sb.AppendLine(_copilotSettings.KyraPersistentMemoryEnabled ? "Kyra memory: enabled (local disk, user-controlled)." : "Kyra memory: off.");
        sb.AppendLine(
            _copilotSettings.KyraRealtimeGatewayResearchEnabled && _copilotSettings.KyraRealtimeGatewayEnabled
                ? "Realtime gateway research: enabled when gateway URL + token are configured (provider keys stay server-side)."
                : "Realtime gateway research: off — current-data questions use local Kyra and Kyra Advanced live tools only.");
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

    private string? BuildKyraMemorySummaryForPrompt(string prompt)
    {
        if (KyraCodeSnippetDetector.LooksLikeCodeSnippet(prompt))
        {
            return null;
        }

        var parts = new List<string>();
        if (_copilotSettings.KyraPersistentMemoryEnabled)
        {
            var store = new KyraPersistentMemoryStore(_kyraMemoryPath);
            var doc = store.Load();
            doc.Enabled = _copilotSettings.KyraPersistentMemoryEnabled;
            var legacyHint = store.BuildPromptHint(doc);
            if (!string.IsNullOrWhiteSpace(legacyHint))
            {
                parts.Add(legacyHint);
            }
        }

        if (_copilotSettings.KyraLocalRepairMemoryEnabled && KyraMemorySanitizer.IsMachineScopedPrompt(prompt))
        {
            var machineStore = new KyraMachineMemoryStore(_kyraMachineMemoryPath);
            var summary = KyraMemorySummaryBuilder.BuildForPrompt(machineStore.Load(), prompt);
            if (!string.IsNullOrWhiteSpace(summary))
            {
                parts.Add(summary);
            }
        }

        return parts.Count == 0 ? null : string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    private bool KyraLocalRepairMemoryWouldApply(string prompt)
    {
        if (!_copilotSettings.KyraLocalRepairMemoryEnabled || !KyraMemorySanitizer.IsMachineScopedPrompt(prompt))
        {
            return false;
        }

        try
        {
            return new KyraMachineMemoryStore(_kyraMachineMemoryPath).Load().Entries.Count > 0;
        }
        catch
        {
            return false;
        }
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

    private static string KyraPrivacyModeForLearning(CopilotSettings settings) =>
        settings.KyraCommunitySharingEnabled ? "community-preview" : "local-only";

    private void TryRecordKyraIntelligenceMemory(string prompt, CopilotResponse response, string reportPath, KyraIntent intent)
    {
        try
        {
            if (!_copilotSettings.KyraLocalRepairMemoryEnabled ||
                !KyraMemorySanitizer.IsMachineScopedPrompt(prompt) ||
                KyraCodeSnippetDetector.LooksLikeCodeSnippet(prompt))
            {
                return;
            }

            var profile = CopilotService.TryLoadSystemProfileFromReport(reportPath);
            var health = SystemHealthEvaluator.Evaluate(profile);
            var appVer = GetType().Assembly.GetName().Version?.ToString() ?? "unknown";
            var entry = KyraMemorySanitizer.BuildEntryFromPrompt(
                prompt,
                response.Text,
                profile,
                health,
                intent,
                appVer,
                "beta",
                KyraPrivacyModeForLearning(_copilotSettings));
            var store = new KyraMachineMemoryStore(_kyraMachineMemoryPath);
            if (store.TryAppend(entry, KyraMemorySanitizer.FromCopilotSettings(_copilotSettings)))
            {
                AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] Kyra saved a sanitized local repair memory note.", LogSeverity.Info, channel: LiveLogChannel.KyraDetail));
                _ = TryPrepareCommunityLearningStubAsync(entry);
            }
        }
        catch (Exception exception)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Kyra local repair memory write skipped: {exception.Message}", LogSeverity.Warning, channel: LiveLogChannel.KyraDetail));
        }
    }

    private void TryRecordKyraSystemScanLearning()
    {
        try
        {
            if (!_copilotSettings.KyraLocalRepairMemoryEnabled)
            {
                return;
            }

            var reportPath = Path.Combine(GetRuntimeReportsDirectory(), "system-intelligence-latest.json");
            if (!File.Exists(reportPath))
            {
                return;
            }

            var profile = CopilotService.TryLoadSystemProfileFromReport(reportPath);
            var health = SystemHealthEvaluator.Evaluate(profile);
            var appVer = GetType().Assembly.GetName().Version?.ToString() ?? "unknown";
            var entry = KyraMemorySanitizer.BuildEntryFromPrompt(
                "System Intelligence scan completed for this machine.",
                "System Intelligence report refreshed on this PC.",
                profile,
                health,
                KyraIntent.SystemHealthSummary,
                appVer,
                "beta",
                KyraPrivacyModeForLearning(_copilotSettings),
                kyraActionCategory: "system_scan",
                outcomeCategory: "scan_completed",
                sanitizedNotesOverride: "system scan",
                userConfirmedFix: "unknown");
            var store = new KyraMachineMemoryStore(_kyraMachineMemoryPath);
            if (store.TryAppend(entry, KyraMemorySanitizer.FromCopilotSettings(_copilotSettings)))
            {
                _ = TryPrepareCommunityLearningStubAsync(entry);
            }
        }
        catch
        {
            // best-effort learning note
        }
    }

    private void TryRecordKyraUsbBenchmarkLearning(UsbBenchmarkResult result)
    {
        try
        {
            if (!_copilotSettings.KyraLocalRepairMemoryEnabled || !result.Succeeded)
            {
                return;
            }

            var reportPath = Path.Combine(GetRuntimeReportsDirectory(), "system-intelligence-latest.json");
            var profile = CopilotService.TryLoadSystemProfileFromReport(reportPath);
            var health = SystemHealthEvaluator.Evaluate(profile);
            var response =
                $"USB benchmark completed. Summary: {result.Summary}. Read: {result.ReadSpeedDisplay}. Write: {result.WriteSpeedDisplay}.";
            var appVer = GetType().Assembly.GetName().Version?.ToString() ?? "unknown";
            var entry = KyraMemorySanitizer.BuildEntryFromPrompt(
                "USB benchmark completed for the selected removable target.",
                response,
                profile,
                health,
                KyraIntent.USBBuilderHelp,
                appVer,
                "beta",
                KyraPrivacyModeForLearning(_copilotSettings),
                kyraActionCategory: "usb_benchmark",
                outcomeCategory: "benchmark_completed",
                sanitizedNotesOverride: "usb benchmark",
                userConfirmedFix: "unknown");
            var store = new KyraMachineMemoryStore(_kyraMachineMemoryPath);
            if (store.TryAppend(entry, KyraMemorySanitizer.FromCopilotSettings(_copilotSettings)))
            {
                _ = TryPrepareCommunityLearningStubAsync(entry);
            }
        }
        catch
        {
        }
    }

    private void TryRecordKyraUsbBenchmarkBlockedLearning(string blockReason)
    {
        try
        {
            if (!_copilotSettings.KyraLocalRepairMemoryEnabled)
            {
                return;
            }

            var reportPath = Path.Combine(GetRuntimeReportsDirectory(), "system-intelligence-latest.json");
            var profile = CopilotService.TryLoadSystemProfileFromReport(reportPath);
            var health = SystemHealthEvaluator.Evaluate(profile);
            var appVer = GetType().Assembly.GetName().Version?.ToString() ?? "unknown";
            var entry = KyraMemorySanitizer.BuildEntryFromPrompt(
                "USB benchmark was blocked for safety on this machine.",
                blockReason,
                profile,
                health,
                KyraIntent.USBBuilderHelp,
                appVer,
                "beta",
                KyraPrivacyModeForLearning(_copilotSettings),
                kyraActionCategory: "usb_safety",
                outcomeCategory: "blocked",
                sanitizedNotesOverride: "usb target blocked",
                userConfirmedFix: "unknown");
            var store = new KyraMachineMemoryStore(_kyraMachineMemoryPath);
            store.TryAppend(entry, KyraMemorySanitizer.FromCopilotSettings(_copilotSettings));
        }
        catch
        {
        }
    }

    private async Task TryPrepareCommunityLearningStubAsync(KyraMemoryEntry entry)
    {
        try
        {
            var settings = KyraMemorySanitizer.FromCopilotSettings(_copilotSettings);
            if (!settings.CommunitySharingEnabled)
            {
                return;
            }

            var client = new DisabledKyraCommunityIntelligenceClient();
            var dto = KyraCommunityPayloadPreviewBuilder.FromMemoryEntry(
                entry,
                GetType().Assembly.GetName().Version?.ToString() ?? "unknown",
                "beta");
            await new KyraCommunityConsentService()
                .TrySubmitAsync(client, dto, settings, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private void ApplyKyraFeedbackThisFixedIt(CopilotChatMessage? message) =>
        ApplyKyraUserFeedback(message, "yes", "resolved", "User confirmed the suggested fix worked.");

    private void ApplyKyraFeedbackStillBroken(CopilotChatMessage? message) =>
        ApplyKyraUserFeedback(message, "no", "unresolved", "User reported the issue is still present.");

    private void ApplyKyraFeedbackNotSure(CopilotChatMessage? message) =>
        ApplyKyraUserFeedback(message, "unknown", "unknown", "User was not sure whether the fix worked.");

    private void ApplyKyraFeedbackSaveRepairNote(CopilotChatMessage? message)
    {
        if (message is null)
        {
            return;
        }

        var note = _userPromptService.PromptText(
            "Save repair note",
            "Short repair note (sanitized; no secrets, paths, or serials):",
            string.Empty);
        if (string.IsNullOrWhiteSpace(note))
        {
            return;
        }

        ApplyKyraUserFeedback(message, "unknown", "note_saved", note.Trim());
    }

    private void ApplyKyraUserFeedback(
        CopilotChatMessage? message,
        string userConfirmedFix,
        string outcomeCategory,
        string sanitizedNotes)
    {
        if (message is null || !message.Role.Equals("Kyra", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        message.ShowTroubleshootingFeedback = false;
        if (!_copilotSettings.KyraLocalRepairMemoryEnabled)
        {
            return;
        }

        try
        {
            var reportPath = Path.Combine(GetRuntimeReportsDirectory(), "system-intelligence-latest.json");
            var profile = CopilotService.TryLoadSystemProfileFromReport(reportPath);
            var health = SystemHealthEvaluator.Evaluate(profile);
            var prompt = message.LearningUserPrompt ?? string.Empty;
            var responseText = message.LearningKyraResponsePlain ?? string.Empty;
            var appVer = GetType().Assembly.GetName().Version?.ToString() ?? "unknown";
            var entry = KyraMemorySanitizer.BuildEntryFromPrompt(
                prompt,
                responseText,
                profile,
                health,
                message.LearningIntent,
                appVer,
                "beta",
                KyraPrivacyModeForLearning(_copilotSettings),
                kyraActionCategory: "user_feedback",
                outcomeCategory: outcomeCategory,
                sanitizedNotesOverride: sanitizedNotes,
                userConfirmedFix: userConfirmedFix);
            var store = new KyraMachineMemoryStore(_kyraMachineMemoryPath);
            if (store.TryAppend(entry, KyraMemorySanitizer.FromCopilotSettings(_copilotSettings)))
            {
                AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] Kyra saved your feedback as a sanitized local learning note.", LogSeverity.Info, channel: LiveLogChannel.KyraDetail));
                _ = TryPrepareCommunityLearningStubAsync(entry);
            }
        }
        catch (Exception exception)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Kyra feedback could not be saved: {exception.Message}", LogSeverity.Warning, channel: LiveLogChannel.KyraDetail));
        }
    }

    private void BetaWelcomeKyraKeepLocalOnly()
    {
        KeepKyraLocalOnly();
        ResetBetaWelcomeKyraConsentCheckboxes();
        DismissBetaWelcome();
    }

    private void BetaWelcomeKyraHelpImprove()
    {
        var any =
            BetaWelcomeKyraShareRepairIntelligence ||
            BetaWelcomeKyraShareHardwarePatterns ||
            BetaWelcomeKyraShareResolvedCategories ||
            BetaWelcomeKyraShareCrashDiagnostics;
        KyraCommunitySharingEnabled = any;
        if (any)
        {
            KyraShareHardwareCompatibilityPerformancePatterns = BetaWelcomeKyraShareHardwarePatterns;
            KyraShareResolvedIssueFixPatterns = BetaWelcomeKyraShareResolvedCategories;
            KyraShareCrashErrorDiagnostics = BetaWelcomeKyraShareCrashDiagnostics;
            if (BetaWelcomeKyraShareRepairIntelligence &&
                !BetaWelcomeKyraShareHardwarePatterns &&
                !BetaWelcomeKyraShareResolvedCategories &&
                !BetaWelcomeKyraShareCrashDiagnostics)
            {
                KyraShareResolvedIssueFixPatterns = true;
            }
        }

        SaveCopilotSettings();
        ResetBetaWelcomeKyraConsentCheckboxes();
        DismissBetaWelcome();
    }

    private void BetaWelcomeKyraViewSharingPreview()
    {
        try
        {
            var any =
                BetaWelcomeKyraShareRepairIntelligence ||
                BetaWelcomeKyraShareHardwarePatterns ||
                BetaWelcomeKyraShareResolvedCategories ||
                BetaWelcomeKyraShareCrashDiagnostics;
            var hypo = new KyraMemorySettings
            {
                LocalRepairMemoryEnabled = true,
                CommunitySharingEnabled = any,
                ShareResolvedIssueFixPatterns = BetaWelcomeKyraShareResolvedCategories,
                ShareHardwareCompatibilityPerformancePatterns = BetaWelcomeKyraShareHardwarePatterns,
                ShareCrashErrorDiagnostics = BetaWelcomeKyraShareCrashDiagnostics
            };
            var preview = KyraCommunityPayloadPreviewBuilder.BuildPreview(
                new KyraMachineMemoryStore(_kyraMachineMemoryPath).Load(),
                KyraMemorySanitizer.FromCopilotSettings(_copilotSettings),
                GetType().Assembly.GetName().Version?.ToString() ?? "unknown",
                "beta",
                hypo);
            MessageBox.Show(preview, "Kyra Intelligence — what would be shared (sanitized preview)", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Kyra preview", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SeedBetaWelcomeKyraCheckboxesFromSettings()
    {
        var snap = KyraInstallerIntelligenceRegistry.ReadSnapshot();
        if (snap.Any)
        {
            BetaWelcomeKyraShareRepairIntelligence = snap.Repair != 0;
            BetaWelcomeKyraShareHardwarePatterns = snap.Hardware != 0;
            BetaWelcomeKyraShareResolvedCategories = snap.Resolved != 0;
            BetaWelcomeKyraShareCrashDiagnostics = snap.Crash != 0;
            return;
        }

        BetaWelcomeKyraShareHardwarePatterns = _copilotSettings.KyraShareHardwareCompatibilityPerformancePatterns;
        BetaWelcomeKyraShareResolvedCategories = _copilotSettings.KyraShareResolvedIssueFixPatterns;
        BetaWelcomeKyraShareCrashDiagnostics = _copilotSettings.KyraShareCrashErrorDiagnostics;
        BetaWelcomeKyraShareRepairIntelligence =
            _copilotSettings.KyraCommunitySharingEnabled &&
            !BetaWelcomeKyraShareHardwarePatterns &&
            !BetaWelcomeKyraShareResolvedCategories &&
            !BetaWelcomeKyraShareCrashDiagnostics;
    }

    private void ResetBetaWelcomeKyraConsentCheckboxes()
    {
        BetaWelcomeKyraShareRepairIntelligence = false;
        BetaWelcomeKyraShareHardwarePatterns = false;
        BetaWelcomeKyraShareResolvedCategories = false;
        BetaWelcomeKyraShareCrashDiagnostics = false;
    }

    private void ResetKyraMachineLearning()
    {
        if (!_userPromptService.Confirm(
                "Reset Kyra learning",
                "Delete local Kyra Intelligence memory for this machine? Consent choices are not changed."))
        {
            return;
        }

        try
        {
            new KyraMachineMemoryStore(_kyraMachineMemoryPath).Delete();
            AppendLog(new LogLine(DateTimeOffset.Now, "[OK] Kyra machine learning memory reset on this PC.", LogSeverity.Success));
        }
        catch (Exception exception)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Kyra learning reset failed: {exception.Message}", LogSeverity.Warning));
        }
    }

    private void ViewKyraCommunityPayloadPreview()
    {
        try
        {
            var store = new KyraMachineMemoryStore(_kyraMachineMemoryPath);
            var preview = KyraCommunityPayloadPreviewBuilder.BuildPreview(
                store.Load(),
                KyraMemorySanitizer.FromCopilotSettings(BuildCopilotSettingsFromUi()),
                GetType().Assembly.GetName().Version?.ToString() ?? "unknown",
                "beta");
            MessageBox.Show(preview, "Kyra Intelligence sharing preview (sanitized)", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Kyra Intelligence preview", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExportKyraIntelligenceMemory()
    {
        try
        {
            var dlg = new SaveFileDialog
            {
                Filter = "JSON (*.json)|*.json",
                FileName = "kyra-intelligence-memory-export.json"
            };
            if (dlg.ShowDialog() == true)
            {
                File.WriteAllText(dlg.FileName, new KyraMachineMemoryStore(_kyraMachineMemoryPath).ExportSanitized());
                AppendLog(new LogLine(DateTimeOffset.Now, "[OK] Exported Kyra Intelligence memory (sanitized).", LogSeverity.Success));
            }
        }
        catch (Exception exception)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Kyra Intelligence memory export failed: {exception.Message}", LogSeverity.Warning));
        }
    }

    private void DeleteKyraIntelligenceMemory()
    {
        if (!_userPromptService.Confirm(
                "Delete Kyra Memory",
                "Delete local Kyra repair memory from this PC? This keeps Kyra local-only behavior intact, but removes stored sanitized repair notes."))
        {
            return;
        }

        try
        {
            new KyraMachineMemoryStore(_kyraMachineMemoryPath).Delete();
            AppendLog(new LogLine(DateTimeOffset.Now, "[OK] Kyra Intelligence memory deleted from disk.", LogSeverity.Success));
        }
        catch (Exception exception)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Kyra Intelligence memory delete failed: {exception.Message}", LogSeverity.Warning));
        }
    }

    private void KeepKyraLocalOnly()
    {
        KyraCommunitySharingEnabled = false;
        KyraShareResolvedIssueFixPatterns = false;
        KyraShareHardwareCompatibilityPerformancePatterns = false;
        KyraShareCrashErrorDiagnostics = false;
        SaveCopilotSettings();
    }

    private void EnableKyraCommunityLearning()
    {
        KyraCommunitySharingEnabled = true;
        if (!KyraShareResolvedIssueFixPatterns &&
            !KyraShareHardwareCompatibilityPerformancePatterns &&
            !KyraShareCrashErrorDiagnostics)
        {
            KyraShareResolvedIssueFixPatterns = true;
        }

        SaveCopilotSettings();
    }

    private void CopyBetaReportTemplate()
    {
        var text =
            $"App version: {AppVersionText}{Environment.NewLine}" +
            "Windows version (Win+R → winver): " +
            Environment.NewLine +
            Environment.NewLine +
            "Device model (if known): " +
            Environment.NewLine +
            Environment.NewLine +
            "Tab / action: " +
            Environment.NewLine +
            Environment.NewLine +
            "Expected result: " +
            Environment.NewLine +
            Environment.NewLine +
            "Actual result: " +
            Environment.NewLine +
            Environment.NewLine +
            "Screenshot: (attach)" +
            Environment.NewLine +
            Environment.NewLine +
            "Safe logs: (attach excerpts — no passwords, API keys, or private files)" +
            Environment.NewLine;
        Clipboard.SetText(text);
        AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] Copied beta issue template to clipboard.", LogSeverity.Info));
    }

    private async Task CheckKyraGatewayStatusAsync()
    {
        try
        {
            if (!ForgerEmsEnvironmentConfiguration.KyraGatewayEnabled)
            {
                KyraGatewayProviderStatusSummary =
                    "Realtime gateway is off (FORGEREMS_KYRA_GATEWAY_ENABLED=false). Local Kyra stays available.";
                AppendLog(new LogLine(
                    DateTimeOffset.Now,
                    "[INFO] Kyra gateway status: disabled via environment.",
                    LogSeverity.Info,
                    channel: LiveLogChannel.KyraDetail));
                return;
            }

            var settings = BuildCopilotSettingsFromUi();
            if (!settings.KyraRealtimeGatewayEnabled)
            {
                KyraGatewayProviderStatusSummary =
                    "Kyra Realtime Gateway is disabled in Kyra Advanced. Enable it here to use the secure gateway path.";
                return;
            }

            var cfg = settings.Providers.TryGetValue(KyraGatewayProvider.ProviderId, out var pc)
                ? KyraGatewayProviderConfig.FromProviderConfiguration(pc)
                : KyraGatewayProviderConfig.FromEnvironment();

            if (!cfg.IsConfigured)
            {
                KyraGatewayProviderStatusSummary =
                    "Gateway URL or beta token is missing. Configure the ForgerEMS gateway provider under Providers, or set FORGEREMS_KYRA_GATEWAY_URL and FORGEREMS_KYRA_GATEWAY_BETA_TOKEN.";
                return;
            }

            var endpoint = KyraGatewayStatusClient.BuildStatusEndpoint(cfg.GatewayUrl);
            var result = await KyraGatewayStatusClient.FetchAsync(
                    endpoint,
                    cfg.BetaToken,
                    cfg.TimeoutSeconds,
                    CancellationToken.None)
                .ConfigureAwait(true);

            if (!result.Ok || result.Providers is null)
            {
                var code = result.ErrorCode ?? "unknown";
                KyraGatewayProviderStatusSummary =
                    $"Gateway status request did not succeed (code: {code}). The worker may be unreachable, outdated, or the token may be invalid. Local Kyra is still available.";
                AppendLog(new LogLine(
                    DateTimeOffset.Now,
                    "[INFO] Kyra gateway status check finished without OK response (no secrets logged).",
                    LogSeverity.Info,
                    channel: LiveLogChannel.KyraDetail));
                return;
            }

            var p = result.Providers;
            KyraGatewayProviderStatusSummary =
                $"Gateway host: {cfg.GatewayHost}{Environment.NewLine}" +
                $"AI chat: {p.AiChat ?? "unknown"}{Environment.NewLine}" +
                $"Crypto: {p.Crypto ?? "unknown"}{Environment.NewLine}" +
                $"Weather: {p.Weather ?? "unknown"}{Environment.NewLine}" +
                $"Finance: {p.Finance ?? "unknown"}{Environment.NewLine}" +
                $"News: {p.News ?? "unknown"}{Environment.NewLine}" +
                $"Web research: {p.WebResearch ?? "unknown"}{Environment.NewLine}" +
                "Server-side readiness only — no provider secrets are returned to the app.";

            AppendLog(new LogLine(
                DateTimeOffset.Now,
                "[INFO] Kyra gateway status check completed.",
                LogSeverity.Info,
                channel: LiveLogChannel.KyraDetail));
        }
        catch (Exception exception)
        {
            KyraGatewayProviderStatusSummary =
                "Gateway status check encountered an error. Local Kyra is still available.";
            AppendLog(new LogLine(
                DateTimeOffset.Now,
                $"[WARN] Kyra gateway status: {exception.Message}",
                LogSeverity.Warning));
        }
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

    private void CopySelectedToolkitExpectedPath()
    {
        var item = SelectedToolkitHealthItem;
        if (item is null)
        {
            return;
        }

        var path = string.IsNullOrWhiteSpace(item.ResolvedExpectedPath)
            ? item.ExpectedPath
            : item.ResolvedExpectedPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            AppendLog(new LogLine(DateTimeOffset.Now, "[WARN] Expected path is not available for the selected item.", LogSeverity.Warning));
            return;
        }

        Clipboard.SetText(path);
        AppendLog(new LogLine(DateTimeOffset.Now, "[OK] Copied expected path for selected toolkit item.", LogSeverity.Success));
    }

    private void CopySelectedToolkitDetectedPath()
    {
        var item = SelectedToolkitHealthItem;
        if (item is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(item.MatchedPath))
        {
            AppendLog(new LogLine(DateTimeOffset.Now, "[WARN] Detected path is not available for the selected item.", LogSeverity.Warning));
            return;
        }

        Clipboard.SetText(item.MatchedPath);
        AppendLog(new LogLine(DateTimeOffset.Now, "[OK] Copied detected path for selected toolkit item.", LogSeverity.Success));
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

        var preflight = _usbIntelligenceService.GetVentoyPreflight(selectedUsbTarget, null);
        if (preflight.ShouldWarn &&
            !_userPromptService.Confirm(
                "USB builder pre-flight",
                $"{preflight.Message}{Environment.NewLine}{Environment.NewLine}Continue preparing Ventoy on this port?"))
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

        using var gate = await EnterUsbBuilderActionGateAsync("Preparing Ventoy").ConfigureAwait(true);

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
            try
            {
                await RefreshUsbTargetsAsync();
                AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] USB targets refreshed after Ventoy install/update action.", LogSeverity.Info, channel: LiveLogChannel.Diagnostics));
            }
            catch (Exception refreshException)
            {
                AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] USB target refresh after Ventoy action failed: {refreshException.Message}", LogSeverity.Warning));
            }

            IsBusy = false;
            ResetProgressSoon();
        }
    }

    private void StartUsbPortMappingWorkflow()
    {
        _usbGuidedMappingWorkflow.StartMappingSession();
        UsbMappingWorkflowStatus =
            "Step 1: USB Mapping started. Step 2: With the USB in the starting port, click Capture Current Port.";
        AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] USB port mapping workflow started.", LogSeverity.Info));
    }

    private void CaptureUsbMappingBefore()
    {
        var snap = _usbIntelligenceService.BuildTopologySnapshot(SelectedUsbTarget);
        _usbGuidedMappingWorkflow.CaptureBeforeSnapshot(snap);
        UsbMappingWorkflowStatus =
            "Step 3: Move the USB to the port you want to label, wait for it to mount, then click Detect Port Change.";
        AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] USB mapping: before snapshot captured.", LogSeverity.Info));
    }

    private void CaptureUsbMappingAfter()
    {
        var snap = _usbIntelligenceService.BuildTopologySnapshot(SelectedUsbTarget);
        _usbGuidedMappingWorkflow.CaptureAfterSnapshot(snap);
        UsbMappingWorkflowStatus =
            "Step 4: Enter a short label for that port. Step 5: Click Save Port Label (writes to your local profile).";
        AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] USB mapping: after snapshot captured.", LogSeverity.Info));
    }

    private async Task RunUsbIntelligenceBenchmarkAsync() =>
        await AutoBenchmarkSelectedUsbSafeAsync(isAutomatic: false).ConfigureAwait(true);

    private async void OpenUsbMappingWizard()
    {
        var vm = new UsbMappingWizardViewModel(
            _usbIntelligenceService,
            _usbMachineProfileStore,
            () => UsbTargets.ToList(),
            RunWizardUsbBenchmarkAsync);
        var win = new UsbMappingWizardWindow(vm);
        if (Application.Current?.MainWindow is { } owner)
        {
            win.Owner = owner;
        }

        win.ShowDialog();
        await RefreshUsbIntelligenceReportForSelectedTargetAsync().ConfigureAwait(true);
        _autoIntelligenceOrchestrator.ScheduleUsbSelectionRefresh(_backendContext, SelectedUsbTarget);
    }

    private async Task RefreshUsbIntelligenceReportForSelectedTargetAsync()
    {
        try
        {
            var reports = GetRuntimeReportsDirectory();
            Directory.CreateDirectory(reports);
            var usbPath = Path.Combine(reports, "usb-intelligence-latest.json");
            UsbTopologySnapshot? previousUsb = null;
            if (File.Exists(usbPath))
            {
                try
                {
                    previousUsb = JsonSerializer.Deserialize<UsbTopologySnapshot>(
                        File.ReadAllText(usbPath),
                        UsbIntelligenceService.UsbJsonReadOptions);
                }
                catch (Exception ex)
                {
                    AppendDiagnosticsLog($"USB intelligence refresh: previous snapshot ignored ({ex.Message}).");
                }
            }

            var profile = _usbMachineProfileStore.LoadOrCreate();
            var snapshot = _usbIntelligenceService.BuildTopologySnapshot(
                SelectedUsbTarget,
                new UsbTopologyBuildOptions
                {
                    PreviousSnapshot = previousUsb,
                    MachineProfile = profile
                });
            _usbMachineProfileStore.ApplySnapshot(profile, snapshot);
            _usbMachineProfileStore.Save(profile);
            await _usbIntelligenceService.WriteLatestReportAsync(reports, snapshot).ConfigureAwait(true);
            RefreshUsbIntelligenceFromDisk();
            AppendDiagnosticsLog("USB mapping panel refreshed after wizard save/close. uiPanelRefreshed=true");
        }
        catch (Exception ex)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] USB mapping panel refresh failed: {ex.Message}", LogSeverity.Warning));
            RefreshUsbIntelligenceFromDisk();
        }
    }

    private async Task RunWizardUsbBenchmarkAsync(UsbTargetInfo target)
    {
        SelectedUsbTarget = target;
        await AutoBenchmarkSelectedUsbSafeAsync(isAutomatic: false).ConfigureAwait(true);
    }

    private void SaveUsbMappingLabel()
    {
        var profile = _usbMachineProfileStore.LoadOrCreate();
        if (!_usbGuidedMappingWorkflow.TrySaveMappingLabel(
                profile,
                _usbMachineProfileStore,
                UsbMappingLabelDraft.Trim(),
                out var inference,
                out var errorMessage,
                SelectedUsbTarget,
                UsbPortMappingSaveMode.TopologyInference))
        {
            UsbMappingWorkflowStatus = errorMessage;
            AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] USB mapping: {errorMessage}", LogSeverity.Warning));
            return;
        }

        UsbMappingWorkflowStatus = $"Mapping saved. {inference.SuggestionLine}";
        UsbMappingLabelDraft = string.Empty;
        AppendLog(new LogLine(DateTimeOffset.Now, "[OK] USB port label saved to machine profile.", LogSeverity.Success));
        _autoIntelligenceOrchestrator.ScheduleUsbSelectionRefresh(_backendContext, SelectedUsbTarget);
    }

    private UsbTargetInfo? TryGetUsbTargetByRootPath(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        var match = UsbTargets.FirstOrDefault(item =>
            string.Equals(item.RootPath, rootPath, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            return match;
        }

        return SelectedUsbTarget is not null && UsbRootPathsEqual(SelectedUsbTarget.RootPath, rootPath)
            ? SelectedUsbTarget
            : null;
    }

    private UsbBenchmarkResult BuildAutomaticBenchmarkNeutralUiResult(string rootPath)
    {
        var key = GetBenchmarkCacheKey(rootPath);
        if (_benchmarkResultsByRoot.TryGetValue(key, out var cached) && cached.Succeeded)
        {
            return cached;
        }

        return new UsbBenchmarkResult
        {
            Succeeded = false,
            Status = "Skipped",
            Summary = "Automatic benchmark skipped",
            Details = string.Empty,
            ReadSpeedDisplay = "Not tested",
            WriteSpeedDisplay = "Not tested",
            LastTestedAt = null
        };
    }

    private bool IsUsbRootStillPresent(string rootPath) =>
        TryGetUsbTargetByRootPath(rootPath) is not null;

    private void LogManualBenchmarkCancellation(string targetAtStartPath, UsbBenchmarkResultKind kind)
    {
        if (kind == UsbBenchmarkResultKind.DeviceRemoved)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, "[WARN] Benchmark stopped: USB drive removed.", LogSeverity.Warning));
            return;
        }

        if (kind == UsbBenchmarkResultKind.TargetChanged)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] Benchmark stopped: USB target identity changed.", LogSeverity.Info));
            return;
        }

        if (kind == UsbBenchmarkResultKind.CancelledByHost)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] Benchmark stopped: application shutdown.", LogSeverity.Info));
            return;
        }

        if (!UsbRootPathsEqual(SelectedUsbTarget?.RootPath, targetAtStartPath))
        {
            AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] Benchmark stopped: selection changed.", LogSeverity.Info));
            return;
        }

        AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] Benchmark cancelled by user.", LogSeverity.Info));
    }

    private static UsbBenchmarkResult ReconcileUsbBenchmarkWithLockedIdentity(
        UsbBenchmarkResult result,
        UsbTargetIdentitySnapshot identityAtStart,
        string targetAtStartPath,
        Func<string, UsbTargetInfo?> tryGetLiveTarget,
        bool isAutomatic)
    {
        if (isAutomatic)
        {
            return result;
        }

        var live = tryGetLiveTarget(targetAtStartPath);
        if (live is null)
        {
            if (result.Succeeded && result.ResultKind == UsbBenchmarkResultKind.Completed)
            {
                return BuildUsbBenchmarkReclassified(result, UsbBenchmarkResultKind.DeviceRemoved);
            }

            if (result.ResultKind == UsbBenchmarkResultKind.CancelledByUser)
            {
                return BuildUsbBenchmarkReclassified(result, UsbBenchmarkResultKind.DeviceRemoved);
            }

            return result;
        }

        if (!identityAtStart.MatchesVolumeIdentity(live, out _))
        {
            if (result.Succeeded && result.ResultKind == UsbBenchmarkResultKind.Completed)
            {
                return BuildUsbBenchmarkReclassified(result, UsbBenchmarkResultKind.TargetChanged);
            }

            if (result.ResultKind == UsbBenchmarkResultKind.CancelledByUser)
            {
                return BuildUsbBenchmarkReclassified(result, UsbBenchmarkResultKind.TargetChanged);
            }
        }

        return result;
    }

    private static UsbBenchmarkResult BuildUsbBenchmarkReclassified(UsbBenchmarkResult source, UsbBenchmarkResultKind kind)
    {
        var now = DateTimeOffset.UtcNow;
        return new UsbBenchmarkResult
        {
            RunId = source.RunId,
            Succeeded = false,
            Status = kind switch
            {
                UsbBenchmarkResultKind.DeviceRemoved => "Device removed",
                UsbBenchmarkResultKind.TargetChanged => "Target changed",
                UsbBenchmarkResultKind.CancelledByHost => "Cancelled",
                _ => "Cancelled"
            },
            Summary = "Benchmark did not complete",
            Details = source.Details,
            ReadSpeedDisplay = "—",
            WriteSpeedDisplay = "—",
            TestSizeMb = source.TestSizeMb,
            LastTestedAt = now,
            WriteSpeedMBps = 0,
            ReadSpeedMBps = 0,
            BenchmarkDurationMs = source.BenchmarkDurationMs,
            IntelligenceMeasurementClass = string.Empty,
            IntelligenceConfidenceScore = 0,
            ResultKind = kind,
            CancellationSource = source.CancellationSource,
            StartedAtUtc = source.StartedAtUtc,
            CompletedAtUtc = now,
            ActualBytesWritten = source.ActualBytesWritten,
            ActualBytesRead = source.ActualBytesRead,
            WriteElapsedMs = source.WriteElapsedMs,
            ReadElapsedMs = source.ReadElapsedMs,
            ReadLikelyCached = source.ReadLikelyCached,
            ReadIsEstimate = source.ReadIsEstimate,
            BenchmarkConfidence = source.BenchmarkConfidence,
            AccuracyWarning = source.AccuracyWarning,
            TargetTopologyFingerprint = source.TargetTopologyFingerprint,
            UiSummaryLine = UsbBenchmarkUiMessages.BuildUiSummary(kind, 0, 0)
        };
    }

    private static UsbBenchmarkResult BuildManualBenchmarkCooperativeCancelResult(
        UsbBenchmarkResultKind kind,
        Guid runId,
        DateTimeOffset startedAt,
        string fingerprint)
    {
        var now = DateTimeOffset.UtcNow;
        return new UsbBenchmarkResult
        {
            RunId = runId,
            Succeeded = false,
            Status = kind switch
            {
                UsbBenchmarkResultKind.DeviceRemoved => "Device removed",
                UsbBenchmarkResultKind.TargetChanged => "Target changed",
                UsbBenchmarkResultKind.CancelledByHost => "Cancelled",
                _ => "Cancelled"
            },
            Summary = "Benchmark interrupted",
            Details = string.Empty,
            ReadSpeedDisplay = "—",
            WriteSpeedDisplay = "—",
            LastTestedAt = now,
            ResultKind = kind,
            CancellationSource = UsbBenchmarkCancellationSource.OperationCanceledUnknown,
            StartedAtUtc = startedAt,
            CompletedAtUtc = now,
            TargetTopologyFingerprint = fingerprint,
            UiSummaryLine = UsbBenchmarkUiMessages.BuildUiSummary(kind, 0, 0)
        };
    }

    private UsbBenchmarkResultKind ClassifyBenchmarkCancellation(string targetAtStartPath, bool isAutomatic)
    {
        if (isAutomatic)
        {
            return UsbBenchmarkResultKind.CancelledByHost;
        }

        var interrupt = _usbBenchmarkHostInterruptKind;
        _usbBenchmarkHostInterruptKind = UsbBenchmarkHostInterruptKind.None;
        if (!IsUsbRootStillPresent(targetAtStartPath))
        {
            return UsbBenchmarkResultKind.DeviceRemoved;
        }

        if (interrupt == UsbBenchmarkHostInterruptKind.UserRequested)
        {
            return UsbBenchmarkResultKind.CancelledByUser;
        }

        if (interrupt == UsbBenchmarkHostInterruptKind.AppShutdown)
        {
            return UsbBenchmarkResultKind.CancelledByHost;
        }

        if (interrupt == UsbBenchmarkHostInterruptKind.SelectionChanged ||
            !UsbRootPathsEqual(SelectedUsbTarget?.RootPath, targetAtStartPath))
        {
            return UsbBenchmarkResultKind.TargetChanged;
        }

        return UsbBenchmarkResultKind.CancelledByHost;
    }

    private async Task AutoBenchmarkSelectedUsbSafeAsync(bool isAutomatic = true)
    {
        if (!isAutomatic)
        {
            CancelScheduledAutomaticUsbBenchmark();
        }

        var target = SelectedUsbTarget;
        _benchmarkRequestId++;

        if (target is null)
        {
            return;
        }

        if (_usbBuilderActionGate.CurrentCount == 0)
        {
            if (isAutomatic)
            {
                AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] Auto benchmark paused while Ventoy action is running.", LogSeverity.Info));
                ScheduleAutomaticUsbBenchmark();
                return;
            }

            AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] USB benchmark deferred: USB Builder is busy with another action.", LogSeverity.Info));
            return;
        }

        var targetAtStartPath = target.RootPath;
        var benchmarkKey = GetBenchmarkCacheKey(target.RootPath);
        var identityAtStart = UsbTargetIdentitySnapshot.Capture(target);

        if (isAutomatic && IsManualUsbBenchmarkActive())
        {
            AppendLog(new LogLine(
                DateTimeOffset.Now,
                "[INFO] Benchmark skipped: manual benchmark in progress.",
                LogSeverity.Info,
                channel: LiveLogChannel.Diagnostics));
            return;
        }

        if (!isAutomatic)
        {
            CancelScheduledAutomaticUsbBenchmark();
            CancelAutoUsbBenchmarkCtsOnly();
            _benchmarksInProgress.Remove(benchmarkKey);
        }

        if (isAutomatic)
        {
            if (!_benchmarksInProgress.Add(benchmarkKey))
            {
                AppendLog(new LogLine(
                    DateTimeOffset.Now,
                    "[INFO] Benchmark skipped: device still settling (already running for this target).",
                    LogSeverity.Info,
                    channel: LiveLogChannel.Diagnostics));
                return;
            }

            if (!_usbAutomaticBenchmarkPolicy.TryRegisterAutomaticStart(target.RootPath, DateTimeOffset.UtcNow))
            {
                _benchmarksInProgress.Remove(benchmarkKey);
                AppendLog(new LogLine(
                    DateTimeOffset.Now,
                    "[INFO] USB benchmark skipped — this USB target was automatically benchmarked within the last 30 seconds.",
                    LogSeverity.Info));
                return;
            }
        }
        else
        {
            if (!_benchmarksInProgress.Add(benchmarkKey))
            {
                AppendLog(new LogLine(
                    DateTimeOffset.Now,
                    "[INFO] USB benchmark already running for this target; duplicate start ignored.",
                    LogSeverity.Info,
                    channel: LiveLogChannel.Diagnostics));
                return;
            }
        }

        if (!UsbTargetSafety.IsSafeForBenchmark(target, out var blockReason))
        {
            var nowB = DateTimeOffset.UtcNow;
            ApplyBenchmarkResult(target, new UsbBenchmarkResult
            {
                Succeeded = false,
                Status = "Blocked",
                Summary = "Benchmark skipped",
                Details = blockReason,
                ReadSpeedDisplay = "Skipped (unsafe)",
                WriteSpeedDisplay = "Skipped (unsafe)",
                LastTestedAt = nowB,
                ResultKind = UsbBenchmarkResultKind.BlockedBySafety,
                StartedAtUtc = nowB,
                CompletedAtUtc = nowB,
                TargetTopologyFingerprint = identityAtStart.TopologyFingerprint,
                UiSummaryLine = UsbBenchmarkUiMessages.BuildUiSummary(UsbBenchmarkResultKind.BlockedBySafety, 0, 0)
            });
            _benchmarksInProgress.Remove(benchmarkKey);
            TryRecordKyraUsbBenchmarkBlockedLearning(blockReason);
            return;
        }

        CancellationTokenSource? ownedCts = null;
        if (isAutomatic)
        {
            CancelAutoUsbBenchmarkCtsOnly();
        }
        else
        {
            CancelManualUsbBenchmarkCtsOnly();
        }

        _usbBenchmarkHostInterruptKind = UsbBenchmarkHostInterruptKind.None;

        try
        {
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
                LastTestedAt = target.BenchmarkLastTestedAt,
                ResultKind = UsbBenchmarkResultKind.Running,
                TargetTopologyFingerprint = identityAtStart.TopologyFingerprint,
                UiSummaryLine = UsbBenchmarkUiMessages.BuildUiSummary(UsbBenchmarkResultKind.Running, 0, 0)
            });
            var startLabel = isAutomatic ? "Automatic USB speed check started" : "USB benchmark started";
            AppendLog(new LogLine(DateTimeOffset.Now, $"[INFO] {startLabel} for {target.RootPath}.", LogSeverity.Info));

            try
            {
                ownedCts = CreateFreshUsbBenchmarkCts(isAutomatic, targetAtStartPath);
                var benchmarkToken = ownedCts.Token;
                if (benchmarkToken.IsCancellationRequested)
                {
                    AppendLog(new LogLine(
                        DateTimeOffset.Now,
                        $"[WARN] USB benchmark token was cancelled before start for {targetAtStartPath}; creating a clean run token.",
                        LogSeverity.Warning,
                        channel: LiveLogChannel.Diagnostics));
                    if (isAutomatic && ReferenceEquals(_autoUsbBenchmarkCts, ownedCts))
                    {
                        _autoUsbBenchmarkCts = null;
                    }
                    else if (!isAutomatic && ReferenceEquals(_manualUsbBenchmarkCts, ownedCts))
                    {
                        _manualUsbBenchmarkCts = null;
                    }

                    ownedCts.Dispose();
                    ownedCts = CreateFreshUsbBenchmarkCts(isAutomatic, targetAtStartPath);
                    benchmarkToken = ownedCts.Token;
                }

                RaiseCommandStates();
                var liveTarget = TryGetUsbTargetByRootPath(targetAtStartPath) ?? target;
                var serviceResult = await _usbBenchmarkService.RunSequentialBenchmarkAsync(liveTarget, AppendLog, benchmarkToken);
                var result = ReconcileUsbBenchmarkWithLockedIdentity(
                    serviceResult,
                    identityAtStart,
                    targetAtStartPath,
                    TryGetUsbTargetByRootPath,
                    isAutomatic);
                if (result.ResultKind == UsbBenchmarkResultKind.CancelledByUser &&
                    benchmarkToken.IsCancellationRequested)
                {
                    var classified = ClassifyBenchmarkCancellation(targetAtStartPath, isAutomatic);
                    if (classified != UsbBenchmarkResultKind.CancelledByUser)
                    {
                        result = BuildUsbBenchmarkReclassified(result, classified);
                    }
                }

                var liveAfter = TryGetUsbTargetByRootPath(targetAtStartPath) ?? liveTarget;

                if (isAutomatic &&
                    (!serviceResult.Succeeded &&
                     (string.Equals(serviceResult.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
                      serviceResult.Summary.Contains("cancel", StringComparison.OrdinalIgnoreCase))))
                {
                    AppendLog(new LogLine(
                        DateTimeOffset.Now,
                        "[INFO] Benchmark skipped: device still settling.",
                        LogSeverity.Info,
                        channel: LiveLogChannel.Diagnostics));
                    ApplyBenchmarkResult(liveAfter, BuildAutomaticBenchmarkNeutralUiResult(targetAtStartPath));
                }
                else
                {
                    ApplyBenchmarkResult(liveAfter, result);
                }

                if (!string.IsNullOrWhiteSpace(result.UiSummaryLine))
                {
                    AppendLog(
                        new LogLine(
                            DateTimeOffset.Now,
                            $"[INFO] View benchmark details: {result.UiSummaryLine}",
                            LogSeverity.Info,
                            channel: LiveLogChannel.Diagnostics));
                }

                if (result.Succeeded && result.ResultKind == UsbBenchmarkResultKind.Completed)
                {
                    var doneLabel = isAutomatic ? "Automatic USB speed check finished" : "USB benchmark finished";
                    AppendLog(new LogLine(DateTimeOffset.Now, $"[OK] {doneLabel} for {target.RootPath}: write {result.WriteSpeedDisplay}, read {result.ReadSpeedDisplay}.", LogSeverity.Success));
                }
                else if (!isAutomatic)
                {
                    var k = result.GetEffectiveResultKind();
                    if (k is UsbBenchmarkResultKind.CancelledByUser
                        or UsbBenchmarkResultKind.DeviceRemoved
                        or UsbBenchmarkResultKind.TargetChanged
                        or UsbBenchmarkResultKind.CancelledByHost)
                    {
                        LogManualBenchmarkCancellation(targetAtStartPath, k);
                    }
                    else if (result.Summary.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                             result.Status.Contains("failed", StringComparison.OrdinalIgnoreCase))
                    {
                        AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] USB benchmark could not complete for {target.RootPath}.", LogSeverity.Warning));
                    }
                }
                else if (isAutomatic &&
                         (result.Summary.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                          result.Status.Contains("failed", StringComparison.OrdinalIgnoreCase)))
                {
                    AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Automatic USB speed check could not complete for {target.RootPath}.", LogSeverity.Warning));
                }
            }
            catch (OperationCanceledException)
            {
                var liveAfter = TryGetUsbTargetByRootPath(targetAtStartPath) ?? target;
                if (isAutomatic)
                {
                    AppendLog(new LogLine(
                        DateTimeOffset.Now,
                        "[INFO] Benchmark skipped: device still settling.",
                        LogSeverity.Info,
                        channel: LiveLogChannel.Diagnostics));
                    ApplyBenchmarkResult(liveAfter, BuildAutomaticBenchmarkNeutralUiResult(targetAtStartPath));
                }
                else
                {
                    var interruptKind = ClassifyBenchmarkCancellation(targetAtStartPath, isAutomatic: false);
                    var cooperative = BuildManualBenchmarkCooperativeCancelResult(
                        interruptKind,
                        Guid.NewGuid(),
                        DateTimeOffset.UtcNow,
                        identityAtStart.TopologyFingerprint);
                    ApplyBenchmarkResult(liveAfter, cooperative);
                    if (!string.IsNullOrWhiteSpace(cooperative.UiSummaryLine))
                    {
                        AppendLog(
                            new LogLine(
                                DateTimeOffset.Now,
                                $"[INFO] View benchmark details: {cooperative.UiSummaryLine}",
                                LogSeverity.Info,
                                channel: LiveLogChannel.Diagnostics));
                    }

                    LogManualBenchmarkCancellation(targetAtStartPath, interruptKind);
                }
            }
            catch (Exception exception)
            {
                var liveAfter = TryGetUsbTargetByRootPath(targetAtStartPath) ?? target;
                var nowE = DateTimeOffset.UtcNow;
                ApplyBenchmarkResult(liveAfter, new UsbBenchmarkResult
                {
                    Succeeded = false,
                    Status = "Failed",
                    Summary = "Benchmark could not complete",
                    Details = exception.Message,
                    ReadSpeedDisplay = "Could not complete",
                    WriteSpeedDisplay = "Could not complete",
                    LastTestedAt = nowE,
                    ResultKind = UsbBenchmarkResultKind.IoFailed,
                    CompletedAtUtc = nowE,
                    TargetTopologyFingerprint = identityAtStart.TopologyFingerprint,
                    UiSummaryLine = UsbBenchmarkUiMessages.BuildUiSummary(
                        UsbBenchmarkResultKind.IoFailed,
                        0,
                        0,
                        "Unexpected error.")
                });
                var failLabel = isAutomatic ? "Automatic USB speed check could not complete" : "USB benchmark could not complete";
                AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] {failLabel} for {target.RootPath}.", LogSeverity.Warning));
            }
        }
        finally
        {
            if (ownedCts is not null)
            {
                if (isAutomatic && ReferenceEquals(_autoUsbBenchmarkCts, ownedCts))
                {
                    try
                    {
                        _autoUsbBenchmarkCts.Dispose();
                    }
                    catch
                    {
                        // ignored
                    }

                    _autoUsbBenchmarkCts = null;
                }
                else if (!isAutomatic && ReferenceEquals(_manualUsbBenchmarkCts, ownedCts))
                {
                    try
                    {
                        _manualUsbBenchmarkCts.Dispose();
                    }
                    catch
                    {
                        // ignored
                    }

                    _manualUsbBenchmarkCts = null;
                }
            }

            if (!isAutomatic)
            {
                _usbAutomaticBenchmarkPolicy.TouchCooldown(targetAtStartPath);
            }

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

        var requiresUsbBuilderGate = action is ScriptActionType.VerifyBackend
            or ScriptActionType.RevalidateManagedDownloads
            or ScriptActionType.SetupUsb
            or ScriptActionType.UpdateUsb
            or ScriptActionType.RenameUsb;

        using var gate = requiresUsbBuilderGate
            ? await EnterUsbBuilderActionGateAsync(request.DisplayName).ConfigureAwait(true)
            : null;

        ClearLogs();
        LastCommandText = $"{request.DisplayName} -> {Path.GetFileName(request.ScriptPath ?? "inline command")}";
        _lastCommandStartedAt = DateTimeOffset.Now;
        _lastCommandFinishedAt = null;
        _lastCommandExitCode = null;
        LastCommandStatusText = "Running";
        LastCommandSummaryText = "Command started.";
        OnPropertyChanged(nameof(LastCommandNameText));
        OnPropertyChanged(nameof(LastCommandToolText));
        OnPropertyChanged(nameof(LastCommandStartedText));
        OnPropertyChanged(nameof(LastCommandFinishedText));
        OnPropertyChanged(nameof(LastCommandDurationText));
        OnPropertyChanged(nameof(LastCommandExitCodeText));

        var startedAt = DateTimeOffset.Now;
        _usbManagedHeartbeatPhase = UsbManagedHeartbeatPhase.Unknown;
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
            var phaseTimer = Stopwatch.StartNew();
            var effectiveRequest = WithOptionalManagedDownloadHeartbeat(request);
            var runResult = await _powerShellRunnerService.RunAsync(effectiveRequest, AppendLog);
            var runMs = phaseTimer.ElapsedMilliseconds;
            var parsed = _scriptStatusParser.Parse(action, request.DisplayName, runResult);

            await LoadManagedSummaryAsync();
            var managedMs = phaseTimer.ElapsedMilliseconds;
            RefreshManagedDownloadRunArtifactFromSelectedUsb();
            var artifactMs = phaseTimer.ElapsedMilliseconds;
            await RefreshVentoyStatusAsync();
            var ventoyMs = phaseTimer.ElapsedMilliseconds;
            AppendLog(new LogLine(
                DateTimeOffset.Now,
                $"[INFO] {request.DisplayName} timing: full verify/script={runMs}ms, manifest load={Math.Max(0, managedMs - runMs)}ms, UI update={Math.Max(0, artifactMs - managedMs)}ms, backend version check/ventoy status={Math.Max(0, ventoyMs - artifactMs)}ms",
                LogSeverity.Info,
                channel: LiveLogChannel.Diagnostics));

            if (parsed.Succeeded)
            {
                SetStatus(
                    parsed.Summary,
                    parsed.Details,
                    parsed.HasWarnings ? WarningBackground : ReadyBackground,
                    parsed.HasWarnings ? WarningBorder : ReadyBorder,
                    parsed.HasWarnings ? WarningForeground : ReadyForeground);
                AppendLifecycleComplete(request.DisplayName, startedAt);
                _lastCommandExitCode = 0;
                LastCommandStatusText = parsed.HasWarnings ? "Completed with warnings" : "Completed";
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
                _lastCommandExitCode = 1;
                LastCommandStatusText = "Failed";
            }

            _lastCommandFinishedAt = DateTimeOffset.Now;
            LastCommandSummaryText = parsed.Summary;
            OnPropertyChanged(nameof(LastCommandFinishedText));
            OnPropertyChanged(nameof(LastCommandDurationText));
            OnPropertyChanged(nameof(LastCommandExitCodeText));

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
            _lastCommandFinishedAt = DateTimeOffset.Now;
            _lastCommandExitCode = -1;
            LastCommandStatusText = "Failed to start";
            LastCommandSummaryText = exception.Message;
            OnPropertyChanged(nameof(LastCommandFinishedText));
            OnPropertyChanged(nameof(LastCommandDurationText));
            OnPropertyChanged(nameof(LastCommandExitCodeText));
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
        var reportPath = GetSystemIntelligenceJsonPath();
        if (!File.Exists(reportPath))
        {
            SystemIntelligenceStaleBannerText = string.Empty;
            SystemIntelligenceAutomationLineText = string.Empty;
            SystemIntelligenceWarningReasonText = "Warning reason: waiting for first scan.";
            SystemIntelligenceScanStatusText = "Scan status: Not scanned";
            SystemIntelligenceHealthStatusText = "Health status: Unknown";
            SystemIntelligenceWindowsReadinessText = "Windows readiness: Needs verification";
            SystemIntelligenceReportSafePathText = @"Runtime\reports\system-intelligence-latest.json";
            SystemIntelligenceReportPathText = "Report: not generated yet.";
            SystemIntelligenceNetworkTechnicalDetailsText = "Technical network details are hidden.";
            SystemIntelligenceNextActions.Clear();
            SystemIntelligenceNextActions.Add("Run Scan to collect a baseline hardware/security profile.");
            SystemIntelligenceNextActions.Add("Use Run Elevated Scan if deeper TPM/Secure Boot/storage detail is needed.");
            RefreshCopilotContextText();
            RefreshKyraQuickPromptVisibilities();
            return;
        }

        SystemIntelligenceAutomationMerger.TryMerge(reportPath);

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            var root = document.RootElement;
            if (root.TryGetProperty("forgerAutomation", out var autoLine) &&
                autoLine.TryGetProperty("summaryLine", out var autoSummary) &&
                autoSummary.ValueKind == JsonValueKind.String)
            {
                var autoText = autoSummary.GetString();
                SystemIntelligenceAutomationLineText = string.IsNullOrWhiteSpace(autoText)
                    ? string.Empty
                    : NormalizeSystemIntelligenceAutomationLine(autoText.Trim());
            }
            else
            {
                SystemIntelligenceAutomationLineText = string.Empty;
            }

            var overallStatus = GetJsonString(root, "overallStatus", "UNKNOWN");
            SystemIntelligenceStatusText = overallStatus;
            SystemIntelligenceScanStatusText = $"Scan status: {GetJsonString(root, "scanMode", "Standard")} scan READY";
            SystemIntelligenceHealthStatusText = $"Health status: {MapHealthStatusLabel(root, overallStatus)}";
            SystemIntelligenceWindowsReadinessText = BuildWindowsReadinessSummary(root);
            SystemIntelligenceLastScanText = $"Last scan: {FormatGeneratedUtc(GetJsonString(root, "generatedUtc", string.Empty))}";
            SystemIntelligenceStaleBannerText = string.Empty;
            var generatedRaw = GetJsonString(root, "generatedUtc", string.Empty);
            if (DateTime.TryParse(
                    generatedRaw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var generatedUtc) &&
                DateTime.UtcNow - generatedUtc > TimeSpan.FromDays(7))
            {
                SystemIntelligenceStaleBannerText =
                    "This system scan is more than 7 days old. Run System Scan to refresh hardware context, then revisit summaries and Kyra.";
            }
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
                var tpm = FormatTpmForUi(summary, GetJsonProviderDisplay(summary, "tpmInfo", $"Present {FormatNullableBool(GetJsonNullableBool(summary, "tpmPresent"))}, Ready {FormatNullableBool(GetJsonNullableBool(summary, "tpmReady"))}"));
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
                    $"Service / asset identifiers: not shown in ForgerEMS (privacy).{Environment.NewLine}" +
                    $"Windows: {os} (build {osBuild}){Environment.NewLine}" +
                    $"License channel (no product keys shown): {licenseChannel}{Environment.NewLine}" +
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
            SystemIntelligenceNetworkText = BuildNetworkSummaryCompact(root);
            SystemIntelligenceNetworkTechnicalDetailsText = BuildNetworkTechnicalDetails(root);
            SystemIntelligenceSecurityText = BuildSecuritySummary(root);
            SystemIntelligenceStorageCardText = SystemIntelligenceDiskHealthText;
            SystemIntelligenceBatteryCardText = SystemIntelligenceBatteryText;
            SystemIntelligenceNetworkCardText = SystemIntelligenceNetworkText;
            SystemIntelligenceSecurityCardText = SystemIntelligenceSecurityText;
            SystemIntelligenceFlipValueCardText = BuildFlipValueSummary(root);
            SystemIntelligenceDeviceFitCardText = BuildDeviceFitSummary(root);
            SystemIntelligenceHardwareXrayCardText = BuildHardwareXraySummary(root);
            SystemIntelligenceWarningReasonText = BuildSystemIntelligenceWarningReason(root, UnifiedDiagnosticsSummaryText);
            var scanMode = GetJsonString(root, "scanMode", "Standard");
            var permissionLimitedProviders = CountOptionalProviderStatuses(root, "PermissionRequired");
            SystemIntelligenceScanModeHintText = permissionLimitedProviders > 0
                ? $"Scan mode: {scanMode}. Some deep hardware/security details are permission-limited. Run Elevated Scan for additional detail."
                : $"Scan mode: {scanMode}. Standard scan is safe non-admin scan; Elevated scan unlocks extra detail when needed.";
            SystemIntelligenceReportSafePathText = @"Runtime\reports\system-intelligence-latest.json";
            SystemIntelligenceReportPathText = "Report available";
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

            SystemIntelligenceNextActions.Clear();
            foreach (var action in BuildSystemIntelligenceTopActions(root, SystemIntelligenceRecommendations))
            {
                SystemIntelligenceNextActions.Add(action);
            }

            RefreshKyraQuickPromptVisibilities();
        }
        catch (Exception exception)
        {
            SystemIntelligenceAutomationLineText = string.Empty;
            SystemIntelligenceStatusText = "Needs attention";
            SystemIntelligenceWarningReasonText = "Warning: scan report needs regeneration.";
            SystemIntelligenceScanStatusText = "Scan status: Needs attention";
            SystemIntelligenceHealthStatusText = "Health status: Warning";
            SystemIntelligenceWindowsReadinessText = "Windows readiness: Needs verification";
            SystemIntelligenceSummaryText =
                "The saved system report could not be read. Run System Scan to generate a fresh report.";
            SystemIntelligenceReportSafePathText = @"Runtime\reports\system-intelligence-latest.json";
            SystemIntelligenceReportPathText = "Report needs attention (parse error).";
            SystemIntelligenceNetworkTechnicalDetailsText = "Technical network details are unavailable until the report is rebuilt.";
            SystemIntelligenceRecommendations.Clear();
            SystemIntelligenceRecommendations.Add("Run System Scan to replace or rebuild the report file.");
            SystemIntelligenceNextActions.Clear();
            SystemIntelligenceNextActions.Add("Run Scan to regenerate the System Intelligence report.");
            SystemIntelligenceNextActions.Add("If TPM/Secure Boot remain unknown, run Elevated Scan.");
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
            var reportTargetRoot = GetJsonString(root, "targetRoot", string.Empty);
            var selectedRoot = SelectedUsbTarget?.RootPath ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(selectedRoot) &&
                !string.IsNullOrWhiteSpace(reportTargetRoot) &&
                !UsbRootPathsEqual(selectedRoot, reportTargetRoot))
            {
                ToolkitStatusText = "Needs refresh";
                ToolkitHealthVerdictText = $"Toolkit report was cached for {reportTargetRoot}. Click Refresh Toolkit for {selectedRoot}.";
                ToolkitReportPathText = $"Report: stale for selected target ({reportPath})";
                ToolkitHealthItems.Clear();
                _allToolkitHealthItems.Clear();
                return;
            }

            var installed = 0;
            var missing = 0;
            var updates = 0;
            var failed = 0;
            var pending = 0;
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
                pending = GetJsonInt(summary, "verificationPending");
                manual = GetJsonInt(summary, "manual");
                placeholder = GetJsonInt(summary, "placeholder");
                skipped = GetJsonInt(summary, "skipped");
                unknown = GetJsonInt(summary, "unknown");
            }

            ToolkitInstalledCountText = $"Managed Ready: {installed}";
            ToolkitMissingCountText = $"Managed Missing: {missing}";
            ToolkitUpdatesCountText = $"Managed updates available: {updates}";
            ToolkitFailedCountText = $"Verification issues: {failed}";
            ToolkitManualCountText = $"Manual / Info items: {manual}";
            ToolkitPlaceholderCountText = $"Skipped/Placeholder {skipped + placeholder}";
            var healthVerdict = GetJsonString(root, "healthVerdict", "UNKNOWN");
            ToolkitLastScanText = $"Last scan: {FormatGeneratedUtc(GetJsonString(root, "generatedUtc", string.Empty))} | Target: {reportTargetRoot}";
            ToolkitManualExplanationText = GetJsonString(root, "manualItemsExplanation", ToolkitManualExplanationText);

            var functionalHealthy = missing == 0 && failed == 0 && updates == 0;
            var verdictUpper = healthVerdict.ToUpperInvariant();
            if (functionalHealthy && verdictUpper.Contains("MANUAL", StringComparison.Ordinal))
            {
                ToolkitHealthVerdictText =
                    "Toolkit usable — manual/info items available (expected: download pages, licensed tools, or gated downloads ForgerEMS does not auto-fetch).";
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
                    var resolvedAbsolute = GetJsonString(item, "resolvedAbsolutePath", string.Empty);
                    var exists = GetJsonBool(item, "exists");
                    var sizeBytes = GetJsonLong(item, "sizeBytes");
                    var expectedFoundPath = string.IsNullOrWhiteSpace(expectedPath) ? "(path not reported)" : expectedPath;

                    var status = GetJsonString(item, "status", "UNKNOWN");
                    var type = GetJsonString(item, "type", "UNKNOWN");
                    var verification = GetJsonString(item, "verification", string.Empty);
                    var normalized = ToolkitDisplayClassification.BuildNormalizedLabel(status, type, verification);
                    _allToolkitHealthItems.Add(new ToolkitHealthItemView
                    {
                        Tool = GetJsonString(item, "tool", "Unknown tool"),
                        Category = GetJsonString(item, "category", "General"),
                        Status = status,
                        Type = type,
                        ExpectedPath = expectedPath,
                        ResolvedExpectedPath = string.IsNullOrWhiteSpace(resolvedAbsolute) ? expectedPath : resolvedAbsolute,
                        ExpectedFoundPath = expectedFoundPath,
                        MatchedPath = matchedPath,
                        Exists = exists,
                        SizeBytes = sizeBytes,
                        Url = GetJsonString(item, "url", string.Empty),
                        ClassificationReason = GetJsonString(item, "classificationReason", string.Empty),
                        Version = GetJsonString(item, "version", "Unknown"),
                        Verification = verification,
                        Recommendation = GetJsonString(item, "recommendation", string.Empty),
                        NormalizedCategoryLabel = normalized
                    });
                }
            }

            var summaryBuckets = _allToolkitHealthItems
                .GroupBy(i => i.NormalizedCategoryLabel)
                .Select(g => $"{g.Key}: {g.Count()}")
                .ToArray();
            ToolkitClassificationSummaryText = summaryBuckets.Length == 0
                ? "Toolkit classification: no items in the last report."
                : "Toolkit classification — " + string.Join("; ", summaryBuckets);

            SelectedToolkitHealthItem = _allToolkitHealthItems.FirstOrDefault();
            ApplyToolkitFilter();
            ToolkitReportPathText = $"Report: {reportPath}";
            IntelligenceLogWriter.Append(
                "toolkit-manager.log",
                $"Toolkit health loaded | ready {installed} | missing {missing} | manual {manual} | verify issues {failed} | pending {pending} | target {reportTargetRoot} | {reportPath}");
        }
        catch (Exception exception)
        {
            ToolkitStatusText = "Needs attention";
            ToolkitReportPathText = $"Report needs attention (parse error): {exception.Message}";
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

    private void InvalidateToolkitHealthForSelectionChange(string? previousRoot, string? newRoot)
    {
        if (string.IsNullOrWhiteSpace(previousRoot) && string.IsNullOrWhiteSpace(newRoot))
        {
            return;
        }

        _allToolkitHealthItems.Clear();
        ToolkitHealthItems.Clear();
        ToolkitClassificationSummaryText = "Toolkit classification: refresh required for selected target.";
        ToolkitHealthVerdictText = "Toolkit health cache invalidated after USB target change. Click Refresh Toolkit.";
        ToolkitReportPathText = "Report: pending refresh for selected target.";
        ToolkitMissingCountText = "Managed Missing: --";
        ToolkitInstalledCountText = "Managed Ready: --";
    }

    private bool ShouldShowToolkitItem(ToolkitHealthItemView item)
    {
        var statusMatches = SelectedToolkitFilter switch
        {
            "Installed" => string.Equals(item.Status, "INSTALLED", StringComparison.OrdinalIgnoreCase),
            "Managed Missing" => string.Equals(item.Status, "MISSING_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(item.Status, "MISSING", StringComparison.OrdinalIgnoreCase),
            "Managed Updates" => string.Equals(item.Status, "UPDATE_AVAILABLE", StringComparison.OrdinalIgnoreCase),
            "Verification Issues" => string.Equals(item.Status, "HASH_FAILED", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(item.Status, "VERIFICATION_PENDING", StringComparison.OrdinalIgnoreCase),
            "Skipped/Placeholder" => string.Equals(item.Status, "SKIPPED", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(item.Status, "PLACEHOLDER", StringComparison.OrdinalIgnoreCase),
            "Manual Required" => string.Equals(item.Status, "MANUAL_REQUIRED", StringComparison.OrdinalIgnoreCase),
            "Manual / Info" => string.Equals(item.Status, "MANUAL_REQUIRED", StringComparison.OrdinalIgnoreCase),
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
            var haystack = $"{item.Tool} {item.Category} {item.StatusDisplayUi} {item.TypeDisplay} {item.ExpectedPath} {item.LocationDisplay} {item.VerificationDisplay} {item.Recommendation}";
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

    private string ResolveSystemIntelligenceScriptForBackend(BackendContext ctx)
    {
        if (string.IsNullOrWhiteSpace(ctx.RootPath))
        {
            return string.Empty;
        }

        var repoModePath = Path.Combine(ctx.RootPath, "backend", "SystemIntelligence", "Invoke-ForgerEMSSystemScan.ps1");
        if (File.Exists(repoModePath))
        {
            return repoModePath;
        }

        return Path.Combine(ctx.RootPath, "SystemIntelligence", "Invoke-ForgerEMSSystemScan.ps1");
    }

    private Task MarshalIntelligenceRefreshAsync()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            RunIntelligenceUiRefresh();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(RunIntelligenceUiRefresh, DispatcherPriority.Background).Task;
    }

    private void RunIntelligenceUiRefresh()
    {
        LoadSystemIntelligenceReport();
        ApplyDiagnosticsFromDisk();
        RefreshUsbIntelligenceFromDisk();
        RefreshCopilotContextText();
        RefreshKyraAssistantPanel();
    }

    private void ApplyDiagnosticsFromDisk()
    {
        var path = Path.Combine(GetRuntimeReportsDirectory(), "diagnostics-latest.json");
        if (!File.Exists(path))
        {
            UnifiedDiagnosticsSummaryText = "Unified diagnostics: report not found yet.";
            DiagnosticsHealthChecklistText =
                "Diagnostics checklist: no unified report on disk yet. Continue using the app; a checklist appears after diagnostics run.";
            DiagnosticsWarningReasonText = "Warning reason: report not found yet.";
            DiagnosticsAppActionStatusText = $"App action status: {CurrentTaskState}";
            DiagnosticsHealthStatusText = "Diagnostics health: Warning";
            DiagnosticsBackendChipText = $"Backend: {BackendDiagnosticText}";
            DiagnosticsUsbChipText = $"USB: {(SelectedUsbTarget?.DisplayName ?? "none")}";
            DiagnosticsSystemChipText = $"System Intelligence: {SystemIntelligenceStatusText}";
            DiagnosticsToolkitChipText = $"Toolkit: {ToolkitStatusText}";
            DiagnosticsKyraChipText = $"Kyra: {CopilotProviderBadgeText}";
            DiagnosticsUpdateChipText = $"Update: {AppUpdateMachineStateDisplay}";
            DiagnosticsActionCenterItems.Clear();
            DiagnosticsActionCenterItems.Add("[Warning] General | Missing diagnostics report | Run Refresh Backend Context | source: Diagnostics");
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var baseSummary = root.TryGetProperty("summaryLine", out var s) && s.ValueKind == JsonValueKind.String
                ? s.GetString() ?? "Unified diagnostics loaded."
                : "Unified diagnostics loaded.";
            DiagnosticsHealthChecklistText = DiagnosticsUiFormatter.BuildHealthChecklist(root, DiagnosticsShowFullDetail);
            DiagnosticsWarningReasonText = DiagnosticsUiFormatter.BuildWarningReason(root);
            var overallSeverity = GetJsonString(root, "overallSeverity", "Unknown");
            DiagnosticsAppActionStatusText = $"App action status: {CurrentTaskState}";
            DiagnosticsHealthStatusText = $"Diagnostics health: {DiagnosticsUiFormatter.FormatSeverityLabel(overallSeverity)}";
            DiagnosticsBackendChipText = $"Backend: {(BackendDiagnosticText.Contains("compatible", StringComparison.OrdinalIgnoreCase) ? "Compatible" : "Problem")}";
            DiagnosticsUsbChipText = $"USB: {(SelectedUsbTarget?.DisplayName ?? "none")}";
            DiagnosticsSystemChipText = $"System Intelligence: {SystemIntelligenceStatusText}";
            DiagnosticsToolkitChipText = $"Toolkit: {ToolkitStatusText}";
            DiagnosticsKyraChipText = $"Kyra: {CopilotProviderBadgeText}";
            DiagnosticsUpdateChipText = $"Update: {AppUpdateMachineStateDisplay}";
            DiagnosticsActionCenterItems.Clear();
            foreach (var action in DiagnosticsUiFormatter.BuildActionCenterItems(root, limit: 5))
            {
                DiagnosticsActionCenterItems.Add($"[{action.Severity}] {action.Category} | {action.Reason} | {action.SuggestedAction} | source: {action.Source}");
            }
            if (DiagnosticsActionCenterItems.Count == 0)
            {
                DiagnosticsActionCenterItems.Add("[Info] General | No high-priority actions detected | Continue normal workflow | source: Diagnostics");
            }

            var usbExtra = new List<string>();
            if (root.TryGetProperty("usb", out var usbDiag) && usbDiag.ValueKind == JsonValueKind.Object)
            {
                if (usbDiag.TryGetProperty("usbProfileKnownPortsCount", out var mpc) && mpc.TryGetInt32(out var n) && n > 0)
                {
                    usbExtra.Add($"USB mapped ports: {n}");
                }

                if (usbDiag.TryGetProperty("usbCurrentTargetRiskSummary", out var rsk) &&
                    rsk.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(rsk.GetString()))
                {
                    usbExtra.Add(UsbIntelligencePanelUiCopy.HumanizeBuilderHintLine(rsk.GetString()!));
                }

                if (usbDiag.TryGetProperty("lastBenchmark", out var lb) &&
                    lb.ValueKind == JsonValueKind.Object &&
                    lb.TryGetProperty("succeeded", out var okLb) &&
                    okLb.ValueKind == JsonValueKind.True &&
                    lb.TryGetProperty("summaryLine", out var sl) &&
                    sl.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(sl.GetString()))
                {
                    usbExtra.Add($"Last benchmark: {sl.GetString()}");
                }

                if (usbDiag.TryGetProperty("usbBestKnownPortSummary", out var bp) &&
                    bp.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(bp.GetString()))
                {
                    usbExtra.Add(UsbIntelligencePanelUiCopy.HumanizeBuilderHintLine(bp.GetString()!));
                }

                if (usbDiag.TryGetProperty("usbSummaryLine", out var usbLine) &&
                    usbLine.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(usbLine.GetString()))
                {
                    usbExtra.Add(UsbIntelligencePanelUiCopy.HumanizeBuilderHintLine(usbLine.GetString()!));
                }
            }

            if (usbExtra.Count > 0)
            {
                UnifiedDiagnosticsSummaryText =
                    baseSummary + Environment.NewLine + "USB Intelligence: " + string.Join(" · ", usbExtra);
            }
            else
            {
                UnifiedDiagnosticsSummaryText = baseSummary;
            }
        }
        catch (Exception exception)
        {
            UnifiedDiagnosticsSummaryText = $"Unified diagnostics: parse error ({exception.Message}).";
            DiagnosticsHealthChecklistText = $"Diagnostics checklist: could not parse report ({exception.Message}).";
            DiagnosticsWarningReasonText = "Warning reason: diagnostics report parse error.";
            DiagnosticsHealthStatusText = "Diagnostics health: Warning";
            DiagnosticsActionCenterItems.Clear();
            DiagnosticsActionCenterItems.Add("[Warning] Diagnostics | Report parse error | Run Refresh Backend Context and retry | source: Diagnostics parser");
        }
    }

    private void RefreshUsbIntelligenceFromDisk()
    {
        var targetLine = FormatUsbIntelligenceTargetLine(SelectedUsbTarget);
        UsbIntelligencePanelTargetDisplay = targetLine;

        var path = Path.Combine(GetRuntimeReportsDirectory(), "usb-intelligence-latest.json");
        if (!File.Exists(path))
        {
            UsbIntelligenceBuilderHintText =
                "USB Intelligence: waiting for a saved report. Select a USB target in USB Builder, then run USB Benchmark or refresh intelligence when available.";
            ResetUsbIntelligencePanelFieldsForMissingReport(targetLine, showBenchmarkHint: true);
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            var state = UsbIntelligenceLatestPanelReader.Parse(json);

            UsbIntelligencePanelTargetDisplay = targetLine;
            UsbIntelligenceDetectedClassDisplay = state.DetectedClassDisplay;
            UsbIntelligenceBenchmarkReadWriteDisplay = state.BenchmarkReadWriteDisplay;
            UsbIntelligenceRecommendationQualityDisplay = state.RecommendationQualityDisplay;
            UsbIntelligenceConfidenceScoreDisplay = state.ConfidenceScoreDisplay;
            UsbIntelligenceConfidenceReasonDisplay = state.ConfidenceReasonDisplay;
            UsbIntelligenceLastBenchmarkTimeDisplay = state.LastBenchmarkTimeDisplay;
            UsbIntelligenceMappingLabelDisplay = state.MappingLabelDisplay;
            UsbIntelligenceBestKnownPortDisplay = FormatBestKnownPortLine(state.BestKnownPortSummary);
            UsbIntelligenceBenchmarkAgeDisplay = FormatBenchmarkAgeLine(state.BenchmarkAgeSummary);
            UsbIntelligenceRunBenchmarkHintDisplay = state.RunBenchmarkRecommendedLine;
            ApplyLocalUsbBenchmarkPromptOverlay();

            UsbIntelligenceBuilderHintText = string.IsNullOrWhiteSpace(state.BuilderSummaryLine)
                ? UsbIntelligencePanelUiCopy.GuidanceIntro
                : state.BuilderSummaryLine;
        }
        catch
        {
            UsbIntelligenceBuilderHintText = "USB Intelligence: could not read the latest topology file.";
            ResetUsbIntelligencePanelFieldsForMissingReport(targetLine, showBenchmarkHint: true);
        }
    }

    private static string FormatUsbIntelligenceTargetLine(UsbTargetInfo? target)
    {
        if (target is null)
        {
            return "No USB target selected — choose a drive in USB Builder first.";
        }

        var letter = string.IsNullOrWhiteSpace(target.DriveLetter) ? "—" : target.DriveLetter.TrimEnd('\\');
        return $"{letter} · {target.LabelDisplay}".Trim();
    }

    private static string FormatBestKnownPortLine(string bestPort) =>
        string.IsNullOrWhiteSpace(bestPort) || string.Equals(bestPort, "—", StringComparison.Ordinal)
            ? UsbIntelligencePanelUiCopy.BestKnownPortPending
            : $"Best port: {bestPort}";

    private static string FormatBenchmarkAgeLine(string age) =>
        string.IsNullOrWhiteSpace(age) || string.Equals(age, "—", StringComparison.Ordinal)
            ? UsbIntelligencePanelUiCopy.NoBenchmarkYet
            : $"Last benchmark age: {age}";

    private void ResetUsbIntelligencePanelFieldsForMissingReport(string targetLine, bool showBenchmarkHint)
    {
        UsbIntelligencePanelTargetDisplay = targetLine;
        UsbIntelligenceDetectedClassDisplay = UsbIntelligencePanelUiCopy.NotMeasuredClass;
        UsbIntelligenceBenchmarkReadWriteDisplay = UsbIntelligencePanelUiCopy.NoBenchmarkYet;
        UsbIntelligenceRecommendationQualityDisplay = UsbIntelligencePanelUiCopy.RunBenchmarkToAnalyze;
        UsbIntelligenceConfidenceScoreDisplay = UsbIntelligencePanelUiCopy.InsufficientConfidence;
        UsbIntelligenceConfidenceReasonDisplay = string.Empty;
        UsbIntelligenceLastBenchmarkTimeDisplay = "—";
        UsbIntelligenceMappingLabelDisplay = UsbIntelligencePanelUiCopy.NoPortLabelYet;
        UsbIntelligenceBestKnownPortDisplay = UsbIntelligencePanelUiCopy.BestKnownPortPending;
        UsbIntelligenceBenchmarkAgeDisplay = UsbIntelligencePanelUiCopy.NoBenchmarkYet;
        UsbIntelligenceRunBenchmarkHintDisplay = showBenchmarkHint ? UsbIntelligencePanelUiCopy.RunBenchmarkRecommended : string.Empty;
        ApplyLocalUsbBenchmarkPromptOverlay();
    }

    /// <summary>
    /// Prefer a clear local prompt when a selectable USB is chosen but this session has no completed benchmark for it.
    /// </summary>
    private void ApplyLocalUsbBenchmarkPromptOverlay()
    {
        var baseline = UsbIntelligenceRunBenchmarkHintDisplay ?? string.Empty;

        if (SelectedUsbTarget is null ||
            !SelectedUsbTarget.IsSelectable ||
            UsbTargetSafety.GetExecutionBlockReason(SelectedUsbTarget) is not null)
        {
            return;
        }

        var status = SelectedUsbTarget.BenchmarkStatus ?? string.Empty;
        if (status.Equals("Testing", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(baseline))
            {
                UsbIntelligenceRunBenchmarkHintDisplay = "USB benchmark is running…";
            }

            return;
        }

        if (!UsbTargetBenchmarkUi.HasSuccessfulMeasuredBenchmark(SelectedUsbTarget))
        {
            UsbIntelligenceRunBenchmarkHintDisplay = UsbIntelligencePanelUiCopy.UsbSelectedNotBenchmarkedPrompt;
        }
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
            .Select(disk =>
            {
                var interfaceType = GetJsonString(disk, "interfaceType", "UNKNOWN");
                var mediaType = HumanizeStorageMediaType(interfaceType, GetJsonString(disk, "mediaType", "UNKNOWN"));
                var temperatureDisplay = NormalizeMetricLabel(GetJsonString(disk, "temperatureDisplay", "Temp: Not exposed"), "Temp");
                var wearDisplay = NormalizeMetricLabel(GetJsonString(disk, "wearDisplay", "Wear: Not exposed"), "Wear");
                var health = BuildDiskHealthPercentText(disk);
                return $"{GetJsonString(disk, "name", "Disk")} | {interfaceType} {mediaType} | {GetJsonString(disk, "size", "UNKNOWN")} | {health} | {temperatureDisplay} | {wearDisplay} ({GetJsonString(disk, "status", "UNKNOWN")})";
            })
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
            .Select(battery => $"{GetJsonString(battery, "name", "Battery")} {GetJsonInt(battery, "estimatedChargeRemaining")}% charge, design {GetJsonString(battery, "designCapacityDisplay", "Not exposed by firmware/Windows")}, full {GetJsonString(battery, "fullChargeCapacityDisplay", "Not exposed by firmware/Windows")}, wear {GetJsonString(battery, "wearDisplay", "Battery wear: Not exposed by firmware/Windows")}, cycles {GetJsonString(battery, "cycleCountDisplay", "Not exposed by firmware/Windows")} (cycle count may be firmware-dependent), AC {FormatNullableBool(GetJsonNullableBool(battery, "acConnected"))} ({GetJsonString(battery, "healthDisplay", GetJsonString(battery, "status", "UNKNOWN"))})")
            .ToArray();
        var notExposed = parts.Any(part => part.Contains("Not exposed", StringComparison.OrdinalIgnoreCase));
        var note = notExposed
            ? " Firmware/Windows did not expose some battery wear/cycle fields; verify with battery report or vendor diagnostics before treating it as failure."
            : string.Empty;
        return $"Battery: {FormatList(parts, "present, details unavailable")}.{note}";
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
        var physicalCount = network.TryGetProperty("physicalAdapters", out var physicalAdapters) && physicalAdapters.ValueKind == JsonValueKind.Array
            ? physicalAdapters.GetArrayLength()
            : CountAdaptersByKind(network, virtualAdapters: false);
        var virtualCount = network.TryGetProperty("virtualAdapters", out var virtualAdapters) && virtualAdapters.ValueKind == JsonValueKind.Array
            ? virtualAdapters.GetArrayLength()
            : CountAdaptersByKind(network, virtualAdapters: true);
        var adapterProperty = physicalAdapters.ValueKind == JsonValueKind.Array
            ? physicalAdapters
            : network.TryGetProperty("adapters", out var allAdapters)
                ? allAdapters
                : default;
        if (adapterProperty.ValueKind != JsonValueKind.Array || adapterProperty.GetArrayLength() == 0)
        {
            return $"Network: {status}. {internet}. {defaultRoute}. {physicalCount} physical / {virtualCount} virtual adapters detected. No active physical adapter detected. {virtualIgnored}.";
        }

        var parts = adapterProperty.EnumerateArray()
            .Where(adapter => !ShouldTreatAdapterAsVirtual(adapter))
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
                var wifi = NormalizeWifiDisplay(GetJsonString(adapter, "wifiDisplay", "Wi-Fi: not connected"));
                var apipa = FormatNullableBool(GetJsonNullableBool(adapter, "apipaDetected"));
                return $"{GetJsonString(adapter, "name", GetJsonString(adapter, "description", "Adapter"))} | {HumanizeNetworkRole(GetJsonString(adapter, "adapterRole", "Physical adapter"))} | IP {ips} | GW {gateways} | DNS {dns} | {wifi} | APIPA {apipa}";
            })
            .Take(3)
            .ToArray();
        var virtualNote = virtualCount > 0
            ? $"{virtualIgnored}; host-only/VPN/virtual adapters are informational unless they are the active internet route"
            : virtualIgnored;
        var adapterSummary = FormatList(parts, "No active physical adapter detected");
        return $"Network: {status}. {internet}. {defaultRoute}. Adapters: {physicalCount} physical / {virtualCount} virtual. Active physical: {adapterSummary}. {virtualNote}.";
    }

    private static string BuildNetworkSummaryCompact(JsonElement root)
    {
        if (!root.TryGetProperty("network", out var network))
        {
            return "Network: UNKNOWN.";
        }

        var status = GetJsonString(network, "status", "UNKNOWN");
        var internet = GetJsonString(network, "internetDisplay", GetJsonBool(network, "internetCheck") ? "Internet: Working" : "Internet: Check failed");
        var defaultRoute = GetJsonProviderDisplay(network, "defaultRoute", "Default route: not detected");
        var physicalCount = network.TryGetProperty("physicalAdapters", out var physicalAdapters) && physicalAdapters.ValueKind == JsonValueKind.Array
            ? physicalAdapters.GetArrayLength()
            : CountAdaptersByKind(network, virtualAdapters: false);
        var virtualCount = network.TryGetProperty("virtualAdapters", out var virtualAdapters) && virtualAdapters.ValueKind == JsonValueKind.Array
            ? virtualAdapters.GetArrayLength()
            : CountAdaptersByKind(network, virtualAdapters: true);
        var dnsState = HasAnyDnsServer(network) ? "configured" : "not reported";
        return $"Network: {status}. {internet}. {defaultRoute}. Adapters: {physicalCount} physical / {virtualCount} virtual. DNS: {dnsState}.";
    }

    private static string BuildNetworkTechnicalDetails(JsonElement root)
    {
        var details = BuildNetworkSummary(root);
        return string.IsNullOrWhiteSpace(details)
            ? "Technical network details unavailable."
            : details;
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
        var summary = root.TryGetProperty("summary", out var summaryElement) ? summaryElement : default;
        var secureBoot = summary.ValueKind != JsonValueKind.Undefined
            ? GetJsonProviderDisplay(summary, "secureBootInfo", "Secure Boot: Unknown")
            : "Secure Boot: Unknown";
        var secureBootStatus = summary.ValueKind != JsonValueKind.Undefined
            ? GetJsonProviderStatus(summary, "secureBootInfo")
            : "UNKNOWN";
        var tpm = summary.ValueKind != JsonValueKind.Undefined
            ? GetJsonProviderDisplay(summary, "tpmInfo", "TPM: Unknown")
            : "TPM: Unknown";
        var tpmStatus = summary.ValueKind != JsonValueKind.Undefined
            ? GetJsonProviderStatus(summary, "tpmInfo")
            : "UNKNOWN";
        var bitLocker = security.TryGetProperty("bitLockerSummary", out _)
            ? GetJsonProviderDisplay(security, "bitLockerSummary", "unavailable")
            : security.TryGetProperty("bitLockerVolumes", out var bitLockerVolumes) && bitLockerVolumes.ValueKind == JsonValueKind.Array
            ? FormatList(bitLockerVolumes.EnumerateArray().Select(volume => $"{GetJsonString(volume, "mountPoint", "Volume")} {GetJsonString(volume, "protectionStatus", "UNKNOWN")}"), "unavailable")
            : "unavailable";
        var verificationNote = IsUnknownProviderStatus(tpmStatus) || IsUnknownProviderStatus(secureBootStatus)
            ? " Unknown firmware fields are verification items, not confirmed failures."
            : string.Empty;
        var tpmText = IsUnknownProviderStatus(tpmStatus) || ShouldTreatTpmAsVerificationItem(summary, tpm)
            ? "Not reported by Windows scan. Verify BIOS/UEFI TPM/PTT setting."
            : tpm;
        var secureBootText = IsUnknownProviderStatus(secureBootStatus)
            ? "Unknown — requires admin or unavailable."
            : secureBoot;
        var bitLockerText = bitLocker.Equals("unavailable", StringComparison.OrdinalIgnoreCase) ||
                            bitLocker.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
                            bitLocker.Contains("reason not reported", StringComparison.OrdinalIgnoreCase)
            ? "Unavailable — Windows did not report a reason."
            : bitLocker.Replace("Unavailable -", "Unavailable —", StringComparison.Ordinal);
        return $"Security: {status}. Defender AV: {FormatNullableBool(avEnabled)}. Real-time: {FormatNullableBool(realtime)}. Firewall: {FormatNullableBool(firewall)}. TPM: {tpmText}. Secure Boot: {secureBootText}. Registered AV: {FormatList(products, "none detected")}. BitLocker: {bitLockerText}.{verificationNote}";
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
        var locationBasis = GetJsonString(flipValue, "locationBasis", "Location not configured; national/offline heuristic basis");
        var title = GetJsonString(flipValue, "suggestedListingTitle", "UNKNOWN");
        var missingInfo = flipValue.TryGetProperty("missingInfoNeeded", out var missingInfoArray) && missingInfoArray.ValueKind == JsonValueKind.Array
            ? FormatList(missingInfoArray.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Take(3), "none")
            : "none";
        var apiStatus = providerStatus.Contains("not configured", StringComparison.OrdinalIgnoreCase)
            ? "Offline estimate only"
            : "Comps provider configured";
        var rawDrivers = flipValue.TryGetProperty("valueDrivers", out var driverArray) && driverArray.ValueKind == JsonValueKind.Array
            ? FormatList(driverArray.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Take(3), "none")
            : "none";
        var rawReducers = flipValue.TryGetProperty("valueReducers", out var reducerArray) && reducerArray.ValueKind == JsonValueKind.Array
            ? FormatList(reducerArray.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Take(3), "none")
            : "none";
        var drivers = BuildEvidenceBasedFlipDrivers(root, rawDrivers);
        var reducers = BuildEvidenceBasedFlipReducers(root, rawReducers);

        return
            $"Estimate: {range} (list {list}; quick-sale {quick}; parts/repair {parts}){Environment.NewLine}" +
            $"Confidence: {confidence} | Source: {estimateType} | API: {apiStatus}{Environment.NewLine}" +
            $"Top drivers: {drivers}{Environment.NewLine}" +
            $"Top reducers: {reducers}{Environment.NewLine}" +
            $"Missing before confident pricing: {missingInfo}{Environment.NewLine}" +
            "Improve estimate with condition details: cosmetic condition, screen condition, keyboard/trackpad condition, charger included, battery replacement status." + Environment.NewLine +
            $"Listing title: {title}";
    }

    private static string BuildDeviceFitSummary(JsonElement root)
    {
        if (root.TryGetProperty("deviceFit", out var deviceFit) && deviceFit.ValueKind == JsonValueKind.Object)
        {
            var primary = GetJsonString(deviceFit, "primaryFit", "Unknown / needs scan");
            var machineClass = GetJsonString(deviceFit, "machineClass", "Unknown / Mixed");
            var confidence = GetJsonString(deviceFit, "confidence", "Low");
            var strongFits = deviceFit.TryGetProperty("strongFits", out var strongArray) && strongArray.ValueKind == JsonValueKind.Array
                ? FormatList(strongArray.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Take(5), "needs more data")
                : "needs more data";
            var weakFits = deviceFit.TryGetProperty("weakFits", out var weakArray) && weakArray.ValueKind == JsonValueKind.Array
                ? FormatList(weakArray.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Take(4), "none obvious")
                : "none obvious";
            var examples = deviceFit.TryGetProperty("exampleWorkloads", out var exampleArray) && exampleArray.ValueKind == JsonValueKind.Array
                ? FormatList(exampleArray.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Take(6), "no workload examples available")
                : "no workload examples available";
            var upgrades = deviceFit.TryGetProperty("upgradeFirstAdvice", out var upgradeArray) && upgradeArray.ValueKind == JsonValueKind.Array
                ? FormatList(upgradeArray.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Take(3), "verify condition and drivers")
                : "verify condition and drivers";
            var listing = GetJsonString(deviceFit, "listingPositioning", "Market around verified specs and disclose unknowns.");

            return
                $"Primary fit: {primary}{Environment.NewLine}" +
                $"Machine class: {machineClass}{Environment.NewLine}" +
                $"Confidence: {confidence}{Environment.NewLine}" +
                $"Strong fits: {strongFits}{Environment.NewLine}" +
                $"Watch-outs: {BuildEvidenceBasedDeviceFitWatchOuts(root, weakFits)}{Environment.NewLine}" +
                $"Good for: {BuildGroupedExamples(deviceFit, "good")}{Environment.NewLine}" +
                $"Games: {BuildGroupedExamples(deviceFit, "games")}{Environment.NewLine}" +
                $"Creator/dev: {BuildGroupedExamples(deviceFit, "creator")}{Environment.NewLine}" +
                $"Not ideal for: {BuildGroupedExamples(deviceFit, "notideal")}{Environment.NewLine}" +
                $"Upgrade/listing advice: {upgrades}{Environment.NewLine}" +
                $"Listing angle: {listing}";
        }

        try
        {
            var profile = SystemProfileMapper.FromJson(root);
            return DeviceFitEngine.FormatCard(new DeviceFitEngine().Evaluate(profile));
        }
        catch
        {
            return "Best Use / Device Fit: run System Intelligence again to generate fit guidance.";
        }
    }

    private static string BuildHardwareXraySummary(JsonElement root)
    {
        if (root.TryGetProperty("machineClass", out var machineClass) && machineClass.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("sensorMatrix", out var sensorMatrix) && sensorMatrix.ValueKind == JsonValueKind.Object)
        {
            var primary = GetJsonString(machineClass, "primaryClass", "Unknown / Mixed");
            var confidence = GetJsonString(machineClass, "confidence", "Low");
            var note = GetJsonString(machineClass, "technicianNote", "Signals are incomplete; verify manually.");
            var secondary = machineClass.TryGetProperty("secondaryClasses", out var secondaryArray) && secondaryArray.ValueKind == JsonValueKind.Array
                ? FormatList(secondaryArray.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Take(3), "none")
                : "none";
            var coverage = GetJsonString(sensorMatrix, "coverageSummary", string.Empty);
            if (string.IsNullOrWhiteSpace(coverage) && sensorMatrix.TryGetProperty("groups", out var groups) && groups.ValueKind == JsonValueKind.Array)
            {
                coverage = FormatList(groups.EnumerateArray().Select(group =>
                    $"{GetJsonString(group, "category", "Group")}: {GetJsonString(group, "knownFields", "0")}/{GetJsonString(group, "totalFields", "0")} fields known"), "No coverage data");
            }
            coverage = ApplyUsbCoverageFromIntelligence(root, coverage);

            var live = FindLiveSensorNames(sensorMatrix);
            var deepNote = GetJsonString(sensorMatrix, "deepSensorModeNote", "Some sensors require admin access, firmware support, vendor drivers, or optional reviewed providers.");
            var limited = BuildLimitedSensorCompactSummary(sensorMatrix);
            var storage = BuildStorageSensorCompactSummary(sensorMatrix);
            var usb = BuildUsbSensorCompactSummary(root, sensorMatrix);
            var optionalProviders = BuildOptionalProviderStatusSummary(root);

            return
                $"Machine: {primary} ({confidence}){Environment.NewLine}" +
                (secondary.Equals("none", StringComparison.OrdinalIgnoreCase) ? string.Empty : $"Secondary: {secondary}{Environment.NewLine}") +
                $"Coverage: {CompactCoverageSummary(coverage)}{Environment.NewLine}" +
                $"Live: {live}{Environment.NewLine}" +
                $"Sensor Providers: {BuildSensorProviderCompactSummary(sensorMatrix)}{Environment.NewLine}" +
                $"Deep Sensor Mode: {BuildDeepSensorModeCompactSummary(root, sensorMatrix)}{Environment.NewLine}" +
                $"Inventory: {GetInventoryDataSummary(sensorMatrix)}{Environment.NewLine}" +
                $"USB: {usb}{Environment.NewLine}" +
                $"Optional providers: {optionalProviders}{Environment.NewLine}" +
                $"Limited: {limited}{Environment.NewLine}" +
                $"Storage: {storage}{Environment.NewLine}" +
                "Guide: Unknown lowers confidence; NotExposed means firmware/driver/permission limit; failure requires explicit evidence." + Environment.NewLine +
                $"Note: {note} {deepNote}";
        }

        try
        {
            var profile = SystemProfileMapper.FromJson(root);
            var classification = MachineClassifier.Classify(profile);
            var sensors = SensorMatrixBuilder.Build(profile);
            return
                $"Machine class: {classification.PrimaryClass} ({classification.Confidence}){Environment.NewLine}" +
                $"Secondary: {FormatList(classification.SecondaryClasses.Take(3), "none")}{Environment.NewLine}" +
                $"Sensor coverage: {CompactCoverageSummary(sensors.CoverageSummary)}{Environment.NewLine}" +
                $"Live sensors: {FormatList(sensors.Groups.SelectMany(g => g.Readings).Where(r => r.IsLive).Select(r => r.Name).Take(5), "none exposed in safe scan")}{Environment.NewLine}" +
                $"Sensor Providers: {BuildSensorProviderCompactSummary(sensors)}{Environment.NewLine}" +
                $"Deep Sensor Mode: {sensors.DeepSensorMode.Mode} via {sensors.DeepSensorMode.DisplaySource}; Safety: read-only; no fan/voltage/clock/firmware control.{Environment.NewLine}" +
                $"Limited: CPU/GPU temps, fan RPM, package power may require deep/vendor sensor support.{Environment.NewLine}" +
                $"Note: {classification.TechnicianNote} {sensors.DeepSensorModeNote}";
        }
        catch
        {
            return "Hardware X-Ray: run System Intelligence again to generate machine class and sensor coverage.";
        }
    }

    private static string FindLiveSensorNames(JsonElement sensorMatrix)
    {
        if (!sensorMatrix.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array)
        {
            return "none exposed in safe scan";
        }

        var names = new List<string>();
        foreach (var reading in EnumerateSensorReadings(groups))
        {
            if (reading.TryGetProperty("isLive", out var live) &&
                live.ValueKind == JsonValueKind.True)
            {
                var name = GetJsonString(reading, "name", string.Empty);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }
        }

        return FormatList(names.Take(5), "none exposed in safe scan");
    }

    private static string BuildDeepSensorModeCompactSummary(JsonElement root, JsonElement sensorMatrix)
    {
        JsonElement modeElement;
        if (root.TryGetProperty("deepSensorMode", out modeElement) && modeElement.ValueKind == JsonValueKind.Object)
        {
            var value = GetJsonString(modeElement, "value", "Off");
            var source = FormatDeepSensorSource(GetJsonString(modeElement, "source", "BuiltInDefault"));
            return $"{value} via {source}; Safety: read-only; no fan/voltage/clock/firmware control.";
        }

        if (sensorMatrix.TryGetProperty("deepSensorMode", out modeElement) && modeElement.ValueKind == JsonValueKind.Object)
        {
            var value = GetJsonString(modeElement, "mode", "Off");
            var source = FormatDeepSensorSource(GetJsonString(modeElement, "source", "BuiltInDefault"));
            return $"{value} via {source}; Safety: read-only; no fan/voltage/clock/firmware control.";
        }

        var resolution = ForgerEmsEnvironmentConfiguration.DeepSensorModeResolution;
        return $"{resolution.Mode} via {resolution.DisplaySource}; Safety: read-only; no fan/voltage/clock/firmware control.";
    }

    private static string FormatDeepSensorSource(string source) => source switch
    {
        "Environment" => "environment variable",
        "UserSetting" => "user setting",
        "InstallerDefault" => "installer default",
        "BuiltInDefault" => "built-in default",
        _ => string.IsNullOrWhiteSpace(source) ? "built-in default" : source
    };

    private static string BuildSensorProviderCompactSummary(JsonElement sensorMatrix)
    {
        if (!sensorMatrix.TryGetProperty("sensorProviders", out var providers) || providers.ValueKind != JsonValueKind.Array)
        {
            return "Windows Native: Active; LibreHardwareMonitor: Off; Admin Bridge: Off; Driver Provider: Not included";
        }

        var rows = new List<string>();
        foreach (var provider in providers.EnumerateArray())
        {
            var name = GetJsonString(provider, "providerName", "Provider");
            var enabled = GetJsonBool(provider, "isEnabled");
            var bundled = GetJsonBool(provider, "isBundled");
            var mode = GetJsonString(provider, "runtimeMode", "Disabled");
            var failure = GetJsonString(provider, "failureReason", string.Empty);
            var label = name switch
            {
                var n when n.Contains("Windows", StringComparison.OrdinalIgnoreCase) => "Windows Native",
                var n when n.Contains("LibreHardwareMonitor", StringComparison.OrdinalIgnoreCase) => "LibreHardwareMonitor",
                var n when n.Contains("Deep", StringComparison.OrdinalIgnoreCase) => "Deep Sensor Provider",
                var n when n.Contains("Admin", StringComparison.OrdinalIgnoreCase) => "Admin Bridge",
                var n when n.Contains("Driver", StringComparison.OrdinalIgnoreCase) => "Driver Provider",
                _ => name
            };
            var status = enabled
                ? label.Equals("LibreHardwareMonitor", StringComparison.OrdinalIgnoreCase) ? "Active read-only" : "Active"
                : bundled
                    ? "Bundled but disabled"
                    : label.Equals("Driver Provider", StringComparison.OrdinalIgnoreCase)
                        ? "Not included"
                        : "Off";
            if (!enabled &&
                (label.Equals("Deep Sensor Provider", StringComparison.OrdinalIgnoreCase) ||
                 label.Equals("LibreHardwareMonitor", StringComparison.OrdinalIgnoreCase)) &&
                failure.Contains("not packaged", StringComparison.OrdinalIgnoreCase))
            {
                status = "Not packaged";
            }

            if (!enabled && mode.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
            {
                rows.Add($"{label}: {status}");
            }
            else
            {
                rows.Add($"{label}: {status}");
            }
        }

        return FormatList(rows.Take(4), "Windows Native: Active; LibreHardwareMonitor: Off; Admin Bridge: Off; Driver Provider: Not included");
    }

    private static string BuildSensorProviderCompactSummary(SensorMatrixResult sensors)
    {
        var rows = sensors.SensorProviders.Select(provider =>
        {
            var label = provider.ProviderName switch
            {
                var n when n.Contains("Windows", StringComparison.OrdinalIgnoreCase) => "Windows Native",
                var n when n.Contains("LibreHardwareMonitor", StringComparison.OrdinalIgnoreCase) => "LibreHardwareMonitor",
                var n when n.Contains("Deep", StringComparison.OrdinalIgnoreCase) => "Deep Sensor Provider",
                var n when n.Contains("Admin", StringComparison.OrdinalIgnoreCase) => "Admin Bridge",
                var n when n.Contains("Driver", StringComparison.OrdinalIgnoreCase) => "Driver Provider",
                _ => provider.ProviderName
            };
            var status = provider.IsEnabled
                ? label.Equals("LibreHardwareMonitor", StringComparison.OrdinalIgnoreCase) ? "Active read-only" : "Active"
                : provider.IsBundled
                    ? "Bundled but disabled"
                    : label.Equals("Driver Provider", StringComparison.OrdinalIgnoreCase)
                        ? "Not included"
                        : "Off";
            if (!provider.IsEnabled &&
                (label.Equals("Deep Sensor Provider", StringComparison.OrdinalIgnoreCase) ||
                 label.Equals("LibreHardwareMonitor", StringComparison.OrdinalIgnoreCase)) &&
                provider.FailureReason.Contains("not packaged", StringComparison.OrdinalIgnoreCase))
            {
                status = "Not packaged";
            }

            return $"{label}: {status}";
        });
        return FormatList(rows.Take(4), "Windows Native: Active; LibreHardwareMonitor: Off; Admin Bridge: Off; Driver Provider: Not included");
    }

    private static string BuildOptionalProviderStatusSummary(JsonElement root)
    {
        if (!root.TryGetProperty("optionalProviderStatus", out var providers) || providers.ValueKind != JsonValueKind.Array)
        {
            return "Available: n/a; Permission required: n/a; Not exposed: n/a; Provider unavailable: n/a";
        }

        var available = 0;
        var permissionRequired = 0;
        var notExposed = 0;
        var providerUnavailable = 0;
        foreach (var provider in providers.EnumerateArray())
        {
            switch (GetJsonString(provider, "status", string.Empty))
            {
                case "Ready":
                case "READY":
                    available++;
                    break;
                case "PermissionRequired":
                    permissionRequired++;
                    break;
                case "NotExposed":
                    notExposed++;
                    break;
                case "ProviderUnavailable":
                case "Timeout":
                    providerUnavailable++;
                    break;
            }
        }

        return $"Available: {available}; Permission required: {permissionRequired}; Not exposed by firmware/driver: {notExposed}; Provider unavailable: {providerUnavailable}. Run Elevated Scan for more detail.";
    }

    private static string BuildSystemIntelligenceWarningReason(JsonElement root, string diagnosticsSummary)
    {
        var reasons = new List<string>();
        if (TryGetBatteryWearPercent(root, out var wear) && wear >= 25d)
        {
            reasons.Add("Battery wear");
        }

        var readiness = BuildWindowsReadinessSummary(root);
        if (readiness.Contains("Needs verification", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("Windows readiness verification needed");
        }

        if (IsStorageHealthWarning(root))
        {
            reasons.Add("Storage health review");
        }

        if (ContainsRiskSignal(diagnosticsSummary, "toolkit") || ContainsRiskSignal(diagnosticsSummary, "checksum"))
        {
            reasons.Add("Toolkit verification issue");
        }

        if (ContainsRiskSignal(diagnosticsSummary, "benchmark") || ContainsRiskSignal(diagnosticsSummary, "cache suspected"))
        {
            reasons.Add("USB benchmark review");
        }

        if (CountOptionalProviderStatuses(root, "Failure") > 0)
        {
            reasons.Add("Required provider failure");
        }

        if (reasons.Count == 0)
        {
            return "Warning reason: none (scan looks stable).";
        }

        return $"Warning: {string.Join(" + ", reasons.Take(2))}.";
    }

    private static string MapHealthStatusLabel(JsonElement root, string overallStatus)
    {
        if (string.Equals(overallStatus, "ERROR", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(overallStatus, "FAILED", StringComparison.OrdinalIgnoreCase))
        {
            return "Error";
        }

        return BuildSystemIntelligenceWarningReason(root, string.Empty).Contains("Warning:", StringComparison.Ordinal)
            ? "Warning"
            : "Ready";
    }

    private static string BuildWindowsReadinessSummary(JsonElement root)
    {
        var summary = root.TryGetProperty("summary", out var s) ? s : default;
        if (summary.ValueKind == JsonValueKind.Undefined)
        {
            return "Windows readiness: Needs verification";
        }

        var tpmStatus = GetJsonProviderStatus(summary, "tpmInfo");
        var secureBootStatus = GetJsonProviderStatus(summary, "secureBootInfo");
        var needsVerification = IsUnknownProviderStatus(tpmStatus) || IsUnknownProviderStatus(secureBootStatus);
        return needsVerification
            ? "Windows readiness: Needs verification"
            : "Windows readiness: Ready";
    }

    private static IEnumerable<string> BuildSystemIntelligenceTopActions(JsonElement root, IEnumerable<string> recommendations)
    {
        var actions = new List<string>();
        if (TryGetBatteryWearPercent(root, out var wear) && wear >= 25d)
        {
            actions.Add($"Battery wear is high ({wear:0.#}%) — plan replacement or disclose for resale.");
        }

        if (BuildWindowsReadinessSummary(root).Contains("Needs verification", StringComparison.OrdinalIgnoreCase))
        {
            actions.Add("TPM/Secure Boot need verification — run Elevated Scan or check BIOS/UEFI.");
        }

        actions.Add("Flip Value can improve — add cosmetic/screen/keyboard/trackpad condition.");
        if (CountOptionalProviderStatuses(root, "PermissionRequired") > 0)
        {
            actions.Add("Some sensors require permission/vendor support — run Elevated Scan if needed.");
        }

        foreach (var recommendation in recommendations)
        {
            if (!string.IsNullOrWhiteSpace(recommendation) && actions.Count < 5)
            {
                actions.Add(recommendation);
            }
        }

        return actions.Take(5);
    }

    private static string NormalizeSystemIntelligenceAutomationLine(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : text.Contains("Scan Confidence", StringComparison.Ordinal)
                ? text
                : text.Replace("Confidence ", "Scan Confidence ", StringComparison.Ordinal)
                .Replace("| Confidence:", "| Scan Confidence:", StringComparison.Ordinal);

    private static bool ContainsRiskSignal(string source, string token) =>
        !string.IsNullOrWhiteSpace(source) &&
        source.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static bool IsStorageHealthWarning(JsonElement root)
    {
        var diskStatus = GetJsonString(root, "diskStatus", "UNKNOWN");
        return diskStatus.Contains("WARN", StringComparison.OrdinalIgnoreCase) ||
               diskStatus.Contains("DEGRADED", StringComparison.OrdinalIgnoreCase) ||
               diskStatus.Contains("FAIL", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetBatteryWearPercent(JsonElement root, out double wearPercent)
    {
        wearPercent = 0d;
        if (!root.TryGetProperty("batteries", out var batteries) || batteries.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var battery in batteries.EnumerateArray())
        {
            var wearDisplay = GetJsonString(battery, "wearDisplay", string.Empty);
            var match = Regex.Match(wearDisplay, @"(?<value>\d+(\.\d+)?)\s*%", RegexOptions.CultureInvariant);
            if (match.Success &&
                double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                wearPercent = parsed;
                return true;
            }
        }

        return false;
    }

    private static string FormatTpmForUi(JsonElement summary, string raw)
    {
        return ShouldTreatTpmAsVerificationItem(summary, raw)
            ? "Not reported by Windows scan. Verify BIOS/UEFI TPM/PTT setting."
            : raw;
    }

    private static bool ShouldTreatTpmAsVerificationItem(JsonElement summary, string raw)
    {
        if (!raw.Contains("TPM not detected", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var source = summary.TryGetProperty("tpmInfo", out var info)
            ? GetJsonString(info, "source", string.Empty)
            : string.Empty;
        var confidence = summary.TryGetProperty("tpmInfo", out info)
            ? GetJsonString(info, "confidence", string.Empty)
            : string.Empty;

        // A single Get-Tpm "not detected" result can be firmware/access/reporting ambiguity on business laptops.
        // Keep the UI as a verification item unless a future report adds stronger multi-source absence evidence.
        return source.Contains("Get-Tpm", StringComparison.OrdinalIgnoreCase) ||
               !confidence.Equals("ConfirmedAbsent", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeMetricLabel(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return $"{label}: Not exposed";
        }

        var trimmed = value.Trim();
        return trimmed.StartsWith(label + ":", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{label}: {trimmed}";
    }

    private static string BuildDiskHealthPercentText(JsonElement disk)
    {
        if (disk.TryGetProperty("diskHealthPercent", out var hp) && hp.ValueKind == JsonValueKind.Object)
        {
            var value = GetJsonString(hp, "value", string.Empty);
            if (!string.IsNullOrWhiteSpace(value) && !value.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                var source = GetJsonString(hp, "source", "storage reliability data");
                var confidence = GetJsonString(hp, "confidence", "Medium");
                var estimated = GetJsonBool(hp, "isEstimated") ? "estimated " : string.Empty;
                return $"Disk health: {value}% {estimated}from {source} ({confidence})";
            }
        }

        var wear = GetJsonDouble(disk, "wearPercent");
        if (wear.HasValue)
        {
            var health = Math.Clamp(100d - wear.Value, 0d, 100d);
            return $"Disk health: {health:0.#}% estimated from NVMe/SMART wear data";
        }

        var healthDisplay = GetJsonString(disk, "healthDisplay", GetJsonString(disk, "health", "Health not reported"));
        return healthDisplay.Contains("Healthy", StringComparison.OrdinalIgnoreCase)
            ? "Disk health: Healthy; percentage not exposed"
            : $"Disk health: {healthDisplay}";
    }

    private static string CompactCoverageSummary(string coverage)
    {
        if (string.IsNullOrWhiteSpace(coverage))
        {
            return "coverage unavailable";
        }

        return coverage
            .Replace("fields known", "known", StringComparison.OrdinalIgnoreCase)
            .Replace("Storage:", "Storage ", StringComparison.Ordinal)
            .Replace("Network:", "Network ", StringComparison.Ordinal)
            .Replace("Cooling:", "Cooling ", StringComparison.Ordinal)
            .Replace("Security:", "Security ", StringComparison.Ordinal)
            .Replace("Battery:", "Battery ", StringComparison.Ordinal)
            .Replace("CPU:", "CPU ", StringComparison.Ordinal)
            .Replace("GPU:", "GPU ", StringComparison.Ordinal)
            .Replace("USB:", "USB ", StringComparison.Ordinal);
    }

    private static string ApplyUsbCoverageFromIntelligence(JsonElement root, string coverage)
    {
        var usbKnown = CountUsbIntelligenceEvidence(root);
        if (usbKnown == 0)
        {
            return coverage;
        }

        var replacement = $"USB: {Math.Min(usbKnown, 3)}/3 fields known";
        return System.Text.RegularExpressions.Regex.Replace(
            coverage,
            "USB:\\s*0/3\\s*fields known",
            replacement,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private static int CountUsbIntelligenceEvidence(JsonElement root)
    {
        var count = 0;
        if (root.TryGetProperty("usbDiagnostics", out var diag) && diag.ValueKind == JsonValueKind.Object)
        {
            if (GetJsonInt(diag, "usbProfileKnownPortsCount") > 0) count++;
            if (!string.IsNullOrWhiteSpace(GetJsonString(diag, "usbCurrentTargetRiskSummary", string.Empty))) count++;
            if (!string.IsNullOrWhiteSpace(GetJsonString(diag, "usbBestKnownPortSummary", string.Empty))) count++;
            if (diag.TryGetProperty("lastBenchmark", out var bench) && bench.ValueKind == JsonValueKind.Object && GetJsonBool(bench, "succeeded")) count++;
        }

        if (root.TryGetProperty("selectedTargetBenchmark", out var selectedBench) && selectedBench.ValueKind == JsonValueKind.Object && GetJsonBool(selectedBench, "succeeded")) count++;
        if (root.TryGetProperty("selectedTargetRecommendation", out var rec) && rec.ValueKind == JsonValueKind.Object && !string.IsNullOrWhiteSpace(GetJsonString(rec, "risk", string.Empty))) count++;
        return count;
    }

    private static string BuildUsbSensorCompactSummary(JsonElement root, JsonElement sensorMatrix)
    {
        var evidence = new List<string>();
        if (root.TryGetProperty("usbDiagnostics", out var diag) && diag.ValueKind == JsonValueKind.Object)
        {
            if (GetJsonInt(diag, "usbProfileKnownPortsCount") > 0)
            {
                evidence.Add("mapped ports known");
            }

            var risk = GetJsonString(diag, "usbCurrentTargetRiskSummary", string.Empty);
            if (!string.IsNullOrWhiteSpace(risk))
            {
                evidence.Add(risk.TrimEnd('.'));
            }

            var benchmark = GetJsonString(diag, "usbBestKnownPortSummary", string.Empty);
            if (!string.IsNullOrWhiteSpace(benchmark))
            {
                evidence.Add(benchmark);
            }
        }

        if (evidence.Count > 0)
        {
            return FormatList(evidence.Take(3), "USB Intelligence available");
        }

        var usbGroup = FindSensorGroup(sensorMatrix, "USB");
        return usbGroup.HasValue
            ? $"{GetJsonString(usbGroup.Value, "knownFields", "0")}/{GetJsonString(usbGroup.Value, "totalFields", "0")} USB fields known"
            : "USB Intelligence not summarized in this report";
    }

    private static string BuildLimitedSensorCompactSummary(JsonElement sensorMatrix)
    {
        var unavailable = GetUnavailableSensorNames(sensorMatrix).ToArray();
        if (unavailable.Length == 0)
        {
            return "no major limited sensors reported";
        }

        var names = unavailable
            .Where(name => name.Contains("temperature", StringComparison.OrdinalIgnoreCase) ||
                           name.Contains("Fan", StringComparison.OrdinalIgnoreCase) ||
                           name.Contains("power", StringComparison.OrdinalIgnoreCase) ||
                           name.Contains("load", StringComparison.OrdinalIgnoreCase))
            .Take(4)
            .ToArray();
        return names.Length == 0
            ? "some sensors need firmware/vendor support"
            : $"{FormatList(names, "limited sensors")} may require deep/vendor sensor support";
    }

    private static string BuildStorageSensorCompactSummary(JsonElement sensorMatrix)
    {
        var storage = FindSensorGroup(sensorMatrix, "Storage");
        if (!storage.HasValue)
        {
            return "storage sensor coverage unavailable";
        }

        var unavailable = EnumerateReadingsFromSensorGroup(storage.Value)
            .Where(r => GetJsonBool(r, "isUnavailable"))
            .Select(r => GetJsonString(r, "name", string.Empty))
            .Where(name => name.Contains("temperature", StringComparison.OrdinalIgnoreCase) ||
                           name.Contains("wear", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        return unavailable.Length == 0
            ? "health/wear/temp fields exposed where available"
            : "health good where reported; temp/wear percentage not exposed by current provider";
    }

    private static JsonElement? FindSensorGroup(JsonElement sensorMatrix, string category)
    {
        if (!sensorMatrix.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var group in groups.EnumerateArray())
        {
            if (GetJsonString(group, "category", string.Empty).Equals(category, StringComparison.OrdinalIgnoreCase))
            {
                return group;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetUnavailableSensorNames(JsonElement sensorMatrix)
    {
        if (!sensorMatrix.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var reading in EnumerateSensorReadings(groups))
        {
            if (GetJsonBool(reading, "isUnavailable"))
            {
                var name = GetJsonString(reading, "name", string.Empty);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    yield return name;
                }
            }
        }
    }

    private static string FindUnavailableSensorNotes(JsonElement sensorMatrix)
    {
        if (!sensorMatrix.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array)
        {
            return "none summarized";
        }

        var notesByCategory = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var reading in EnumerateSensorReadings(groups))
        {
            if (reading.TryGetProperty("isUnavailable", out var unavailable) &&
                unavailable.ValueKind == JsonValueKind.True)
            {
                var name = GetJsonString(reading, "name", string.Empty);
                var reason = GetJsonString(reading, "unavailableReason", "Unknown");
                var category = GetJsonString(reading, "category", "Other");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    if (!notesByCategory.TryGetValue(category, out var categoryNotes))
                    {
                        categoryNotes = [];
                        notesByCategory[category] = categoryNotes;
                    }

                    categoryNotes.Add($"{name} ({HumanizeSensorReason(reason)})");
                }
            }
        }
        if (notesByCategory.Count == 0)
        {
            return "none summarized";
        }

        var preferredOrder = new[] { "CPU", "GPU", "Cooling", "Battery", "Security", "USB" };
        var orderedKeys = preferredOrder
            .Where(key => notesByCategory.ContainsKey(key))
            .Concat(notesByCategory.Keys.Where(key => !preferredOrder.Contains(key, StringComparer.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var lines = orderedKeys
            .Select(key => $"{key}: {FormatList(notesByCategory[key].Distinct(StringComparer.OrdinalIgnoreCase).Take(3), "none")}")
            .ToArray();
        return string.Join(Environment.NewLine, lines);
    }

    private static string HumanizeSensorReason(string reason)
    {
        return reason switch
        {
            "RequiresExternalProvider" => "Requires deep sensor provider",
            "RequiresVendorDriver" => "Requires vendor driver/support",
            "NotExposedByFirmware" => "Not exposed by firmware",
            _ => reason
        };
    }

    private static string HumanizeStorageMediaType(string interfaceType, string mediaType)
    {
        if (interfaceType.Contains("RAID", StringComparison.OrdinalIgnoreCase) &&
            (mediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase) || mediaType.Contains("NVMe", StringComparison.OrdinalIgnoreCase)))
        {
            return "NVMe/SSD via RAID/RST controller";
        }

        return mediaType;
    }

    private static string NormalizeWifiDisplay(string wifi)
    {
        if (wifi.Contains("Not a Wi-Fi adapter", StringComparison.OrdinalIgnoreCase))
        {
            return "Wi-Fi: not detected";
        }

        if (wifi.StartsWith("Wi-Fi", StringComparison.OrdinalIgnoreCase))
        {
            return wifi.Contains(':', StringComparison.Ordinal) ? wifi : $"Wi-Fi: {wifi[5..].Trim()}";
        }

        return $"Wi-Fi: {wifi}";
    }

    private static string HumanizeNetworkRole(string role)
    {
        return role switch
        {
            "ActivePhysicalInternet" => "Active physical internet adapter",
            _ => role
        };
    }

    private static string BuildEvidenceBasedFlipDrivers(JsonElement root, string rawDrivers)
    {
        var evidence = new List<string>();
        if (root.TryGetProperty("summary", out var summary))
        {
            if (double.TryParse(GetJsonString(summary, "ramTotal", "0").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), out var ramGb) && ramGb >= 32)
            {
                evidence.Add("32 GB RAM supports premium workstation/dev resale positioning.");
            }
        }

        if (root.TryGetProperty("disks", out var disks) && disks.ValueKind == JsonValueKind.Array)
        {
            var hasHealthyNvme = disks.EnumerateArray().Any(d =>
                HumanizeStorageMediaType(GetJsonString(d, "interfaceType", string.Empty), GetJsonString(d, "mediaType", string.Empty)).Contains("NVMe/SSD", StringComparison.OrdinalIgnoreCase) &&
                GetJsonString(d, "status", "UNKNOWN").Equals("READY", StringComparison.OrdinalIgnoreCase));
            if (hasHealthyNvme)
            {
                evidence.Add("Healthy NVMe/SSD storage is a resale driver.");
            }
        }

        var raw = rawDrivers
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !x.Equals("16 GB RAM meets a strong resale baseline.", StringComparison.OrdinalIgnoreCase));
        var merged = evidence.Count == 0 ? FormatList(raw, "none") : FormatList(evidence.Concat(raw).Where(x => !string.IsNullOrWhiteSpace(x)), "none");
        return merged;
    }

    private static string BuildEvidenceBasedFlipReducers(JsonElement root, string rawReducers)
    {
        var reducers = rawReducers
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !item.Contains("spinning or unknown storage lowers buyer confidence", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (root.TryGetProperty("disks", out var disks) && disks.ValueKind == JsonValueKind.Array)
        {
            var hasHealthyNvme = disks.EnumerateArray().Any(d =>
                HumanizeStorageMediaType(GetJsonString(d, "interfaceType", string.Empty), GetJsonString(d, "mediaType", string.Empty)).Contains("NVMe/SSD", StringComparison.OrdinalIgnoreCase) &&
                GetJsonString(d, "status", "UNKNOWN").Equals("READY", StringComparison.OrdinalIgnoreCase));
            if (hasHealthyNvme)
            {
                reducers = reducers
                    .Where(item => !item.Contains("spinning", StringComparison.OrdinalIgnoreCase) &&
                                   !item.Contains("unknown storage", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        return FormatList(reducers, "none from detected hardware evidence");
    }

    private static string BuildEvidenceBasedDeviceFitWatchOuts(JsonElement root, string weakFitsText)
    {
        var watch = weakFitsText
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !item.Equals("none obvious", StringComparison.OrdinalIgnoreCase) &&
                           !item.Equals("none listed", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (root.TryGetProperty("batteries", out var batteries) && batteries.ValueKind == JsonValueKind.Array)
        {
            var highWear = batteries.EnumerateArray()
                .Select(b => GetJsonString(b, "wearDisplay", string.Empty))
                .FirstOrDefault(w => w.Contains('%') && double.TryParse(w.Replace("%", string.Empty), out var v) && v >= 35);
            if (!string.IsNullOrWhiteSpace(highWear))
            {
                watch.Add($"Battery wear {highWear} is a real watch-out");
            }
        }

        if (root.TryGetProperty("summary", out var summary))
        {
            if (IsUnknownProviderStatus(GetJsonProviderStatus(summary, "tpmInfo")) ||
                IsUnknownProviderStatus(GetJsonProviderStatus(summary, "secureBootInfo")))
            {
                watch.Add("TPM/Secure Boot verification still needed");
            }
        }

        if (!watch.Any(item => item.Contains("heavy gaming", StringComparison.OrdinalIgnoreCase)))
        {
            watch.Add("Heavy gaming/thermals remain confidence-limited unless benchmarked");
        }

        return FormatList(watch.Distinct(StringComparer.OrdinalIgnoreCase), "verify battery/security/gaming limits before listing");
    }

    private static string BuildGroupedExamples(JsonElement deviceFit, string mode)
    {
        if (!deviceFit.TryGetProperty("exampleWorkloads", out var examples) || examples.ValueKind != JsonValueKind.Array)
        {
            return "none listed";
        }

        var list = examples.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        IEnumerable<string> filtered = mode switch
        {
            "games" => list.Where(x => x.Contains("game", StringComparison.OrdinalIgnoreCase) || x.Contains("Fortnite", StringComparison.OrdinalIgnoreCase) || x.Contains("AAA", StringComparison.OrdinalIgnoreCase)),
            "creator" => list.Where(x => x.Contains("Visual Studio", StringComparison.OrdinalIgnoreCase) || x.Contains("Docker", StringComparison.OrdinalIgnoreCase) || x.Contains("Premiere", StringComparison.OrdinalIgnoreCase) || x.Contains("creator", StringComparison.OrdinalIgnoreCase) || x.Contains("development", StringComparison.OrdinalIgnoreCase)),
            "notideal" => GetJsonStringArray(deviceFit, "weakFits"),
            _ => list.Where(x => !x.Contains("AAA", StringComparison.OrdinalIgnoreCase))
        };

        var fallback = mode.Equals("notideal", StringComparison.OrdinalIgnoreCase)
            ? "modern AAA gaming at high settings; long unplugged sessions unless battery is replaced/verified; heavy AI/GPU rendering; thermal-heavy workloads unless benchmarked"
            : "none listed";
        return FormatList(filtered.Take(3), fallback);
    }

    private static string GetInventoryDataSummary(JsonElement sensorMatrix)
    {
        if (!sensorMatrix.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array)
        {
            return "No grouped inventory data";
        }

        var parts = groups.EnumerateArray()
            .Select(group => $"{GetJsonString(group, "category", "Group")}: {GetJsonString(group, "knownFields", "0")}/{GetJsonString(group, "totalFields", "0")}")
            .Take(6);
        return FormatList(parts, "No grouped inventory data");
    }

    private static IEnumerable<JsonElement> EnumerateSensorReadings(JsonElement groups)
    {
        foreach (var group in groups.EnumerateArray())
        {
            if (!group.TryGetProperty("readings", out var readings) ||
                readings.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var reading in readings.EnumerateArray())
            {
                yield return reading;
            }
        }
    }

    private static IEnumerable<JsonElement> EnumerateReadingsFromSensorGroup(JsonElement group)
    {
        if (!group.TryGetProperty("readings", out var readings) || readings.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var reading in readings.EnumerateArray())
        {
            yield return reading;
        }
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
                $"Storage: {SystemIntelligenceDiskHealthText}{Environment.NewLine}" +
                $"Best use: {ShortenForSummary(SystemIntelligenceDeviceFitCardText)}{Environment.NewLine}" +
                $"Hardware X-Ray: {ShortenForSummary(SystemIntelligenceHardwareXrayCardText)}";
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
        var deviceFit = SystemIntelligenceDeviceFitCardText.StartsWith("Run a system scan", StringComparison.OrdinalIgnoreCase)
            ? "Run scan for device-fit guidance"
            : ShortenForSummary(SystemIntelligenceDeviceFitCardText);
        var hardwareXray = SystemIntelligenceHardwareXrayCardText.StartsWith("Run a system scan", StringComparison.OrdinalIgnoreCase)
            ? "Run scan for machine class and sensor coverage"
            : ShortenForSummary(SystemIntelligenceHardwareXrayCardText);
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
            $"- Best use: {deviceFit}{Environment.NewLine}" +
            $"- Hardware X-Ray: {hardwareXray}{Environment.NewLine}" +
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

    private static string GetJsonProviderStatus(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Object)
        {
            return "UNKNOWN";
        }

        return GetJsonString(property, "status", "UNKNOWN");
    }

    private static bool IsUnknownProviderStatus(string status) =>
        string.IsNullOrWhiteSpace(status) ||
        status.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("NOT_EXPOSED", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("NOTEXPOSED", StringComparison.OrdinalIgnoreCase);

    private static int CountOptionalProviderStatuses(JsonElement root, string status)
    {
        if (!root.TryGetProperty("optionalProviderStatus", out var providers) || providers.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        return providers.EnumerateArray().Count(provider =>
            string.Equals(GetJsonString(provider, "status", string.Empty), status, StringComparison.OrdinalIgnoreCase));
    }

    private static int CountAdaptersByKind(JsonElement network, bool virtualAdapters)
    {
        if (!network.TryGetProperty("adapters", out var adapters) || adapters.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        return adapters.EnumerateArray().Count(adapter => ShouldTreatAdapterAsVirtual(adapter) == virtualAdapters);
    }

    private static bool HasAnyDnsServer(JsonElement network)
    {
        if (!network.TryGetProperty("adapters", out var adapters) || adapters.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var adapter in adapters.EnumerateArray())
        {
            if (adapter.TryGetProperty("dnsServers", out var dnsServers) &&
                dnsServers.ValueKind == JsonValueKind.Array &&
                dnsServers.GetArrayLength() > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldTreatAdapterAsVirtual(JsonElement adapter)
    {
        if (GetJsonBool(adapter, "isVirtual"))
        {
            return true;
        }

        var role = GetJsonString(adapter, "adapterRole", string.Empty);
        if (role.Contains("virtual", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("host-only", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("vpn", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("loopback", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return SystemIntelligenceFormatter.ShouldIgnoreAdapterForWarnings(
            GetJsonString(adapter, "name", string.Empty),
            GetJsonString(adapter, "description", string.Empty));
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

    private static long GetJsonLong(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return 0L;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var value))
        {
            return value;
        }

        if (property.ValueKind == JsonValueKind.String &&
            long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0L;
    }

    private static string FormatBytesForToolkit(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        var value = (double)bytes;
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var unitIndex = 0;
        while (value >= 1024d && unitIndex < units.Length - 1)
        {
            value /= 1024d;
            unitIndex++;
        }

        return $"{value:0.#} {units[unitIndex]}";
    }

    private static double? GetJsonDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var value))
        {
            return value;
        }

        if (property.ValueKind == JsonValueKind.String &&
            double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
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
            TargetWarningText =
                "No USB selected yet. Pick a removable drive from the list to see safety notes, Ventoy status, and USB Intelligence.";
            ActionWarningText = "Setup USB, Update USB, and Ventoy stay disabled until you choose a valid USB data partition (not EFI/VTOYEFI).";
            SetTargetWarningVisuals(RunningBackground, RunningBorder, RunningForeground);
            OnPropertyChanged(nameof(UsbBuilderTargetStatusBanner));
            return;
        }

        if (!SelectedUsbTarget.IsSelectable)
        {
            TargetWarningText = SelectedUsbTarget.SelectionWarningDisplay;
            ActionWarningText =
                "This target is blocked. ForgerEMS will not run actions against an EFI, VTOYEFI, or other boot-only partition — choose the large data volume.";
            SetTargetWarningVisuals(ErrorBackground, ErrorBorder, ErrorForeground);
            OnPropertyChanged(nameof(UsbBuilderTargetStatusBanner));
            return;
        }

        var executionBlockReason = UsbTargetSafety.GetExecutionBlockReason(SelectedUsbTarget);
        if (!string.IsNullOrWhiteSpace(executionBlockReason))
        {
            TargetWarningText = executionBlockReason;
            ActionWarningText =
                "Blocked: the small EFI/VTOYEFI slice is for boot metadata only. Select the large removable data partition for Ventoy/toolkit files.";
            SetTargetWarningVisuals(ErrorBackground, ErrorBorder, ErrorForeground);
            OnPropertyChanged(nameof(UsbBuilderTargetStatusBanner));
            return;
        }

        var preferredOrVentoy =
            SelectedUsbTarget.IsPreferredUsbTarget || SelectedUsbTarget.HasVentoyStyleLargeDataPartition;

        if (preferredOrVentoy && SelectedUsbTarget.IsLikelyUsb)
        {
            TargetWarningText = "Ventoy data partition detected. This is the correct target.";
            ActionWarningText =
                "You are on the large Ventoy data volume. Avoid the small VTOYEFI slice and confirm the drive letter before writing.";
            SetTargetWarningVisuals(ReadyBackground, ReadyBorder, ReadyForeground);
            OnPropertyChanged(nameof(UsbBuilderTargetStatusBanner));
            return;
        }

        if (SelectedUsbTarget.IsRemovableMedia)
        {
            var selectableRemovable = UsbTargets.Count(t => t.IsSelectable && t.IsRemovableMedia);
            if (selectableRemovable > 1)
            {
                TargetWarningText =
                    "Multiple removable drives detected. Match letter, label, and capacity to the USB you intend to use.";
                ActionWarningText = "Check USB selection before continuing.";
                SetTargetWarningVisuals(WarningBackground, WarningBorder, WarningForeground);
                OnPropertyChanged(nameof(UsbBuilderTargetStatusBanner));
                return;
            }

            TargetWarningText = "Ready — removable USB detected";
            ActionWarningText =
                "Only use a USB drive you are willing to modify. Do not select the small VTOYEFI partition. Double-check drive letter and size before continuing.";
            SetTargetWarningVisuals(ReadyBackground, ReadyBorder, ReadyForeground);
            OnPropertyChanged(nameof(UsbBuilderTargetStatusBanner));
            return;
        }

        if (SelectedUsbTarget.IsLikelyUsb && SelectedUsbTarget.IsLargeDataPartition)
        {
            TargetWarningText = "Ready — USB storage partition detected.";
            ActionWarningText =
                "This fixed-type USB volume is OK for Ventoy/toolkit files. Verify drive letter and free space before destructive actions.";
            SetTargetWarningVisuals(ReadyBackground, ReadyBorder, ReadyForeground);
            OnPropertyChanged(nameof(UsbBuilderTargetStatusBanner));
            return;
        }

        if (!SelectedUsbTarget.IsLikelyUsb)
        {
            TargetWarningText = SelectedUsbTarget.SelectionWarningDisplay;
            ActionWarningText = "Check USB selection before continuing.";
            SetTargetWarningVisuals(WarningBackground, WarningBorder, WarningForeground);
            OnPropertyChanged(nameof(UsbBuilderTargetStatusBanner));
            return;
        }

        TargetWarningText = SelectedUsbTarget.SelectionWarningDisplay;
        ActionWarningText = "Check USB selection before continuing.";
        SetTargetWarningVisuals(WarningBackground, WarningBorder, WarningForeground);
        OnPropertyChanged(nameof(UsbBuilderTargetStatusBanner));
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

        if (result.ShouldPersistSuccessfulHistory)
        {
            _benchmarkResultsByRoot[GetBenchmarkCacheKey(target.RootPath)] = result;
            SaveBenchmarkCache();

            if (result.WriteSpeedMBps > 0)
            {
                var intel = UsbBenchmarkProfileSync.FromServiceResult(result);
                if (intel is not null)
                {
                    var profile = _usbMachineProfileStore.LoadOrCreate();
                    var letter = target.DriveLetter.TrimEnd('\\').TrimEnd(':').ToUpperInvariant();
                    if (!string.IsNullOrEmpty(letter))
                    {
                        profile.PendingBenchmarkByDriveLetter[letter] = intel;
                        _usbMachineProfileStore.Save(profile);
                    }

                    _autoIntelligenceOrchestrator.ScheduleUsbSelectionRefresh(_backendContext, SelectedUsbTarget);
                }
            }
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
        RefreshUsbIntelligenceFromDisk();
        RaiseCommandStates();
        if (result.Succeeded)
        {
            TryRecordKyraUsbBenchmarkLearning(result);
        }
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
        var copilotConfigExisted = File.Exists(_copilotConfigPath);
        var settings = new CopilotSettingsStore(_copilotConfigPath, _copilotProviderRegistry).Load();
        KyraInstallerIntelligenceRegistry.ApplyWhenNewConfig(settings, copilotConfigExisted);
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

        if (!copilotConfigExisted)
        {
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
        OnPropertyChanged(nameof(KyraLocalRepairMemoryEnabled));
        OnPropertyChanged(nameof(KyraCommunitySharingEnabled));
        OnPropertyChanged(nameof(KyraShareResolvedIssueFixPatterns));
        OnPropertyChanged(nameof(KyraShareHardwareCompatibilityPerformancePatterns));
        OnPropertyChanged(nameof(KyraShareCrashErrorDiagnostics));
        OnPropertyChanged(nameof(KyraRealtimeGatewayEnabled));
        OnPropertyChanged(nameof(KyraRealtimeGatewayResearchEnabled));
        OnPropertyChanged(nameof(KyraRealtimeGatewayResearchConsent));
        OnPropertyChanged(nameof(KyraUseSanitizedSystemIntelligenceContext));
        OnPropertyChanged(nameof(KyraLiveToolsForBinding));
        OnPropertyChanged(nameof(KyraDeveloperManagedProviderUi));
        OnPropertyChanged(nameof(KyraTesterEditableProviders));
    }

    private void LoadBetaSettings()
    {
        var welcomeDismissed = false;
        var entitlement = false;
        var verboseLogs = false;
        var embeddedWslRunner = false;

        try
        {
            if (File.Exists(_betaConfigPath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(_betaConfigPath));
                var root = document.RootElement;
                welcomeDismissed = GetJsonBool(root, "welcomeDismissed");
                entitlement = GetJsonBool(root, "betaTesterEntitlement");
                verboseLogs = GetJsonBool(root, "verboseLiveLogs");
                embeddedWslRunner = GetJsonBool(root, "experimentalEmbeddedWslRunner");
            }
        }
        catch
        {
            welcomeDismissed = false;
            entitlement = false;
            verboseLogs = false;
            embeddedWslRunner = false;
        }

        if (string.Equals(Environment.GetEnvironmentVariable("FORGEREMS_LICENSE_TIER"), "BetaTesterPro", StringComparison.OrdinalIgnoreCase))
        {
            entitlement = true;
        }

        BetaTesterEntitlement = entitlement;
        BetaWelcomeVisibility = welcomeDismissed ? Visibility.Collapsed : Visibility.Visible;
        if (!welcomeDismissed)
        {
            SeedBetaWelcomeKyraCheckboxesFromSettings();
        }

        _verboseLiveLogs = verboseLogs;
        _experimentalEmbeddedWslRunner = embeddedWslRunner;
        DiagnosticsFeatureFlags.EmbeddedWslCommandRunnerEnabled = _experimentalEmbeddedWslRunner;
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
                experimentalEmbeddedWslRunner = _experimentalEmbeddedWslRunner,
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
        settings.TimeoutSeconds = settings.TimeoutSeconds <= 0 ? ForgerEmsEnvironmentConfiguration.KyraProviderTimeoutSeconds : settings.TimeoutSeconds;
        settings.OfflineFallbackEnabled = _copilotSettings?.OfflineFallbackEnabled ?? settings.OfflineFallbackEnabled;
        settings.RedactContextEnabled = true;
        settings.MaxContextCharacters = settings.MaxContextCharacters <= 0 ? ForgerEmsEnvironmentConfiguration.KyraContextMaxChars : settings.MaxContextCharacters;
        settings.UseLatestSystemScanContext = UseLatestSystemScanContext;
        settings.AllowOnlineSystemContextSharing = AllowOnlineSystemContextSharing;
        settings.EnableFreeProviderPool = EnableFreeProviderPool;
        settings.EnableByokProviders = EnableByokProviders;
        settings.KyraLocalRepairMemoryEnabled = KyraLocalRepairMemoryEnabled;
        settings.KyraCommunitySharingEnabled = KyraCommunitySharingEnabled;
        settings.KyraShareResolvedIssueFixPatterns = KyraShareResolvedIssueFixPatterns && KyraCommunitySharingEnabled;
        settings.KyraShareHardwareCompatibilityPerformancePatterns = KyraShareHardwareCompatibilityPerformancePatterns && KyraCommunitySharingEnabled;
        settings.KyraShareCrashErrorDiagnostics = KyraShareCrashErrorDiagnostics && KyraCommunitySharingEnabled;
        settings.KyraRealtimeGatewayEnabled = KyraRealtimeGatewayEnabled;
        settings.KyraRealtimeGatewayResearchEnabled = KyraRealtimeGatewayResearchEnabled;
        settings.KyraRealtimeGatewayResearchConsent = KyraRealtimeGatewayResearchConsent;
        settings.KyraUseSanitizedSystemIntelligenceContext = KyraUseSanitizedSystemIntelligenceContext;
        settings.MaxContextTurns = Math.Clamp(settings.MaxContextTurns <= 0 ? ForgerEmsEnvironmentConfiguration.KyraMaxContextTurns : settings.MaxContextTurns, 1, 200);
        settings.ProviderPriorityCsv = string.IsNullOrWhiteSpace(settings.ProviderPriorityCsv)
            ? ForgerEmsEnvironmentConfiguration.KyraProviderPriority
            : settings.ProviderPriorityCsv;
        settings.MemoryMode = string.IsNullOrWhiteSpace(settings.MemoryMode) ? ForgerEmsEnvironmentConfiguration.KyraMemoryMode : settings.MemoryMode;
        settings.PersonalityProfile = string.IsNullOrWhiteSpace(settings.PersonalityProfile) ? ForgerEmsEnvironmentConfiguration.KyraPersonality : settings.PersonalityProfile;

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
            ? "Hybrid/API-first: local facts stay authoritative; online providers receive sanitized system context only when sharing is enabled."
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
            CopilotMode.ForgerEmsBetaGateway => anyOnlineConfigured
                ? "Kyra Mode: ForgerEMS Beta Gateway - gateway first, then BYOK/local/offline fallback."
                : "ForgerEMS Gateway not configured. Local Kyra is active.",
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
                ? "Kyra Mode: API-first hybrid - configured providers first, Local Kyra fallback always available."
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
        CopilotActiveProviderText = response.OnlineEnhancementApplied
            ? "Kyra · online assist contributed"
            : "Kyra";
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
        CopilotDiagnosticsSummaryText =
            $"Kyra online assistants — enabled: {enabledCount} | configured: {configuredCount} | cooling down: {coolingCount} | {fallback}";
    }

    private string GetProviderDisplayName(CopilotProviderType providerType)
    {
        return _copilotProviderRegistry.FindByType(providerType)?.DisplayName ?? providerType.ToString();
    }

    private static CopilotMode ToCopilotMode(string mode)
    {
        return mode switch
        {
            "ForgerEMS Beta Gateway" => CopilotMode.ForgerEmsBetaGateway,
            "Local Only" => CopilotMode.OfflineOnly,
            "Offline Only" => CopilotMode.OfflineOnly,
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
            CopilotMode.ForgerEmsBetaGateway => "ForgerEMS Beta Gateway",
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
            BenchmarkUiSummaryLine = string.IsNullOrWhiteSpace(result.UiSummaryLine)
                ? UsbBenchmarkUiMessages.BuildUiSummary(result.GetEffectiveResultKind(), result.ReadSpeedMBps, result.WriteSpeedMBps, result.Details)
                : result.UiSummaryLine,
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
                .Where(pair => pair.Value.ShouldPersistSuccessfulHistory &&
                               pair.Value.LastTestedAt.HasValue)
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
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested || _isBusy || _refreshingUsbTargets || _benchmarksInProgress.Count > 0)
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
                    foreach (var removedRoot in removed)
                    {
                        UsbPortLabelResolver.MarkDriveRemoved(removedRoot);
                    }

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
        try
        {
            var quick = SafeTestingEnvironmentProbe.ProbeQuick();
            _cachedSafeTestingStatus = quick;
            SafeTestingEnvironmentSummaryText = quick.FormatSummary();

            WindowsSandboxStatusText = quick.WindowsSandboxBinary.Equals("Yes", StringComparison.OrdinalIgnoreCase)
                ? "Windows Sandbox appears present (System32\\WindowsSandbox.exe). ForgerEMS does not launch it or run unknown files automatically."
                : quick.WindowsSandboxBinary.Equals("No", StringComparison.OrdinalIgnoreCase)
                    ? "Windows Sandbox was not detected as installed. You can still use Hyper-V, VMware, VirtualBox, or another VM manually. ForgerEMS will not auto-run downloads."
                    : "Windows Sandbox availability: Unknown. Check optional Windows features manually if you need isolation.";
        }
        catch (Exception ex)
        {
            try
            {
                IntelligenceLogWriter.Append("diagnostics.log", $"RefreshDiagnosticsAuxiliaryText failed: {ex.Message}");
            }
            catch
            {
            }

            StartupDiagnosticLog.AppendException("RefreshDiagnosticsAuxiliaryText", ex);
            SafeTestingEnvironmentSummaryText = "Safe testing status: Unknown (probe failed — see diagnostics.log).";
            WindowsSandboxStatusText = "Windows Sandbox: Unknown.";
        }
    }

    private void RefreshEmbeddedWslDiagnosticsBindings()
    {
        try
        {
            RefreshWslRunnerSummary();
            OnPropertyChanged(nameof(DiagnosticsEmbeddedWslRunnerContentVisibility));
            OnPropertyChanged(nameof(DiagnosticsEmbeddedWslDisabledBannerVisibility));
            InsertWslRunnerPresetCommand.RaiseCanExecuteChanged();
            RunWslRunnerCommand.RaiseCanExecuteChanged();
            RunWslHostListVerboseRunnerCommand.RaiseCanExecuteChanged();
            RunWslHostStatusRunnerCommand.RaiseCanExecuteChanged();
        }
        catch (Exception exception)
        {
            StartupDiagnosticLog.AppendException("RefreshEmbeddedWslDiagnosticsBindings", exception);
        }
    }

    private async Task RefreshSafeTestingEnvironmentAsync()
    {
        try
        {
            AppendDiagnosticsLog("RefreshSafeTestingEnvironmentAsync started");
            _safeTestingEnvironmentRefreshCts?.Cancel();
            _safeTestingEnvironmentRefreshCts?.Dispose();
            _safeTestingEnvironmentRefreshCts = new CancellationTokenSource(TimeSpan.FromSeconds(28));

            var status = await SafeTestingEnvironmentProbe
                .ProbeWithWslStatusAsync(_wslExecutor, TimeSpan.FromSeconds(12), _safeTestingEnvironmentRefreshCts.Token)
                .ConfigureAwait(false);

            RunOnUi(() =>
            {
                try
                {
                    _cachedSafeTestingStatus = status;
                    SafeTestingEnvironmentSummaryText = status.FormatSummary();
                }
                catch (Exception ex)
                {
                    StartupDiagnosticLog.AppendException("RefreshSafeTestingEnvironmentAsync.ApplyUi", ex);
                }
            });

            AppendDiagnosticsLog("Safe testing / sandbox probe completed.");
        }
        catch (OperationCanceledException)
        {
            RunOnUi(() =>
            {
                try
                {
                    var baseText = _cachedSafeTestingStatus.FormatSummary();
                    SafeTestingEnvironmentSummaryText =
                        baseText + Environment.NewLine + "[Warning] Refresh was cancelled or timed out.";
                    AppendDiagnosticsLog("Safe testing refresh cancelled or timed out.");
                }
                catch (Exception ex)
                {
                    StartupDiagnosticLog.AppendException("RefreshSafeTestingEnvironmentAsync.CancelledUi", ex);
                }
            });
        }
        catch (Exception ex)
        {
            AppendDiagnosticsLog("Safe testing refresh failed", ex);
            StartupDiagnosticLog.AppendException("RefreshSafeTestingEnvironmentAsync", ex);
            RunOnUi(() =>
            {
                try
                {
                    var baseText = _cachedSafeTestingStatus.FormatSummary();
                    SafeTestingEnvironmentSummaryText =
                        baseText + Environment.NewLine + "[Warning] Full refresh failed — quick probe values retained.";
                }
                catch (Exception inner)
                {
                    StartupDiagnosticLog.AppendException("RefreshSafeTestingEnvironmentAsync.FailedUi", inner);
                }
            });
        }
    }

    private void AppendDiagnosticsLog(string message, Exception? ex = null)
    {
        try
        {
            var line = ex is null ? message : $"{message}: {ex.Message}";
            IntelligenceLogWriter.Append("diagnostics.log", line);
        }
        catch
        {
        }
    }

    private void CopySafeTestingSummary()
    {
        try
        {
            var summary = _cachedSafeTestingStatus.BuildCopySafeSummary(UnifiedDiagnosticsSummaryText);
            Clipboard.SetDataObject(summary, copy: true);
            AppendLog(new LogLine(DateTimeOffset.Now, "[OK] Copied safe diagnostics summary (redacted) to clipboard.", LogSeverity.Success));
            AppendDiagnosticsLog("CopySafeTestingSummary completed");
        }
        catch (Exception ex)
        {
            StartupDiagnosticLog.AppendException("CopySafeTestingSummary", ex);
            AppendDiagnosticsLog("CopySafeTestingSummary failed", ex);
            try
            {
                _userPromptService.ShowMessage(
                    "Diagnostics",
                    "Could not copy the safe summary. You can still use Refresh and copy lines manually.",
                    MessageBoxImage.Warning);
            }
            catch
            {
            }
        }
    }

    private void CopyDiagnosticsCommandSummary()
    {
        var summary =
            $"Command: {LastCommandNameText}{Environment.NewLine}" +
            $"Tool: {LastCommandToolText}{Environment.NewLine}" +
            $"Status: {LastCommandStatusText}{Environment.NewLine}" +
            $"Started: {LastCommandStartedText}{Environment.NewLine}" +
            $"Finished: {LastCommandFinishedText}{Environment.NewLine}" +
            $"Duration: {LastCommandDurationText}{Environment.NewLine}" +
            $"Exit: {LastCommandExitCodeText}{Environment.NewLine}" +
            $"Summary: {LastCommandSummaryText}";
        Clipboard.SetText(summary);
        AppendDiagnosticsLog("Copied command summary.");
    }

    private void CopyLast200Logs()
    {
        var lines = Logs
            .Where(IsVisibleInFullLogViewer)
            .Select(item => item.DisplayText)
            .TakeLast(200)
            .ToArray();
        Clipboard.SetText(lines.Length == 0 ? "No log lines available." : string.Join(Environment.NewLine, lines));
        AppendDiagnosticsLog("Copied last 200 log lines.");
    }

    private void RunBackendFilesReadOnlyCheck()
    {
        var root = _backendContext.RootPath;
        var backendPath = Path.Combine(root, "backend");
        var manifestPath = Path.Combine(root, "manifests");
        var verifyScript = Path.Combine(backendPath, "Verify-VentoyCore.ps1");
        var summary =
            $"Backend root: {backendPath} => {(Directory.Exists(backendPath) ? "present" : "missing")}{Environment.NewLine}" +
            $"Manifests: {manifestPath} => {(Directory.Exists(manifestPath) ? "present" : "missing")}{Environment.NewLine}" +
            $"Verify script: {verifyScript} => {(File.Exists(verifyScript) ? "present" : "missing")}";
        LastCommandText = "Check backend files -> local file checks";
        _lastCommandStartedAt = DateTimeOffset.Now;
        _lastCommandFinishedAt = DateTimeOffset.Now;
        _lastCommandExitCode = 0;
        LastCommandStatusText = "Completed";
        LastCommandSummaryText = "Backend file presence check completed.";
        OnPropertyChanged(nameof(LastCommandNameText));
        OnPropertyChanged(nameof(LastCommandToolText));
        OnPropertyChanged(nameof(LastCommandStartedText));
        OnPropertyChanged(nameof(LastCommandFinishedText));
        OnPropertyChanged(nameof(LastCommandDurationText));
        OnPropertyChanged(nameof(LastCommandExitCodeText));
        AppendDiagnosticsLog(summary);
    }

    private void RunReleaseIdentityReadOnlyCheck()
    {
        LastCommandText = "Check release identity -> app metadata";
        _lastCommandStartedAt = DateTimeOffset.Now;
        _lastCommandFinishedAt = DateTimeOffset.Now;
        _lastCommandExitCode = 0;
        LastCommandStatusText = "Completed";
        LastCommandSummaryText = "Release identity check completed.";
        OnPropertyChanged(nameof(LastCommandNameText));
        OnPropertyChanged(nameof(LastCommandToolText));
        OnPropertyChanged(nameof(LastCommandStartedText));
        OnPropertyChanged(nameof(LastCommandFinishedText));
        OnPropertyChanged(nameof(LastCommandDurationText));
        OnPropertyChanged(nameof(LastCommandExitCodeText));
        AppendDiagnosticsLog($"Release identity: {AppReleaseInfo.DisplayVersion} | {AppReleaseInfo.ReleaseIdentifier}");
    }

    private void OpenWindowsSandboxHelp()
    {
        try
        {
            AppendDiagnosticsLog("OpenWindowsSandboxHelp requested");
            Process.Start(new ProcessStartInfo("https://learn.microsoft.com/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-install")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StartupDiagnosticLog.AppendException("OpenWindowsSandboxHelp", ex);
            AppendDiagnosticsLog("OpenWindowsSandboxHelp failed", ex);
            try
            {
                _userPromptService.ShowMessage(
                    "Windows Sandbox",
                    "ForgerEMS could not open the help link. Search for Windows Sandbox install in your browser.",
                    MessageBoxImage.Warning);
            }
            catch
            {
            }
        }
    }

    private void RefreshWslRunnerSummary()
    {
        if (!DiagnosticsFeatureFlags.EmbeddedWslCommandRunnerEnabled)
        {
            WslRunnerSummaryText = _wslExecutor.IsWslInstalled()
                ? "WSL is available (wsl.exe found). The in-app command runner is disabled for beta stability — use Open WSL Terminal (external) or the Safe Testing / Sandbox section."
                : "WSL was not detected. Install Ubuntu/WSL from the Microsoft Store or run wsl --install once from an Administrator PowerShell window, then restart the PC if prompted.";
            return;
        }

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
        InsertWslRunnerPresetCommand.RaiseCanExecuteChanged();
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
        if (!DiagnosticsFeatureFlags.EmbeddedWslCommandRunnerEnabled)
        {
            try
            {
                SafeAppendWslLine("Embedded WSL terminal is experimental and disabled for beta stability.");
            }
            catch (Exception exception)
            {
                AppendDiagnosticsLog("RunWslHostArgumentsUiAsync.disabled", exception);
            }

            return;
        }

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
        if (!DiagnosticsFeatureFlags.EmbeddedWslCommandRunnerEnabled)
        {
            try
            {
                SafeAppendWslLine("Embedded WSL terminal is experimental and disabled for beta stability.");
            }
            catch (Exception exception)
            {
                AppendDiagnosticsLog("RunWslRunnerAsync.disabled", exception);
            }

            return;
        }

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
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "ForgerEMS/1.2.0-preview.1 (beta link checker; no execute)");
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
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "ForgerEMS/1.2.0-preview.1 (beta quarantine download; no execute)");
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
        OnPropertyChanged(nameof(IncludeBetaRcChannels));
        OnPropertyChanged(nameof(AppUpdateSettingsChannelLine));
        OnPropertyChanged(nameof(LastUpdateCheckDisplayText));
        OnPropertyChanged(nameof(AppUpdateSettingsIgnoredSummary));
        OnPropertyChanged(nameof(AppUpdateSettingsIgnoredVisibility));
        OnPropertyChanged(nameof(AppUpdateIncludePrereleasesValueText));
        RaiseAppUpdateStatusDetailProperties();
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
            AppendLog(new LogLine(
                DateTimeOffset.Now,
                "[INFO] Update check already running; this request was skipped.",
                LogSeverity.Info,
                channel: LiveLogChannel.Update));
            return;
        }

        _updateCheckInProgress = true;
        RunOnUi(() =>
        {
            CheckForUpdatesNowCommand.RaiseCanExecuteChanged();
            RefreshAppUpdateMachineStateForUi(inProgress: true);
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
                var channel = _appUpdateSettings.IncludeBetaRcChannels
                    ? UpdateReleaseChannel.BetaRcAllowed
                    : UpdateReleaseChannel.StableOnly;
                result = await _updateCheckService
                    .CheckForNewerReleaseAsync(installedLabel, ignored, channel, CancellationToken.None)
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
                        result.VersionComparisonUncertain
                            ? $"[OK] GitHub release may be newer (version tag not fully comparable). Installed={ReleaseVersionParser.NormalizeLabel(installedLabel)} Release={result.LatestVersionLabel}"
                            : $"[OK] Update available. Installed={ReleaseVersionParser.NormalizeLabel(installedLabel)} Latest={ReleaseVersionParser.NormalizeLabel(result.LatestVersionLabel)}",
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
                else if (result.Outcome == UpdateCheckOutcome.NoSuitableAssets)
                {
                    AppendLog(new LogLine(
                        DateTimeOffset.Now,
                        "[INFO] Latest GitHub release has no downloadable assets yet.",
                        LogSeverity.Info,
                        channel: LiveLogChannel.Update));
                }
            }
            else
            {
                var isQuietNetwork =
                    result.FailureKind == UpdateCheckFailureKind.Network ||
                    result.FailureKind == UpdateCheckFailureKind.Timeout ||
                    result.FailureKind == UpdateCheckFailureKind.UpdateSourceUnreachable;
                AppendLog(new LogLine(
                    DateTimeOffset.Now,
                    isQuietNetwork
                        ? $"Update check did not complete: {result.FailureKind}."
                        : $"Update check failed: {result.FailureKind}.",
                    isQuietNetwork ? LogSeverity.Info : LogSeverity.Warning,
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

            _updateCheckInProgress = false;
            var applyResult = result;
            RunOnUi(() => ApplyUpdateCheckResultToUi(applyResult, manual));
            RunOnUi(() =>
            {
                CheckForUpdatesNowCommand.RaiseCanExecuteChanged();
                CopyUpdateDiagnosticsCommand.RaiseCanExecuteChanged();
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
        return result with
        {
            Outcome = UpdateCheckOutcome.IgnoredVersion,
            UpdateAvailable = false,
            ErrorMessage = UpdateCheckDisplay.FormatIgnoredVersion(norm)
        };
    }

    private void ApplyUpdateCheckResultToUi(UpdateCheckResult result, bool manual)
    {
        OnPropertyChanged(nameof(LastUpdateCheckDisplayText));

        _lastAppliedUpdateCheckResult = result;
        _appUpdateMachineState = UpdateCheckMachineStateResolver.Resolve(updateCheckInProgress: false, result);
        RaiseAppUpdateStatusDetailProperties();

        var state = UpdateCheckUiPresenter.Map(result, manual, AppReleaseInfo.Version);

        AppUpdateStateDisplay = state.StatusText;

        if (state.LatestChannelSummary is not null)
        {
            _appUpdateLatestChannelText = state.LatestChannelSummary;
        }

        OnPropertyChanged(nameof(AppUpdateSettingsLatestSummary));

        AppUpdateBannerVisibility = state.BannerVisibility;
        if (state.BannerTitle is not null)
        {
            AppUpdateBannerTitle = state.BannerTitle;
        }

        if (state.BannerDetail is not null)
        {
            AppUpdateBannerDetail = state.BannerDetail;
        }

        AppUpdateDiagnosticsHintVisibility = state.DiagnosticsHintVisibility;
        AppUpdateDownloadButtonVisibility = state.DownloadButtonVisibility;
        AppUpdateIgnoreButtonVisibility = state.IgnoreButtonVisibility;
        AppUpdateViewReleaseNotesVisibility = state.ReleaseNotesVisibility;

        _pendingInstallerUrl = state.PendingInstallerUrl;
        _pendingAdvancedInstallerUrl = state.PendingAdvancedInstallerUrl;
        _pendingReleaseNotesUrl = state.PendingReleaseNotesUrl;
        _pendingVersionLabel = state.PendingVersionLabel;
        _pendingZipUrlForClipboard = state.PendingZipUrlForClipboard;
        _checksumInstructionsClipboardText = state.ChecksumInstructionsClipboardText;
        AppUpdateAdvancedDownloadButtonVisibility = state.AdvancedInstallerDownloadVisibility;
        AppUpdateCopyZipLinkVisibility = state.CopyZipLinkVisibility;
        AppUpdateCopyChecksumInstructionsVisibility = state.CopyChecksumInstructionsVisibility;

        if (!string.IsNullOrWhiteSpace(state.SafeDiagnosticText))
        {
            AppendLog(new LogLine(
                DateTimeOffset.Now,
                state.SafeDiagnosticText,
                LogSeverity.Warning,
                channel: LiveLogChannel.Diagnostics));
        }

        AppUpdateDownloadInstallerCommand.RaiseCanExecuteChanged();
        AppUpdateDownloadAdvancedInstallerCommand.RaiseCanExecuteChanged();
        CopyUpdateZipLinkCommand.RaiseCanExecuteChanged();
        CopyUpdateChecksumInstructionsCommand.RaiseCanExecuteChanged();
    }

    private void HideAppUpdateBanner()
    {
        AppUpdateBannerVisibility = Visibility.Collapsed;
        AppUpdateDiagnosticsHintVisibility = Visibility.Collapsed;
    }

    private void RefreshAppUpdateMachineStateForUi(bool inProgress)
    {
        _appUpdateMachineState = UpdateCheckMachineStateResolver.Resolve(inProgress, _lastAppliedUpdateCheckResult);
        RaiseAppUpdateStatusDetailProperties();
    }

    private void RaiseAppUpdateStatusDetailProperties()
    {
        OnPropertyChanged(nameof(AppUpdateMachineStateDisplay));
        OnPropertyChanged(nameof(AppUpdateLatestReleaseTagDisplay));
        OnPropertyChanged(nameof(AppUpdateLatestPublishedDisplay));
        OnPropertyChanged(nameof(AppUpdateAssetFoundDisplay));
        OnPropertyChanged(nameof(AppUpdateSafeFailureReasonDisplay));
    }

    private static bool CanCopyUpdateCheckDiagnostics() => true;

    private void CopyUpdateCheckDiagnostics()
    {
        try
        {
            var text = UpdateCheckDiagnosticsFormatter.BuildClipboardSummary(
                _lastAppliedUpdateCheckResult,
                UpdateCheckMachineStateResolver.Resolve(_updateCheckInProgress, _lastAppliedUpdateCheckResult),
                AppReleaseInfo.Version,
                _appUpdateSettings.IncludeBetaRcChannels);
            Clipboard.SetDataObject(text, copy: true);
            AppendLog(new LogLine(DateTimeOffset.Now, "[OK] Update-check diagnostics copied to clipboard (safe summary).", LogSeverity.Success, channel: LiveLogChannel.Update));
        }
        catch (Exception exception)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Clipboard copy failed: {exception.Message}", LogSeverity.Warning, channel: LiveLogChannel.Update));
        }
    }

    private async Task ExportSupportBundleAsync()
    {
        await Task.Yield();
        try
        {
            var dlg = new SaveFileDialog
            {
                Filter = "ZIP archive (*.zip)|*.zip",
                FileName = $"ForgerEMS-support-bundle-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip"
            };

            if (dlg.ShowDialog() != true)
            {
                return;
            }

            var updateText = UpdateCheckDiagnosticsFormatter.BuildClipboardSummary(
                _lastAppliedUpdateCheckResult,
                UpdateCheckMachineStateResolver.Resolve(_updateCheckInProgress, _lastAppliedUpdateCheckResult),
                AppReleaseInfo.Version,
                _appUpdateSettings.IncludeBetaRcChannels);

            var tier = FeatureGateService.ResolveEffectiveTier(BetaTesterEntitlement);
            var configHealth =
                KyraProviderHubConfigHealthFormatter.BuildSummary() +
                Environment.NewLine +
                $"Effective license tier (local): {tier}" + Environment.NewLine +
                $"FORGEREMS_ENV: {ForgerEmsEnvironmentConfiguration.ForgerEmsEnv}" + Environment.NewLine +
                $"FORGEREMS_RELEASE_CHANNEL: {ForgerEmsEnvironmentConfiguration.ReleaseChannel}" + Environment.NewLine +
                $"GitHub update source: {ForgerEmsEnvironmentConfiguration.GitHubOwner}/{ForgerEmsEnvironmentConfiguration.GitHubRepo}" + Environment.NewLine +
                $"TelemetryEnabled(env): {ForgerEmsFeatureFlags.TelemetryEnabled} | CrashReporting: {ForgerEmsFeatureFlags.CrashReportingEnabled}";

            if (!SupportBundleExporter.TryCreateSupportBundle(
                    dlg.FileName,
                    _appRuntimeService,
                    SelectedUsbTarget?.RootPath,
                    updateText,
                    configHealth,
                    out var err))
            {
                AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Support bundle export failed: {err}", LogSeverity.Warning, channel: LiveLogChannel.Diagnostics));
                return;
            }

            var redactedBundlePath = CopilotRedactor.Redact(dlg.FileName, enabled: true);
            var entryCount = 0;
            try
            {
                using var zip = System.IO.Compression.ZipFile.OpenRead(dlg.FileName);
                entryCount = zip.Entries.Count;
            }
            catch
            {
                // best effort
            }

            AppendLog(new LogLine(
                DateTimeOffset.Now,
                $"[OK] Exported redacted support bundle: {redactedBundlePath} | files: {entryCount} | redaction status: active | No API keys or arbitrary USB files included.",
                LogSeverity.Success,
                channel: LiveLogChannel.Diagnostics));
        }
        catch (Exception exception)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Support bundle export failed: {exception.Message}", LogSeverity.Warning, channel: LiveLogChannel.Diagnostics));
        }
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
        _pendingAdvancedInstallerUrl = string.Empty;
        _pendingReleaseNotesUrl = string.Empty;
        _pendingVersionLabel = string.Empty;
        _pendingZipUrlForClipboard = string.Empty;
        _checksumInstructionsClipboardText = string.Empty;
        AppUpdateAdvancedDownloadButtonVisibility = Visibility.Collapsed;
        AppUpdateCopyZipLinkVisibility = Visibility.Collapsed;
        AppUpdateCopyChecksumInstructionsVisibility = Visibility.Collapsed;

        HideAppUpdateBanner();
        AppUpdateDownloadButtonVisibility = Visibility.Collapsed;
        AppUpdateIgnoreButtonVisibility = Visibility.Collapsed;
        AppUpdateViewReleaseNotesVisibility = Visibility.Collapsed;
        AppUpdateDownloadInstallerCommand.RaiseCanExecuteChanged();
        AppUpdateDownloadAdvancedInstallerCommand.RaiseCanExecuteChanged();
        CopyUpdateZipLinkCommand.RaiseCanExecuteChanged();
        CopyUpdateChecksumInstructionsCommand.RaiseCanExecuteChanged();

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

    private static bool IsHttpsAssetUrl(string url, bool exeOnly)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var p = uri.AbsolutePath;
        if (exeOnly)
        {
            return p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        }

        return p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
               p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    }

    private bool CanDownloadPendingInstaller() =>
        !_updateDownloadInProgress && IsHttpsAssetUrl(_pendingInstallerUrl, exeOnly: false);

    private bool CanDownloadPendingAdvancedInstaller() =>
        !_updateDownloadInProgress && IsHttpsAssetUrl(_pendingAdvancedInstallerUrl, exeOnly: true);

    private bool CanCopyPendingZipLink() =>
        AppUpdateCopyZipLinkVisibility == Visibility.Visible && !string.IsNullOrWhiteSpace(_pendingZipUrlForClipboard);

    private bool CanCopyPendingChecksumInstructions() =>
        AppUpdateCopyChecksumInstructionsVisibility == Visibility.Visible &&
        !string.IsNullOrWhiteSpace(_checksumInstructionsClipboardText);

    private void CopyPendingZipLink()
    {
        try
        {
            Clipboard.SetDataObject(_pendingZipUrlForClipboard, copy: true);
            AppendLog(new LogLine(DateTimeOffset.Now, "[OK] ZIP download link copied to clipboard.", LogSeverity.Success));
        }
        catch (Exception exception)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Clipboard copy failed: {exception.Message}", LogSeverity.Warning));
        }
    }

    private void CopyPendingChecksumInstructions()
    {
        try
        {
            Clipboard.SetDataObject(_checksumInstructionsClipboardText, copy: true);
            AppendLog(new LogLine(DateTimeOffset.Now, "[OK] Checksum instructions copied to clipboard.", LogSeverity.Success));
        }
        catch (Exception exception)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Clipboard copy failed: {exception.Message}", LogSeverity.Warning));
        }
    }

    private async Task DownloadPendingInstallerAsync()
    {
        await DownloadPendingReleaseAssetAsync(_pendingInstallerUrl, isAdvancedInstaller: false).ConfigureAwait(false);
    }

    private async Task DownloadPendingAdvancedInstallerAsync()
    {
        await DownloadPendingReleaseAssetAsync(_pendingAdvancedInstallerUrl, isAdvancedInstaller: true).ConfigureAwait(false);
    }

    private async Task DownloadPendingReleaseAssetAsync(string downloadUrl, bool isAdvancedInstaller)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            return;
        }

        _updateDownloadInProgress = true;
        RunOnUi(() =>
        {
            AppUpdateDownloadInstallerCommand.RaiseCanExecuteChanged();
            AppUpdateDownloadAdvancedInstallerCommand.RaiseCanExecuteChanged();
            AppUpdateBannerDetail = isAdvancedInstaller
                ? "Downloading installer EXE (not running it; SmartScreen may prompt separately)…"
                : "Downloading release asset (not running it)…";
            AppUpdateStateDisplay = "Downloading…";
        });

        try
        {
            var updatesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForgerEMS", "Updates");
            Directory.CreateDirectory(updatesDir);
            var uri = new Uri(downloadUrl);
            var fileName = Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = isAdvancedInstaller ? "ForgerEMS-Update.exe" : "ForgerEMS-Update.zip";
            }

            foreach (var c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c, '_');
            }

            var targetPath = Path.Combine(updatesDir, $"{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{fileName}");

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(20);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"ForgerEMS-UpdateDownload/{AppReleaseInfo.Version}");
            await using var stream = await client.GetStreamAsync(downloadUrl).ConfigureAwait(false);
            await using var file = File.Create(targetPath);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            var maxBytes = fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                ? 650L * 1024 * 1024
                : 280L * 1024 * 1024;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > maxBytes)
                {
                    throw new IOException("Download exceeds allowed size for this beta channel.");
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
                    "Download complete (file was not run).\n" + targetPath + "\nSHA256: " + sha;
                AppUpdateStateDisplay = "Download complete.";
                AppendLog(new LogLine(
                    DateTimeOffset.Now,
                    "[OK] Update asset saved under local Updates folder (not executed).",
                    LogSeverity.Success));
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
            RunOnUi(() =>
            {
                AppUpdateDownloadInstallerCommand.RaiseCanExecuteChanged();
                AppUpdateDownloadAdvancedInstallerCommand.RaiseCanExecuteChanged();
            });
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

    private void ShowPrivacy()
    {
        ScrollableInfoWindow.Show(
            Application.Current?.MainWindow,
            "ForgerEMS Privacy (Beta)",
            InfoDocumentTexts.BuildPrivacy());
    }

    private async Task OpenUbuntuTerminalAsync()
    {
        const string failureMessage =
            "ForgerEMS could not open WSL. Check that WSL is installed and a distro is available.";

        try
        {
            LastCommandText = "Open WSL Terminal -> wt.exe wsl.exe";
            AppendLog(new LogLine(DateTimeOffset.Now, "[INFO] Opening WSL in an external window (not hosted inside ForgerEMS).", LogSeverity.Info));
            AppendDiagnosticsLog("OpenUbuntuTerminalAsync: external launch requested");

            if (TryStartDetachedProcess("wt.exe", "wsl.exe"))
            {
                AppendLog(new LogLine(DateTimeOffset.Now, "[OK] Launched wt.exe wsl.exe.", LogSeverity.Success));
                return;
            }

            LastCommandText = "Open WSL Terminal -> wt.exe -p Ubuntu";
            AppendDiagnosticsLog("OpenUbuntuTerminalAsync: wt wsl.exe unavailable, trying Ubuntu profile");
            if (TryStartDetachedProcess("wt.exe", "-p", "Ubuntu"))
            {
                AppendLog(new LogLine(DateTimeOffset.Now, "[OK] Launched Windows Terminal Ubuntu profile.", LogSeverity.Success));
                return;
            }

            LastCommandText = "Open WSL Terminal -> wsl.exe";
            AppendDiagnosticsLog("OpenUbuntuTerminalAsync: falling back to wsl.exe");
            if (TryStartDetachedProcess("wsl.exe"))
            {
                AppendLog(new LogLine(DateTimeOffset.Now, "[OK] Launched wsl.exe.", LogSeverity.Success));
                return;
            }

            AppendDiagnosticsLog("OpenUbuntuTerminalAsync: all launch attempts failed");
            AppendLog(new LogLine(DateTimeOffset.Now, "[WARN] " + failureMessage, LogSeverity.Warning));
            _userPromptService.ShowMessage("WSL Terminal", failureMessage, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            AppendDiagnosticsLog("OpenUbuntuTerminalAsync unexpected exception", ex);
            StartupDiagnosticLog.AppendException("OpenUbuntuTerminalAsync", ex);
            AppendLog(new LogLine(DateTimeOffset.Now, "[ERROR] " + failureMessage, LogSeverity.Error, isErrorStream: true));
            try
            {
                _userPromptService.ShowMessage("WSL Terminal", failureMessage, MessageBoxImage.Warning);
            }
            catch
            {
            }
        }

        await Task.CompletedTask;
    }

    private async Task RunSafeExternalCommandAsync(string displayName, string fileName, params string[] arguments)
    {
        LastCommandText = $"{displayName} -> {fileName} {string.Join(" ", arguments)}";
        _lastCommandStartedAt = DateTimeOffset.Now;
        _lastCommandFinishedAt = null;
        _lastCommandExitCode = null;
        LastCommandStatusText = "Running";
        LastCommandSummaryText = "Command started.";
        OnPropertyChanged(nameof(LastCommandNameText));
        OnPropertyChanged(nameof(LastCommandToolText));
        OnPropertyChanged(nameof(LastCommandStartedText));
        OnPropertyChanged(nameof(LastCommandFinishedText));
        OnPropertyChanged(nameof(LastCommandDurationText));
        OnPropertyChanged(nameof(LastCommandExitCodeText));
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
            _lastCommandFinishedAt = DateTimeOffset.Now;
            _lastCommandExitCode = process.ExitCode;
            LastCommandStatusText = process.ExitCode == 0 ? "Completed" : "Completed with warnings";
            LastCommandSummaryText = $"{displayName} exited with code {process.ExitCode}.";
            OnPropertyChanged(nameof(LastCommandFinishedText));
            OnPropertyChanged(nameof(LastCommandDurationText));
            OnPropertyChanged(nameof(LastCommandExitCodeText));
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, $"[ERROR] Unable to run {fileName}: {exception.Message}", LogSeverity.Error, isErrorStream: true));
            _lastCommandFinishedAt = DateTimeOffset.Now;
            _lastCommandExitCode = -1;
            LastCommandStatusText = "Failed";
            LastCommandSummaryText = exception.Message;
            OnPropertyChanged(nameof(LastCommandFinishedText));
            OnPropertyChanged(nameof(LastCommandDurationText));
            OnPropertyChanged(nameof(LastCommandExitCodeText));
        }
        catch (Exception exception)
        {
            StartupDiagnosticLog.AppendException("RunSafeExternalCommandAsync", exception);
            AppendLog(new LogLine(DateTimeOffset.Now, $"[ERROR] {displayName} failed: {exception.Message}", LogSeverity.Error, isErrorStream: true));
            _lastCommandFinishedAt = DateTimeOffset.Now;
            _lastCommandExitCode = -1;
            LastCommandStatusText = "Failed";
            LastCommandSummaryText = exception.Message;
            OnPropertyChanged(nameof(LastCommandFinishedText));
            OnPropertyChanged(nameof(LastCommandDurationText));
            OnPropertyChanged(nameof(LastCommandExitCodeText));
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
        catch (Exception ex)
        {
            try
            {
                IntelligenceLogWriter.Append(
                    "diagnostics.log",
                    $"TryStartDetachedProcess failed ({fileName}): {ex.Message}");
            }
            catch
            {
            }

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
                while (_pendingLiveLogs.TryDequeue(out _))
                {
                }
                _liveLogFlushTimer?.Stop();
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
        RetryFailedManagedDownloadsCommand.RaiseCanExecuteChanged();
        RenameUsbCommand.RaiseCanExecuteChanged();
        InstallOrUpdateVentoyCommand.RaiseCanExecuteChanged();
        RunSystemScanCommand.RaiseCanExecuteChanged();
        RunElevatedSystemScanCommand.RaiseCanExecuteChanged();
        RefreshToolkitHealthCommand.RaiseCanExecuteChanged();
        UpdateToolkitCommand.RaiseCanExecuteChanged();
        OpenToolkitUsbReportsCommand.RaiseCanExecuteChanged();
        RecheckSelectedToolCommand.RaiseCanExecuteChanged();
        OpenSelectedToolLocationCommand.RaiseCanExecuteChanged();
        OpenManualDownloadShortcutCommand.RaiseCanExecuteChanged();
        CopySelectedToolkitExpectedPathCommand.RaiseCanExecuteChanged();
        CopySelectedToolkitDetectedPathCommand.RaiseCanExecuteChanged();
        OpenUbuntuTerminalCommand.RaiseCanExecuteChanged();
        RefreshSafeTestingEnvironmentCommand.RaiseCanExecuteChanged();
        CheckWslInstalledCommand.RaiseCanExecuteChanged();
        ShowWslDistrosCommand.RaiseCanExecuteChanged();
        CheckPowerShellVersionCommand.RaiseCanExecuteChanged();
        CheckNetworkDnsCommand.RaiseCanExecuteChanged();
        RunWslRunnerCommand.RaiseCanExecuteChanged();
        StopWslRunnerCommand.RaiseCanExecuteChanged();
        CopyWslRunnerOutputCommand.RaiseCanExecuteChanged();
        ClearWslRunnerOutputCommand.RaiseCanExecuteChanged();
        InsertWslRunnerPresetCommand.RaiseCanExecuteChanged();
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
        StartUsbPortMappingWorkflowCommand.RaiseCanExecuteChanged();
        CaptureUsbMappingBeforeCommand.RaiseCanExecuteChanged();
        CaptureUsbMappingAfterCommand.RaiseCanExecuteChanged();
        SaveUsbMappingLabelCommand.RaiseCanExecuteChanged();
        OpenUsbMappingWizardCommand.RaiseCanExecuteChanged();
        RunUsbIntelligenceBenchmarkCommand.RaiseCanExecuteChanged();
        CancelUsbIntelligenceBenchmarkCommand.RaiseCanExecuteChanged();
        CheckForUpdatesNowCommand.RaiseCanExecuteChanged();
        AppUpdateDownloadInstallerCommand.RaiseCanExecuteChanged();
        AppUpdateDownloadAdvancedInstallerCommand.RaiseCanExecuteChanged();
        CopyUpdateZipLinkCommand.RaiseCanExecuteChanged();
        CopyUpdateChecksumInstructionsCommand.RaiseCanExecuteChanged();
    }

    private void AppendLog(LogLine line)
    {
        var redacted = CopilotRedactor.Redact(line.Text, enabled: true);
        var normalizedForDisplay = UsbLogDisplayNormalizer.NormalizeHashProviderLabels(redacted);
        var sanitized = new LogLine(
            line.Timestamp,
            normalizedForDisplay,
            line.Severity,
            line.IsErrorStream,
            line.Channel);

        _pendingLiveLogs.Enqueue(sanitized);
        ScheduleLiveLogFlush();

        try
        {
            _appRuntimeService.AppendSessionLog(sanitized);
        }
        catch
        {
            // Session log persistence is best effort only.
        }
    }

    private void EnsureLiveLogFlushTimer()
    {
        if (_liveLogFlushTimer is not null)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        _liveLogFlushTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(160)
        };
        _liveLogFlushTimer.Tick += (_, _) => FlushLiveLogsOnUi();
    }

    private void ScheduleLiveLogFlush()
    {
        RunOnUi(() =>
        {
            EnsureLiveLogFlushTimer();
            if (_liveLogFlushTimer is not null && !_liveLogFlushTimer.IsEnabled)
            {
                _liveLogFlushTimer.Start();
            }
        });
    }

    private void FlushLiveLogsOnUi()
    {
        try
        {
            var processed = 0;
            var changed = false;
            while (processed++ < 160 && _pendingLiveLogs.TryDequeue(out var line))
            {
                ApplyProgressFromLog(line.Text);
                Logs.Add(line);
                changed = true;
            }

            while (Logs.Count > 1200)
            {
                Logs.RemoveAt(0);
                changed = true;
            }

            if (changed)
            {
                RefreshLogsText();
                OnPropertyChanged(nameof(LogStatusLineText));
                CopyLogsCommand.RaiseCanExecuteChanged();
                ClearLogsCommand.RaiseCanExecuteChanged();
            }

            if (_pendingLiveLogs.IsEmpty)
            {
                _liveLogFlushTimer?.Stop();
            }
        }
        catch (Exception exception)
        {
            StartupDiagnosticLog.AppendException("FlushLiveLogsOnUi", exception);
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
        var fullVisible = Logs.Where(IsVisibleInFullLogViewer).ToArray();
        LogsText = string.Join(Environment.NewLine, fullVisible.Select(item => item.DisplayText));

        if (VerboseLiveLogs)
        {
            RecentLogsText = fullVisible.Length == 0
                ? "No log output yet."
                : string.Join(Environment.NewLine, fullVisible.Select(item => item.DisplayText).TakeLast(12));
        }
        else
        {
            var sidebarLines = new List<string>();
            foreach (var item in fullVisible)
            {
                if (UsbBuilderLiveLogPresentation.TryGetConciseSidebarLine(item, VerboseLiveLogs, out var compact) &&
                    !string.IsNullOrWhiteSpace(compact))
                {
                    sidebarLines.Add(compact);
                }
            }

            RecentLogsText = sidebarLines.Count == 0
                ? "No concise status yet. Open View Full Logs for complete output, or enable Verbose Live Logs."
                : string.Join(Environment.NewLine, sidebarLines.TakeLast(12));
        }

        CopyLogsCommand.RaiseCanExecuteChanged();
    }

    private bool IsVisibleInFullLogViewer(LogLine line)
    {
        if (!VerboseLiveLogs && line.Channel == LiveLogChannel.KyraDetail)
        {
            return false;
        }

        var levelVisible = SelectedLogLevelFilter switch
        {
            "Info" => line.Severity == LogSeverity.Info,
            "Success" => line.Severity == LogSeverity.Success,
            "Warning" => line.Severity == LogSeverity.Warning,
            "Error" => line.Severity == LogSeverity.Error,
            _ => true
        };
        if (!levelVisible)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(DiagnosticsLogSearchText) &&
            !line.DisplayText.Contains(DiagnosticsLogSearchText, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private PowerShellRunRequest WithOptionalManagedDownloadHeartbeat(PowerShellRunRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProgressItemName) ||
            request.HeartbeatKind != PowerShellHeartbeatKind.Download)
        {
            return request;
        }

        return new PowerShellRunRequest
        {
            DisplayName = request.DisplayName,
            WorkingDirectory = request.WorkingDirectory,
            ScriptPath = request.ScriptPath,
            InlineCommand = request.InlineCommand,
            Arguments = request.Arguments,
            ProgressItemName = request.ProgressItemName,
            HeartbeatKind = request.HeartbeatKind,
            BuildDownloadHeartbeatMessage = idle => BuildUsbManagedDownloadHeartbeatMessage(request.ProgressItemName!, idle)
        };
    }

    private string? BuildUsbManagedDownloadHeartbeatMessage(string itemName, TimeSpan idleSinceLastOutput)
    {
        var secs = Math.Max(1, (int)idleSinceLastOutput.TotalSeconds);
        return _usbManagedHeartbeatPhase switch
        {
            UsbManagedHeartbeatPhase.CheckingExisting =>
                $"[INFO] {itemName}: still working — checking existing file ({secs}s since last log line).",
            UsbManagedHeartbeatPhase.Downloading =>
                $"[INFO] {itemName}: still working — downloading ({secs}s since last log line).",
            UsbManagedHeartbeatPhase.HashingLargeFile =>
                $"[INFO] {itemName}: still working — hashing a large ISO / computing checksum ({secs}s since last log line; large files can take several minutes).",
            UsbManagedHeartbeatPhase.VerifyingChecksum =>
                $"[INFO] {itemName}: still working — verifying checksum ({secs}s since last log line).",
            UsbManagedHeartbeatPhase.WritingFinal =>
                $"[INFO] {itemName}: still working — writing files to USB ({secs}s since last log line).",
            _ =>
                $"[INFO] {itemName}: still working — waiting on toolkit scripts ({secs}s since last log line). May be downloading, hashing a large ISO, or verifying checksums."
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
        var inferred = UsbBuilderLiveLogPresentation.InferHeartbeatPhase(normalized);
        if (inferred != UsbManagedHeartbeatPhase.Unknown)
        {
            _usbManagedHeartbeatPhase = inferred;
        }

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
            normalized.Contains("still working", StringComparison.OrdinalIgnoreCase) ||
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
            text.Contains("SHA256 hash provider:", StringComparison.OrdinalIgnoreCase) ? "Hashing / verifying" :
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
        _usbBenchmarkHostInterruptKind = UsbBenchmarkHostInterruptKind.AppShutdown;
        try
        {
            _manualUsbBenchmarkCts?.Cancel();
        }
        catch
        {
            // ignore
        }

        try
        {
            _autoUsbBenchmarkCts?.Cancel();
        }
        catch
        {
            // ignore
        }

        _usbMonitorCancellation?.Dispose();
        _copilotGenerationCancellation?.Dispose();
        _manualUsbBenchmarkCts?.Dispose();
        _autoUsbBenchmarkCts?.Dispose();
        CancelScheduledAutomaticUsbBenchmark();
        _usbIntelligenceDebounceTimer?.Stop();
        try
        {
            _wslRunnerCancellation?.Cancel();
        }
        catch
        {
        }

        _wslRunnerCancellation?.Dispose();
        try
        {
            _safeTestingEnvironmentRefreshCts?.Cancel();
        }
        catch
        {
        }

        _safeTestingEnvironmentRefreshCts?.Dispose();
        _updateCheckService.Dispose();
        try
        {
            _wslOutputFlushTimer?.Stop();
            while (_wslPendingOutputLines.TryDequeue(out _))
            {
            }

            _liveLogFlushTimer?.Stop();
            while (_pendingLiveLogs.TryDequeue(out _))
            {
            }
        }
        catch
        {
        }
    }
}
