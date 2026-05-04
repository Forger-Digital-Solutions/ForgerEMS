using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using VentoyToolkitSetup.Wpf.ViewModels;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class UsbMappingWizardAndResolutionTests
{
    private sealed class StubUsbIntelligence : IUsbIntelligenceService
    {
        public UsbTopologySnapshot BuildTopologySnapshot(UsbTargetInfo? selectedTarget, UsbTopologyBuildOptions? options = null) =>
            new()
            {
                GeneratedUtc = DateTimeOffset.UtcNow,
                CombinedConfidenceScore = 50,
                CombinedConfidenceReason = "stub",
                SelectedTargetRecommendation = new UsbBuilderRecommendation
                {
                    ClassificationLine = "Quality: Good",
                    Summary = "ok",
                    Detail = "",
                    Risk = UsbPortRiskLevel.Low,
                    Speed = UsbSpeedClassification.Usb3,
                    Quality = UsbBuilderQuality.Good,
                    ConfidenceScore = 50,
                    ConfidenceReason = "stub"
                }
            };

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

    /// <summary>Always returns the same single-device topology so port-change resolution fails.</summary>
    private sealed class IdenticalTopologyUsbIntelligence : IUsbIntelligenceService
    {
        public UsbTopologySnapshot BuildTopologySnapshot(UsbTargetInfo? selectedTarget, UsbTopologyBuildOptions? options = null) =>
            new()
            {
                GeneratedUtc = DateTimeOffset.UtcNow,
                CombinedConfidenceScore = 40,
                CombinedConfidenceReason = "identical-stub",
                Devices =
                [
                    new UsbDeviceInfo
                    {
                        FriendlyName = "USB Disk",
                        DriveLetter = "E:",
                        InferredSpeed = UsbSpeedClassification.Usb2,
                        StableDeviceKey = "dev-1",
                        StablePortKey = "port-same",
                        ControllerKey = "c1",
                        HubKey = "h0",
                        IsRemovableMassStorage = true
                    }
                ],
                Controllers = [],
                Ports = [],
                SummaryLine = "",
                SelectedTargetRecommendation = new UsbBuilderRecommendation
                {
                    ClassificationLine = "Quality: OK",
                    Summary = "ok",
                    Detail = "",
                    Risk = UsbPortRiskLevel.Low,
                    Speed = UsbSpeedClassification.Usb2,
                    Quality = UsbBuilderQuality.Good,
                    ConfidenceScore = 40,
                    ConfidenceReason = "stub"
                }
            };

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

    private sealed class SlowTopologyUsbIntelligence : IUsbIntelligenceService
    {
        private readonly StubUsbIntelligence _inner = new();
        private readonly int _delayMs;

        public SlowTopologyUsbIntelligence(int delayMs) => _delayMs = delayMs;

        public UsbTopologySnapshot BuildTopologySnapshot(UsbTargetInfo? selectedTarget, UsbTopologyBuildOptions? options = null)
        {
            Thread.Sleep(_delayMs);
            return _inner.BuildTopologySnapshot(selectedTarget, options);
        }

        public Task WriteLatestReportAsync(string reportsDirectory, UsbTopologySnapshot snapshot) =>
            _inner.WriteLatestReportAsync(reportsDirectory, snapshot);

        public UsbBuilderPreflightResult GetVentoyPreflight(UsbTargetInfo? selectedTarget, UsbTopologySnapshot? snapshot) =>
            _inner.GetVentoyPreflight(selectedTarget, snapshot);
    }

    /// <summary>Alternates port key on each snapshot build so before/after capture yields a topology change.</summary>
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

    private static UsbTargetInfo MakeRemovable(string letter, string label, bool isEfiSystemPartition = false) =>
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
            IsEfiSystemPartition = isEfiSystemPartition,
            IsUndersizedPartition = false
        };

    [Fact]
    public void UsbMappingWizardDeviceFilter_ExcludesEfiAndVtoyefi()
    {
        var ok = MakeRemovable("E:", "Ventoy");
        Assert.True(UsbMappingWizardDeviceFilter.IsEligibleMappingUsb(ok));

        var efi = MakeRemovable("F:", "BOOT", isEfiSystemPartition: true);
        Assert.False(UsbMappingWizardDeviceFilter.IsEligibleMappingUsb(efi));

        var vtoy = MakeRemovable("G:", "VTOYEFI");
        Assert.False(UsbMappingWizardDeviceFilter.IsEligibleMappingUsb(vtoy));
    }

    [Fact]
    public void UsbMappingWizard_StartsOnWelcomeStep()
    {
        var intel = new StubUsbIntelligence();
        var root = Path.Combine(Path.GetTempPath(), $"fe-wiz-{Guid.NewGuid():N}");
        try
        {
            var store = new UsbMachineProfileStore(root);
            var vm = new UsbMappingWizardViewModel(
                intel,
                store,
                () => [MakeRemovable("E:", "Data")]);
            Assert.True(vm.IsWelcomeStep);
            Assert.False(vm.ContinueSelectDeviceCommand.CanExecute(null));
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void UsbMappingWizard_ContinueDisabledUntilDeviceSelected()
    {
        var intel = new StubUsbIntelligence();
        var root = Path.Combine(Path.GetTempPath(), $"fe-wiz2-{Guid.NewGuid():N}");
        try
        {
            var store = new UsbMachineProfileStore(root);
            var vm = new UsbMappingWizardViewModel(intel, store, () => [MakeRemovable("E:", "Data")]);
            vm.StartMappingCommand.Execute(null);
            Assert.False(vm.ContinueSelectDeviceCommand.CanExecute(null));
            vm.SelectedDevice = vm.DeviceOptions[0];
            Assert.True(vm.ContinueSelectDeviceCommand.CanExecute(null));
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task UsbMappingWizard_DetectChange_Failure_AlwaysExposesStatusAndFailurePanel()
    {
        var intel = new IdenticalTopologyUsbIntelligence();
        var root = Path.Combine(Path.GetTempPath(), $"fe-wiz-fail-{Guid.NewGuid():N}");
        try
        {
            var store = new UsbMachineProfileStore(root);
            var vm = new UsbMappingWizardViewModel(intel, store, () => [MakeRemovable("E:", "Data")]);
            vm.StartMappingCommand.Execute(null);
            vm.SelectedDevice = vm.DeviceOptions[0];
            vm.ContinueSelectDeviceCommand.Execute(null);
            vm.CaptureCurrentPortCommand.Execute(null);
            vm.NextAfterCaptureCommand.Execute(null);
            vm.UserConfirmedUsbMoved = true;
            await vm.DetectPortChangeAsync();
            Assert.True(vm.IsDetectStep);
            Assert.False(vm.DetectionSuccess);
            Assert.False(string.IsNullOrWhiteSpace(vm.FailureMessage));
            Assert.False(string.IsNullOrWhiteSpace(vm.DetectChangePrimaryStatus));
            Assert.True(vm.ShowDetectFailureDetails);
            Assert.False(vm.ShowDetectSuccessDetails);
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task UsbMappingWizard_DetectChange_Timeout_ShowsFallbackMessage()
    {
        var intel = new SlowTopologyUsbIntelligence(delayMs: 500);
        var root = Path.Combine(Path.GetTempPath(), $"fe-wiz-to-{Guid.NewGuid():N}");
        try
        {
            var store = new UsbMachineProfileStore(root);
            var vm = new UsbMappingWizardViewModel(
                intel,
                store,
                () => [MakeRemovable("E:", "Data")],
                detectOperationTimeoutOverride: TimeSpan.FromMilliseconds(120));
            vm.StartMappingCommand.Execute(null);
            vm.SelectedDevice = vm.DeviceOptions[0];
            vm.ContinueSelectDeviceCommand.Execute(null);
            vm.CaptureCurrentPortCommand.Execute(null);
            vm.NextAfterCaptureCommand.Execute(null);
            vm.UserConfirmedUsbMoved = true;
            await vm.DetectPortChangeAsync();
            Assert.Contains("longer than expected", vm.FailureMessage, StringComparison.OrdinalIgnoreCase);
            Assert.True(vm.ShowDetectFailureDetails);
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task UsbMappingWizard_DetectChange_Success_ShowsSuccessPanel()
    {
        var intel = new AlternatingPortIntelligence();
        var root = Path.Combine(Path.GetTempPath(), $"fe-wiz-ok-{Guid.NewGuid():N}");
        try
        {
            var store = new UsbMachineProfileStore(root);
            var vm = new UsbMappingWizardViewModel(intel, store, () => [MakeRemovable("E:", "Data")]);
            vm.StartMappingCommand.Execute(null);
            vm.SelectedDevice = vm.DeviceOptions[0];
            vm.ContinueSelectDeviceCommand.Execute(null);
            vm.CaptureCurrentPortCommand.Execute(null);
            vm.NextAfterCaptureCommand.Execute(null);
            vm.UserConfirmedUsbMoved = true;
            await vm.DetectPortChangeAsync();
            Assert.True(vm.DetectionSuccess);
            Assert.True(vm.ShowDetectSuccessDetails);
            Assert.Contains("Port change", vm.DetectChangePrimaryStatus, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void UsbMappingWizard_DetectDisabledUntilMovedConfirmed()
    {
        var intel = new StubUsbIntelligence();
        var root = Path.Combine(Path.GetTempPath(), $"fe-wiz3-{Guid.NewGuid():N}");
        try
        {
            var store = new UsbMachineProfileStore(root);
            var vm = new UsbMappingWizardViewModel(intel, store, () => [MakeRemovable("E:", "Data")]);
            vm.StartMappingCommand.Execute(null);
            vm.SelectedDevice = vm.DeviceOptions[0];
            vm.ContinueSelectDeviceCommand.Execute(null);
            vm.CaptureCurrentPortCommand.Execute(null);
            vm.NextAfterCaptureCommand.Execute(null);
            Assert.False(vm.DetectPortChangeCommand.CanExecute(null));
            vm.UserConfirmedUsbMoved = true;
            Assert.True(vm.DetectPortChangeCommand.CanExecute(null));
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void UsbMappingPortResolution_ReEnumeratedVolume_MatchesWhenCorrelationKeyDrifts()
    {
        var before = new UsbTopologySnapshot
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            Devices =
            [
                new UsbDeviceInfo
                {
                    FriendlyName = "USB Disk (MyLabel)",
                    DriveLetter = null,
                    InferredSpeed = UsbSpeedClassification.Usb2,
                    StableDeviceKey = "dev-old",
                    StablePortKey = "port-a",
                    ControllerKey = "c1",
                    HubKey = "h0",
                    VolumeIdentityHash = "volhash-1",
                    IsRemovableMassStorage = true
                }
            ],
            Controllers = [],
            Ports = [],
            SummaryLine = ""
        };
        var after = new UsbTopologySnapshot
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            Devices =
            [
                new UsbDeviceInfo
                {
                    FriendlyName = "USB Disk (MyLabel)",
                    DriveLetter = "E:",
                    InferredSpeed = UsbSpeedClassification.Usb3,
                    StableDeviceKey = "dev-new",
                    StablePortKey = "port-b",
                    ControllerKey = "c2",
                    HubKey = "h0",
                    VolumeIdentityHash = "volhash-1",
                    IsRemovableMassStorage = true
                }
            ],
            Controllers = [],
            Ports = [],
            SummaryLine = ""
        };
        var target = MakeRemovable("E:", "MyLabel");
        var res = UsbMappingPortResolution.Resolve(before, after, target);
        Assert.True(res.Success);
        Assert.Equal(UsbPortMappingMatchKind.ReEnumeratedSameVolume, res.MatchKind);
    }

    [Fact]
    public void UsbMappingPortResolution_FallbackSameDriveLetter_WhenCorrelationKeysDiffer()
    {
        var before = new UsbTopologySnapshot
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            Devices =
            [
                new UsbDeviceInfo
                {
                    FriendlyName = "Vendor USB (MyToolkit)",
                    DriveLetter = "E:",
                    InferredSpeed = UsbSpeedClassification.Usb2,
                    StableDeviceKey = "k1",
                    StablePortKey = "port-a",
                    ControllerKey = "c1",
                    IsRemovableMassStorage = true
                }
            ],
            Controllers = [],
            Ports = [],
            SummaryLine = ""
        };
        var after = new UsbTopologySnapshot
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            Devices =
            [
                new UsbDeviceInfo
                {
                    FriendlyName = "Vendor USB (MyToolkit)",
                    DriveLetter = "E:",
                    InferredSpeed = UsbSpeedClassification.Usb3,
                    StableDeviceKey = "k2",
                    StablePortKey = "port-b",
                    ControllerKey = "c2",
                    IsRemovableMassStorage = true
                }
            ],
            Controllers = [],
            Ports = [],
            SummaryLine = ""
        };
        var target = MakeRemovable("E:", "MyToolkit");
        var res = UsbMappingPortResolution.Resolve(before, after, target);
        Assert.True(res.Success);
        Assert.Equal(UsbPortMappingMatchKind.SameDriveLetterPortChange, res.MatchKind);
        Assert.True(res.UsedLimitedConfidenceFallback);
    }

    [Fact]
    public void UsbAutomaticBenchmarkPolicy_BlocksSecondAutoStartWithin30Seconds()
    {
        var p = new UsbAutomaticBenchmarkPolicy();
        var now = DateTimeOffset.UtcNow;
        Assert.True(p.TryRegisterAutomaticStart("E:\\", now));
        Assert.False(p.TryRegisterAutomaticStart("E:\\", now.AddSeconds(5)));
        Assert.True(p.TryRegisterAutomaticStart("E:\\", now.AddSeconds(31)));
    }

    [Fact]
    public void UsbGuidedMappingWorkflow_ManualCurrentPortMode_SavesWithoutPortDelta()
    {
        var root = Path.Combine(Path.GetTempPath(), $"fe-man-{Guid.NewGuid():N}");
        try
        {
            var store = new UsbMachineProfileStore(root);
            var profile = store.LoadOrCreate();
            var wf = new UsbGuidedMappingWorkflow();
            wf.StartMappingSession();
            var before = new UsbTopologySnapshot
            {
                GeneratedUtc = DateTimeOffset.UtcNow,
                Devices =
                [
                    new UsbDeviceInfo
                    {
                        FriendlyName = "Disk",
                        DriveLetter = "E:",
                        StableDeviceKey = "k1",
                        StablePortKey = "p-same",
                        ControllerKey = "c1",
                        IsRemovableMassStorage = true
                    }
                ],
                Controllers = [],
                Ports = [],
                SummaryLine = ""
            };
            var after = new UsbTopologySnapshot
            {
                GeneratedUtc = DateTimeOffset.UtcNow,
                Devices =
                [
                    new UsbDeviceInfo
                    {
                        FriendlyName = "Disk",
                        DriveLetter = "E:",
                        StableDeviceKey = "k1",
                        StablePortKey = "p-same",
                        ControllerKey = "c1",
                        IsRemovableMassStorage = true
                    }
                ],
                Controllers = [],
                Ports = [],
                SummaryLine = ""
            };
            wf.CaptureBeforeSnapshot(before);
            wf.CaptureAfterSnapshot(after);
            var target = MakeRemovable("E:", "Data");
            var ok = wf.TrySaveMappingLabel(
                profile,
                store,
                "Left front",
                out var inf,
                out var err,
                target,
                UsbPortMappingSaveMode.CurrentPortForSelectedTarget);
            Assert.True(ok, err);
            Assert.Contains("Manual", inf.SuggestionLine, StringComparison.OrdinalIgnoreCase);
            var rec = Assert.Single(profile.KnownPorts);
            Assert.Equal("p-same", rec.StablePortKey);
            Assert.Equal("Left front", rec.UserLabel);
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
            }
        }
    }
}
