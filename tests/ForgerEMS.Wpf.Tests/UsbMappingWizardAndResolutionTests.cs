using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using VentoyToolkitSetup.Wpf.ViewModels;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

[Collection(UsbPortLabelResolverSerialFixture.Name)]
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

    private sealed class RetrySensitivePortIntelligence : IUsbIntelligenceService
    {
        private int _call;

        public bool ReturnChangedPort { get; set; }

        public UsbTopologySnapshot BuildTopologySnapshot(UsbTargetInfo? selectedTarget, UsbTopologyBuildOptions? options = null)
        {
            var call = Interlocked.Increment(ref _call);
            var port = ReturnChangedPort && call >= 3 ? "port-b" : "port-a";
            var location = ReturnChangedPort && call >= 3 ? "loc-b" : "loc-a";
            return new UsbTopologySnapshot
            {
                GeneratedUtc = DateTimeOffset.UtcNow,
                CombinedConfidenceScore = 60,
                CombinedConfidenceReason = "retry",
                Devices =
                [
                    new UsbDeviceInfo
                    {
                        FriendlyName = "USB Disk",
                        DriveLetter = "E:",
                        InferredSpeed = UsbSpeedClassification.Usb3,
                        StableDeviceKey = "dev-1",
                        StablePortKey = port,
                        LocationPathHash = location,
                        ControllerKey = "c1",
                        HubKey = "h0",
                        VolumeIdentityHash = "vol-fixed",
                        IsRemovableMassStorage = true
                    }
                ],
                Controllers = [],
                Ports = [],
                SummaryLine = ""
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

    private sealed class MountedSequence
    {
        private readonly bool[] _states;
        private int _index = -1;

        public MountedSequence(params bool[] states) => _states = states.Length == 0 ? [true] : states;

        public bool IsMounted(string? _)
        {
            var i = Interlocked.Increment(ref _index);
            return _states[Math.Min(i, _states.Length - 1)];
        }
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
            var target = MakeRemovable("E:", "Data");
            var targets = new RemovalThenReinsertTargets(target);
            var vm = new UsbMappingWizardViewModel(
                intel,
                store,
                targets.GetTargets,
                detectOperationTimeoutOverride: TimeSpan.FromSeconds(5),
                isDriveRootMounted: _ => targets.IsMounted);
            vm.StartMappingCommand.Execute(null);
            vm.SelectedDevice = vm.DeviceOptions[0];
            vm.ContinueSelectDeviceCommand.Execute(null);
            vm.CaptureCurrentPortCommand.Execute(null);
            vm.NextAfterCaptureCommand.Execute(null);
            targets.StartDetectPass();
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
    public async Task UsbMappingWizard_RemovalNotObserved_ShowsWaitingRemovalCopy()
    {
        var intel = new IdenticalTopologyUsbIntelligence();
        var root = Path.Combine(Path.GetTempPath(), $"fe-wiz-to-{Guid.NewGuid():N}");
        try
        {
            var store = new UsbMachineProfileStore(root);
            var target = MakeRemovable("E:", "Data");
            var vm = new UsbMappingWizardViewModel(
                intel,
                store,
                () => [target],
                detectOperationTimeoutOverride: TimeSpan.FromMilliseconds(120),
                isDriveRootMounted: _ => true);
            vm.StartMappingCommand.Execute(null);
            vm.SelectedDevice = vm.DeviceOptions[0];
            vm.ContinueSelectDeviceCommand.Execute(null);
            vm.CaptureCurrentPortCommand.Execute(null);
            vm.NextAfterCaptureCommand.Execute(null);
            await vm.DetectPortChangeAsync();
            Assert.Equal(UsbMappingWizardDetectionPhase.WaitingForRemoval, vm.DetectionPhase);
            Assert.Contains("Waiting for USB Removal", vm.FailureMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("physical port path", vm.FailureMessage, StringComparison.OrdinalIgnoreCase);
            Assert.True(vm.ShowDetectFailureDetails);
            Assert.True(vm.ShowRemovalNotObservedActions);
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
            var target = MakeRemovable("E:", "Data");
            var targets = new RemovalThenReinsertTargets(target);
            var vm = new UsbMappingWizardViewModel(
                intel,
                store,
                targets.GetTargets,
                detectOperationTimeoutOverride: TimeSpan.FromSeconds(5),
                isDriveRootMounted: _ => targets.IsMounted);
            vm.StartMappingCommand.Execute(null);
            vm.SelectedDevice = vm.DeviceOptions[0];
            vm.ContinueSelectDeviceCommand.Execute(null);
            vm.CaptureCurrentPortCommand.Execute(null);
            vm.NextAfterCaptureCommand.Execute(null);
            targets.StartDetectPass();
            await vm.DetectPortChangeAsync();
            Assert.True(vm.DetectionSuccess);
            Assert.True(vm.ShowDetectSuccessDetails);
            Assert.Contains("confidence", vm.DetectChangePrimaryStatus, StringComparison.OrdinalIgnoreCase);
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
    public void UsbMappingWizard_DetectEnabledAfterCurrentPortCaptured()
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
            Assert.False(vm.DetectPortChangeCommand.CanExecute(null));
            vm.CaptureCurrentPortCommand.Execute(null);
            vm.NextAfterCaptureCommand.Execute(null);
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
    public void UsbMappingPortResolution_SameDeviceCountChangedLocationPath_MapsWithLowConfidence()
    {
        var before = new UsbTopologySnapshot
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            Devices =
            [
                new UsbDeviceInfo
                {
                    FriendlyName = "Vendor USB (Ventoy)",
                    DriveLetter = "E:",
                    InferredSpeed = UsbSpeedClassification.Usb3,
                    StableDeviceKey = "same-device",
                    StablePortKey = "same-port-key",
                    LocationPathHash = "loc-a",
                    VolumeIdentityHash = "vol-1",
                    IsRemovableMassStorage = true
                }
            ]
        };
        var after = new UsbTopologySnapshot
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            Devices =
            [
                new UsbDeviceInfo
                {
                    FriendlyName = "Vendor USB (Ventoy)",
                    DriveLetter = "E:",
                    InferredSpeed = UsbSpeedClassification.Usb3,
                    StableDeviceKey = "same-device",
                    StablePortKey = "same-port-key",
                    LocationPathHash = "loc-b",
                    VolumeIdentityHash = "vol-1",
                    IsRemovableMassStorage = true
                }
            ]
        };

        var res = UsbMappingPortResolution.Resolve(before, after, MakeRemovable("E:", "Ventoy"));

        Assert.True(res.Success);
        Assert.Equal(UsbPortMappingMatchKind.WeakTopologyEvidencePortChange, res.MatchKind);
        Assert.Equal("Medium", res.ConfidenceTier);
        Assert.Contains("location-path-changed", res.ReasonCodes);
    }

    [Fact]
    public void UsbMappingPortResolution_SameDeviceCountChangedControllerHub_MapsWithMediumConfidence()
    {
        var before = new UsbTopologySnapshot
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            Devices =
            [
                new UsbDeviceInfo
                {
                    FriendlyName = "Vendor USB (Ventoy)",
                    DriveLetter = "E:",
                    StableDeviceKey = "same-device",
                    StablePortKey = "same-port-key",
                    ControllerKey = "controller-a",
                    HubKey = "hub-a",
                    VolumeIdentityHash = "vol-1",
                    IsRemovableMassStorage = true
                }
            ]
        };
        var after = new UsbTopologySnapshot
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            Devices =
            [
                new UsbDeviceInfo
                {
                    FriendlyName = "Vendor USB (Ventoy)",
                    DriveLetter = "E:",
                    StableDeviceKey = "same-device",
                    StablePortKey = "same-port-key",
                    ControllerKey = "controller-b",
                    HubKey = "hub-b",
                    VolumeIdentityHash = "vol-1",
                    IsRemovableMassStorage = true
                }
            ]
        };

        var res = UsbMappingPortResolution.Resolve(before, after, MakeRemovable("E:", "Ventoy"));

        Assert.True(res.Success);
        Assert.Equal(UsbPortMappingMatchKind.WeakTopologyEvidencePortChange, res.MatchKind);
        Assert.Equal("Medium", res.ConfidenceTier);
        Assert.Contains("controller-changed", res.ReasonCodes);
        Assert.Contains("hub-parent-changed", res.ReasonCodes);
    }

    [Fact]
    public void UsbMappingPortResolution_SameDriveIdentityWithoutTopology_ReturnsManualLabelRecommended()
    {
        var before = new UsbTopologySnapshot
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            Devices =
            [
                new UsbDeviceInfo
                {
                    FriendlyName = "USB Disk",
                    DriveLetter = "E:",
                    VolumeLabel = "Ventoy",
                    VolumeIdentityHash = "vol-1",
                    IsRemovableMassStorage = true
                }
            ]
        };
        var after = new UsbTopologySnapshot
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            Devices =
            [
                new UsbDeviceInfo
                {
                    FriendlyName = "USB Disk",
                    DriveLetter = "E:",
                    VolumeLabel = "Ventoy",
                    VolumeIdentityHash = "vol-1",
                    IsRemovableMassStorage = true
                }
            ]
        };

        var res = UsbMappingPortResolution.Resolve(before, after, MakeRemovable("E:", "Ventoy"));

        Assert.False(res.Success);
        Assert.True(res.ManualLabelRecommended);
        Assert.Equal(UsbPortMappingMatchKind.ManualLabelRecommended, res.MatchKind);
        Assert.Contains("same-device-identity-matched", res.ReasonCodes);
    }

    [Fact]
    public async Task UsbMappingWizard_SameIdentityWeakTopologyFallsBackToManualLabel()
    {
        var intel = new IdenticalTopologyUsbIntelligence();
        var root = Path.Combine(Path.GetTempPath(), $"fe-wiz-weak-{Guid.NewGuid():N}");
        try
        {
            var store = new UsbMachineProfileStore(root);
            var target = MakeRemovable("E:", "Data");
            var targets = new RemovalThenReinsertTargets(target);
            var vm = new UsbMappingWizardViewModel(
                intel,
                store,
                targets.GetTargets,
                detectOperationTimeoutOverride: TimeSpan.FromSeconds(5),
                isDriveRootMounted: _ => targets.IsMounted);
            vm.StartMappingCommand.Execute(null);
            vm.SelectedDevice = vm.DeviceOptions[0];
            vm.ContinueSelectDeviceCommand.Execute(null);
            vm.CaptureCurrentPortCommand.Execute(null);
            vm.NextAfterCaptureCommand.Execute(null);
            targets.StartDetectPass();
            await vm.DetectPortChangeAsync();

            Assert.False(vm.DetectionSuccess);
            Assert.Equal(UsbMappingWizardDetectionPhase.ManualLabelRecommended, vm.DetectionPhase);
            Assert.Contains("Manual Label", vm.FailureMessage, StringComparison.OrdinalIgnoreCase);
            Assert.True(vm.UseCurrentPortAnywayCommand.CanExecute(null));
            Assert.True(vm.SaveManualLabelPathCommand.CanExecute(null));
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
    public async Task UsbMappingWizard_RemovalObservationExpiresPreviousManualCurrentLabel()
    {
        UsbPortLabelResolver.ResetSessionStateForTests();
        var intel = new IdenticalTopologyUsbIntelligence();
        var root = Path.Combine(Path.GetTempPath(), $"fe-wiz-expire-{Guid.NewGuid():N}");
        try
        {
            var store = new UsbMachineProfileStore(root);
            var profile = store.LoadOrCreate();
            var existingDevice = new UsbDeviceInfo
            {
                FriendlyName = "USB Disk",
                DriveLetter = "E:",
                InferredSpeed = UsbSpeedClassification.Usb2,
                StableDeviceKey = "dev-1",
                StablePortKey = "port-same",
                ControllerKey = "c1",
                HubKey = "h0",
                IsRemovableMassStorage = true
            };
            var right = new UsbKnownPortRecord();
            UsbPortLabelResolver.StampManualLabel(right, existingDevice, "RT USB-C", 55, DateTimeOffset.UtcNow);
            profile.KnownPorts.Add(right);
            store.Save(profile);

            var target = MakeRemovable("E:", "Data");
            var targets = new RemovalThenReinsertTargets(target);
            var vm = new UsbMappingWizardViewModel(
                intel,
                store,
                targets.GetTargets,
                detectOperationTimeoutOverride: TimeSpan.FromSeconds(5),
                isDriveRootMounted: _ => targets.IsMounted);
            vm.StartMappingCommand.Execute(null);
            vm.SelectedDevice = vm.DeviceOptions[0];
            vm.ContinueSelectDeviceCommand.Execute(null);
            vm.CaptureCurrentPortCommand.Execute(null);
            vm.NextAfterCaptureCommand.Execute(null);
            targets.StartDetectPass();

            await vm.DetectPortChangeAsync();

            Assert.False(vm.DetectionSuccess);
            var reloaded = store.LoadOrCreate();
            var status = UsbPortLabelResolver.Resolve(existingDevice, reloaded);
            Assert.NotEqual(UsbPortLabelValidity.CurrentSessionManual, status.Validity);
            Assert.Null(status.CurrentLabel);
            Assert.Equal("RT USB-C", status.LastKnownLabel);
            Assert.Contains("Unverified", status.StatusLine, StringComparison.OrdinalIgnoreCase);
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
    public async Task UsbMappingWizard_ConfirmSavedLabelBindsThatLabelForCurrentEpoch()
    {
        UsbPortLabelResolver.ResetSessionStateForTests();
        var intel = new IdenticalTopologyUsbIntelligence();
        var root = Path.Combine(Path.GetTempPath(), $"fe-wiz-confirm-{Guid.NewGuid():N}");
        try
        {
            var store = new UsbMachineProfileStore(root);
            var profile = store.LoadOrCreate();
            var weakDevice = new UsbDeviceInfo
            {
                FriendlyName = "USB Disk",
                DriveLetter = "E:",
                InferredSpeed = UsbSpeedClassification.Usb2,
                StableDeviceKey = "dev-1",
                StablePortKey = "port-same",
                ControllerKey = "c1",
                HubKey = "h0",
                IsRemovableMassStorage = true
            };
            var left = new UsbKnownPortRecord();
            var right = new UsbKnownPortRecord();
            UsbPortLabelResolver.StampManualLabel(left, weakDevice, "LT USB-C", 55, DateTimeOffset.UtcNow.AddMinutes(-3));
            UsbPortLabelResolver.MarkDriveRemoved("E:");
            UsbPortLabelResolver.StampManualLabel(right, weakDevice, "RT USB-C", 55, DateTimeOffset.UtcNow.AddMinutes(-2));
            profile.KnownPorts.Add(left);
            profile.KnownPorts.Add(right);
            store.Save(profile);

            var target = MakeRemovable("E:", "Data");
            var targets = new RemovalThenReinsertTargets(target);
            var vm = new UsbMappingWizardViewModel(
                intel,
                store,
                targets.GetTargets,
                detectOperationTimeoutOverride: TimeSpan.FromSeconds(5),
                isDriveRootMounted: _ => targets.IsMounted);
            vm.StartMappingCommand.Execute(null);
            Assert.True(vm.HasSavedPortLabels);
            Assert.Contains(vm.SavedPortLabels, p => p.Label == "LT USB-C");
            Assert.Contains(vm.SavedPortLabels, p => p.Label == "RT USB-C");
            vm.SelectedDevice = vm.DeviceOptions[0];
            vm.ContinueSelectDeviceCommand.Execute(null);
            vm.CaptureCurrentPortCommand.Execute(null);
            vm.NextAfterCaptureCommand.Execute(null);
            targets.StartDetectPass();
            await vm.DetectPortChangeAsync();

            Assert.False(vm.DetectionSuccess);
            var leftOption = vm.SavedPortLabels.Single(p => p.Label == "LT USB-C");
            Assert.True(vm.ConfirmSavedPortLabelCommand.CanExecute(leftOption.MappingId));
            vm.ConfirmSavedPortLabelCommand.Execute(leftOption.MappingId);

            Assert.True(vm.IsDoneStep);
            Assert.Equal("LT USB-C", vm.DoneResult?.Label);
            var reloaded = store.LoadOrCreate();
            Assert.Equal(2, reloaded.KnownPorts.Count);
            var status = UsbPortLabelResolver.Resolve(weakDevice, reloaded);
            Assert.Equal(UsbPortLabelValidity.CurrentSessionManual, status.Validity);
            Assert.Equal("LT USB-C", status.CurrentLabel);
            Assert.Contains(reloaded.KnownPorts, p => p.UserLabel == "RT USB-C");
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
    public void UsbMappingWizard_DeleteSavedLabelRemovesOnlyThatPort()
    {
        UsbPortLabelResolver.ResetSessionStateForTests();
        var root = Path.Combine(Path.GetTempPath(), $"fe-wiz-delete-label-{Guid.NewGuid():N}");
        try
        {
            var store = new UsbMachineProfileStore(root);
            var profile = store.LoadOrCreate();
            var device = new UsbDeviceInfo
            {
                FriendlyName = "USB Disk",
                DriveLetter = "E:",
                StableDeviceKey = "dev-1",
                StablePortKey = "port-same",
                IsRemovableMassStorage = true
            };
            var left = new UsbKnownPortRecord();
            var right = new UsbKnownPortRecord();
            UsbPortLabelResolver.StampManualLabel(left, device, "LT USB C", 55, DateTimeOffset.UtcNow.AddMinutes(-5));
            UsbPortLabelResolver.StampManualLabel(right, device, "RT USB-C", 55, DateTimeOffset.UtcNow);
            profile.KnownPorts.Add(left);
            profile.KnownPorts.Add(right);
            store.Save(profile);

            var vm = new UsbMappingWizardViewModel(
                new IdenticalTopologyUsbIntelligence(),
                store,
                () => [MakeRemovable("E:", "Data")]);
            vm.StartMappingCommand.Execute(null);
            var rightOption = vm.SavedPortLabels.Single(p => p.Label == "RT USB-C");

            vm.DeleteSavedPortLabelCommand.Execute(rightOption.MappingId);

            var reloaded = store.LoadOrCreate();
            Assert.Single(reloaded.KnownPorts);
            Assert.Contains(reloaded.KnownPorts, p => p.UserLabel == "LT USB-C");
            Assert.DoesNotContain(reloaded.KnownPorts, p => p.UserLabel == "RT USB-C");
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
    public void UsbMappingWizard_RenameSavedLabelUpdatesDisplayAndNormalizedKey()
    {
        UsbPortLabelResolver.ResetSessionStateForTests();
        var root = Path.Combine(Path.GetTempPath(), $"fe-wiz-rename-label-{Guid.NewGuid():N}");
        try
        {
            var store = new UsbMachineProfileStore(root);
            var profile = store.LoadOrCreate();
            var device = new UsbDeviceInfo
            {
                FriendlyName = "USB Disk",
                DriveLetter = "E:",
                StableDeviceKey = "dev-1",
                StablePortKey = "port-same",
                IsRemovableMassStorage = true
            };
            var rec = new UsbKnownPortRecord();
            UsbPortLabelResolver.StampManualLabel(rec, device, "LT USB C", 55, DateTimeOffset.UtcNow);
            profile.KnownPorts.Add(rec);
            store.Save(profile);

            var vm = new UsbMappingWizardViewModel(
                new IdenticalTopologyUsbIntelligence(),
                store,
                () => [MakeRemovable("E:", "Data")]);
            vm.StartMappingCommand.Execute(null);
            var option = vm.SavedPortLabels.Single(p => p.Label == "LT USB-C");
            vm.PortLabelDraft = "left usb a";

            vm.RenameSavedPortLabelCommand.Execute(option.MappingId);

            var reloaded = store.LoadOrCreate();
            var renamed = Assert.Single(reloaded.KnownPorts);
            Assert.Equal("Left USB-A", renamed.UserLabel);
            Assert.Equal(UsbPortLabelNormalizer.NormalizeKey("Left USB-A"), renamed.NormalizedLabelKey);
            Assert.Contains(vm.SavedPortLabels, p => p.Label == "Left USB-A");
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
    public async Task UsbMappingWizard_TryAgainClearsStaleSnapshotsAndStartsFreshDetection()
    {
        var intel = new RetrySensitivePortIntelligence();
        var root = Path.Combine(Path.GetTempPath(), $"fe-wiz-retry-{Guid.NewGuid():N}");
        try
        {
            var target = MakeRemovable("E:", "Data");
            var targets = new RemovalThenReinsertTargets(target);
            var store = new UsbMachineProfileStore(root);
            var vm = new UsbMappingWizardViewModel(
                intel,
                store,
                targets.GetTargets,
                detectOperationTimeoutOverride: TimeSpan.FromSeconds(5),
                isDriveRootMounted: _ => targets.IsMounted);
            vm.StartMappingCommand.Execute(null);
            vm.SelectedDevice = vm.DeviceOptions[0];
            vm.ContinueSelectDeviceCommand.Execute(null);
            vm.CaptureCurrentPortCommand.Execute(null);
            vm.NextAfterCaptureCommand.Execute(null);
            targets.StartDetectPass();
            await vm.DetectPortChangeAsync();

            Assert.False(vm.DetectionSuccess);
            Assert.Contains("manual", vm.ConfidenceTierDisplay, StringComparison.OrdinalIgnoreCase);

            intel.ReturnChangedPort = true;
            targets.StartDetectPass();
            await vm.TryDetectionAgainAsync();
            Assert.True(vm.IsDetectStep);
            Assert.True(vm.DetectionSuccess);
            Assert.Contains("confidence", vm.DetectChangePrimaryStatus, StringComparison.OrdinalIgnoreCase);
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
    public async Task UsbMappingWizard_PollingFallbackDetectsRemovalAndReinsertWhenTargetListIsStale()
    {
        var intel = new AlternatingPortIntelligence();
        var root = Path.Combine(Path.GetTempPath(), $"fe-wiz-poll-{Guid.NewGuid():N}");
        try
        {
            var target = MakeRemovable("E:", "Data");
            var mounted = new MountedSequence(true, false, false, true, true);
            var vm = new UsbMappingWizardViewModel(
                intel,
                new UsbMachineProfileStore(root),
                () => [target],
                detectOperationTimeoutOverride: TimeSpan.FromSeconds(5),
                isDriveRootMounted: mounted.IsMounted);
            vm.StartMappingCommand.Execute(null);
            vm.SelectedDevice = vm.DeviceOptions[0];
            vm.ContinueSelectDeviceCommand.Execute(null);
            vm.CaptureCurrentPortCommand.Execute(null);
            vm.NextAfterCaptureCommand.Execute(null);

            await vm.DetectPortChangeAsync();

            Assert.True(vm.DetectionSuccess);
            Assert.Equal(UsbMappingWizardDetectionPhase.Mapped, vm.DetectionPhase);
            Assert.Contains("confidence", vm.DetectChangePrimaryStatus, StringComparison.OrdinalIgnoreCase);
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
    public async Task UsbMappingWizard_DebugDetailsHiddenOutsideDiagnosticsMode()
    {
        var original = Environment.GetEnvironmentVariable("FORGEREMS_USB_MAPPING_DEBUG_UI");
        var originalEnv = Environment.GetEnvironmentVariable("FORGEREMS_ENV");
        Environment.SetEnvironmentVariable("FORGEREMS_USB_MAPPING_DEBUG_UI", null);
        Environment.SetEnvironmentVariable("FORGEREMS_ENV", "Production");
        var root = Path.Combine(Path.GetTempPath(), $"fe-wiz-debug-{Guid.NewGuid():N}");
        try
        {
            var target = MakeRemovable("E:", "Data");
            var targets = new RemovalThenReinsertTargets(target);
            var vm = new UsbMappingWizardViewModel(
                new IdenticalTopologyUsbIntelligence(),
                new UsbMachineProfileStore(root),
                targets.GetTargets,
                detectOperationTimeoutOverride: TimeSpan.FromSeconds(5),
                isDriveRootMounted: _ => targets.IsMounted);
            vm.StartMappingCommand.Execute(null);
            vm.SelectedDevice = vm.DeviceOptions[0];
            vm.ContinueSelectDeviceCommand.Execute(null);
            vm.CaptureCurrentPortCommand.Execute(null);
            vm.NextAfterCaptureCommand.Execute(null);
            targets.StartDetectPass();
            await vm.DetectPortChangeAsync();

            Assert.True(vm.IsDetectStep);
            Assert.False(vm.ShowDetectChangeDebugDetails);
            Assert.False(string.IsNullOrWhiteSpace(vm.DetectChangeDebugSummary));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FORGEREMS_USB_MAPPING_DEBUG_UI", original);
            Environment.SetEnvironmentVariable("FORGEREMS_ENV", originalEnv);
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
    public void UsbMappingWizardWindow_CopyDoesNotReferencePreviousStep()
    {
        var xaml = File.ReadAllText(FindRepoFile("src/ForgerEMS.Wpf/UsbMappingWizardWindow.xaml"));
        Assert.DoesNotContain("previous step", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Start detection first", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Confirm Current Port", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ConfirmSavedPortLabelCommand", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<WrapPanel", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("I moved the USB device", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UsbIntelligenceService_RestrictedOrMissingPnpEvidence_DoesNotCrashSnapshot()
    {
        var svc = new UsbIntelligenceService();
        var snapshot = svc.BuildTopologySnapshot(MakeRemovable("Z:", "NoSuchUsb"));
        Assert.NotNull(snapshot);
        Assert.NotNull(snapshot.Devices);
    }

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find repo file {relativePath}");
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
            Assert.Equal("Left Front", rec.UserLabel);
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
