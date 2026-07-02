using System.Linq;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.ViewModels;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class CategoryBuilderViewModelTests
{
    private static UsbBuilderProfileOption BuildLinuxOption()
    {
        var def = UsbBuilderProfileCatalog.GetRequired("linux-rescue");
        var option = UsbBuilderProfileOption.FromDefinition(def, included: true);
        option.LoadItems(UsbBuilderProfileItemCatalog.ForCategory("linux-rescue"), null);
        return option;
    }

    [Fact]
    public void Constructor_ClonesParentItemsWithoutMutating()
    {
        var option = BuildLinuxOption();
        var snapshotIds = option.GetSelectedItemIds().ToHashSet();

        var vm = new CategoryBuilderViewModel(option);
        var optionalItem = vm.WorkingItems.First(i => i.Tier == UsbBuilderProfileItemTier.Optional && i.CanToggle);
        optionalItem.IsSelected = true;

        // Parent option should still match the original snapshot — Cancel-equivalent.
        Assert.True(option.GetSelectedItemIds().ToHashSet().SetEquals(snapshotIds));
    }

    [Fact]
    public void Constructor_ExposesCategoryTitleDescriptionAndSelectedSummaries()
    {
        var option = BuildLinuxOption();
        var vm = new CategoryBuilderViewModel(option);

        Assert.Equal(option.DisplayName, vm.CategoryHeader);
        Assert.Equal($"{option.DisplayName} item picker", vm.WindowTitle);
        Assert.Equal(option.ShortDescription, vm.CategoryPurpose);
        Assert.Equal($"{vm.SelectedCount} of {vm.TotalCount} selected", vm.SelectionSummary);
        Assert.Equal(vm.EstimatedSelectedSize, vm.EstimatedSelectedDownloadSize);
        Assert.NotEqual("size unknown", vm.EstimatedSelectedUsbSpace);
        Assert.False(string.IsNullOrWhiteSpace(vm.EstimatedSelectedDownloadSize));
        Assert.False(string.IsNullOrWhiteSpace(vm.EstimatedSelectedUsbSpace));
    }

    [Fact]
    public void CancelCommand_RaisesFalseAndPreservesParentSelection()
    {
        var option = BuildLinuxOption();
        var snapshotIds = option.GetSelectedItemIds().ToHashSet();
        var vm = new CategoryBuilderViewModel(option);
        var optionalItem = vm.WorkingItems.First(i => i.Tier == UsbBuilderProfileItemTier.Optional && i.CanToggle);
        bool? accepted = null;

        optionalItem.IsSelected = true;
        vm.CloseRequested += (_, result) => accepted = result;
        vm.CancelCommand.Execute(null);

        Assert.False(accepted);
        Assert.True(option.GetSelectedItemIds().ToHashSet().SetEquals(snapshotIds));
    }

    [Fact]
    public void CommitTo_AppliesWorkingSetToParent()
    {
        var option = BuildLinuxOption();
        var vm = new CategoryBuilderViewModel(option);

        vm.ClearOptionalCommand.Execute(null);
        vm.SelectAllCommand.Execute(null);
        vm.CommitTo(option);

        Assert.Equal(option.Items.Count, option.Items.Count(i => i.IsSelected));
    }

    [Fact]
    public void ApplyCommand_RaisesTrueBeforeCommitUpdatesParent()
    {
        var option = BuildLinuxOption();
        var vm = new CategoryBuilderViewModel(option);
        bool? accepted = null;

        vm.SelectAllCommand.Execute(null);
        vm.CloseRequested += (_, result) => accepted = result;
        vm.ApplyCommand.Execute(null);
        vm.CommitTo(option);

        Assert.True(accepted);
        Assert.Equal(option.Items.Count, option.Items.Count(i => i.IsSelected));
    }

    [Fact]
    public void SelectRecommended_DoesNotMatchSelectAll()
    {
        var option = BuildLinuxOption();
        var vm = new CategoryBuilderViewModel(option);

        vm.SelectRecommendedCommand.Execute(null);
        var recommended = vm.GetSelectedItemIds().ToHashSet();

        vm.SelectAllCommand.Execute(null);
        var all = vm.GetSelectedItemIds().ToHashSet();

        Assert.NotEqual(recommended, all);
        Assert.Equal(vm.TotalCount, all.Count);
    }

    [Fact]
    public void ClearOptional_KeepsOnlyRequiredItems()
    {
        var option = BuildLinuxOption();
        var vm = new CategoryBuilderViewModel(option);

        vm.SelectAllCommand.Execute(null);
        vm.ClearOptionalCommand.Execute(null);

        Assert.All(vm.WorkingItems.Where(i => i.Tier != UsbBuilderProfileItemTier.Required), i => Assert.False(i.IsSelected));
        Assert.All(vm.WorkingItems.Where(i => i.Tier == UsbBuilderProfileItemTier.Required), i => Assert.True(i.IsSelected));
    }

    [Fact]
    public void Filter_NarrowsVisibleItems()
    {
        var option = BuildLinuxOption();
        var vm = new CategoryBuilderViewModel(option);
        var totalBefore = vm.FilteredItems.Count;

        vm.FilterText = "Rescuezilla";
        Assert.True(vm.FilteredItems.Count < totalBefore);
        Assert.All(vm.FilteredItems, i => Assert.Contains("Rescuezilla", i.DisplayName, System.StringComparison.OrdinalIgnoreCase));
    }
}
