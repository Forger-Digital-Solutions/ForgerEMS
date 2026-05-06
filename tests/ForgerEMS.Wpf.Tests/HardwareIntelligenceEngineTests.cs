using System.Text.Json;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Intelligence;

namespace ForgerEMS.Wpf.Tests;

public sealed class HardwareIntelligenceEngineTests
{
    [Theory]
    [InlineData("Dell", "Precision 5540", "NVIDIA Quadro T2000", "Mobile Workstation")]
    [InlineData("HP", "ZBook Studio G8", "NVIDIA RTX A2000", "Mobile Workstation")]
    [InlineData("Lenovo", "ThinkPad P1 Gen 5", "NVIDIA RTX A1000", "Mobile Workstation")]
    public void WorkstationLinesClassifyAsMobileWorkstation(string manufacturer, string model, string gpu, string expected)
    {
        var result = MachineClassifier.Classify(Profile(manufacturer, model, "Intel Core i7-9850H", 32, gpu, hasBattery: true));

        Assert.Equal(expected, result.PrimaryClass);
        Assert.True(result.Confidence is "High" or "Medium");
    }

    [Theory]
    [InlineData("Dell", "Latitude 7420")]
    [InlineData("HP", "EliteBook 840 G8")]
    [InlineData("Lenovo", "ThinkPad T14 Gen 3")]
    public void BusinessLaptopLinesClassifyAsBusinessLaptop(string manufacturer, string model)
    {
        var result = MachineClassifier.Classify(Profile(manufacturer, model, "Intel Core i5-1135G7", 16, "Intel Iris Xe", hasBattery: true));

        Assert.Equal("Business Laptop", result.PrimaryClass);
    }

    [Theory]
    [InlineData("HP", "Omen 16")]
    [InlineData("Lenovo", "Legion 5")]
    [InlineData("ASUS", "ROG Zephyrus")]
    [InlineData("Acer", "Nitro 5")]
    [InlineData("Acer", "Predator Helios")]
    public void GamingLaptopLinesClassifyAsGamingLaptop(string manufacturer, string model)
    {
        var result = MachineClassifier.Classify(Profile(manufacturer, model, "AMD Ryzen 7 6800H", 16, "NVIDIA GeForce RTX 3060", hasBattery: true));

        Assert.Equal("Gaming Laptop", result.PrimaryClass);
    }

    [Theory]
    [InlineData("Dell", "Inspiron 15")]
    [InlineData("HP", "Pavilion 14")]
    [InlineData("Lenovo", "IdeaPad 3")]
    public void ConsumerLaptopLinesClassifyAsConsumerLaptop(string manufacturer, string model)
    {
        var result = MachineClassifier.Classify(Profile(manufacturer, model, "Intel Core i3-1115G4", 8, "Intel UHD", hasBattery: true));

        Assert.Equal("Consumer Laptop", result.PrimaryClass);
    }

    [Theory]
    [InlineData("Dell", "OptiPlex 7080", "Desktop PC")]
    [InlineData("HP", "EliteDesk 800 G6", "Desktop PC")]
    [InlineData("Lenovo", "ThinkCentre M720q Tiny", "Mini PC")]
    [InlineData("Intel", "NUC 12 Pro Mini PC", "Mini PC")]
    public void DesktopAndMiniPcLinesClassifyCorrectly(string manufacturer, string model, string expected)
    {
        var result = MachineClassifier.Classify(Profile(manufacturer, model, "Intel Core i5-10500T", 16, "Intel UHD", hasBattery: false));

        Assert.Equal(expected, result.PrimaryClass);
    }

    [Fact]
    public void WorkstationGpuImprovesWorkstationFit()
    {
        var fit = new DeviceFitEngine().Evaluate(Profile("Dell", "Precision 5540", "Intel Core i7-9850H", 32, "NVIDIA Quadro T2000", hasBattery: true));

        Assert.Equal("Mobile Workstation", fit.MachineClass);
        Assert.Contains("workstation", fit.PrimaryFit, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(fit.Scores, score => score.Category == "CAD / workstation" && score.Score >= 70);
    }

    [Fact]
    public void GamingGpuImprovesGamingScore()
    {
        var fit = new DeviceFitEngine().Evaluate(Profile("Lenovo", "Legion 5", "AMD Ryzen 7 6800H", 32, "NVIDIA GeForce RTX 3060", hasBattery: true));

        Assert.Equal("Gaming Laptop", fit.MachineClass);
        Assert.Contains(fit.Scores, score => score.Category == "Medium gaming" && score.Score >= 70);
    }

    [Fact]
    public void NoDedicatedGpuPreventsHeavyGamingRecommendation()
    {
        var fit = new DeviceFitEngine().Evaluate(Profile("Dell", "Latitude 7420", "Intel Core i5-1135G7", 16, "Intel Iris Xe", hasBattery: true));

        Assert.DoesNotContain(fit.StrongFits, fitName => fitName.Contains("Heavy gaming", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(fit.WeakFits, fitName => fitName.Contains("Heavy gaming", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SensorNotExposedLowersSensorConfidenceButDoesNotCreateHealthFailure()
    {
        var profile = Profile("Dell", "Latitude 7420", "Intel Core i5-1135G7", 16, "Intel Iris Xe", hasBattery: true, batteryWearKnown: false);
        var sensors = SensorMatrixBuilder.Build(profile);
        var health = SystemHealthEvaluator.Evaluate(profile);

        Assert.Contains(sensors.Groups.SelectMany(group => group.Readings), reading => reading.Name == "Fan RPM" && reading.IsUnavailable && reading.UnavailableReason == "RequiresVendorDriver");
        Assert.DoesNotContain(health.DetectedIssues, issue => issue.Contains("fan", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BatteryCycleMissingDoesNotMarkBatteryFailed()
    {
        var profile = Profile("HP", "EliteBook 840 G8", "Intel Core i5-1135G7", 16, "Intel Iris Xe", hasBattery: true, batteryWearKnown: false);
        var sensors = SensorMatrixBuilder.Build(profile);
        var health = SystemHealthEvaluator.Evaluate(profile);

        Assert.Contains(sensors.Groups.SelectMany(group => group.Readings), reading => reading.Name == "Battery cycle count" && reading.IsUnavailable);
        Assert.DoesNotContain(health.DetectedIssues, issue => issue.Contains("battery failure", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AutomationMergeAddsMachineClassSensorMatrixAndDeviceFit()
    {
        WithDeepSensorMode(null, () =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "forgerems-hardware-intel-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var reportPath = Path.Combine(dir, "system-intelligence-latest.json");
            File.WriteAllText(reportPath, """
        {
          "summary": {
            "manufacturer": "Dell",
            "model": "Precision 5540",
            "os": "Windows 11 Pro",
            "cpu": "Intel Core i7-9850H",
            "cpuCores": 6,
            "cpuLogicalProcessors": 12,
            "ramTotal": "32 GB",
            "ramSpeed": "2666",
            "gpus": [{ "name": "NVIDIA Quadro T2000", "type": "Dedicated", "driverVersion": "1.2.3" }],
            "tpmInfo": { "status": "Unknown", "source": "Get-Tpm", "reason": "not exposed" },
            "secureBootInfo": { "status": "Unknown", "source": "Confirm-SecureBootUEFI", "reason": "not exposed" }
          },
          "disks": [{ "name": "Samsung 990 Pro", "mediaType": "NVMe SSD", "size": "1 TB", "health": "Healthy", "status": "OK" }],
          "batteries": [{ "name": "Internal Battery", "estimatedChargeRemaining": 80, "status": "UNKNOWN" }],
          "network": { "status": "OK", "internetCheck": true, "physicalAdapters": [{}], "virtualAdapters": [] },
          "overallStatus": "OK",
          "diskStatus": "OK",
          "batteryStatus": "UNKNOWN",
          "flipValue": { "confidenceScore": 0.6, "valueDrivers": [], "valueReducers": [], "suggestedUpgradeRecommendations": [] },
          "obviousProblems": [],
          "recommendations": []
        }
        """);
            File.WriteAllText(Path.Combine(dir, "usb-intelligence-latest.json"), """
        {
          "usbDiagnostics": {
            "usbProfileKnownPortsCount": 4,
            "usbCurrentTargetRiskSummary": "Current target risk: Low.",
            "usbBestKnownPortSummary": "Best measured port: LT USB-C (~60.5 MB/s write).",
            "lastBenchmark": { "succeeded": true, "summaryLine": "USB benchmark complete: Usb3", "benchmarkConfidence": "Read may be cached" }
          },
          "topologyDiff": { "summaryLine": "USB topology: unchanged since last scan." }
        }
        """);

            Assert.True(SystemIntelligenceAutomationMerger.TryMerge(reportPath));
            using var doc = JsonDocument.Parse(File.ReadAllText(reportPath));

            Assert.Equal("Mobile Workstation", doc.RootElement.GetProperty("machineClass").GetProperty("primaryClass").GetString());
            Assert.True(doc.RootElement.TryGetProperty("sensorMatrix", out var sensorMatrix));
            Assert.Contains("CPU:", sensorMatrix.GetProperty("coverageSummary").GetString());
            Assert.True(sensorMatrix.TryGetProperty("sensorProviders", out var providers));
            Assert.Contains(providers.EnumerateArray(), provider =>
                provider.GetProperty("providerName").GetString() == "Windows Native" &&
                provider.GetProperty("isEnabled").GetBoolean());
            Assert.Contains(providers.EnumerateArray(), provider =>
                provider.GetProperty("providerName").GetString() == "LibreHardwareMonitor" &&
                !provider.GetProperty("isEnabled").GetBoolean());
            Assert.DoesNotContain("USB: 0/3 fields known", sensorMatrix.GetProperty("coverageSummary").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("USB: 5/5 fields known", sensorMatrix.GetProperty("coverageSummary").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Mobile Workstation", doc.RootElement.GetProperty("deviceFit").GetProperty("machineClass").GetString());
            Assert.Contains("Scan Confidence", doc.RootElement.GetProperty("forgerAutomation").GetProperty("summaryLine").GetString());
        });
    }

    [Fact]
    public void OptionalDeepSensorProvider_IsDisabledByDefaultAndReadOnly()
    {
        WithDeepSensorMode(null, () =>
        {
            var provider = new OptionalDeepSensorProvider();
            var result = provider.Read(Profile("Dell", "Precision 5540", "Intel Core i7-9850H", 32, "NVIDIA Quadro T2000", hasBattery: true));

            Assert.False(result.IsEnabled);
            Assert.Empty(result.Readings);
            Assert.True(result.IsReadOnly);
            Assert.True(result.RequiresThirdPartyLicenseNotice);
            Assert.Equal(SensorProviderRuntimeModes.Disabled, result.RuntimeMode);
            Assert.Contains("Deep Sensor Mode is Off", result.FailureReason, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(result.Notes, note => note.Contains("read-only", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Notes, note => note.Contains("does not control fans", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Notes, note => note.Contains("voltages", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Notes, note => note.Contains("BIOS", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void SensorProviderHost_EnablesWindowsNativeByDefaultAndDoesNotRequireDownloads()
    {
        var sensors = SensorMatrixBuilder.Build(Profile("Dell", "Precision 5540", "Intel Core i7-9850H", 32, "NVIDIA Quadro T2000", hasBattery: true));
        var windows = Assert.Single(sensors.SensorProviders, provider => provider.ProviderName == "Windows Native");

        Assert.True(windows.IsEnabled);
        Assert.True(windows.IsBundled);
        Assert.False(windows.RequiresAdmin);
        Assert.False(windows.RequiresThirdPartyLicenseNotice);
        Assert.Equal(SensorProviderTrustLevels.BuiltInWindows, windows.TrustLevel);
        Assert.Equal(SensorProviderRuntimeModes.DefaultSafe, windows.RuntimeMode);
        Assert.Contains(windows.TechnicianNotes, note => note.Contains("No internet", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(windows.TechnicianNotes, note => note.Contains("user-downloaded", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(windows.Readings);
    }

    [Fact]
    public void SensorProviderRegistry_ListsBundledReviewedDeepProviderShellWithNotice()
    {
        WithDeepSensorMode(null, () =>
        {
            var sensors = SensorMatrixBuilder.Build(Profile("Dell", "Precision 5540", "Intel Core i7-9850H", 32, "NVIDIA Quadro T2000", hasBattery: true));
            var deep = Assert.Single(sensors.SensorProviders, provider => provider.ProviderName == "LibreHardwareMonitor");

            Assert.False(deep.IsEnabled);
            Assert.True(deep.IsReadOnly);
            Assert.True(deep.RequiresThirdPartyLicenseNotice);
            Assert.Equal(SensorProviderRuntimeModes.Disabled, deep.RuntimeMode);
            Assert.NotNull(deep.ThirdPartyNotice);
            Assert.Equal("LibreHardwareMonitor", deep.ThirdPartyNotice!.Name);
            Assert.Equal("0.9.6", deep.ThirdPartyNotice.Version);
            Assert.Equal("MPL-2.0", deep.ThirdPartyNotice.License);
            Assert.Equal("providers/sensors/LibreHardwareMonitorLib.dll", deep.ThirdPartyNotice.BundledPath);
        });
    }

    [Fact]
    public void LibreHardwareMonitorProvider_RequiresReadOnlyModeAndPackagedAssembly()
    {
        WithDeepSensorMode("ReadOnly", () =>
        {
            var provider = new LibreHardwareMonitorSensorProvider(packagedOverride: false);
            var result = provider.Read(Profile("Dell", "Precision 5540", "Intel Core i7-9850H", 32, "NVIDIA Quadro T2000", hasBattery: true));

            Assert.False(result.IsEnabled);
            Assert.False(result.IsBundled);
            Assert.Equal(SensorProviderRuntimeModes.Disabled, result.RuntimeMode);
            Assert.Contains("not packaged", result.FailureReason, StringComparison.OrdinalIgnoreCase);
            Assert.True(result.IsReadOnly);
            Assert.True(result.RequiresThirdPartyLicenseNotice);
        });
    }

    [Fact]
    public void LibreHardwareMonitorProvider_ReadOnlyPackagedProbeFailsSafelyOrReturnsReadings()
    {
        WithDeepSensorMode("ReadOnly", () =>
        {
            var provider = new LibreHardwareMonitorSensorProvider(packagedOverride: true);
            var result = provider.Read(Profile("Dell", "Precision 5540", "Intel Core i7-9850H", 32, "NVIDIA Quadro T2000", hasBattery: true));

            Assert.True(result.IsBundled);
            Assert.True(result.IsReadOnly);
            Assert.True(result.RequiresThirdPartyLicenseNotice);
            Assert.Equal(SensorProviderRuntimeModes.DeepSensorReadOnly, result.RuntimeMode);
            Assert.Equal(SensorProviderTrustLevels.BundledReviewed, result.TrustLevel);
            Assert.Contains(result.Capabilities.ReadOnlyGuarantees, guarantee => guarantee.Contains("No fan control", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(result.ThirdPartyNotice);
            Assert.Equal("MPL-2.0", result.ThirdPartyNotice!.License);
            Assert.DoesNotContain(result.Readings, reading => reading.Status.Equals("Failure", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void SensorProviderCapabilities_DoNotExposeUnsafeHardwareControl()
    {
        var sensors = SensorMatrixBuilder.Build(Profile("Dell", "Precision 5540", "Intel Core i7-9850H", 32, "NVIDIA Quadro T2000", hasBattery: true));
        var capabilityText = string.Join(" ", sensors.SensorProviders.SelectMany(provider =>
            provider.Capabilities.SupportedCapabilities
                .Concat(provider.Capabilities.MissingCapabilities)
                .Concat(provider.Capabilities.ReadOnlyGuarantees)));

        Assert.DoesNotContain("fan-control", capabilityText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("voltage-control", capabilityText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("clock-control", capabilityText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("overclock", capabilityText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("undervolt", capabilityText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BIOS-write capability", capabilityText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No fan control", capabilityText);
        Assert.Contains("No voltage control", capabilityText);
        Assert.Contains("No clock control", capabilityText);
        Assert.Contains("No BIOS or firmware writes", capabilityText);
    }

    [Fact]
    public void KyraAnswersMachineClassAndMissingSensorQuestions()
    {
        var profile = Profile("Dell", "Precision 5540", "Intel Core i7-9850H", 32, "NVIDIA Quadro T2000", hasBattery: true);

        Assert.True(VentoyToolkitSetup.Wpf.Services.Kyra.KyraLocalSpecAnswerBuilder.TryBuildLocalSpecAnswer(
            "What kind of machine is this?",
            profile,
            out var classAnswer));
        Assert.Contains("Mobile Workstation", classAnswer.Text);

        Assert.True(VentoyToolkitSetup.Wpf.Services.Kyra.KyraLocalSpecAnswerBuilder.TryBuildLocalSpecAnswer(
            "Why can't ForgerEMS read my fan speed?",
            profile,
            out var sensorAnswer));
        Assert.Contains("does not mean", sensorAnswer.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Unknown = lower confidence", sensorAnswer.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BackendMarkdownTemplateIncludesMachineClassAndSensorMatrix()
    {
        var path = FindRepoFile("backend", "SystemIntelligence", "Invoke-ForgerEMSSystemScan.ps1");
        var text = File.ReadAllText(path);

        Assert.Contains("New-MachineClassReport", text);
        Assert.Contains("New-SensorMatrixReport", text);
        Assert.Contains("## Machine Class / Hardware X-Ray", text);
        Assert.Contains("### Sensor Availability Matrix", text);
        Assert.Contains("New-SensorProviderReport", text);
        Assert.Contains("### Sensor Provider Host", text);
        Assert.Contains("Windows Native", text);
        Assert.Contains("LibreHardwareMonitor", text);
        Assert.Contains("providers/sensors/LibreHardwareMonitorLib.dll", text);
        Assert.Contains("ForgerEMS Admin Sensor Bridge", text);
        Assert.Contains("ForgerEMS Signed Driver Provider", text);
        Assert.Contains("$tpmSensorStatus", text, StringComparison.Ordinal);
        Assert.Contains("$secureBootSensorStatus", text, StringComparison.Ordinal);
        Assert.Contains("Unknown TPM state should be verified in BIOS/UEFI before calling it failed.", text, StringComparison.Ordinal);
    }

    private static SystemProfile Profile(
        string manufacturer,
        string model,
        string cpu,
        double ramGb,
        string gpu,
        bool hasBattery,
        bool batteryWearKnown = true) => new()
        {
            Manufacturer = manufacturer,
            Model = model,
            OperatingSystem = "Windows 11 Pro",
            OsBuild = "22631",
            Cpu = cpu,
            CpuCores = cpu.Contains("9850H", StringComparison.OrdinalIgnoreCase) ? 6 : cpu.Contains("6800H", StringComparison.OrdinalIgnoreCase) ? 8 : 4,
            CpuThreads = cpu.Contains("9850H", StringComparison.OrdinalIgnoreCase) ? 12 : cpu.Contains("6800H", StringComparison.OrdinalIgnoreCase) ? 16 : 8,
            RamTotal = $"{ramGb:0.#} GB",
            RamTotalGb = ramGb,
            RamSpeed = "3200",
            Gpus = [new SystemGpuProfile { Name = gpu, GpuKind = gpu.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? "Integrated" : "Dedicated", DriverVersion = "1.0" }],
            Disks = [new SystemDiskProfile { Name = "NVMe SSD", MediaType = "NVMe SSD", Size = "1 TB", Health = "Healthy", Status = "OK" }],
            Batteries = hasBattery
                ? [new SystemBatteryProfile { Name = "Internal Battery", ChargePercent = 80, WearPercent = batteryWearKnown ? 12 : null, CycleCount = batteryWearKnown ? 120 : null, Status = "UNKNOWN" }]
                : [],
            NetworkStatus = "OK",
            InternetCheck = true,
            PhysicalNetworkAdapterCount = 1,
            VirtualNetworkAdapterCount = 0,
            TpmStatus = "Unknown",
            SecureBootStatus = "Unknown",
            OverallStatus = "OK",
            DiskStatus = "OK",
            BatteryStatus = hasBattery ? "UNKNOWN" : "NOT_APPLICABLE"
        };

    private static string FindRepoFile(params string[] segments)
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate repo file.", Path.Combine(segments));
    }

    private static void WithDeepSensorMode(string? value, Action action)
    {
        var previous = Environment.GetEnvironmentVariable("FORGEREMS_DEEP_SENSOR_MODE");
        try
        {
            Environment.SetEnvironmentVariable("FORGEREMS_DEEP_SENSOR_MODE", value ?? "Off");
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("FORGEREMS_DEEP_SENSOR_MODE", previous);
        }
    }
}
