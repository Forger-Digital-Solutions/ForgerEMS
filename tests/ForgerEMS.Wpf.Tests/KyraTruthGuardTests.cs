using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Kyra;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraTruthGuardTests
{
    [Fact]
    public void SafetyPolicy_DiscardsDenialWhenLocalProfileExists()
    {
        var ctx = new CopilotContext
        {
            SystemProfile = new SystemProfile
            {
                Cpu = "Intel Core i7-1165G7",
                Manufacturer = "Dell",
                Model = "XPS 13"
            }
        };

        var ledger = KyraFactsLedger.FromCopilotContext(ctx);
        var online = "I do not have access to your device specifications, so I cannot tell what CPU you have.";
        Assert.True(KyraSafetyPolicy.ShouldDiscardOnlineAnswer(online, "local", ledger));
    }

    [Fact]
    public void SafetyPolicy_AllowsOnlineWhenNoLocalFacts()
    {
        var ctx = new CopilotContext { SystemProfile = null };
        var ledger = KyraFactsLedger.FromCopilotContext(ctx);
        Assert.False(ledger.HasTrustedLocalHardwareFacts);
        Assert.False(KyraSafetyPolicy.ShouldDiscardOnlineAnswer("I can help with general Windows tips.", "x", ledger));
    }

    [Fact]
    public void FactsLedger_IncludesDeviceLineFromProfile()
    {
        var ctx = new CopilotContext
        {
            SystemProfile = new SystemProfile { Manufacturer = "Lenovo", Model = "T14", Cpu = "AMD Ryzen 7 PRO" }
        };

        var ledger = KyraFactsLedger.FromCopilotContext(ctx);
        Assert.Contains("Lenovo", ledger.DeviceSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AMD Ryzen", ledger.CpuSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IntentRouter_DeviceQuestionMapsToSystemHealth()
    {
        var intent = KyraIntentRouter.DetectIntent("What device are we working on?");
        Assert.Equal(KyraIntent.SystemHealthSummary, intent);
    }

    [Fact]
    public void FollowUpClassifier_RecognizesThatUsb()
    {
        Assert.True(KyraFollowUpClassifier.LooksLikeConversationFollowUp("What about that USB?"));
    }

    [Fact]
    public void ContradictsLedger_IntelCpuVsRyzenOnline_IsContradiction()
    {
        var ledger = new KyraFactsLedger
        {
            HasSystemIntelligenceProfile = true,
            CpuSummary = "Intel Core i7-9850H",
            DeviceSummary = "Dell Precision 5540"
        };
        const string online = "Your CPU is an AMD Ryzen 9 5900HX with 64 GB RAM.";
        Assert.True(KyraSafetyPolicy.ContradictsLocalHardwareLedger(online, ledger));
    }

    [Fact]
    public void ContradictsLedger_AmdCpuVsIntelCoreOnline_IsContradiction()
    {
        var ledger = new KyraFactsLedger
        {
            HasSystemIntelligenceProfile = true,
            CpuSummary = "AMD Ryzen 7 PRO 4750U",
            DeviceSummary = "Lenovo ThinkPad"
        };
        const string online = "This PC has an Intel Core i7-1165G7.";
        Assert.True(KyraSafetyPolicy.ContradictsLocalHardwareLedger(online, ledger));
    }

    [Fact]
    public void ContradictsLedger_IntelUhdVsRtxOnline_IsContradiction()
    {
        var ledger = new KyraFactsLedger
        {
            HasSystemIntelligenceProfile = true,
            CpuSummary = "Intel Core i7-9850H",
            GpuSummary = "Intel UHD Graphics 630",
            RamSummary = "32 GB",
            DeviceSummary = "Dell Precision 5540"
        };
        const string online = "Your laptop has an NVIDIA GeForce RTX 3080 Laptop GPU.";
        Assert.True(KyraSafetyPolicy.ContradictsLocalHardwareLedger(online, ledger));
    }

    [Fact]
    public void ContradictsLedger_RamAmountFarFromLedger_IsContradiction()
    {
        var ledger = new KyraFactsLedger
        {
            HasSystemIntelligenceProfile = true,
            CpuSummary = "Intel Core i7-9850H",
            RamSummary = "32 GB",
            GpuSummary = "NVIDIA Quadro T2000",
            DeviceSummary = "Dell Precision 5540"
        };
        const string online = "This machine ships with 64 GB of RAM for heavy CAD workloads.";
        Assert.True(KyraSafetyPolicy.ContradictsLocalHardwareLedger(online, ledger));
    }
}
