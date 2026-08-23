using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using ForgerEMS.Wpf.Services;
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

    private sealed class StubPowerShellRunnerService(PowerShellRunResult result) : IPowerShellRunnerService
    {
        public int RunCount { get; private set; }

        public Task<PowerShellRunResult> RunAsync(
            PowerShellRunRequest request,
            Action<LogLine>? onOutput = null,
            CancellationToken cancellationToken = default)
        {
            RunCount++;
            return Task.FromResult(result);
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
            new ManagedDownloadResolverService(new HttpClient()),
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

        public int? PickOption(string title, string message, IReadOnlyList<string> options) => options.Count > 0 ? 0 : null;
    }

    private static UsbTargetInfo BenchmarkTarget(string deviceModel = "") =>
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

        var cachedRead = new UsbBenchmarkResult
        {
            Succeeded = true,
            Status = "Complete",
            WriteSpeedMBps = 58.8,
            ReadSpeedMBps = 3851.1,
            ReadLikelyCached = true,
            ResultKind = UsbBenchmarkResultKind.Completed
        };
        Assert.False(cachedRead.ShouldPersistSuccessfulHistory);
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
    public void UiMessages_CachedReadWarningDoesNotCertifyReadSpeed()
    {
        var s = UsbBenchmarkUiMessages.BuildUiSummary(
            UsbBenchmarkResultKind.Completed,
            4536.4,
            58.2,
            "Read may be cached",
            readMayBeCached: true);

        Assert.Contains("Write 58.2 MB/s verified", s, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Read speed not verified", s, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cache suspected", s, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BenchmarkAccuracy_FlagsImpossibleCachedReadSpeed()
    {
        var target = BenchmarkTarget("USB Flash Drive");

        var assessment = UsbBenchmarkAccuracy.Assess(
            writeMbps: 58.2,
            readMbps: 4536.4,
            UsbSpeedClassification.Usb3,
            target);

        Assert.True(assessment.ReadLikelyCached);
        Assert.True(assessment.ReadIsEstimate);
        Assert.Contains("plausible", assessment.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(59.0, 1486.0)]
    [InlineData(57.0, 1895.0)]
    [InlineData(56.0, 2457.0)]
    public void BenchmarkAccuracy_ImpossibleReadSpeedsStayCacheSuspect(double writeMbps, double readMbps)
    {
        var target = BenchmarkTarget("USB Flash Drive");
        var assessment = UsbBenchmarkAccuracy.Assess(writeMbps, readMbps, UsbSpeedClassification.Usb3, target);
        var summary = UsbBenchmarkUiMessages.BuildUiSummary(
            UsbBenchmarkResultKind.Completed,
            readMbps,
            writeMbps,
            assessment.ConfidenceLabel,
            readMayBeCached: assessment.ReadLikelyCached || assessment.ReadIsEstimate);

        Assert.True(assessment.ReadLikelyCached);
        Assert.Contains("Read speed not verified", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BenchmarkAccuracy_DoesNotFlagPlausibleUsbSsdReadSpeed()
    {
        var target = BenchmarkTarget("Portable SSD");

        var assessment = UsbBenchmarkAccuracy.Assess(
            writeMbps: 720,
            readMbps: 980,
            UsbSpeedClassification.UsbC,
            target);

        Assert.False(assessment.ReadLikelyCached);
        Assert.Equal("Measured", assessment.ConfidenceLabel);
    }

    [Fact]
    public void RecommendationEngine_CachedReadDoesNotProduceIdeal()
    {
        var recommendation = UsbBuilderRecommendationEngine.Build(
            selectedTarget: BenchmarkTarget(),
            matchedDevice: null,
            controllers: [],
            diff: null,
            profile: null,
            benchmark: new UsbIntelligenceBenchmarkResult
            {
                Succeeded = true,
                WriteSpeedMBps = 59.7,
                ReadSpeedMBps = 4121.6,
                Classification = UsbSpeedMeasurementClass.Usb3,
                ConfidenceScore = 85,
                ReadLikelyCached = true,
                ReadIsEstimate = true,
                SummaryLine = "Benchmark complete with warning"
            },
            portRecord: null);

        Assert.Equal(UsbBuilderQuality.Good, recommendation.Quality);
        Assert.DoesNotContain("Ideal", recommendation.ClassificationLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Read speed not verified", recommendation.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecommendationEngine_MeasuredReadWriteCanStillBeIdeal()
    {
        var recommendation = UsbBuilderRecommendationEngine.Build(
            selectedTarget: BenchmarkTarget(),
            matchedDevice: null,
            controllers: [],
            diff: null,
            profile: null,
            benchmark: new UsbIntelligenceBenchmarkResult
            {
                Succeeded = true,
                WriteSpeedMBps = 58.8,
                ReadSpeedMBps = 265.4,
                Classification = UsbSpeedMeasurementClass.Usb3,
                ConfidenceScore = 80,
                ReadLikelyCached = false,
                ReadIsEstimate = false,
                SummaryLine = "Measured"
            },
            portRecord: null);

        Assert.NotEqual(UsbBuilderQuality.Unknown, recommendation.Quality);
        Assert.DoesNotContain("cache suspected", recommendation.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BenchmarkAccuracy_SelectsLargerSamplesWhenFreeSpaceAllows()
    {
        Assert.Equal(64, UsbBenchmarkAccuracy.SelectTestSizeMb(300L * 1024 * 1024));
        Assert.Equal(128, UsbBenchmarkAccuracy.SelectTestSizeMb(1L * 1024 * 1024 * 1024));
        Assert.Equal(512, UsbBenchmarkAccuracy.SelectTestSizeMb(4L * 1024 * 1024 * 1024));
        Assert.Equal(1024, UsbBenchmarkAccuracy.SelectTestSizeMb(16L * 1024 * 1024 * 1024));
    }

    [Fact]
    public void ProfileSync_PreservesCachedReadConfidence()
    {
        var result = new UsbBenchmarkResult
        {
            Succeeded = true,
            Status = "Complete",
            WriteSpeedMBps = 58.2,
            ReadSpeedMBps = 4536.4,
            TestSizeMb = 1024,
            IntelligenceMeasurementClass = UsbSpeedMeasurementClass.Usb3.ToString(),
            IntelligenceConfidenceScore = 45,
            ReadLikelyCached = true,
            ReadIsEstimate = true,
            BenchmarkConfidence = "Read may be cached",
            AccuracyWarning = "Read sample exceeded plausible USB limit."
        };

        var profileResult = UsbBenchmarkProfileSync.FromServiceResult(result);

        Assert.NotNull(profileResult);
        Assert.True(profileResult!.ReadLikelyCached);
        Assert.True(profileResult.ReadIsEstimate);
        Assert.Equal("Read may be cached", profileResult.BenchmarkConfidence);
        Assert.True(profileResult.ConfidenceScore <= 45);
    }

    [Fact]
    public void BenchmarkUiMessages_UsesContextAwareCancellationCopy()
    {
        Assert.Contains(
            "another USB action started",
            UsbBenchmarkUiMessages.BuildUiSummary(UsbBenchmarkResultKind.CancelledByUsbAction, 0, 0),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "settling",
            UsbBenchmarkUiMessages.BuildUiSummary(UsbBenchmarkResultKind.SkippedTargetSettling, 0, 0),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "application closed",
            UsbBenchmarkUiMessages.BuildUiSummary(UsbBenchmarkResultKind.CancelledByHost, 0, 0),
            StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public async Task UsbBenchmarkService_MissingDriveRootSkipsNativeAndPowerShellFallback()
    {
        var root = Path.Combine(Path.GetTempPath(), "fe-missing-usb-" + Guid.NewGuid().ToString("N")) + Path.DirectorySeparatorChar;
        var target = new UsbTargetInfo
        {
            DriveLetter = root,
            RootPath = root,
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
        var runner = new StubPowerShellRunnerService(new PowerShellRunResult
        {
            ExitCode = 1,
            StandardErrorText = "A device which does not exist was specified."
        });
        var service = new UsbBenchmarkService(runner);
        var logs = new List<string>();

        var result = await service.RunSequentialBenchmarkAsync(
            target,
            line => logs.Add(line.Text));

        Assert.Equal(0, runner.RunCount);
        Assert.False(result.Succeeded);
        Assert.Equal(UsbBenchmarkResultKind.DeviceRemoved, result.ResultKind);
        Assert.Contains("USB target is no longer available", result.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(logs, line => line.Contains("USB target unavailable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void KyraNarrative_CacheSuspectedRead_ConfidenceIsMediumNotHigh()
    {
        var snapshot = new UsbTopologySnapshot
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            Devices = [],
            Controllers = [],
            Ports = [],
            SummaryLine = "test",
            CombinedConfidenceScore = 85,
            SelectedTargetBenchmark = new UsbIntelligenceBenchmarkResult
            {
                Succeeded = true,
                WriteSpeedMBps = 59.9,
                ReadSpeedMBps = 4666.0,
                Classification = UsbSpeedMeasurementClass.Usb3,
                ConfidenceScore = 85,
                ReadLikelyCached = true,
                ReadIsEstimate = true,
                SummaryLine = "Good write speed; read unverified."
            }
        };

        var narrative = UsbKyraNarrativeBuilder.Build(snapshot);

        Assert.DoesNotContain("confidence is high", narrative.ShortAnswer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("read is unverified", narrative.ShortAnswer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cache suspected", narrative.ShortAnswer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProfileSync_CacheSuspectedRead_PopulatesVerifiedWriteAndNullVerifiedRead()
    {
        // Simulate a result as UsbBenchmarkService produces it: VerifiedWriteMbps set,
        // VerifiedReadMbps null, RawReadMbps = the impossible cached value.
        var result = new UsbBenchmarkResult
        {
            Succeeded = true,
            Status = "Complete",
            WriteSpeedMBps = 59.9,
            ReadSpeedMBps = 4666.0,
            VerifiedWriteMbps = 59.9,
            VerifiedReadMbps = null,
            RawReadMbps = 4666.0,
            IsReadCacheSuspected = true,
            ReadVerificationStatus = "Unverified / cache suspected",
            TestSizeMb = 512,
            IntelligenceMeasurementClass = UsbSpeedMeasurementClass.Usb3.ToString(),
            IntelligenceConfidenceScore = 60,
            ReadLikelyCached = true,
            ReadIsEstimate = true,
            BenchmarkConfidence = "Read may be cached"
        };

        var profileResult = UsbBenchmarkProfileSync.FromServiceResult(result);

        Assert.NotNull(profileResult);
        Assert.True(profileResult!.IsReadCacheSuspected);
        Assert.True(profileResult.VerifiedWriteMbps > 0);
        Assert.Null(profileResult.VerifiedReadMbps);
        Assert.Equal(4666.0, profileResult.RawReadMbps);
        Assert.Equal("Unverified / cache suspected", profileResult.ReadVerificationStatus);
    }
}
