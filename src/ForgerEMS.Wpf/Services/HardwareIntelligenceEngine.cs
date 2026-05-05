using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using VentoyToolkitSetup.Wpf.Configuration;

namespace VentoyToolkitSetup.Wpf.Services;

public sealed class MachineClassSignal
{
    public string Name { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    public int Weight { get; init; }

    public string Source { get; init; } = string.Empty;
}

public sealed class MachineClassResult
{
    public string PrimaryClass { get; init; } = "Unknown / Mixed";

    public IReadOnlyList<string> SecondaryClasses { get; init; } = Array.Empty<string>();

    public string Confidence { get; init; } = "Low";

    public IReadOnlyList<MachineClassSignal> Signals { get; init; } = Array.Empty<MachineClassSignal>();

    public string TechnicianNote { get; init; } = "Run System Intelligence to classify this machine.";

    public string SummaryLine =>
        $"{PrimaryClass} ({Confidence} confidence). {TechnicianNote}";
}

public sealed class SensorReading
{
    public string Name { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string Value { get; init; } = "Not exposed";

    public string Unit { get; init; } = string.Empty;

    public string Status { get; init; } = "Unknown";

    public string Confidence { get; init; } = "Low";

    public string Source { get; init; } = string.Empty;

    public DateTimeOffset LastUpdatedUtc { get; init; } = DateTimeOffset.UtcNow;

    public bool IsLive { get; init; }

    public bool IsInferred { get; init; }

    public bool IsUnavailable { get; init; }

    public string UnavailableReason { get; init; } = string.Empty;

    public string TechnicianNote { get; init; } = string.Empty;
}

public sealed class SensorGroup
{
    public string Category { get; init; } = string.Empty;

    public int KnownFields { get; init; }

    public int TotalFields { get; init; }

    public string Summary { get; init; } = string.Empty;

    public IReadOnlyList<SensorReading> Readings { get; init; } = Array.Empty<SensorReading>();
}

public sealed class SensorMatrixResult
{
    public IReadOnlyList<SensorGroup> Groups { get; init; } = Array.Empty<SensorGroup>();

    public IReadOnlyList<SensorProviderManifest> SensorProviders { get; init; } = Array.Empty<SensorProviderManifest>();

    public DeepSensorModeResolution DeepSensorMode { get; init; } = DeepSensorModeResolver.Resolve();

    public string Confidence { get; init; } = "Medium";

    public string DeepSensorModeNote { get; init; } =
        "Some sensors require admin access, firmware support, vendor drivers, or an optional reviewed sensor provider.";

    public string CoverageSummary =>
        string.Join("; ", Groups.Select(group => $"{group.Category}: {group.KnownFields}/{group.TotalFields} fields known"));

    public string SummaryLine => $"{CoverageSummary}. Confidence: {Confidence}.";
}

public sealed class SensorProviderResult
{
    public string ProviderName { get; init; } = string.Empty;

    public string ProviderVersion { get; init; } = "1.0";

    public string ProviderKind { get; init; } = "Unknown";

    public bool IsEnabled { get; init; }

    public bool IsBundled { get; init; }

    public bool RequiresAdmin { get; init; }

    public bool RequiresThirdPartyLicenseNotice { get; init; }

    public bool IsReadOnly { get; init; } = true;

    public string TrustLevel { get; init; } = SensorProviderTrustLevels.ExperimentalDisabled;

    public string RuntimeMode { get; init; } = SensorProviderRuntimeModes.Disabled;

    public SensorProviderCapabilities Capabilities { get; init; } = SensorProviderCapabilities.None;

    public string FailureReason { get; init; } = string.Empty;

    public DateTimeOffset LastRunUtc { get; init; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<SensorReading> Readings { get; init; } = Array.Empty<SensorReading>();

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();

    public ThirdPartyNotice? ThirdPartyNotice { get; init; }
}

public static class SensorProviderTrustLevels
{
    public const string BuiltInWindows = "BuiltInWindows";
    public const string BundledReviewed = "BundledReviewed";
    public const string VendorDetected = "VendorDetected";
    public const string AdminRequired = "AdminRequired";
    public const string ExperimentalDisabled = "ExperimentalDisabled";
}

public static class SensorProviderRuntimeModes
{
    public const string DefaultSafe = "DefaultSafe";
    public const string DeepSensorReadOnly = "DeepSensorReadOnly";
    public const string AdminReadOnly = "AdminReadOnly";
    public const string Disabled = "Disabled";
}

public sealed class SensorProviderCapabilities
{
    public static SensorProviderCapabilities None => new()
    {
        SupportedCapabilities = Array.Empty<string>(),
        MissingCapabilities = Array.Empty<string>(),
        ReadOnlyGuarantees =
        [
            "No fan control",
            "No voltage control",
            "No clock control",
            "No BIOS or firmware writes"
        ]
    };

    public IReadOnlyList<string> SupportedCapabilities { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> MissingCapabilities { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ReadOnlyGuarantees { get; init; } = Array.Empty<string>();
}

public sealed class ThirdPartyNotice
{
    public string Name { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string License { get; init; } = string.Empty;

    public string ProjectUrl { get; init; } = string.Empty;

    public string BundledPath { get; init; } = string.Empty;

    public string SourceOfferOrNotice { get; init; } = string.Empty;

    public bool ModifiedFilesDisclosureNeeded { get; init; }
}

public sealed class SensorProviderManifest
{
    public string ProviderName { get; init; } = string.Empty;

    public string ProviderVersion { get; init; } = "1.0";

    public string ProviderKind { get; init; } = string.Empty;

    public bool IsBundled { get; init; }

    public bool IsEnabled { get; init; }

    public bool RequiresAdmin { get; init; }

    public bool RequiresThirdPartyLicenseNotice { get; init; }

    public bool IsReadOnly { get; init; } = true;

    public SensorProviderCapabilities Capabilities { get; init; } = SensorProviderCapabilities.None;

    public string TrustLevel { get; init; } = SensorProviderTrustLevels.ExperimentalDisabled;

    public string RuntimeMode { get; init; } = SensorProviderRuntimeModes.Disabled;

    public string FailureReason { get; init; } = string.Empty;

    public DateTimeOffset LastRunUtc { get; init; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<SensorReading> Readings { get; init; } = Array.Empty<SensorReading>();

    public IReadOnlyList<string> TechnicianNotes { get; init; } = Array.Empty<string>();

    public ThirdPartyNotice? ThirdPartyNotice { get; init; }
}

public interface IHardwareSensorProvider
{
    string Name { get; }

    SensorProviderResult Read(SystemProfile profile);
}

public sealed class WindowsBuiltInSensorProvider : IHardwareSensorProvider
{
    public string Name => "Windows Native";

    public SensorProviderResult Read(SystemProfile profile)
    {
        _ = profile;
        return new SensorProviderResult
        {
            ProviderName = Name,
            ProviderVersion = "1.0",
            ProviderKind = "WindowsBuiltInSensorProvider",
            IsEnabled = true,
            IsBundled = true,
            IsReadOnly = true,
            TrustLevel = SensorProviderTrustLevels.BuiltInWindows,
            RuntimeMode = SensorProviderRuntimeModes.DefaultSafe,
            Capabilities = new SensorProviderCapabilities
            {
                SupportedCapabilities =
                [
                    "WMI/CIM hardware inventory",
                    "Storage reliability counters where Windows exposes them",
                    "powercfg/Win32_Battery battery fields",
                    "Performance counters where safe",
                    "GPU inventory through Windows APIs",
                    "Security posture APIs",
                    "ForgerEMS USB Intelligence evidence"
                ],
                MissingCapabilities =
                [
                    "CPU package temperature on many systems",
                    "GPU temperature on many systems",
                    "Fan RPM without vendor/deep provider support",
                    "Package power without vendor/deep provider support"
                ],
                ReadOnlyGuarantees = SensorProviderCapabilities.None.ReadOnlyGuarantees
            },
            Readings = Array.Empty<SensorReading>(),
            Notes =
            [
                "Uses ForgerEMS normalized WMI/CIM/registry/powercfg/report fields already collected by System Intelligence.",
                "Does not require internet or user-downloaded tools.",
                "Does not perform unsafe hardware probing."
            ]
        };
    }
}

public class BundledDeepSensorProvider : LibreHardwareMonitorSensorProvider
{
}

public sealed class OptionalDeepSensorProvider : BundledDeepSensorProvider
{
}

public static class SensorProviderRegistry
{
    public static IReadOnlyList<SensorProviderManifest> BuildDefaultManifests(SystemProfile profile, IReadOnlyList<SensorGroup> builtInGroups)
    {
        var providers = new IHardwareSensorProvider[]
        {
            new BundledDeepSensorProvider()
        };
        var manifests = new List<SensorProviderManifest>
        {
            CreateWindowsNativeManifest(builtInGroups)
        };
        manifests.AddRange(providers
            .Select(provider => SensorProviderHost.RunProvider(provider, profile))
            .ToArray());

        manifests.Add(CreateAdminBridgeManifest());
        manifests.Add(CreateDriverRoadmapManifest());
        return manifests;
    }

    public static bool IsBundledDeepProviderPackaged()
    {
        var providerRoot = Path.Combine(AppContext.BaseDirectory, "providers", "sensors");
        var candidates = new[]
        {
            Path.Combine(providerRoot, "LibreHardwareMonitorLib.dll"),
            Path.Combine(providerRoot, "ForgerEMS.SensorProviders.LibreHardwareMonitor.dll")
        };
        if (candidates.Any(File.Exists))
        {
            return true;
        }

        try
        {
            return typeof(LibreHardwareMonitor.Hardware.Computer).Assembly.GetName().Name is "LibreHardwareMonitorLib";
        }
        catch
        {
            return false;
        }
    }

    private static SensorProviderManifest CreateWindowsNativeManifest(IReadOnlyList<SensorGroup> builtInGroups) => new()
    {
        ProviderName = "Windows Native",
        ProviderVersion = "1.0",
        ProviderKind = "WindowsBuiltInSensorProvider",
        IsBundled = true,
        IsEnabled = true,
        IsReadOnly = true,
        TrustLevel = SensorProviderTrustLevels.BuiltInWindows,
        RuntimeMode = SensorProviderRuntimeModes.DefaultSafe,
        Capabilities = new SensorProviderCapabilities
        {
            SupportedCapabilities =
            [
                "WMI/CIM hardware inventory",
                "MSFT_PhysicalDisk and MSFT_StorageReliabilityCounter where Windows exposes them",
                "powercfg and Win32_Battery fields",
                "Safe performance counters where useful",
                "DX/WMI GPU inventory",
                "Defender/Firewall/BitLocker/TPM/Secure Boot status",
                "ForgerEMS USB Intelligence evidence"
            ],
            MissingCapabilities =
            [
                "CPU/GPU temperatures on many systems",
                "Fan RPM without vendor/deep provider support",
                "Package power without vendor/deep provider support"
            ],
            ReadOnlyGuarantees = SensorProviderCapabilities.None.ReadOnlyGuarantees
        },
        Readings = builtInGroups.SelectMany(group => group.Readings).ToArray(),
        TechnicianNotes =
        [
            "Active by default. Uses local Windows APIs and ForgerEMS reports only.",
            "No internet, cloud service, or user-downloaded sensor tool is required."
        ]
    };

    private static SensorProviderManifest CreateAdminBridgeManifest() => new()
    {
        ProviderName = "ForgerEMS Admin Sensor Bridge",
        ProviderVersion = "0.1-design",
        ProviderKind = "AdminReadOnlyBridgeShell",
        IsBundled = false,
        IsEnabled = false,
        RequiresAdmin = true,
        IsReadOnly = true,
        TrustLevel = SensorProviderTrustLevels.AdminRequired,
        RuntimeMode = SensorProviderRuntimeModes.Disabled,
        FailureReason = "Design scaffold only; not enabled in this beta.",
        Capabilities = new SensorProviderCapabilities
        {
            SupportedCapabilities =
            [
                "Future on-demand admin read-only deep scan IPC"
            ],
            MissingCapabilities =
            [
                "Signed bridge binary not included",
                "UAC opt-in not implemented"
            ],
            ReadOnlyGuarantees = SensorProviderCapabilities.None.ReadOnlyGuarantees
        },
        TechnicianNotes =
        [
            "Deep Sensor Mode may require admin access. It only reads supported sensors and does not change fan, voltage, clock, or firmware settings."
        ]
    };

    private static SensorProviderManifest CreateDriverRoadmapManifest() => new()
    {
        ProviderName = "ForgerEMS Signed Driver Provider",
        ProviderVersion = "roadmap",
        ProviderKind = "FutureReadOnlyDriver",
        IsBundled = false,
        IsEnabled = false,
        IsReadOnly = true,
        TrustLevel = SensorProviderTrustLevels.ExperimentalDisabled,
        RuntimeMode = SensorProviderRuntimeModes.Disabled,
        FailureReason = "Not included. Future releases would require Microsoft driver signing and installer-managed distribution.",
        Capabilities = new SensorProviderCapabilities
        {
            SupportedCapabilities =
            [
                "Future read-only sensors unavailable to user-mode providers"
            ],
            MissingCapabilities =
            [
                "No driver included in current beta"
            ],
            ReadOnlyGuarantees = SensorProviderCapabilities.None.ReadOnlyGuarantees
        },
        TechnicianNotes =
        [
            "Driver path is documentation-only for this beta. Users do not need to download it separately."
        ]
    };
}

public static class SensorProviderHost
{
    public static SensorProviderManifest RunProvider(IHardwareSensorProvider provider, SystemProfile profile)
    {
        try
        {
            var result = provider.Read(profile);
            return FromResult(result);
        }
        catch (Exception ex)
        {
            return new SensorProviderManifest
            {
                ProviderName = provider.Name,
                ProviderVersion = "unknown",
                ProviderKind = provider.GetType().Name,
                IsEnabled = false,
                IsReadOnly = true,
                RuntimeMode = SensorProviderRuntimeModes.Disabled,
                FailureReason = $"Provider probe failed safely: {ex.Message}",
                TechnicianNotes =
                [
                    "Provider failure was contained; missing sensor data is a coverage limitation, not hardware failure."
                ]
            };
        }
    }

    private static SensorProviderManifest FromResult(SensorProviderResult result) => new()
    {
        ProviderName = result.ProviderName,
        ProviderVersion = result.ProviderVersion,
        ProviderKind = result.ProviderKind,
        IsBundled = result.IsBundled,
        IsEnabled = result.IsEnabled,
        RequiresAdmin = result.RequiresAdmin,
        RequiresThirdPartyLicenseNotice = result.RequiresThirdPartyLicenseNotice,
        IsReadOnly = result.IsReadOnly,
        TrustLevel = result.TrustLevel,
        RuntimeMode = result.RuntimeMode,
        Capabilities = result.Capabilities,
        FailureReason = result.FailureReason,
        LastRunUtc = result.LastRunUtc,
        Readings = result.Readings,
        TechnicianNotes = result.Notes,
        ThirdPartyNotice = result.ThirdPartyNotice ?? (result is { RequiresThirdPartyLicenseNotice: true } ? TryGetThirdPartyNotice(result) : null)
    };

    private static ThirdPartyNotice? TryGetThirdPartyNotice(SensorProviderResult result)
    {
        // The bundled deep-provider shell exposes the notice directly through a deterministic provider result.
        return result.ProviderKind.Contains("LibreHardwareMonitor", StringComparison.OrdinalIgnoreCase)
            ? new ThirdPartyNotice
            {
                Name = "LibreHardwareMonitor",
                Version = "0.9.6",
                License = "MPL-2.0",
                ProjectUrl = "https://github.com/LibreHardwareMonitor/LibreHardwareMonitor",
                BundledPath = "providers/sensors/LibreHardwareMonitorLib.dll",
                SourceOfferOrNotice = "ForgerEMS uses the unmodified NuGet package LibreHardwareMonitorLib and ships MPL-2.0 notices with installed and portable builds.",
                ModifiedFilesDisclosureNeeded = false
            }
            : null;
    }
}

public static class MachineClassifier
{
    public static MachineClassResult Classify(SystemProfile? profile)
    {
        if (profile is null)
        {
            return new MachineClassResult();
        }

        var text = $"{profile.Manufacturer} {profile.Model}".Trim();
        var gpuText = string.Join(" ", profile.Gpus.Select(g => $"{g.Name} {g.GpuKind}"));
        var cpu = profile.Cpu ?? string.Empty;
        var ram = profile.RamTotalGb ?? 0;
        var hasBattery = profile.Batteries.Count > 0;
        var isLaptopLine = Matches(text, "latitude|thinkpad|elitebook|probook|zbook|precision|inspiron|pavilion|ideapad|xps|legion|rog|tuf|omen|victus|nitro|predator|notebook|laptop|surface");
        var isLaptop = hasBattery || isLaptopLine;
        var hasWorkstationGpu = Matches(gpuText, "quadro|rtx\\s*a\\d|radeon\\s+pro|firepro");
        var hasGamingGpu = Matches(gpuText, "geforce|gtx|rtx|radeon\\s+rx");
        var scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Business Laptop"] = 0,
            ["Consumer Laptop"] = 0,
            ["Gaming Laptop"] = 0,
            ["Mobile Workstation"] = 0,
            ["Desktop Workstation"] = 0,
            ["Desktop PC"] = 0,
            ["Mini PC"] = 0,
            ["All-in-One"] = 0,
            ["Server / Homelab"] = 0,
            ["Repair / Parts Machine"] = 0
        };
        var signals = new List<MachineClassSignal>();

        AddSignal("OEM/model line", text, 10, "SystemProfile.Manufacturer/Model", signals);
        if (isLaptop)
        {
            Add(scores, "Business Laptop", 10);
            Add(scores, "Consumer Laptop", 8);
            Add(scores, "Mobile Workstation", 8);
            AddSignal("Battery/mobile chassis signal", hasBattery ? "Battery present" : "Laptop model-line hint", 12, "SystemProfile.Batteries/Model", signals);
        }
        else
        {
            Add(scores, "Desktop PC", 18);
            AddSignal("No battery signal", "No battery exposed; likely desktop/mini/server class unless all-in-one chassis says otherwise.", 8, "SystemProfile.Batteries", signals);
        }

        if (Matches(text, "precision|zbook|thinkpad\\s*p|thinkpadp|p\\d{2}\\b") || hasWorkstationGpu)
        {
            Add(scores, isLaptop ? "Mobile Workstation" : "Desktop Workstation", 48);
            AddSignal("Workstation signal", hasWorkstationGpu ? "Workstation GPU/model line" : "Workstation OEM model line", 48, "GPU/Model heuristic", signals);
        }

        if (Matches(text, "latitude|thinkpad\\s*[tx]|elitebook|probook|surface\\s+pro|xps"))
        {
            Add(scores, "Business Laptop", 38);
            AddSignal("Business-class OEM line", text, 38, "Model heuristic", signals);
        }

        if (Matches(text, "omen|legion|rog|tuf|victus|nitro|predator|alienware|razer|msi") || (isLaptop && hasGamingGpu && !hasWorkstationGpu))
        {
            Add(scores, "Gaming Laptop", 42);
            AddSignal("Gaming signal", hasGamingGpu ? gpuText : text, 42, "GPU/Model heuristic", signals);
        }

        if (Matches(text, "inspiron|pavilion|ideapad|vivobook|aspire|envy"))
        {
            Add(scores, "Consumer Laptop", 34);
            AddSignal("Consumer OEM line", text, 34, "Model heuristic", signals);
        }

        if (Matches(text, "optiplex|elitedesk|thinkcentre|prodesk|vostro") && !isLaptop)
        {
            Add(scores, "Desktop PC", 30);
            Add(scores, "Server / Homelab", ram >= 32 ? 12 : 6);
            AddSignal("Business desktop OEM line", text, 30, "Model heuristic", signals);
        }

        if (Matches(text, "mini|micro|tiny|nuc|deskmini|beelink|minisforum"))
        {
            Add(scores, "Mini PC", 58);
            AddSignal("Mini PC line/chassis hint", text, 44, "Model heuristic", signals);
        }

        if (Matches(text, "all.in.one|aio|inspiron\\s+one|ideacentre\\s+aio|pavilion\\s+all"))
        {
            Add(scores, "All-in-One", 44);
            AddSignal("All-in-one model hint", text, 44, "Model heuristic", signals);
        }

        if (Matches(cpu, "xeon|epyc") || ram >= 64 || profile.Disks.Count >= 3)
        {
            Add(scores, "Server / Homelab", 28);
            AddSignal("Server/homelab signal", $"{cpu}; {ram:0.#} GB RAM; {profile.Disks.Count} disk(s)", 28, "CPU/RAM/storage heuristic", signals);
        }

        if (profile.OverallStatus.Contains("critical", StringComparison.OrdinalIgnoreCase) ||
            profile.DiskStatus.Contains("critical", StringComparison.OrdinalIgnoreCase) ||
            profile.BatteryStatus.Contains("critical", StringComparison.OrdinalIgnoreCase))
        {
            Add(scores, "Repair / Parts Machine", 30);
            AddSignal("Repair signal", $"{profile.OverallStatus}; {profile.DiskStatus}; {profile.BatteryStatus}", 30, "Health status", signals);
        }

        if (ram >= 32)
        {
            Add(scores, "Mobile Workstation", isLaptop ? 8 : 0);
            Add(scores, "Desktop Workstation", isLaptop ? 0 : 8);
            Add(scores, "Server / Homelab", 8);
            AddSignal("High RAM capacity", $"{ram:0.#} GB", 8, "SystemProfile.RamTotalGb", signals);
        }

        var ranked = scores
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .ToArray();
        var best = ranked.FirstOrDefault();
        var primary = best.Value >= 24 ? best.Key : "Unknown / Mixed";
        var secondary = ranked
            .Where(pair => pair.Key != primary && pair.Value >= Math.Max(18, best.Value - 14))
            .Select(pair => pair.Key)
            .Take(3)
            .ToArray();
        var confidence = best.Value >= 58 ? "High" : best.Value >= 34 ? "Medium" : "Low";
        var note = primary switch
        {
            "Mobile Workstation" => "Classified as a mobile workstation because workstation model/GPU/RAM signals dominate.",
            "Gaming Laptop" => "Classified as a gaming laptop only when gaming model/GPU signals dominate.",
            "Business Laptop" => "Business-class laptop signals are stronger than consumer/gaming signals.",
            "Consumer Laptop" => "Consumer laptop model-line signals dominate.",
            "Mini PC" => "Mini/micro chassis signals dominate.",
            "Server / Homelab" => "Server/homelab signals come from CPU/RAM/storage layout; verify chassis and cooling manually.",
            _ => "Signals are mixed or incomplete; verify chassis/model manually."
        };

        return new MachineClassResult
        {
            PrimaryClass = primary,
            SecondaryClasses = secondary,
            Confidence = confidence,
            Signals = signals
                .OrderByDescending(signal => signal.Weight)
                .Take(8)
                .ToArray(),
            TechnicianNote = note
        };
    }

    private static void Add(IDictionary<string, int> scores, string key, int amount)
    {
        if (scores.ContainsKey(key))
        {
            scores[key] += amount;
        }
    }

    private static void AddSignal(string name, string value, int weight, string source, IList<MachineClassSignal> signals)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            signals.Add(new MachineClassSignal
            {
                Name = name,
                Value = value,
                Weight = weight,
                Source = source
            });
        }
    }

    private static bool Matches(string text, string pattern) =>
        Regex.IsMatch(text ?? string.Empty, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}

public static class SensorMatrixBuilder
{
    public static SensorMatrixResult Build(SystemProfile? profile)
    {
        var now = DateTimeOffset.UtcNow;
        if (profile is null)
        {
            return new SensorMatrixResult
            {
                Confidence = "Low",
                DeepSensorMode = DeepSensorModeResolver.Resolve(),
                Groups =
                [
                    Group("System", now, Unavailable("System profile", "System", "Run System Intelligence first.", "NotApplicable", now))
                ]
            };
        }

        var groups = new[]
        {
            BuildCpu(profile, now),
            BuildGpu(profile, now),
            BuildBattery(profile, now),
            BuildStorage(profile, now),
            BuildMemory(profile, now),
            BuildNetwork(profile, now),
            BuildUsb(profile, now),
            BuildSecurity(profile, now),
            BuildCooling(profile, now)
        };
        var known = groups.Sum(g => g.KnownFields);
        var total = groups.Sum(g => g.TotalFields);
        var confidenceRatio = total == 0 ? 0 : known / (double)total;

        return new SensorMatrixResult
        {
            Groups = groups,
            DeepSensorMode = DeepSensorModeResolver.Resolve(),
            SensorProviders = SensorProviderRegistry.BuildDefaultManifests(profile, groups),
            Confidence = confidenceRatio >= 0.7 ? "High" : confidenceRatio >= 0.45 ? "Medium" : "Low"
        };
    }

    private static SensorGroup BuildCpu(SystemProfile p, DateTimeOffset now)
    {
        var readings = new List<SensorReading>
        {
            Known("CPU model", "CPU", p.Cpu, string.Empty, "WMI/CIM Win32_Processor", now, "Processor identity is inventory data, not a live sensor."),
            KnownIf("CPU cores", "CPU", p.CpuCores?.ToString(CultureInfo.InvariantCulture), "cores", "WMI/CIM Win32_Processor.NumberOfCores", now),
            KnownIf("CPU logical processors", "CPU", p.CpuThreads?.ToString(CultureInfo.InvariantCulture), "threads", "WMI/CIM Win32_Processor.NumberOfLogicalProcessors", now),
            Unavailable("CPU temperature", "CPU", "Windows did not expose CPU package temperature in the normalized scan. Deep sensor mode/vendor driver may be required.", "RequiresExternalProvider", now),
            Unavailable("CPU package power", "CPU", "Package power is usually not exposed through safe built-in Windows inventory.", "RequiresExternalProvider", now),
            Unavailable("CPU throttling", "CPU", "Thermal throttling needs live counters or a reviewed sensor provider.", "RequiresExternalProvider", now)
        };
        return Group("CPU", now, readings.ToArray());
    }

    private static SensorGroup BuildGpu(SystemProfile p, DateTimeOffset now)
    {
        var readings = new List<SensorReading>();
        if (p.Gpus.Count == 0)
        {
            readings.Add(Unavailable("GPU inventory", "GPU", "No GPU list was exposed in the scan.", "NotExposedByFirmware", now));
        }
        else
        {
            foreach (var gpu in p.Gpus.Take(3))
            {
                readings.Add(Known("GPU", "GPU", gpu.Name, string.Empty, "WMI/CIM Win32_VideoController", now, $"Kind: {gpu.GpuKind}; driver: {gpu.DriverVersion}"));
            }
        }

        readings.Add(Unavailable("GPU temperature", "GPU", "GPU temperature is not exposed by standard WMI on many systems.", "RequiresVendorDriver", now));
        readings.Add(Unavailable("GPU clocks/load", "GPU", "GPU clocks/load need driver counters or optional deep sensor provider.", "RequiresExternalProvider", now));
        readings.Add(Unavailable("GPU VRAM detail", "GPU", "VRAM was not normalized by this scan; ForgerEMS will infer lightly from model only.", "NotExposedByFirmware", now));
        return Group("GPU", now, readings.ToArray());
    }

    private static SensorGroup BuildBattery(SystemProfile p, DateTimeOffset now)
    {
        var readings = new List<SensorReading>();
        if (p.Batteries.Count == 0)
        {
            readings.Add(Unavailable("Battery", "Battery", "No battery was exposed. This is normal for desktops/mini PCs.", "NotApplicable", now));
            return Group("Battery", now, readings.ToArray());
        }

        var battery = p.Batteries[0];
        readings.Add(Known("Battery charge", "Battery", battery.ChargePercent?.ToString(CultureInfo.InvariantCulture) ?? "Unknown", "%", "Win32_Battery / powercfg", now, "Charge can be live-ish but may lag Windows reporting.", isLive: battery.ChargePercent.HasValue));
        readings.Add(KnownIf("Battery wear", "Battery", battery.WearPercent?.ToString("0.#", CultureInfo.InvariantCulture), "%", "powercfg /batteryreport", now, unavailableReason: "NotExposedByFirmware", note: "Firmware/Windows did not expose design/full-charge capacity; do not treat as failure."));
        readings.Add(KnownIf("Battery cycle count", "Battery", battery.CycleCount?.ToString(CultureInfo.InvariantCulture), "cycles", "powercfg /batteryreport / vendor firmware", now, unavailableReason: "NotExposedByFirmware", note: "Cycle count is often hidden by firmware."));
        readings.Add(KnownIf("AC connected", "Battery", battery.AcConnected?.ToString(), string.Empty, "Win32_Battery", now));
        readings.Add(Unavailable("Battery discharge rate", "Battery", "Discharge rate was not normalized by the safe scan.", "NotExposedByFirmware", now));
        return Group("Battery", now, readings.ToArray());
    }

    private static SensorGroup BuildStorage(SystemProfile p, DateTimeOffset now)
    {
        var readings = new List<SensorReading>();
        if (p.Disks.Count == 0)
        {
            readings.Add(Unavailable("Storage inventory", "Storage", "No storage devices were exposed in the scan.", "ProbeFailed", now));
            return Group("Storage", now, readings.ToArray());
        }

        foreach (var disk in p.Disks.Take(4))
        {
            readings.Add(Known("Disk", "Storage", $"{disk.Name} {disk.Size} {disk.MediaType}".Trim(), string.Empty, "MSFT_PhysicalDisk / Win32_DiskDrive", now, $"Health: {disk.Health}; status: {disk.Status}"));
            readings.Add(KnownIf($"{disk.Name} temperature", "Storage", disk.TemperatureC?.ToString("0.#", CultureInfo.InvariantCulture), "C", "SMART/NVMe health where exposed", now, unavailableReason: "NotExposedByFirmware"));
            readings.Add(KnownIf($"{disk.Name} wear", "Storage", disk.WearPercent?.ToString("0.#", CultureInfo.InvariantCulture), "%", "SMART/NVMe wear where exposed", now, unavailableReason: "NotExposedByFirmware"));
        }

        readings.Add(Unavailable("ForgerEMS storage benchmark", "Storage", "Benchmark appears in USB Builder when measured against a safe USB target.", "NotApplicable", now));
        return Group("Storage", now, readings.ToArray());
    }

    private static SensorGroup BuildMemory(SystemProfile p, DateTimeOffset now)
    {
        var readings = new[]
        {
            Known("RAM amount", "RAM", p.RamTotal, string.Empty, "Win32_PhysicalMemory / summary", now),
            KnownIf("RAM speed", "RAM", NullIfUnknown(p.RamSpeed), "MT/s", "Win32_PhysicalMemory.Speed", now, unavailableReason: "NotExposedByFirmware"),
            KnownIf("RAM slots free", "RAM", p.RamSlotsFree?.ToString(CultureInfo.InvariantCulture), "slots", "Win32_PhysicalMemoryArray / slot summary", now, unavailableReason: "NotExposedByFirmware")
        };
        return Group("RAM", now, readings);
    }

    private static SensorGroup BuildNetwork(SystemProfile p, DateTimeOffset now)
    {
        var readings = new[]
        {
            Known("Internet connectivity", "Network", p.InternetCheck ? "Working" : "Not confirmed", string.Empty, "Connectivity probe/default-route summary", now, p.InternetCheck ? "Route/DNS/connectivity probe indicates internet." : "No positive internet confirmation from the scan."),
            Known("Physical adapters", "Network", p.PhysicalNetworkAdapterCount.ToString(CultureInfo.InvariantCulture), "adapters", "Get-NetAdapter / Win32_NetworkAdapter", now),
            Known("Virtual adapters", "Network", p.VirtualNetworkAdapterCount.ToString(CultureInfo.InvariantCulture), "adapters", "Get-NetAdapter / classification", now),
            Unavailable("Wi-Fi signal/generation", "Network", "Wi-Fi generation/signal is only shown when Windows exposes adapter details in the scan.", "NotExposedByFirmware", now),
            Unavailable("Network link speed", "Network", "Link speed is not present in the normalized profile yet.", "NotExposedByFirmware", now)
        };
        return Group("Network", now, readings);
    }

    private static SensorGroup BuildUsb(SystemProfile p, DateTimeOffset now)
    {
        _ = p;
        var readings = new[]
        {
            Unavailable("USB controller inventory", "USB", "USB controller/device speed details are collected by USB Intelligence when a target is selected.", "NotApplicable", now),
            Unavailable("USB port speed", "USB", "Select a USB target and run USB Intelligence/benchmark for port speed evidence.", "NotApplicable", now),
            Unavailable("USB benchmark", "USB", "USB read/write benchmark appears only after a safe target benchmark is run.", "NotApplicable", now)
        };
        return Group("USB", now, readings);
    }

    private static SensorGroup BuildSecurity(SystemProfile p, DateTimeOffset now)
    {
        var readings = new[]
        {
            KnownIf("TPM", "Security", p.TpmStatus, string.Empty, "Get-Tpm / WMI fallback", now, unavailableReason: "NotExposedByFirmware", note: "Unknown TPM state should be verified in BIOS/UEFI before calling it failed."),
            KnownIf("Secure Boot", "Security", p.SecureBootStatus, string.Empty, "Confirm-SecureBootUEFI / registry fallback", now, unavailableReason: "PermissionDenied", note: "Unknown Secure Boot state does not prove disabled."),
            Unavailable("BitLocker", "Security", "BitLocker status is reported in the full security section when exposed.", "NotExposedByFirmware", now),
            Unavailable("Defender live state", "Security", "Defender state is collected in the security scan; live telemetry is not treated as a hardware sensor.", "NotApplicable", now)
        };
        return Group("Security", now, readings);
    }

    private static SensorGroup BuildCooling(SystemProfile p, DateTimeOffset now)
    {
        _ = p;
        var readings = new[]
        {
            Unavailable("Fan RPM", "Cooling", "Windows/firmware did not expose fan RPM in the safe scan. That does not mean the fan is broken.", "RequiresVendorDriver", now),
            Unavailable("Fan curve/control", "Cooling", "ForgerEMS does not change fan control. Vendor tools may expose this separately.", "UnsupportedHardware", now)
        };
        return Group("Cooling", now, readings);
    }

    private static SensorGroup Group(string category, DateTimeOffset now, params SensorReading[] readings)
    {
        _ = now;
        var total = readings.Length;
        var known = readings.Count(r => !r.IsUnavailable);
        return new SensorGroup
        {
            Category = category,
            KnownFields = known,
            TotalFields = total,
            Summary = $"{known}/{total} fields known",
            Readings = readings
        };
    }

    private static SensorReading Known(string name, string category, string value, string unit, string source, DateTimeOffset now, string note = "", bool isLive = false) => new()
    {
        Name = name,
        Category = category,
        Value = string.IsNullOrWhiteSpace(value) ? "Unknown" : value,
        Unit = unit,
        Status = "Ready",
        Confidence = string.IsNullOrWhiteSpace(value) || value.Contains("unknown", StringComparison.OrdinalIgnoreCase) ? "Low" : "High",
        Source = source,
        LastUpdatedUtc = now,
        IsLive = isLive,
        TechnicianNote = note
    };

    private static SensorReading KnownIf(string name, string category, string? value, string unit, string source, DateTimeOffset now, string unavailableReason = "NotExposedByFirmware", string note = "")
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase))
        {
            return Unavailable(name, category, string.IsNullOrWhiteSpace(note) ? "Windows/firmware did not expose this field." : note, unavailableReason, now, source);
        }

        return Known(name, category, value, unit, source, now, note);
    }

    private static SensorReading Unavailable(string name, string category, string note, string reason, DateTimeOffset now, string source = "ForgerEMS normalized safe scan") => new()
    {
        Name = name,
        Category = category,
        Value = "Not exposed",
        Status = reason is "NotApplicable" ? "NotExposed" : "Unknown",
        Confidence = "Low",
        Source = source,
        LastUpdatedUtc = now,
        IsUnavailable = true,
        UnavailableReason = reason,
        TechnicianNote = note
    };

    private static string? NullIfUnknown(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase) ? null : value;
}
