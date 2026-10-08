using VentoyToolkitSetup.Wpf.Services.Licensing;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class FeatureGateServiceTests
{
    [Fact]
    public void PublicPreview_UnlocksUsbIntelligence()
    {
        Assert.True(FeatureGateService.IsUsbIntelligenceExperienceEnabled(LicenseTier.PublicPreview));
    }

    [Fact]
    public void FreeTier_BlocksUsbIntelligenceExperience()
    {
        Assert.False(FeatureGateService.IsUsbIntelligenceExperienceEnabled(LicenseTier.Free));
    }
}
