using System.Linq;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class UsbBuilderProfileTriStateTests
{
    private static UsbBuilderProfileOption BuildLinuxOption()
    {
        var def = UsbBuilderProfileCatalog.GetRequired("linux-rescue");
        var option = UsbBuilderProfileOption.FromDefinition(def, included: true);
        option.LoadItems(UsbBuilderProfileItemCatalog.ForCategory("linux-rescue"),
            preselectedItemIds: null); // null = required + recommended baseline
        return option;
    }

    [Fact]
    public void NewlyLoadedOption_IsInRecommendedState()
    {
        var option = BuildLinuxOption();
        Assert.Equal(UsbBuilderProfileCategorySelectionState.Recommended, option.SelectionState);
        Assert.Equal("Recommended", option.SelectionStateLabel);
    }

    [Fact]
    public void TogglingOneOptionalItem_TransitionsToPartial()
    {
        var option = BuildLinuxOption();
        var optional = option.Items.First(i => i.Tier == UsbBuilderProfileItemTier.Optional);
        optional.IsSelected = true;
        Assert.Equal(UsbBuilderProfileCategorySelectionState.Partial, option.SelectionState);
    }

    [Fact]
    public void SelectingEveryToggleableItem_TransitionsToFull()
    {
        var option = BuildLinuxOption();
        option.ApplyItemSelection(_ => true);
        Assert.Equal(UsbBuilderProfileCategorySelectionState.Full, option.SelectionState);
    }

    [Fact]
    public void DeselectingEverything_TransitionsToNone()
    {
        var option = BuildLinuxOption();
        option.ApplyItemSelection(_ => false);
        Assert.Equal(UsbBuilderProfileCategorySelectionState.None, option.SelectionState);
    }

    [Fact]
    public void SelectRecommendedDoesNotSelectLargeIsos()
    {
        var option = BuildLinuxOption();
        // Recommended seed picks the curated baseline. The expensive ISOs
        // (Fedora/Rocky/Alma/Proxmox, LargePayload=true) must remain optional.
        var largeSelected = option.Items
            .Where(i => i.IsSelected && i.LargePayload)
            .Select(i => i.Id)
            .ToList();

        // Ubuntu (~5 GB) is intentionally Recommended; we accept that but no
        // *additional* large ISOs should be selected by default.
        Assert.True(largeSelected.Count <= 1,
            $"Recommended baseline selected too many large ISOs: {string.Join(", ", largeSelected)}");
    }

    [Fact]
    public void FullCategorySelection_IsNotTheRecommendedDefault()
    {
        var option = BuildLinuxOption();
        Assert.NotEqual(UsbBuilderProfileCategorySelectionState.Full, option.SelectionState);
    }

    [Fact]
    public void DeselectingAllItems_AlsoFlipsCategoryIncludedOff()
    {
        var option = BuildLinuxOption();
        Assert.True(option.IsIncluded);
        option.ApplyItemSelection(_ => false);
        Assert.False(option.IsIncluded);
    }
}
