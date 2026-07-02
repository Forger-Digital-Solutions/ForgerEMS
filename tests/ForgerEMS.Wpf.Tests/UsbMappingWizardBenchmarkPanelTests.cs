using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using VentoyToolkitSetup.Wpf.ViewModels;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Port Mapping Wizard final-screen benchmark panel: running a benchmark from the Done step
/// must show progress and readings inside the wizard, tie results to the mapped port, clear
/// stale readings when the port changes, and surface friendly errors.
/// </summary>
[Collection(UsbPortLabelResolverSerialFixture.Name)]
public sealed class UsbMappingWizardBenchmarkPanelTests
{
    /// <summary>Alternates port key per snapshot build so before/after capture yields a port change.</summary>
    private sealed class AlternatingPortIntelligence : IUsbIntelligenceService
    {
        private int _call;

        public UsbTopologySnapshot BuildTopologySnapshot(UsbTargetInfo? selectedTarget, UsbTopologyBuildOptions? options = null)
        {
            var port = Interlocked.Increment(ref _call) % 2 == 1 ? "port-a" : "port-b";
            return new UsbTopologySnapshot
            {
                GeneratedUtc = DateTimeOffset.UtcNow,
                CombinedConfidenceScore = 60,
                CombinedConfidenceReason = "alt",
                Devices =
                [
                    new UsbDeviceInfo
                    {
                        FriendlyName = "USB Disk",
                        DriveLetter = "E:",
                        InferredSpeed = UsbSpeedClassification.Usb3,
                        StableDeviceKey = "dev-1",
                        StablePortKey = port,
                        ControllerKey = "c1",
                        HubKey = "h0",
                        VolumeIdentityHash = "vol-fixed",
                        IsRemovableMassStorage = true
                    }
                ],
                Controllers = [],
                Ports = [],
                SummaryLine = "",
                SelectedTargetRecommendation = new UsbBuilderRecommendation
                {
                    ClassificationLine = "Quality: Good",
                    Summary = "changed",
                    Detail = "",
                    Risk = UsbPortRiskLevel.Low,
                    Speed = UsbSpeedClassification.Usb3,
                    Quality = UsbBuilderQuality.Good,
                    ConfidenceScore = 60,
                    ConfidenceReason = "alt"
                }
            };
        }

        public Task WriteLatestReportAsync(string reportsDirectory, UsbTopologySnapshot snapshot) => Task.CompletedTask;

        public UsbBuilderPreflightResult GetVentoyPreflight(UsbTargetInfo? selectedTarget, UsbTopologySnapshot? snapshot) =>
            new()
            {
                ShouldWarn = false,
                Message = "",
                Speed = UsbSpeedClassification.Unknown,
                Risk = UsbPortRiskLevel.Unknown,
                Quality = UsbBuilderQuality.Unknown
            };
    }

    private sealed class RemovalThenReinsertTargets
    {
        private readonly UsbTargetInfo _target;
        private bool _detectMode;
        private int _callsInDetect;

        public RemovalThenReinsertTargets(UsbTargetInfo target) => _target = target;

        public void StartDetectPass()
        {
            _detectMode = true;
            _callsInDetect = 0;
        }

        public bool IsMounted => !_detectMode || _callsInDetect != 1;

        public IReadOnlyList<UsbTargetInfo> GetTargets()
        {
            if (!_detectMode)
            {
                return [_target];
            }

            _callsInDetect++;
            return _callsInDetect == 1 ? [] : [_target];
        }
    }

    private static UsbTargetInfo MakeRemovable(string letter, string label) =>
        new()
        {
            DriveLetter = letter,
            RootPath = letter.EndsWith('\\') ? letter : letter + "\\",
            Label = label,
            FileSystem = "NTFS",
            TotalBytes = 16L * 1024 * 1024 * 1024,
            FreeBytes = 8L * 1024 * 1024 * 1024,
            DriveType = "Removable",
            BusType = "USB",
            IsLikelyUsb = true,
            IsRemovableMedia = true,
            IsSystemDrive = false,
            IsBootDrive = false,
            IsEfiSystemPartition = false,
            IsUndersizedPartition = false
        };

    private static UsbBenchmarkResult CompletedResult() =>
        new()
        {
            Succeeded = true,
            Status = "Complete",
            Summary = "USB benchmark complete",
            WriteSpeedMBps = 50,
            ReadSpeedMBps = 120,
            WriteSpeedDisplay = "50 MB/s",
            ReadSpeedDisplay = "120 MB/s",
            BenchmarkDurationMs = 12_500,
            Classification = "USB 3.x class throughput",
            LastTestedAt = DateTimeOffset.Now,
            ResultKind = UsbBenchmarkResultKind.Completed,
            UiSummaryLine = UsbBenchmarkUiMessages.BuildUiSummary(UsbBenchmarkResultKind.Completed, 120, 50)
        };

    private static async Task<UsbMappingWizardViewModel> CreateWizardAtDoneAsync(
        string root,
        Func<UsbTargetInfo, Task<UsbBenchmarkResult?>> runBenchmark,
        Func<string?>? getPowerHintLine = null,
        string label = "Left USB")
    {
        UsbPortLabelResolver.ResetSessionStateForTests();
        var store = new UsbMachineProfileStore(root);
        var target = MakeRemovable("E:", "Data");
        var targets = new RemovalThenReinsertTargets(target);
        var vm = new UsbMappingWizardViewModel(
            new AlternatingPortIntelligence(),
            store,
            targets.GetTargets,
            runBenchmark,
            detectOperationTimeoutOverride: TimeSpan.FromSeconds(5),
            isDriveRootMounted: _ => targets.IsMounted,
            getPowerHintLine: getPowerHintLine);
        vm.StartMappingCommand.Execute(null);
        vm.SelectedDevice = vm.DeviceOptions[0];
        vm.ContinueSelectDeviceCommand.Execute(null);
        vm.CaptureCurrentPortCommand.Execute(null);
        vm.NextAfterCaptureCommand.Execute(null);
        targets.StartDetectPass();
        await vm.DetectPortChangeAsync();
        Assert.True(vm.DetectionSuccess, "detection harness should report a successful port change");
        vm.NextToLabelCommand.Execute(null);
        vm.PortLabelDraft = label;
        vm.SavePortLabelCommand.Execute(null);
        Assert.True(vm.IsDoneStep, "wizard should reach the Done step");
        return vm;
    }

    private static string TempRoot(string tag) => Path.Combine(Path.GetTempPath(), $"fe-wizbench-{tag}-{Guid.NewGuid():N}");

    private static void Cleanup(string root)
    {
        try
        {
            Directory.Delete(root, true);
        }
        catch
        {
        }
    }

    [Fact]
    public async Task DoneStep_ExposesMappedPortDetails_AndBenchmarkCommand()
    {
        var root = TempRoot("done");
        try
        {
            var vm = await CreateWizardAtDoneAsync(root, _ => Task.FromResult<UsbBenchmarkResult?>(CompletedResult()));
            Assert.Equal("Left USB", vm.DoneResult?.Label);
            Assert.Equal("E:\\", vm.DoneResult?.MappedTarget?.RootPath);
            Assert.True(vm.RunBenchmarkOnThisPortCommand.CanExecute(null));
            Assert.False(vm.HasWizardBenchmarkReadings);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task RunBenchmark_RunsForMappedTarget_AndPopulatesInWizardReadings()
    {
        var root = TempRoot("readings");
        try
        {
            UsbTargetInfo? benchmarkedTarget = null;
            var vm = await CreateWizardAtDoneAsync(
                root,
                t =>
                {
                    benchmarkedTarget = t;
                    return Task.FromResult<UsbBenchmarkResult?>(CompletedResult());
                },
                getPowerHintLine: () => "On AC power; battery 80%");

            await vm.RunWizardBenchmarkAsync();

            Assert.Equal("E:\\", benchmarkedTarget?.RootPath);
            Assert.False(vm.IsWizardBenchmarkRunning);
            Assert.True(vm.HasWizardBenchmarkReadings);
            Assert.Equal("E:", vm.WizardBenchmarkResultRoot);
            Assert.False(vm.HasWizardBenchmarkError);
            Assert.Contains("complete", vm.WizardBenchmarkStatusText, StringComparison.OrdinalIgnoreCase);

            var lines = vm.WizardBenchmarkReadingLines.ToArray();
            Assert.Contains(lines, l => l.StartsWith("Port: Left USB", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.Contains("Read speed: 120.0 MB/s", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.Contains("Write speed: 50.0 MB/s", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.Contains("12.5 s", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.Contains("Power/charging hint: On AC power", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.StartsWith("Result mode: Full measured benchmark", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.StartsWith("Benchmarked: ", StringComparison.Ordinal));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task RunBenchmark_ShowsProgressInsideWizard_WithoutBlocking()
    {
        var root = TempRoot("progress");
        try
        {
            var tcs = new TaskCompletionSource<UsbBenchmarkResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var vm = await CreateWizardAtDoneAsync(root, _ => tcs.Task);

            var run = vm.RunWizardBenchmarkAsync();

            // While the benchmark is pending, the wizard shows progress state and blocks re-entry —
            // proving the async command returned control instead of blocking the caller.
            Assert.True(vm.IsWizardBenchmarkRunning);
            Assert.True(vm.HasWizardBenchmarkStatus);
            Assert.Contains("running", vm.WizardBenchmarkStatusText, StringComparison.OrdinalIgnoreCase);
            Assert.False(vm.RunBenchmarkOnThisPortCommand.CanExecute(null));
            Assert.False(run.IsCompleted);

            tcs.SetResult(CompletedResult());
            await run;

            Assert.False(vm.IsWizardBenchmarkRunning);
            Assert.True(vm.HasWizardBenchmarkReadings);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task MapAnotherPort_ClearsPreviousReadings()
    {
        var root = TempRoot("clear");
        try
        {
            var vm = await CreateWizardAtDoneAsync(root, _ => Task.FromResult<UsbBenchmarkResult?>(CompletedResult()));
            await vm.RunWizardBenchmarkAsync();
            Assert.True(vm.HasWizardBenchmarkReadings);

            vm.MapAnotherPortCommand.Execute(null);

            Assert.False(vm.HasWizardBenchmarkReadings);
            Assert.Empty(vm.WizardBenchmarkReadingLines);
            Assert.False(vm.HasWizardBenchmarkStatus);
            Assert.False(vm.HasWizardBenchmarkError);
            Assert.Equal(string.Empty, vm.WizardBenchmarkResultRoot);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task LateResultFromAbandonedRun_DoesNotPaintStaleReadings()
    {
        var root = TempRoot("stale");
        try
        {
            var tcs = new TaskCompletionSource<UsbBenchmarkResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var vm = await CreateWizardAtDoneAsync(root, _ => tcs.Task);

            var run = vm.RunWizardBenchmarkAsync();
            Assert.True(vm.IsWizardBenchmarkRunning);

            // The user maps another port while the old benchmark is still in flight.
            vm.MapAnotherPortCommand.Execute(null);
            tcs.SetResult(CompletedResult());
            await run;

            // The late result belongs to the abandoned run: nothing may be painted.
            Assert.False(vm.HasWizardBenchmarkReadings);
            Assert.Empty(vm.WizardBenchmarkReadingLines);
            Assert.False(vm.HasWizardBenchmarkStatus);
            Assert.False(vm.IsWizardBenchmarkRunning);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task BenchmarkException_ShowsFriendlyInWizardError()
    {
        var root = TempRoot("error");
        try
        {
            var vm = await CreateWizardAtDoneAsync(
                root,
                _ => Task.FromException<UsbBenchmarkResult?>(new InvalidOperationException("disk exploded")));

            await vm.RunWizardBenchmarkAsync();

            Assert.False(vm.IsWizardBenchmarkRunning);
            Assert.True(vm.HasWizardBenchmarkError);
            Assert.Contains("Live Logs", vm.WizardBenchmarkErrorText, StringComparison.Ordinal);
            // Friendly message, not a raw exception dump.
            Assert.DoesNotContain("InvalidOperationException", vm.WizardBenchmarkErrorText, StringComparison.Ordinal);
            Assert.False(vm.HasWizardBenchmarkReadings);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task NullResult_ExplainsSkippedBenchmark()
    {
        var root = TempRoot("null");
        try
        {
            var vm = await CreateWizardAtDoneAsync(root, _ => Task.FromResult<UsbBenchmarkResult?>(null));
            await vm.RunWizardBenchmarkAsync();

            Assert.True(vm.HasWizardBenchmarkError);
            Assert.Contains("skipped", vm.WizardBenchmarkErrorText, StringComparison.OrdinalIgnoreCase);
            Assert.False(vm.IsWizardBenchmarkRunning);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task BlockedBySafety_ShowsSkippedModeAndReason()
    {
        var root = TempRoot("blocked");
        try
        {
            var blocked = new UsbBenchmarkResult
            {
                Succeeded = false,
                Status = "Blocked",
                Summary = "Benchmark skipped",
                Details = "Target is a system drive.",
                ReadSpeedDisplay = "Skipped (unsafe)",
                WriteSpeedDisplay = "Skipped (unsafe)",
                ResultKind = UsbBenchmarkResultKind.BlockedBySafety,
                UiSummaryLine = UsbBenchmarkUiMessages.BuildUiSummary(UsbBenchmarkResultKind.BlockedBySafety, 0, 0)
            };
            var vm = await CreateWizardAtDoneAsync(root, _ => Task.FromResult<UsbBenchmarkResult?>(blocked));
            await vm.RunWizardBenchmarkAsync();

            Assert.True(vm.HasWizardBenchmarkReadings);
            var lines = vm.WizardBenchmarkReadingLines.ToArray();
            Assert.Contains(lines, l => l.Contains("safety gate", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(lines, l => l.Contains("Why: Target is a system drive.", StringComparison.Ordinal));
            Assert.True(vm.HasWizardBenchmarkError);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void ModeLine_LabelsLimitedAndSkippedRunsHonestly()
    {
        var cached = new UsbBenchmarkResult { ReadLikelyCached = true };
        Assert.Contains("Limited", UsbMappingWizardViewModel.BuildWizardBenchmarkModeLine(cached, UsbBenchmarkResultKind.Completed), StringComparison.Ordinal);

        var full = new UsbBenchmarkResult();
        Assert.Contains("Full measured", UsbMappingWizardViewModel.BuildWizardBenchmarkModeLine(full, UsbBenchmarkResultKind.Completed), StringComparison.Ordinal);

        Assert.Contains("Skipped", UsbMappingWizardViewModel.BuildWizardBenchmarkModeLine(full, UsbBenchmarkResultKind.BlockedBySafety), StringComparison.Ordinal);
        Assert.Contains("Cancelled", UsbMappingWizardViewModel.BuildWizardBenchmarkModeLine(full, UsbBenchmarkResultKind.CancelledByUser), StringComparison.Ordinal);
        Assert.Contains("Failed", UsbMappingWizardViewModel.BuildWizardBenchmarkModeLine(full, UsbBenchmarkResultKind.IoFailed), StringComparison.Ordinal);
    }
}
