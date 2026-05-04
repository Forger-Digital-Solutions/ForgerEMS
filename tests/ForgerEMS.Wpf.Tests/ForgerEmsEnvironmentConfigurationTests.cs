using VentoyToolkitSetup.Wpf.Configuration;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class ForgerEmsEnvironmentConfigurationTests
{
    [Fact]
    public void GitHubOwner_DefaultsToForgerOrg()
    {
        Assert.Equal("Forger-Digital-Solutions", ForgerEmsEnvironmentConfiguration.GitHubOwner);
    }

    [Fact]
    public void UpdateUserAgent_DefaultsToForgerEMS()
    {
        Assert.Equal("ForgerEMS", ForgerEmsEnvironmentConfiguration.UpdateUserAgent);
    }

    [Fact]
    public void TelemetryDefaultsOff()
    {
        Assert.False(ForgerEmsFeatureFlags.TelemetryEnabled);
        Assert.False(ForgerEmsFeatureFlags.CrashReportingEnabled);
    }
}
