using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using VentoyToolkitSetup.Wpf.Configuration;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Services.Compatibility;

namespace VentoyToolkitSetup.Wpf.Services;

public class LibreHardwareMonitorSensorProvider : IHardwareSensorProvider
{
    private const string ProviderVersion = "0.9.6";
    private readonly bool? _packagedOverride;

    public LibreHardwareMonitorSensorProvider(bool? packagedOverride = null)
    {
        _packagedOverride = packagedOverride;
    }

    public string Name => "LibreHardwareMonitor";

    public SensorProviderResult Read(SystemProfile profile)
    {
        _ = profile;
        var packaged = _packagedOverride ?? SensorProviderRegistry.IsBundledDeepProviderPackaged();
        var resolution = DeepSensorModeResolver.Resolve();

        // LibreHardwareMonitor probes MSRs, SMBus, ACPI and SuperIO via
        // Windows-only ring0 hooks that Wine does not implement — calling
        // Computer.Open() under Wine has historically faulted the process.
        // Refuse to attempt it under compatibility mode and report neutrally.
        if (WineProbeGate.IsCompatibilityMode)
        {
            return BuildDisabledResult(
                packaged,
                WineProbeGate.DescribeUnsupported("LibreHardwareMonitor deep sensor probe"),
                resolution);
        }

        if (!resolution.IsEnabled)
        {
            return BuildDisabledResult(
                packaged,
                $"{resolution.TechnicianNote} Enable Read-only local sensors in Settings or set FORGEREMS_DEEP_SENSOR_MODE=ReadOnly for testing.",
                resolution);
        }

        if (!packaged)
        {
            return BuildDisabledResult(
                packaged,
                "Not packaged / unavailable — LibreHardwareMonitorLib.dll was not found under providers/sensors.",
                resolution);
        }

        var readings = new List<SensorReading>();
        var notes = new List<string>
        {
            "ForgerEMS Deep Sensor Mode is local and read-only.",
            $"Deep Sensor Mode source: {resolution.DisplaySource}.",
            "Some sensors may require admin access, vendor drivers, or firmware support.",
            "No fan, voltage, clock, BIOS, or firmware control is exposed."
        };
        if (!ProcessElevationHelper.IsRunningElevated())
        {
            notes.Add("Running ForgerEMS as administrator may improve LibreHardwareMonitor sensor coverage on some systems.");
        }
        var failures = new List<string>();
        Computer? computer = null;
        try
        {
            computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsControllerEnabled = true,
                IsNetworkEnabled = true,
                IsStorageEnabled = true
            };
            computer.Open();

            foreach (var hardware in computer.Hardware)
            {
                ReadHardware(hardware, readings, failures);
            }
        }
        catch (Exception ex)
        {
            failures.Add($"LibreHardwareMonitor probe failed safely: {ex.Message}");
        }
        finally
        {
            try
            {
                computer?.Close();
            }
            catch (Exception ex)
            {
                failures.Add($"LibreHardwareMonitor close failed safely: {ex.Message}");
            }
        }

        AddUnavailableCoverageNotes(readings);

        return new SensorProviderResult
        {
            ProviderName = Name,
            ProviderVersion = ProviderVersion,
            ProviderKind = nameof(LibreHardwareMonitorSensorProvider),
            IsEnabled = readings.Any(reading => !reading.IsUnavailable),
            IsBundled = true,
            RequiresAdmin = failures.Any(failure => failure.Contains("access", StringComparison.OrdinalIgnoreCase) ||
                                                    failure.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
                                                    failure.Contains("administrator", StringComparison.OrdinalIgnoreCase)),
            RequiresThirdPartyLicenseNotice = true,
            IsReadOnly = true,
            TrustLevel = SensorProviderTrustLevels.BundledReviewed,
            RuntimeMode = SensorProviderRuntimeModes.DeepSensorReadOnly,
            Capabilities = BuildCapabilities(),
            FailureReason = string.Join("; ", failures.Distinct(StringComparer.OrdinalIgnoreCase).Take(3)),
            Readings = readings.ToArray(),
            Notes = failures.Count == 0 ? notes : notes.Concat(failures.Take(3)).ToArray(),
            ThirdPartyNotice = BuildThirdPartyNotice()
        };
    }

    private static SensorProviderResult BuildDisabledResult(
        bool packaged,
        string reason,
        DeepSensorModeResolution? resolution = null) => new()
    {
        ProviderName = "LibreHardwareMonitor",
        ProviderVersion = ProviderVersion,
        ProviderKind = nameof(LibreHardwareMonitorSensorProvider),
        IsEnabled = false,
        IsBundled = packaged,
        RequiresThirdPartyLicenseNotice = true,
        IsReadOnly = true,
        TrustLevel = packaged ? SensorProviderTrustLevels.BundledReviewed : SensorProviderTrustLevels.ExperimentalDisabled,
        RuntimeMode = SensorProviderRuntimeModes.Disabled,
        Capabilities = BuildCapabilities(),
        FailureReason = reason,
        Notes =
        [
            packaged
                ? "LibreHardwareMonitor: bundled but disabled."
                : "LibreHardwareMonitor: not packaged in this build.",
            $"Deep Sensor Mode setting: {resolution?.Mode ?? ForgerEmsEnvironmentConfiguration.DeepSensorMode}.",
            $"Deep Sensor Mode source: {resolution?.DisplaySource ?? ForgerEmsEnvironmentConfiguration.DeepSensorModeResolution.DisplaySource}.",
            "Deep Sensor Mode is local and read-only. ForgerEMS does not control fans, voltages, clocks, BIOS, or firmware."
        ],
        ThirdPartyNotice = BuildThirdPartyNotice()
    };

    private static void ReadHardware(IHardware hardware, List<SensorReading> readings, List<string> failures)
    {
        try
        {
            hardware.Update();
            foreach (var subHardware in hardware.SubHardware)
            {
                ReadHardware(subHardware, readings, failures);
            }

            foreach (var sensor in hardware.Sensors)
            {
                var reading = TryMapSensor(hardware, sensor);
                if (reading is not null)
                {
                    readings.Add(reading);
                }
            }
        }
        catch (Exception ex)
        {
            failures.Add($"{hardware.Name}: {ex.Message}");
        }
    }

    private static SensorReading? TryMapSensor(IHardware hardware, ISensor sensor)
    {
        if (!sensor.Value.HasValue || sensor.SensorType == SensorType.Control)
        {
            return null;
        }

        var category = MapCategory(hardware, sensor);
        var unit = MapUnit(sensor.SensorType);
        var note = sensor.SensorType == SensorType.Voltage
            ? "Voltage is displayed read-only when exposed; ForgerEMS does not change voltage."
            : "Read-only deep sensor value from bundled LibreHardwareMonitor provider.";
        return new SensorReading
        {
            Name = BuildSensorName(hardware, sensor),
            Category = category,
            Value = sensor.Value.Value.ToString("0.##", CultureInfo.InvariantCulture),
            Unit = unit,
            Status = "Ready",
            Confidence = "Medium",
            Source = $"LibreHardwareMonitorLib {ProviderVersion} ({hardware.HardwareType}/{sensor.SensorType})",
            LastUpdatedUtc = DateTimeOffset.UtcNow,
            IsLive = true,
            TechnicianNote = note
        };
    }

    private static void AddUnavailableCoverageNotes(List<SensorReading> readings)
    {
        var now = DateTimeOffset.UtcNow;
        AddMissingIf(readings, "CPU temperature", "CPU", "RequiresDeepProvider", "CPU temperature was not exposed by the deep provider. This can require admin, vendor drivers, or firmware support.", now);
        AddMissingIf(readings, "GPU temperature", "GPU", "RequiresDeepProvider", "GPU temperature was not exposed by the deep provider. This can require vendor driver support.", now);
        AddMissingIf(readings, "Fan RPM", "Cooling", "RequiresVendorDriver", "Fan RPM was not exposed. That does not mean the fan is broken.", now);
    }

    private static void AddMissingIf(List<SensorReading> readings, string name, string category, string reason, string note, DateTimeOffset now)
    {
        if (readings.Any(reading =>
                reading.Category.Equals(category, StringComparison.OrdinalIgnoreCase) &&
                reading.Name.Contains(name.Split(' ')[0], StringComparison.OrdinalIgnoreCase) &&
                reading.Name.Contains(name.Split(' ')[^1], StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        readings.Add(new SensorReading
        {
            Name = name,
            Category = category,
            Value = "Not exposed",
            Status = "Unknown",
            Confidence = "Low",
            Source = $"LibreHardwareMonitorLib {ProviderVersion}",
            LastUpdatedUtc = now,
            IsUnavailable = true,
            UnavailableReason = reason,
            TechnicianNote = note
        });
    }

    private static string BuildSensorName(IHardware hardware, ISensor sensor)
    {
        var hardwareName = string.IsNullOrWhiteSpace(hardware.Name) ? hardware.HardwareType.ToString() : hardware.Name.Trim();
        var sensorName = string.IsNullOrWhiteSpace(sensor.Name) ? sensor.SensorType.ToString() : sensor.Name.Trim();
        return $"{hardwareName} {sensorName}";
    }

    private static string MapCategory(IHardware hardware, ISensor sensor)
    {
        if (sensor.SensorType == SensorType.Fan)
        {
            return "Cooling";
        }

        return hardware.HardwareType switch
        {
            HardwareType.Cpu => "CPU",
            HardwareType.GpuAmd or HardwareType.GpuIntel or HardwareType.GpuNvidia => "GPU",
            HardwareType.Memory => "RAM",
            HardwareType.Storage => "Storage",
            HardwareType.Network => "Network",
            HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.Cooler => "Cooling",
            _ => "System"
        };
    }

    private static string MapUnit(SensorType sensorType) => sensorType switch
    {
        SensorType.Temperature => "C",
        SensorType.Power => "W",
        SensorType.Clock => "MHz",
        SensorType.Load => "%",
        SensorType.Fan => "RPM",
        SensorType.Voltage => "V",
        SensorType.Data or SensorType.SmallData => "GB",
        SensorType.Throughput => "B/s",
        SensorType.Level => "%",
        SensorType.Frequency => "Hz",
        SensorType.Flow => "L/h",
        _ => string.Empty
    };

    private static SensorProviderCapabilities BuildCapabilities() => new()
    {
        SupportedCapabilities =
        [
            "Read-only CPU temperature when exposed",
            "Read-only CPU package power when exposed",
            "Read-only CPU clocks/load when exposed",
            "Read-only GPU temperature/clocks/load when exposed",
            "Read-only GPU memory/VRAM sensors when exposed",
            "Read-only fan RPM when exposed",
            "Read-only storage temperature/wear when exposed",
            "Read-only voltage display when exposed"
        ],
        MissingCapabilities =
        [
            "Sensors blocked by firmware/vendor drivers",
            "Sensors requiring admin access",
            "Unsupported hardware sensors"
        ],
        ReadOnlyGuarantees = SensorProviderCapabilities.None.ReadOnlyGuarantees,
        DataClasses =
        [
            new(SensorDataClass.CpuTemperature, SensorDataClassStatus.Available, "Available when LibreHardwareMonitor exposes a per-core or package temperature sensor."),
            new(SensorDataClass.CpuPackagePower, SensorDataClassStatus.Available, "Available when an MSR/CPU power sensor is exposed."),
            new(SensorDataClass.CpuLoad, SensorDataClassStatus.Available, "Available when LibreHardwareMonitor reports per-core or package load."),
            new(SensorDataClass.CpuClock, SensorDataClassStatus.Available, "Live clocks when exposed by the CPU sensor tree."),
            new(SensorDataClass.GpuTemperature, SensorDataClassStatus.Available, "Vendor driver dependent (NVIDIA / AMD / Intel)."),
            new(SensorDataClass.GpuLoad, SensorDataClassStatus.Available, "Vendor driver dependent."),
            new(SensorDataClass.GpuClock, SensorDataClassStatus.Available, "Vendor driver dependent."),
            new(SensorDataClass.GpuVram, SensorDataClassStatus.Available, "When a Data or SmallData sensor is exposed for the GPU."),
            new(SensorDataClass.FanRpm, SensorDataClassStatus.Available, "Available when SuperIO / EC fan tachometer is readable. Read-only only."),
            new(SensorDataClass.StorageTemperature, SensorDataClassStatus.Available, "NVMe/SATA temperature when surfaced by Storage hardware."),
            new(SensorDataClass.StorageSmart, SensorDataClassStatus.NotExposed, "Use Windows MSFT_StorageReliabilityCounter; LibreHardwareMonitor is not the SMART source."),
            new(SensorDataClass.BoardSensors, SensorDataClassStatus.Available, "Motherboard / SuperIO temperatures and voltages, read-only display only."),
            new(SensorDataClass.ThermalZone, SensorDataClassStatus.NotExposed, "Use the ACPI Thermal Zones optional probe instead."),
            new(SensorDataClass.BatteryWear, SensorDataClassStatus.NotApplicable, "Use the Windows-Native powercfg path."),
            new(SensorDataClass.BatteryCycleCount, SensorDataClassStatus.NotApplicable, "Use the Windows-Native powercfg path."),
            new(SensorDataClass.BatteryChargeRate, SensorDataClassStatus.NotApplicable, "Use Windows battery APIs.")
        ]
    };

    private static ThirdPartyNotice BuildThirdPartyNotice() => new()
    {
        Name = "LibreHardwareMonitor",
        Version = ProviderVersion,
        License = "MPL-2.0",
        ProjectUrl = "https://github.com/LibreHardwareMonitor/LibreHardwareMonitor",
        BundledPath = "providers/sensors/LibreHardwareMonitorLib.dll",
        SourceOfferOrNotice = "ForgerEMS uses the unmodified NuGet package LibreHardwareMonitorLib. MPL-covered source is available from the upstream project/repository commit published by the package.",
        ModifiedFilesDisclosureNeeded = false
    };
}
