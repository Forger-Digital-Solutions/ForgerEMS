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
        Assert.Contains("Official driver apps, OEM support, GPU tools, firmware guidance, and Linux driver help.", tab, StringComparison.Ordinal);
        Assert.Contains("Official links only • No auto BIOS flashing • No driver installs without your action", tab, StringComparison.Ordinal);
        Assert.Contains("Recommended for this PC", tab, StringComparison.Ordinal);
        Assert.Contains("Detected hardware", tab, StringComparison.Ordinal);
        Assert.Contains("Search NVIDIA, Intel, Dell, Wi-Fi, Linux, BIOS...", tab, StringComparison.Ordinal);
        Assert.Contains("No Driver Hub cards match your filter.", tab, StringComparison.Ordinal);

        var vmCode = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "ViewModels", "MainViewModel.DriverHub.cs"));
        Assert.Contains("Run System Intelligence to personalize recommendations.", vmCode, StringComparison.Ordinal);
        Assert.Contains("Run System Intelligence to personalize Driver Hub.", vmCode, StringComparison.Ordinal);
        Assert.Contains("Select a USB target to add Driver Hub shortcuts.", vmCode, StringComparison.Ordinal);

        foreach (var filter in new[] { "All", "Recommended", "Driver Apps", "GPU", "OEM", "Network", "Chipset", "BIOS/Firmware", "Linux", "Windows" })
        {
            Assert.Contains($"\"{filter}\"", vmCode, StringComparison.Ordinal);
        }

        Assert.Contains("DriverHubRecommendedBrandTile", tab, StringComparison.Ordinal);
        Assert.Contains("DriverHubCatalogBrandTile", tab, StringComparison.Ordinal);
        Assert.Contains("BrandTileText", tab, StringComparison.Ordinal);
        Assert.Contains("BrandAccentHex", tab, StringComparison.Ordinal);
        Assert.Contains("SafetyStatusText", tab, StringComparison.Ordinal);
        Assert.Contains("DriverHubPrimaryActionButtonStyle", tab, StringComparison.Ordinal);
        Assert.Contains("DriverHubRecommendedPrimaryActionButton", tab, StringComparison.Ordinal);
        Assert.Contains("ExecuteDriverHubPrimaryActionCommand", tab, StringComparison.Ordinal);
        Assert.Contains("PrimaryActionLabel", tab, StringComparison.Ordinal);
        Assert.Contains("SecondaryPageActionLabel", tab, StringComparison.Ordinal);
        Assert.Contains("HasDistinctSecondaryPageAction", tab, StringComparison.Ordinal);
        // Helper actions still exist but live in the overflow popup, not the primary action row.
        Assert.Contains("DriverHubOverflowToggleStyle", tab, StringComparison.Ordinal);
        Assert.Contains("DriverHubOverflowMenuButtonStyle", tab, StringComparison.Ordinal);
        Assert.Contains("DriverHubRecommendedOverflowToggle", tab, StringComparison.Ordinal);
        Assert.Contains("DriverHubCatalogOverflowToggle", tab, StringComparison.Ordinal);
        Assert.Contains("DriverHubRecommendedMoreCopyLinkButton", tab, StringComparison.Ordinal);
        Assert.Contains("DriverHubRecommendedMoreAddShortcutButton", tab, StringComparison.Ordinal);
        Assert.Contains("DriverHubCatalogMoreCopyLinkButton", tab, StringComparison.Ordinal);
        Assert.Contains("DriverHubCatalogMoreAddShortcutButton", tab, StringComparison.Ordinal);
        Assert.Contains("Copy Link", tab, StringComparison.Ordinal);
        Assert.Contains("Add Shortcut to USB", tab, StringComparison.Ordinal);
        Assert.Contains("FirmwareBadgeText", tab, StringComparison.Ordinal);
        Assert.Contains("DriverHubDownloadSafetyText", tab, StringComparison.Ordinal);
        Assert.Contains("Installer downloads only appear for safe official direct URLs", vmCode, StringComparison.Ordinal);
    }

    [Fact]
    public void DriverHub_PrimaryActionRow_DoesNotShowCopyLinkOrAddShortcutAsButtons()
    {
        // Store-style cards: only the primary CTA (and optional small "More" overflow toggle)
        // belong in the visible action row. Copy Link / Add Shortcut helper actions must be
        // tucked into the overflow popup so the card stays uncluttered.
        var xaml = LoadMainWindowXaml();
        var tabStart = xaml.IndexOf("<TabItem Header=\"▥  Driver Hub\">", StringComparison.Ordinal);
        Assert.True(tabStart >= 0);
        var tabEnd = xaml.IndexOf("<TabItem Header=\"◇  Kyra (Beta)\">", tabStart, StringComparison.Ordinal);
        Assert.True(tabEnd > tabStart);
        var tab = xaml[tabStart..tabEnd];

        // Every Copy Link / Add Shortcut button in the Driver Hub tab must live inside
        // the overflow popup (DriverHubOverflowMenuButtonStyle), not the primary action row.
        AssertHelperButtonIsOverflowOnly(tab, "Copy Link");
        AssertHelperButtonIsOverflowOnly(tab, "Add Shortcut to USB");

        // The legacy compact "Add Shortcut" label must not appear as a primary-row button anymore.
        var legacyMatches = AllIndexesOf(tab, "Content=\"Add Shortcut\"");
        Assert.Empty(legacyMatches);
    }

    [Fact]
    public void DriverHub_NoDirectUrlOrInstallerLiteralsInXaml()
    {
        // The app must never embed vendor URLs directly in markup — every link flows through
        // the catalog and is gated by DriverHubUrlSafety. This prevents a stray 404 link from
        // being baked into the tab itself.
        var xaml = LoadMainWindowXaml();
        var tabStart = xaml.IndexOf("<TabItem Header=\"▥  Driver Hub\">", StringComparison.Ordinal);
        Assert.True(tabStart >= 0);
        var tabEnd = xaml.IndexOf("<TabItem Header=\"◇  Kyra (Beta)\">", tabStart, StringComparison.Ordinal);
        Assert.True(tabEnd > tabStart);
        var tab = xaml[tabStart..tabEnd];

        Assert.DoesNotContain("http://", tab, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", tab, StringComparison.Ordinal);
    }

    private static void AssertHelperButtonIsOverflowOnly(string tabXaml, string buttonContent)
    {
        // For every <Button ... Content="<buttonContent>" ...>, look backwards from the button's
        // opening tag and confirm the nearest Style attribute is the overflow menu style.
        var needle = $"Content=\"{buttonContent}\"";
        var index = 0;
        var foundAny = false;
        while (true)
        {
            var hit = tabXaml.IndexOf(needle, index, StringComparison.Ordinal);
            if (hit < 0)
            {
                break;
            }

            foundAny = true;
            var buttonStart = tabXaml.LastIndexOf("<Button", hit, StringComparison.Ordinal);
            Assert.True(buttonStart >= 0, $"Could not locate <Button for '{buttonContent}'.");
            var buttonOpenEnd = tabXaml.IndexOf('>', hit);
            Assert.True(buttonOpenEnd > buttonStart);
            var buttonOpen = tabXaml[buttonStart..(buttonOpenEnd + 1)];
            Assert.Contains(
                "DriverHubOverflowMenuButtonStyle",
                buttonOpen,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "DriverHubPrimaryActionButtonStyle",
                buttonOpen,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "DriverHubSecondaryActionButtonStyle",
                buttonOpen,
                StringComparison.Ordinal);
            index = buttonOpenEnd + 1;
        }

        Assert.True(foundAny, $"Expected at least one '{buttonContent}' button in Driver Hub tab.");
    }

    [Fact]
    public void MainWindow_DriverHubBindingsResolveToViewModelMembers()
    {
        var xaml = LoadMainWindowXaml();
        string[] viewModelBindings =
        {
            "DriverHubRecommendationSummaryText",
            "DriverHubDetectedHardwareText",
            "DriverHubRecommendedEntries",
            "DriverHubSearchText",
            "DriverHubFilterChips",
            "ApplyDriverHubFilterCommand",
            "DriverHubDownloadSafetyText",
            "DriverHubUsbTargetStatusText",
            "DriverHubStatusText",
            "DriverHubEmptyStateText",
            "DriverHubVisibleEntries",
            "ExecuteDriverHubPrimaryActionCommand",
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
