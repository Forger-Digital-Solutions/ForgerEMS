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
        var mainTabIdx = mainXaml.IndexOf("<TabControl x:Name=\"MainTabControl\"", liveIdx, StringComparison.Ordinal);
        Assert.True(liveIdx >= 0 && mainTabIdx > liveIdx);

        var liveRegion = mainXaml[liveIdx..mainTabIdx];
        Assert.DoesNotContain("CopyLogsCommand", liveRegion, StringComparison.Ordinal);
        Assert.DoesNotContain("ClearLogsCommand", liveRegion, StringComparison.Ordinal);
        Assert.DoesNotContain("CopySupportEmailCommand", liveRegion, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenSupportEmailCommand", liveRegion, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowXaml_LiveLogsTab_IsNotPresent()
    {
        // Live Logs was a dedicated TabItem duplicating the side panel / Full Logs
        // overlay; it has been removed so there is exactly one live-logging surface
        // (the side panel) plus its View Full Logs overlay expansion.
        var root = FindRepoRootWithMainWindow();
        var mainXaml = File.ReadAllText(Path.Combine(root, "src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        Assert.DoesNotContain("<TabItem Header=\"Live Logs\">", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("LiveLogsTabTextBox", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("NavLiveLogsButton", mainXaml, StringComparison.Ordinal);
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
        Assert.Contains("LogsText", region, StringComparison.Ordinal);
        Assert.Contains("Content=\"Open support email\"", region, StringComparison.Ordinal);
        Assert.DoesNotContain("Open support email…", region, StringComparison.Ordinal);
        Assert.Contains("<WrapPanel", region, StringComparison.Ordinal);
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
    public void MainWindowXaml_KyraLayout_HasStickyHeaderTranscriptAndComposer()
    {
        var root = FindRepoRootWithMainWindow();
        var mainXaml = File.ReadAllText(Path.Combine(root, "src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        var kyraStart = mainXaml.IndexOf("<TabItem Header=\"◇  Kyra (Beta)\">", StringComparison.Ordinal);
        // Kyra is now immediately followed by Settings (the Diagnostics tab that
        // used to sit between them moved to Dr. Forge).
        var nextTabStart = mainXaml.IndexOf("<TabItem Header=\"☰  Settings\">", StringComparison.Ordinal);
        Assert.True(kyraStart >= 0 && nextTabStart > kyraStart);
        var kyra = mainXaml[kyraStart..nextTabStart];

        Assert.Contains("<RowDefinition Height=\"Auto\" />", kyra, StringComparison.Ordinal);
        Assert.Contains("<RowDefinition Height=\"*\" MinHeight=\"260\" />", kyra, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"KyraChatScrollViewer\"", kyra, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"KyraChatInputBox\"", kyra, StringComparison.Ordinal);

        var headerIdx = kyra.IndexOf("Grid.Row=\"0\"", StringComparison.Ordinal);
        var scrollIdx = kyra.IndexOf("x:Name=\"KyraChatScrollViewer\"", StringComparison.Ordinal);
        var composerIdx = kyra.IndexOf("x:Name=\"KyraChatInputBox\"", StringComparison.Ordinal);
        Assert.True(headerIdx >= 0 && scrollIdx > headerIdx && composerIdx > scrollIdx);

        var scrollEnd = kyra.IndexOf("</ScrollViewer>", scrollIdx, StringComparison.Ordinal);
        Assert.True(scrollEnd > scrollIdx);
        var transcriptRegion = kyra[scrollIdx..scrollEnd];
        Assert.Contains("ItemsSource=\"{Binding CopilotMessages}\"", transcriptRegion, StringComparison.Ordinal);
        Assert.DoesNotContain("KyraQuickActionChipButtonStyle", transcriptRegion, StringComparison.Ordinal);
        Assert.DoesNotContain("Kyra is working", transcriptRegion, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowXaml_KyraComposer_IsBoundedAndButtonsStayCompact()
    {
        var root = FindRepoRootWithMainWindow();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        var styleStart = xaml.IndexOf("x:Key=\"KyraChatInputTextBoxStyle\"", StringComparison.Ordinal);
        var styleEnd = xaml.IndexOf("</Style>", styleStart, StringComparison.Ordinal);
        var style = xaml[styleStart..styleEnd];
        Assert.Contains("MaxHeight\" Value=\"150\"", style, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility\" Value=\"Auto\"", style, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility\" Value=\"Disabled\"", style, StringComparison.Ordinal);

        var composerStart = xaml.IndexOf("x:Name=\"KyraChatInputBox\"", StringComparison.Ordinal);
        var composer = xaml[composerStart..Math.Min(xaml.Length, composerStart + 8000)];
        Assert.Contains("AcceptsReturn=\"True\"", composer, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding SendCopilotMessageCommand}\"", composer, StringComparison.Ordinal);
        Assert.Equal(3, CountOccurrences(composer, "Height=\"42\""));
        Assert.Equal(3, CountOccurrences(composer, "VerticalAlignment=\"Bottom\""));
    }

    [Fact]
    public void MainWindowCodeBehind_KyraAutoScroll_UsesBeginInvokeScrollToEnd()
    {
        var root = FindRepoRootWithMainWindow();
        var code = File.ReadAllText(Path.Combine(root, "src", "ForgerEMS.Wpf", "MainWindow.xaml.cs"));
        Assert.Contains("ScrollKyraChatToLatest()", code, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.BeginInvoke(() => KyraChatScrollViewer?.ScrollToEnd(), DispatcherPriority.Background)", code, StringComparison.Ordinal);
        Assert.Contains("OnCopilotMessagesChanged", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowXaml_StatusChipStyles_StaySharedAndDiagnosticsMissionControlIsGone()
    {
        var root = FindRepoRootWithMainWindow();
        var mainXaml = File.ReadAllText(Path.Combine(root, "src", "ForgerEMS.Wpf", "MainWindow.xaml"));

        // The high-contrast status-chip styles outlived the Diagnostics tab: the
        // Settings tab reuses them for its preview / sensor-stack / update chips,
        // so the shared style definitions must remain.
        Assert.Contains("DiagnosticsStatusChipTextStyle", mainXaml, StringComparison.Ordinal);
        Assert.Contains("DiagnosticsStatusChipReadyStyle", mainXaml, StringComparison.Ordinal);
        Assert.Contains("DiagnosticsStatusChipWarningStyle", mainXaml, StringComparison.Ordinal);
        Assert.Contains("DiagnosticsStatusChipProblemStyle", mainXaml, StringComparison.Ordinal);
        Assert.Contains("DiagnosticsStatusChipNeutralStyle", mainXaml, StringComparison.Ordinal);

        var settingsStart = mainXaml.IndexOf("<TabItem Header=\"☰  Settings\">", StringComparison.Ordinal);
        Assert.True(settingsStart >= 0);
        var settingsRegion = mainXaml[settingsStart..];
        Assert.Contains("DiagnosticsStatusChipTextStyle", settingsRegion, StringComparison.Ordinal);

        // The Diagnostics mission-control / evidence / safety-lab surface is gone.
        Assert.DoesNotContain("Text=\"1) Mission Control\"", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"2) Evidence &amp; Logs\"", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"3) Safety Lab\"", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Style=\"{StaticResource DiagnosticsExpanderStyle}\"", mainXaml, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    [Fact]
    public void KyraAdvancedSettingsXaml_LoadsAndDefinesTabs()
    {
        var root = FindRepoRootWithMainWindow();
        var path = Path.Combine(root, "src", "ForgerEMS.Wpf", "KyraAdvancedSettingsWindow.xaml");
        Assert.True(File.Exists(path));
        var text = File.ReadAllText(path);
        Assert.Contains("Title=\"Kyra AI Settings (Beta)\"", text, StringComparison.Ordinal);
        Assert.Contains("CopilotProviderSettings", text, StringComparison.Ordinal);
        Assert.Contains("Header=\"Overview\"", text, StringComparison.Ordinal);
        Assert.Contains("Header=\"Providers\"", text, StringComparison.Ordinal);
        Assert.Contains("Header=\"Bring Your Own Key\"", text, StringComparison.Ordinal);
        Assert.Contains("Header=\"Live Tools\"", text, StringComparison.Ordinal);
        Assert.Contains("Header=\"Privacy &amp; Context\"", text, StringComparison.Ordinal);
        Assert.Contains("Header=\"Local AI\"", text, StringComparison.Ordinal);
        Assert.Contains("Header=\"Diagnostics\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Debug / Source\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Provider Status Help\"", text, StringComparison.Ordinal);
        var localStart = text.IndexOf("Header=\"Local AI\"", StringComparison.Ordinal);
        var diagnosticsStart = text.IndexOf("Header=\"Diagnostics\"", StringComparison.Ordinal);
        Assert.True(localStart >= 0 && diagnosticsStart > localStart);
        var localRegion = text[localStart..diagnosticsStart];
        Assert.Contains("ItemsSource=\"{Binding LocalAiProviderSettings}\"", localRegion, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemsSource=\"{Binding CopilotProviderSettings}\"", localRegion, StringComparison.Ordinal);
        Assert.Contains("No cloud API key required.", localRegion, StringComparison.Ordinal);
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
        Assert.Contains("Content=\"Save key\"", text, StringComparison.Ordinal);
        Assert.Contains("Content=\"Clear saved key\"", text, StringComparison.Ordinal);
        Assert.Contains("Content=\"Use as default\"", text, StringComparison.Ordinal);
        Assert.Contains("Content=\"Test connection\"", text, StringComparison.Ordinal);
        Assert.Contains("Content=\"Refresh Status\"", text, StringComparison.Ordinal);
        Assert.Contains("Content=\"Copy Diagnostics\"", text, StringComparison.Ordinal);
        Assert.Contains("PasswordChanged=\"OnProviderPasswordChanged\"", text, StringComparison.Ordinal);
        Assert.Contains("Content=\"Close\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void KyraAdvancedSettingsXaml_PrivacyAndApiHelpTextsPresent()
    {
        var root = FindRepoRootWithMainWindow();
        var path = Path.Combine(root, "src", "ForgerEMS.Wpf", "KyraAdvancedSettingsWindow.xaml");
        var text = File.ReadAllText(path);
        Assert.Contains("API keys are never sent to support email", text, StringComparison.Ordinal);
        Assert.Contains("Private documents are not automatically sent", text, StringComparison.Ordinal);
        Assert.Contains("Advanced environment setup", text, StringComparison.Ordinal);
        Assert.Contains("Show technical diagnostics", text, StringComparison.Ordinal);
        Assert.Contains("IsExpanded=\"False\"", text, StringComparison.Ordinal);
        Assert.Contains("Web Search", text, StringComparison.Ordinal);
        Assert.Contains("Gateway search available", text, StringComparison.Ordinal);
        Assert.DoesNotContain("developer-managed", text, StringComparison.OrdinalIgnoreCase);
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
