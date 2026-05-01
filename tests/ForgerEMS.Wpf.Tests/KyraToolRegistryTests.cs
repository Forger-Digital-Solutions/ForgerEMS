using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.KyraTools;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraToolRegistryTests
{
    [Fact]
    public void LiveDataTools_NotConfigured_UntilApisWired()
    {
        var reg = new KyraToolRegistry();
        var facts = new KyraToolHostFacts { HasSystemIntelligenceScan = true, HasToolkitHealthReport = true };
        var settings = new CopilotSettings();
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
