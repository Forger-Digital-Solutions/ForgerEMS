using System.IO;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraUnifiedMemoryTests
{
    private static string FindRepoRootWithMainWindow()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 16; i++)
        {
            var candidate = Path.Combine(dir, "src", "ForgerEMS.Wpf", "MainWindow.xaml");
            if (File.Exists(candidate))
            {
                return dir;
            }

            var parent = Directory.GetParent(dir);
            if (parent is null)
            {
                break;
            }

            dir = parent.FullName;
        }

        throw new InvalidOperationException("Could not locate repo root (MainWindow.xaml).");
    }

    [Fact]
    public void KyraConversationMemory_ThatUsbFollowUp_ResolvesToUsbIntent()
    {
        var memory = new KyraConversationMemory();
        memory.AddTurn(
            "usb help",
            "Pick a removable USB drive; never the Windows OS volume.",
            KyraIntent.USBBuilderHelp,
            new SystemContext());

        var resolved = memory.ResolveIntent("What about that USB?", KyraIntent.GeneralTechQuestion);
        Assert.Equal(KyraIntent.USBBuilderHelp, resolved);
    }

    [Fact]
    public void KyraConversationMemory_FixThoseIssuesFollowUp_ResolvesToPriorDeviceIntent()
    {
        var memory = new KyraConversationMemory();
        memory.AddTurn(
            "what is wrong",
            """
            Short answer:
            What I found:
            TPM not ready; VirtualBox adapter has APIPA; storage health unknown.
            """,
            KyraIntent.SystemHealthSummary,
            new SystemContext());

        var resolved = memory.ResolveIntent("How do I fix those issues?", KyraIntent.GeneralTechQuestion);
        Assert.Equal(KyraIntent.SystemHealthSummary, resolved);
    }

    [Fact]
    public void KyraProviderPromptBuilder_AppendsConversationRecapForProviders()
    {
        var context = new CopilotContext
        {
            UserQuestion = "How do I fix those issues?",
            ContextText = "Sanitized summary for this turn.",
            Intent = KyraIntent.SystemHealthSummary,
            ConversationHistory =
            [
                new CopilotChatMessage { Role = "You", Text = "What is wrong with my PC?" },
                new CopilotChatMessage { Role = "Kyra", Text = "What I found: TPM not ready; storage health unknown." }
            ]
        };

        var prompt = KyraProviderPromptBuilder.AppendConversationRecap(context.ContextText, context);
        Assert.Contains("Recent Kyra conversation", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TPM not ready", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sanitized summary", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KyraAdvancedSettingsXaml_DoesNotAskTestersToEnterApiKeys()
    {
        var root = FindRepoRootWithMainWindow();
        var path = Path.Combine(root, "src", "ForgerEMS.Wpf", "KyraAdvancedSettingsWindow.xaml");
        var text = File.ReadAllText(path);
        Assert.DoesNotContain("enter your api key", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("add your api key", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Provider Status Help", text, StringComparison.Ordinal);
    }

    [Fact]
    public void KyraConversationMemory_ExplainSimpler_ResolvesToPreviousIntent()
    {
        var memory = new KyraConversationMemory();
        memory.AddTurn(
            "Why is my PC slow?",
            "What I'm seeing: storage health unknown; many startup apps.",
            KyraIntent.PerformanceLag,
            new SystemContext());

        var resolved = memory.ResolveIntent("Can you explain that simpler?", KyraIntent.GeneralTechQuestion);
        Assert.Equal(KyraIntent.PerformanceLag, resolved);
    }

    [Fact]
    public void KyraConversationMemory_GetState_TracksRollingFields()
    {
        var memory = new KyraConversationMemory();
        memory.AddTurn(
            "Toolkit shows managed missing",
            "Short answer: refresh downloads for the toolkit manifest.",
            KyraIntent.ToolkitManagerHelp,
            new SystemContext { Device = "Contoso Laptop", CPU = "Test CPU", RAM = 16, GPU = "Test GPU", Storage = "SSD OK", OS = "Windows 11" });

        var state = memory.GetState();
        Assert.Equal(KyraIntent.ToolkitManagerHelp, state.LastIntent);
        Assert.Contains("Toolkit", state.LastKnownToolkitIssue, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Contoso", state.LastKnownDeviceReference, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(state.RollingSummary));
    }

    [Fact]
    public void LocalRules_BetaApiKeyQuestion_ReturnsOperatorManagedGuidance()
    {
        var text = LocalRulesCopilotEngine.GenerateReply(
            "How do I add an API key?",
            new CopilotContext
            {
                UserQuestion = "How do I add an API key?",
                Intent = KyraIntent.GeneralTechQuestion,
                SystemContext = new SystemContext()
            });

        Assert.Contains("environment", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("beta", text, StringComparison.OrdinalIgnoreCase);
    }
}
