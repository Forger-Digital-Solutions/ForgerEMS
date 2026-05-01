using System.IO;
using System.Reflection;
using VentoyToolkitSetup.Wpf.ViewModels;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Header should stay compact: no Copy/Open support buttons (those stay in Full Logs).
/// </summary>
public sealed class MainWindowHeaderSupportTests
{
    private static string FindRepoRootContainingMainWindow()
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

        throw new InvalidOperationException("Could not locate repo root (MainWindow.xaml). BaseDirectory=" + AppContext.BaseDirectory);
    }

    [Fact]
    public void MainWindowXaml_HeaderHasMailtoHyperlink_NotCopyOpenButtons()
    {
        var repoRoot = FindRepoRootContainingMainWindow();
        var xamlPath = Path.Combine(repoRoot, "src", "ForgerEMS.Wpf", "MainWindow.xaml");
        Assert.True(File.Exists(xamlPath), "Expected MainWindow.xaml at " + xamlPath);

        var text = File.ReadAllText(xamlPath);
        Assert.Contains("SupportMailtoUri", text, StringComparison.Ordinal);
        Assert.Contains("RequestNavigate=\"SupportMailto_OnRequestNavigate\"", text, StringComparison.Ordinal);
        Assert.Contains("Beta issue? Send logs/screenshots to", text, StringComparison.Ordinal);
        Assert.Contains("ForgerDigitalSolutions@outlook.com", text, StringComparison.Ordinal);

        var headerEnd = text.IndexOf("FullLogsOverlay", StringComparison.Ordinal);
        Assert.True(headerEnd > 0);
        var headerRegion = text[..headerEnd];
        Assert.DoesNotContain("Copy support email", headerRegion, StringComparison.Ordinal);

        Assert.Contains("NavigateUri=\"{Binding SupportMailtoUri, Mode=OneWay}\"", text, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding SupportEmailAddress, Mode=OneWay}\"", text, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding SupportEmailDoNotSecretsText, Mode=OneWay}\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MainViewModel_SupportEmailProperties_AreReadOnly()
    {
        foreach (var name in new[] { nameof(MainViewModel.SupportEmailAddress), nameof(MainViewModel.SupportMailtoUri) })
        {
            var prop = typeof(MainViewModel).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(prop);
            Assert.False(prop!.CanWrite, name + " should remain display-only (no setter).");
        }
    }
}
