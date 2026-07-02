using System.Collections.Generic;
using System.Linq;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class UsbBuilderProfileItemModelTests
{
    [Fact]
    public void Catalog_SeedsAtLeastFiveItemsPerNonEmptyCategory()
    {
        // Spec asks for "roughly 5–10 useful items per category". Core ships
        // 3 (Ventoy, docs, manifest) because the rest is infrastructure, so we
        // only enforce 5+ for the user-facing categories.
        var minimums = new Dictionary<string, int>
        {
            ["windows"] = 5,
            ["legacy-windows"] = 5,
            ["linux-rescue"] = 5,
            ["macos"] = 5,
            ["android"] = 5,
            ["ios-ipados"] = 5,
            ["oem-tools"] = 5,
            ["diagnostics"] = 5,
        };

        foreach (var (categoryId, min) in minimums)
        {
            var items = UsbBuilderProfileItemCatalog.ForCategory(categoryId);
            Assert.True(items.Count >= min,
                $"Category '{categoryId}' should seed at least {min} items, got {items.Count}.");
        }
    }

    [Fact]
    public void Catalog_AllItemIdsAreUniqueAndNonEmpty()
    {
        var all = UsbBuilderProfileItemCatalog.All;
        Assert.All(all, item => Assert.False(string.IsNullOrWhiteSpace(item.Id)));
        var duplicates = all.GroupBy(i => i.Id, System.StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.Empty(duplicates);
    }

    [Fact]
    public void Catalog_EveryItemBelongsToARegisteredCategory()
    {
        var validCategoryIds = UsbBuilderProfileCatalog.All
            .Select(d => d.CategoryId)
            .ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        var stranded = UsbBuilderProfileItemCatalog.All
            .Where(item => !validCategoryIds.Contains(item.CategoryId))
            .Select(item => item.Id)
            .ToList();
        Assert.Empty(stranded);
    }

    [Fact]
    public void Catalog_IncludesManifestDerivedGranularRows()
    {
        Assert.Contains(UsbBuilderProfileItemCatalog.ForCategory("android"),
            item => string.Equals(item.ManifestEntryName, "Google Pixel OTA Images", System.StringComparison.Ordinal));
        Assert.Contains(UsbBuilderProfileItemCatalog.ForCategory("oem-tools"),
            item => string.Equals(item.ManifestEntryName, "MSI Laptop Support", System.StringComparison.Ordinal));
        Assert.Contains(UsbBuilderProfileItemCatalog.ForCategory("linux-rescue"),
            item => string.Equals(item.ManifestEntryName, "Arch Linux 2026.05.01 (x86_64)", System.StringComparison.Ordinal));
        Assert.Contains(UsbBuilderProfileItemCatalog.ForCategory("diagnostics"),
            item => string.Equals(item.ManifestEntryName, "HWiNFO Download Page", System.StringComparison.Ordinal));
    }

    [Fact]
    public void RequiredItems_CannotBeToggledOff()
    {
        var required = UsbBuilderProfileItemCatalog.All
            .First(i => i.Tier == UsbBuilderProfileItemTier.Required);

        Assert.False(required.CanToggle);
        required.IsSelected = false; // setter normalises Required → true
        Assert.True(required.IsSelected);
    }

    [Fact]
    public void ProjectedNewBytes_ExcludesUserSuppliedAndAlreadyPresentItems()
    {
        var item = UsbBuilderProfileItemCatalog.All
            .First(i => i.RequiresUserSuppliedMedia && i.CanToggle);
        item.IsSelected = true;
        Assert.Equal(0, item.ProjectedNewBytes);

        var managed = UsbBuilderProfileItemCatalog.All
            .First(i => !i.RequiresUserSuppliedMedia &&
                        i.Kind == UsbBuilderProfileItemKind.ManagedDownload &&
                        i.CanToggle);
        managed.IsSelected = true;
        managed.ExistsOnUsb = true;
        Assert.Equal(0, managed.ProjectedNewBytes);

        managed.ExistsOnUsb = false;
        Assert.True(managed.ProjectedNewBytes > 0);
    }
}
