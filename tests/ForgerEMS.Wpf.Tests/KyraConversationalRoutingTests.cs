using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Kyra;
using VentoyToolkitSetup.Wpf.Services.KyraTools;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

[Collection("GatewayEnv")]
public sealed class KyraConversationalRoutingTests
{
    private static CopilotSettings GatewayOnSettings() =>
        new()
        {
            KyraRealtimeGatewayEnabled = true,
            KyraRealtimeGatewayResearchEnabled = true,
            KyraRealtimeGatewayResearchConsent = true,
            LiveTools = new KyraLiveToolsSettings { StocksEnabled = true, StocksApiKey = "test-key", StocksProvider = "finnhub" }
        };

    [Fact]
    public void KyraSimpleMathEvaluator_TenTimesTen_IsolatedAnswer()
    {
        Assert.True(KyraSimpleMathEvaluator.TryEvaluate("whats ten times ten", out var answer, out _));
        Assert.Contains("100", answer, StringComparison.Ordinal);
        Assert.DoesNotContain("environment", answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KyraSimpleMathEvaluator_PercentOf_Works()
    {
        Assert.True(KyraSimpleMathEvaluator.TryEvaluate("what is 25 percent of 80", out var answer, out _));
        Assert.Contains("20", answer, StringComparison.Ordinal);
    }

    [Fact]
    public void KyraRealtimeResearchClassifier_SimpleMath_DoesNotUseGateway()
    {
        var prevUrl = Environment.GetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_URL");
        var prevTok = Environment.GetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_BETA_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_URL", "https://unit.test/gateway/");
            Environment.SetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_BETA_TOKEN", "unit-test-beta-token");

            var use = KyraRealtimeResearchClassifier.ShouldUseRealtimeGateway(
                "whats ten times ten",
                GatewayOnSettings(),
                out var intent);
            Assert.False(use);
            Assert.Equal("chat", intent);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_URL", prevUrl);
            Environment.SetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_BETA_TOKEN", prevTok);
        }
    }

    [Fact]
    public void KyraRealtimeResearchClassifier_FinancePrompt_UsesGatewayWithoutDesktopStockKey()
    {
        var prevUrl = Environment.GetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_URL");
        var prevTok = Environment.GetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_BETA_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_URL", "https://unit.test/gateway/");
            Environment.SetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_BETA_TOKEN", "unit-test-beta-token");

            var settings = GatewayOnSettings();
            settings.LiveTools = new KyraLiveToolsSettings { StocksEnabled = false };

            var use = KyraRealtimeResearchClassifier.ShouldUseRealtimeGateway(
                "stock market changes today",
                settings,
                out var gi);
            Assert.True(use);
            Assert.Equal("finance", gi);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_URL", prevUrl);
            Environment.SetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_BETA_TOKEN", prevTok);
        }
    }

    [Fact]
    public void KyraIntentRouter_EnvConfiguration_GoesToForgerEmsQuestion()
    {
        var intent = KyraIntentRouter.DetectIntent("How do I update my env variables for you?");
        Assert.Equal(KyraIntent.ForgerEMSQuestion, intent);
    }

    [Fact]
    public void KyraEnvHelpAnswer_IsWindowsFirst_WithPowerShellExamples()
    {
        var text = LocalRulesCopilotEngine.GenerateReply(
            "How do I update my environment variables for Kyra?",
            new CopilotContext { Intent = KyraIntent.ForgerEMSQuestion, UserQuestion = "How do I update my environment variables for Kyra?" });
        Assert.Contains("[Environment]::SetEnvironmentVariable", text, StringComparison.Ordinal);
        Assert.Contains("FORGEREMS_KYRA_GATEWAY_URL", text, StringComparison.Ordinal);
        Assert.Contains("restart ForgerEMS", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not paste API keys", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KyraProviderPromptBuilder_MathQuestion_DoesNotInjectConversationRecap()
    {
        var context = new CopilotContext
        {
            UserQuestion = "whats ten times ten",
            ContextText = "User question: whats ten times ten",
            Intent = KyraIntent.GeneralTechQuestion,
            ConversationHistory =
            [
                new CopilotChatMessage { Role = "You", Text = "How do I set environment variables?" },
                new CopilotChatMessage { Role = "Kyra", Text = "Use FORGEREMS_KYRA_GATEWAY_URL in user environment." }
            ]
        };

        var merged = KyraProviderPromptBuilder.AppendConversationRecap(context.ContextText, context);
        Assert.DoesNotContain("FORGEREMS_KYRA_GATEWAY_URL", merged, StringComparison.Ordinal);
        Assert.DoesNotContain("environment variables", merged, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KyraSimpleMath_AfterEnvTopic_LocalRules_DoesNotMentionEnv()
    {
        var ctx = new CopilotContext
        {
            Intent = KyraIntent.GeneralTechQuestion,
            UserQuestion = "whats ten times ten",
            ConversationHistory =
            [
                new CopilotChatMessage { Role = "You", Text = "How do I update my env variables for you?" },
                new CopilotChatMessage
                {
                    Role = "Kyra",
                    Text = "Use PowerShell SetEnvironmentVariable for FORGEREMS_KYRA_GATEWAY_URL then restart."
                }
            ]
        };
        var answer = LocalRulesCopilotEngine.GenerateReply("whats ten times ten", ctx);
        Assert.Contains("100", answer, StringComparison.Ordinal);
        Assert.DoesNotContain("gateway", answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("environment", answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KyraCodeAssist_PromptBuilder_RemainsIsolatedFromHistory()
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
                new CopilotChatMessage { Role = "You", Text = "env vars" },
                new CopilotChatMessage { Role = "Kyra", Text = "FORGEREMS_KYRA_GATEWAY_URL" }
            ]
        };
        var merged = KyraProviderPromptBuilder.AppendConversationRecap(context.ContextText, context);
        Assert.DoesNotContain("FORGEREMS_KYRA_GATEWAY_URL", merged, StringComparison.Ordinal);
    }

    [Fact]
    public void KyraCodeAssist_AfterBtcPrompt_DoesNotUseMarketHistory()
    {
        var snippet = """
                      public int Add(int a, int b) { return a - b; }
                      """;
        var context = new CopilotContext
        {
            UserQuestion = snippet,
            ContextText = "isolated code question",
            Intent = KyraIntent.CodeAssist,
            ConversationHistory =
            [
                new CopilotChatMessage { Role = "You", Text = "price of BTC today" },
                new CopilotChatMessage { Role = "Kyra", Text = "BTC was about $99,000 from live research." }
            ]
        };

        var merged = KyraProviderPromptBuilder.AppendConversationRecap(context.ContextText, context);

        Assert.DoesNotContain("BTC", merged, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("$99,000", merged, StringComparison.Ordinal);
    }

    [Fact]
    public void KyraGeneralChat_AfterUsbWarning_DoesNotUseWarningHistory()
    {
        var context = new CopilotContext
        {
            UserQuestion = "thanks",
            ContextText = "User question: thanks",
            Intent = KyraIntent.GeneralTechQuestion,
            ConversationHistory =
            [
                new CopilotChatMessage { Role = "You", Text = "Explain this warning: No likely USB targets were detected." },
                new CopilotChatMessage { Role = "Kyra", Text = "No safe removable USB target was found." }
            ]
        };

        var merged = KyraProviderPromptBuilder.AppendConversationRecap(context.ContextText, context);

        Assert.DoesNotContain("USB", merged, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No likely USB", merged, StringComparison.OrdinalIgnoreCase);
    }
}
