using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using VentoyToolkitSetup.Wpf.ViewModels;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class UsbBenchmarkHardeningTests
{
    private sealed class CapturingBenchmarkService : IUsbBenchmarkService
    {
        private readonly Queue<Func<CancellationToken, Task<UsbBenchmarkResult>>> _runs = new();

        public List<CancellationToken> Tokens { get; } = [];

        public int StartedCount { get; private set; }

        public void Enqueue(Func<CancellationToken, Task<UsbBenchmarkResult>> run) => _runs.Enqueue(run);

        public Task<UsbBenchmarkResult> RunSequentialBenchmarkAsync(
            UsbTargetInfo target,
            Action<LogLine>? onOutput = null,
            CancellationToken cancellationToken = default)
        {
            Tokens.Add(cancellationToken);
            StartedCount++;
            if (_runs.Count == 0)
            {
                return Task.FromResult(CompletedResult());
            }

            return _runs.Dequeue()(cancellationToken);
        }
    }

    private static MainViewModel BuildBenchmarkViewModel(CapturingBenchmarkService benchmarkService, UsbTargetInfo target)
    {
        var powerShell = new PowerShellRunnerService();
        var runtime = new AppRuntimeService();
        var registry = new CopilotProviderRegistry();
        var vm = new MainViewModel(
            new BackendDiscoveryService(),
            powerShell,
            new StaticUsbDetectionService(target),
            new ManagedDownloadSummaryService(),
            new ScriptStatusParser(),
            new AcceptingPromptService(),
            new VentoyIntegrationService(powerShell, runtime),
            runtime,
            benchmarkService,
            new CopilotService(registry),
            registry,
            usbIntelligenceService: new UsbIntelligenceService(),
            autoIntelligenceOrchestrator: new NoOpAutoIntelligenceOrchestrator());
        vm.UsbTargets.Add(target);
        vm.SelectedUsbTarget = target;
        return vm;
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

    private static UsbTargetInfo BenchmarkTarget() =>
        new()
        {
            DriveLetter = "E:",
            RootPath = "E:\\",
            Label = "Ventoy",
            FileSystem = "exFAT",
            TotalBytes = 128L * 1024 * 1024 * 1024,
            FreeBytes = 100L * 1024 * 1024 * 1024,
            DriveType = "Removable",
            BusType = "USB",
            IsLikelyUsb = true,
            IsRemovableMedia = true,
            IsSystemDrive = false,
            IsBootDrive = false,
            IsEfiSystemPartition = false,
            IsUndersizedPartition = false,
            IsSelectable = true
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
            ResultKind = UsbBenchmarkResultKind.Completed,
            UiSummaryLine = UsbBenchmarkUiMessages.BuildUiSummary(UsbBenchmarkResultKind.Completed, 120, 50)
        };

    private static UsbBenchmarkResult CancelledResult() =>
        new()
        {
            Succeeded = false,
            Status = "Cancelled",
            Summary = "Benchmark cancelled",
            ResultKind = UsbBenchmarkResultKind.CancelledByUser,
            UiSummaryLine = UsbBenchmarkUiMessages.BuildUiSummary(UsbBenchmarkResultKind.CancelledByUser, 0, 0)
        };

    private static async Task WaitForStartedAsync(CapturingBenchmarkService service, int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (service.StartedCount < count)
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    [Fact]
    public void IdentitySnapshot_MismatchWhenCapacityChanges()
    {
        var a = UsbTargetIdentitySnapshot.Capture(new UsbTargetInfo
        {
            DriveLetter = "E:",
            Label = "USB",
            TotalBytes = 64L * 1024 * 1024 * 1024,
            FreeBytes = 32L * 1024 * 1024 * 1024,
            DeviceModel = "FlashDrive",
            ClassificationDetails = "bus=USB"
        });

        var b = new UsbTargetInfo
        {
            DriveLetter = "E:",
            Label = "USB",
            TotalBytes = 32L * 1024 * 1024 * 1024,
            FreeBytes = 16L * 1024 * 1024 * 1024,
            DeviceModel = "FlashDrive",
            ClassificationDetails = "bus=USB"
        };

        Assert.False(a.MatchesVolumeIdentity(b, out _), "Different capacity should break identity match.");
    }

    [Fact]
    public void ServiceResult_ShouldPersistSuccessfulHistory_OnlyWhenCompleted()
    {
        var ok = new UsbBenchmarkResult
        {
            Succeeded = true,
            Status = "Complete",
            WriteSpeedMBps = 40,
            ReadSpeedMBps = 120,
            ResultKind = UsbBenchmarkResultKind.Completed
        };
        Assert.True(ok.ShouldPersistSuccessfulHistory);

        var cancelled = new UsbBenchmarkResult
        {
            Succeeded = false,
            Status = "Cancelled",
            ResultKind = UsbBenchmarkResultKind.CancelledByUser
        };
        Assert.False(cancelled.ShouldPersistSuccessfulHistory);

        var blocked = new UsbBenchmarkResult
        {
            Succeeded = false,
            Status = "Blocked",
            ResultKind = UsbBenchmarkResultKind.BlockedBySafety
        };
        Assert.False(blocked.ShouldPersistSuccessfulHistory);
    }

    [Fact]
    public void UiMessages_CompletedFormatsReadWriteOrder()
    {
        var s = UsbBenchmarkUiMessages.BuildUiSummary(UsbBenchmarkResultKind.Completed, 100.2, 40.5);
        Assert.Contains("100.2", s);
        Assert.Contains("40.5", s);
        Assert.Contains("Read", s);
        Assert.Contains("Write", s);
    }

    [Fact]
    public void NativeOperationCanceled_IsNotMappedToUserCancellationWithoutTokenContext()
    {
        var kind = UsbBenchmarkUiMessages.MapNativeEndKind(UsbNativeBenchmarkEndKind.OperationCanceled, succeeded: false);
        Assert.NotEqual(UsbBenchmarkResultKind.CancelledByUser, kind);
    }

    [Fact]
    public async Task MainViewModel_BenchmarkStartUsesFreshNonCancelledToken()
    {
        var service = new CapturingBenchmarkService();
        var gate = new TaskCompletionSource<UsbBenchmarkResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Enqueue(_ => gate.Task);
        using var vm = BuildBenchmarkViewModel(service, BenchmarkTarget());

        vm.RunUsbIntelligenceBenchmarkCommand.Execute(null);
        await WaitForStartedAsync(service, 1);

        Assert.False(service.Tokens[0].IsCancellationRequested);
        gate.SetResult(CompletedResult());
    }

    [Fact]
    public async Task MainViewModel_PreviouslyCancelledBenchmarkTokenDoesNotPoisonNextRun()
    {
        var service = new CapturingBenchmarkService();
        var first = new TaskCompletionSource<UsbBenchmarkResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<UsbBenchmarkResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Enqueue(_ => first.Task);
        service.Enqueue(_ => second.Task);
        using var vm = BuildBenchmarkViewModel(service, BenchmarkTarget());

        vm.RunUsbIntelligenceBenchmarkCommand.Execute(null);
        await WaitForStartedAsync(service, 1);
        vm.CancelUsbIntelligenceBenchmarkCommand.Execute(null);
        Assert.True(service.Tokens[0].IsCancellationRequested);
        first.SetResult(CancelledResult());
        Assert.True(SpinWait.SpinUntil(() => vm.RunUsbIntelligenceBenchmarkCommand.CanExecute(null), TimeSpan.FromSeconds(5)));

        vm.RunUsbIntelligenceBenchmarkCommand.Execute(null);
        await WaitForStartedAsync(service, 2);

        Assert.False(service.Tokens[1].IsCancellationRequested);
        second.SetResult(CompletedResult());
    }

    [Fact]
    public async Task MainViewModel_NonUserOperationCanceledIsNotLoggedAsUserCancellation()
    {
        var service = new CapturingBenchmarkService();
        service.Enqueue(_ => throw new OperationCanceledException("host cancellation without benchmark token"));
        using var vm = BuildBenchmarkViewModel(service, BenchmarkTarget());

        vm.RunUsbIntelligenceBenchmarkCommand.Execute(null);
        await WaitForStartedAsync(service, 1);
        await Task.Delay(150);

        Assert.DoesNotContain(
            vm.Logs.Select(line => line.Text),
            line => line.Contains("Benchmark cancelled by user", StringComparison.OrdinalIgnoreCase));
    }
}
