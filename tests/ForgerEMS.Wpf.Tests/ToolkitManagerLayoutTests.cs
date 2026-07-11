using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class ToolkitManagerLayoutTests
{
    [Fact]
    public void ToolkitGrid_UsesReadableCompactColumns()
    {
        var xaml = ReadToolkitManagerSection();

        Assert.Contains("Header=\"Location\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Action\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Expected Path\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Plan\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedForDownload", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolkitDetailPanel_HasFullPathAccessButtons()
    {
        var xaml = ReadToolkitManagerSection();

        Assert.Contains("Copy expected path", xaml, StringComparison.Ordinal);
        Assert.Contains("Copy detected path", xaml, StringComparison.Ordinal);
        Assert.Contains("Open containing folder", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedToolkitExpectedFullPath", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedToolkitDetectedFullPath", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolkitGrid_ToolColumn_BindsCatalogStatusTagAndBadges()
    {
        var xaml = ReadToolkitManagerSection();

        Assert.Contains("CatalogStatusTag", xaml, StringComparison.Ordinal);
        Assert.Contains("CatalogBadgesDisplay", xaml, StringComparison.Ordinal);
        Assert.Contains("SafetyBadgesDisplay", xaml, StringComparison.Ordinal);
        Assert.Contains("FreshnessDetailDisplay", xaml, StringComparison.Ordinal);
        Assert.Contains("DataTrigger Binding=\"{Binding CatalogStatusTag}\" Value=\"\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DataTrigger Binding=\"{Binding CatalogBadgesDisplay}\" Value=\"\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolkitManager_FocusesOnInspectionNotUsbBuilderWorkflows()
    {
        var xaml = ReadToolkitManagerSection();

        Assert.Contains("Content=\"Refresh Health\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Open Reports\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Open Toolkit Folder\"", xaml, StringComparison.Ordinal);
        Assert.Contains("OpenToolkitReportsCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("OpenToolkitFolderCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolkitTargetChipText", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolkitTargetHelperText", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolkitManagerFooterGuidanceText", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedToolkitDetailText", xaml, StringComparison.Ordinal);

        Assert.DoesNotContain("Download Plan", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Workspace Profile", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ToolkitDownloadPlanItems", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Download selected managed", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateToolkitCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectVisibleToolkitItemsCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedItem=\"{Binding SelectedUsbTarget}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Advanced link diagnostics", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("VerifyToolkitLinksCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyToolkitQuickFilterCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ToolkitLinkVerificationCountsText", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Secure Boot\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Clear filters\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolkitManager_KeepsInventorySearchAndDropdownFilters()
    {
        var xaml = ReadToolkitManagerSection();

        Assert.Contains("ToolkitSearchText", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolkitFilterOptions", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolkitCategoryFilterOptions", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolkitFamilyFilterOptions", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolkitArchitectureFilterOptions", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolkitBootModeFilterOptions", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolkitSourceTrustFilterOptions", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolkitHealthVerdictCopy_PointsAtRefreshHealthButton()
    {
        var source = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "ViewModels", "MainViewModel.cs"));

        // The action button in MainWindow.xaml is labeled "Refresh Health".
        // User-facing prompts that direct the user to that button must match the label
        // exactly — never "Refresh Toolkit", which does not exist.
        Assert.DoesNotContain("Click Refresh Toolkit", source, StringComparison.Ordinal);
        Assert.Contains(
            "Toolkit health cache invalidated after USB target change. Click Refresh Health.",
            source,
            StringComparison.Ordinal);
        Assert.Contains("Click Refresh Health for ", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupAndUsbSelection_DoNotInvokeToolkitHealthInspection()
    {
        var source = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "ViewModels", "MainViewModel.cs"));

        Assert.DoesNotContain("MaybeAutoRefreshToolkitHealthAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Toolkit health auto-refresh", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Toolkit Manager inspection is intentionally user initiated", source, StringComparison.Ordinal);
        Assert.Contains("private Task RunToolkitHealthScanAsync()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SetupUsb_IsTheOnlyDestructiveActionTile()
    {
        var xaml = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));

        Assert.Contains("x:Key=\"DestructiveActionTileButtonStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding SetupUsbCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource DestructiveActionTileButtonStyle}\" Command=\"{Binding SetupUsbCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(xaml, "DestructiveActionTileButtonStyle}\" Command"));
        Assert.Contains("IsMouseOver", xaml, StringComparison.Ordinal);
        Assert.Contains("IsPressed", xaml, StringComparison.Ordinal);
        Assert.Contains("IsKeyboardFocused", xaml, StringComparison.Ordinal);
        Assert.Contains("IsEnabled", xaml, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(needle, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += needle.Length;
        }

        return count;
    }

    private static string ReadToolkitManagerSection()
    {
        var xaml = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        var tabStart = xaml.IndexOf("<TabItem Header=\"▤  Toolkit Manager\">", StringComparison.Ordinal);
        Assert.True(tabStart >= 0);
        var tabEnd = xaml.IndexOf("<TabItem Header=\"◇  Kyra (Beta)\">", tabStart, StringComparison.Ordinal);
        Assert.True(tabEnd > tabStart);
        return xaml[tabStart..tabEnd];
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
