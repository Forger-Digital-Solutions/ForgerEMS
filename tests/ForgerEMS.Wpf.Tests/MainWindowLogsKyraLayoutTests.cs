using System.IO;

namespace ForgerEMS.Wpf.Tests;

public sealed class MainWindowLogsKyraLayoutTests
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
    public void MainWindowXaml_LiveLogsPanel_IsCompactMiniTerminal()
    {
        var root = FindRepoRootWithMainWindow();
        var mainXaml = File.ReadAllText(Path.Combine(root, "src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        Assert.Contains("Text=\"Live Logs\"", mainXaml, StringComparison.Ordinal);
        Assert.Contains("View Full Logs", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("LogPanelToggle", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Hide\"", mainXaml, StringComparison.Ordinal);

        var liveIdx = mainXaml.IndexOf("Text=\"Live Logs\"", StringComparison.Ordinal);
        var fullLogsIdx = mainXaml.IndexOf("FullLogsOverlay", StringComparison.Ordinal);
        Assert.True(liveIdx >= 0 && fullLogsIdx > liveIdx);

        var liveRegion = mainXaml[liveIdx..fullLogsIdx];
        Assert.DoesNotContain("CopyLogsCommand", liveRegion, StringComparison.Ordinal);
        Assert.DoesNotContain("ClearLogsCommand", liveRegion, StringComparison.Ordinal);
        Assert.DoesNotContain("CopySupportEmailCommand", liveRegion, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenSupportEmailCommand", liveRegion, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowXaml_FullLogsOverlay_HasLogAndSupportActions()
    {
        var root = FindRepoRootWithMainWindow();
        var mainXaml = File.ReadAllText(Path.Combine(root, "src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        var idx = mainXaml.IndexOf("FullLogsOverlay", StringComparison.Ordinal);
        Assert.True(idx >= 0);
        var region = mainXaml[idx..];
        Assert.Contains("CopyLogsCommand", region, StringComparison.Ordinal);
        Assert.Contains("ClearLogsCommand", region, StringComparison.Ordinal);
        Assert.Contains("CopySupportEmailCommand", region, StringComparison.Ordinal);
        Assert.Contains("OpenSupportEmailCommand", region, StringComparison.Ordinal);
        Assert.Contains("CopyBetaReportTemplateCommand", region, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowXaml_KyraTab_UsesAdvancedDialog_NotInlineProviderList()
    {
        var root = FindRepoRootWithMainWindow();
        var mainXaml = File.ReadAllText(Path.Combine(root, "src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        Assert.Contains("OpenKyraAdvancedSettingsCommand", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ToggleCopilotAdvancedProvidersCommand", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CopilotProviderSettings", mainXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void KyraAdvancedSettingsXaml_LoadsAndDefinesTabs()
    {
        var root = FindRepoRootWithMainWindow();
        var path = Path.Combine(root, "src", "ForgerEMS.Wpf", "KyraAdvancedSettingsWindow.xaml");
        Assert.True(File.Exists(path));
        var text = File.ReadAllText(path);
        Assert.Contains("Title=\"Kyra Advanced Settings\"", text, StringComparison.Ordinal);
        Assert.Contains("CopilotProviderSettings", text, StringComparison.Ordinal);
        Assert.Contains("Header=\"Providers\"", text, StringComparison.Ordinal);
        Assert.Contains("Header=\"Privacy\"", text, StringComparison.Ordinal);
        Assert.Contains("Header=\"System Context\"", text, StringComparison.Ordinal);
        Assert.Contains("Header=\"Debug / Source\"", text, StringComparison.Ordinal);
        Assert.Contains("Header=\"API Setup Help\"", text, StringComparison.Ordinal);
        Assert.Contains("Assets/KyraAdvancedBackground.png", text, StringComparison.Ordinal);
        Assert.Contains("KyraAdvancedTabControlStyle", text, StringComparison.Ordinal);
        Assert.Contains("KyraAdvancedTabItemStyle", text, StringComparison.Ordinal);
        Assert.Contains("KyraAdvancedCardBackground", text, StringComparison.Ordinal);
        Assert.Contains("#FF0B1220", text, StringComparison.Ordinal);
        Assert.Contains("#FFF2E6D5", text, StringComparison.Ordinal);
        Assert.Contains("#CC07101D", text, StringComparison.Ordinal);
    }

    [Fact]
    public void KyraAdvancedSettingsXaml_HasProviderActionsAndFooter()
    {
        var root = FindRepoRootWithMainWindow();
        var path = Path.Combine(root, "src", "ForgerEMS.Wpf", "KyraAdvancedSettingsWindow.xaml");
        var text = File.ReadAllText(path);
        Assert.Contains("Clear Session Keys", text, StringComparison.Ordinal);
        Assert.Contains("Refresh Provider Status", text, StringComparison.Ordinal);
        Assert.Contains("Test Connection", text, StringComparison.Ordinal);
        Assert.Contains("Tip: Advanced settings affect how Kyra chooses providers and protects privacy.", text, StringComparison.Ordinal);
        Assert.Contains("Content=\"Close\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void KyraAdvancedSettingsXaml_PrivacyAndApiHelpTextsPresent()
    {
        var root = FindRepoRootWithMainWindow();
        var path = Path.Combine(root, "src", "ForgerEMS.Wpf", "KyraAdvancedSettingsWindow.xaml");
        var text = File.ReadAllText(path);
        Assert.Contains("Never sent: serials, usernames, file paths, logs, API keys, passwords, product/license keys.", text, StringComparison.Ordinal);
        Assert.Contains("Do not paste API keys into chat. Use Provider Settings or Windows environment variables.", text, StringComparison.Ordinal);
        foreach (var env in new[]
                 {
                     "GEMINI_API_KEY",
                     "GROQ_API_KEY",
                     "OPENROUTER_API_KEY",
                     "CEREBRAS_API_KEY",
                     "MISTRAL_API_KEY",
                     "GITHUB_MODELS_TOKEN",
                     "CLOUDFLARE_API_KEY",
                     "CLOUDFLARE_ACCOUNT_ID",
                     "OPENAI_API_KEY",
                     "ANTHROPIC_API_KEY"
                 })
        {
            Assert.Contains(env, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void KyraAdvancedSettingsXaml_AvoidsWhiteDefaultSurfacesAndRawKeyValues()
    {
        var root = FindRepoRootWithMainWindow();
        var path = Path.Combine(root, "src", "ForgerEMS.Wpf", "KyraAdvancedSettingsWindow.xaml");
        var text = File.ReadAllText(path);
        Assert.DoesNotContain("#FFF4F1EA", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"White\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-", text, StringComparison.OrdinalIgnoreCase);
    }
}
