using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class DiagnosticsMissionControlLayoutTests
{
    [Fact]
    public void DiagnosticsLayout_UsesMissionControlSections()
    {
        var xaml = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        Assert.Contains("Text=\"1) Mission Control\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"2) Action Center\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"3) Evidence &amp; Logs\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"4) Safety Lab\" IsExpanded=\"False\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsLayout_CommandCenter_IsAllowlistedAndNoArbitraryInput()
    {
        var xaml = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        Assert.Contains("Content=\"Check WSL installed\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Check PowerShell version\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Check backend files\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Check release identity\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Check network/DNS\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RunWslRunnerCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("WslRunnerCommandInput", xaml, StringComparison.Ordinal);
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
