using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
    private static readonly Brush RunningBackground = new SolidColorBrush(Color.FromRgb(255, 244, 230));
    private static readonly Brush RunningBorder = new SolidColorBrush(Color.FromRgb(251, 191, 36));
    private static readonly Brush RunningForeground = new SolidColorBrush(Color.FromRgb(146, 64, 14));
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
    private readonly Dictionary<string, UsbBenchmarkResult> _benchmarkResultsByRoot = new(StringComparer.OrdinalIgnoreCase);

    private BackendContext _backendContext = BackendContext.Unavailable("Backend discovery has not run yet.");
    private UsbTargetInfo? _selectedUsbTarget;
    private bool _isBusy;
    private bool _useDryRun = true;
    private bool _initialized;
    private bool _autoScrollLogs = true;
    private bool _suppressSelectionRefresh;
    private int _ventoyStatusRequestId;
    private int _benchmarkRequestId;
    private string _usbOwnerName = string.Empty;
    private string _statusText = "Starting up";
    private string _statusDetail = "Discovering the backend and checking for likely USB targets.";
    private string _currentTaskState = "WORKING";
    private string _currentTaskText = "Verifying backend";
    private double _currentProgressValue;
    private bool _isProgressIndeterminate;
    private Visibility _progressVisibility = Visibility.Collapsed;
    private Visibility _dryRunBannerVisibility = Visibility.Visible;
    private Brush _statusBackground = RunningBackground;
    private Brush _statusBorderBrush = RunningBorder;
    private Brush _statusForeground = RunningForeground;
    private string _lastCommandText = "No command has been run yet.";
    private string _managedSummaryText = "No managed-download summary has been loaded yet.";
    private string _managedSummaryPathText = "Summary source: not detected";
    private string _managedSummaryUpdatedText = "Updated: n/a";
    private string _managedSummaryStatusText = "No snapshot";
    private string _logsText = string.Empty;
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

    public MainViewModel(
        IBackendDiscoveryService backendDiscoveryService,
        IPowerShellRunnerService powerShellRunnerService,
        IUsbDetectionService usbDetectionService,
        IManagedDownloadSummaryService managedDownloadSummaryService,
        IScriptStatusParser scriptStatusParser,
        IUserPromptService userPromptService,
        IVentoyIntegrationService ventoyIntegrationService,
        IAppRuntimeService appRuntimeService,
        IUsbBenchmarkService usbBenchmarkService)
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

        RefreshAllCommand = new AsyncRelayCommand(RefreshAllAsync, () => !IsBusy);
        RefreshUsbTargetsCommand = new AsyncRelayCommand(RefreshUsbTargetsAsync, () => !IsBusy);
        VerifyCommand = new AsyncRelayCommand(RunVerifyAsync, CanRunBackendOnlyActions);
        RevalidateManagedDownloadsCommand = new AsyncRelayCommand(RunRevalidateManagedDownloadsAsync, CanRunBackendOnlyActions);
        SetupUsbCommand = new AsyncRelayCommand(RunSetupUsbAsync, CanRunTargetedActions);
        UpdateUsbCommand = new AsyncRelayCommand(RunUpdateUsbAsync, CanRunTargetedActions);
        RenameUsbCommand = new AsyncRelayCommand(RunRenameUsbAsync, CanRunTargetedActions);
        InstallOrUpdateVentoyCommand = new AsyncRelayCommand(RunInstallOrUpdateVentoyAsync, CanRunTargetedActions);
        CopyLogsCommand = new RelayCommand(CopyLogs, () => !string.IsNullOrWhiteSpace(LogsText));
        ClearLogsCommand = new RelayCommand(ClearLogs, () => Logs.Count > 0);
        ShowAboutCommand = new RelayCommand(ShowAbout);
        ShowFaqCommand = new RelayCommand(ShowFaq);
        ShowLegalCommand = new RelayCommand(ShowLegal);
    }

    public ObservableCollection<UsbTargetInfo> UsbTargets { get; } = [];

    public ObservableCollection<LogLine> Logs { get; } = [];

    public AsyncRelayCommand RefreshAllCommand { get; }

    public AsyncRelayCommand RefreshUsbTargetsCommand { get; }

    public AsyncRelayCommand VerifyCommand { get; }

    public AsyncRelayCommand RevalidateManagedDownloadsCommand { get; }

    public AsyncRelayCommand SetupUsbCommand { get; }

    public AsyncRelayCommand UpdateUsbCommand { get; }

    public AsyncRelayCommand RenameUsbCommand { get; }

    public AsyncRelayCommand InstallOrUpdateVentoyCommand { get; }

    public RelayCommand CopyLogsCommand { get; }

    public RelayCommand ClearLogsCommand { get; }

    public RelayCommand ShowAboutCommand { get; }

    public RelayCommand ShowFaqCommand { get; }

    public RelayCommand ShowLegalCommand { get; }

    public UsbTargetInfo? SelectedUsbTarget
    {
        get => _selectedUsbTarget;
        set
        {
            if (SetProperty(ref _selectedUsbTarget, value))
            {
                UpdateTargetWarnings();
                RaiseCommandStates();

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

    public bool UseDryRun
    {
        get => _useDryRun;
        set
        {
            if (SetProperty(ref _useDryRun, value))
            {
                DryRunBannerVisibility = value ? Visibility.Visible : Visibility.Collapsed;
                if (value)
                {
                    AppendLog(new LogLine(DateTimeOffset.Now, "[WARN] Dry-run mode enabled. No USB changes will be written.", LogSeverity.Warning));
                }
            }
        }
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

    public Visibility DryRunBannerVisibility
    {
        get => _dryRunBannerVisibility;
        private set => SetProperty(ref _dryRunBannerVisibility, value);
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

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await RefreshAllAsync();
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
        var previousSelection = SelectedUsbTarget?.RootPath;
        _benchmarkResultsByRoot.Clear();
        var detectionResult = await _usbDetectionService.GetUsbTargetsAsync();
        var targets = detectionResult.Targets;

        UsbTargets.Clear();
        foreach (var target in targets)
        {
            UsbTargets.Add(ApplyCachedBenchmarkResult(target));
        }

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
                UseDryRun
                    ? "Dry-run is enabled. The backend should preview changes without modifying the target."
                    : "This will create or refresh toolkit folders on the selected target and can seed the manifest."))
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

        if (UseDryRun)
        {
            arguments.Add("-WhatIf");
        }
        else
        {
            arguments.Add("-WaitForManagedDownloads");
        }

        await RunScriptAsync(
            ScriptActionType.SetupUsb,
            new PowerShellRunRequest
            {
                DisplayName = UseDryRun ? "Setup USB (dry-run)" : "Setup USB + managed downloads",
                WorkingDirectory = _backendContext.WorkingDirectory,
                ScriptPath = _backendContext.SetupScriptPath,
                Arguments = arguments,
                ProgressItemName = UseDryRun ? null : "managed downloads"
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
                UseDryRun
                    ? "Dry-run is enabled. The backend should only preview archive and replacement steps."
                    : "This can archive and replace managed files on the selected target."))
        {
            return;
        }

        var arguments = new System.Collections.Generic.List<string>
        {
            "-UsbRoot",
            selectedUsbTarget.RootPath
        };

        if (UseDryRun)
        {
            arguments.Add("-WhatIf");
        }

        await RunScriptAsync(
            ScriptActionType.UpdateUsb,
            new PowerShellRunRequest
            {
                DisplayName = UseDryRun ? "Update USB (dry-run)" : "Update USB",
                WorkingDirectory = _backendContext.WorkingDirectory,
                ScriptPath = _backendContext.UpdateScriptPath,
                Arguments = arguments,
                ProgressItemName = UseDryRun ? null : "managed downloads"
            });
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
        var requestId = ++_benchmarkRequestId;

        if (target is null)
        {
            return;
        }

        if (_benchmarkResultsByRoot.ContainsKey(GetBenchmarkCacheKey(target.RootPath)))
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
            return;
        }

        RaiseCommandStates();
        ApplyBenchmarkResult(target, new UsbBenchmarkResult
        {
            Succeeded = false,
            Summary = "Benchmark testing",
            Details = "USB speed test is running.",
            ReadSpeedDisplay = "Testing...",
            WriteSpeedDisplay = "Testing..."
        });

        try
        {
            var result = await _usbBenchmarkService.RunSequentialBenchmarkAsync(target);
            if (requestId != _benchmarkRequestId)
            {
                return;
            }

            ApplyBenchmarkResult(target, result);
            if (!result.Succeeded && result.Summary.Contains("failed", StringComparison.OrdinalIgnoreCase))
            {
                AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Automatic USB speed check failed for {target.RootPath}.", LogSeverity.Warning));
            }
        }
        catch (Exception exception)
        {
            if (requestId != _benchmarkRequestId)
            {
                return;
            }

            ApplyBenchmarkResult(target, new UsbBenchmarkResult
            {
                Succeeded = false,
                Summary = "Benchmark failed",
                Details = exception.Message,
                ReadSpeedDisplay = "Failed",
                WriteSpeedDisplay = "Failed"
            });
            AppendLog(new LogLine(DateTimeOffset.Now, $"[WARN] Automatic USB speed check failed for {target.RootPath}.", LogSeverity.Warning));
        }
        finally
        {
            if (requestId == _benchmarkRequestId)
            {
                RaiseCommandStates();
            }
        }
    }

    private async Task RunScriptAsync(ScriptActionType action, PowerShellRunRequest request)
    {
        if (!_backendContext.IsAvailable)
        {
            SetStatus(
                "Backend unavailable",
                "The scripts could not be discovered, so the command cannot run.",
                ErrorBackground,
                ErrorBorder,
                ErrorForeground);
            return;
        }

        ClearLogs();
        LastCommandText = $"{request.DisplayName} -> {Path.GetFileName(request.ScriptPath ?? "inline command")}";

        var startedAt = DateTimeOffset.Now;
        AppendLifecycleStart(request.DisplayName, SelectedUsbTarget);
        if (UseDryRun && request.Arguments.Any(argument => string.Equals(argument, "-WhatIf", StringComparison.OrdinalIgnoreCase)))
        {
            AppendLog(new LogLine(DateTimeOffset.Now, "[WARN] Dry-run mode enabled. No USB changes will be written.", LogSeverity.Warning));
        }

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
        }
        finally
        {
            IsBusy = false;
            ResetProgressSoon();
        }
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
        _benchmarkResultsByRoot[GetBenchmarkCacheKey(target.RootPath)] = result;
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

    private void ClearLogs()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Logs.Clear();
            LogsText = string.Empty;
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
        CopyLogsCommand.RaiseCanExecuteChanged();
        ClearLogsCommand.RaiseCanExecuteChanged();
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

            LogsText = string.Join(Environment.NewLine, Logs.Select(item => item.DisplayText));
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
            }
            return;
        }

        if (text.Contains("Downloading", StringComparison.OrdinalIgnoreCase))
        {
            SetProgress(0, indeterminate: true);
            CurrentTaskState = "WORKING";
            CurrentTaskText = text;
        }
    }
}
