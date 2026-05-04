using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Intelligence;

namespace VentoyToolkitSetup.Wpf.ViewModels;

public sealed class UsbMappingWizardViewModel : ObservableObject
{
    private readonly IUsbIntelligenceService _intelligence;
    private readonly UsbMachineProfileStore _profileStore;
    private readonly Func<IReadOnlyList<UsbTargetInfo>> _getUsbTargets;
    private readonly Func<UsbTargetInfo, Task>? _runBenchmarkForTargetAsync;
    private readonly TimeSpan _detectOperationTimeout;
    private readonly UsbGuidedMappingWorkflow _workflow = new();
    private UsbMappingWizardStep _step = UsbMappingWizardStep.Welcome;
    private UsbMappingWizardDeviceOption? _selectedDevice;
    private bool _userConfirmedUsbMoved;
    private bool _beforeCaptured;
    private string _captureSummary = string.Empty;
    private string _confidenceAfterCapture = string.Empty;
    private bool _detectionSuccess;
    private string _detectionDetail = string.Empty;
    private string _oldPortKeyShort = string.Empty;
    private string _newPortKeyShort = string.Empty;
    private string _speedClassDisplay = string.Empty;
    private string _confidenceTierDisplay = string.Empty;
    private string _recommendationDisplay = string.Empty;
    private string _failureMessage = string.Empty;
    private string _portLabelDraft = string.Empty;
    private UsbPortMappingSaveMode _pendingSaveMode = UsbPortMappingSaveMode.TopologyInference;
    private UsbMappingWizardResult? _doneResult;
    private UsbTopologySnapshot? _beforeSnap;
    private UsbTopologySnapshot? _afterSnap;
    private UsbPortMappingResolution? _lastResolution;
    private bool _isAnalyzingPortChange;
    private string _detectChangePrimaryStatus = string.Empty;
    private string _detectChangeSubStatus = string.Empty;
    private string _detectChangeDebugSummary = string.Empty;

    public UsbMappingWizardViewModel(
        IUsbIntelligenceService intelligence,
        UsbMachineProfileStore profileStore,
        Func<IReadOnlyList<UsbTargetInfo>> getUsbTargets,
        Func<UsbTargetInfo, Task>? runBenchmarkForTargetAsync = null,
        TimeSpan? detectOperationTimeoutOverride = null)
    {
        _intelligence = intelligence;
        _profileStore = profileStore;
        _getUsbTargets = getUsbTargets;
        _runBenchmarkForTargetAsync = runBenchmarkForTargetAsync;
        _detectOperationTimeout = detectOperationTimeoutOverride ?? TimeSpan.FromSeconds(15);

        StartMappingCommand = new RelayCommand(StartMapping, () => Step == UsbMappingWizardStep.Welcome);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(this, false));
        ContinueSelectDeviceCommand = new RelayCommand(GoConfirmPort, () => SelectedDevice is not null);
        CaptureCurrentPortCommand = new RelayCommand(CaptureCurrentPort, () => SelectedDevice is not null);
        NextAfterCaptureCommand = new RelayCommand(() => Step = UsbMappingWizardStep.MoveUsb, () => _beforeCaptured);
        DetectPortChangeCommand = new AsyncRelayCommand(DetectPortChangeAsync, () => _userConfirmedUsbMoved && SelectedDevice is not null && !_isAnalyzingPortChange);
        NextToLabelCommand = new RelayCommand(() => Step = UsbMappingWizardStep.LabelPort, () => _detectionSuccess);
        TryAgainCommand = new RelayCommand(TryDetectionAgain, () => CanRetry);
        UseCurrentPortAnywayCommand = new RelayCommand(UseCurrentPortAnyway, () => !_detectionSuccess && SelectedDevice is not null && !IsAnalyzingPortChange);
        SaveManualLabelPathCommand = new RelayCommand(UseCurrentPortAnyway, () => !_detectionSuccess && SelectedDevice is not null && !IsAnalyzingPortChange);
        BackFromDetectCommand = new RelayCommand(BackFromDetect, () => IsDetectStep && !IsAnalyzingPortChange);
        SavePortLabelCommand = new RelayCommand(SavePortLabel, () => !string.IsNullOrWhiteSpace(PortLabelDraft));
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, true));
        MapAnotherPortCommand = new RelayCommand(RestartWizard);
        RunBenchmarkOnThisPortCommand = new AsyncRelayCommand(RunBenchmarkFromDoneAsync, () => _doneResult?.MappedTarget is not null && _runBenchmarkForTargetAsync is not null);
    }

    public event EventHandler<bool>? CloseRequested;

    public RelayCommand StartMappingCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ContinueSelectDeviceCommand { get; }
    public RelayCommand CaptureCurrentPortCommand { get; }
    public RelayCommand NextAfterCaptureCommand { get; }
    public AsyncRelayCommand DetectPortChangeCommand { get; }
    public RelayCommand NextToLabelCommand { get; }
    public RelayCommand TryAgainCommand { get; }
    public RelayCommand UseCurrentPortAnywayCommand { get; }
    public RelayCommand SaveManualLabelPathCommand { get; }
    public RelayCommand BackFromDetectCommand { get; }
    public RelayCommand SavePortLabelCommand { get; }
    public RelayCommand CloseCommand { get; }
    public RelayCommand MapAnotherPortCommand { get; }
    public AsyncRelayCommand RunBenchmarkOnThisPortCommand { get; }

    public ObservableCollection<UsbMappingWizardDeviceOption> DeviceOptions { get; } = new();

    public UsbMappingWizardStep Step
    {
        get => _step;
        private set
        {
            var previous = _step;
            if (SetProperty(ref _step, value))
            {
                OnPropertyChanged(nameof(IsWelcomeStep));
                OnPropertyChanged(nameof(IsSelectDeviceStep));
                OnPropertyChanged(nameof(IsConfirmPortStep));
                OnPropertyChanged(nameof(IsMoveUsbStep));
                OnPropertyChanged(nameof(IsDetectStep));
                OnPropertyChanged(nameof(IsLabelStep));
                OnPropertyChanged(nameof(IsDoneStep));
                if (previous == UsbMappingWizardStep.DetectChange || value == UsbMappingWizardStep.DetectChange)
                {
                    RefreshDetectChangeChrome();
                }

                if (value == UsbMappingWizardStep.DetectChange)
                {
                    StartupDiagnosticLog.AppendLine("[UsbMappingWizard] Wizard step changed to DetectChange");
                }

                RaiseAllCommands();
            }
        }
    }

    public bool IsWelcomeStep => Step == UsbMappingWizardStep.Welcome;
    public bool IsSelectDeviceStep => Step == UsbMappingWizardStep.SelectDevice;
    public bool IsConfirmPortStep => Step == UsbMappingWizardStep.ConfirmCurrentPort;
    public bool IsMoveUsbStep => Step == UsbMappingWizardStep.MoveUsb;
    public bool IsDetectStep => Step == UsbMappingWizardStep.DetectChange;
    public bool IsLabelStep => Step == UsbMappingWizardStep.LabelPort;
    public bool IsDoneStep => Step == UsbMappingWizardStep.Done;

    public UsbMappingWizardDeviceOption? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                OnPropertyChanged(nameof(SelectedUsbTarget));
                RaiseAllCommands();
            }
        }
    }

    /// <summary>Wizard selection as <see cref="UsbTargetInfo"/> for topology capture.</summary>
    public UsbTargetInfo? SelectedUsbTarget => SelectedDevice?.Target;

    public bool UserConfirmedUsbMoved
    {
        get => _userConfirmedUsbMoved;
        set
        {
            if (SetProperty(ref _userConfirmedUsbMoved, value))
            {
                DetectPortChangeCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(BuildStateSnapshot));
            }
        }
    }

    public string CaptureSummary
    {
        get => _captureSummary;
        private set => SetProperty(ref _captureSummary, value);
    }

    public string ConfidenceAfterCapture
    {
        get => _confidenceAfterCapture;
        private set => SetProperty(ref _confidenceAfterCapture, value);
    }

    public bool DetectionSuccess
    {
        get => _detectionSuccess;
        private set
        {
            if (SetProperty(ref _detectionSuccess, value))
            {
                RefreshDetectChangeChrome();
                RaiseAllCommands();
            }
        }
    }

    /// <summary>Legacy binding name — prefer <see cref="ShowDetectFailureDetails"/>.</summary>
    public bool ShowDetectionFailureChrome => ShowDetectFailureDetails;

    public bool ShowDetectFailureDetails => IsDetectStep && !IsAnalyzingPortChange && !DetectionSuccess;

    public bool ShowDetectSuccessDetails => IsDetectStep && !IsAnalyzingPortChange && DetectionSuccess;

    public bool IsAnalyzingPortChange
    {
        get => _isAnalyzingPortChange;
        private set
        {
            if (SetProperty(ref _isAnalyzingPortChange, value))
            {
                OnPropertyChanged(nameof(ShowDetectSpinner));
                RefreshDetectChangeChrome();
                RaiseAllCommands();
            }
        }
    }

    public bool ShowDetectSpinner => IsDetectStep && IsAnalyzingPortChange;

#if DEBUG
    public bool ShowDetectChangeDebugDetails => IsDetectStep && !IsAnalyzingPortChange;
#else
    public bool ShowDetectChangeDebugDetails => false;
#endif

    public string DetectChangeDebugSummary
    {
        get => _detectChangeDebugSummary;
        private set => SetProperty(ref _detectChangeDebugSummary, value);
    }

    public string DetectChangePrimaryStatus
    {
        get => _detectChangePrimaryStatus;
        private set => SetProperty(ref _detectChangePrimaryStatus, value);
    }

    public string DetectChangeSubStatus
    {
        get => _detectChangeSubStatus;
        private set => SetProperty(ref _detectChangeSubStatus, value);
    }

    public string DetectionDetail
    {
        get => _detectionDetail;
        private set => SetProperty(ref _detectionDetail, value);
    }

    public string OldPortKeyShort
    {
        get => _oldPortKeyShort;
        private set => SetProperty(ref _oldPortKeyShort, value);
    }

    public string NewPortKeyShort
    {
        get => _newPortKeyShort;
        private set => SetProperty(ref _newPortKeyShort, value);
    }

    public string SpeedClassDisplay
    {
        get => _speedClassDisplay;
        private set => SetProperty(ref _speedClassDisplay, value);
    }

    public string ConfidenceTierDisplay
    {
        get => _confidenceTierDisplay;
        private set => SetProperty(ref _confidenceTierDisplay, value);
    }

    public string RecommendationDisplay
    {
        get => _recommendationDisplay;
        private set => SetProperty(ref _recommendationDisplay, value);
    }

    public string FailureMessage
    {
        get => _failureMessage;
        private set
        {
            if (SetProperty(ref _failureMessage, value))
            {
                RefreshDetectChangeChrome();
            }
        }
    }

    public string PortLabelDraft
    {
        get => _portLabelDraft;
        set
        {
            if (SetProperty(ref _portLabelDraft, value))
            {
                SavePortLabelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public UsbMappingWizardResult? DoneResult
    {
        get => _doneResult;
        private set
        {
            if (SetProperty(ref _doneResult, value))
            {
                RunBenchmarkOnThisPortCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanRetry => IsDetectStep && !DetectionSuccess && !IsAnalyzingPortChange;

    public UsbMappingWizardState BuildStateSnapshot() =>
        new()
        {
            Step = Step,
            SelectedTargetRootPath = SelectedUsbTarget?.RootPath,
            BeforeCaptured = _beforeCaptured,
            AfterCaptured = _afterSnap is not null,
            PortChangeDetected = DetectionSuccess,
            ConfidenceTier = ConfidenceTierDisplay,
            UserLabel = PortLabelDraft,
            ErrorMessage = string.IsNullOrEmpty(FailureMessage) ? null : FailureMessage,
            DetectionSummary = DetectionDetail,
            CanContinue = Step switch
            {
                UsbMappingWizardStep.SelectDevice => SelectedDevice is not null,
                UsbMappingWizardStep.ConfirmCurrentPort => _beforeCaptured,
                UsbMappingWizardStep.MoveUsb => UserConfirmedUsbMoved,
                UsbMappingWizardStep.DetectChange => DetectionSuccess,
                UsbMappingWizardStep.LabelPort => !string.IsNullOrWhiteSpace(PortLabelDraft),
                _ => true
            },
            CanRetry = CanRetry,
            CanSaveManualLabel = IsDetectStep && !DetectionSuccess && SelectedDevice is not null && !IsAnalyzingPortChange,
            UserConfirmedUsbMoved = UserConfirmedUsbMoved,
            PendingSaveMode = _pendingSaveMode
        };

    /// <summary>For unit tests — same work as <see cref="DetectPortChangeCommand"/>.</summary>
    internal Task DetectPortChangeAsync() => DetectPortChangeCoreAsync();

    private void RefreshDetectChangeChrome()
    {
        OnPropertyChanged(nameof(ShowDetectFailureDetails));
        OnPropertyChanged(nameof(ShowDetectSuccessDetails));
        OnPropertyChanged(nameof(ShowDetectionFailureChrome));
        OnPropertyChanged(nameof(ShowDetectChangeDebugDetails));
    }

    private void StartMapping()
    {
        _workflow.StartMappingSession();
        ReloadDeviceOptions();
        Step = UsbMappingWizardStep.SelectDevice;
    }

    private void ReloadDeviceOptions()
    {
        DeviceOptions.Clear();
        foreach (var t in _getUsbTargets().Where(UsbMappingWizardDeviceFilter.IsEligibleMappingUsb))
        {
            var snap = _intelligence.BuildTopologySnapshot(t);
            var rec = snap.SelectedTargetRecommendation;
            var bench = snap.SelectedTargetBenchmark;
            var benchLine = bench?.Succeeded == true
                ? $"{bench.WriteSpeedMBps:0.0} / {bench.ReadSpeedMBps:0.0} MB/s"
                : t.BenchmarkStatusDisplay;
            DeviceOptions.Add(new UsbMappingWizardDeviceOption
            {
                RootPath = t.RootPath,
                DriveLetterDisplay = string.IsNullOrWhiteSpace(t.DriveLetter) ? "—" : t.DriveLetter.TrimEnd('\\'),
                VolumeLabelDisplay = t.LabelDisplay,
                SizeDisplay = t.DisplayTotalBytes,
                FileSystemDisplay = string.IsNullOrWhiteSpace(t.FileSystem) ? "—" : t.FileSystem,
                DetectedClassDisplay = rec?.ClassificationLine ?? t.RoleDisplay,
                LastBenchmarkDisplay = benchLine,
                MappingLabelDisplay = string.IsNullOrWhiteSpace(snap.SelectedTargetPortUserLabel)
                    ? "—"
                    : snap.SelectedTargetPortUserLabel!,
                Target = t
            });
        }
    }

    private void GoConfirmPort()
    {
        Step = UsbMappingWizardStep.ConfirmCurrentPort;
        _beforeCaptured = false;
        CaptureSummary = string.Empty;
        ConfidenceAfterCapture = string.Empty;
        NextAfterCaptureCommand.RaiseCanExecuteChanged();
    }

    private void CaptureCurrentPort()
    {
        if (SelectedUsbTarget is null)
        {
            return;
        }

        _beforeSnap = _intelligence.BuildTopologySnapshot(SelectedUsbTarget);
        _workflow.CaptureBeforeSnapshot(_beforeSnap);
        _beforeCaptured = true;
        CaptureSummary =
            $"{SelectedUsbTarget.LabelDisplay} · {SelectedUsbTarget.DisplayTotalBytes} · {SelectedUsbTarget.FileSystem}";
        ConfidenceAfterCapture =
            $"Score {_beforeSnap.CombinedConfidenceScore} — {_beforeSnap.CombinedConfidenceReason}";
        NextAfterCaptureCommand.RaiseCanExecuteChanged();
    }

    private async Task DetectPortChangeCoreAsync()
    {
        if (SelectedUsbTarget is null || _beforeSnap is null)
        {
            StartupDiagnosticLog.AppendLine("[UsbMappingWizard] Detection aborted: missing device or before snapshot.");
            IsAnalyzingPortChange = false;
            DetectionSuccess = false;
            FailureMessage = "ForgerEMS could not start detection (missing capture or device).";
            DetectionDetail = "Go Back, confirm the USB, and capture the current port again.";
            DetectChangePrimaryStatus = FailureMessage;
            DetectChangeSubStatus = string.Empty;
            Step = UsbMappingWizardStep.DetectChange;
            RefreshDetectChangeChrome();
            return;
        }

        IsAnalyzingPortChange = true;
        DetectionSuccess = false;
        FailureMessage = string.Empty;
        DetectChangePrimaryStatus = "Detecting USB topology changes...";
        DetectChangeSubStatus = "Analyzing port change...";
        DetectChangeDebugSummary = string.Empty;
        Step = UsbMappingWizardStep.DetectChange;
        StartupDiagnosticLog.AppendLine("[UsbMappingWizard] Detection started (async).");

        var beforeCount = _beforeSnap.Devices.Count;

        Task SlowHintAsync(Task work)
        {
            return Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(6), CancellationToken.None).ConfigureAwait(false);
                    if (work.IsCompleted)
                    {
                        return;
                    }

                    var dispatcher = Application.Current?.Dispatcher;
                    if (dispatcher is null)
                    {
                        return;
                    }

                    await dispatcher.InvokeAsync(() =>
                    {
                        if (IsAnalyzingPortChange && Step == UsbMappingWizardStep.DetectChange)
                        {
                            DetectChangeSubStatus = "Still analyzing... this may take a few seconds.";
                        }
                    });
                }
                catch
                {
                    // ignore
                }
            }, CancellationToken.None);
        }

        try
        {
            var work = Task.Run(() =>
            {
                var after = _intelligence.BuildTopologySnapshot(SelectedUsbTarget);
                var resolution = UsbMappingPortResolution.Resolve(_beforeSnap, after, SelectedUsbTarget);
                return (after, resolution);
            });

            _ = SlowHintAsync(work);

            (UsbTopologySnapshot after, UsbPortMappingResolution resolution) result;
            try
            {
                result = await work.WaitAsync(_detectOperationTimeout).ConfigureAwait(true);
            }
            catch (TimeoutException)
            {
                StartupDiagnosticLog.AppendLine("[UsbMappingWizard] Detection timeout triggered.");
                DetectionSuccess = false;
                FailureMessage = "Detection is taking longer than expected. You can retry or save manually.";
                DetectionDetail =
                    "The USB topology scan did not finish in time. Try again after moving the device, or save a manual label for the current port.";
                DetectChangePrimaryStatus = FailureMessage;
                DetectChangeSubStatus = string.Empty;
#if DEBUG
                DetectChangeDebugSummary = FormattableString.Invariant(
                    $"Before devices: {beforeCount} · After: (timeout) · Match: —");
                OnPropertyChanged(nameof(ShowDetectChangeDebugDetails));
#endif
                RefreshDetectChangeChrome();
                return;
            }

            _afterSnap = result.after;
            _workflow.CaptureAfterSnapshot(_afterSnap);
            _lastResolution = result.resolution;

            var afterCount = _afterSnap.Devices.Count;
#if DEBUG
            var matchLine = _lastResolution is null
                ? "—"
                : $"{(_lastResolution.Success ? "matched" : "no match")} · kind={_lastResolution.MatchKind} · conf={_lastResolution.ConfidenceTier}";
            DetectChangeDebugSummary = FormattableString.Invariant(
                $"Before devices: {beforeCount} · After devices: {afterCount} · {matchLine}");
            OnPropertyChanged(nameof(ShowDetectChangeDebugDetails));
#endif

            if (_lastResolution.Success)
            {
                DetectionSuccess = true;
                FailureMessage = string.Empty;
                OldPortKeyShort = _lastResolution.OldPortKeyShort;
                NewPortKeyShort = _lastResolution.NewPortKeyShort;
                SpeedClassDisplay = _lastResolution.AfterDevice?.InferredSpeed.ToString() ?? "Unknown";
                ConfidenceTierDisplay = _lastResolution.ConfidenceTier;
                RecommendationDisplay = _afterSnap.SelectedTargetRecommendation?.Summary ?? string.Empty;
                DetectionDetail = "Port change detected.";
                _pendingSaveMode = UsbPortMappingSaveMode.TopologyInference;
                DetectChangePrimaryStatus = "Port change detected.";
                DetectChangeSubStatus = string.Empty;
                StartupDiagnosticLog.AppendLine("[UsbMappingWizard] Detection completed successfully.");
            }
            else
            {
                DetectionSuccess = false;
                FailureMessage =
                    "ForgerEMS could not confidently detect a port change.";
                DetectionDetail = _lastResolution?.UserHint ?? FailureMessage;
                OldPortKeyShort = string.Empty;
                NewPortKeyShort = string.Empty;
                SpeedClassDisplay = string.Empty;
                ConfidenceTierDisplay = "Low";
                RecommendationDisplay =
                    "Windows did not expose enough USB topology data. You can still save a manual label for the currently selected port.";
                DetectChangePrimaryStatus = FailureMessage;
                DetectChangeSubStatus = string.Empty;
                StartupDiagnosticLog.AppendLine("[UsbMappingWizard] Detection completed without a confident port change.");
            }
        }
        catch (Exception ex)
        {
            StartupDiagnosticLog.AppendException("UsbMappingWizard.DetectPortChange", ex);
            DetectionSuccess = false;
            FailureMessage = "ForgerEMS could not confidently detect a port change.";
            DetectionDetail = ex.Message;
            DetectChangePrimaryStatus = FailureMessage;
            DetectChangeSubStatus = string.Empty;
        }
        finally
        {
            IsAnalyzingPortChange = false;
            if (DetectionSuccess)
            {
                DetectChangePrimaryStatus = "Port change detected.";
            }
            else if (string.IsNullOrWhiteSpace(DetectChangePrimaryStatus))
            {
                DetectChangePrimaryStatus = FailureMessage.Length > 0
                    ? FailureMessage
                    : "Waiting for USB change...";
            }

            TryAgainCommand.RaiseCanExecuteChanged();
            UseCurrentPortAnywayCommand.RaiseCanExecuteChanged();
            SaveManualLabelPathCommand.RaiseCanExecuteChanged();
            BackFromDetectCommand.RaiseCanExecuteChanged();
            NextToLabelCommand.RaiseCanExecuteChanged();
            RefreshDetectChangeChrome();
        }
    }

    private void TryDetectionAgain()
    {
        _workflow.ClearAfterSnapshotForRetry();
        UserConfirmedUsbMoved = false;
        _afterSnap = null;
        DetectionSuccess = false;
        FailureMessage = string.Empty;
        DetectionDetail = string.Empty;
        DetectChangePrimaryStatus = string.Empty;
        DetectChangeSubStatus = string.Empty;
        DetectChangeDebugSummary = string.Empty;
        Step = UsbMappingWizardStep.MoveUsb;
        TryAgainCommand.RaiseCanExecuteChanged();
    }

    private void BackFromDetect()
    {
        _workflow.ClearAfterSnapshotForRetry();
        _afterSnap = null;
        DetectionSuccess = false;
        FailureMessage = string.Empty;
        DetectionDetail = string.Empty;
        DetectChangePrimaryStatus = string.Empty;
        DetectChangeSubStatus = string.Empty;
        DetectChangeDebugSummary = string.Empty;
        Step = UsbMappingWizardStep.MoveUsb;
        TryAgainCommand.RaiseCanExecuteChanged();
    }

    private void UseCurrentPortAnyway()
    {
        _pendingSaveMode = UsbPortMappingSaveMode.CurrentPortForSelectedTarget;
        ConfidenceTierDisplay = "Manual";
        DetectionSuccess = true;
        FailureMessage = string.Empty;
        Step = UsbMappingWizardStep.LabelPort;
        NextToLabelCommand.RaiseCanExecuteChanged();
    }

    private void SavePortLabel()
    {
        var profile = _profileStore.LoadOrCreate();
        var label = PortLabelDraft.Trim();
        if (!_workflow.TrySaveMappingLabel(
                profile,
                _profileStore,
                label,
                out var inf,
                out var err,
                SelectedUsbTarget,
                _pendingSaveMode))
        {
            FailureMessage = err;
            return;
        }

        var target = SelectedUsbTarget;
        DoneResult = new UsbMappingWizardResult
        {
            Saved = true,
            Label = label,
            ConfidenceTier = ConfidenceTierDisplay,
            BenchmarkStatus = target?.BenchmarkStatusDisplay ?? "—",
            Recommendation = inf.SuggestionLine,
            MappedTarget = target
        };
        Step = UsbMappingWizardStep.Done;
    }

    private async Task RunBenchmarkFromDoneAsync()
    {
        if (_doneResult?.MappedTarget is null || _runBenchmarkForTargetAsync is null)
        {
            return;
        }

        await _runBenchmarkForTargetAsync(_doneResult.MappedTarget).ConfigureAwait(true);
    }

    private void RestartWizard()
    {
        PortLabelDraft = string.Empty;
        UserConfirmedUsbMoved = false;
        _beforeCaptured = false;
        _beforeSnap = null;
        _afterSnap = null;
        DetectionSuccess = false;
        FailureMessage = string.Empty;
        ConfidenceTierDisplay = string.Empty;
        DoneResult = null;
        _pendingSaveMode = UsbPortMappingSaveMode.TopologyInference;
        IsAnalyzingPortChange = false;
        DetectChangePrimaryStatus = string.Empty;
        DetectChangeSubStatus = string.Empty;
        DetectChangeDebugSummary = string.Empty;
        _workflow.StartMappingSession();
        ReloadDeviceOptions();
        Step = UsbMappingWizardStep.Welcome;
        RaiseAllCommands();
    }

    private void RaiseAllCommands()
    {
        StartMappingCommand.RaiseCanExecuteChanged();
        ContinueSelectDeviceCommand.RaiseCanExecuteChanged();
        CaptureCurrentPortCommand.RaiseCanExecuteChanged();
        NextAfterCaptureCommand.RaiseCanExecuteChanged();
        DetectPortChangeCommand.RaiseCanExecuteChanged();
        NextToLabelCommand.RaiseCanExecuteChanged();
        TryAgainCommand.RaiseCanExecuteChanged();
        UseCurrentPortAnywayCommand.RaiseCanExecuteChanged();
        SaveManualLabelPathCommand.RaiseCanExecuteChanged();
        BackFromDetectCommand.RaiseCanExecuteChanged();
        SavePortLabelCommand.RaiseCanExecuteChanged();
        MapAnotherPortCommand.RaiseCanExecuteChanged();
    }
}
