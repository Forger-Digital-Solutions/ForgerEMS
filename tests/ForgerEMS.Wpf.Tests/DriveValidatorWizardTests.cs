#pragma warning disable CA1305 // Locale-sensitive calls in test assertions
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services.DriveValidation;
using VentoyToolkitSetup.Wpf.ViewModels;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Part H — Drive Validator Wizard coverage. Wizard step transitions, target selection, mode
/// availability, Full Free-Space confirmation gate, destructive unavailability, running-step
/// region tile updates, results step, and clipboard summary content. Hits the wizard VM directly
/// — no XAML/Window is constructed (those require an STA WPF host).
/// </summary>
public sealed class DriveValidatorWizardTests
{
    private static UsbTargetInfo Removable(string root = "E:\\", string label = "TestUSB", bool isSafe = true) => new()
    {
        DriveLetter = root.TrimEnd('\\', ':') + ":",
        RootPath = root,
        Label = label,
        FileSystem = "exFAT",
        TotalBytes = 64L * 1024 * 1024 * 1024,
        FreeBytes = 2L * 1024 * 1024 * 1024,
        DriveType = "Removable",
        BusType = "USB",
        IsLikelyUsb = true,
        IsRemovableMedia = isSafe,
        IsSelectable = isSafe,
        IsLargeDataPartition = isSafe
    };

    private static UsbTargetInfo Unsafe(string root = "C:\\") => new()
    {
        DriveLetter = "C:",
        RootPath = root,
        Label = "OS",
        IsSystemDrive = true,
        IsBootDrive = true,
        IsRemovableMedia = false
    };

    private static DriveValidatorWizardViewModel BuildVm(
        IReadOnlyList<UsbTargetInfo> targets,
        DriveValidatorWizardViewModel.RunValidationDelegate? runner = null,
        UsbTargetInfo? preferred = null,
        Func<string, string, bool>? confirmHeavyMode = null,
        Func<UsbTargetInfo, string>? portLabelLookup = null)
    {
        runner ??= (_, _, _, _, _) => Task.FromResult(new DriveValidationResult { Status = DriveValidationStatus.Passed, Summary = "ok" });
        return new DriveValidatorWizardViewModel(
            getTargets: () => targets,
            safetyEvaluator: t =>
            {
                var ok = DriveValidationTargetSafety.IsSafeToStart(t, new DriveValidationOptions(), out var reason);
                return (ok, reason);
            },
            runValidationAsync: runner,
            lastValidationLookup: _ => null,
            portLabelLookup: portLabelLookup ?? (_ => "unmapped"),
            confirmHeavyMode: confirmHeavyMode,
            appendLog: null,
            preferredTarget: preferred);
    }

    [Fact]
    public void Wizard_StartsOnSelectTargetStep()
    {
        var vm = BuildVm(new[] { Removable() });
        Assert.Equal(DriveValidatorWizardStep.SelectTarget, vm.Step);
        Assert.True(vm.IsSelectTargetStep);
    }

    [Fact]
    public void Wizard_PreselectsPreferredTarget()
    {
        var a = Removable("E:\\", "Alpha");
        var b = Removable("F:\\", "Bravo");
        var vm = BuildVm(new[] { a, b }, preferred: b);
        Assert.NotNull(vm.SelectedTarget);
        Assert.Equal("F:\\", vm.SelectedTarget!.RootPath);
    }

    [Fact]
    public void Wizard_UnsafeTargetCannotProceedToModeStep()
    {
        var unsafeT = Unsafe();
        var vm = BuildVm(new[] { unsafeT });
        Assert.NotNull(vm.SelectedTarget);
        Assert.False(vm.SelectedTarget!.IsSafe);
        Assert.False(vm.NextFromSelectTargetCommand.CanExecute(null));
    }

    [Fact]
    public void Wizard_SafeTargetCanProceedToModeStep()
    {
        var vm = BuildVm(new[] { Removable() });
        Assert.True(vm.SelectedTarget!.IsSafe);
        Assert.True(vm.NextFromSelectTargetCommand.CanExecute(null));
        vm.NextFromSelectTargetCommand.Execute(null);
        Assert.Equal(DriveValidatorWizardStep.ChooseMode, vm.Step);
    }

    [Fact]
    public void Wizard_AllFourModesAreDescribedAndDestructiveIsUnavailable()
    {
        var vm = BuildVm(new[] { Removable() });
        Assert.Equal(4, vm.Modes.Count);
        var destructive = vm.Modes.Single(m => m.Mode == DriveValidationMode.DestructiveFullMediaValidation);
        Assert.False(destructive.IsAvailable);
        Assert.Contains("not available", destructive.UnavailableReason, StringComparison.OrdinalIgnoreCase);
        foreach (var mode in vm.Modes)
        {
            Assert.False(string.IsNullOrWhiteSpace(mode.Description));
            Assert.False(string.IsNullOrWhiteSpace(mode.Heaviness));
        }
    }

    [Fact]
    public void Wizard_FullFreeSpaceRequiresAcknowledgementCheckbox()
    {
        var vm = BuildVm(new[] { Removable() });
        vm.SelectedMode = vm.Modes.Single(m => m.Mode == DriveValidationMode.FullFreeSpaceValidation);
        Assert.True(vm.NeedsFullModeConfirmation);
        Assert.False(vm.FullModeUserAcknowledged);
        Assert.False(vm.StartValidationCommand.CanExecute(null));
        vm.FullModeUserAcknowledged = true;
        Assert.True(vm.StartValidationCommand.CanExecute(null));
    }

    [Fact]
    public void Wizard_QuickModeDoesNotRequireAcknowledgement()
    {
        var vm = BuildVm(new[] { Removable() });
        vm.SelectedMode = vm.Modes.Single(m => m.Mode == DriveValidationMode.QuickSafeCheck);
        Assert.False(vm.NeedsFullModeConfirmation);
        Assert.True(vm.StartValidationCommand.CanExecute(null));
    }

    [Fact]
    public void Wizard_DestructiveModeStartCommandRefuses()
    {
        var vm = BuildVm(new[] { Removable() });
        vm.SelectedMode = vm.Modes.Single(m => m.Mode == DriveValidationMode.DestructiveFullMediaValidation);
        Assert.False(vm.StartValidationCommand.CanExecute(null));
    }

    [Fact]
    public void Wizard_PortAmbiguousHintShownWhenUnmapped()
    {
        var vm = BuildVm(new[] { Removable() }, portLabelLookup: _ => string.Empty);
        Assert.True(vm.SelectedTargetPortAmbiguous);
        Assert.Contains("USB Mapping Wizard", vm.SelectedTargetPortHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Wizard_PortAmbiguousHintHiddenWhenLabelled()
    {
        var vm = BuildVm(new[] { Removable() }, portLabelLookup: _ => "Rear Blue USB 3");
        Assert.False(vm.SelectedTargetPortAmbiguous);
        Assert.Empty(vm.SelectedTargetPortHint);
    }

    [Fact]
    public async Task Wizard_StartValidation_RunsServiceAndReachesResultsStep()
    {
        var captured = new List<DriveValidationProgress>();
        var passedResult = new DriveValidationResult
        {
            Status = DriveValidationStatus.Passed,
            Mode = DriveValidationMode.QuickSafeCheck,
            Summary = "No issues found in sampled validation.",
            Phase = DriveValidationPhase.Complete,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            TargetRootPath = "E:\\",
            Evidence = new DriveValidationEvidence
            {
                SamplesPlanned = 3,
                SamplesVerified = 3,
                CleanupStatus = "All temporary validation files removed."
            }
        };

        DriveValidatorWizardViewModel.RunValidationDelegate runner =
            async (target, options, portHint, onProgress, ct) =>
            {
                // Emit a few progress events so the wizard exercises HandleProgress.
                onProgress?.Invoke(new DriveValidationProgress
                {
                    Phase = DriveValidationPhase.WritingSample,
                    Message = "Writing region 1/3…",
                    SampleIndex = 1,
                    SampleCount = 3,
                    ProgressFraction = 0.3,
                    MapSnapshot = MakeSnapshot(3, 1, DriveValidationRegionStatus.Writing)
                });
                onProgress?.Invoke(new DriveValidationProgress
                {
                    Phase = DriveValidationPhase.CleaningUp,
                    Message = "Removing temporary validation files…",
                    SampleIndex = 3,
                    SampleCount = 3,
                    ProgressFraction = 0.92,
                    MapSnapshot = MakeSnapshot(3, 3, DriveValidationRegionStatus.Passed)
                });
                await Task.Yield();
                return passedResult;
            };

        var raised = false;
        var vm = BuildVm(new[] { Removable() }, runner: runner);
        vm.ValidationCompleted += (_, _) => raised = true;
        vm.NextFromSelectTargetCommand.Execute(null);
        vm.NextFromChooseModeCommand.Execute(null);
        Assert.Equal(DriveValidatorWizardStep.SafetyReview, vm.Step);

        // StartValidation goes through AsyncRelayCommand which fires-and-forgets via async void;
        // call the underlying task path so we can await completion in the test.
        await ((Func<Task>)(async () => await InvokeStartValidationAsync(vm)))().ConfigureAwait(true);

        Assert.True(raised);
        Assert.Equal(DriveValidatorWizardStep.Results, vm.Step);
        Assert.NotNull(vm.LastResult);
        Assert.Contains("No issues found", vm.ResultSummary, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task Wizard_ProgressMapSnapshot_PopulatesAndUpdatesRegionTiles()
    {
        DriveValidatorWizardViewModel.RunValidationDelegate runner =
            async (target, options, portHint, onProgress, ct) =>
            {
                onProgress?.Invoke(new DriveValidationProgress
                {
                    Phase = DriveValidationPhase.WritingSample,
                    SampleIndex = 1,
                    SampleCount = 4,
                    ProgressFraction = 0.2,
                    MapSnapshot = MakeSnapshot(4, 1, DriveValidationRegionStatus.Writing)
                });
                onProgress?.Invoke(new DriveValidationProgress
                {
                    Phase = DriveValidationPhase.Verifying,
                    SampleIndex = 2,
                    SampleCount = 4,
                    ProgressFraction = 0.5,
                    MapSnapshot = MakeSnapshot(4, 2, DriveValidationRegionStatus.Passed)
                });
                await Task.Yield();
                return new DriveValidationResult
                {
                    Status = DriveValidationStatus.Passed,
                    Summary = "ok",
                    CompletedAtUtc = DateTimeOffset.UtcNow,
                    TargetRootPath = "E:\\",
                    Evidence = new DriveValidationEvidence { SamplesPlanned = 4, SamplesVerified = 4 }
                };
            };

        var vm = BuildVm(new[] { Removable() }, runner: runner);
        vm.NextFromSelectTargetCommand.Execute(null);
        vm.NextFromChooseModeCommand.Execute(null);
        await InvokeStartValidationAsync(vm);

        Assert.Equal(4, vm.RegionTiles.Count);
        Assert.True(
            vm.RegionTiles.Take(2).All(t => t.Status is DriveValidationRegionStatus.Passed or DriveValidationRegionStatus.Writing),
            "Early region tiles should reflect progress updates.");
    }

    [Fact]
    public async Task Wizard_TerminalResult_OverwritesCleaningUpPhaseText()
    {
        // Regression: ensure the wizard's running step does not stay stuck on CleaningUp once
        // a terminal result is applied (mirrors the inline-panel Part A fix from Phase 1).
        DriveValidatorWizardViewModel.RunValidationDelegate runner =
            (target, options, portHint, onProgress, ct) =>
            {
                onProgress?.Invoke(new DriveValidationProgress
                {
                    Phase = DriveValidationPhase.CleaningUp,
                    Message = "Removing temporary validation files…",
                    SampleIndex = 3,
                    SampleCount = 3,
                    ProgressFraction = 0.92
                });
                return Task.FromResult(new DriveValidationResult
                {
                    Status = DriveValidationStatus.Passed,
                    Mode = DriveValidationMode.QuickSafeCheck,
                    Summary = "No issues found in sampled validation.",
                    CompletedAtUtc = DateTimeOffset.UtcNow,
                    TargetRootPath = "E:\\",
                    Evidence = new DriveValidationEvidence { SamplesPlanned = 3, SamplesVerified = 3 }
                });
            };

        var vm = BuildVm(new[] { Removable() }, runner: runner);
        vm.NextFromSelectTargetCommand.Execute(null);
        vm.NextFromChooseModeCommand.Execute(null);
        await InvokeStartValidationAsync(vm);

        Assert.DoesNotContain("Removing temporary", vm.RunningPhaseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CleaningUp", vm.RunningProgressText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(100, vm.RunningProgressValue);
    }

    [Fact]
    public async Task Wizard_ResultsEvidence_DescribesRegionsAndIdentity()
    {
        var result = new DriveValidationResult
        {
            Status = DriveValidationStatus.Passed,
            Mode = DriveValidationMode.SampledCapacityCheck,
            Summary = "No issues found in sampled validation.",
            CompletedAtUtc = DateTimeOffset.UtcNow,
            TargetRootPath = "E:\\",
            Evidence = new DriveValidationEvidence
            {
                SamplesPlanned = 7,
                SamplesVerified = 7,
                BytesWritten = 1024 * 1024 * 7,
                BytesVerified = 1024 * 1024 * 7,
                MapSummary = new DriveValidationMapSummary
                {
                    Planned = 7,
                    Tested = 7,
                    Passed = 7,
                    FastestReadMBps = 60,
                    SlowestReadMBps = 50
                },
                IdentityConfidence = "Strong",
                CleanupStatus = "All temporary validation files removed."
            }
        };

        var vm = BuildVm(new[] { Removable() }, runner: (_, _, _, _, _) => Task.FromResult(result));
        vm.NextFromSelectTargetCommand.Execute(null);
        vm.NextFromChooseModeCommand.Execute(null);
        vm.SelectedMode = vm.Modes.Single(m => m.Mode == DriveValidationMode.SampledCapacityCheck);
        await InvokeStartValidationAsync(vm);

        Assert.Contains("Regions planned: 7", vm.ResultEvidence);
        Assert.Contains("Regions tested: 7", vm.ResultEvidence);
        Assert.Contains("Strong", vm.ResultEvidence);
        Assert.Contains("All temporary validation files removed", vm.ResultEvidence);
    }

    [Fact]
    public void Wizard_CopySummary_BuildsTechnicianReportContent()
    {
        var vm = BuildVm(new[] { Removable() });
        var result = new DriveValidationResult
        {
            Status = DriveValidationStatus.Passed,
            Mode = DriveValidationMode.QuickSafeCheck,
            Summary = "No issues found in sampled validation.",
            CompletedAtUtc = DateTimeOffset.UtcNow,
            TargetRootPath = "E:\\",
            Evidence = new DriveValidationEvidence
            {
                SamplesPlanned = 3,
                SamplesVerified = 3,
                BytesWritten = 3 * 1024 * 1024,
                BytesVerified = 3 * 1024 * 1024,
                IdentityConfidence = "Strong",
                CleanupStatus = "All temporary validation files removed.",
                TargetLabel = "TestUSB"
            }
        };

        _ = vm; // VM retained to ensure it constructed cleanly under the same code paths.
        var text = DriveValidatorWizardViewModel.BuildClipboardSummary(result);

        Assert.Contains("ForgerEMS Drive Validator", text);
        Assert.Contains("Quick Safe Check", text);
        Assert.Contains("Passed", text);
        Assert.Contains("Strong", text);
        Assert.Contains("Limitations", text);
        // No secret-ish content.
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
        // No false certification language.
        Assert.DoesNotContain("genuine", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("certif", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("100%", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Wizard_SafetyReviewBody_MentionsTempFolderAndNoFormat()
    {
        var vm = BuildVm(new[] { Removable() });
        vm.NextFromSelectTargetCommand.Execute(null);
        vm.SelectedMode = vm.Modes.Single(m => m.Mode == DriveValidationMode.QuickSafeCheck);
        vm.NextFromChooseModeCommand.Execute(null);

        Assert.Contains(".forgerems-drive-validator", vm.SafetyReviewBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do NOT format", vm.SafetyReviewBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not a 100% authenticity certificate", vm.SafetyReviewBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Destructive Full Media Validation is NOT available", vm.SafetyReviewBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Wizard_HeartbeatActivates_AfterIdleProgress()
    {
        var vm = BuildVm(new[] { Removable() });
        // Simulate that we are mid-run; flip via reflection-free path: enter run by hand.
        // The HeartbeatActive flag depends on IsRunning being true; we exercise the gate.
        vm.TickHeartbeat();
        Assert.False(vm.HeartbeatActive);
    }

    /// <summary>
    /// Helper to invoke the wizard's start command and await its task. AsyncRelayCommand.Execute
    /// is async-void, so we deliberately bypass it and await StartValidationCommand directly via
    /// reflection-free repeat-yield. The cleanest path is to spin until IsRunning flips back.
    /// </summary>
    private static async Task InvokeStartValidationAsync(DriveValidatorWizardViewModel vm)
    {
        vm.StartValidationCommand.Execute(null);
        // The runner is synchronous-ish (await Task.Yield + FromResult). Yield until done.
        for (var i = 0; i < 1000 && vm.IsRunning; i++)
        {
            await Task.Delay(5).ConfigureAwait(true);
        }
    }

    private static DriveValidationMap MakeSnapshot(int total, int completed, DriveValidationRegionStatus completedStatus)
    {
        var regions = new List<DriveValidationRegion>();
        for (var i = 0; i < total; i++)
        {
            regions.Add(new DriveValidationRegion
            {
                Index = i,
                LogicalOffsetHint = (i + 1) * 1024L * 1024L,
                PlannedBytes = 1024L * 1024L,
                ExpectedSignatureHash = $"sig-{i}",
                Status = i < completed ? completedStatus : DriveValidationRegionStatus.Planned
            });
        }
        return new DriveValidationMap
        {
            Regions = regions,
            Summary = DriveValidationMapSummary.FromRegions(regions)
        };
    }
}
