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
    public void MainWindow_VisualEffectsDefaultIsStaticLowPower()
    {
        var root = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "ForgerEMS.Wpf", "MainWindow.xaml.cs"));

        Assert.Contains("Text=\"Visual Effects\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Off / Plain dark", xaml, StringComparison.Ordinal);
        Assert.Contains("Static / Low Power", xaml, StringComparison.Ordinal);
        Assert.Contains("Animated", xaml, StringComparison.Ordinal);
        Assert.Contains("private VisualEffectsMode _visualEffectsMode = VisualEffectsMode.Static;", code, StringComparison.Ordinal);
        Assert.Contains("private bool _animatedBackgroundEnabled;", code, StringComparison.Ordinal);
        Assert.Contains("CanRunBackgroundAnimation()", code, StringComparison.Ordinal);
        Assert.Contains("WindowState != WindowState.Minimized", code, StringComparison.Ordinal);
        Assert.DoesNotContain("private bool _animatedBackgroundEnabled = true;", code, StringComparison.Ordinal);
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
        var serviceFile = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "ForgerEMS.Wpf", "Services", "CopilotService.cs"));
        Assert.Contains("BuildLiveDataAnswer", serviceFile, StringComparison.Ordinal);
        Assert.Contains("KyraLiveToolRouter.LiveToolsUnavailableMessage", serviceFile, StringComparison.Ordinal);
        Assert.DoesNotContain("knowledge cutoff", serviceFile, StringComparison.OrdinalIgnoreCase);
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
