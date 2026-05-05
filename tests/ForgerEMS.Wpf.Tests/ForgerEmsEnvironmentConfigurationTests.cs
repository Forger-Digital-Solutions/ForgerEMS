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

    [Fact]
    public void DeepSensorModeDefaultsOff()
    {
        var previous = Environment.GetEnvironmentVariable("FORGEREMS_DEEP_SENSOR_MODE");
        try
        {
            Environment.SetEnvironmentVariable("FORGEREMS_DEEP_SENSOR_MODE", null);

            Assert.Equal("Off", ForgerEmsEnvironmentConfiguration.DeepSensorMode);
            Assert.False(ForgerEmsFeatureFlags.DeepSensorModeRequested);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FORGEREMS_DEEP_SENSOR_MODE", previous);
        }
    }
}
