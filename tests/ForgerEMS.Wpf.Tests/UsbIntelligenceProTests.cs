using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class UsbIntelligenceProTests
{
    [Fact]
    public void UsbSnapshotJson_DoesNotLeakRawPnpOrWmiIds()
    {
        var dev = new UsbDeviceInfo
        {
            FriendlyName = "TestDisk",
            DriveLetter = "E:",
            IsRemovableMassStorage = true,
            InferredSpeed = UsbSpeedClassification.Usb3,
            PnpDeviceId = @"USBSTOR\Disk&Ven_EVIL&Prod_DEVICE\7&123456&0&000000000000001",
            WmiDeviceId = @"\\.\PHYSICALDRIVE9",
            StableDeviceKey = "abc",
            DeviceInstanceIdHash = "h1",
            StablePortKey = "p1",
            ControllerKey = "c1"
        };

        var snap = new UsbTopologySnapshot
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            Devices = [dev],
            Controllers = [],
            Ports = [],
            SummaryLine = "test"
        };

        var json = JsonSerializer.Serialize(snap, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        Assert.DoesNotContain("USBSTOR", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PHYSICALDRIVE", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("000000000000001", json);
    }

    [Fact]
    public void UsbTopologyDiffService_Detects_Added_Removed_And_Changes()
    {
        var prev = new UsbTopologySnapshot
        {
            GeneratedUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            Devices =
            [
                new UsbDeviceInfo
                {
                    FriendlyName = "StickA",
                    DriveLetter = "E:",
                    IsRemovableMassStorage = true,
                    InferredSpeed = UsbSpeedClassification.Unknown,
                    StableDeviceKey = "k1",
                    StablePortKey = "port-a",
                    ControllerKey = "ctl-a",
                    DeviceInstanceIdHash = "d1",
                    LocationPathHash = ""
                }
            ],
            Controllers = [],
            Ports = [],
            SummaryLine = ""
        };

        var curr = new UsbTopologySnapshot
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            Devices =
            [
                new UsbDeviceInfo
                {
                    FriendlyName = "StickA",
                    DriveLetter = "E:",
                    IsRemovableMassStorage = true,
                    InferredSpeed = UsbSpeedClassification.Usb3,
                    StableDeviceKey = "k1",
                    StablePortKey = "port-b",
                    ControllerKey = "ctl-b",
                    DeviceInstanceIdHash = "d1",
                    LocationPathHash = ""
                },
                new UsbDeviceInfo
                {
                    FriendlyName = "StickB",
                    DriveLetter = "F:",
                    IsRemovableMassStorage = true,
                    InferredSpeed = UsbSpeedClassification.Usb2,
                    StableDeviceKey = "k2",
                    StablePortKey = "p2",
                    ControllerKey = "ctl-a",
                    DeviceInstanceIdHash = "d2",
                    LocationPathHash = ""
                }
            ],
            Controllers = [],
            Ports = [],
            SummaryLine = ""
        };

        var diff = UsbTopologyDiffService.Compare(prev, curr);
        Assert.Single(diff.AddedDevices);
        Assert.Contains("StickB", diff.AddedDevices[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SpeedClarified", diff.ChangedDevices.Select(c => c.ChangeKind));
        Assert.Contains("LikelyPortMove", diff.ChangedDevices.Select(c => c.ChangeKind));
        Assert.Contains("ControllerPathChanged", diff.ChangedDevices.Select(c => c.ChangeKind));
        Assert.False(string.IsNullOrWhiteSpace(diff.SummaryLine));
    }

    [Fact]
    public void UsbBuilderRecommendationEngine_ClassifiesIdealAndSlow()
    {
        var controllers = new[]
        {
            new UsbControllerInfo
            {
                Name = "USB 3",
                ControllerKey = "c1",
                InferredSpeed = UsbSpeedClassification.Usb3,
                SpeedRationale = ""
            }
        };

        var dev = new UsbDeviceInfo
        {
            FriendlyName = "Fast",
            DriveLetter = "E:",
            InferredSpeed = UsbSpeedClassification.UsbC,
            IsRemovableMassStorage = true,
            StablePortKey = "stable",
            ControllerKey = "c1"
        };

        var target = new UsbTargetInfo { DriveLetter = "E:", RootPath = "E:\\" };
        var ideal = UsbBuilderRecommendationEngine.Build(target, dev, controllers.ToList(), null, null, null, null);
        Assert.Equal(UsbBuilderQuality.Ideal, ideal.Quality);

        var devSlow = new UsbDeviceInfo
        {
            FriendlyName = "Fast",
            DriveLetter = "E:",
            InferredSpeed = UsbSpeedClassification.Usb2,
            IsRemovableMassStorage = true,
            StablePortKey = "stable",
            ControllerKey = "c1"
        };
        var slow = UsbBuilderRecommendationEngine.Build(target, devSlow, controllers.ToList(), null, null, null, null);
        Assert.Equal(UsbBuilderQuality.Slow, slow.Quality);
    }

    [Fact]
    public void UsbMachineProfileStore_RoundTrips()
    {
        var root = Path.Combine(Path.GetTempPath(), $"fe-usb-prof-{Guid.NewGuid():N}");
        try
        {
            var store = new UsbMachineProfileStore(root);
            var p = store.LoadOrCreate();
            p.KnownControllerKeys.Add("ctl-hash");
            p.KnownStablePortKeys.Add("port-hash");
            store.Save(p);

            var p2 = store.LoadOrCreate();
            Assert.Contains("ctl-hash", p2.KnownControllerKeys);
            Assert.Contains("port-hash", p2.KnownStablePortKeys);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void UsbMappingSessionService_InfersSuggestion()
    {
        var svc = new UsbMappingSessionService();
        var session = svc.StartSession();
        var before = new UsbTopologySnapshot
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            Devices =
            [
                new UsbDeviceInfo
                {
                    FriendlyName = "One",
                    DriveLetter = "E:",
                    InferredSpeed = UsbSpeedClassification.Usb2,
                    StableDeviceKey = "a",
                    StablePortKey = "p1",
                    ControllerKey = "c1",
                    DeviceInstanceIdHash = "h1",
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
                    FriendlyName = "One",
                    DriveLetter = "E:",
                    InferredSpeed = UsbSpeedClassification.Usb3,
                    StableDeviceKey = "a",
                    StablePortKey = "p2",
                    ControllerKey = "c2",
                    DeviceInstanceIdHash = "h1",
                    IsRemovableMassStorage = true
                }
            ],
            Controllers = [],
            Ports = [],
            SummaryLine = ""
        };

        svc.RecordBefore(session, before);
        svc.RecordAfter(session, after);
        var inf = svc.InferMappingChange(session);
        Assert.True(inf.Success);
        Assert.False(string.IsNullOrWhiteSpace(inf.SuggestionLine));
    }

    [Fact]
    public void KyraSafeContextBuilder_DoesNotEmitRawUsbSerialTokens()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"kyra-usb-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                tmp,
                """
                {
                  "summaryLine": "USB ok",
                  "selectedTargetRecommendation": {
                    "summary": "OK",
                    "detail": "detail",
                    "quality": "good",
                    "classificationLine": "Quality: Good",
                    "risk": "Low",
                    "speed": "usb3"
                  },
                  "topologyDiff": {
                    "summaryLine": "SECRET123456789012345678901234567890ABCDEF token",
                    "recommendationLine": "replug"
                  }
                }
                """);

            var text = KyraSafeContextBuilder.BuildBriefSummary(null, tmp, null, null, enableRedaction: true);
            Assert.DoesNotContain("SECRET123456789012345678901234567890ABCDEF", text);
        }
        finally
        {
            try
            {
                File.Delete(tmp);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void DiagnosticsUsbSeverity_UsesIssueList()
    {
        var issues = new UsbDiagnosticIssue[]
        {
            new()
            {
                Severity = DiagnosticSeverityLevel.Warning,
                Message = "slow"
            },
            new()
            {
                Severity = DiagnosticSeverityLevel.Ok,
                Message = "ok"
            }
        };

        var overall = issues.Any(i => i.Severity == DiagnosticSeverityLevel.Warning)
            ? DiagnosticSeverityLevel.Warning
            : DiagnosticSeverityLevel.Ok;
        Assert.Equal(DiagnosticSeverityLevel.Warning, overall);
    }

    [Theory]
    [InlineData(20, 21, null, UsbSpeedMeasurementClass.Usb2)]
    [InlineData(45, 50, UsbSpeedClassification.Usb3, UsbSpeedMeasurementClass.Usb3)]
    [InlineData(150, 155, UsbSpeedClassification.UsbC, UsbSpeedMeasurementClass.UsbC)]
    [InlineData(5, 95, null, UsbSpeedMeasurementClass.Bottleneck)]
    public void UsbMeasurementClassifier_ClassifiesSpeedBands(
        double write,
        double read,
        UsbSpeedClassification? wmi,
        UsbSpeedMeasurementClass expected)
    {
        var (cls, _, _) = UsbMeasurementClassifier.Classify(write, read, wmi);
        Assert.Equal(expected, cls);
    }

    [Fact]
    public void UsbMeasurementClassifier_UnknownForInvalidSample()
    {
        var (cls, score, _) = UsbMeasurementClassifier.Classify(0, 40, null);
        Assert.Equal(UsbSpeedMeasurementClass.Unknown, cls);
        Assert.True(score < 50);
    }

    [Fact]
    public void UsbConfidenceAggregator_CombinesBenchmarkAndUserLabel()
    {
        var diff = new UsbTopologyDiffResult
        {
            DiffConfidenceScore = 60,
            DiffConfidenceReason = "Compared snapshots."
        };
        var bench = new UsbIntelligenceBenchmarkResult
        {
            Succeeded = true,
            WriteSpeedMBps = 40,
            ReadSpeedMBps = 42,
            DurationMs = 1000,
            Classification = UsbSpeedMeasurementClass.Usb3,
            ConfidenceScore = 72,
            Timestamp = DateTimeOffset.UtcNow,
            SummaryLine = "ok",
            DetailReason = "ok"
        };
        var port = new UsbKnownPortRecord { UserLabel = "rear-blue", MappingConfidenceScore = 50 };

        var (score, reason) = UsbConfidenceAggregator.Combine(40, diff, bench, port);
        Assert.True(score >= 60);
        Assert.Contains("benchmark", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("label", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UsbConfidenceAggregator_FallsBackWhenNoSignals()
    {
        var (score, reason) = UsbConfidenceAggregator.Combine(0, null, null, null);
        Assert.True(score is > 30 and < 45);
        Assert.Contains("Limited signals", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UsbGuidedMappingWorkflow_PersistsLabelToKnownPorts()
    {
        var root = Path.Combine(Path.GetTempPath(), $"fe-usb-map-{Guid.NewGuid():N}");
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
                        FriendlyName = "MapMe",
                        DriveLetter = "E:",
                        InferredSpeed = UsbSpeedClassification.Usb2,
                        StableDeviceKey = "dev-x",
                        StablePortKey = "port-old",
                        ControllerKey = "c1",
                        DeviceInstanceIdHash = "h1",
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
                        FriendlyName = "MapMe",
                        DriveLetter = "E:",
                        InferredSpeed = UsbSpeedClassification.Usb3,
                        StableDeviceKey = "dev-x",
                        StablePortKey = "port-new",
                        ControllerKey = "c2",
                        DeviceInstanceIdHash = "h1",
                        IsRemovableMassStorage = true
                    }
                ],
                Controllers = [],
                Ports = [],
                SummaryLine = ""
            };

            wf.CaptureBeforeSnapshot(before);
            wf.CaptureAfterSnapshot(after);

            var ok = wf.TrySaveMappingLabel(profile, store, "front-left USB-A", out var inf, out var err);
            Assert.True(ok, err);
            Assert.True(inf.Success);
            var rec = Assert.Single(profile.KnownPorts);
            Assert.Equal("port-new", rec.StablePortKey);
            Assert.Equal("front-left USB-A", rec.UserLabel);
            Assert.True(rec.MappingConfidenceScore > 0);

            var profile2 = store.LoadOrCreate();
            var rec2 = profile2.KnownPorts.FirstOrDefault(p => p.StablePortKey == "port-new");
            Assert.NotNull(rec2);
            Assert.Equal("front-left USB-A", rec2!.UserLabel);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void UsbSnapshotJson_WithBenchmark_DoesNotLeakRawPnpOrWmiIds()
    {
        var dev = new UsbDeviceInfo
        {
            FriendlyName = "Disk",
            DriveLetter = "E:",
            IsRemovableMassStorage = true,
            InferredSpeed = UsbSpeedClassification.Usb3,
            PnpDeviceId = @"USBSTOR\Disk&Ven_X&Prod_Y\8&ABCDEF&0&SENSITIVE_SERIAL",
            WmiDeviceId = @"\\.\PHYSICALDRIVE2",
            StableDeviceKey = "k",
            DeviceInstanceIdHash = "h1",
            StablePortKey = "p1",
            ControllerKey = "c1"
        };
        var bench = new UsbIntelligenceBenchmarkResult
        {
            Succeeded = true,
            WriteSpeedMBps = 40,
            ReadSpeedMBps = 41,
            DurationMs = 900,
            Classification = UsbSpeedMeasurementClass.Usb3,
            ConfidenceScore = 70,
            Timestamp = DateTimeOffset.UtcNow,
            SummaryLine = "Measured speeds OK.",
            DetailReason = "Throughput typical."
        };
        var snap = new UsbTopologySnapshot
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            Devices = [dev],
            Controllers = [],
            Ports = [],
            SummaryLine = "ok",
            SelectedTargetBenchmark = bench
        };

        var json = JsonSerializer.Serialize(snap, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        Assert.DoesNotContain("USBSTOR", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PHYSICALDRIVE", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SENSITIVE_SERIAL", json, StringComparison.OrdinalIgnoreCase);
    }
}
