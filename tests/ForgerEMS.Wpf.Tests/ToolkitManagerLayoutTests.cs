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

    [Fact]
    public void ToolkitGrid_ToolColumn_BindsCatalogStatusTagAndBadges()
    {
        var xaml = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));

        // Chip tag binding present.
        Assert.Contains("CatalogStatusTag", xaml, StringComparison.Ordinal);
        // Badges subtitle binding present.
        Assert.Contains("CatalogBadgesDisplay", xaml, StringComparison.Ordinal);
        Assert.Contains("SafetyBadgesDisplay", xaml, StringComparison.Ordinal);
        Assert.Contains("FreshnessDetailDisplay", xaml, StringComparison.Ordinal);
        // Chip and subtitle are gated on the binding being non-empty so legacy items
        // (no catalog metadata) render unchanged.
        Assert.Contains("DataTrigger Binding=\"{Binding CatalogStatusTag}\" Value=\"\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DataTrigger Binding=\"{Binding CatalogBadgesDisplay}\" Value=\"\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolkitManager_HasPlanningPanelAndProfileControls()
    {
        var xaml = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));

        Assert.Contains("SelectedForDownload", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolkitDownloadPlanItems", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolkitDownloadPlanStorageText", xaml, StringComparison.Ordinal);
        Assert.Contains("ValidateToolkitDownloadPlanCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolkitProfileOptions", xaml, StringComparison.Ordinal);
        Assert.Contains("SaveToolkitProfileCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("LoadToolkitProfileCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("DownloadSelectedManagedItemsCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("CancelSelectedManagedDownloadsCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedManagedDownloadQueueItems", xaml, StringComparison.Ordinal);
        Assert.Contains("CopySelectedManualInstructionsCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("OpenSelectedVendorPageCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("Review update", xaml, StringComparison.Ordinal);
        Assert.Contains("PlanSectionLabel", xaml, StringComparison.Ordinal);
        Assert.Contains("FreshnessLabel", xaml, StringComparison.Ordinal);
        Assert.Contains("Ready to download", xaml, StringComparison.Ordinal);
        Assert.Contains("Manual required", xaml, StringComparison.Ordinal);
        Assert.Contains("Blocked / needs attention", xaml, StringComparison.Ordinal);
        Assert.Contains("Checksum verified", xaml, StringComparison.Ordinal);
        Assert.Contains("Checksum limited", xaml, StringComparison.Ordinal);
        Assert.Contains("Managed download", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolkitManager_HasMetadataFiltersAndQuickChips()
    {
        var xaml = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));

        Assert.Contains("ToolkitFamilyFilterOptions", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolkitArchitectureFilterOptions", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolkitBootModeFilterOptions", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolkitSourceTrustFilterOptions", xaml, StringComparison.Ordinal);
        Assert.Contains("ApplyToolkitQuickFilterCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectVisibleToolkitItemsCommand", xaml, StringComparison.Ordinal);
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
