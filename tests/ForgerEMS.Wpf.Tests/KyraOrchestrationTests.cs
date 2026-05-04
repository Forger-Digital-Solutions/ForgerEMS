using System.IO;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Kyra;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraOrchestrationTests
{
    [Fact]
    public void KyraContextBuilder_IncludesFactsLedgerAndLastKyraAnswer()
    {
        var memory = new KyraConversationMemory();
        memory.AddTurn("hi", "Kyra local answer about TPM.", KyraIntent.SystemHealthSummary, new SystemContext());
        var ctx = new CopilotContext
        {
            UserQuestion = "follow up",
            Intent = KyraIntent.SystemHealthSummary,
            ContextText = "ctx body",
            SystemProfile = new SystemProfile { Cpu = "Intel i5", Manufacturer = "Dell", Model = "7400" }
        };
        var ledger = KyraFactsLedger.FromCopilotContext(ctx);
        var plan = new KyraToolCallPlan { ShouldUseLocalToolAnswer = false };
        var settings = new CopilotSettings { RedactContextEnabled = true };
        var pkg = KyraContextBuilder.BuildPackage(ctx, ledger, memory, plan, settings);
        Assert.Contains("Facts ledger", pkg.FactsLedgerSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TPM", pkg.LastKyraAnswer, StringComparison.OrdinalIgnoreCase);
        Assert.True(pkg.LocalTruthAvailable);
    }

    [Fact]
    public void KyraRedactionService_RedactsPathLikeSegments()
    {
        var raw = @"C:\Users\SomeOne\secret\app.log token sk-1234567890abcdef";
        var r = KyraRedactionService.RedactForPersistence(raw, enabled: true);
        Assert.DoesNotContain(@"C:\Users\SomeOne", r, StringComparison.Ordinal);
    }

    [Fact]
    public void KyraMachineContextRouter_UpgradeAdviceIsMachineAnchored()
    {
        Assert.True(KyraMachineContextRouter.IsMachineAnchoredIntent(KyraIntent.UpgradeAdvice, "anything"));
    }

    [Fact]
    public void KyraProviderCapabilityCatalog_LocalOfflineIncludesLocalOnly()
    {
        var local = new LocalOfflineCopilotProvider();
        var caps = KyraProviderCapabilityCatalog.ForProvider(local);
        Assert.True(caps.HasFlag(KyraProviderCapabilities.SupportsLocalOnly));
    }

    [Fact]
    public void KyraLiveToolRouter_WeatherIsLiveDataIntent()
    {
        Assert.True(KyraLiveToolRouter.IsLiveDataIntent(KyraIntent.Weather));
        Assert.Contains("live data", KyraLiveToolRouter.LiveToolsUnavailableMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KyraMemoryStore_RoundTripTurns()
    {
        var path = Path.Combine(Path.GetTempPath(), "kyra-conv-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new KyraMemoryStore(path);
            var mem = new KyraConversationMemory(30, store);
            mem.SetPersistenceGate(() => true);
            mem.AddTurn("ask about USB", "Use the toolkit USB tab.", KyraIntent.USBBuilderHelp, new SystemContext());
            var mem2 = new KyraConversationMemory(30, new KyraMemoryStore(path));
            Assert.NotEmpty(mem2.Snapshot());
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void KyraSafetyPolicy_ApiFirstStyleDiscard_WhenLedgerHasProfile()
    {
        var ctx = new CopilotContext
        {
            SystemProfile = new SystemProfile { Cpu = "AMD Ryzen 5", Manufacturer = "HP", Model = "14" }
        };
        var ledger = KyraFactsLedger.FromCopilotContext(ctx);
        var online = "I cannot see your device or CPU.";
        Assert.True(KyraSafetyPolicy.ShouldDiscardOnlineAnswer(online, null, ledger));
    }

    [Fact]
    public void KyraResponseComposer_Sanitize_ReplacesGroqEcho()
    {
        var ledger = new KyraFactsLedger
        {
            HasSystemIntelligenceProfile = true,
            CpuSummary = "Intel Core i7",
            DeviceSummary = "Dell XPS"
        };
        var t = KyraResponseComposer.SanitizeProviderSelfIdentification("I'm Groq and I think your CPU is fine.", ledger);
        Assert.Contains("Kyra", t, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Groq", t, StringComparison.OrdinalIgnoreCase);
    }
}
