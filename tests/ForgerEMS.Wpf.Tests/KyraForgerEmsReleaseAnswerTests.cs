using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraForgerEmsReleaseAnswerTests
{
    [Fact]
    public void NewestForgerEmsRelease_WithAppVersionLine_UsesLocalMetadataNotLiveUnavailable()
    {
        var text = LocalRulesCopilotEngine.GenerateReply(
            "What is the newest ForgerEMS release?",
            new CopilotContext
            {
                UserQuestion = "What is the newest ForgerEMS release?",
                Intent = KyraIntent.ForgerEMSQuestion,
                PromptMode = CopilotPromptMode.General,
                ContextText = """
                    User question: What is the newest ForgerEMS release?
                    App version: 1.1.12-rc.7
                    """
            });

        Assert.Contains("1.1.12-rc.7", text, StringComparison.Ordinal);
        Assert.Contains("running install", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("don't have live data tools", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NewestForgerEmsRelease_WithoutVersion_DoesNotFabricateWebRelease()
    {
        var text = LocalRulesCopilotEngine.GenerateReply(
            "What is the newest ForgerEMS release?",
            new CopilotContext
            {
                UserQuestion = "What is the newest ForgerEMS release?",
                Intent = KyraIntent.ForgerEMSQuestion,
                PromptMode = CopilotPromptMode.General,
                ContextText = "User question: What is the newest ForgerEMS release?"
            });

        Assert.DoesNotContain("don't have live data tools", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("verified", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("v99.0.0-fake", text, StringComparison.OrdinalIgnoreCase);
    }
}
