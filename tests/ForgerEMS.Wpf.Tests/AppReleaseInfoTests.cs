using VentoyToolkitSetup.Wpf.Infrastructure;

namespace ForgerEMS.Wpf.Tests;

public sealed class AppReleaseInfoTests
{
    [Fact]
    public void Version_Is_CurrentMechanicalRc()
    {
        Assert.Equal("1.1.12-rc.3", AppReleaseInfo.Version);
        Assert.Contains("1.1.12", AppReleaseInfo.DisplayVersion, System.StringComparison.Ordinal);
    }
}
