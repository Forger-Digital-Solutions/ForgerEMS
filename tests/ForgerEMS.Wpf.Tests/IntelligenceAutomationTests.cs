using System.IO;
using System.Text.Json;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class IntelligenceAutomationTests
{
    [Fact]
    public void Diagnostics_AggregateSeverity_RespectsPriority()
    {
        var warningOnly = new UnifiedDiagnosticsReport
        {
            Items =
            [
                new UnifiedDiagnosticItem { Severity = DiagnosticSeverityLevel.Warning }
            ]
        };
        Assert.Equal(DiagnosticSeverityLevel.Warning, DiagnosticsService.AggregateSeverity(warningOnly.Items));

        var mixed = new List<UnifiedDiagnosticItem>
        {
            new() { Severity = DiagnosticSeverityLevel.Warning },
            new() { Severity = DiagnosticSeverityLevel.Blocked }
        };
        Assert.Equal(DiagnosticSeverityLevel.Blocked, DiagnosticsService.AggregateSeverity(mixed));

        var ok = new List<UnifiedDiagnosticItem>
        {
            new() { Severity = DiagnosticSeverityLevel.Ok },
            new() { Severity = DiagnosticSeverityLevel.Ok }
        };
        Assert.Equal(DiagnosticSeverityLevel.Ok, DiagnosticsService.AggregateSeverity(ok));
    }

    [Fact]
    public void ToolkitDisplayClassification_NormalizesStatuses()
    {
        Assert.Equal("Managed Ready", ToolkitDisplayClassification.BuildNormalizedLabel("INSTALLED", "MANAGED", ""));
        Assert.Equal("Managed Missing", ToolkitDisplayClassification.BuildNormalizedLabel("MISSING_REQUIRED", "MANAGED", ""));
        Assert.Equal("Manual Required", ToolkitDisplayClassification.BuildNormalizedLabel("MANUAL_REQUIRED", "MANUAL", ""));
        Assert.Equal("Verification Issues", ToolkitDisplayClassification.BuildNormalizedLabel("HASH_FAILED", "MANAGED", "FAIL"));
        Assert.Equal("Verification Pending", ToolkitDisplayClassification.BuildNormalizedLabel("VERIFICATION_PENDING", "MANAGED", ""));
    }

    [Fact]
    public void KyraSafeContextBuilder_RedactsLongTokens()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"kyra-safe-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(
                tmp,
                """
                {
                  "forgerAutomation": {
                    "summaryLine": "SECRET123456789012345678901234567890ABCDEF token"
                  }
                }
                """);

            var text = KyraSafeContextBuilder.BuildBriefSummary(tmp, null, null, null, enableRedaction: true);
            Assert.DoesNotContain("SECRET123456789012345678901234567890ABCDEF", text);
            Assert.Contains("[redacted]", text);
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
    public void SystemIntelligenceAutomationMerger_WritesForgerAutomation()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"si-merge-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                tmp,
                """
                {
                  "generatedUtc": "2026-01-01T12:00:00Z",
                  "overallStatus": "READY",
                  "diskStatus": "READY",
                  "batteryStatus": "READY",
                  "summary": {
                    "manufacturer": "TestCo",
                    "model": "TestBook",
                    "os": "Windows 11",
                    "osBuild": "22631",
                    "cpu": "Intel Core i5-1135G7",
                    "cpuCores": 4,
                    "cpuLogicalProcessors": 8,
                    "ramTotal": "16 GB",
                    "ramSpeed": "3200 MT/s",
                    "ramSlotsFree": 0,
                    "ramUpgradePath": "SODIMM",
                    "ramStatus": "READY",
                    "tpmPresent": true,
                    "tpmReady": true,
                    "secureBoot": true,
                    "gpus": [{"name": "Iris Xe", "type": "Integrated", "driverVersion": "1.0"}]
                  },
                  "network": { "status": "READY", "internetCheck": true, "adapters": [] },
                  "flipValue": { "estimateType": "local", "providerStatus": "offline", "estimatedResaleRange": "0", "recommendedListPrice": "0", "quickSalePrice": "0", "partsRepairPrice": "0", "confidenceScore": 0.5, "valueDrivers": [], "valueReducers": [], "suggestedUpgradeRecommendations": [] },
                  "disks": [],
                  "batteries": [],
                  "obviousProblems": [],
                  "recommendations": []
                }
                """);

            Assert.True(SystemIntelligenceAutomationMerger.TryMerge(tmp));
            using var doc = JsonDocument.Parse(File.ReadAllText(tmp));
            Assert.True(doc.RootElement.TryGetProperty("forgerAutomation", out var auto));
            Assert.True(auto.TryGetProperty("healthScore", out _));
            Assert.True(auto.TryGetProperty("issues", out var issues) && issues.ValueKind == JsonValueKind.Array);
            Assert.True(auto.TryGetProperty("recommendedActions", out _));
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
    public void SystemGpuProfileMapper_ReadsGpuKindFromJson()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "overallStatus":"READY",
              "diskStatus":"READY",
              "batteryStatus":"READY",
              "summary":{"gpus":[{"name":"X","type":"Dedicated","driverVersion":"1"}]},
              "network":{"adapters":[],"internetCheck":true},
              "flipValue":{"estimateType":"local","providerStatus":"offline","estimatedResaleRange":"0","recommendedListPrice":"0","quickSalePrice":"0","partsRepairPrice":"0","confidenceScore":0.5,"valueDrivers":[],"valueReducers":[],"suggestedUpgradeRecommendations":[]},
              "disks":[],
              "batteries":[],
              "obviousProblems":[],
              "recommendations":[]
            }
            """);
        var profile = SystemProfileMapper.FromJson(doc.RootElement);
        Assert.Single(profile.Gpus);
        Assert.Equal("Dedicated", profile.Gpus[0].GpuKind);
    }

    [Fact]
    public void UnknownFirmwareFieldsLowerConfidenceMoreThanHealth()
    {
        var profile = new SystemProfile
        {
            Manufacturer = "Dell",
            Model = "Precision 5540",
            Cpu = "Intel Core i7-9850H",
            RamTotal = "32 GB",
            RamTotalGb = 32,
            OverallStatus = "READY",
            DiskStatus = "READY",
            BatteryStatus = "UNKNOWN",
            RamStatus = "READY",
            TpmPresent = null,
            TpmReady = null,
            TpmStatus = "UNKNOWN",
            SecureBoot = null,
            SecureBootStatus = "UNKNOWN",
            InternetCheck = true,
            HasActivePhysicalInternetAdapter = true,
            Gpus = [new SystemGpuProfile { Name = "NVIDIA Quadro T2000", GpuKind = "Dedicated" }],
            Disks = [new SystemDiskProfile { Name = "Samsung 990 Pro", MediaType = "NVMe SSD", Health = "Healthy", Status = "READY" }]
        };

        var health = SystemHealthEvaluator.Evaluate(profile);

        Assert.True(health.HealthScore >= 80);
        Assert.True(health.ConfidenceScore < 100);
        Assert.Contains(health.DetectedIssues, issue => issue.Contains("TPM state is unknown", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void VirtualBoxHostOnlyApipaDoesNotCreateCriticalNetworkPenalty()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "overallStatus":"READY",
              "diskStatus":"READY",
              "batteryStatus":"READY",
              "summary":{"ramTotal":"32 GB","ramStatus":"READY","tpmInfo":{"status":"READY"},"secureBootInfo":{"status":"READY"},"tpmPresent":true,"tpmReady":true,"secureBoot":true},
              "network":{
                "status":"READY",
                "internetCheck":true,
                "adapters":[
                  {"name":"Ethernet","description":"Intel Ethernet","adapterRole":"ActivePhysicalInternet","apipaDetected":false,"gatewayPresent":true,"isVirtual":false},
                  {"name":"VirtualBox Host-Only Network","description":"VirtualBox Ethernet Adapter","adapterRole":"VirtualAdapter","apipaDetected":true,"gatewayPresent":false,"isVirtual":true}
                ]
              },
              "flipValue":{"confidenceScore":0.6,"valueDrivers":[],"valueReducers":[],"suggestedUpgradeRecommendations":[]},
              "disks":[],
              "batteries":[],
              "obviousProblems":[],
              "recommendations":[]
            }
            """);

        var profile = SystemProfileMapper.FromJson(doc.RootElement);
        var health = SystemHealthEvaluator.Evaluate(profile);

        Assert.Equal(0, profile.ApipaAdapterCount);
        Assert.Equal(0, profile.MissingGatewayAdapterCount);
        Assert.True(health.HealthScore >= 80);
    }

    [Fact]
    public void AutomationSummaryCountsAdaptersAndNamesGpus()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"si-merge-gpu-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                tmp,
                """
                {
                  "overallStatus":"READY",
                  "diskStatus":"READY",
                  "batteryStatus":"READY",
                  "summary":{
                    "manufacturer":"Dell",
                    "model":"Precision 5540",
                    "cpu":"Intel Core i7-9850H",
                    "ramTotal":"32 GB",
                    "ramStatus":"READY",
                    "tpmPresent":null,
                    "tpmReady":null,
                    "secureBoot":null,
                    "tpmInfo":{"status":"UNKNOWN","friendlyDisplayText":"TPM status unavailable"},
                    "secureBootInfo":{"status":"UNKNOWN","friendlyDisplayText":"Unknown - requires admin or unavailable"},
                    "gpus":[
                      {"name":"Intel UHD 630","type":"Integrated","driverVersion":"1"},
                      {"name":"NVIDIA Quadro T2000","type":"Dedicated","driverVersion":"2"}
                    ]
                  },
                  "network":{
                    "status":"READY",
                    "internetCheck":true,
                    "physicalAdapters":[{"name":"Ethernet"}],
                    "virtualAdapters":[{"name":"VirtualBox Host-Only"}],
                    "adapters":[]
                  },
                  "flipValue":{"confidenceScore":0.6,"valueDrivers":[],"valueReducers":[],"suggestedUpgradeRecommendations":[]},
                  "disks":[],
                  "batteries":[],
                  "obviousProblems":[],
                  "recommendations":[]
                }
                """);

            Assert.True(SystemIntelligenceAutomationMerger.TryMerge(tmp));
            using var doc = JsonDocument.Parse(File.ReadAllText(tmp));
            var summary = doc.RootElement.GetProperty("forgerAutomation").GetProperty("summaryLine").GetString();

            Assert.Contains("1 physical / 1 virtual", summary);
            Assert.Contains("NVIDIA Quadro T2000", summary);
            Assert.DoesNotContain("GPUs: Unknown", summary);
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
    public void KyraIntentRouter_UsbSlowRoutesToUsbBuilder()
    {
        Assert.Equal(KyraIntent.USBBuilderHelp, KyraIntentRouter.DetectIntent("Why is my USB stick so slow?"));
        Assert.Equal(KyraIntent.USBBuilderHelp, KyraIntentRouter.DetectIntent("What is the best port to use for this USB stick?"));
    }
}
