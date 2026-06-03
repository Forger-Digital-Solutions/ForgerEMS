#pragma warning disable CA1305 // Locale-sensitive calls; text is diagnostic/UI output
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services.DriveValidation;

namespace VentoyToolkitSetup.Wpf.ViewModels;

/// <summary>
/// View model behind <c>DriveValidatorWizardWindow</c>. Mirrors the structural pattern of
/// <see cref="UsbMappingWizardViewModel"/> but owns its own state — Phase 2 deliberately does
/// not extract a shared wizard base class, per architecture decision.
///
/// The wizard reuses the existing <see cref="IDriveValidationService"/> + region map; it does
/// not duplicate the validation algorithm. Progress callbacks publish <see cref="DriveValidationMap"/>
/// snapshots; the wizard reflects them into observable <see cref="DriveValidatorRegionTileViewModel"/>
/// instances so the tile UI can update without recreating the collection.
/// </summary>
public sealed class DriveValidatorWizardViewModel : ObservableObject, IDisposable
{
    public delegate Task<DriveValidationResult> RunValidationCallback(
        UsbTargetInfo target,
        DriveValidationOptions options,
        string? portPathHint,
        Action<DriveValidationProgress>? onProgress,
        CancellationToken cancellationToken);

    private readonly Func<IReadOnlyList<UsbTargetInfo>> _getTargets;
    private readonly Func<UsbTargetInfo, (bool isSafe, string reason)> _safetyEvaluator;
    private readonly Func<UsbTargetInfo, DriveValidationResult?> _lastValidationLookup;
    private readonly Func<UsbTargetInfo, string> _portLabelLookup;
    private readonly Func<string, string, bool>? _confirmHeavyMode;
    private readonly Action<string, LogSeverity>? _appendLog;
    private readonly RunValidationCallback _runValidationAsync;
    private readonly UsbTargetInfo? _preferredTarget;

    private DriveValidatorWizardStep _step = DriveValidatorWizardStep.SelectTarget;
    private DriveValidatorWizardTargetOption? _selectedTarget;
    private DriveValidatorWizardModeOption? _selectedMode;
    private bool _fullModeUserAcknowledged;
    private string _runningPhaseText = "—";
    private string _runningProgressText = "—";
    private double _runningProgressValue;
    private string _runningElapsedText = "—";
    private string _runningBytesText = "—";
    private string _runningSpeedText = "—";
    private CancellationTokenSource? _runCts;
    private bool _isRunning;
    private DriveValidationResult? _lastResult;
    private string _resultSummary = string.Empty;
    private string _resultEvidence = string.Empty;
    private string _resultLimitations = string.Empty;
    private DateTime _runStartedUtc;
    private DateTime _lastProgressUtc;
    private bool _heartbeatActive;
    private string _heartbeatMessage = string.Empty;

    public DriveValidatorWizardViewModel(
        Func<IReadOnlyList<UsbTargetInfo>> getTargets,
        Func<UsbTargetInfo, (bool isSafe, string reason)> safetyEvaluator,
        RunValidationCallback runValidationAsync,
        Func<UsbTargetInfo, DriveValidationResult?>? lastValidationLookup = null,
        Func<UsbTargetInfo, string>? portLabelLookup = null,
        Func<string, string, bool>? confirmHeavyMode = null,
        Action<string, LogSeverity>? appendLog = null,
        UsbTargetInfo? preferredTarget = null)
    {
        _getTargets = getTargets ?? throw new ArgumentNullException(nameof(getTargets));
        _safetyEvaluator = safetyEvaluator ?? throw new ArgumentNullException(nameof(safetyEvaluator));
        _runValidationAsync = runValidationAsync ?? throw new ArgumentNullException(nameof(runValidationAsync));
        _lastValidationLookup = lastValidationLookup ?? (_ => null);
        _portLabelLookup = portLabelLookup ?? (_ => string.Empty);
        _confirmHeavyMode = confirmHeavyMode;
        _appendLog = appendLog;
        _preferredTarget = preferredTarget;

        Modes = BuildModes();
        _selectedMode = Modes.First(m => m.Mode == DriveValidationMode.QuickSafeCheck);

        RefreshTargetsCommand = new RelayCommand(RefreshTargets);
        NextFromSelectTargetCommand = new RelayCommand(
            () => Step = DriveValidatorWizardStep.ChooseMode,
            () => SelectedTarget is { IsSafe: true });
        NextFromChooseModeCommand = new RelayCommand(
            () => Step = DriveValidatorWizardStep.SafetyReview,
            () => SelectedMode is { IsAvailable: true });
        StartValidationCommand = new AsyncRelayCommand(StartValidationAsync, CanStartValidation);
        CancelValidationCommand = new RelayCommand(CancelValidation, () => _isRunning);
        BackFromChooseModeCommand = new RelayCommand(() => Step = DriveValidatorWizardStep.SelectTarget, () => !_isRunning);
        BackFromSafetyCommand = new RelayCommand(() => Step = DriveValidatorWizardStep.ChooseMode, () => !_isRunning);
        BackToTargetSelectionFromResultsCommand = new RelayCommand(() => Step = DriveValidatorWizardStep.SelectTarget, () => !_isRunning);
        RunAnotherCommand = new RelayCommand(() => Step = DriveValidatorWizardStep.ChooseMode, () => !_isRunning);
        CopySummaryCommand = new RelayCommand(CopySummary, () => _lastResult is not null);
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, _lastResult is not null));
        OpenUsbMappingWizardCommand = new RelayCommand(() => OpenUsbMappingWizardRequested?.Invoke(this, EventArgs.Empty));

        RefreshTargets();
    }

    public event EventHandler<bool>? CloseRequested;

    /// <summary>Raised when the user clicks the "Open USB Mapping Wizard" recommendation. The
    /// host (MainViewModel) wires this to <see cref="MainViewModel.OpenUsbMappingWizardCommand"/>.</summary>
    public event EventHandler? OpenUsbMappingWizardRequested;

    public event EventHandler<DriveValidationResult>? ValidationCompleted;

    public RelayCommand RefreshTargetsCommand { get; }
    public RelayCommand NextFromSelectTargetCommand { get; }
    public RelayCommand NextFromChooseModeCommand { get; }
    public AsyncRelayCommand StartValidationCommand { get; }
    public RelayCommand CancelValidationCommand { get; }
    public RelayCommand BackFromChooseModeCommand { get; }
    public RelayCommand BackFromSafetyCommand { get; }
    public RelayCommand BackToTargetSelectionFromResultsCommand { get; }
    public RelayCommand RunAnotherCommand { get; }
    public RelayCommand CopySummaryCommand { get; }
    public RelayCommand CloseCommand { get; }
    public RelayCommand OpenUsbMappingWizardCommand { get; }

    public ObservableCollection<DriveValidatorWizardTargetOption> Targets { get; } = new();

    public IReadOnlyList<DriveValidatorWizardModeOption> Modes { get; }

    public ObservableCollection<DriveValidatorRegionTileViewModel> RegionTiles { get; } = new();

    public DriveValidatorWizardStep Step
    {
        get => _step;
        private set
        {
            if (SetProperty(ref _step, value))
            {
                OnPropertyChanged(nameof(IsSelectTargetStep));
                OnPropertyChanged(nameof(IsChooseModeStep));
                OnPropertyChanged(nameof(IsSafetyReviewStep));
                OnPropertyChanged(nameof(IsRunningStep));
                OnPropertyChanged(nameof(IsResultsStep));
                RaiseAllCommands();
            }
        }
    }

    public bool IsSelectTargetStep => Step == DriveValidatorWizardStep.SelectTarget;
    public bool IsChooseModeStep => Step == DriveValidatorWizardStep.ChooseMode;
    public bool IsSafetyReviewStep => Step == DriveValidatorWizardStep.SafetyReview;
    public bool IsRunningStep => Step == DriveValidatorWizardStep.Running;
    public bool IsResultsStep => Step == DriveValidatorWizardStep.Results;

    public DriveValidatorWizardTargetOption? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (SetProperty(ref _selectedTarget, value))
            {
                OnPropertyChanged(nameof(SelectedTargetBlockedReason));
                OnPropertyChanged(nameof(SelectedTargetPortAmbiguous));
                OnPropertyChanged(nameof(SelectedTargetPortHint));
                RaiseAllCommands();
            }
        }
    }

    public string SelectedTargetBlockedReason =>
        SelectedTarget is null ? string.Empty :
        SelectedTarget.IsSafe ? string.Empty :
        SelectedTarget.SafetyReason;

    public bool SelectedTargetPortAmbiguous =>
        SelectedTarget is not null && SelectedTarget.PortLabel is "unmapped" or "—";

    public string SelectedTargetPortHint =>
        SelectedTargetPortAmbiguous
            ? "Port mapping is ambiguous — run USB Mapping Wizard for better port evidence."
            : string.Empty;

    public DriveValidatorWizardModeOption? SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (SetProperty(ref _selectedMode, value))
            {
                OnPropertyChanged(nameof(NeedsFullModeConfirmation));
                _fullModeUserAcknowledged = false;
                OnPropertyChanged(nameof(FullModeUserAcknowledged));
                RaiseAllCommands();
            }
        }
    }

    public bool NeedsFullModeConfirmation => SelectedMode?.Mode == DriveValidationMode.FullFreeSpaceValidation;

    public bool FullModeUserAcknowledged
    {
        get => _fullModeUserAcknowledged;
        set
        {
            if (SetProperty(ref _fullModeUserAcknowledged, value))
            {
                StartValidationCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SafetyReviewBody
    {
        get
        {
            if (SelectedMode is null || SelectedTarget is null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Target: {SelectedTarget.RootPath} ({SelectedTarget.LabelDisplay}, {SelectedTarget.CapacityDisplay})");
            sb.AppendLine($"Mode: {SelectedMode.Title} — {SelectedMode.Heaviness}");
            sb.AppendLine();
            sb.AppendLine("What will happen:");
            sb.AppendLine("• ForgerEMS writes temporary test files into the drive's free space.");
            sb.AppendLine($"• All test files live under .{DriveValidationTargetSafety.TempFolderName.TrimStart('.')}\\ on the drive.");
            sb.AppendLine("• Safe modes do NOT format the drive and do NOT delete your existing files.");
            sb.AppendLine("• Cleanup only removes ForgerEMS-owned sample files.");
            sb.AppendLine("• Results are advisory evidence for a technician — not a 100% authenticity certificate.");
            if (SelectedMode.Mode == DriveValidationMode.FullFreeSpaceValidation)
            {
                sb.AppendLine();
                sb.AppendLine("⚠ Full Free-Space Validation is heavy:");
                sb.AppendLine("• Writes a large fraction of your drive's free space.");
                sb.AppendLine("• Can take a long time depending on drive size and speed.");
                sb.AppendLine("• Back up important data before running this.");
            }
            sb.AppendLine();
            sb.AppendLine("Destructive Full Media Validation is NOT available in this build. A future version " +
                          "would erase the drive entirely and require a typed confirmation phrase.");
            return sb.ToString().TrimEnd();
        }
    }

    public string RunningPhaseText
    {
        get => _runningPhaseText;
        private set => SetProperty(ref _runningPhaseText, value);
    }

    public string RunningProgressText
    {
        get => _runningProgressText;
        private set => SetProperty(ref _runningProgressText, value);
    }

    public double RunningProgressValue
    {
        get => _runningProgressValue;
        private set => SetProperty(ref _runningProgressValue, value);
    }

    public string RunningElapsedText
    {
        get => _runningElapsedText;
        private set => SetProperty(ref _runningElapsedText, value);
    }

    public string RunningBytesText
    {
        get => _runningBytesText;
        private set => SetProperty(ref _runningBytesText, value);
    }

    public string RunningSpeedText
    {
        get => _runningSpeedText;
        private set => SetProperty(ref _runningSpeedText, value);
    }

    public bool HeartbeatActive
    {
        get => _heartbeatActive;
        private set => SetProperty(ref _heartbeatActive, value);
    }

    public string HeartbeatMessage
    {
        get => _heartbeatMessage;
        private set => SetProperty(ref _heartbeatMessage, value);
    }

    public DriveValidationResult? LastResult => _lastResult;

    public string ResultSummary
    {
        get => _resultSummary;
        private set => SetProperty(ref _resultSummary, value);
    }

    public string ResultEvidence
    {
        get => _resultEvidence;
        private set => SetProperty(ref _resultEvidence, value);
    }

    public string ResultLimitations
    {
        get => _resultLimitations;
        private set => SetProperty(ref _resultLimitations, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                RaiseAllCommands();
            }
        }
    }

    public void RefreshTargets()
    {
        Targets.Clear();
        var live = _getTargets();
        foreach (var t in live)
        {
            var (isSafe, reason) = _safetyEvaluator(t);
            var portLabel = _portLabelLookup(t);
            var option = new DriveValidatorWizardTargetOption(t, isSafe, reason, portLabel);
            var last = _lastValidationLookup(t);
            if (last is not null)
            {
                option.LastValidationSummary = $"{DriveValidationUiCopy.StatusDisplay(last.Status)} — {last.Summary}";
            }
            else
            {
                option.LastValidationSummary = "Not validated yet for this target.";
            }
            Targets.Add(option);
        }

        if (_preferredTarget is not null && SelectedTarget is null)
        {
            SelectedTarget = Targets.FirstOrDefault(o =>
                string.Equals(o.RootPath, _preferredTarget.RootPath, StringComparison.OrdinalIgnoreCase));
        }
        SelectedTarget ??= Targets.FirstOrDefault(o => o.IsSafe) ?? Targets.FirstOrDefault();
    }

    private bool CanStartValidation()
    {
        if (_isRunning) return false;
        if (SelectedTarget is null || !SelectedTarget.IsSafe) return false;
        if (SelectedMode is null || !SelectedMode.IsAvailable) return false;
        if (SelectedMode.RequiresConfirmation && !_fullModeUserAcknowledged) return false;
        return true;
    }

    private async Task StartValidationAsync()
    {
        if (!CanStartValidation() || SelectedTarget is null || SelectedMode is null)
        {
            return;
        }

        var target = SelectedTarget.Target;
        var mode = SelectedMode.Mode;

        if (mode == DriveValidationMode.FullFreeSpaceValidation && _confirmHeavyMode is not null)
        {
            if (!_confirmHeavyMode(
                "Drive Validator — heavy writes",
                "Full Free-Space Validation writes many temporary test files and can take a long time. Continue?"))
            {
                return;
            }
        }

        var options = new DriveValidationOptions { Mode = mode };
        _runCts?.Cancel();
        _runCts = new CancellationTokenSource();
        var token = _runCts.Token;

        _appendLog?.Invoke($"[INFO] Drive Validator Wizard started on {target.RootPath} mode={DriveValidationUiCopy.ModeDisplay(mode)}", LogSeverity.Info);

        RegionTiles.Clear();
        RunningPhaseText = "Starting validation…";
        RunningProgressText = "—";
        RunningProgressValue = 0;
        RunningElapsedText = "—";
        RunningBytesText = "—";
        RunningSpeedText = "—";
        HeartbeatActive = false;
        HeartbeatMessage = string.Empty;
        IsRunning = true;
        _runStartedUtc = DateTime.UtcNow;
        _lastProgressUtc = _runStartedUtc;
        Step = DriveValidatorWizardStep.Running;

        try
        {
            var portHint = _portLabelLookup(target);
            var result = await _runValidationAsync(
                target,
                options,
                portHint,
                progress => HandleProgress(progress),
                token).ConfigureAwait(true);

            ApplyTerminalResult(result);
            ValidationCompleted?.Invoke(this, result);
        }
        catch (OperationCanceledException)
        {
            // Cancellation path: the service still returns a result with Status=Cancelled; this
            // catch only fires if the cancellation was raised before the service produced one.
            var cancelled = new DriveValidationResult
            {
                Status = DriveValidationStatus.Cancelled,
                Mode = mode,
                Summary = "Drive validation cancelled.",
                CompletedAtUtc = DateTimeOffset.UtcNow,
                TargetRootPath = target.RootPath
            };
            ApplyTerminalResult(cancelled);
            ValidationCompleted?.Invoke(this, cancelled);
        }
        catch (Exception ex)
        {
            _appendLog?.Invoke($"[ERROR] Drive Validator Wizard failed: {ex.Message}", LogSeverity.Error);
            var failed = new DriveValidationResult
            {
                Status = DriveValidationStatus.Failed,
                Mode = mode,
                Summary = "Validation failed unexpectedly.",
                Detail = ex.Message,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                TargetRootPath = target.RootPath
            };
            ApplyTerminalResult(failed);
            ValidationCompleted?.Invoke(this, failed);
        }
        finally
        {
            _runCts?.Dispose();
            _runCts = null;
            IsRunning = false;
            HeartbeatActive = false;
            HeartbeatMessage = string.Empty;
        }
    }

    private void HandleProgress(DriveValidationProgress progress)
    {
        _lastProgressUtc = DateTime.UtcNow;
        RunningPhaseText = string.IsNullOrWhiteSpace(progress.Message) ? progress.Phase.ToString() : progress.Message;
        RunningProgressText = progress.SampleCount > 0
            ? $"{progress.SampleIndex}/{progress.SampleCount} · {progress.Phase}"
            : progress.Phase.ToString();
        RunningProgressValue = progress.ProgressFraction * 100;
        var elapsed = DateTime.UtcNow - _runStartedUtc;
        RunningElapsedText = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";

        if (progress.MapSnapshot is not null)
        {
            SyncRegionTiles(progress.MapSnapshot);
            var s = progress.MapSnapshot.Summary;
            RunningBytesText = $"regions {s.Tested}/{s.Planned} passed:{s.Passed} warn:{s.Warning} fail:{s.Mismatch + s.AliasSuspected + s.IoError}";
            if (s.FastestReadMBps > 0)
            {
                RunningSpeedText = $"read {s.SlowestReadMBps:0.0}–{s.FastestReadMBps:0.0} MB/s";
            }
        }

        HeartbeatActive = false;
        HeartbeatMessage = string.Empty;
    }

    private void SyncRegionTiles(DriveValidationMap map)
    {
        for (var i = RegionTiles.Count; i < map.Regions.Count; i++)
        {
            var r = map.Regions[i];
            RegionTiles.Add(new DriveValidatorRegionTileViewModel(
                r.Index, r.LogicalOffsetHint, r.PlannedBytes, r.ExpectedSignatureHash));
        }

        for (var i = 0; i < map.Regions.Count && i < RegionTiles.Count; i++)
        {
            RegionTiles[i].ApplyRegion(map.Regions[i]);
        }
    }

    /// <summary>
    /// Called by a UI timer (or the host) once the running step has been on screen for at least
    /// 10 seconds without a progress event. Updates the wizard's heartbeat message so the user
    /// can see the wizard is still alive rather than visually frozen.
    /// </summary>
    public void TickHeartbeat()
    {
        if (!_isRunning) return;
        var sinceLast = DateTime.UtcNow - _lastProgressUtc;
        if (sinceLast.TotalSeconds < 10)
        {
            HeartbeatActive = false;
            HeartbeatMessage = string.Empty;
            return;
        }

        HeartbeatActive = true;
        HeartbeatMessage = RunningPhaseText switch
        {
            var s when s.Contains("Writing", StringComparison.OrdinalIgnoreCase) => $"Still writing… ({(int)sinceLast.TotalSeconds}s)",
            var s when s.Contains("Verifying", StringComparison.OrdinalIgnoreCase) => $"Still verifying… ({(int)sinceLast.TotalSeconds}s)",
            _ => $"Still waiting on drive I/O… ({(int)sinceLast.TotalSeconds}s)"
        };
        var elapsed = DateTime.UtcNow - _runStartedUtc;
        RunningElapsedText = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
    }

    private void CancelValidation()
    {
        _runCts?.Cancel();
        RunningPhaseText = "Cancelling…";
        _appendLog?.Invoke("[WARN] Drive Validator Wizard cancellation requested.", LogSeverity.Warning);
    }

    private void ApplyTerminalResult(DriveValidationResult result)
    {
        _lastResult = result;
        ResultSummary = $"{DriveValidationUiCopy.StatusDisplay(result.Status)} — {result.Summary}";
        ResultEvidence = FormatEvidence(result);
        ResultLimitations = BuildLimitations(result.Mode);
        RunningPhaseText = DriveValidationUiCopy.TerminalPhaseDisplay(result.Status);
        RunningProgressText = DriveValidationUiCopy.TerminalProgressDisplay(
            result.Status, result.Evidence.SamplesVerified, result.Evidence.SamplesPlanned);
        RunningProgressValue = result.Status switch
        {
            DriveValidationStatus.Passed
                or DriveValidationStatus.PassedWithWarnings
                or DriveValidationStatus.Failed
                or DriveValidationStatus.CleanupWarning => 100,
            _ => 0
        };
        SyncRegionTilesFromEvidence(result.Evidence);
        Step = DriveValidatorWizardStep.Results;
        _appendLog?.Invoke(
            $"[INFO] Drive Validator Wizard {result.Status} on {result.TargetRootPath}: {result.Summary}",
            result.Status == DriveValidationStatus.Failed ? LogSeverity.Error :
            result.Status is DriveValidationStatus.PassedWithWarnings or DriveValidationStatus.CleanupWarning
                ? LogSeverity.Warning
                : LogSeverity.Info);
    }

    private void SyncRegionTilesFromEvidence(DriveValidationEvidence evidence)
    {
        if (evidence.Regions.Count == 0)
        {
            return;
        }
        for (var i = RegionTiles.Count; i < evidence.Regions.Count; i++)
        {
            var r = evidence.Regions[i];
            RegionTiles.Add(new DriveValidatorRegionTileViewModel(
                r.Index, r.LogicalOffsetHint, r.PlannedBytes, r.ExpectedSignatureHash));
        }
        for (var i = 0; i < evidence.Regions.Count && i < RegionTiles.Count; i++)
        {
            RegionTiles[i].ApplyRegion(evidence.Regions[i]);
        }
    }

    private void CopySummary()
    {
        if (_lastResult is null) return;
        var text = BuildClipboardSummary(_lastResult!);
        try
        {
            System.Windows.Clipboard.SetText(text);
            _appendLog?.Invoke("[INFO] Drive Validator Wizard summary copied to clipboard.", LogSeverity.Info);
        }
        catch (Exception ex)
        {
            _appendLog?.Invoke("[WARN] Drive Validator Wizard summary copy failed: " + ex.Message, LogSeverity.Warning);
        }
    }

    public static string BuildClipboardSummary(DriveValidationResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ForgerEMS Drive Validator — summary");
        sb.AppendLine($"Status: {DriveValidationUiCopy.StatusDisplay(result.Status)}");
        sb.AppendLine($"Mode: {DriveValidationUiCopy.ModeDisplay(result.Mode)}");
        sb.AppendLine($"Target: {result.TargetRootPath} ({result.Evidence.TargetLabel})");
        sb.AppendLine($"Capacity reported: {UsbTargetInfo.FormatBytes(result.Evidence.TargetTotalBytes)}");
        sb.AppendLine($"Summary: {result.Summary}");
        sb.AppendLine();
        var e = result.Evidence;
        sb.AppendLine($"Regions planned/tested: {e.SamplesPlanned}/{e.SamplesVerified}");
        sb.AppendLine($"Bytes written/verified: {e.BytesWritten:N0} / {e.BytesVerified:N0}");
        sb.AppendLine($"Write/Read speed: {e.WriteSpeedMBps:0.0} / {e.ReadSpeedMBps:0.0} MB/s");
        sb.AppendLine($"Mismatches: {e.MismatchCount} · alias flags: {e.SuspiciousAliasCount} · I/O errors: {e.IoErrorCount}");
        if (e.MapSummary.Planned > 0)
        {
            sb.AppendLine($"Region map: passed={e.MapSummary.Passed} warn={e.MapSummary.Warning} mismatch={e.MapSummary.Mismatch} alias={e.MapSummary.AliasSuspected} ioErr={e.MapSummary.IoError}");
        }
        if (e.MapSummary.FastestReadMBps > 0 || e.MapSummary.SlowestReadMBps > 0)
        {
            sb.AppendLine($"Per-region read range: {e.MapSummary.SlowestReadMBps:0.0} – {e.MapSummary.FastestReadMBps:0.0} MB/s");
        }
        sb.AppendLine($"Cleanup: {e.CleanupStatus}");
        sb.AppendLine($"Identity confidence: {e.IdentityConfidence}");
        if (!string.IsNullOrWhiteSpace(result.Detail))
        {
            sb.AppendLine();
            sb.AppendLine("Detail: " + result.Detail);
        }
        sb.AppendLine();
        sb.AppendLine(BuildLimitations(result.Mode));
        return sb.ToString().TrimEnd();
    }

    public static string FormatEvidence(DriveValidationResult result)
    {
        var e = result.Evidence;
        var sb = new StringBuilder();
        sb.AppendLine($"Regions planned: {e.SamplesPlanned}");
        sb.AppendLine($"Regions tested: {e.SamplesVerified}");
        if (e.MapSummary.Planned > 0)
        {
            sb.AppendLine($"  passed: {e.MapSummary.Passed}");
            sb.AppendLine($"  warnings: {e.MapSummary.Warning}");
            sb.AppendLine($"  mismatches: {e.MapSummary.Mismatch}");
            sb.AppendLine($"  alias suspected: {e.MapSummary.AliasSuspected}");
            sb.AppendLine($"  I/O errors: {e.MapSummary.IoError}");
        }
        sb.AppendLine($"Bytes written: {e.BytesWritten:N0}");
        sb.AppendLine($"Bytes verified: {e.BytesVerified:N0}");
        sb.AppendLine($"Write speed: {e.WriteSpeedMBps:0.0} MB/s");
        sb.AppendLine($"Read speed: {e.ReadSpeedMBps:0.0} MB/s");
        if (e.MapSummary.FastestReadMBps > 0 || e.MapSummary.SlowestReadMBps > 0)
        {
            sb.AppendLine($"Per-region read range: {e.MapSummary.SlowestReadMBps:0.0} – {e.MapSummary.FastestReadMBps:0.0} MB/s");
        }
        sb.AppendLine($"Cleanup: {e.CleanupStatus}");
        sb.AppendLine($"Identity confidence: {e.IdentityConfidence}");
        if (e.SpeedCollapseSuspected)
        {
            sb.AppendLine("Speed-collapse warning: at least one region's read speed dropped sharply versus the median.");
        }
        if (!string.IsNullOrWhiteSpace(result.Detail))
        {
            sb.AppendLine();
            sb.AppendLine(result.Detail);
        }
        return sb.ToString().TrimEnd();
    }

    private static string BuildLimitations(DriveValidationMode mode) => mode switch
    {
        DriveValidationMode.QuickSafeCheck =>
            "Limitations: Quick Safe Check is a tiny sampled probe. It cannot prove full drive capacity. " +
            "Run Sampled Capacity Check or Full Free-Space Validation for stronger evidence.",
        DriveValidationMode.SampledCapacityCheck =>
            "Limitations: Sampled Capacity Check is bounded sampled evidence. It catches many fake-capacity " +
            "and aliasing patterns but does not exercise every region. Run Full Free-Space Validation for " +
            "the strongest non-destructive evidence.",
        DriveValidationMode.FullFreeSpaceValidation =>
            "Limitations: Full Free-Space Validation is the strongest non-destructive mode but still cannot " +
            "directly inspect raw NAND. Destructive Full Media validation is not available in this build.",
        _ => "Limitations: no safe mode can directly inspect raw NAND chips through normal Windows file I/O."
    };

    private void RaiseAllCommands()
    {
        NextFromSelectTargetCommand.RaiseCanExecuteChanged();
        NextFromChooseModeCommand.RaiseCanExecuteChanged();
        StartValidationCommand.RaiseCanExecuteChanged();
        CancelValidationCommand.RaiseCanExecuteChanged();
        BackFromChooseModeCommand.RaiseCanExecuteChanged();
        BackFromSafetyCommand.RaiseCanExecuteChanged();
        BackToTargetSelectionFromResultsCommand.RaiseCanExecuteChanged();
        RunAnotherCommand.RaiseCanExecuteChanged();
        CopySummaryCommand.RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        try { _runCts?.Cancel(); } catch (ObjectDisposedException) { /* already disposed */ }
        _runCts?.Dispose();
        _runCts = null;
    }

    private static DriveValidatorWizardModeOption[] BuildModes() => new[]
    {
        new DriveValidatorWizardModeOption(
            DriveValidationMode.QuickSafeCheck,
            "Quick Safe Check",
            "Fast sanity check that writes a tiny bounded sample (a few MB) across the drive's free space.",
            "very light",
            requiresConfirmation: false,
            isAvailable: true),
        new DriveValidatorWizardModeOption(
            DriveValidationMode.SampledCapacityCheck,
            "Sampled Capacity Check",
            "Tests more regions across the drive. Better at detecting fake-capacity, aliasing, and damaged-region symptoms. Still bounded and non-destructive.",
            "moderate",
            requiresConfirmation: false,
            isAvailable: true),
        new DriveValidatorWizardModeOption(
            DriveValidationMode.FullFreeSpaceValidation,
            "Full Free-Space Validation",
            "Strongest non-destructive mode. Writes and verifies a large fraction of the drive's free space. Can take a long time and causes heavy writes.",
            "heavy",
            requiresConfirmation: true,
            isAvailable: true),
        new DriveValidatorWizardModeOption(
            DriveValidationMode.DestructiveFullMediaValidation,
            "Destructive Full Media Validation",
            "Would erase the entire drive and overwrite/verify every reachable sector. Currently disabled.",
            "destructive (disabled)",
            requiresConfirmation: true,
            isAvailable: false,
            unavailableReason: "not available in this build")
    };
}
