using Xunit;

namespace ForgerEMS.Wpf.Tests;

// The Diagnostics tab (Mission Control / Evidence & Logs / Safety Lab / Command
// Center) was removed from the main ForgerEMS shell. Full diagnostics moved to
// Dr. Forge. These tests guard that the surface — and its allowlisted command
// center / safety-lab actions — does not reappear in MainWindow.xaml.
public sealed class DiagnosticsMissionControlLayoutTests
{
    [Fact]
    public void DiagnosticsMissionControlSections_AreRemovedFromShell()
    {
        var xaml = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        Assert.DoesNotContain("<TabItem Header=\"⚙  Diagnostics\">", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"1) Mission Control\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"2) Evidence &amp; Logs\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"3) Safety Lab\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsCommandCenterAndSafetyLab_ActionsAreRemovedFromShell()
    {
        var xaml = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        Assert.DoesNotContain("Content=\"Check WSL installed\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Check PowerShell version\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Check backend files\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Check release identity\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Check network/DNS\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CopySafeTestingSummaryCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AnalyzeLinkSafetyCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AnalyzeLocalFileSafetyCommand", xaml, StringComparison.Ordinal);
        // An embedded arbitrary-command terminal must never appear.
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
