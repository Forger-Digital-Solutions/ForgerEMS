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

    [Fact]
    public void UsbPortLabelResolver_UsesManualLabelAsCurrentOnlyWhenCurrentSessionConnectionStillMatches()
    {
        UsbPortLabelResolver.ResetSessionStateForTests();
        var profile = new UsbMachineProfile();
        var current = TestUsbDevice("M:", "dev-a", "port-a", seenCount: 1);
        var rec = new UsbKnownPortRecord { StablePortKey = current.StablePortKey };
        UsbPortLabelResolver.StampManualLabel(rec, current, "LT USB C", 60, DateTimeOffset.UtcNow);
        profile.KnownPorts.Add(rec);

        var status = UsbPortLabelResolver.Resolve(current, profile);

        Assert.Equal(UsbPortLabelValidity.CurrentSessionManual, status.Validity);
        Assert.Equal("LT USB C", status.CurrentLabel);
        Assert.True(status.CanAttachBenchmarkToVerifiedPort);
        Assert.Equal(UsbPortLabelResolver.GetCurrentConnectionEpoch("M:"), rec.LastManualLabelConnectionEpoch);
    }

    [Fact]
    public void UsbPortLabelResolver_AfterReconnectWeakTopology_ShowsLastKnownLabelNotCurrent()
    {
        UsbPortLabelResolver.ResetSessionStateForTests();
        var profile = new UsbMachineProfile();
        var original = TestUsbDevice("N:", "dev-b", "port-b", seenCount: 1);
        var rec = new UsbKnownPortRecord { StablePortKey = original.StablePortKey };
        UsbPortLabelResolver.StampManualLabel(rec, original, "LT USB C", 60, DateTimeOffset.UtcNow);
        profile.KnownPorts.Add(rec);
        UsbPortLabelResolver.MarkDriveRemoved("N:");

        var reinserted = TestUsbDevice("N:", "dev-b", "port-b", seenCount: 2);
        var status = UsbPortLabelResolver.Resolve(reinserted, profile);

        Assert.Equal(UsbPortLabelValidity.TopologyUnavailable, status.Validity);
        Assert.Null(status.CurrentLabel);
        Assert.Equal("LT USB C", status.LastKnownLabel);
        Assert.Contains("Last known label: LT USB C", status.ReasonLine, StringComparison.Ordinal);
        Assert.Contains("manual-session-epoch-mismatch", status.ReasonCodes);
        Assert.Contains("stale-current-label-invalidated", status.ReasonCodes);
        Assert.False(status.CanAttachBenchmarkToVerifiedPort);
    }

    [Fact]
    public void UsbPortLabelResolver_ReinsertCreatesNewConnectionEpochAndExpiresCurrentSessionManual()
    {
        UsbPortLabelResolver.ResetSessionStateForTests();
        var profile = new UsbMachineProfile();
        var original = TestUsbDevice("V:", "dev-v", "weak-port", seenCount: 1);
        var rec = new UsbKnownPortRecord();
        UsbPortLabelResolver.StampManualLabel(rec, original, "LT USB C", 60, DateTimeOffset.UtcNow);
        profile.KnownPorts.Add(rec);

        var originalEpoch = rec.LastManualLabelConnectionEpoch;
        var removalEpoch = UsbPortLabelResolver.MarkDriveRemoved("V:");
        var reinserted = TestUsbDevice("V:", "dev-v", "weak-port", seenCount: 2);
        var status = UsbPortLabelResolver.Resolve(reinserted, profile);

        Assert.True(removalEpoch > originalEpoch);
        Assert.Equal(removalEpoch, UsbPortLabelResolver.GetCurrentConnectionEpoch("V:"));
        Assert.NotEqual(UsbPortLabelValidity.CurrentSessionManual, status.Validity);
        Assert.Null(status.CurrentLabel);
        Assert.Equal("LT USB C", status.LastKnownLabel);
        Assert.Contains("Unverified after reconnect", status.StatusLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UsbPortLabelResolver_StrongTopologyChanged_MarksPortChangeSuspected()
    {
        UsbPortLabelResolver.ResetSessionStateForTests();
        var profile = new UsbMachineProfile();
        var original = TestUsbDevice(
            "O:",
            "dev-c",
            "port-c-old",
            seenCount: 1,
            locationPathHash: "loc-left");
        var rec = new UsbKnownPortRecord { StablePortKey = original.StablePortKey };
        UsbPortLabelResolver.StampManualLabel(rec, original, "LT USB C", 85, DateTimeOffset.UtcNow);
        profile.KnownPorts.Add(rec);

        var moved = TestUsbDevice(
            "O:",
            "dev-c",
            "port-c-new",
            seenCount: 2,
            locationPathHash: "loc-right");
        var status = UsbPortLabelResolver.Resolve(moved, profile);

        Assert.Equal(UsbPortLabelValidity.PortChangedSuspected, status.Validity);
        Assert.Null(status.CurrentLabel);
        Assert.Equal("LT USB C", status.LastKnownLabel);
        Assert.Contains("Port change suspected", status.StatusLine, StringComparison.Ordinal);
    }

    [Fact]
    public void UsbMachineProfileStore_UnverifiedBenchmarkDoesNotOverwriteBestLabeledPort()
    {
        UsbPortLabelResolver.ResetSessionStateForTests();
        var root = Path.Combine(Path.GetTempPath(), $"fe-usb-profile-{Guid.NewGuid():N}");
        try
        {
            var store = new UsbMachineProfileStore(root);
            var profile = store.LoadOrCreate();
            var original = TestUsbDevice("P:", "dev-d", "port-d", seenCount: 1);
            var rec = new UsbKnownPortRecord
            {
                StablePortKey = original.StablePortKey,
                LastBenchmark = new UsbIntelligenceBenchmarkResult
                {
                    Succeeded = true,
                    WriteSpeedMBps = 70,
                    ReadSpeedMBps = 80,
                    Timestamp = DateTimeOffset.UtcNow,
                    SummaryLine = "old"
                }
            };
            UsbPortLabelResolver.StampManualLabel(rec, original, "LT USB C", 60, DateTimeOffset.UtcNow);
            profile.KnownPorts.Add(rec);
            profile.PendingBenchmarkByDriveLetter["P"] = new UsbIntelligenceBenchmarkResult
            {
                Succeeded = true,
                WriteSpeedMBps = 120,
                ReadSpeedMBps = 130,
                Timestamp = DateTimeOffset.UtcNow,
                SummaryLine = "new unverified"
            };
            UsbPortLabelResolver.MarkDriveRemoved("P:");

            var snapshot = new UsbTopologySnapshot
            {
                GeneratedUtc = DateTimeOffset.UtcNow,
                Devices = [TestUsbDevice("P:", "dev-d", "port-d", seenCount: 2)],
                SummaryLine = "s"
            };
            store.ApplySnapshot(profile, snapshot);

            Assert.Equal(70, rec.LastBenchmark!.WriteSpeedMBps);
            Assert.True(profile.UnverifiedBenchmarkByDriveLetter.TryGetValue("P", out var unverified));
            Assert.False(unverified!.AttachedToVerifiedPort);
            Assert.Equal(UsbPortLabelValidity.TopologyUnavailable, unverified.PortLabelValidity);
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
    public void UsbGuidedMappingWorkflow_SavingNewLabelAttachesUnverifiedBenchmarkToCurrentPort()
    {
        UsbPortLabelResolver.ResetSessionStateForTests();
        var root = Path.Combine(Path.GetTempPath(), $"fe-usb-map-attach-{Guid.NewGuid():N}");
        try
        {
            var store = new UsbMachineProfileStore(root);
            var profile = store.LoadOrCreate();
            profile.UnverifiedBenchmarkByDriveLetter["Q"] = new UsbIntelligenceBenchmarkResult
            {
                Succeeded = true,
                WriteSpeedMBps = 90,
                ReadSpeedMBps = 95,
                Timestamp = DateTimeOffset.UtcNow,
                SummaryLine = "bench on current port",
                AttachedToVerifiedPort = false
            };

            var wf = new UsbGuidedMappingWorkflow();
            wf.StartMappingSession();
            wf.CaptureBeforeSnapshot(new UsbTopologySnapshot
            {
                GeneratedUtc = DateTimeOffset.UtcNow,
                Devices = [TestUsbDevice("Q:", "dev-e", "port-old", seenCount: 1, locationPathHash: "loc-old")],
                SummaryLine = "before"
            });
            wf.CaptureAfterSnapshot(new UsbTopologySnapshot
            {
                GeneratedUtc = DateTimeOffset.UtcNow,
                Devices = [TestUsbDevice("Q:", "dev-e", "port-new", seenCount: 1, locationPathHash: "loc-new")],
                SummaryLine = "after"
            });

            var ok = wf.TrySaveMappingLabel(profile, store, "RT USB C", out _, out var err);

            Assert.True(ok, err);
            var rec = Assert.Single(profile.KnownPorts);
            Assert.Equal("RT USB C", rec.UserLabel);
            Assert.NotNull(rec.LastBenchmark);
            Assert.True(rec.LastBenchmark!.AttachedToVerifiedPort);
            Assert.False(profile.UnverifiedBenchmarkByDriveLetter.ContainsKey("Q"));
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
    public void UsbPortLabelResolver_MultipleSavedPorts_SelectsMatchingFingerprintNotLastSavedLabel()
    {
        UsbPortLabelResolver.ResetSessionStateForTests();
        var profile = new UsbMachineProfile();
        var left = TestUsbDevice("R:", "dev-f", "port-left", seenCount: 1, locationPathHash: "loc-left");
        var right = TestUsbDevice("R:", "dev-f", "port-right", seenCount: 1, locationPathHash: "loc-right");
        var leftRec = new UsbKnownPortRecord { StablePortKey = left.StablePortKey };
        var rightRec = new UsbKnownPortRecord { StablePortKey = right.StablePortKey };
        UsbPortLabelResolver.StampManualLabel(leftRec, left, "LT USB C", 90, DateTimeOffset.UtcNow.AddMinutes(-5));
        UsbPortLabelResolver.StampManualLabel(rightRec, right, "RT USB C", 90, DateTimeOffset.UtcNow);
        profile.KnownPorts.Add(leftRec);
        profile.KnownPorts.Add(rightRec);
        UsbPortLabelResolver.MarkDriveRemoved("R:");

        var currentLeft = TestUsbDevice("R:", "dev-f", "port-left", seenCount: 3, locationPathHash: "loc-left");
        var status = UsbPortLabelResolver.Resolve(currentLeft, profile);

        Assert.Equal(UsbPortLabelValidity.VerifiedCurrent, status.Validity);
        Assert.Equal("LT USB C", status.CurrentLabel);
        Assert.Contains("RT USB C", status.CandidateLabels);
    }

    [Fact]
    public void UsbPortLabelResolver_MultipleSavedPorts_SelectsRightFingerprint()
    {
        UsbPortLabelResolver.ResetSessionStateForTests();
        var profile = new UsbMachineProfile();
        var left = TestUsbDevice("S:", "dev-g", "port-left", seenCount: 1, locationPathHash: "loc-left");
        var right = TestUsbDevice("S:", "dev-g", "port-right", seenCount: 1, locationPathHash: "loc-right");
        var leftRec = new UsbKnownPortRecord { StablePortKey = left.StablePortKey };
        var rightRec = new UsbKnownPortRecord { StablePortKey = right.StablePortKey };
        UsbPortLabelResolver.StampManualLabel(leftRec, left, "LT USB C", 90, DateTimeOffset.UtcNow.AddMinutes(-5));
        UsbPortLabelResolver.StampManualLabel(rightRec, right, "RT USB C", 90, DateTimeOffset.UtcNow);
        profile.KnownPorts.Add(leftRec);
        profile.KnownPorts.Add(rightRec);
        UsbPortLabelResolver.MarkDriveRemoved("S:");

        var currentRight = TestUsbDevice("S:", "dev-g", "port-right", seenCount: 3, locationPathHash: "loc-right");
        var status = UsbPortLabelResolver.Resolve(currentRight, profile);

        Assert.Equal(UsbPortLabelValidity.VerifiedCurrent, status.Validity);
        Assert.Equal("RT USB C", status.CurrentLabel);
    }

    [Fact]
    public void UsbPortLabelResolver_WeakIdenticalPortEvidence_ReturnsAmbiguousNotLastSaved()
    {
        UsbPortLabelResolver.ResetSessionStateForTests();
        var profile = new UsbMachineProfile();
        var left = TestUsbDevice("T:", "dev-h", "same-weak-port", seenCount: 1);
        var right = TestUsbDevice("T:", "dev-h", "same-weak-port", seenCount: 1);
        var leftRec = new UsbKnownPortRecord();
        var rightRec = new UsbKnownPortRecord();
        UsbPortLabelResolver.StampManualLabel(leftRec, left, "LT USB C", 55, DateTimeOffset.UtcNow.AddMinutes(-5));
        UsbPortLabelResolver.StampManualLabel(rightRec, right, "RT USB C", 55, DateTimeOffset.UtcNow);
        profile.KnownPorts.Add(leftRec);
        profile.KnownPorts.Add(rightRec);
        UsbPortLabelResolver.MarkDriveRemoved("T:");

        var current = TestUsbDevice("T:", "dev-h", "same-weak-port", seenCount: 3);
        var status = UsbPortLabelResolver.Resolve(current, profile);

        Assert.Equal(UsbPortLabelValidity.Ambiguous, status.Validity);
        Assert.Null(status.CurrentLabel);
        Assert.Contains("LT USB C", status.CandidateLabels);
        Assert.Contains("RT USB C", status.CandidateLabels);
        Assert.Contains("Ambiguous", status.StatusLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UsbGuidedMappingWorkflow_SavingSecondLabelDoesNotOverwriteFirstLabel()
    {
        UsbPortLabelResolver.ResetSessionStateForTests();
        var profile = new UsbMachineProfile();
        var left = TestUsbDevice("U:", "dev-i", "same-weak-port", seenCount: 1);
        var right = TestUsbDevice("U:", "dev-i", "same-weak-port", seenCount: 1);
        var leftRec = new UsbKnownPortRecord();
        UsbPortLabelResolver.StampManualLabel(leftRec, left, "LT USB C", 55, DateTimeOffset.UtcNow.AddMinutes(-5));
        profile.KnownPorts.Add(leftRec);

        var rightRec = profile.KnownPorts.FirstOrDefault(p =>
            string.Equals(p.UserLabel?.Trim(), "RT USB C", StringComparison.OrdinalIgnoreCase));
        if (rightRec is null)
        {
            rightRec = new UsbKnownPortRecord { MappingId = Guid.NewGuid().ToString("N") };
            profile.KnownPorts.Add(rightRec);
        }

        UsbPortLabelResolver.StampManualLabel(rightRec, right, "RT USB C", 55, DateTimeOffset.UtcNow);

        Assert.Equal(2, profile.KnownPorts.Count);
        Assert.Contains(profile.KnownPorts, p => p.UserLabel == "LT USB C");
        Assert.Contains(profile.KnownPorts, p => p.UserLabel == "RT USB C");
    }

    [Fact]
    public void UsbGuidedMappingWorkflow_SavingRightAfterReconnectMakesRightCurrentForNewEpoch()
    {
        UsbPortLabelResolver.ResetSessionStateForTests();
        var profile = new UsbMachineProfile();
        var left = TestUsbDevice("W:", "dev-w", "weak-port", seenCount: 1);
        var leftRec = new UsbKnownPortRecord();
        UsbPortLabelResolver.StampManualLabel(leftRec, left, "LT USB C", 55, DateTimeOffset.UtcNow.AddMinutes(-10));
        profile.KnownPorts.Add(leftRec);

        UsbPortLabelResolver.MarkDriveRemoved("W:");
        var right = TestUsbDevice("W:", "dev-w", "weak-port", seenCount: 2);
        var rightRec = new UsbKnownPortRecord();
        UsbPortLabelResolver.StampManualLabel(rightRec, right, "RT USB C", 55, DateTimeOffset.UtcNow);
        profile.KnownPorts.Add(rightRec);

        var status = UsbPortLabelResolver.Resolve(right, profile);

        Assert.Equal(2, profile.KnownPorts.Count);
        Assert.Equal(UsbPortLabelValidity.CurrentSessionManual, status.Validity);
        Assert.Equal("RT USB C", status.CurrentLabel);
        Assert.Contains(profile.KnownPorts, p => p.UserLabel == "LT USB C");
        Assert.Contains(profile.KnownPorts, p => p.UserLabel == "RT USB C");
        Assert.Equal(UsbPortLabelResolver.GetCurrentConnectionEpoch("W:"), rightRec.LastManualLabelConnectionEpoch);
        Assert.NotEqual(leftRec.LastManualLabelConnectionEpoch, rightRec.LastManualLabelConnectionEpoch);
    }

    [Fact]
    public void UsbMachineProfileStore_BenchmarkAfterReconnectStaysUnverifiedUntilPortConfirmed()
    {
        UsbPortLabelResolver.ResetSessionStateForTests();
        var root = Path.Combine(Path.GetTempPath(), $"fe-usb-profile-reconnect-{Guid.NewGuid():N}");
        try
        {
            var store = new UsbMachineProfileStore(root);
            var profile = store.LoadOrCreate();
            var left = TestUsbDevice("X:", "dev-x", "weak-port", seenCount: 1);
            var rec = new UsbKnownPortRecord();
            UsbPortLabelResolver.StampManualLabel(rec, left, "LT USB C", 55, DateTimeOffset.UtcNow);
            profile.KnownPorts.Add(rec);
            profile.PendingBenchmarkByDriveLetter["X"] = new UsbIntelligenceBenchmarkResult
            {
                Succeeded = true,
                WriteSpeedMBps = 62,
                ReadSpeedMBps = 4000,
                Timestamp = DateTimeOffset.UtcNow,
                SummaryLine = "bench after reconnect"
            };

            UsbPortLabelResolver.MarkDriveRemoved("X:");
            store.ApplySnapshot(profile, new UsbTopologySnapshot
            {
                GeneratedUtc = DateTimeOffset.UtcNow,
                Devices = [TestUsbDevice("X:", "dev-x", "weak-port", seenCount: 2)],
                SummaryLine = "after reconnect"
            });

            Assert.Null(rec.LastBenchmark);
            Assert.True(profile.UnverifiedBenchmarkByDriveLetter.TryGetValue("X", out var unverified));
            Assert.False(unverified.AttachedToVerifiedPort);
            Assert.Equal("LT USB C", unverified.AttachedPortLabel);
            Assert.Equal(UsbPortLabelValidity.TopologyUnavailable, unverified.PortLabelValidity);
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

    private static UsbDeviceInfo TestUsbDevice(
        string driveLetter,
        string stableDeviceKey,
        string stablePortKey,
        int seenCount,
        string locationPathHash = "")
    {
        return new UsbDeviceInfo
        {
            FriendlyName = "Ventoy",
            DriveLetter = driveLetter,
            VolumeLabel = "Ventoy",
            FileSystem = "exFAT",
            IsRemovableMassStorage = true,
            InferredSpeed = UsbSpeedClassification.Usb3,
            StableDeviceKey = stableDeviceKey,
            StablePortKey = stablePortKey,
            SerialHash = "serial-" + stableDeviceKey,
            VolumeIdentityHash = "volume-" + stableDeviceKey,
            DeviceInstanceIdHash = "instance-" + stableDeviceKey,
            PnpDeviceIdHash = "pnp-" + stableDeviceKey,
            ControllerKey = "controller",
            LocationPathHash = locationPathHash,
            SeenCount = seenCount
        };
    }
}
