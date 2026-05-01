using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraSystemAnalyzerTests
{
    private static SystemProfile LowRamProfile() =>
        new()
        {
            Manufacturer = "TestCo",
            Model = "Book",
            RamTotalGb = 4,
            RamTotal = "4 GB",
            OverallStatus = "OK",
            Disks = [new SystemDiskProfile { MediaType = "SSD", Size = "256GB", Health = "OK", Status = "OK" }],
            InternetCheck = true
        };

    [Fact]
    public void LowRam_FlagsRisk()
    {
        var h = new SystemHealthEvaluation { HealthScore = 70, DetectedIssues = [] };
        var insight = KyraSystemAnalyzer.Analyze(LowRamProfile(), h, [], null);
        Assert.Contains(insight.RiskFlags, x => x.Contains("RAM", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HddOnly_FlagsBottleneck()
    {
        var p = new SystemProfile
        {
            Manufacturer = "TestCo",
            Model = "Book",
            RamTotalGb = 16,
            RamTotal = "16 GB",
            OverallStatus = "OK",
            Disks = [new SystemDiskProfile { MediaType = "HDD", Size = "500GB", Health = "OK", Status = "OK" }],
            InternetCheck = true
        };
        var insight = KyraSystemAnalyzer.Analyze(p, new SystemHealthEvaluation { HealthScore = 80, DetectedIssues = [] }, [], null);
        Assert.Contains(insight.RiskFlags, x => x.Contains("HDD", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BatteryWear_Flags()
    {
        var p = new SystemProfile
        {
            Manufacturer = "TestCo",
            Model = "Book",
            RamTotalGb = 16,
            RamTotal = "16 GB",
            OverallStatus = "OK",
            Disks = [new SystemDiskProfile { MediaType = "SSD", Size = "256GB", Health = "OK", Status = "OK" }],
            Batteries = [new SystemBatteryProfile { WearPercent = 55, Status = "OK" }],
            InternetCheck = true
        };
        var insight = KyraSystemAnalyzer.Analyze(p, new SystemHealthEvaluation { HealthScore = 75, DetectedIssues = [] }, [], null);
        Assert.Contains(insight.RiskFlags, x => x.Contains("Battery", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SecureBootOff_Flags()
    {
        var p = new SystemProfile
        {
            Manufacturer = "TestCo",
            Model = "Book",
            RamTotalGb = 16,
            RamTotal = "16 GB",
            OverallStatus = "OK",
            SecureBoot = false,
            Disks = [new SystemDiskProfile { MediaType = "SSD", Size = "256GB", Health = "OK", Status = "OK" }],
            InternetCheck = true
        };
        var insight = KyraSystemAnalyzer.Analyze(p, new SystemHealthEvaluation { HealthScore = 80, DetectedIssues = [] }, [], null);
        Assert.Contains(insight.RiskFlags, x => x.Contains("Secure Boot", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TpmNotReady_FlagsWin11()
    {
        var p = new SystemProfile
        {
            Manufacturer = "TestCo",
            Model = "Book",
            RamTotalGb = 16,
            RamTotal = "16 GB",
            OverallStatus = "OK",
            TpmPresent = false,
            TpmReady = false,
            Disks = [new SystemDiskProfile { MediaType = "SSD", Size = "256GB", Health = "OK", Status = "OK" }],
            InternetCheck = true
        };
        var insight = KyraSystemAnalyzer.Analyze(p, new SystemHealthEvaluation { HealthScore = 80, DetectedIssues = [] }, [], null);
        Assert.Contains(insight.RiskFlags, x => x.Contains("TPM", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DiskHealthWarn_Flags()
    {
        var p = new SystemProfile
        {
            Manufacturer = "TestCo",
            Model = "Book",
            RamTotalGb = 16,
            RamTotal = "16 GB",
            OverallStatus = "OK",
            Disks =
            [
                new SystemDiskProfile { MediaType = "SSD", Size = "256GB", Health = "Warning", Status = "OK" }
            ],
            InternetCheck = true
        };
        var insight = KyraSystemAnalyzer.Analyze(p, new SystemHealthEvaluation { HealthScore = 60, DetectedIssues = ["disk"] }, [], null);
        Assert.Contains(insight.RiskFlags, x => x.Contains("Storage", StringComparison.OrdinalIgnoreCase));
    }
}
