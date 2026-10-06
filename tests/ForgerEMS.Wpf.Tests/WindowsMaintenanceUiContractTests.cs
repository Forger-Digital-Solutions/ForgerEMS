using System;
using System.IO;
using System.Linq;
using VentoyToolkitSetup.Wpf.ViewModels;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Wiring contract for the read-only Windows maintenance card (Driver Hub tab)
/// and the managed-resource "Check Everything" card (Toolkit Manager tab).
/// The ViewModel surface existed VM-only before; these tests keep the XAML
/// bindings real and the cards read-only (no install/delete/repair controls).
/// </summary>
public sealed class WindowsMaintenanceUiContractTests
{
    [Fact]
    public void DriverHubTab_WindowsMaintenanceCard_IsWiredReadOnly()
    {
        var tab = ExtractTab(LoadMainWindowXaml(), "▥  Driver Hub");

        Assert.Contains("Header=\"Windows maintenance (read-only)\"", tab, StringComparison.Ordinal);
        Assert.Contains("Content=\"Scan Windows maintenance\"", tab, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ScanWindowsMaintenanceCommand}\"", tab, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding CancelWindowsMaintenanceScanCommand}\"", tab, StringComparison.Ordinal);
        Assert.Contains("Content=\"Open Windows Update settings\"", tab, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding OpenWindowsUpdateSettingsCommand}\"", tab, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding WindowsMaintenanceStatusText}\"", tab, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding WindowsMaintenanceDetailText}\"", tab, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding WindowsMaintenanceFindings}\"", tab, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding WindowsMaintenanceDriverAssessments}\"", tab, StringComparison.Ordinal);

        // Diagnostic card only — no mutation affordances.
        Assert.DoesNotContain("Install driver", tab, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Remove driver", tab, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Roll back", tab, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Repair driver", tab, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Check for updates now", tab, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolkitManagerTab_CheckEverythingCard_IsWired()
    {
        var tab = ExtractTab(LoadMainWindowXaml(), "▤  Toolkit Manager");

        Assert.Contains("Header=\"Managed resource check\"", tab, StringComparison.Ordinal);
        Assert.Contains("Content=\"Check Everything\"", tab, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding CheckEverythingResourcesCommand}\"", tab, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ResourceCheckStatusText}\"", tab, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding ResourceCheckResults}\"", tab, StringComparison.Ordinal);
        Assert.Contains("Nothing is downloaded", tab, StringComparison.Ordinal);
    }

    [Fact]
    public void BoundMembers_ExistOnMainViewModel()
    {
        var vmType = typeof(MainViewModel);
        foreach (var member in new[]
        {
            "ScanWindowsMaintenanceCommand",
            "CancelWindowsMaintenanceScanCommand",
            "CheckEverythingResourcesCommand",
            "OpenWindowsUpdateSettingsCommand",
            "WindowsMaintenanceStatusText",
            "WindowsMaintenanceDetailText",
            "WindowsMaintenanceFindings",
            "WindowsMaintenanceDriverAssessments",
            "ResourceCheckStatusText",
            "ResourceCheckResults",
            "WindowsMaintenanceBusy"
        })
        {
            Assert.NotNull(vmType.GetProperty(member));
        }
    }

    private static string LoadMainWindowXaml() =>
        File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));

    private static string ExtractTab(string xaml, string header)
    {
        const string mainTabIndent = "                    ";
        var start = xaml.IndexOf(mainTabIndent + $"<TabItem Header=\"{header}\">", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find tab '{header}'.");

        var end = xaml.IndexOf("\n" + mainTabIndent + "<TabItem Header=", start + 1, StringComparison.Ordinal);
        return end > start ? xaml[start..end] : xaml[start..];
    }

    private static string FindRepoFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
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
