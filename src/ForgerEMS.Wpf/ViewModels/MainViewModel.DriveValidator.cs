#pragma warning disable CA1305 // Locale-sensitive calls; text is diagnostic/UI output
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services.DriveValidation;
using VentoyToolkitSetup.Wpf.Services.Intelligence;

namespace VentoyToolkitSetup.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private CancellationTokenSource? _driveValidationCts;
    private int _driveValidatorModeIndex;
    private string _driveValidatorDestructiveConfirmation = string.Empty;
    private string _driveValidatorTargetDisplay = "—";
    private string _driveValidatorCapacityDisplay = "—";
    private string _driveValidatorFileSystemDisplay = "—";
    private string _driveValidatorFreeSpaceDisplay = "—";
    private string _driveValidatorBusPortDisplay = "—";
    private string _driveValidatorModeDisplay = DriveValidationUiCopy.ModeDisplay(DriveValidationMode.QuickSafeCheck);
    private string _driveValidatorPhaseDisplay = "—";
    private string _driveValidatorProgressDisplay = "—";
    private string _driveValidatorResultSummary = "Not validated yet.";
    private string _driveValidatorEvidenceDisplay = string.Empty;
    private string _driveValidatorBuilderWarningText = string.Empty;
    private double _driveValidatorProgressValue;

    public int DriveValidatorModeIndex
    {
        get => _driveValidatorModeIndex;
        set
        {
            if (SetProperty(ref _driveValidatorModeIndex, Math.Clamp(value, 0, 2)))
            {
                DriveValidatorModeDisplay = DriveValidationUiCopy.ModeDisplay(ResolveDriveValidatorMode());
                RunDriveValidatorCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string DriveValidatorDestructiveConfirmation
    {
        get => _driveValidatorDestructiveConfirmation;
        set => SetProperty(ref _driveValidatorDestructiveConfirmation, value);
    }

    public static string DriveValidatorIntro => DriveValidationUiCopy.Intro;

    public string DriveValidatorTargetDisplay
    {
        get => _driveValidatorTargetDisplay;
        private set => SetProperty(ref _driveValidatorTargetDisplay, value);
    }

    public string DriveValidatorCapacityDisplay
    {
        get => _driveValidatorCapacityDisplay;
        private set => SetProperty(ref _driveValidatorCapacityDisplay, value);
    }

    public string DriveValidatorFileSystemDisplay
    {
        get => _driveValidatorFileSystemDisplay;
        private set => SetProperty(ref _driveValidatorFileSystemDisplay, value);
    }

    public string DriveValidatorFreeSpaceDisplay
    {
        get => _driveValidatorFreeSpaceDisplay;
        private set => SetProperty(ref _driveValidatorFreeSpaceDisplay, value);
    }

    public string DriveValidatorBusPortDisplay
    {
        get => _driveValidatorBusPortDisplay;
        private set => SetProperty(ref _driveValidatorBusPortDisplay, value);
    }

    public string DriveValidatorModeDisplay
    {
        get => _driveValidatorModeDisplay;
        private set => SetProperty(ref _driveValidatorModeDisplay, value);
    }

    public string DriveValidatorPhaseDisplay
    {
        get => _driveValidatorPhaseDisplay;
        private set => SetProperty(ref _driveValidatorPhaseDisplay, value);
    }

    public string DriveValidatorProgressDisplay
    {
        get => _driveValidatorProgressDisplay;
        private set => SetProperty(ref _driveValidatorProgressDisplay, value);
    }

    public double DriveValidatorProgressValue
    {
        get => _driveValidatorProgressValue;
        private set => SetProperty(ref _driveValidatorProgressValue, value);
    }

    public string DriveValidatorResultSummary
    {
        get => _driveValidatorResultSummary;
        private set => SetProperty(ref _driveValidatorResultSummary, value);
    }

    public string DriveValidatorEvidenceDisplay
    {
        get => _driveValidatorEvidenceDisplay;
        private set => SetProperty(ref _driveValidatorEvidenceDisplay, value);
    }

    public string DriveValidatorBuilderWarningText
    {
        get => _driveValidatorBuilderWarningText;
        private set
        {
            if (SetProperty(ref _driveValidatorBuilderWarningText, value))
            {
                OnPropertyChanged(nameof(HasDriveValidatorBuilderWarning));
            }
        }
    }

    public bool HasDriveValidatorBuilderWarning => !string.IsNullOrWhiteSpace(DriveValidatorBuilderWarningText);

    private DriveValidationMode ResolveDriveValidatorMode() =>
        _driveValidatorModeIndex switch
        {
            1 => DriveValidationMode.SampledCapacityCheck,
            2 => DriveValidationMode.FullFreeSpaceValidation,
            _ => DriveValidationMode.QuickSafeCheck
        };

    private bool CanRunDriveValidator()
    {
        if (_driveValidationCts is { Token.IsCancellationRequested: false })
        {
            return false;
        }

        if (SelectedUsbTarget is null)
        {
            return false;
        }

        var options = new DriveValidationOptions { Mode = ResolveDriveValidatorMode() };
        return DriveValidationTargetSafety.IsSafeToStart(SelectedUsbTarget, options, out _);
    }

    private async Task RunDriveValidatorAsync()
    {
        if (SelectedUsbTarget is null)
        {
            return;
        }

        var mode = ResolveDriveValidatorMode();
        if (mode == DriveValidationMode.FullFreeSpaceValidation &&
            !_userPromptService.Confirm(
                "Drive Validator — heavy writes",
                "Full free-space validation writes many temporary test files and can take a long time. Continue?"))
        {
            return;
        }

        var options = new DriveValidationOptions
        {
            Mode = mode,
            DestructiveConfirmationText = DriveValidatorDestructiveConfirmation
        };

        _driveValidationCts?.Cancel();
        _driveValidationCts = new CancellationTokenSource();
        var token = _driveValidationCts.Token;
        RunDriveValidatorCommand.RaiseCanExecuteChanged();
        CancelDriveValidatorCommand.RaiseCanExecuteChanged();

        var target = SelectedUsbTarget;
        var portHint = UsbIntelligenceMappingLabelDisplay is "—" or ""
            ? string.Empty
            : UsbIntelligenceMappingLabelDisplay;

        AppendLog(new LogLine(
            DateTimeOffset.Now,
            $"[INFO] Drive Validator started on {target.RootPath} mode={DriveValidationUiCopy.ModeDisplay(mode)}",
            LogSeverity.Info));

        try
        {
            var result = await _driveValidationService.RunAsync(
                target,
                options,
                portHint,
                progress =>
                {
                    RunOnUi(() =>
                    {
                        DriveValidatorPhaseDisplay = progress.Message;
                        DriveValidatorProgressDisplay =
                            progress.SampleCount > 0
                                ? $"{progress.SampleIndex}/{progress.SampleCount} · {progress.Phase}"
                                : progress.Phase.ToString();
                        DriveValidatorProgressValue = progress.ProgressFraction * 100;
                    });
                },
                token).ConfigureAwait(true);

            result = EnsureIdentityCaptured(result, target);
            PersistDriveValidationResult(result);
            ApplyDriveValidationResultToUi(result);
            SyncDriveValidationToPortProfile(result, target);
            AppendDriveValidationLog(result);
            RefreshDriveValidatorPanel();
            UpdateTargetWarnings();
        }
        catch (Exception ex)
        {
            AppendLog(new LogLine(DateTimeOffset.Now, "[ERROR] Drive Validator failed: " + ex.Message, LogSeverity.Error, isErrorStream: true));
        }
        finally
        {
            _driveValidationCts?.Dispose();
            _driveValidationCts = null;
            RunDriveValidatorCommand.RaiseCanExecuteChanged();
            CancelDriveValidatorCommand.RaiseCanExecuteChanged();
        }
    }

    private void CancelDriveValidator()
    {
        _driveValidationCts?.Cancel();
        DriveValidatorPhaseDisplay = "Cancelling…";
        CancelDriveValidatorCommand.RaiseCanExecuteChanged();
    }

    private void RefreshDriveValidatorPanel()
    {
        if (SelectedUsbTarget is null)
        {
            DriveValidatorTargetDisplay = "—";
            DriveValidatorCapacityDisplay = "—";
            DriveValidatorFileSystemDisplay = "—";
            DriveValidatorFreeSpaceDisplay = "—";
            DriveValidatorBusPortDisplay = "—";
            DriveValidatorResultSummary = "Select a USB target to validate.";
            DriveValidatorEvidenceDisplay = string.Empty;
            DriveValidatorBuilderWarningText = string.Empty;
            RunDriveValidatorCommand.RaiseCanExecuteChanged();
            return;
        }

        var target = SelectedUsbTarget;
        DriveValidatorTargetDisplay = $"{target.RootPath} ({target.LabelDisplay})";
        DriveValidatorCapacityDisplay = target.DisplayTotalBytes;
        DriveValidatorFileSystemDisplay = string.IsNullOrWhiteSpace(target.FileSystem) ? "—" : target.FileSystem;
        DriveValidatorFreeSpaceDisplay = target.DisplayFreeBytes;
        DriveValidatorBusPortDisplay =
            $"{target.BusTypeDisplay} · {target.DeviceIdentityDisplay} · port: {(UsbIntelligenceMappingLabelDisplay is "—" ? "unmapped" : UsbIntelligenceMappingLabelDisplay)}";

        var cached = GetDriveValidationResultForTarget(target);
        if (cached is not null)
        {
            ApplyDriveValidationResultToUi(cached);
        }
        else
        {
            DriveValidatorResultSummary = "Not validated yet for this target.";
            DriveValidatorEvidenceDisplay = string.Empty;
            DriveValidatorBuilderWarningText = DriveValidationUiCopy.NotValidatedBuilderHint;
        }

        RunDriveValidatorCommand.RaiseCanExecuteChanged();
    }

    private void ApplyDriveValidationResultToUi(DriveValidationResult result)
    {
        DriveValidatorResultSummary =
            $"{DriveValidationUiCopy.StatusDisplay(result.Status)} — {result.Summary}";
        DriveValidatorEvidenceDisplay = FormatDriveValidationEvidence(result);
        DriveValidatorBuilderWarningText = result.Status switch
        {
            DriveValidationStatus.Failed => DriveValidationUiCopy.FailedBuilderWarning,
            DriveValidationStatus.CleanupWarning => DriveValidationUiCopy.CleanupWarningBuilderHint,
            DriveValidationStatus.InsufficientFreeSpace => DriveValidationUiCopy.InsufficientFreeSpaceBuilderHint,
            DriveValidationStatus.NotRun => DriveValidationUiCopy.NotValidatedBuilderHint,
            _ => string.Empty
        };
    }

    private static string FormatDriveValidationEvidence(DriveValidationResult result)
    {
        var e = result.Evidence;
        var sb = new StringBuilder();
        sb.AppendLine($"samples {e.SamplesVerified}/{e.SamplesPlanned} written {e.SamplesWritten}");
        sb.AppendLine($"bytes written {e.BytesWritten:N0} verified {e.BytesVerified:N0}");
        sb.AppendLine($"speed write {e.WriteSpeedMBps:0.0} MB/s read {e.ReadSpeedMBps:0.0} MB/s");
        sb.AppendLine($"mismatches {e.MismatchCount} ioErrors {e.IoErrorCount} aliasFlags {e.SuspiciousAliasCount}");
        sb.AppendLine($"temp {e.TempFolder}");
        sb.AppendLine($"cleanup {e.CleanupStatus}");
        if (!string.IsNullOrWhiteSpace(e.IdentityConfidence))
        {
            sb.AppendLine($"identity {e.IdentityConfidence}");
        }
        if (!string.IsNullOrWhiteSpace(result.Detail))
        {
            sb.AppendLine(result.Detail);
        }

        return sb.ToString().TrimEnd();
    }

    private bool TryAcknowledgeDriveValidationForBuild(string actionName)
    {
        if (SelectedUsbTarget is null)
        {
            return true;
        }

        var result = GetDriveValidationResultForTarget(SelectedUsbTarget);
        if (result is null || result.Status == DriveValidationStatus.NotRun)
        {
            return _userPromptService.Confirm(
                "Drive Validator recommendation",
                DriveValidationUiCopy.NotValidatedBuilderHint + Environment.NewLine + Environment.NewLine +
                $"Continue {actionName} without running Drive Validator on {SelectedUsbTarget.RootPath}?");
        }

        if (result.ShouldWarnUsbBuilder)
        {
            return _userPromptService.Confirm(
                "Drive validation warning",
                DriveValidationUiCopy.FailedBuilderWarning + Environment.NewLine + Environment.NewLine +
                result.Summary + Environment.NewLine + Environment.NewLine +
                $"Continue {actionName} anyway?");
        }

        return true;
    }

    private DriveValidationResult? GetDriveValidationResultForTarget(UsbTargetInfo target)
    {
        var key = GetDriveValidationCacheKey(target.RootPath);
        if (!_driveValidationResultsByRoot.TryGetValue(key, out var result))
        {
            return null;
        }

        // Identity guard: the same drive letter can be reassigned to a totally different drive
        // between sessions. Only trust the cache if the recorded identity still matches the
        // currently selected target. Legacy entries with no fingerprint are dropped so a stale
        // pass cannot mask a fresh, unvalidated drive.
        var current = DriveValidationIdentity.Compute(target);
        if (string.IsNullOrWhiteSpace(result.Evidence.IdentityFingerprint) ||
            !DriveValidationIdentity.Matches(current, result.Evidence))
        {
            return null;
        }

        if (result.CompletedAtUtc is { } completed &&
            DateTimeOffset.UtcNow - completed > TimeSpan.FromDays(30))
        {
            return null;
        }

        return result;
    }

    /// <summary>
    /// Stamps the identity fingerprint onto a result when the service did not (test stubs, future
    /// alternate service implementations). Without this, the cache lookup's identity guard would
    /// reject the result that was just written, surfacing a confusing "not validated" state right
    /// after the user runs a validation.
    /// </summary>
    private static DriveValidationResult EnsureIdentityCaptured(DriveValidationResult result, UsbTargetInfo target)
    {
        if (!string.IsNullOrWhiteSpace(result.Evidence.IdentityFingerprint))
        {
            return result;
        }

        var identity = DriveValidationIdentity.Compute(target);
        return new DriveValidationResult
        {
            RunId = result.RunId,
            Status = result.Status,
            Mode = result.Mode,
            Phase = result.Phase,
            Summary = result.Summary,
            Detail = result.Detail,
            StartedAtUtc = result.StartedAtUtc,
            CompletedAtUtc = result.CompletedAtUtc ?? DateTimeOffset.UtcNow,
            TargetRootPath = string.IsNullOrWhiteSpace(result.TargetRootPath) ? target.RootPath : result.TargetRootPath,
            Evidence = result.Evidence with
            {
                IdentityFingerprint = identity.Hash,
                IdentityConfidence = identity.ConfidenceText,
                VolumeSerial = identity.VolumeSerial,
                TargetTotalBytes = result.Evidence.TargetTotalBytes == 0 ? target.TotalBytes : result.Evidence.TargetTotalBytes,
                TargetLabel = string.IsNullOrWhiteSpace(result.Evidence.TargetLabel) ? target.Label : result.Evidence.TargetLabel,
                TargetVolume = string.IsNullOrWhiteSpace(result.Evidence.TargetVolume) ? target.RootPath : result.Evidence.TargetVolume,
                TargetDriveModel = string.IsNullOrWhiteSpace(result.Evidence.TargetDriveModel) ? target.DeviceModel : result.Evidence.TargetDriveModel,
                BusType = string.IsNullOrWhiteSpace(result.Evidence.BusType) ? target.BusType : result.Evidence.BusType
            }
        };
    }

    private void PersistDriveValidationResult(DriveValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(result.TargetRootPath))
        {
            return;
        }

        var key = GetDriveValidationCacheKey(result.TargetRootPath);
        _driveValidationResultsByRoot[key] = result;
        SaveDriveValidationCache();
    }

    private void SyncDriveValidationToPortProfile(DriveValidationResult result, UsbTargetInfo target)
    {
        try
        {
            var profile = _usbMachineProfileStore.LoadOrCreate();
            var snap = _usbIntelligenceService.BuildTopologySnapshot(target);
            var portKey = snap.SelectedTargetStablePortKey;
            if (string.IsNullOrWhiteSpace(portKey))
            {
                return;
            }

            var rec = profile.KnownPorts.FirstOrDefault(p => p.StablePortKey == portKey);
            if (rec is null)
            {
                return;
            }

            rec.LastDriveValidationPortStatus = result.ToPortStatus();
            rec.LastDriveValidationSummary = result.Summary;
            rec.LastDriveValidationUtc = result.CompletedAtUtc ?? DateTimeOffset.UtcNow;
            _usbMachineProfileStore.Save(profile);
        }
        catch
        {
            // Profile sync is best effort.
        }
    }

    private void AppendDriveValidationLog(DriveValidationResult result)
    {
        var severity = result.Status switch
        {
            DriveValidationStatus.Passed => LogSeverity.Info,
            DriveValidationStatus.PassedWithWarnings or DriveValidationStatus.CleanupWarning => LogSeverity.Warning,
            DriveValidationStatus.Failed or DriveValidationStatus.UnsafeTargetBlocked => LogSeverity.Error,
            _ => LogSeverity.Info
        };

        AppendLog(new LogLine(
            DateTimeOffset.Now,
            $"[INFO] Drive Validator {result.Status} on {result.TargetRootPath}: {result.Summary}",
            severity,
            isErrorStream: severity == LogSeverity.Error));
    }

    private static string GetDriveValidationCacheKey(string rootPath) =>
        DriveValidationIdentity.NormalizeRoot(rootPath);

    private void LoadDriveValidationCache()
    {
        try
        {
            if (!File.Exists(_driveValidationCachePath))
            {
                return;
            }

            var cached = JsonSerializer.Deserialize<Dictionary<string, DriveValidationResult>>(
                File.ReadAllText(_driveValidationCachePath));
            if (cached is null)
            {
                return;
            }

            foreach (var pair in cached)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || !pair.Value.CompletedAtUtc.HasValue)
                {
                    continue;
                }

                if (DateTimeOffset.UtcNow - pair.Value.CompletedAtUtc.Value >= TimeSpan.FromDays(30))
                {
                    continue;
                }

                // Drop legacy entries that predate identity fingerprinting — they cannot be safely
                // matched to a re-inserted drive on the same letter, so force re-validation.
                if (string.IsNullOrWhiteSpace(pair.Value.Evidence.IdentityFingerprint))
                {
                    continue;
                }

                _driveValidationResultsByRoot[pair.Key] = pair.Value;
            }
        }
        catch
        {
            // Best effort.
        }
    }

    private void SaveDriveValidationCache()
    {
        try
        {
            var stable = _driveValidationResultsByRoot
                .Where(p => p.Value.CompletedAtUtc.HasValue &&
                            p.Value.Status is not DriveValidationStatus.Running and not DriveValidationStatus.NotRun)
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

            Directory.CreateDirectory(Path.GetDirectoryName(_driveValidationCachePath)!);
            File.WriteAllText(_driveValidationCachePath, JsonSerializer.Serialize(stable, IndentedJsonOptions));
        }
        catch
        {
            // Best effort.
        }
    }
}
