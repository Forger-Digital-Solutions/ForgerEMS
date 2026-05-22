using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.DriveValidation;
using VentoyToolkitSetup.Wpf.ViewModels;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class DriveValidationTests
{
    private static UsbTargetInfo RemovableTarget(string root, long freeBytes = 2L * 1024 * 1024 * 1024) =>
        new()
        {
            DriveLetter = "E:",
            RootPath = root,
            Label = "TestUSB",
            FileSystem = "exFAT",
            TotalBytes = 64L * 1024 * 1024 * 1024,
            FreeBytes = freeBytes,
            DriveType = "Removable",
            BusType = "USB",
            IsLikelyUsb = true,
            IsRemovableMedia = true,
            IsSelectable = true,
            IsLargeDataPartition = true
        };

    [Fact]
    public void TargetSafety_BlocksCdrive()
    {
        var c = RemovableTarget("C:\\");
        c = new UsbTargetInfo
        {
            RootPath = "C:\\",
            Label = "OS",
            IsRemovableMedia = false,
            IsSystemDrive = true,
            IsBootDrive = true
        };

        Assert.False(DriveValidationTargetSafety.IsSafeToStart(c, new DriveValidationOptions(), out var reason));
        Assert.Contains("Windows OS", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TargetSafety_BlocksSystemDriveByDefault()
    {
        var t = new UsbTargetInfo
        {
            RootPath = "D:\\",
            Label = "System",
            IsSystemDrive = true,
            IsBootDrive = true,
            IsRemovableMedia = false,
            IsLargeDataPartition = false
        };

        Assert.False(DriveValidationTargetSafety.IsSafeToStart(t, new DriveValidationOptions(), out _));
    }

    [Fact]
    public void TargetSafety_AllowsRemovableTarget()
    {
        var t = RemovableTarget("E:\\");
        Assert.True(DriveValidationTargetSafety.IsSafeToStart(t, new DriveValidationOptions(), out var reason));
        Assert.True(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void TargetSafety_DestructiveRequiresConfirmationPhrase()
    {
        var t = RemovableTarget("E:\\");
        var options = new DriveValidationOptions
        {
            Mode = DriveValidationMode.DestructiveFullMediaValidation,
            DestructiveConfirmationText = "wrong"
        };

        Assert.False(DriveValidationTargetSafety.IsSafeToStart(t, options, out var reason));
        Assert.Contains("confirmation", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Planner_QuickModePlansBoundedSamples()
    {
        var plan = DriveValidationPlanner.Plan(RemovableTarget("E:\\"), new DriveValidationOptions
        {
            Mode = DriveValidationMode.QuickSafeCheck
        });

        Assert.Null(plan.BlockReason);
        Assert.InRange(plan.Samples.Count, 2, 5);
        Assert.True(plan.ReservedBytes > 0);
    }

    [Fact]
    public void Planner_SampledModeSpreadsSamples()
    {
        var plan = DriveValidationPlanner.Plan(RemovableTarget("E:\\"), new DriveValidationOptions
        {
            Mode = DriveValidationMode.SampledCapacityCheck
        });

        Assert.True(plan.Samples.Count >= 5);
        var names = plan.Samples.Select(s => s.RelativePath).Distinct().ToList();
        Assert.Equal(plan.Samples.Count, names.Count);
    }

    [Fact]
    public void Planner_FullModeRefusesInsufficientSpace()
    {
        var plan = DriveValidationPlanner.Plan(
            RemovableTarget("E:\\", freeBytes: 40L * 1024 * 1024),
            new DriveValidationOptions { Mode = DriveValidationMode.FullFreeSpaceValidation });

        Assert.NotNull(plan.BlockReason);
    }

    [Fact]
    public void Planner_SignaturesAreDistinctPerSample()
    {
        var plan = DriveValidationPlanner.Plan(RemovableTarget("E:\\"), new DriveValidationOptions
        {
            Mode = DriveValidationMode.SampledCapacityCheck
        });

        Assert.True(DriveValidationPlanner.HasDistinctSignatures(plan.Samples));
    }

    [Fact]
    public void Signature_DetectsAliasedHeaders()
    {
        var a = DriveValidationSignature.BuildBlock(0, 0, 101, 512);
        var b = (byte[])a.Clone();
        Assert.True(DriveValidationSignature.BlocksAppearAliased(a, b));
        var c = DriveValidationSignature.BuildBlock(5, 0, 707, 512);
        Assert.False(DriveValidationSignature.BlocksAppearAliased(a, c));
    }

    [Fact]
    public async Task TempFileManager_CreatesOnlyUnderValidatorFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "forgerems-dv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var mgr = new DriveValidationTempFileManager();
            mgr.EnsureTempRoot(root);
            var sample = new DriveValidationSample
            {
                Index = 0,
                RelativePath = "sample-000.bin",
                ByteLength = 4096,
                Seed = 11
            };
            var path = mgr.GetSamplePath(sample);
            await File.WriteAllBytesAsync(path, new byte[4096]);
            mgr.Track(path);

            Assert.StartsWith(Path.Combine(root, DriveValidationTargetSafety.TempFolderName), path, StringComparison.OrdinalIgnoreCase);
            var cleanup = mgr.Cleanup();
            Assert.Empty(cleanup.LeftoverPaths);
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Service_PassedWhenSamplesVerify()
    {
        var root = Path.Combine(Path.GetTempPath(), "forgerems-dv-pass-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var target = RemovableTarget(root + "\\");
            var svc = new DriveValidationService();
            var result = await svc.RunAsync(
                target,
                new DriveValidationOptions { Mode = DriveValidationMode.QuickSafeCheck, BlockSizeBytes = 64 * 1024 });

            Assert.True(
                result.Status is DriveValidationStatus.Passed or DriveValidationStatus.PassedWithWarnings,
                result.Summary);
            Assert.True(result.Evidence.SamplesVerified > 0);
            Assert.False(Directory.Exists(Path.Combine(root, DriveValidationTargetSafety.TempFolderName)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Service_CancelledPreservesState()
    {
        var root = Path.Combine(Path.GetTempPath(), "forgerems-dv-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var target = RemovableTarget(root + "\\");
            var svc = new DriveValidationService();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var result = await svc.RunAsync(
                target,
                new DriveValidationOptions { Mode = DriveValidationMode.QuickSafeCheck },
                cancellationToken: cts.Token);

            Assert.Equal(DriveValidationStatus.Cancelled, result.Status);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ResultClassification_FailedForMismatchScenario()
    {
        var result = new DriveValidationResult
        {
            Status = DriveValidationStatus.Failed,
            Summary = "Suspicious capacity behavior detected."
        };

        Assert.True(result.ShouldWarnUsbBuilder);
        Assert.False(result.IsSuccessfulForUsbBuilder);
    }

    private sealed class StubDriveValidationService(DriveValidationResult result) : IDriveValidationService
    {
        public Task<DriveValidationResult> RunAsync(
            UsbTargetInfo target,
            DriveValidationOptions options,
            string? portPathHint = null,
            Action<DriveValidationProgress>? onProgress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    [Fact]
    public void ViewModel_StartDisabledWithoutTarget()
    {
        var vm = new MainViewModel(
            new BackendDiscoveryService(),
            new PowerShellRunnerService(),
            new StaticUsbDetectionService(RemovableTarget("E:\\")),
            new ManagedDownloadSummaryService(),
            new ScriptStatusParser(),
            new AcceptingPromptService(),
            new VentoyIntegrationService(new PowerShellRunnerService(), new AppRuntimeService()),
            new AppRuntimeService(),
            new UsbBenchmarkService(new PowerShellRunnerService()),
            new CopilotService(new CopilotProviderRegistry()),
            new CopilotProviderRegistry(),
            driveValidationService: new StubDriveValidationService(
                DriveValidationResult.Blocked(DriveValidationStatus.UnsafeTargetBlocked, "blocked", "detail")));

        Assert.False(vm.RunDriveValidatorCommand.CanExecute(null));
    }

    [Fact]
    public void ViewModel_FailedValidationShowsBuilderWarning()
    {
        var target = RemovableTarget("E:\\");
        var failed = new DriveValidationResult
        {
            Status = DriveValidationStatus.Failed,
            Summary = "Failed verification — do not trust this drive.",
            TargetRootPath = target.RootPath,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };

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
            driveValidationService: new StubDriveValidationService(failed));

        vm.UsbTargets.Add(target);
        vm.SelectedUsbTarget = target;
        vm.RunDriveValidatorCommand.Execute(null);

        Assert.Contains("failed validation", vm.DriveValidatorBuilderWarningText, StringComparison.OrdinalIgnoreCase);
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
