using System.Text.Json;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Kyra;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraHardwarePersonalityAndFactsTests
{
    [Fact]
    public void LocalRules_CasualHi_DoesNotDumpDiagnostics()
    {
        var text = LocalRulesCopilotEngine.GenerateReply(
            "hi",
            new CopilotContext
            {
                UserQuestion = "hi",
                Intent = KyraIntent.GeneralTechQuestion,
                PersonalityProfile = "bubbly-tech"
            });
        Assert.DoesNotContain("System Intelligence", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Kyra", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalRules_NormalConversation_IsWarmNotDismissive()
    {
        var text = LocalRulesCopilotEngine.GenerateReply(
            "can't we just have a normal conversation?",
            new CopilotContext
            {
                UserQuestion = "can't we just have a normal conversation?",
                Intent = KyraIntent.GeneralTechQuestion,
                PersonalityProfile = "bubbly-tech"
            });
        Assert.DoesNotContain("focus on the facts", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("chat", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalRules_BeSerious_ReducesPlayfulWording()
    {
        var text = LocalRulesCopilotEngine.GenerateReply(
            "be serious please",
            new CopilotContext
            {
                UserQuestion = "be serious please",
                Intent = KyraIntent.GeneralTechQuestion,
                PersonalityProfile = "bubbly-tech"
            });
        Assert.DoesNotContain("😄", text, StringComparison.Ordinal);
        Assert.Contains("neutral", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KyraHardwareFactsEngine_NvMe_FromBusType()
    {
        var p = new SystemProfile
        {
            Disks =
            [
                new SystemDiskProfile
                {
                    Name = "Samsung SSD",
                    InterfaceType = "NVMe",
                    MediaType = "SSD",
                    Health = "Healthy",
                    Status = "READY"
                }
            ]
        };
        Assert.Equal(KyraStorageBusKind.Nvme, KyraHardwareFactsEngine.PrimaryStorageBusKind(p));
        Assert.True(KyraHardwareFactsEngine.StorageLooksHealthyNvmeSsd(p));
    }

    [Fact]
    public void KyraHardwareFactsEngine_MemoryType_FromSummary()
    {
        var p = new SystemProfile { MemoryTypeSummary = "DDR4", RamTotal = "32 GB" };
        Assert.Equal("DDR4", KyraHardwareFactsEngine.MemoryTypeLabel(p));
    }

    [Fact]
    public void KyraHardwarePartsAnswer_DoesNotClaimExactBatteryPart()
    {
        var profile = new SystemProfile
        {
            Manufacturer = "Dell",
            Model = "Precision 5540",
            Batteries =
            [
                new SystemBatteryProfile
                {
                    Name = "Dell Battery",
                    WearPercent = 40.2,
                    DesignCapacityDisplay = "90000 mWh",
                    FullChargeCapacityDisplay = "54000 mWh"
                }
            ]
        };
        Assert.True(KyraHardwarePartsAnswerBuilder.TryBuild(
            "what battery do I need for this machine?",
            profile,
            new CopilotSettings { PersonalityProfile = "professional" },
            out var r));
        Assert.Contains("not in the scan", r.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FRP", r.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void KyraHardwarePartsAnswer_HealthyNvMe_DoesNotPushSsdFirst()
    {
        var profile = new SystemProfile
        {
            Manufacturer = "Dell",
            Model = "Precision 5540",
            Batteries = [new SystemBatteryProfile { WearPercent = 5 }],
            Disks =
            [
                new SystemDiskProfile
                {
                    InterfaceType = "NVMe",
                    MediaType = "SSD",
                    Health = "Healthy",
                    Status = "READY",
                    Name = "NVMe SSD"
                }
            ],
            RamTotalGb = 32
        };
        Assert.True(KyraHardwarePartsAnswerBuilder.TryBuild(
            "what should I upgrade first?",
            profile,
            new CopilotSettings(),
            out var r));
        Assert.Contains("NVMe", r.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("swap the SSD as your first upgrade", r.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KyraHardwarePartsAnswer_HighBatteryWear_MentionsReplacement()
    {
        var profile = new SystemProfile
        {
            Manufacturer = "Dell",
            Model = "XPS",
            Batteries = [new SystemBatteryProfile { WearPercent = 42 }],
            Disks =
            [
                new SystemDiskProfile
                {
                    InterfaceType = "NVMe",
                    MediaType = "SSD",
                    Health = "Healthy",
                    Status = "READY",
                    Name = "SSD"
                }
            ]
        };
        Assert.True(KyraHardwarePartsAnswerBuilder.TryBuild(
            "what should I upgrade first?",
            profile,
            new CopilotSettings(),
            out var r));
        Assert.Contains("Battery", r.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KyraRealtimeResearchClassifier_CheapestBattery_UsesHardwarePartLookup()
    {
        using var env = KyraTestEnvGate.Open("https://unit.test/gateway/", "unit-test-beta-token");
        var settings = new CopilotSettings
        {
            KyraRealtimeGatewayEnabled = true,
            KyraRealtimeGatewayResearchEnabled = true,
            KyraRealtimeGatewayResearchConsent = true
        };
        var use = KyraRealtimeResearchClassifier.ShouldUseRealtimeGateway(
            "find the cheapest compatible battery for this laptop",
            settings,
            out var intent);
        Assert.True(use);
        Assert.Equal("hardware_part_lookup", intent);
    }

    [Fact]
    public void KyraRealtimeResearchClassifier_NvMeQuestion_StaysLocalFirst()
    {
        using var env = KyraTestEnvGate.Open("https://unit.test/gateway/", "unit-test-beta-token");
        var settings = new CopilotSettings
        {
            KyraRealtimeGatewayEnabled = true,
            KyraRealtimeGatewayResearchEnabled = true,
            KyraRealtimeGatewayResearchConsent = true
        };
        var use = KyraRealtimeResearchClassifier.ShouldUseRealtimeGateway(
            "is my drive NVMe or SATA?",
            settings,
            out _);
        Assert.False(use);
    }

    [Fact]
    public void MachineClassifier_MinimalGatewayScanJson_DoesNotThrow()
    {
        const string json = """{"schemaVersion":1,"summary":{"manufacturer":"Dell","model":"Precision 5540","memoryType":"DDR4","ramTotal":"32 GB"},"disks":[{"name":"Disk0","interfaceType":"NVMe","mediaType":"SSD","size":"512 GB","health":"Healthy","status":"READY"}],"batteries":[{"name":"Batt","wearPercent":40}],"health":{"overallScore":82},"overallStatus":"READY","diskStatus":"READY","batteryStatus":"READY","obviousProblems":[],"recommendations":[]}""";
        using var doc = JsonDocument.Parse(json);
        var profile = SystemProfileMapper.FromJson(doc.RootElement);
        var mc = MachineClassifier.Classify(profile).PrimaryClass;
        Assert.False(string.IsNullOrEmpty(mc));
    }

    [Fact]
    public void KyraGatewayResearchContextBuilder_PartLookup_IncludesBands()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kyra-ut-{Guid.NewGuid():N}.json");
        const string root = """{"schemaVersion":1,"summary":{"manufacturer":"Dell","model":"Precision 5540","memoryType":"DDR4","ramTotal":"32 GB"},"disks":[{"name":"Disk0","interfaceType":"NVMe","mediaType":"SSD","size":"512 GB","health":"Healthy","status":"READY"}],"batteries":[{"name":"Batt","wearPercent":40}],"health":{"overallScore":82},"overallStatus":"READY","diskStatus":"READY","batteryStatus":"READY","obviousProblems":[],"recommendations":[]}""";
        File.WriteAllText(path, root);
        try
        {
            Assert.True(File.Exists(path));
            var ctx = KyraGatewayResearchContextBuilder.Build(
                new CopilotSettings { KyraUseSanitizedSystemIntelligenceContext = true },
                path,
                null,
                "hardware_part_lookup",
                "cheapest battery");

            Assert.False(
                string.IsNullOrEmpty(ctx.MachineClass),
                "Expected scan JSON to load (machineClass missing). IssueCategory=" + (ctx.IssueCategory ?? "null"));
            Assert.Equal("Dell", ctx.Manufacturer);
            Assert.Contains("5540", ctx.ModelFamily ?? "", StringComparison.Ordinal);
            Assert.Equal("battery", ctx.PartCategory);
            Assert.NotNull(ctx.KnownLocalFacts);
            Assert.Equal("NVMe", ctx.KnownLocalFacts!.StorageBusBand);
            Assert.Equal("high", ctx.KnownLocalFacts.BatteryWearBand);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class KyraTestEnvGate : IDisposable
    {
        private readonly string? _prevUrl;
        private readonly string? _prevTok;

        private KyraTestEnvGate(string? prevUrl, string? prevTok)
        {
            _prevUrl = prevUrl;
            _prevTok = prevTok;
        }

        public static IDisposable Open(string url, string tok)
        {
            var prevUrl = Environment.GetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_URL");
            var prevTok = Environment.GetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_BETA_TOKEN");
            Environment.SetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_URL", url);
            Environment.SetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_BETA_TOKEN", tok);
            return new KyraTestEnvGate(prevUrl, prevTok);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_URL", _prevUrl);
            Environment.SetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_BETA_TOKEN", _prevTok);
        }
    }
}
