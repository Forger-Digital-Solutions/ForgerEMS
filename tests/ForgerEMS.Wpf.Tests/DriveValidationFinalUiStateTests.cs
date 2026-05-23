using System;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.DriveValidation;
using VentoyToolkitSetup.Wpf.ViewModels;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Part A — final-UI-state coverage. The service's last progress event before a terminal result
/// is always an in-flight phase (e.g. CleaningUp ~92%). Before the fix, ApplyDriveValidationResultToUi
/// did not overwrite Phase/Progress/Value, so the panel stayed visually stuck on "Removing temporary
/// validation files…" / "3/3 · CleaningUp" / 92% even after the result was already on screen
/// (Dev Smoke 2026-05-23). These tests pin the new terminal-state behavior.
/// </summary>
public sealed class DriveValidationFinalUiStateTests
{
    private static UsbTargetInfo RemovableTarget(string root = "E:\\") => new()
    {
        DriveLetter = "E:",
        RootPath = root,
        Label = "TestUSB",
        FileSystem = "exFAT",
        TotalBytes = 64L * 1024 * 1024 * 1024,
        FreeBytes = 2L * 1024 * 1024 * 1024,
        DriveType = "Removable",
        BusType = "USB",
        IsLikelyUsb = true,
        IsRemovableMedia = true,
        IsSelectable = true,
        IsLargeDataPartition = true
    };

    private static MainViewModel BuildViewModelWith(DriveValidationResult result, UsbTargetInfo target)
    {
        var vm = new MainViewModel(
            new BackendDiscoveryService(),
            new PowerShellRunnerService(),
            new StaticUsbDetectionService(target),
            new ManagedDownloadSummaryService(),
            new ScriptStatusParser(),
            new AcceptingPromptService(),
            new VentoyIntegrationService(new PowerShellRunnerService(), new AppRuntimeService()),
            new AppRuntimeService(),
            new UsbBenchmarkService(new PowerShellRunnerService()),
            new CopilotService(new CopilotProviderRegistry()),
            new CopilotProviderRegistry(),
            driveValidationService: new ProgressEmittingStubService(result));
        vm.UsbTargets.Add(target);
        vm.SelectedUsbTarget = target;
        return vm;
    }

    private static DriveValidationResult MakeResult(DriveValidationStatus status, string summary)
    {
        return new DriveValidationResult
        {
            Status = status,
            Mode = DriveValidationMode.QuickSafeCheck,
            // Service produces CleaningUp as the last live phase before returning — leave it set
            // on the result so we can verify ApplyDriveValidationResultToUi overwrites the UI.
            Phase = status switch
            {
                DriveValidationStatus.Cancelled => DriveValidationPhase.Cancelled,
                DriveValidationStatus.Failed => DriveValidationPhase.Failed,
                _ => DriveValidationPhase.Complete
            },
            Summary = summary,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            TargetRootPath = "E:\\",
            Evidence = new DriveValidationEvidence
            {
                SamplesPlanned = 3,
                SamplesVerified = status == DriveValidationStatus.Cancelled ? 1 : 3,
                CleanupStatus = "All temporary validation files removed."
            }
        };
    }

    [Theory]
    [InlineData(DriveValidationStatus.Passed, "Validation complete.")]
    [InlineData(DriveValidationStatus.PassedWithWarnings, "Validation complete with warnings.")]
    [InlineData(DriveValidationStatus.Failed, "Validation failed.")]
    [InlineData(DriveValidationStatus.Cancelled, "Validation cancelled.")]
    [InlineData(DriveValidationStatus.CleanupWarning, "Validation complete — cleanup warning.")]
    public void TerminalState_OverwritesCleaningUpPhase(DriveValidationStatus status, string expectedPhase)
    {
        var target = RemovableTarget();
        var vm = BuildViewModelWith(MakeResult(status, "summary"), target);

        vm.RunDriveValidatorCommand.Execute(null);

        Assert.Equal(expectedPhase, vm.DriveValidatorPhaseDisplay);
        Assert.DoesNotContain("CleaningUp", vm.DriveValidatorProgressDisplay, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Removing temporary", vm.DriveValidatorPhaseDisplay, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DriveValidationStatus.Passed)]
    [InlineData(DriveValidationStatus.PassedWithWarnings)]
    [InlineData(DriveValidationStatus.Failed)]
    [InlineData(DriveValidationStatus.CleanupWarning)]
    public void TerminalCompletion_SetsProgressBarTo100(DriveValidationStatus status)
    {
        var target = RemovableTarget();
        var vm = BuildViewModelWith(MakeResult(status, "summary"), target);

        vm.RunDriveValidatorCommand.Execute(null);

        Assert.Equal(100, vm.DriveValidatorProgressValue);
    }

    [Fact]
    public void Cancelled_LeavesProgressAtZero()
    {
        var target = RemovableTarget();
        var vm = BuildViewModelWith(MakeResult(DriveValidationStatus.Cancelled, "Cancelled"), target);

        vm.RunDriveValidatorCommand.Execute(null);

        Assert.Equal(0, vm.DriveValidatorProgressValue);
    }

    [Fact]
    public void TerminalState_StartCommandReenabled_CancelDisabled()
    {
        var target = RemovableTarget();
        var vm = BuildViewModelWith(MakeResult(DriveValidationStatus.Passed, "ok"), target);

        vm.RunDriveValidatorCommand.Execute(null);

        Assert.False(vm.IsDriveValidatorRunning);
        Assert.True(vm.RunDriveValidatorCommand.CanExecute(null));
        Assert.False(vm.CancelDriveValidatorCommand.CanExecute(null));
    }

    [Fact]
    public void TerminalState_KeepsResultSummaryAndEvidenceVisible()
    {
        var target = RemovableTarget();
        var result = MakeResult(DriveValidationStatus.Passed, "No issues found in sampled validation.");
        var vm = BuildViewModelWith(result, target);

        vm.RunDriveValidatorCommand.Execute(null);

        Assert.Contains("No issues found", vm.DriveValidatorResultSummary, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(vm.DriveValidatorEvidenceDisplay));
    }

    [Fact]
    public void TerminalState_PopulatesCompactSummaryCardFields()
    {
        var target = RemovableTarget();
        var result = MakeResult(DriveValidationStatus.Passed, "No issues found in sampled validation.");
        var vm = BuildViewModelWith(result, target);

        vm.RunDriveValidatorCommand.Execute(null);

        Assert.Equal("Passed", vm.DriveValidatorQuickSummary);
        Assert.Equal("Passed", vm.DriveValidatorLastStatusDisplay);
        Assert.NotEqual("—", vm.DriveValidatorLastValidationAgeDisplay);
    }

    /// <summary>
    /// Stub that emits a CleaningUp progress event before returning the terminal result, exactly
    /// like the real DriveValidationService does. Without the Part A fix, the VM would record
    /// "CleaningUp" and 92% as its last UI state — these tests pin that the terminal result must
    /// overwrite them.
    /// </summary>
    private sealed class ProgressEmittingStubService(DriveValidationResult result) : IDriveValidationService
    {
        public Task<DriveValidationResult> RunAsync(
            UsbTargetInfo target,
            DriveValidationOptions options,
            string? portPathHint = null,
            Action<DriveValidationProgress>? onProgress = null,
            CancellationToken cancellationToken = default)
        {
            onProgress?.Invoke(new DriveValidationProgress
            {
                Phase = DriveValidationPhase.CleaningUp,
                Message = "Removing temporary validation files…",
                SampleIndex = 3,
                SampleCount = 3,
                ProgressFraction = 0.92
            });
            return Task.FromResult(result);
        }
    }

    private sealed class StaticUsbDetectionService(UsbTargetInfo target) : IUsbDetectionService
    {
        public Task<UsbDetectionResult> GetUsbTargetsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new UsbDetectionResult { Targets = [target] });
    }

    private sealed class AcceptingPromptService : IUserPromptService
    {
        public bool Confirm(string title, string message) => true;

        public string? PromptText(string title, string message, string initialValue = "") => initialValue;

        public void ShowMessage(string title, string message, System.Windows.MessageBoxImage image = System.Windows.MessageBoxImage.Information)
        {
        }
    }
}
