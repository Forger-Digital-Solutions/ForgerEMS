using System.Text;
using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class CopilotSettingsStoreTests
{
    [Fact]
    public void LoadReturnsDefaultsWhenJsonCorrupt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forgerems-settings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ not valid json", Encoding.UTF8);
            var store = new CopilotSettingsStore(path, new CopilotProviderRegistry());
            var settings = store.Load();

            Assert.NotNull(settings);
            Assert.True(settings.TimeoutSeconds > 0);
            Assert.NotEmpty(settings.Providers);
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
