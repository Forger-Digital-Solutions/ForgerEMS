using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class ToolkitManagerLayoutTests
{
    [Fact]
    public void ToolkitGrid_UsesReadableCompactColumns()
    {
        var xaml = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));

        Assert.Contains("Header=\"Location\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Action\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Expected Path\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolkitDetailPanel_HasFullPathAccessButtons()
    {
        var xaml = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));

        Assert.Contains("Copy expected path", xaml, StringComparison.Ordinal);
        Assert.Contains("Copy detected path", xaml, StringComparison.Ordinal);
        Assert.Contains("Open containing folder", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedToolkitExpectedFullPath", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedToolkitDetectedFullPath", xaml, StringComparison.Ordinal);
    }

    private static string FindRepoFile(params string[] segments)
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate repo file.", Path.Combine(segments));
    }
}
