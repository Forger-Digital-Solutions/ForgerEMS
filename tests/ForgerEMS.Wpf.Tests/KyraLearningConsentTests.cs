using System.IO;
using System.Text.Json;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Kyra;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraLearningConsentTests
{
    [Fact]
    public void CopilotSettings_CommunityFlags_AreOffByDefault()
    {
        var s = new CopilotSettings();
        Assert.False(s.KyraCommunitySharingEnabled);
        Assert.False(s.KyraShareResolvedIssueFixPatterns);
        Assert.False(s.KyraShareHardwareCompatibilityPerformancePatterns);
        Assert.False(s.KyraShareCrashErrorDiagnostics);
        Assert.True(s.KyraLocalRepairMemoryEnabled);
        Assert.True(s.KyraUseSanitizedSystemIntelligenceContext);
    }

    [Fact]
    public void KyraCommunityMetadataFormatter_Default_IsSharingOffChip()
    {
        Assert.Equal("Community sharing off", KyraCommunityMetadataFormatter.SummaryChip(new CopilotSettings()));
        Assert.Contains("off", KyraCommunityMetadataFormatter.DetailsParagraph(new CopilotSettings()), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KyraCommunityMetadataFormatter_MasterEnabled_ShowsPreviewNotOff()
    {
        var s = new CopilotSettings { KyraCommunitySharingEnabled = true };
        Assert.Equal("Community preview only", KyraCommunityMetadataFormatter.SummaryChip(s));
        Assert.DoesNotContain("Community sharing off", KyraCommunityMetadataFormatter.SummaryChip(s), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KyraCommunityMetadataFormatter_SubFlagAlone_StillOptedInForMetadata()
    {
        var s = new CopilotSettings
        {
            KyraCommunitySharingEnabled = false,
            KyraShareResolvedIssueFixPatterns = true
        };
        Assert.Equal("Community preview only", KyraCommunityMetadataFormatter.SummaryChip(s));
    }

    [Fact]
    public void CopilotSettingsStore_LoadsCamelCaseKyraFlags()
    {
        var path = Path.Combine(Path.GetTempPath(), "kyra-camel-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(
                path,
                """
                {
                  "kyraCommunitySharingEnabled": true,
                  "kyraShareResolvedIssueFixPatterns": true,
                  "providers": {}
                }
                """);
            var store = new CopilotSettingsStore(path, new CopilotProviderRegistry());
            var s = store.Load();
            Assert.True(s.KyraCommunitySharingEnabled);
            Assert.True(s.KyraShareResolvedIssueFixPatterns);
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
    public void CopilotSettingsStore_Load_PromotesMasterWhenSubShareTrue()
    {
        var path = Path.Combine(Path.GetTempPath(), "kyra-promote-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(
                path,
                """
                {
                  "kyraCommunitySharingEnabled": false,
                  "kyraShareHardwareCompatibilityPerformancePatterns": true,
                  "providers": {}
                }
                """);
            var store = new CopilotSettingsStore(path, new CopilotProviderRegistry());
            var s = store.Load();
            Assert.True(s.KyraCommunitySharingEnabled);
            Assert.True(s.KyraShareHardwareCompatibilityPerformancePatterns);
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
    public void KyraMemorySanitizer_SanitizeText_RemovesPathLikeSegments()
    {
        var t = KyraMemorySanitizer.SanitizeText(@"Issue at C:\Users\someone\secret\file.txt and \\server\share", 400);
        Assert.DoesNotContain(@"C:\Users", t, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("someone", t, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KyraMemorySanitizer_SanitizeText_StripsEmailAndIpPatterns()
    {
        var t = KyraMemorySanitizer.SanitizeText("contact me at user@example.com from 203.0.113.10 thanks", 400);
        Assert.DoesNotContain("example.com", t, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("203.0.113", t, StringComparison.Ordinal);
    }

    [Fact]
    public void KyraCommunityPayloadPreview_LocalOnly_StatusInJson()
    {
        var profile = new KyraMachineMemoryProfile();
        var settings = new KyraMemorySettings { CommunitySharingEnabled = false };
        var json = KyraCommunityPayloadPreviewBuilder.BuildPreview(profile, settings, "1.0.0", "beta");
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("status", out var st));
        Assert.Contains("Local Only", st.GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KyraCommunityPayloadPreview_HypotheticalConsent_UsesEffectiveFlags()
    {
        var profile = new KyraMachineMemoryProfile();
        var stored = new KyraMemorySettings { CommunitySharingEnabled = false };
        var hypo = new KyraMemorySettings
        {
            CommunitySharingEnabled = true,
            ShareResolvedIssueFixPatterns = true,
            ShareHardwareCompatibilityPerformancePatterns = false,
            ShareCrashErrorDiagnostics = false
        };
        var json = KyraCommunityPayloadPreviewBuilder.BuildPreview(profile, stored, "1.0.0", "beta", hypo);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("consent", out var c));
        Assert.True(c.GetProperty("communitySharing").GetBoolean());
        Assert.True(c.GetProperty("resolvedIssueFixPatterns").GetBoolean());
    }

    [Fact]
    public void ShouldOfferFixFeedback_SkipsSimpleMath()
    {
        Assert.False(KyraMemorySanitizer.ShouldOfferFixFeedback(
            KyraIntent.PerformanceLag,
            "whats ten times ten",
            new string('x', 120)));
    }

    [Fact]
    public void ShouldOfferFixFeedback_SkipsLocalHardwarePartsStyleAnswer()
    {
        var text = """
            Tiny upgrade goblin check-in 😄

            What I know (local scan):
            • Machine: Dell Test

            Confirm before buying: match any part against your service manual.
            """;
        Assert.False(KyraMemorySanitizer.ShouldOfferFixFeedback(KyraIntent.UpgradeAdvice, "battery part", text));
    }

    [Theory]
    [InlineData(KyraIntent.CodeAssist, "public int Add(int a, int b) { return a - b; }")]
    [InlineData(KyraIntent.GeneralTechQuestion, "hi kyra")]
    [InlineData(KyraIntent.ForgerEMSQuestion, "How do I update my env variables for you?")]
    [InlineData(KyraIntent.Weather, "what’s the weather today")]
    [InlineData(KyraIntent.CryptoPrice, "price of BTC today")]
    public void ShouldOfferFixFeedback_HidesForDirectNonRepairAnswers(KyraIntent intent, string prompt)
    {
        Assert.False(KyraMemorySanitizer.ShouldOfferFixFeedback(intent, prompt, new string('x', 160)));
    }

    [Theory]
    [InlineData(KyraIntent.ForgerEMSQuestion, "Explain this warning: No likely USB targets were detected.")]
    [InlineData(KyraIntent.SystemHealthSummary, "What device are we working on?")]
    [InlineData(KyraIntent.GeneralTechQuestion, "What changed since last scan?")]
    [InlineData(KyraIntent.GeneralTechQuestion, "gateway unauthorized provider troubleshooting")]
    [InlineData(KyraIntent.StorageIssue, "storage warning guidance")]
    [InlineData(KyraIntent.PerformanceLag, "app lag high CPU")]
    public void ShouldOfferFixFeedback_ShowsForTroubleshootingRepairAnswers(KyraIntent intent, string prompt)
    {
        Assert.True(KyraMemorySanitizer.ShouldOfferFixFeedback(intent, prompt, new string('x', 160)));
    }

    [Fact]
    public async Task DisabledKyraCommunityIntelligenceClient_SubmitReturnsFalse()
    {
        var client = new DisabledKyraCommunityIntelligenceClient();
        var ok = await client.SubmitDiagnosticEventAsync(
            KyraCommunityPayloadPreviewBuilder.FromMemoryEntry(
                KyraMemorySanitizer.BuildEntryFromPrompt(
                    "diagnostic",
                    "response",
                    profile: null,
                    health: null),
                "1.0.0",
                "beta"),
            default);
        Assert.False(ok);
        Assert.True(client.SubmitAttempted);
    }

    [Fact]
    public void KyraGatewayResearchContextBuilder_WhenSiContextDisabled_SkipsMachineClass()
    {
        var settings = new CopilotSettings
        {
            KyraUseSanitizedSystemIntelligenceContext = false,
            KyraCommunitySharingEnabled = false
        };
        var ctx = KyraGatewayResearchContextBuilder.Build(settings, "nope.json", null);
        Assert.True(string.IsNullOrWhiteSpace(ctx.MachineClass));
    }

    [Fact]
    public void KyraMemoryStore_WhenLocalMemoryDisabled_DoesNotAppend()
    {
        var path = Path.Combine(Path.GetTempPath(), "kyra-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new KyraMachineMemoryStore(path);
            var entry = KyraMemorySanitizer.BuildEntryFromPrompt("a", "b", null, null);
            Assert.False(store.TryAppend(entry, new KyraMemorySettings { LocalRepairMemoryEnabled = false }));
            Assert.False(File.Exists(path));
        }
        finally
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }
}
