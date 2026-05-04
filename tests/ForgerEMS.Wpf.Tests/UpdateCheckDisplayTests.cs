using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class UpdateCheckDisplayTests
{
    [Fact]
    public void FormatInstalledAlreadyLatest_IncludesVersion()
    {
        var t = UpdateCheckDisplay.FormatInstalledAlreadyLatest("1.1.4");
        Assert.Contains("1.1.4", t);
        Assert.Contains("latest", t, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatInstalledNewerThanPublic_ShowsBoth()
    {
        var t = UpdateCheckDisplay.FormatInstalledNewerThanPublic("1.2.0", "1.1.4");
        Assert.Contains("1.2.0", t);
        Assert.Contains("1.1.4", t);
        Assert.Contains("newer", t, System.StringComparison.OrdinalIgnoreCase);
    }
}
