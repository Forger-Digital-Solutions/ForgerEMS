using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public sealed class UsbIntelligenceService : IUsbIntelligenceService
{
    public static readonly JsonSerializerOptions UsbJsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public UsbTopologySnapshot BuildTopologySnapshot(UsbTargetInfo? selectedTarget, UsbTopologyBuildOptions? options = null)
    {
        options ??= new UsbTopologyBuildOptions();
        var generated = DateTimeOffset.UtcNow;
        var controllers = EnumerateControllers().ToList();
        var devices = EnumerateUsbMassStorage().ToList();
        AnnotateDriveLetters(devices);
        AnnotateVolumeIdentity(devices);
        EnrichDeviceHashes(devices);
        LinkDevicesToControllers(devices, controllers);
        FinalizeStablePortKeys(devices);
        ApplyMachineProfile(devices, options.MachineProfile, generated);
        foreach (var d in devices)
        {
            d.ConfidenceScore = ComputeDeviceConfidence(d);
        }

        var anyUsbDisk = devices.Any(d => d.IsRemovableMassStorage);
        var ports = BuildPorts(controllers, devices, anyUsbDisk);

        var summary =
            $"USB controllers: {controllers.Count}; removable USB storage devices: {devices.Count(d => d.IsRemovableMassStorage)}; " +
            $"dominant link speed: {DescribeDominantSpeed(controllers, devices)}.";

        var match = FindDeviceForTarget(selectedTarget, devices);
        var shell = new UsbTopologySnapshot
        {
            GeneratedUtc = generated,
            Controllers = controllers,
            Ports = ports,
            Devices = devices,
            SummaryLine = summary
        };

        var diff = UsbTopologyDiffService.Compare(options.PreviousSnapshot, shell);
        var benchmarkRaw = ResolveBenchmark(match, options.MachineProfile);
        var portRec = ResolvePortRecord(match, options.MachineProfile);
        var recommendation = UsbBuilderRecommendationEngine.Build(
            selectedTarget,
            match,
            controllers,
            diff,
            options.MachineProfile,
            benchmarkRaw,
            portRec);

        var fingerprint = string.IsNullOrWhiteSpace(options.MachineProfile?.MachineFingerprintHash)
            ? UsbMachineProfileStore.ComputeMachineFingerprintHash()
            : options.MachineProfile!.MachineFingerprintHash;

        var refinedBench = UsbBenchmarkRefinery.Refine(benchmarkRaw, match?.InferredSpeed);
        var (combinedScore, combinedReason) = UsbConfidenceAggregator.Combine(
            match?.ConfidenceScore ?? 0,
            diff,
            refinedBench,
            portRec);

        var withRec = new UsbTopologySnapshot
        {
            GeneratedUtc = generated,
            Controllers = controllers,
            Ports = ports,
            Devices = devices,
            SelectedTargetRecommendation = recommendation,
            SummaryLine = summary,
            TopologyDiff = diff,
            MachineProfileFingerprint = fingerprint,
            SelectedTargetBenchmark = refinedBench,
            SelectedTargetStablePortKey = match?.StablePortKey,
            SelectedTargetPortUserLabel = portRec?.UserLabel,
            SelectedTargetMappingConfidence = portRec?.MappingConfidenceScore ?? 0,
            CombinedConfidenceScore = combinedScore,
            CombinedConfidenceReason = combinedReason
        };

        var usbDiag = UsbDiagnosticsComposer.Build(withRec, options.MachineProfile);
        var narrative = UsbKyraNarrativeBuilder.Build(withRec);

        IntelligenceLogWriter.Append("usb-intelligence.log", $"Topology snapshot: {summary}");

        return new UsbTopologySnapshot
        {
            GeneratedUtc = generated,
            Controllers = controllers,
            Ports = ports,
            Devices = devices,
            SelectedTargetRecommendation = recommendation,
            SummaryLine = summary,
            TopologyDiff = diff,
            UsbDiagnostics = usbDiag,
            KyraUsbNarrative = narrative,
            MachineProfileFingerprint = fingerprint,
            SelectedTargetBenchmark = refinedBench,
            SelectedTargetStablePortKey = match?.StablePortKey,
            SelectedTargetPortUserLabel = portRec?.UserLabel,
            SelectedTargetMappingConfidence = portRec?.MappingConfidenceScore ?? 0,
            CombinedConfidenceScore = combinedScore,
            CombinedConfidenceReason = combinedReason
        };
    }

    private static UsbIntelligenceBenchmarkResult? ResolveBenchmark(UsbDeviceInfo? match, UsbMachineProfile? profile)
    {
        if (match is null || profile is null)
        {
            return null;
        }

        var letter = NormalizeDriveLetterForProfile(match.DriveLetter);
        if (!string.IsNullOrEmpty(letter) &&
            profile.PendingBenchmarkByDriveLetter.TryGetValue(letter, out var pending))
        {
            return pending;
        }

        if (string.IsNullOrWhiteSpace(match.StablePortKey))
        {
            return null;
        }

        return profile.KnownPorts.FirstOrDefault(p => p.StablePortKey == match.StablePortKey)?.LastBenchmark;
    }

    private static UsbKnownPortRecord? ResolvePortRecord(UsbDeviceInfo? match, UsbMachineProfile? profile)
    {
        if (match is null || profile is null || string.IsNullOrWhiteSpace(match.StablePortKey))
        {
            return null;
        }

        return profile.KnownPorts.FirstOrDefault(p => p.StablePortKey == match.StablePortKey);
    }

    private static string NormalizeDriveLetterForProfile(string? driveLetter)
    {
        if (string.IsNullOrWhiteSpace(driveLetter))
        {
            return string.Empty;
        }

        return driveLetter.TrimEnd('\\').TrimEnd(':').ToUpperInvariant();
    }

    public async Task WriteLatestReportAsync(string reportsDirectory, UsbTopologySnapshot snapshot)
    {
        await Task.Run(() =>
        {
            Directory.CreateDirectory(reportsDirectory);
            var path = Path.Combine(reportsDirectory, "usb-intelligence-latest.json");
            File.WriteAllText(path, JsonSerializer.Serialize(snapshot, JsonWriteOptions));
        }).ConfigureAwait(false);
    }

    public UsbBuilderPreflightResult GetVentoyPreflight(UsbTargetInfo? selectedTarget, UsbTopologySnapshot? snapshot)
    {
        snapshot ??= BuildTopologySnapshot(selectedTarget);
        var rec = snapshot.SelectedTargetRecommendation;
        if (rec is null)
        {
            return new UsbBuilderPreflightResult
            {
                ShouldWarn = true,
                Message = "USB speed could not be confirmed for the selected target. Consider testing on a blue USB 3.x port.",
                Speed = UsbSpeedClassification.Unknown,
                Risk = UsbPortRiskLevel.Unknown,
                Quality = UsbBuilderQuality.Unknown
            };
        }

        var warn = rec.Quality is UsbBuilderQuality.Slow or UsbBuilderQuality.Unknown or UsbBuilderQuality.Risky ||
                   rec.Speed == UsbSpeedClassification.Usb2 ||
                   rec.Risk is UsbPortRiskLevel.High or UsbPortRiskLevel.Unknown;

        return new UsbBuilderPreflightResult
        {
            ShouldWarn = warn,
            Message = $"{rec.ClassificationLine} {rec.Summary} {rec.Detail}".Trim(),
            Speed = rec.Speed,
            Risk = rec.Risk,
            Quality = rec.Quality
        };
    }

    private static UsbDeviceInfo? FindDeviceForTarget(UsbTargetInfo? selectedTarget, List<UsbDeviceInfo> devices)
    {
        if (selectedTarget is null || string.IsNullOrWhiteSpace(selectedTarget.DriveLetter))
        {
            return null;
        }

        var letter = selectedTarget.DriveLetter.TrimEnd('\\').TrimEnd(':');
        return devices.FirstOrDefault(d =>
            !string.IsNullOrWhiteSpace(d.DriveLetter) &&
            string.Equals(d.DriveLetter.TrimEnd(':'), letter, StringComparison.OrdinalIgnoreCase));
    }

    private static string DescribeDominantSpeed(List<UsbControllerInfo> controllers, List<UsbDeviceInfo> devices)
    {
        var all = controllers.Select(c => c.InferredSpeed)
            .Concat(devices.Select(d => d.InferredSpeed))
            .ToList();
        if (all.Any(s => s == UsbSpeedClassification.UsbC || s == UsbSpeedClassification.Usb3))
        {
            return "USB 3.x / USB-C class";
        }

        if (all.Any(s => s == UsbSpeedClassification.Usb2))
        {
            return "USB 2 class";
        }

        return "unknown";
    }

    private static List<UsbPortInfo> BuildPorts(
        List<UsbControllerInfo> controllers,
        List<UsbDeviceInfo> devices,
        bool anyUsbDisk)
    {
        var ports = controllers
            .Select((c, i) => new UsbPortInfo
            {
                PortLabel = string.IsNullOrWhiteSpace(c.Name) ? $"Controller {i + 1}" : c.Name,
                StablePortKey = UsbIdentityHasher.Sha256Hex($"{c.ControllerKey}|port-index:{i}"),
                ControllerKey = c.ControllerKey,
                HubKey = c.HubKey,
                FriendlyLocation = c.FriendlyLocation,
                InferredSpeed = c.InferredSpeed,
                DeviceAttached = anyUsbDisk && (ControllerMatches(c, devices) || controllers.Count == 1),
                Risk = ToPortRisk(c.InferredSpeed, anyUsbDisk && (ControllerMatches(c, devices) || controllers.Count == 1)),
                ConfidenceScore = c.ConfidenceScore,
                ConfidenceReason = c.ConfidenceReason
            })
            .ToList();

        if (ports.Count == 0 && controllers.Count > 0)
        {
            ports.AddRange(controllers.Select((c, i) => new UsbPortInfo
            {
                PortLabel = c.Name,
                StablePortKey = UsbIdentityHasher.Sha256Hex($"{c.ControllerKey}|port-index:{i}"),
                ControllerKey = c.ControllerKey,
                HubKey = c.HubKey,
                FriendlyLocation = c.FriendlyLocation,
                InferredSpeed = c.InferredSpeed,
                DeviceAttached = anyUsbDisk,
                Risk = ToPortRisk(c.InferredSpeed, anyUsbDisk),
                ConfidenceScore = c.ConfidenceScore,
                ConfidenceReason = c.ConfidenceReason
            }));
        }

        return ports;
    }

    private static UsbPortRiskLevel ToPortRisk(UsbSpeedClassification speed, bool hasDevice)
    {
        if (!hasDevice)
        {
            return UsbPortRiskLevel.Low;
        }

        return speed switch
        {
            UsbSpeedClassification.Usb2 => UsbPortRiskLevel.Medium,
            UsbSpeedClassification.Unknown => UsbPortRiskLevel.Unknown,
            _ => UsbPortRiskLevel.Low
        };
    }

    private static bool ControllerMatches(UsbControllerInfo controller, List<UsbDeviceInfo> devices)
    {
        return devices.Any(device =>
        {
            if (string.IsNullOrWhiteSpace(device.LinkedControllerName))
            {
                return false;
            }

            return device.LinkedControllerName.Contains(controller.Name, StringComparison.OrdinalIgnoreCase) ||
                   controller.Name.Contains(device.LinkedControllerName, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static List<UsbControllerInfo> EnumerateControllers()
    {
        var list = new List<UsbControllerInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, DeviceID, PNPDeviceID FROM Win32_USBController");
            foreach (ManagementObject o in searcher.Get())
            {
                using (o)
                {
                    var name = $"{o["Name"]}";
                    var id = $"{o["PNPDeviceID"] ?? o["DeviceID"]}";
                    var speed = ClassifySpeed(id + " " + name);
                    var key = UsbIdentityHasher.Sha256Hex(id);
                    var hubKey = ExtractHubKey(id);
                    var conf = 50 + (speed != UsbSpeedClassification.Unknown ? 20 : 0) +
                               (id.Contains("XHCI", StringComparison.OrdinalIgnoreCase) ? 15 : 0);
                    list.Add(new UsbControllerInfo
                    {
                        Name = string.IsNullOrWhiteSpace(name) ? "USB controller" : name.Trim(),
                        ControllerKey = key,
                        HubKey = hubKey,
                        FriendlyLocation = string.IsNullOrWhiteSpace(name) ? "USB controller" : name.Trim(),
                        InferredSpeed = speed,
                        SpeedRationale = DescribeSpeed(id, name, speed),
                        ConfidenceScore = Math.Min(95, conf),
                        ConfidenceReason = "WMI Win32_USBController PnP heuristic"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            IntelligenceLogWriter.Append("usb-intelligence.log", $"Controller enumeration failed: {ex.Message}");
        }

        return list;
    }

    private static List<UsbDeviceInfo> EnumerateUsbMassStorage()
    {
        var list = new List<UsbDeviceInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID,Model,PNPDeviceID,InterfaceType,MediaType FROM Win32_DiskDrive");
            foreach (ManagementObject o in searcher.Get())
            {
                using (o)
                {
                    var pnp = $"{o["PNPDeviceID"]}";
                    var devId = $"{o["DeviceID"]}";
                    var iface = $"{o["InterfaceType"]}";
                    var model = $"{o["Model"]}";
                    if (!iface.Equals("USB", StringComparison.OrdinalIgnoreCase) &&
                        pnp.IndexOf("USBSTOR", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    var speed = ClassifySpeed(pnp + " " + model);
                    list.Add(new UsbDeviceInfo
                    {
                        FriendlyName = string.IsNullOrWhiteSpace(model) ? "USB disk" : model.Trim(),
                        IsRemovableMassStorage = true,
                        InferredSpeed = speed,
                        PnpDeviceId = pnp,
                        WmiDeviceId = devId
                    });
                }
            }
        }
        catch (Exception ex)
        {
            IntelligenceLogWriter.Append("usb-intelligence.log", $"Disk enumeration failed: {ex.Message}");
        }

        return list;
    }

    private static void AnnotateDriveLetters(List<UsbDeviceInfo> devices)
    {
        try
        {
            foreach (var device in devices)
            {
                if (string.IsNullOrWhiteSpace(device.WmiDeviceId))
                {
                    continue;
                }

                var letter = ResolveDriveLetterForDisk(device.WmiDeviceId);
                if (!string.IsNullOrWhiteSpace(letter))
                {
                    device.DriveLetter = letter;
                }
            }
        }
        catch (Exception ex)
        {
            IntelligenceLogWriter.Append("usb-intelligence.log", $"Drive letter mapping failed: {ex.Message}");
        }
    }

    private static void AnnotateVolumeIdentity(List<UsbDeviceInfo> devices)
    {
        foreach (var d in devices)
        {
            if (string.IsNullOrWhiteSpace(d.DriveLetter))
            {
                continue;
            }

            var letter = d.DriveLetter.TrimEnd('\\');
            if (!letter.EndsWith(':'))
            {
                letter += ":";
            }

            var serial = TryGetVolumeSerialNumber(letter);
            if (!string.IsNullOrWhiteSpace(serial))
            {
                d.VolumeIdentityHash = UsbIdentityHasher.Sha256Hex(serial);
            }
        }
    }

    private static string? TryGetVolumeSerialNumber(string driveLetter)
    {
        try
        {
            var esc = driveLetter.Replace("'", "''");
            using var searcher =
                new ManagementObjectSearcher($"SELECT VolumeSerialNumber FROM Win32_LogicalDisk WHERE DeviceID='{esc}'");
            foreach (ManagementObject o in searcher.Get())
            {
                using (o)
                {
                    var s = $"{o["VolumeSerialNumber"]}";
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        return s;
                    }
                }
            }
        }
        catch
        {
            // best effort
        }

        return null;
    }

    private static void EnrichDeviceHashes(List<UsbDeviceInfo> devices)
    {
        foreach (var d in devices)
        {
            var pnp = d.PnpDeviceId ?? string.Empty;
            d.DeviceInstanceIdHash = UsbIdentityHasher.Sha256Hex(pnp);
            var parent = ParentPnpPath(pnp);
            d.ParentDeviceIdHash = string.IsNullOrEmpty(parent) ? string.Empty : UsbIdentityHasher.Sha256Hex(parent);
            d.HubKey = ExtractHubKey(pnp);
            d.LocationPathHash = TryQueryLocationHash(pnp);
            d.StableDeviceKey = UsbIdentityHasher.Sha256Hex(
                $"{d.VolumeIdentityHash}|{d.FriendlyName}|{d.DeviceInstanceIdHash}");
            d.FriendlyLocation = string.IsNullOrWhiteSpace(d.DriveLetter)
                ? "Removable USB (no drive letter yet)"
                : $"Removable disk {d.DriveLetter.TrimEnd('\\')}";
            d.ConfidenceReason = "WMI USBSTOR disk + volume serial hash (if available)";
        }
    }

    private static void LinkDevicesToControllers(List<UsbDeviceInfo> devices, List<UsbControllerInfo> controllers)
    {
        if (controllers.Count == 0)
        {
            return;
        }

        if (controllers.Count == 1)
        {
            var c = controllers[0];
            foreach (var d in devices)
            {
                d.LinkedControllerName = c.Name;
                d.ControllerKey = c.ControllerKey;
            }

            return;
        }

        foreach (var d in devices)
        {
            var matchCtrl = controllers.FirstOrDefault(c => c.InferredSpeed == d.InferredSpeed)
                            ?? controllers.FirstOrDefault(c =>
                                c.InferredSpeed is UsbSpeedClassification.Usb3 or UsbSpeedClassification.UsbC)
                            ?? controllers[0];
            d.LinkedControllerName = matchCtrl.Name;
            d.ControllerKey = matchCtrl.ControllerKey;
        }
    }

    private static void FinalizeStablePortKeys(List<UsbDeviceInfo> devices)
    {
        foreach (var d in devices)
        {
            d.StablePortKey = UsbIdentityHasher.Sha256Hex(
                $"{d.ControllerKey}|{d.DeviceInstanceIdHash}|{d.LocationPathHash}");
        }
    }

    private static void ApplyMachineProfile(List<UsbDeviceInfo> devices, UsbMachineProfile? profile, DateTimeOffset generated)
    {
        if (profile is null)
        {
            foreach (var d in devices)
            {
                d.FirstSeenUtc = generated;
                d.LastSeenUtc = generated;
                d.SeenCount = 1;
            }

            return;
        }

        foreach (var d in devices)
        {
            if (string.IsNullOrWhiteSpace(d.StableDeviceKey))
            {
                d.FirstSeenUtc = generated;
                d.LastSeenUtc = generated;
                d.SeenCount = 1;
                continue;
            }

            if (profile.KnownDevicesByStableKey.TryGetValue(d.StableDeviceKey, out var rec))
            {
                d.FirstSeenUtc = rec.FirstSeenUtc;
                d.LastSeenUtc = generated;
                d.SeenCount = rec.SeenCount + 1;
            }
            else
            {
                d.FirstSeenUtc = generated;
                d.LastSeenUtc = generated;
                d.SeenCount = 1;
            }
        }
    }

    private static int ComputeDeviceConfidence(UsbDeviceInfo d)
    {
        var score = 45;
        if (!string.IsNullOrWhiteSpace(d.VolumeIdentityHash))
        {
            score += 25;
        }

        if (!string.IsNullOrWhiteSpace(d.LocationPathHash))
        {
            score += 15;
        }

        if (d.SeenCount >= 3)
        {
            score += 10;
        }

        return Math.Min(95, score);
    }

    private static string? ParentPnpPath(string? pnp)
    {
        if (string.IsNullOrWhiteSpace(pnp))
        {
            return null;
        }

        var i = pnp.LastIndexOf('\\');
        return i > 0 ? pnp[..i] : null;
    }

    private static string ExtractHubKey(string? pnp)
    {
        if (string.IsNullOrWhiteSpace(pnp))
        {
            return string.Empty;
        }

        foreach (var part in pnp.Split('\\'))
        {
            if (part.Contains("HUB", StringComparison.OrdinalIgnoreCase))
            {
                return UsbIdentityHasher.Sha256Hex(part);
            }
        }

        return string.Empty;
    }

    private static string TryQueryLocationHash(string pnp)
    {
        if (string.IsNullOrWhiteSpace(pnp))
        {
            return string.Empty;
        }

        try
        {
            var esc = pnp.Replace("\\", "\\\\").Replace("'", "''");
            using var searcher =
                new ManagementObjectSearcher($"SELECT Location FROM Win32_PnPEntity WHERE PNPDeviceID='{esc}'");
            foreach (ManagementObject o in searcher.Get())
            {
                using (o)
                {
                    var loc = $"{o["Location"]}";
                    if (!string.IsNullOrWhiteSpace(loc))
                    {
                        return UsbIdentityHasher.Sha256Hex(loc);
                    }
                }
            }
        }
        catch
        {
            // ignore
        }

        return string.Empty;
    }

    private static string? ResolveDriveLetterForDisk(string diskDeviceId)
    {
        try
        {
            var path = diskDeviceId.Replace("'", "''");
            using var disk = new ManagementObject($"Win32_DiskDrive.DeviceID='{path}'");
            disk.Get();
            foreach (ManagementObject partition in disk.GetRelated("Win32_DiskPartition"))
            {
                using (partition)
                {
                    foreach (ManagementObject logical in partition.GetRelated("Win32_LogicalDisk"))
                    {
                        using (logical)
                        {
                            var id = $"{logical["DeviceID"]}";
                            if (!string.IsNullOrWhiteSpace(id))
                            {
                                return id;
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Best effort.
        }

        return null;
    }

    private static UsbSpeedClassification ClassifySpeed(string blob)
    {
        var b = blob.ToUpperInvariant();
        if (b.Contains("USB4", StringComparison.Ordinal) ||
            b.Contains("TYPE-C", StringComparison.Ordinal) ||
            b.Contains("USB-C", StringComparison.Ordinal))
        {
            return UsbSpeedClassification.UsbC;
        }

        if (b.Contains("3.1", StringComparison.Ordinal) ||
            b.Contains("3.2", StringComparison.Ordinal) ||
            b.Contains("310", StringComparison.Ordinal) ||
            b.Contains("HUB30", StringComparison.Ordinal) ||
            b.Contains("USB31", StringComparison.Ordinal) ||
            b.Contains("XHCI", StringComparison.Ordinal))
        {
            return UsbSpeedClassification.Usb3;
        }

        if (b.Contains("2.0", StringComparison.Ordinal) ||
            b.Contains("HUB20", StringComparison.Ordinal) ||
            b.Contains("EHCI", StringComparison.Ordinal) ||
            b.Contains("1.1", StringComparison.Ordinal))
        {
            return UsbSpeedClassification.Usb2;
        }

        return UsbSpeedClassification.Unknown;
    }

    private static string DescribeSpeed(string id, string name, UsbSpeedClassification speed) =>
        speed switch
        {
            UsbSpeedClassification.Usb2 => "Heuristic: USB 2 / EHCI indicators in PnP path.",
            UsbSpeedClassification.Usb3 => "Heuristic: USB 3 / xHCI indicators in PnP path.",
            UsbSpeedClassification.UsbC => "Heuristic: USB4 / Type-C style indicators.",
            _ => "Heuristic: no reliable USB generation markers."
        };
}
