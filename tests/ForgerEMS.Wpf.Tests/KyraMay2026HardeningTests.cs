using System.Text;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Kyra;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraMay2026HardeningTests
{
    [Fact]
    public void KyraDestructiveRequestGuard_DiskWipe_ReturnsSafeLocalResponse()
    {
        Assert.True(KyraDestructiveRequestGuard.TryBuildSafeResponse("How do I diskpart clean all drives?", out var r));
        Assert.Contains("wiping disks", r.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Safety guard", r.SourceLabel);
        Assert.NotNull(r.KyraTransparencySummary);
    }

    [Fact]
    public void KyraDestructiveRequestGuard_NormalUsbQuestion_NotBlocked()
    {
        Assert.False(KyraDestructiveRequestGuard.TryBuildSafeResponse("No likely USB targets in USB Builder", out _));
    }

    [Fact]
    public void KyraGatewayResearchSanitizer_StripsJwtProductKeyPaths()
    {
        var raw =
            "Token eyJhbGciOiJIUzI1NiJ9.payload.sig key ABCDE-FGHIJ-KLMNO-PQRST-UVWXY path D:\\Secret\\file.txt";
        var s = KyraGatewayResearchSanitizer.SanitizePrompt(raw);
        Assert.DoesNotContain("eyJ", s, StringComparison.Ordinal);
        Assert.DoesNotContain("FGHIJ", s, StringComparison.Ordinal);
        Assert.DoesNotContain("D:\\Secret", s, StringComparison.Ordinal);
    }

    [Fact]
    public void KyraPrivacyHelpAnswerBuilder_Triggers_LocalResponse()
    {
        var settings = new CopilotSettings();
        Assert.True(KyraPrivacyHelpAnswerBuilder.TryBuild("What did you send to the gateway?", settings, out var r));
        Assert.Contains("sanitized", r.Text, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(r.KyraTransparencySummary);
    }

    [Fact]
    public void KyraScanDeltaAnswerBuilder_TwoScans_ReportsDelta()
    {
        var path = Path.Combine(Path.GetTempPath(), "kyra-scan-delta-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var profile = new KyraMachineMemoryProfile();
            profile.Entries.Add(new KyraMemoryEntry
            {
                ScanTimestamp = DateTimeOffset.UtcNow.AddHours(-2),
                KyraActionCategory = "system_scan",
                OutcomeCategory = "scan_completed",
                HealthScoreBand = "Fair",
                IssueCategory = "Storage"
            });
            profile.Entries.Add(new KyraMemoryEntry
            {
                ScanTimestamp = DateTimeOffset.UtcNow,
                KyraActionCategory = "system_scan",
                OutcomeCategory = "scan_completed",
                HealthScoreBand = "Good",
                IssueCategory = "Storage"
            });
            new KyraMachineMemoryStore(path).Save(profile);

            Assert.True(KyraScanDeltaAnswerBuilder.TryBuild("What changed since last scan?", path, out var r));
            Assert.Contains("Health score band", r.Text, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void KyraProviderPromptBuilder_PrivacyQuestion_DoesNotInjectHistory()
    {
        var context = new CopilotContext
        {
            UserQuestion = "What is stored in Kyra memory?",
            ContextText = "User question only",
            Intent = KyraIntent.GeneralTechQuestion,
            ConversationHistory =
            [
                new CopilotChatMessage { Role = "You", Text = "price of BTC?" },
                new CopilotChatMessage { Role = "Kyra", Text = "Live data unavailable." }
            ]
        };
        var merged = KyraProviderPromptBuilder.AppendConversationRecap(context.ContextText, context);
        Assert.DoesNotContain("BTC", merged, StringComparison.Ordinal);
    }

    [Fact]
    public void KyraSimpleMath_AfterBtcHistory_StaysIsolated()
    {
        var ctx = new CopilotContext
        {
            Intent = KyraIntent.GeneralTechQuestion,
            UserQuestion = "whats ten times ten",
            ConversationHistory =
            [
                new CopilotChatMessage { Role = "You", Text = "price of BTC today?" },
                new CopilotChatMessage { Role = "Kyra", Text = "CoinGecko said 99000." }
            ]
        };
        var merged = KyraProviderPromptBuilder.AppendConversationRecap("core", ctx);
        Assert.DoesNotContain("CoinGecko", merged, StringComparison.Ordinal);
        Assert.DoesNotContain("99000", merged, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("what is the current price of a Dell Precision 5540 battery", "hardware_part_lookup")]
    [InlineData("latest Ventoy version", "software_version")]
    [InlineData("where can I buy a compatible NVMe drive", "hardware_part_lookup")]
    [InlineData("news about Windows update issues today", "news")]
    [InlineData("weather tomorrow", "weather")]
    [InlineData("AAPL stock price", "finance")]
    [InlineData("BTC crypto price right now", "crypto")]
    public void KyraRealtimeResearchClassifier_DetectsCurrentDataNeeds(string prompt, string expectedIntent)
    {
        Assert.True(KyraRealtimeResearchClassifier.TryClassifyRealtimeNeed(prompt, out var intent));
        Assert.Equal(expectedIntent, intent);
    }

    [Fact]
    public void KyraRealtimeResearchClassifier_LocalBatteryFactsCanStayLocal()
    {
        Assert.False(KyraRealtimeResearchClassifier.TryClassifyRealtimeNeed(
            "what battery does my machine have",
            out _));
    }

    [Fact]
    public void KyraCodeAssist_AfterUsbWarning_IsolatedFromHistory()
    {
        var snippet = """
                      public int Add(int a, int b)
                      {
                          return a - b;
                      }
                      """;
        var context = new CopilotContext
        {
            UserQuestion = snippet,
            ContextText = "isolated",
            Intent = KyraIntent.CodeAssist,
            ConversationHistory =
            [
                new CopilotChatMessage { Role = "You", Text = "No likely USB targets were detected" },
                new CopilotChatMessage { Role = "Kyra", Text = "Plug in USB and refresh." }
            ]
        };
        var merged = KyraProviderPromptBuilder.AppendConversationRecap(context.ContextText, context);
        Assert.DoesNotContain("USB", merged, StringComparison.Ordinal);
    }
}
