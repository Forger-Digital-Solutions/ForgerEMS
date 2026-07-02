using System.Collections.Generic;
using System.Linq;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class UsbBuilderCapacityPlannerTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private static UsbTargetInfo Target(long totalGb, long freeGb) => new()
    {
        RootPath = "X:\\",
        TotalBytes = totalGb * Gb,
        FreeBytes = freeGb * Gb,
    };

    private static UsbBuilderProfileOption WithItems(string categoryId, params (string id, long bytes)[] items)
    {
        var def = UsbBuilderProfileCatalog.GetRequired(categoryId);
        var option = UsbBuilderProfileOption.FromDefinition(def, included: true);
        var built = new List<UsbBuilderProfileItem>();
        foreach (var (id, bytes) in items)
        {
            built.Add(new UsbBuilderProfileItem
            {
                Id = id,
                CategoryId = categoryId,
                DisplayName = id,
                Kind = UsbBuilderProfileItemKind.ManagedDownload,
                Tier = UsbBuilderProfileItemTier.Recommended,
                SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(bytes, ""),
            });
        }
        var ids = built.Select(b => b.Id).ToHashSet();
        option.LoadItems(built, ids);
        return option;
    }

    [Fact]
    public void NoTarget_ReturnsNoUsbPlan()
    {
        var plan = UsbBuilderCapacityPlanner.Calculate(System.Array.Empty<UsbBuilderProfileOption>(), null);
        Assert.False(plan.HasUsbContext);
        Assert.Equal(UsbBuilderCapacityLevel.Ample, plan.Level);
    }

    [Fact]
    public void AmpleFreeSpace_ProducesOkLevel()
    {
        // 128 GB USB, 100 GB free, selecting ~5 GB of new content => 95 GB projected free.
        var option = WithItems("diagnostics",
            ("a", 1 * Gb), ("b", 2 * Gb), ("c", 2 * Gb));
        var plan = UsbBuilderCapacityPlanner.Calculate(new[] { option }, Target(128, 100));
        Assert.Equal(UsbBuilderCapacityLevel.Ample, plan.Level);
        Assert.False(plan.BlocksBuild);
    }

    [Fact]
    public void Under20GbFree_YieldsTightYellow()
    {
        // 32 GB USB, 25 GB free, +9 GB new => projected 16 GB free → tight.
        var option = WithItems("diagnostics",
            ("a", 4 * Gb), ("b", 5 * Gb));
        var plan = UsbBuilderCapacityPlanner.Calculate(new[] { option }, Target(32, 25));
        Assert.Equal(UsbBuilderCapacityLevel.Tight, plan.Level);
        Assert.False(plan.BlocksBuild);
    }

    [Fact]
    public void Under10GbFree_YieldsCriticalRedAndBlocks()
    {
        // 32 GB USB, 14 GB free, +6 GB new => projected 8 GB free.
        var option = WithItems("diagnostics",
            ("a", 3 * Gb), ("b", 3 * Gb));
        var plan = UsbBuilderCapacityPlanner.Calculate(new[] { option }, Target(32, 14));
        Assert.Equal(UsbBuilderCapacityLevel.Critical, plan.Level);
        Assert.True(plan.BlocksBuild);
    }

    [Fact]
    public void OverCapacity_BlocksBuild()
    {
        // 16 GB USB, 8 GB free, +12 GB new => 4 GB over.
        var option = WithItems("diagnostics",
            ("a", 6 * Gb), ("b", 6 * Gb));
        var plan = UsbBuilderCapacityPlanner.Calculate(new[] { option }, Target(16, 8));
        Assert.Equal(UsbBuilderCapacityLevel.OverCapacity, plan.Level);
        Assert.True(plan.BlocksBuild);
        Assert.Equal(0, plan.ProjectedFreeBytes);
    }

    [Fact]
    public void HtmlGuidesAndShortcuts_DoNotDominateCapacity()
    {
        // 128 GB USB, 80 GB free, selecting only docs/shortcuts (~250 KB).
        var option = WithItems("oem-tools",
            ("a", 2 * 1024L), ("b", 2 * 1024L), ("c", 200 * 1024L));
        var plan = UsbBuilderCapacityPlanner.Calculate(new[] { option }, Target(128, 80));
        Assert.Equal(UsbBuilderCapacityLevel.Ample, plan.Level);
        Assert.True(plan.SelectedNewBytes < 1 * 1024 * 1024,
            "Shortcuts/docs should sum to <1 MB even when many are selected.");
    }
}
