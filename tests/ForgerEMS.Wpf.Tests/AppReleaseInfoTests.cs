using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class AppReleaseInfoTests
{
    [Fact]
    public void Version_Is_PublicPreview()
    {
        Assert.Equal("1.2.0-preview.1", AppReleaseInfo.Version);
        Assert.Contains("1.2.0", AppReleaseInfo.DisplayVersion, System.StringComparison.Ordinal);
        Assert.Contains("Public Preview", AppReleaseInfo.DisplayVersion, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Version_ParsesAsSemanticVersion()
    {
        Assert.True(AppSemanticVersion.TryParse(AppReleaseInfo.Version, out var v));
        Assert.Equal(1, v.Major);
        Assert.Equal(2, v.Minor);
        Assert.Equal(0, v.Patch);
        Assert.Equal("preview.1", v.Prerelease);
    }
}
