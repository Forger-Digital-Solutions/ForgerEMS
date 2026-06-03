using VentoyToolkitSetup.Wpf.Models;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraUxPolishTests
{
    [Fact]
    public void ChatMessageMetadata_IsSeparateFromBody()
    {
        var msg = new CopilotChatMessage
        {
            Role = "Kyra",
            Text = "Direct answer only.",
            MetadataSummary = "Online • Sanitized context",
            MetadataDetails = "provider=openrouter",
        };

        Assert.Equal("Direct answer only.", msg.Text);
        Assert.True(msg.HasMetadataSummary);
        Assert.True(msg.HasMetadataDetails);
        Assert.DoesNotContain("provider=", msg.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChatMessageText_NormalizesMarkdownFencesAndBoldMarkers()
    {
        var msg = new CopilotChatMessage
        {
            Role = "Kyra",
            Text = """
                **Code Fix**

                ```csharp
                public int Add(int a, int b)
                {
                    return a + b;
                }
                ```
                """
        };

        Assert.Contains("Code Fix", msg.Text, StringComparison.Ordinal);
        Assert.Contains("public int Add", msg.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("**", msg.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("```", msg.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_BindsCompactMetadataFooterWithoutDetailsExpander()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        Assert.Contains("MetadataSummary", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Details\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding MetadataDetails}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding SourceLabel}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_UsesStaticBackgroundWithoutAnimationControls()
    {
        var root = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "ForgerEMS.Wpf", "MainWindow.xaml.cs"));

        Assert.Contains("Source=\"Assets/ForgerEMS_CommandCenterBackground.png\"", xaml, StringComparison.Ordinal);

        // The named main background image must render the full artwork uncropped
        // (Uniform + centered). A separate blurred filler element may use
        // UniformToFill, so we scope the Stretch assertion to the named element.
        var mainImage = ExtractElementByName(xaml, "CommandCenterBackgroundImage");
        Assert.Contains("Stretch=\"Uniform\"", mainImage, StringComparison.Ordinal);
        Assert.DoesNotContain("Stretch=\"UniformToFill\"", mainImage, StringComparison.Ordinal);

        Assert.DoesNotContain("BackgroundDetailComboBox", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ComboBoxItem Content=\"Animated\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("_animatedBackgroundEnabled", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_backgroundAnimationTimer", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CanRunBackgroundAnimation", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TraceParticle", code, StringComparison.Ordinal);
    }

    private static string ExtractElementByName(string xaml, string elementName)
    {
        var marker = $"x:Name=\"{elementName}\"";
        var start = xaml.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find x:Name=\"{elementName}\" in MainWindow.xaml.");
        var end = xaml.IndexOf("/>", start, StringComparison.Ordinal);
        Assert.True(end > start, $"Could not find self-closing end of {elementName} element.");
        return xaml[start..end];
    }

    [Fact]
    public void MainWindow_KyraChatColumnConstrainsBubbleWidth()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        Assert.Contains("MaxWidth=\"900\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"760\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxWidth=\"840\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainViewModel_NoResponseBodyMetadataFooterStringsRemain()
    {
        var vm = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "ForgerEMS.Wpf", "ViewModels", "MainViewModel.cs"));
        Assert.DoesNotContain("_Kyra · grounded in latest System Intelligence scan", vm, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("online wording assist", vm, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TryBuildLiveToolParseForPrompt", vm, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildLiveDataAnswer_DoesNotUseCutoffStyleWording()
    {
        var engineFile = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "ForgerEMS.Wpf", "Services", "LocalRulesCopilotEngine.cs"));
        Assert.Contains("BuildLiveDataAnswer", engineFile, StringComparison.Ordinal);
        Assert.Contains("KyraLiveToolRouter.LiveToolsUnavailableMessage", engineFile, StringComparison.Ordinal);
        Assert.DoesNotContain("knowledge cutoff", engineFile, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ForgerEMS.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? AppContext.BaseDirectory;
    }
}
