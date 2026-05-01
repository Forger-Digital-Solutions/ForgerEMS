using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.KyraTools;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraToolRegistryTests
{
    [Fact]
    public void LiveDataTools_HasCapability_WhenNoKeyProvidersDefaultEnabled()
    {
        var reg = new KyraToolRegistry();
        var facts = new KyraToolHostFacts { HasSystemIntelligenceScan = true, HasToolkitHealthReport = true };
        var settings = new CopilotSettings();
        Assert.True(reg.HasConfiguredLiveDataCapability(settings, facts));
    }

    [Fact]
    public void LiveDataTools_NoCapability_WhenAllConfigurableLiveToolsDisabled()
    {
        var reg = new KyraToolRegistry();
        var facts = new KyraToolHostFacts { HasSystemIntelligenceScan = true, HasToolkitHealthReport = true };
        var settings = new CopilotSettings
        {
            LiveTools = new KyraLiveToolsSettings
            {
                WeatherEnabled = false,
                CryptoEnabled = false,
                NewsEnabled = false,
                StocksEnabled = false,
                SportsEnabled = false
            }
        };
        Assert.False(reg.HasConfiguredLiveDataCapability(settings, facts));
    }

    [Fact]
    public void StatusRows_IncludeWeatherAndSystemContext()
    {
        var reg = new KyraToolRegistry();
        var facts = new KyraToolHostFacts { HasSystemIntelligenceScan = true, HasToolkitHealthReport = false };
        var rows = reg.BuildStatusGridRows(new CopilotSettings(), facts);
        Assert.Contains(rows, r => r.ToolName.Contains("Weather", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rows, r => r.Status.Contains("Available", StringComparison.OrdinalIgnoreCase));
    }
}
