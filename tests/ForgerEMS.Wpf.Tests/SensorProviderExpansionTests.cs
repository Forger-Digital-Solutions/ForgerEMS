using System.Linq;
using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

// v1.2.3 hardware/sensor provider expansion: typed data-class matrix + optional
// safe Windows-native probes (ACPI thermal zones, nvidia-smi when present).
// Coverage here is intentionally focused on safety and honesty — no test
// requires sensor data to actually exist, only that providers report it
// truthfully when it does not.
public sealed class SensorProviderExpansionTests
{
    private static SystemProfile MinimalProfile() => new()
    {
        Manufacturer = "Dell",
        Model = "Precision 5540",
        OperatingSystem = "Windows 11 Pro",
        Cpu = "Intel Core i7-9850H",
        CpuCores = 6,
        CpuThreads = 12,
        RamTotal = "32 GB",
        RamTotalGb = 32,
        Gpus = [new SystemGpuProfile { Name = "NVIDIA Quadro T2000", GpuKind = "Dedicated" }],
        Disks = [new SystemDiskProfile { Name = "NVMe", MediaType = "NVMe SSD", Size = "1 TB", Health = "Healthy", Status = "OK" }],
        Batteries = [new SystemBatteryProfile { Name = "Internal", ChargePercent = 80, Status = "OK" }],
        NetworkStatus = "OK",
        InternetCheck = true,
        PhysicalNetworkAdapterCount = 1,
        TpmStatus = "Unknown",
        SecureBootStatus = "Unknown",
        OverallStatus = "OK",
        DiskStatus = "OK",
        BatteryStatus = "OK"
    };

    [Fact]
    public void CapabilityMatrix_WindowsNative_IncludesTypedDataClasses()
    {
        var sensors = SensorMatrixBuilder.Build(MinimalProfile());
        var windows = Assert.Single(sensors.SensorProviders, p => p.ProviderName == "Forger Sensor Core");

        Assert.NotEmpty(windows.Capabilities.DataClasses);
        Assert.Contains(windows.Capabilities.DataClasses, d => d.DataClass == SensorDataClass.CpuLoad && d.Status == SensorDataClassStatus.Available);
        Assert.Contains(windows.Capabilities.DataClasses, d => d.DataClass == SensorDataClass.CpuTemperature && d.Status == SensorDataClassStatus.NotExposed);
        Assert.Contains(windows.Capabilities.DataClasses, d => d.DataClass == SensorDataClass.FanRpm && d.Status == SensorDataClassStatus.NotExposed);
    }

    [Fact]
    public void CapabilityMatrix_LibreHardwareMonitor_DocumentsExpectedAvailableClassesEvenWhenDisabled()
    {
        // The advertised data-class coverage is a static contract — what LHM
        // CAN expose. Runtime availability still depends on Deep Sensor Mode
        // and per-machine sensor reachability.
        var sensors = SensorMatrixBuilder.Build(MinimalProfile());
        var lhm = Assert.Single(sensors.SensorProviders, p => p.ProviderName == "LibreHardwareMonitor");

        Assert.NotEmpty(lhm.Capabilities.DataClasses);
        Assert.Contains(lhm.Capabilities.DataClasses, d => d.DataClass == SensorDataClass.CpuTemperature && d.Status == SensorDataClassStatus.Available);
        Assert.Contains(lhm.Capabilities.DataClasses, d => d.DataClass == SensorDataClass.GpuTemperature && d.Status == SensorDataClassStatus.Available);
        Assert.Contains(lhm.Capabilities.DataClasses, d => d.DataClass == SensorDataClass.FanRpm && d.Status == SensorDataClassStatus.Available);
        Assert.Contains(lhm.Capabilities.DataClasses, d => d.DataClass == SensorDataClass.BoardSensors && d.Status == SensorDataClassStatus.Available);
        // Things LHM does not own — must be marked NotExposed/NotApplicable, not Available.
        Assert.Contains(lhm.Capabilities.DataClasses, d => d.DataClass == SensorDataClass.StorageSmart && d.Status != SensorDataClassStatus.Available);
        Assert.Contains(lhm.Capabilities.DataClasses, d => d.DataClass == SensorDataClass.BatteryWear && d.Status != SensorDataClassStatus.Available);
    }

    [Fact]
    public void AcpiThermalZoneProvider_ReportsHonestlyOnSystemsWithoutZones()
    {
        // We can't guarantee any given test machine has thermal zones, so the
        // contract is: never throw, never lie, always set IsReadOnly + a
        // NotExposed-style data-class status when nothing is exposed.
        var provider = new AcpiThermalZoneSensorProvider();
        var result = provider.Read(MinimalProfile());

        Assert.True(result.IsReadOnly);
        Assert.False(result.RequiresAdmin);
        Assert.False(result.RequiresThirdPartyLicenseNotice);
        Assert.Equal(SensorProviderTrustLevels.BuiltInWindows, result.TrustLevel);

        // Either it found zones and is Active, or it didn't and is honestly Disabled.
        if (result.IsEnabled)
        {
            Assert.NotEmpty(result.Readings);
            Assert.All(result.Readings, r => Assert.Equal("Cooling", r.Category));
            Assert.All(result.Readings, r => Assert.Equal("C", r.Unit));
            Assert.Contains(result.Capabilities.DataClasses, d => d.DataClass == SensorDataClass.ThermalZone && d.Status == SensorDataClassStatus.Available);
        }
        else
        {
            Assert.Empty(result.Readings);
            Assert.Equal(SensorProviderRuntimeModes.Disabled, result.RuntimeMode);
            Assert.Contains(result.Capabilities.DataClasses, d =>
                d.DataClass == SensorDataClass.ThermalZone &&
                d.Status is SensorDataClassStatus.NotExposed or SensorDataClassStatus.ProviderUnavailable or SensorDataClassStatus.PermissionRequired);
        }
    }

    [Fact]
    public void NvidiaSmiProvider_NotDetected_ReportsNotPackaged_DoesNotInstallAnything()
    {
        // Path resolver overridden to simulate a machine without nvidia-smi.
        var provider = new NvidiaSmiSensorProvider
        {
            PathResolverOverride = () => null
        };

        var result = provider.Read(MinimalProfile());

        Assert.False(result.IsEnabled);
        Assert.False(result.IsBundled);
        Assert.False(result.RequiresAdmin);
        Assert.True(result.IsReadOnly);
        Assert.Equal(SensorProviderRuntimeModes.Disabled, result.RuntimeMode);
        Assert.Contains("does not bundle", result.Notes[0], System.StringComparison.OrdinalIgnoreCase);
        Assert.All(result.Capabilities.DataClasses, d =>
            Assert.Equal(SensorDataClassStatus.NotPackaged, d.Status));
    }

    [Fact]
    public void NvidiaSmiProvider_Detected_ParsesCsvAndProducesGpuReadings()
    {
        // Simulate nvidia-smi being on PATH AND returning a normal CSV row.
        var provider = new NvidiaSmiSensorProvider
        {
            PathResolverOverride = () => "C:\\fake\\nvidia-smi.exe",
            RunNvidiaSmiOverride = _ => "Quadro T2000, 52, 14, 1380, 1024\n"
        };

        var result = provider.Read(MinimalProfile());

        Assert.True(result.IsEnabled);
        Assert.True(result.IsReadOnly);
        Assert.Equal(SensorProviderTrustLevels.VendorDetected, result.TrustLevel);
        Assert.NotEmpty(result.Readings);
        Assert.Contains(result.Readings, r => r.Name.Contains("temperature") && r.Value == "52" && r.Unit == "C");
        Assert.Contains(result.Readings, r => r.Name.Contains("load") && r.Value == "14" && r.Unit == "%");
        Assert.Contains(result.Readings, r => r.Name.Contains("graphics clock") && r.Value == "1380" && r.Unit == "MHz");
        Assert.Contains(result.Readings, r => r.Name.Contains("memory used") && r.Value == "1024" && r.Unit == "MB");
        Assert.All(result.Capabilities.DataClasses, d => Assert.Equal(SensorDataClassStatus.Available, d.Status));
    }

    [Fact]
    public void NvidiaSmiProvider_RunnerThrows_IsContainedAsProviderUnavailable()
    {
        var provider = new NvidiaSmiSensorProvider
        {
            PathResolverOverride = () => "C:\\fake\\nvidia-smi.exe",
            RunNvidiaSmiOverride = _ => throw new System.InvalidOperationException("simulated runtime fault")
        };

        var result = provider.Read(MinimalProfile());

        Assert.False(result.IsEnabled);
        Assert.True(result.IsReadOnly);
        Assert.Contains("simulated runtime fault", result.FailureReason);
        Assert.Empty(result.Readings);
    }

    [Fact]
    public void SensorProviderRegistry_ListsNewOptionalProviders_WithReadOnlyContract()
    {
        var sensors = SensorMatrixBuilder.Build(MinimalProfile());
        var acpi = Assert.Single(sensors.SensorProviders, p => p.ProviderName == "ACPI Thermal Zones");
        var nv = Assert.Single(sensors.SensorProviders, p => p.ProviderName == "NVIDIA SMI");

        Assert.True(acpi.IsReadOnly);
        Assert.False(acpi.RequiresAdmin);
        Assert.True(nv.IsReadOnly);
        Assert.False(nv.RequiresAdmin);

        var combined = string.Join(" ",
            acpi.Capabilities.SupportedCapabilities
                .Concat(acpi.Capabilities.MissingCapabilities)
                .Concat(acpi.Capabilities.ReadOnlyGuarantees)
                .Concat(nv.Capabilities.SupportedCapabilities)
                .Concat(nv.Capabilities.MissingCapabilities)
                .Concat(nv.Capabilities.ReadOnlyGuarantees));

        // Banned hardware-control wording (hyphenated capability-claim forms) must NEVER appear,
        // even on new providers. The unhyphenated negations ("No fan control", "No BIOS or
        // firmware writes") are required and live alongside these guards.
        Assert.DoesNotContain("fan-control", combined, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("voltage-control", combined, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("clock-control", combined, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("overclock", combined, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("undervolt", combined, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BIOS-write capability", combined, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("firmware-write capability", combined, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No fan control", combined);
        Assert.Contains("No voltage control", combined);
        Assert.Contains("No clock control", combined);
        Assert.Contains("No BIOS or firmware writes", combined);
    }

    [Fact]
    public void SensorDataClassAvailability_PreservesDetailString_ForUiAndReport()
    {
        var record = new SensorDataClassAvailability(SensorDataClass.GpuTemperature, SensorDataClassStatus.NotExposed, "vendor driver required");
        Assert.Equal(SensorDataClass.GpuTemperature, record.DataClass);
        Assert.Equal(SensorDataClassStatus.NotExposed, record.Status);
        Assert.Equal("vendor driver required", record.Detail);
    }
}
