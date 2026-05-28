using System;
using System.IO;
using System.Linq;
using VentoyToolkitSetup.Wpf.ViewModels;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class DriverHubUiContractTests
{
    [Fact]
    public void MainWindow_DriverHubTabAndSidebarNavExist()
    {
        var xaml = LoadMainWindowXaml();
        var codeBehind = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml.cs"));

        Assert.Contains("NavDriverHubButton", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"▥  Driver Hub\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<TabItem Header=\"▥  Driver Hub\">", xaml, StringComparison.Ordinal);
        Assert.Contains("NavDriverHubButton", codeBehind, StringComparison.Ordinal);
        Assert.Single(AllIndexesOf(xaml, "<TabItem Header=\"▥  Driver Hub\">"));
    }

    [Fact]
    public void MainWindow_DriverHubCopyAndControlsExist()
    {
        var xaml = LoadMainWindowXaml();
        var tabStart = xaml.IndexOf("<TabItem Header=\"▥  Driver Hub\">", StringComparison.Ordinal);
        Assert.True(tabStart >= 0);
        var tabEnd = xaml.IndexOf("<TabItem Header=\"◇  Kyra (Beta)\">", tabStart, StringComparison.Ordinal);
        Assert.True(tabEnd > tabStart);
        var tab = xaml[tabStart..tabEnd];

        Assert.Contains("Text=\"Driver Hub\"", tab, StringComparison.Ordinal);
        Assert.Contains("Official driver tools, OEM support links, firmware helpers, and GPU utilities.", tab, StringComparison.Ordinal);
        Assert.Contains("Official driver tools, OEM support links, firmware helpers, and GPU utilities for Windows and Linux.", tab, StringComparison.Ordinal);
        Assert.Contains("ForgerEMS opens official vendor pages and does not auto-flash BIOS/firmware or install drivers without your action.", tab, StringComparison.Ordinal);
        Assert.Contains("Recommended for this PC", tab, StringComparison.Ordinal);
        Assert.Contains("Search drivers, vendors, GPUs, Linux, BIOS...", tab, StringComparison.Ordinal);
        Assert.Contains("No Driver Hub cards match your filter.", tab, StringComparison.Ordinal);
        Assert.Contains("Run System Intelligence to personalize recommendations.", tab, StringComparison.Ordinal);

        var vmCode = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "ViewModels", "MainViewModel.DriverHub.cs"));
        foreach (var filter in new[] { "All", "Recommended", "GPU", "OEM", "Network", "Chipset", "BIOS/Firmware", "Linux", "Windows" })
        {
            Assert.Contains($"\"{filter}\"", vmCode, StringComparison.Ordinal);
        }

        Assert.Contains("Open Official Page", tab, StringComparison.Ordinal);
        Assert.Contains("Copy Link", tab, StringComparison.Ordinal);
        Assert.Contains("Add Shortcut to USB", tab, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_DriverHubBindingsResolveToViewModelMembers()
    {
        var xaml = LoadMainWindowXaml();
        string[] viewModelBindings =
        {
            "DriverHubRecommendationSummaryText",
            "DriverHubRecommendedEntries",
            "DriverHubSearchText",
            "DriverHubFilterChips",
            "ApplyDriverHubFilterCommand",
            "DriverHubUsbTargetStatusText",
            "DriverHubStatusText",
            "DriverHubEmptyStateText",
            "DriverHubVisibleEntries",
            "OpenDriverHubOfficialPageCommand",
            "CopyDriverHubLinkCommand",
            "AddDriverHubShortcutToUsbCommand"
        };

        var vmType = typeof(MainViewModel);
        foreach (var binding in viewModelBindings)
        {
            Assert.Contains(binding, xaml, StringComparison.Ordinal);
            Assert.NotNull(vmType.GetProperty(binding));
        }
    }

    private static string LoadMainWindowXaml() =>
        File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));

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

    private static int[] AllIndexesOf(string text, string value)
    {
        var indexes = new List<int>();
        var start = 0;
        while (true)
        {
            var index = text.IndexOf(value, start, StringComparison.Ordinal);
            if (index < 0)
            {
                break;
            }

            indexes.Add(index);
            start = index + value.Length;
        }

        return indexes.ToArray();
    }
}
