using System.Text;
using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class AppUpdateSettingsStoreTests
{
    [Fact]
    public void NewProfile_DefaultsToStableOnly()
    {
        Assert.False(new AppUpdateSettings().IncludeBetaRcChannels);
    }

    [Fact]
    public void Load_PreservesExplicitBetaChoice()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forgerems-update-settings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{"IncludeBetaRcChannels":true}""", Encoding.UTF8);
            var settings = new AppUpdateSettingsStore(path).Load();
            Assert.True(settings.IncludeBetaRcChannels);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    [Fact]
    public void Load_MissingChannelProperty_DefaultsToStableOnly()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forgerems-update-settings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{"CheckAutomatically":true}""", Encoding.UTF8);
            var settings = new AppUpdateSettingsStore(path).Load();
            Assert.False(settings.IncludeBetaRcChannels);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}
