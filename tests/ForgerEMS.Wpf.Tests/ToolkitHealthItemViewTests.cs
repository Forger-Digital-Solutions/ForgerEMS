using VentoyToolkitSetup.Wpf.Models;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class ToolkitHealthItemViewTests
{
    [Fact]
    public void StatusDisplayUi_ManualRequired_IsNotGenericMissing()
    {
        var v = new ToolkitHealthItemView { Status = "MANUAL_REQUIRED", Tool = "x", Category = "y" };
        Assert.Equal("Manual required", v.StatusDisplayUi);
        Assert.DoesNotContain("Missing", v.StatusDisplayUi, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StatusDisplayUi_ManagedMissing_IsReadable()
    {
        var v = new ToolkitHealthItemView { Status = "MISSING_REQUIRED" };
        Assert.Equal("Managed missing", v.StatusDisplayUi);
    }
}
