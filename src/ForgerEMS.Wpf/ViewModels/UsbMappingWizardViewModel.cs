using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VentoyToolkitSetup.Wpf.Configuration;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Intelligence;

namespace VentoyToolkitSetup.Wpf.ViewModels;

public enum UsbMappingWizardDetectionPhase
{
    ReadyToCaptureCurrentPort = 0,
    CurrentPortCaptured = 1,
    WaitingForRemoval = 2,
    RemovalObserved = 3,
    WaitingForReinsert = 4,
    ReinsertObserved = 5,
    CheckingTopology = 6,
    Mapped = 7,
    ManualLabelRecommended = 8
}

public sealed class UsbMappingWizardViewModel : ObservableObject
{
    private static readonly TimeSpan DetectionPollInterval = TimeSpan.FromMilliseconds(650);

    private readonly IUsbIntelligenceService _intelligence;
    private readonly UsbMachineProfileStore _profileStore;
    private readonly Func<IReadOnlyList<UsbTargetInfo>> _getUsbTargets;
    private readonly Func<UsbTargetInfo, Task>? _runBenchmarkForTargetAsync;
    private readonly Func<string?, bool> _isDriveRootMounted;
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
    private UsbMappingWizardDetectionPhase _detectionPhase = UsbMappingWizardDetectionPhase.ReadyToCaptureCurrentPort;
    private string _detectChangePrimaryStatus = string.Empty;
    private string _detectChangeSubStatus = string.Empty;
    private string _detectChangeDebugSummary = string.Empty;

    public UsbMappingWizardViewModel(
        IUsbIntelligenceService intelligence,
        UsbMachineProfileStore profileStore,
        Func<IReadOnlyList<UsbTargetInfo>> getUsbTargets,
        Func<UsbTargetInfo, Task>? runBenchmarkForTargetAsync = null,
        TimeSpan? detectOperationTimeoutOverride = null,
        Func<string?, bool>? isDriveRootMounted = null)
    {
        _intelligence = intelligence;
        _profileStore = profileStore;
        _getUsbTargets = getUsbTargets;
        _runBenchmarkForTargetAsync = runBenchmarkForTargetAsync;
        _isDriveRootMounted = isDriveRootMounted ?? DefaultDriveRootMounted;
        _detectOperationTimeout = detectOperationTimeoutOverride ?? TimeSpan.FromSeconds(28);

        StartMappingCommand = new RelayCommand(StartMapping, () => Step == UsbMappingWizardStep.Welcome);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(this, false));
        ContinueSelectDeviceCommand = new RelayCommand(GoConfirmPort, () => SelectedDevice is not null);
        CaptureCurrentPortCommand = new RelayCommand(CaptureCurrentPort, () => SelectedDevice is not null);
        NextAfterCaptureCommand = new RelayCommand(() => Step = UsbMappingWizardStep.MoveUsb, () => _beforeCaptured);
        DetectPortChangeCommand = new AsyncRelayCommand(DetectPortChangeAsync, () => _beforeCaptured && SelectedDevice is not null && !_isAnalyzingPortChange);
        NextToLabelCommand = new RelayCommand(() => Step = UsbMappingWizardStep.LabelPort, () => _detectionSuccess);
        TryAgainCommand = new AsyncRelayCommand(TryDetectionAgainAsync, () => CanRetry);
        UseCurrentPortAnywayCommand = new RelayCommand(UseCurrentPortAnyway, () => !_detectionSuccess && SelectedDevice is not null && !IsAnalyzingPortChange);
        SaveManualLabelPathCommand = new RelayCommand(UseCurrentPortAnyway, () => !_detectionSuccess && SelectedDevice is not null && !IsAnalyzingPortChange);
        ConfirmSavedPortLabelCommand = new RelayCommand<string>(ConfirmSavedPortLabel, mappingId => !string.IsNullOrWhiteSpace(mappingId) && SelectedDevice is not null && !IsAnalyzingPortChange);
        RenameSavedPortLabelCommand = new RelayCommand<string>(RenameSavedPortLabel, mappingId => !string.IsNullOrWhiteSpace(mappingId) && !string.IsNullOrWhiteSpace(PortLabelDraft) && !IsAnalyzingPortChange);
        DeleteSavedPortLabelCommand = new RelayCommand<string>(DeleteSavedPortLabel, mappingId => !string.IsNullOrWhiteSpace(mappingId) && !IsAnalyzingPortChange);
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
    public AsyncRelayCommand TryAgainCommand { get; }
    public RelayCommand UseCurrentPortAnywayCommand { get; }
    public RelayCommand SaveManualLabelPathCommand { get; }
    public RelayCommand<string> ConfirmSavedPortLabelCommand { get; }
    public RelayCommand<string> RenameSavedPortLabelCommand { get; }
    public RelayCommand<string> DeleteSavedPortLabelCommand { get; }
    public RelayCommand BackFromDetectCommand { get; }
    public RelayCommand SavePortLabelCommand { get; }
    public RelayCommand CloseCommand { get; }
    public RelayCommand MapAnotherPortCommand { get; }
    public AsyncRelayCommand RunBenchmarkOnThisPortCommand { get; }

    public ObservableCollection<UsbMappingWizardDeviceOption> DeviceOptions { get; } = new();

    public ObservableCollection<UsbSavedPortLabelOption> SavedPortLabels { get; } = new();

    public bool HasSavedPortLabels => SavedPortLabels.Count > 0;

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

    public bool ShowRemovalNotObservedActions =>
        ShowDetectFailureDetails && DetectionPhase == UsbMappingWizardDetectionPhase.WaitingForRemoval;

    public bool ShowManualLabelRecommendedActions =>
        ShowDetectFailureDetails && !ShowRemovalNotObservedActions;

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

    public bool ShowDetectChangeDebugDetails => IsDetectStep && !IsAnalyzingPortChange && IsUsbMappingDebugUiEnabled();

    public UsbMappingWizardDetectionPhase DetectionPhase
    {
        get => _detectionPhase;
        private set
        {
            if (SetProperty(ref _detectionPhase, value))
            {
                OnPropertyChanged(nameof(ShowRemovalNotObservedActions));
                OnPropertyChanged(nameof(ShowManualLabelRecommendedActions));
                OnPropertyChanged(nameof(BuildStateSnapshot));
            }
        }
    }

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
                RenameSavedPortLabelCommand.RaiseCanExecuteChanged();
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
                UsbMappingWizardStep.MoveUsb => _beforeCaptured,
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
        OnPropertyChanged(nameof(ShowRemovalNotObservedActions));
        OnPropertyChanged(nameof(ShowManualLabelRecommendedActions));
        OnPropertyChanged(nameof(ShowDetectChangeDebugDetails));
    }

    private void SetDetectChangeStatus(string primary, string subStatus)
    {
        DetectChangePrimaryStatus = primary;
        DetectChangeSubStatus = subStatus;
    }

    private void StartMapping()
    {
        _workflow.StartMappingSession();
        DetectionPhase = UsbMappingWizardDetectionPhase.ReadyToCaptureCurrentPort;
        ReloadDeviceOptions();
        Step = UsbMappingWizardStep.SelectDevice;
    }

    private void ReloadDeviceOptions()
    {
        DeviceOptions.Clear();
        ReloadSavedPortLabels();
        foreach (var t in _getUsbTargets().Where(UsbMappingWizardDeviceFilter.IsEligibleMappingUsb))
        {
            var profile = _profileStore.LoadOrCreate();
            var snap = _intelligence.BuildTopologySnapshot(
                t,
                new UsbTopologyBuildOptions { MachineProfile = profile });
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
        DetectionPhase = UsbMappingWizardDetectionPhase.ReadyToCaptureCurrentPort;
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
        DetectionPhase = UsbMappingWizardDetectionPhase.CurrentPortCaptured;
        NextAfterCaptureCommand.RaiseCanExecuteChanged();
    }

    private UsbTargetInfo? FindSelectedLiveTarget()
    {
        var selected = SelectedUsbTarget;
        if (selected is null)
        {
            return null;
        }

        var selectedRoot = NormalizeRootPath(selected.RootPath);
        var selectedLetter = NormalizeDriveLetter(selected.DriveLetter);
        var match = _getUsbTargets()
            .Where(UsbMappingWizardDeviceFilter.IsEligibleMappingUsb)
            .FirstOrDefault(candidate =>
                string.Equals(NormalizeRootPath(candidate.RootPath), selectedRoot, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(selectedLetter) &&
                 string.Equals(NormalizeDriveLetter(candidate.DriveLetter), selectedLetter, StringComparison.OrdinalIgnoreCase) &&
                 candidate.TotalBytes == selected.TotalBytes));

        if (match is not null)
        {
            return IsTargetMounted(match) ? match : null;
        }

        return IsTargetMounted(selected) ? selected : null;
    }

    private static string NormalizeRootPath(string? rootPath) =>
        string.IsNullOrWhiteSpace(rootPath) ? string.Empty : rootPath.TrimEnd('\\');

    private static string NormalizeDriveLetter(string? driveLetter) =>
        string.IsNullOrWhiteSpace(driveLetter) ? string.Empty : driveLetter.TrimEnd('\\').TrimEnd(':');

    private bool IsTargetMounted(UsbTargetInfo target)
    {
        if (!string.IsNullOrWhiteSpace(target.RootPath) && _isDriveRootMounted(target.RootPath))
        {
            return true;
        }

        var letter = NormalizeDriveLetter(target.DriveLetter);
        return !string.IsNullOrWhiteSpace(letter) && _isDriveRootMounted(letter + ":\\");
    }

    private static bool DefaultDriveRootMounted(string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return false;
        }

        try
        {
            return System.IO.Directory.Exists(rootPath);
        }
        catch
        {
            return false;
        }
    }

    private void ReloadSavedPortLabels()
    {
        SavedPortLabels.Clear();
        var profile = _profileStore.LoadOrCreate();
        foreach (var rec in profile.KnownPorts
                     .Where(p => !string.IsNullOrWhiteSpace(p.UserLabel))
                     .OrderBy(p => p.UserLabel, StringComparer.OrdinalIgnoreCase))
        {
            var lastSeen = rec.LastSeenUtc?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "—";
            var benchmark = rec.LastBenchmark?.Succeeded == true
                ? $"{rec.LastBenchmark.WriteSpeedMBps:0.0} MB/s write"
                : "—";
            var verification = rec.HasStrongPortTopologyEvidence
                ? "topology evidence saved"
                : "weak topology; confirm when connected";
            SavedPortLabels.Add(new UsbSavedPortLabelOption
            {
                MappingId = rec.MappingId,
                Label = UsbPortLabelNormalizer.CanonicalizeDisplay(rec.UserLabel),
                NormalizedLabelKey = rec.NormalizedLabelKey,
                LastSeenDisplay = "Last seen: " + lastSeen,
                LastBenchmarkDisplay = "Benchmark: " + benchmark,
                VerificationDisplay = verification
            });
        }

        OnPropertyChanged(nameof(HasSavedPortLabels));
        ConfirmSavedPortLabelCommand.RaiseCanExecuteChanged();
        RenameSavedPortLabelCommand.RaiseCanExecuteChanged();
        DeleteSavedPortLabelCommand.RaiseCanExecuteChanged();
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
        DetectionDetail = string.Empty;
        RecommendationDisplay = string.Empty;
        DetectChangePrimaryStatus = "Waiting for USB Removal";
        DetectChangeSubStatus = "Unplug the selected USB now. ForgerEMS is waiting for Windows to report removal.";
        DetectChangeDebugSummary = string.Empty;
        DetectionPhase = UsbMappingWizardDetectionPhase.WaitingForRemoval;
        Step = UsbMappingWizardStep.DetectChange;
        var mappingRunId = Guid.NewGuid().ToString("N");
        StartupDiagnosticLog.AppendLine($"[UsbMappingWizard] Detection started. mappingRunId={mappingRunId}");

        var beforeCount = _beforeSnap.Devices.Count;

        try
        {
            using var timeout = new CancellationTokenSource(_detectOperationTimeout);
            var token = timeout.Token;

            var removalObserved = await WaitForSelectedUsbRemovalAsync(mappingRunId, token).ConfigureAwait(true);
            if (!removalObserved)
            {
                await ShowRemovalNotObservedAsync(mappingRunId, beforeCount).ConfigureAwait(true);
                return;
            }

            DetectionPhase = UsbMappingWizardDetectionPhase.RemovalObserved;
            StartupDiagnosticLog.AppendLine($"[UsbMappingWizard] Removal observed. mappingRunId={mappingRunId}");
            SetDetectChangeStatus(
                "USB removed. Plug it into the port you want to map.",
                "Waiting for Windows to mount the selected USB again.");
            DetectionPhase = UsbMappingWizardDetectionPhase.WaitingForReinsert;

            var liveTarget = await WaitForSelectedUsbReinsertAsync(mappingRunId, token).ConfigureAwait(true);
            if (liveTarget is null)
            {
                ShowReinsertNotObserved(mappingRunId, beforeCount);
                return;
            }

            DetectionPhase = UsbMappingWizardDetectionPhase.ReinsertObserved;
            StartupDiagnosticLog.AppendLine($"[UsbMappingWizard] Reinsert observed. mappingRunId={mappingRunId}");
            SetDetectChangeStatus(
                "Checking port identity...",
                "Comparing available Windows topology fields for the selected drive.");
            DetectionPhase = UsbMappingWizardDetectionPhase.CheckingTopology;

            _afterSnap = await Task.Run(() => _intelligence.BuildTopologySnapshot(liveTarget), token).ConfigureAwait(true);
            _workflow.CaptureAfterSnapshot(_afterSnap);
            _lastResolution = UsbMappingPortResolution.Resolve(_beforeSnap, _afterSnap, SelectedUsbTarget);

            var afterCount = _afterSnap.Devices.Count;
            var matchLine = _lastResolution is null
                ? "—"
                : $"{(_lastResolution.Success ? "matched" : "no match")} · kind={_lastResolution.MatchKind} · conf={_lastResolution.ConfidenceTier}";
            DetectChangeDebugSummary = FormattableString.Invariant(
                $"Before devices: {beforeCount} · After devices: {afterCount} · {matchLine}");
            OnPropertyChanged(nameof(ShowDetectChangeDebugDetails));
            LogMappingAttempt(mappingRunId, beforeCount, afterCount, _lastResolution, removalObserved: true, reinsertObserved: true);

            if (_lastResolution is { Success: true } resolution)
            {
                DetectionPhase = UsbMappingWizardDetectionPhase.Mapped;
                DetectionSuccess = true;
                FailureMessage = string.Empty;
                OldPortKeyShort = resolution.OldPortKeyShort;
                NewPortKeyShort = resolution.NewPortKeyShort;
                SpeedClassDisplay = resolution.AfterDevice?.InferredSpeed.ToString() ?? "Unknown";
                ConfidenceTierDisplay = resolution.ConfidenceTier;
                RecommendationDisplay = _afterSnap.SelectedTargetRecommendation?.Summary ?? string.Empty;
                DetectionDetail = "Port change detected.";
                _pendingSaveMode = UsbPortMappingSaveMode.TopologyInference;
                DetectChangePrimaryStatus = $"Port mapped with {resolution.ConfidenceTier.ToLowerInvariant()} confidence.";
                DetectChangeSubStatus = resolution.UserHint;
                StartupDiagnosticLog.AppendLine($"[UsbMappingWizard] Detection completed successfully. mappingRunId={mappingRunId}");
            }
            else
            {
                DetectionPhase = UsbMappingWizardDetectionPhase.ManualLabelRecommended;
                DetectionSuccess = false;
                FailureMessage = "Manual Label Recommended";
                DetectionDetail =
                    "ForgerEMS saw the USB return, but Windows did not expose a reliable physical port path.";
                OldPortKeyShort = string.Empty;
                NewPortKeyShort = string.Empty;
                SpeedClassDisplay = string.Empty;
                ConfidenceTierDisplay = _lastResolution?.ConfidenceTier ?? "Manual";
                RecommendationDisplay =
                    "Save a manual label like Left USB-A, Right USB-C, Rear USB-A, or Dock Port 1.";
                DetectChangePrimaryStatus = FailureMessage;
                DetectChangeSubStatus = "Save Manual Label is recommended for beta-safe port tracking.";
                StartupDiagnosticLog.AppendLine($"[UsbMappingWizard] Detection completed without a confident port change. mappingRunId={mappingRunId}");
            }
        }
        catch (OperationCanceledException)
        {
            if (DetectionPhase == UsbMappingWizardDetectionPhase.WaitingForRemoval)
            {
                await ShowRemovalNotObservedAsync(mappingRunId, beforeCount).ConfigureAwait(true);
            }
            else
            {
                ShowReinsertNotObserved(mappingRunId, beforeCount);
            }
        }
        catch (Exception ex)
        {
            StartupDiagnosticLog.AppendException("UsbMappingWizard.DetectPortChange", ex);
            DetectionPhase = UsbMappingWizardDetectionPhase.ManualLabelRecommended;
            DetectionSuccess = false;
            FailureMessage = "Manual Label Recommended";
            DetectionDetail =
                "ForgerEMS could not complete the topology check. Save a manual label or try the removal/reinsert flow again.";
            RecommendationDisplay =
                "Save a manual label like Left USB-A, Right USB-C, Rear USB-A, or Dock Port 1.";
            DetectChangePrimaryStatus = FailureMessage;
            DetectChangeSubStatus = string.Empty;
        }
        finally
        {
            IsAnalyzingPortChange = false;
            if (DetectionSuccess)
            {
                DetectChangePrimaryStatus = string.IsNullOrWhiteSpace(ConfidenceTierDisplay)
                    ? "Port change detected."
                    : $"Port mapped with {ConfidenceTierDisplay.ToLowerInvariant()} confidence.";
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

    private async Task<bool> WaitForSelectedUsbRemovalAsync(string mappingRunId, CancellationToken token)
    {
        var poll = 0;
        while (!token.IsCancellationRequested)
        {
            poll++;
            if (FindSelectedLiveTarget() is null)
            {
                StartupDiagnosticLog.AppendLine($"[UsbMappingWizard] Removal poll observed selected USB missing. mappingRunId={mappingRunId} poll={poll}");
                var epoch = UsbPortLabelResolver.MarkDriveRemoved(SelectedUsbTarget?.RootPath ?? SelectedUsbTarget?.DriveLetter);
                StartupDiagnosticLog.AppendLine($"[UsbMappingWizard] Resolver connection epoch advanced after wizard removal. mappingRunId={mappingRunId} epoch={epoch}");
                return true;
            }

            SetDetectChangeStatus(
                "Waiting for USB Removal",
                "Unplug the selected USB drive. When Windows reports it was removed, plug it into the port you want to map.");
            await Task.Delay(DetectionPollInterval, token).ConfigureAwait(true);
        }

        return false;
    }

    private async Task<UsbTargetInfo?> WaitForSelectedUsbReinsertAsync(string mappingRunId, CancellationToken token)
    {
        var poll = 0;
        while (!token.IsCancellationRequested)
        {
            poll++;
            var liveTarget = FindSelectedLiveTarget();
            if (liveTarget is not null)
            {
                StartupDiagnosticLog.AppendLine($"[UsbMappingWizard] Reinsert poll observed selected USB present. mappingRunId={mappingRunId} poll={poll}");
                return liveTarget;
            }

            SetDetectChangeStatus(
                "USB removed. Plug it into the port you want to map.",
                "Waiting for Windows to mount the selected USB again.");
            await Task.Delay(DetectionPollInterval, token).ConfigureAwait(true);
        }

        return null;
    }

    private async Task ShowRemovalNotObservedAsync(string mappingRunId, int beforeCount)
    {
        DetectionPhase = UsbMappingWizardDetectionPhase.WaitingForRemoval;
        DetectionSuccess = false;
        FailureMessage = "Waiting for USB Removal";
        DetectionDetail =
            "Unplug the selected USB drive. When Windows reports it was removed, plug it into the port you want to map.";
        RecommendationDisplay =
            "Removal was not detected. Try unplugging the USB fully, avoid hubs/docks, or save a manual label.";
        DetectChangePrimaryStatus = FailureMessage;
        DetectChangeSubStatus = RecommendationDisplay;
        ConfidenceTierDisplay = string.Empty;
        OldPortKeyShort = string.Empty;
        NewPortKeyShort = string.Empty;
        SpeedClassDisplay = string.Empty;
        var liveTarget = FindSelectedLiveTarget();
        if (liveTarget is not null)
        {
            _afterSnap = await Task.Run(() => _intelligence.BuildTopologySnapshot(liveTarget)).ConfigureAwait(true);
            _workflow.CaptureAfterSnapshot(_afterSnap);
        }

        DetectChangeDebugSummary = FormattableString.Invariant(
            $"Before devices: {beforeCount} · After: {(liveTarget is null ? "(not mounted)" : "current")} · removal-not-observed");
        LogMappingAttempt(
            mappingRunId,
            beforeCount,
            _afterSnap?.Devices.Count ?? 0,
            new UsbPortMappingResolution
            {
                Success = false,
                MatchKind = UsbPortMappingMatchKind.None,
                ReasonCodes = ["target-not-removed-before-reinsert"],
                UserHint = "Removal was not detected before topology comparison."
            },
            removalObserved: false,
            reinsertObserved: liveTarget is not null);
        StartupDiagnosticLog.AppendLine($"[UsbMappingWizard] Removal timeout. mappingRunId={mappingRunId}");
    }

    private void ShowReinsertNotObserved(string mappingRunId, int beforeCount)
    {
        DetectionPhase = UsbMappingWizardDetectionPhase.WaitingForReinsert;
        DetectionSuccess = false;
        FailureMessage = "Waiting for USB Reinsert";
        DetectionDetail =
            "USB removal was detected. Plug the selected USB into the port you want to map and wait for Windows to mount it.";
        RecommendationDisplay =
            "If Windows does not remount the drive, unplug it fully, avoid hubs/docks, or save a manual label after it appears.";
        DetectChangePrimaryStatus = FailureMessage;
        DetectChangeSubStatus = RecommendationDisplay;
        ConfidenceTierDisplay = string.Empty;
        DetectChangeDebugSummary = FormattableString.Invariant(
            $"Before devices: {beforeCount} · After: (not remounted) · reinsert-not-observed");
        LogMappingAttempt(
            mappingRunId,
            beforeCount,
            0,
            new UsbPortMappingResolution
            {
                Success = false,
                MatchKind = UsbPortMappingMatchKind.None,
                ReasonCodes = ["selected-usb-not-detected-again"],
                UserHint = "Reinsert was not detected before timeout."
            },
            removalObserved: true,
            reinsertObserved: false);
        StartupDiagnosticLog.AppendLine($"[UsbMappingWizard] Reinsert timeout. mappingRunId={mappingRunId}");
    }

    internal Task TryDetectionAgainAsync()
    {
        _workflow.ClearAfterSnapshotForRetry();
        ClearDetectionStateForRetry();
        UserConfirmedUsbMoved = true;
        DetectChangePrimaryStatus = "Waiting for USB Removal";
        DetectChangeSubStatus = "Unplug the selected USB now. ForgerEMS is waiting for Windows to report removal.";
        DetectionPhase = UsbMappingWizardDetectionPhase.WaitingForRemoval;
        TryAgainCommand.RaiseCanExecuteChanged();
        return DetectPortChangeCoreAsync();
    }

    private void BackFromDetect()
    {
        _workflow.ClearAfterSnapshotForRetry();
        ClearDetectionStateForRetry();
        Step = UsbMappingWizardStep.MoveUsb;
        TryAgainCommand.RaiseCanExecuteChanged();
    }

    private void UseCurrentPortAnyway()
    {
        _pendingSaveMode = UsbPortMappingSaveMode.CurrentPortForSelectedTarget;
        ConfidenceTierDisplay = "Manual";
        DetectionSuccess = true;
        FailureMessage = string.Empty;
        DetectionDetail = "Manual label mode. Confidence is low because Windows did not confirm a physical port path.";
        RecommendationDisplay = "Save a friendly label for this observed port, then use benchmark results to compare ports during beta testing.";
        Step = UsbMappingWizardStep.LabelPort;
        NextToLabelCommand.RaiseCanExecuteChanged();
    }

    private void ConfirmSavedPortLabel(string? mappingId)
    {
        var option = SavedPortLabels.FirstOrDefault(p => string.Equals(p.MappingId, mappingId, StringComparison.Ordinal));
        if (option is null)
        {
            return;
        }

        PortLabelDraft = option.Label;
        _pendingSaveMode = UsbPortMappingSaveMode.CurrentPortForSelectedTarget;
        ConfidenceTierDisplay = "Manual";
        DetectionSuccess = true;
        FailureMessage = string.Empty;
        DetectionDetail = "Manual confirmation mode. Windows could not distinguish the physical port, so this label is user-confirmed for the current connection.";
        RecommendationDisplay = "Benchmark results can now attach to this confirmed label for the active USB connection.";
        SavePortLabel();
    }

    private void RenameSavedPortLabel(string? mappingId)
    {
        if (string.IsNullOrWhiteSpace(mappingId) || string.IsNullOrWhiteSpace(PortLabelDraft))
        {
            return;
        }

        var profile = _profileStore.LoadOrCreate();
        var rec = profile.KnownPorts.FirstOrDefault(p => string.Equals(p.MappingId, mappingId, StringComparison.Ordinal));
        if (rec is null)
        {
            FailureMessage = "Saved label was not found. Refresh the wizard and try again.";
            return;
        }

        var display = UsbPortLabelNormalizer.CanonicalizeDisplay(PortLabelDraft);
        var key = UsbPortLabelNormalizer.NormalizeKey(display);
        if (string.IsNullOrWhiteSpace(key))
        {
            FailureMessage = "Enter a new label first.";
            return;
        }

        var collision = profile.KnownPorts.FirstOrDefault(p =>
            !string.Equals(p.MappingId, rec.MappingId, StringComparison.Ordinal) &&
            string.Equals(p.NormalizedLabelKey, key, StringComparison.Ordinal));
        if (collision is not null)
        {
            FailureMessage = $"A saved label already uses {display}. The duplicate will be merged safely.";
        }

        rec.UserLabel = display;
        rec.NormalizedLabelKey = key;
        rec.UpdatedUtc = DateTimeOffset.UtcNow;
        _profileStore.Save(profile);
        ReloadSavedPortLabels();
        StartupDiagnosticLog.AppendLine(
            $"[UsbMappingWizard] savedPortLabelRenamed mappingId={rec.MappingId} normalizedKeyHash={SafeHash(key)}");
    }

    private void DeleteSavedPortLabel(string? mappingId)
    {
        if (string.IsNullOrWhiteSpace(mappingId))
        {
            return;
        }

        var profile = _profileStore.LoadOrCreate();
        var rec = profile.KnownPorts.FirstOrDefault(p => string.Equals(p.MappingId, mappingId, StringComparison.Ordinal));
        if (rec is null)
        {
            return;
        }

        var deletedLabel = rec.UserLabel?.Trim() ?? "(unlabeled)";
        profile.KnownPorts.Remove(rec);
        _profileStore.Save(profile);
        ReloadSavedPortLabels();
        StartupDiagnosticLog.AppendLine(
            $"[UsbMappingWizard] savedPortLabelDeleted mappingId={mappingId} labelHash={SafeHash(deletedLabel)}");
    }

    private void ClearDetectionStateForRetry()
    {
        _afterSnap = null;
        _lastResolution = null;
        _pendingSaveMode = UsbPortMappingSaveMode.TopologyInference;
        DetectionSuccess = false;
        FailureMessage = string.Empty;
        DetectionDetail = string.Empty;
        OldPortKeyShort = string.Empty;
        NewPortKeyShort = string.Empty;
        SpeedClassDisplay = string.Empty;
        ConfidenceTierDisplay = string.Empty;
        RecommendationDisplay = string.Empty;
        DetectChangePrimaryStatus = string.Empty;
        DetectChangeSubStatus = string.Empty;
        DetectChangeDebugSummary = string.Empty;
        DetectionPhase = UsbMappingWizardDetectionPhase.CurrentPortCaptured;
        RefreshDetectChangeChrome();
    }

    private void LogMappingAttempt(
        string mappingRunId,
        int beforeCount,
        int afterCount,
        UsbPortMappingResolution? resolution,
        bool removalObserved,
        bool reinsertObserved)
    {
        var target = SelectedUsbTarget;
        var selectedSummary =
            $"drive={NormalizeDriveLetter(target?.DriveLetter)} labelHash={SafeHash(target?.LabelDisplay)} sizeBytes={target?.TotalBytes ?? 0}";
        var reasonCodes = resolution is null
            ? "none"
            : string.Join(",", AppendFlowReasons(resolution.ReasonCodes, removalObserved, reinsertObserved));
        var present = resolution is null || resolution.PresentTopologyFields.Count == 0
            ? "none"
            : string.Join(",", resolution.PresentTopologyFields);
        var missing = resolution is null || resolution.MissingTopologyFields.Count == 0
            ? "none"
            : string.Join(",", resolution.MissingTopologyFields);
        StartupDiagnosticLog.AppendLine(
            $"[UsbMappingWizard] mappingAttempt mappingRunId={mappingRunId} beforeCount={beforeCount} afterCount={afterCount} selected={selectedSummary} " +
            $"matchedCandidateCount={resolution?.MatchedCandidateCount ?? 0} confidence={resolution?.ConfidenceTier ?? ""} kind={resolution?.MatchKind.ToString() ?? "None"} " +
            $"reasonCodes={reasonCodes} topologyPresent={present} topologyMissing={missing} removalObserved={removalObserved} reinsertObserved={reinsertObserved}");
    }

    private static string[] AppendFlowReasons(
        IReadOnlyList<string> reasonCodes,
        bool removalObserved,
        bool reinsertObserved)
    {
        var combined = reasonCodes.ToList();
        if (!removalObserved)
        {
            combined.Add("target-not-removed-before-reinsert");
        }

        if (!reinsertObserved)
        {
            combined.Add("selected-usb-not-detected-again");
        }

        return combined.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static string SafeHash(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "none"
            : UsbIdentityHasher.ShortKey(UsbIdentityHasher.Sha256Hex(value));

    private static bool IsUsbMappingDebugUiEnabled()
    {
        var optIn = Environment.GetEnvironmentVariable("FORGEREMS_USB_MAPPING_DEBUG_UI");
        return string.Equals(optIn, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(optIn, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ForgerEmsEnvironmentConfiguration.ForgerEmsEnv, "Development", StringComparison.OrdinalIgnoreCase);
    }

    private void SavePortLabel()
    {
        var profile = _profileStore.LoadOrCreate();
        var label = UsbPortLabelNormalizer.CanonicalizeDisplay(PortLabelDraft);
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

        ReloadSavedPortLabels();
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
        _lastResolution = null;
        DetectionSuccess = false;
        FailureMessage = string.Empty;
        DetectionDetail = string.Empty;
        OldPortKeyShort = string.Empty;
        NewPortKeyShort = string.Empty;
        SpeedClassDisplay = string.Empty;
        ConfidenceTierDisplay = string.Empty;
        RecommendationDisplay = string.Empty;
        DoneResult = null;
        _pendingSaveMode = UsbPortMappingSaveMode.TopologyInference;
        IsAnalyzingPortChange = false;
        DetectChangePrimaryStatus = string.Empty;
        DetectChangeSubStatus = string.Empty;
        DetectChangeDebugSummary = string.Empty;
        DetectionPhase = UsbMappingWizardDetectionPhase.ReadyToCaptureCurrentPort;
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
        ConfirmSavedPortLabelCommand.RaiseCanExecuteChanged();
        RenameSavedPortLabelCommand.RaiseCanExecuteChanged();
        DeleteSavedPortLabelCommand.RaiseCanExecuteChanged();
        BackFromDetectCommand.RaiseCanExecuteChanged();
        SavePortLabelCommand.RaiseCanExecuteChanged();
        MapAnotherPortCommand.RaiseCanExecuteChanged();
    }
}
