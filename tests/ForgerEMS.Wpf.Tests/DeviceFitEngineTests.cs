using System.Text.Json;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Intelligence;

namespace ForgerEMS.Wpf.Tests;

public sealed class DeviceFitEngineTests
{
    [Fact]
    public void WorkstationLaptopGetsDeveloperCreatorWorkstationFit()
    {
        var fit = new DeviceFitEngine().Evaluate(Precision5540());

        Assert.Equal("Developer / Creator Workstation + Light Gaming", fit.PrimaryFit);
        Assert.Contains(fit.StrongFits, item => item.Contains("Software development", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(fit.StrongFits, item => item.Contains("CAD", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("not primarily as a gaming laptop", fit.ListingPositioning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LowEndDualCoreGetsOfficePrimaryAndWeakGaming()
    {
        var fit = new DeviceFitEngine().Evaluate(new SystemProfile
        {
            Manufacturer = "Acer",
            Model = "BudgetBook",
            Cpu = "Intel Celeron N4020",
            CpuCores = 2,
            CpuThreads = 2,
            RamTotal = "4 GB",
            RamTotalGb = 4,
            Disks = [new SystemDiskProfile { Name = "SSD", MediaType = "SSD", Size = "128 GB", Health = "Healthy", Status = "READY" }],
            Gpus = [new SystemGpuProfile { Name = "Intel UHD Graphics", GpuKind = "Integrated" }]
        });

        Assert.Contains("Office", fit.PrimaryFit, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(fit.Scores, score => score.Category == "Heavy gaming" && score.Label is "Weak" or "Not Recommended");
    }

    [Fact]
    public void GamingLaptopWithRtxGetsGamingFit()
    {
        var fit = new DeviceFitEngine().Evaluate(new SystemProfile
        {
            Manufacturer = "Lenovo",
            Model = "Legion",
            Cpu = "Intel Core i7-12700H",
            CpuCores = 14,
            CpuThreads = 20,
            RamTotal = "32 GB",
            RamTotalGb = 32,
            Gpus = [new SystemGpuProfile { Name = "NVIDIA GeForce RTX 3060 Laptop GPU", GpuKind = "Dedicated" }],
            Disks = [new SystemDiskProfile { Name = "NVMe SSD", MediaType = "NVMe SSD", Size = "1 TB", Health = "Healthy", Status = "READY" }]
        });

        Assert.Contains("Gaming", fit.PrimaryFit, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("entry/mid gaming laptop", fit.ListingPositioning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoDedicatedGpuDoesNotRecommendHeavyGaming()
    {
        var fit = new DeviceFitEngine().Evaluate(new SystemProfile
        {
            Manufacturer = "Dell",
            Model = "Latitude",
            Cpu = "Intel Core i5-1135G7",
            CpuCores = 4,
            CpuThreads = 8,
            RamTotal = "16 GB",
            RamTotalGb = 16,
            Gpus = [new SystemGpuProfile { Name = "Intel Iris Xe", GpuKind = "Integrated" }],
            Disks = [new SystemDiskProfile { Name = "NVMe", MediaType = "NVMe SSD", Size = "512 GB", Health = "Healthy", Status = "READY" }]
        });

        Assert.DoesNotContain(fit.StrongFits, item => item.Contains("Heavy gaming", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(fit.WeakFits, item => item.Contains("Heavy gaming", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnknownBatteryDataLowersTravelConfidenceNotOverallUsefulness()
    {
        var fit = new DeviceFitEngine().Evaluate(Precision5540(withBatteryUnknown: true));
        var travel = fit.Scores.Single(score => score.Category == "Travel / battery use");

        Assert.Equal("Low", travel.Confidence);
        Assert.Contains(fit.WeakFits, item => item.Contains("battery", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Developer", fit.PrimaryFit, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StrongRamAndSsdImproveDevelopmentAndProductivityScores()
    {
        var weak = new DeviceFitEngine().Evaluate(new SystemProfile
        {
            Cpu = "Intel Core i5-8250U",
            CpuCores = 4,
            CpuThreads = 8,
            RamTotal = "8 GB",
            RamTotalGb = 8,
            Gpus = [new SystemGpuProfile { Name = "Intel UHD", GpuKind = "Integrated" }],
            Disks = [new SystemDiskProfile { Name = "HDD", MediaType = "HDD", Size = "500 GB", Health = "Healthy", Status = "READY" }]
        });
        var strong = new DeviceFitEngine().Evaluate(new SystemProfile
        {
            Cpu = "Intel Core i5-8250U",
            CpuCores = 4,
            CpuThreads = 8,
            RamTotal = "32 GB",
            RamTotalGb = 32,
            Gpus = [new SystemGpuProfile { Name = "Intel UHD", GpuKind = "Integrated" }],
            Disks = [new SystemDiskProfile { Name = "NVMe", MediaType = "NVMe SSD", Size = "1 TB", Health = "Healthy", Status = "READY" }]
        });

        Assert.True(Score(strong, "Software development") > Score(weak, "Software development"));
        Assert.True(Score(strong, "Office / school / general use") > Score(weak, "Office / school / general use"));
    }

    [Fact]
    public void DeviceFitResultAppearsInMergedJson()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"si-fit-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tmp, MinimalReportJson());

            Assert.True(SystemIntelligenceAutomationMerger.TryMerge(tmp));
            using var doc = JsonDocument.Parse(File.ReadAllText(tmp));

            Assert.True(doc.RootElement.TryGetProperty("deviceFit", out var fit));
            Assert.Contains("workstation", fit.GetProperty("primaryFit").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Mobile Workstation", fit.GetProperty("machineClass").GetString());
            Assert.Contains("Best use", doc.RootElement.GetProperty("forgerAutomation").GetProperty("summaryLine").GetString(), StringComparison.OrdinalIgnoreCase);
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
    public void BackendMarkdownTemplateIncludesDeviceFitSection()
    {
        var script = FindRepoFile("backend", "SystemIntelligence", "Invoke-ForgerEMSSystemScan.ps1");
        Assert.True(File.Exists(script));
        var text = File.ReadAllText(script);

        Assert.Contains("deviceFit =", text, StringComparison.Ordinal);
        Assert.Contains("## Best Use / Device Fit", text, StringComparison.Ordinal);
    }

    [Fact]
    public void KyraLocalSpecAnswerIncludesBestUseSummary()
    {
        Assert.True(VentoyToolkitSetup.Wpf.Services.Kyra.KyraLocalSpecAnswerBuilder.TryBuildLocalSpecAnswer(
            "What is this laptop best for and can it run games?",
            Precision5540(),
            out var response));

        Assert.Contains("Best use / device fit", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Developer", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Light", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inferred from scanned CPU/RAM/GPU/storage/battery signals", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    private static int Score(DeviceFitResult result, string category) =>
        result.Scores.Single(score => score.Category == category).Score;

    private static string FindRepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return Path.Combine(parts);
    }

    private static SystemProfile Precision5540(bool withBatteryUnknown = false) => new()
    {
        Manufacturer = "Dell",
        Model = "Precision 5540",
        OperatingSystem = "Windows 11 Pro",
        Cpu = "Intel Core i7-9850H",
        CpuCores = 6,
        CpuThreads = 12,
        RamTotal = "32 GB",
        RamTotalGb = 31.7,
        Gpus =
        [
            new SystemGpuProfile { Name = "Intel UHD 630", GpuKind = "Integrated" },
            new SystemGpuProfile { Name = "NVIDIA Quadro T2000", GpuKind = "Dedicated" }
        ],
        Disks = [new SystemDiskProfile { Name = "Samsung 990 Pro", MediaType = "NVMe SSD", Size = "1 TB", Health = "Healthy", Status = "READY" }],
        Batteries = withBatteryUnknown ? [new SystemBatteryProfile { Name = "Primary Battery", Status = "UNKNOWN" }] : []
    };

    private static string MinimalReportJson() =>
        """
        {
          "overallStatus":"READY",
          "diskStatus":"READY",
          "batteryStatus":"UNKNOWN",
          "summary":{
            "manufacturer":"Dell",
            "model":"Precision 5540",
            "os":"Windows 11 Pro",
            "cpu":"Intel Core i7-9850H",
            "cpuCores":6,
            "cpuLogicalProcessors":12,
            "ramTotal":"32 GB",
            "ramSpeed":"2667 MT/s",
            "ramStatus":"READY",
            "tpmPresent":null,
            "tpmReady":null,
            "secureBoot":null,
            "gpus":[
              {"name":"Intel UHD 630","type":"Integrated","driverVersion":"1"},
              {"name":"NVIDIA Quadro T2000","type":"Dedicated","driverVersion":"2"}
            ]
          },
          "network":{"status":"READY","internetCheck":true,"adapters":[]},
          "flipValue":{"confidenceScore":0.6,"valueDrivers":[],"valueReducers":[],"suggestedUpgradeRecommendations":[]},
          "disks":[{"name":"Samsung 990 Pro","mediaType":"NVMe SSD","size":"1 TB","health":"Healthy","status":"READY"}],
          "batteries":[],
          "obviousProblems":[],
          "recommendations":[]
        }
        """;
}
