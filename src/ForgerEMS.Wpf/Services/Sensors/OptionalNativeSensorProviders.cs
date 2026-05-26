using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using VentoyToolkitSetup.Wpf.Services.Compatibility;

namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>
/// Read-only ACPI thermal zone probe using WMI namespace root\WMI /
/// MSAcpi_ThermalZoneTemperature. This surfaces firmware-reported thermal
/// zone temperatures (commonly "CPU"/"GPU"/"SKIN") on machines where the
/// vendor exposes them through ACPI even when LibreHardwareMonitor cannot.
/// Honest NotExposed / ProviderUnavailable when the namespace is empty or
/// inaccessible. No writes, no fan/voltage/clock/BIOS interaction.
/// </summary>
public sealed class AcpiThermalZoneSensorProvider : IHardwareSensorProvider
{
    public string Name => "ACPI Thermal Zones";

    public SensorProviderResult Read(SystemProfile profile)
    {
        _ = profile;
        var readings = new List<SensorReading>();
        var notes = new List<string>
        {
            "Reads firmware-reported thermal-zone temperatures via WMI MSAcpi_ThermalZoneTemperature (root\\WMI).",
            "No fan, voltage, clock, BIOS, or firmware control is performed."
        };
        var failureReason = string.Empty;
        var status = SensorDataClassStatus.NotExposed;

        // ACPI / MSAcpi_ThermalZoneTemperature is a Windows-only WMI namespace
        // — Wine has no implementation. Skip the probe under compatibility mode
        // and report neutrally so confidence scoring is not penalized.
        if (WineProbeGate.IsWine)
        {
            failureReason = WineProbeGate.DescribeUnsupported("ACPI thermal zone probe");
            notes.Add("Windows-only probe unavailable under Wine compatibility mode.");
            return BuildAcpiUnsupportedUnderWineResult(failureReason, notes);
        }

        try
        {
            // Cancellation/timeout via ManagementScope is awkward, so we rely on the
            // namespace being fast — this probe completes in milliseconds on machines
            // where ACPI thermal zones exist and yields zero rows when they don't.
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI",
                "SELECT InstanceName, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");

            using var results = searcher.Get();
            foreach (var row in results)
            {
                try
                {
                    var instance = row["InstanceName"]?.ToString() ?? "ThermalZone";
                    // CurrentTemperature is in tenths of a kelvin per ACPI spec.
                    var raw = Convert.ToDouble(row["CurrentTemperature"], CultureInfo.InvariantCulture);
                    if (raw <= 0)
                    {
                        continue;
                    }

                    var celsius = (raw / 10.0) - 273.15;
                    if (celsius is < -40 or > 150)
                    {
                        continue;
                    }

                    readings.Add(new SensorReading
                    {
                        Name = $"ACPI thermal zone {NormalizeInstance(instance)}",
                        Category = "Cooling",
                        Value = celsius.ToString("0.#", CultureInfo.InvariantCulture),
                        Unit = "C",
                        Status = "Ready",
                        Confidence = "Medium",
                        Source = $"WMI root\\WMI MSAcpi_ThermalZoneTemperature ({instance})",
                        LastUpdatedUtc = DateTimeOffset.UtcNow,
                        IsLive = true,
                        TechnicianNote = "Firmware-reported thermal zone. Coverage depends on the OEM/ACPI table."
                    });
                }
                catch (Exception ex)
                {
                    notes.Add($"Row skipped safely: {ex.Message}");
                }
                finally
                {
                    row.Dispose();
                }
            }

            if (readings.Count > 0)
            {
                status = SensorDataClassStatus.Available;
            }
            else
            {
                failureReason = "No ACPI thermal zones were exposed by the firmware on this system.";
                notes.Add("No ACPI thermal zones found. This is common on consumer laptops where vendor utilities own the temperature reporting.");
            }
        }
        catch (ManagementException ex)
        {
            failureReason = $"Provider unavailable: {ex.Message}";
            status = SensorDataClassStatus.ProviderUnavailable;
            notes.Add("WMI MSAcpi_ThermalZoneTemperature could not be queried; treated as ProviderUnavailable. No data is fabricated.");
        }
        catch (UnauthorizedAccessException ex)
        {
            failureReason = $"Permission required: {ex.Message}";
            status = SensorDataClassStatus.PermissionRequired;
            notes.Add("Querying ACPI thermal zones required elevated permission on this system.");
        }
        catch (Exception ex)
        {
            failureReason = $"Probe failed safely: {ex.Message}";
            status = SensorDataClassStatus.ProviderUnavailable;
            notes.Add("Probe failed safely; no thermal-zone data is reported for this cycle.");
        }

        return new SensorProviderResult
        {
            ProviderName = Name,
            ProviderVersion = "1.0",
            ProviderKind = nameof(AcpiThermalZoneSensorProvider),
            IsEnabled = readings.Count > 0,
            IsBundled = true,
            RequiresAdmin = false,
            RequiresThirdPartyLicenseNotice = false,
            IsReadOnly = true,
            TrustLevel = SensorProviderTrustLevels.BuiltInWindows,
            RuntimeMode = readings.Count > 0
                ? SensorProviderRuntimeModes.DefaultSafe
                : SensorProviderRuntimeModes.Disabled,
            Capabilities = new SensorProviderCapabilities
            {
                SupportedCapabilities =
                [
                    "Read-only ACPI thermal zone temperatures when firmware exposes them"
                ],
                MissingCapabilities =
                [
                    "Zones not implemented by the OEM/ACPI table",
                    "Machines where vendor tools own thermal reporting"
                ],
                ReadOnlyGuarantees = SensorProviderCapabilities.None.ReadOnlyGuarantees,
                DataClasses =
                [
                    new(SensorDataClass.ThermalZone, status, failureReason)
                ]
            },
            FailureReason = failureReason,
            Readings = readings.ToArray(),
            Notes = notes.ToArray()
        };
    }

    private static SensorProviderResult BuildAcpiUnsupportedUnderWineResult(string failureReason, List<string> notes) => new()
    {
        ProviderName = "ACPI Thermal Zones",
        ProviderVersion = "1.0",
        ProviderKind = nameof(AcpiThermalZoneSensorProvider),
        IsEnabled = false,
        IsBundled = true,
        RequiresAdmin = false,
        RequiresThirdPartyLicenseNotice = false,
        IsReadOnly = true,
        TrustLevel = SensorProviderTrustLevels.BuiltInWindows,
        RuntimeMode = SensorProviderRuntimeModes.Disabled,
        Capabilities = new SensorProviderCapabilities
        {
            SupportedCapabilities = ["Read-only ACPI thermal zone temperatures when firmware exposes them"],
            MissingCapabilities = ["Wine has no MSAcpi_ThermalZoneTemperature implementation"],
            ReadOnlyGuarantees = SensorProviderCapabilities.None.ReadOnlyGuarantees,
            DataClasses =
            [
                new(SensorDataClass.ThermalZone, SensorDataClassStatus.ProviderUnavailable, failureReason)
            ]
        },
        FailureReason = failureReason,
        Readings = Array.Empty<SensorReading>(),
        Notes = notes.ToArray()
    };

    private static string NormalizeInstance(string instance)
    {
        if (string.IsNullOrWhiteSpace(instance))
        {
            return "ThermalZone";
        }

        // Instance names look like "ACPI\ThermalZone\TZ00_0" — keep only the suffix.
        var lastBackslash = instance.LastIndexOf('\\');
        var suffix = lastBackslash >= 0 ? instance[(lastBackslash + 1)..] : instance;
        return string.IsNullOrWhiteSpace(suffix) ? "TZ" : suffix.Trim();
    }
}

/// <summary>
/// Optional detection of <c>nvidia-smi.exe</c> already installed by the NVIDIA
/// driver. ForgerEMS does NOT bundle or auto-install this tool. If present, we
/// run a single short read-only query and parse one CSV row per GPU. If not
/// present, we report NotPackaged / NotDetected honestly.
/// </summary>
public sealed class NvidiaSmiSensorProvider : IHardwareSensorProvider
{
    public string Name => "NVIDIA SMI";

    // Test seam — production code uses the real ResolveNvidiaSmiPath.
    internal Func<string?>? PathResolverOverride { get; init; }

    internal Func<string, string?>? RunNvidiaSmiOverride { get; init; }

    public SensorProviderResult Read(SystemProfile profile)
    {
        _ = profile;

        // nvidia-smi.exe is a Windows-native binary and the Wine prefix does
        // not contain it. Skip the probe entirely so we do not spawn a
        // process that is guaranteed to fail in the Wine event log.
        if (WineProbeGate.IsWine)
        {
            return BuildNotDetected("Windows-only probe unavailable under Wine compatibility mode.");
        }

        var path = (PathResolverOverride ?? ResolveNvidiaSmiPath)();
        if (string.IsNullOrEmpty(path))
        {
            return BuildNotDetected();
        }

        string? output;
        try
        {
            output = (RunNvidiaSmiOverride ?? (p => RunNvidiaSmi(p)))(path);
        }
        catch (Exception ex)
        {
            return BuildProviderUnavailable($"nvidia-smi probe failed safely: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            return BuildProviderUnavailable("nvidia-smi returned no output.");
        }

        var readings = new List<SensorReading>();
        var now = DateTimeOffset.UtcNow;
        var rowIndex = 0;
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var cols = line.Split(',', StringSplitOptions.TrimEntries);
            if (cols.Length < 5)
            {
                continue;
            }

            var gpuLabel = string.IsNullOrWhiteSpace(cols[0]) ? $"GPU{rowIndex}" : cols[0];

            AddIfNumeric(readings, $"{gpuLabel} temperature", "GPU", cols[1], "C", "nvidia-smi temperature.gpu", now, "GPU temperature reported by NVIDIA driver.");
            AddIfNumeric(readings, $"{gpuLabel} load", "GPU", cols[2], "%", "nvidia-smi utilization.gpu", now, "GPU utilisation reported by NVIDIA driver.");
            AddIfNumeric(readings, $"{gpuLabel} graphics clock", "GPU", cols[3], "MHz", "nvidia-smi clocks.gr", now, "Graphics clock reported by NVIDIA driver.");
            AddIfNumeric(readings, $"{gpuLabel} memory used", "GPU", cols[4], "MB", "nvidia-smi memory.used", now, "VRAM in use reported by NVIDIA driver.");
            rowIndex++;
        }

        var available = readings.Count > 0;
        return new SensorProviderResult
        {
            ProviderName = Name,
            ProviderVersion = "driver",
            ProviderKind = nameof(NvidiaSmiSensorProvider),
            IsEnabled = available,
            IsBundled = false,
            RequiresAdmin = false,
            RequiresThirdPartyLicenseNotice = false,
            IsReadOnly = true,
            TrustLevel = SensorProviderTrustLevels.VendorDetected,
            RuntimeMode = available
                ? SensorProviderRuntimeModes.DefaultSafe
                : SensorProviderRuntimeModes.Disabled,
            Capabilities = new SensorProviderCapabilities
            {
                SupportedCapabilities =
                [
                    "Read-only NVIDIA GPU temperature/load/clock/memory through nvidia-smi"
                ],
                MissingCapabilities =
                [
                    "Non-NVIDIA GPUs",
                    "Systems without the NVIDIA driver installed"
                ],
                ReadOnlyGuarantees = SensorProviderCapabilities.None.ReadOnlyGuarantees,
                DataClasses =
                [
                    new(SensorDataClass.GpuTemperature, available ? SensorDataClassStatus.Available : SensorDataClassStatus.ProviderUnavailable),
                    new(SensorDataClass.GpuLoad, available ? SensorDataClassStatus.Available : SensorDataClassStatus.ProviderUnavailable),
                    new(SensorDataClass.GpuClock, available ? SensorDataClassStatus.Available : SensorDataClassStatus.ProviderUnavailable),
                    new(SensorDataClass.GpuVram, available ? SensorDataClassStatus.Available : SensorDataClassStatus.ProviderUnavailable)
                ]
            },
            FailureReason = available ? string.Empty : "nvidia-smi returned no parseable rows.",
            Readings = readings.ToArray(),
            Notes =
            [
                "nvidia-smi is part of the NVIDIA display driver and is not bundled with ForgerEMS.",
                "Used only when already present locally; the probe runs read-only with a short query.",
                "No fan, voltage, clock, BIOS, or firmware control is performed."
            ]
        };
    }

    private static SensorProviderResult BuildNotDetected(string? overrideReason = null)
    {
        var reason = overrideReason ?? "nvidia-smi not detected. ForgerEMS does not install vendor tools.";
        var missing = overrideReason is null
            ? "nvidia-smi.exe was not detected; no probe was attempted"
            : overrideReason;
        var notes = overrideReason is null
            ? new[]
            {
                "nvidia-smi is installed by the official NVIDIA driver. ForgerEMS does not bundle or download it.",
                "If you want this probe, install the standard NVIDIA driver from nvidia.com or via Windows Update."
            }
            : new[]
            {
                "Windows-only probe — ForgerEMS does not attempt to run nvidia-smi under Wine compatibility mode.",
                "On native Linux the host distro's NVIDIA driver provides its own nvidia-smi binary."
            };

        return new SensorProviderResult
        {
            ProviderName = "NVIDIA SMI",
            ProviderVersion = "driver",
            ProviderKind = nameof(NvidiaSmiSensorProvider),
            IsEnabled = false,
            IsBundled = false,
            RequiresAdmin = false,
            IsReadOnly = true,
            TrustLevel = SensorProviderTrustLevels.VendorDetected,
            RuntimeMode = SensorProviderRuntimeModes.Disabled,
            Capabilities = new SensorProviderCapabilities
            {
                SupportedCapabilities = ["Optional NVIDIA-only GPU sensors when the driver is installed"],
                MissingCapabilities = [missing],
                ReadOnlyGuarantees = SensorProviderCapabilities.None.ReadOnlyGuarantees,
                DataClasses =
                [
                    new(SensorDataClass.GpuTemperature, SensorDataClassStatus.NotPackaged, "nvidia-smi not detected on PATH or System32."),
                    new(SensorDataClass.GpuLoad, SensorDataClassStatus.NotPackaged),
                    new(SensorDataClass.GpuClock, SensorDataClassStatus.NotPackaged),
                    new(SensorDataClass.GpuVram, SensorDataClassStatus.NotPackaged)
                ]
            },
            FailureReason = reason,
            Notes = notes
        };
    }

    private static SensorProviderResult BuildProviderUnavailable(string reason) => new()
    {
        ProviderName = "NVIDIA SMI",
        ProviderVersion = "driver",
        ProviderKind = nameof(NvidiaSmiSensorProvider),
        IsEnabled = false,
        IsBundled = false,
        IsReadOnly = true,
        TrustLevel = SensorProviderTrustLevels.VendorDetected,
        RuntimeMode = SensorProviderRuntimeModes.Disabled,
        Capabilities = new SensorProviderCapabilities
        {
            SupportedCapabilities = ["Optional NVIDIA-only GPU sensors when the driver is installed"],
            MissingCapabilities = [reason],
            ReadOnlyGuarantees = SensorProviderCapabilities.None.ReadOnlyGuarantees,
            DataClasses =
            [
                new(SensorDataClass.GpuTemperature, SensorDataClassStatus.ProviderUnavailable, reason),
                new(SensorDataClass.GpuLoad, SensorDataClassStatus.ProviderUnavailable, reason),
                new(SensorDataClass.GpuClock, SensorDataClassStatus.ProviderUnavailable, reason),
                new(SensorDataClass.GpuVram, SensorDataClassStatus.ProviderUnavailable, reason)
            ]
        },
        FailureReason = reason,
        Notes =
        [
            "nvidia-smi was detected but did not return data this cycle. No data is fabricated.",
            "No fan, voltage, clock, BIOS, or firmware control is performed."
        ]
    };

    private static string? ResolveNvidiaSmiPath()
    {
        try
        {
            var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var candidate = Path.Combine(system32, "nvidia-smi.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var probe = Path.Combine(dir.Trim(), "nvidia-smi.exe");
                    if (File.Exists(probe))
                    {
                        return probe;
                    }
                }
                catch
                {
                }
            }

            // Common NVIDIA driver path.
            var programFiles = Environment.GetEnvironmentVariable("ProgramFiles") ?? "C:\\Program Files";
            var driverPath = Path.Combine(programFiles, "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
            return File.Exists(driverPath) ? driverPath : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? RunNvidiaSmi(string path)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = "--query-gpu=name,temperature.gpu,utilization.gpu,clocks.gr,memory.used --format=csv,noheader,nounits",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            if (!process.Start())
            {
                return null;
            }

            if (!process.WaitForExit(4000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return null;
            }

            return process.ExitCode == 0 ? process.StandardOutput.ReadToEnd() : null;
        }
        catch
        {
            return null;
        }
    }

    private static void AddIfNumeric(List<SensorReading> readings, string name, string category, string raw, string unit, string source, DateTimeOffset now, string note)
    {
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return;
        }

        readings.Add(new SensorReading
        {
            Name = name,
            Category = category,
            Value = value.ToString("0.#", CultureInfo.InvariantCulture),
            Unit = unit,
            Status = "Ready",
            Confidence = "Medium",
            Source = source,
            LastUpdatedUtc = now,
            IsLive = true,
            TechnicianNote = note
        });
    }
}
