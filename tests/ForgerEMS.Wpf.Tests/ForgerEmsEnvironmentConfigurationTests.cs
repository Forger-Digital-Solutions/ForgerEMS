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
        var resolution = DeepSensorModeResolver.Resolve(new DeepSensorModeResolverOptions
        {
            EnvironmentReader = _ => null,
            LocalAppDataRoot = Path.Combine(Path.GetTempPath(), "forgerems-empty-deep-sensor-" + Guid.NewGuid().ToString("N")),
            InstallDefaultReader = () => null
        });

        Assert.Equal("Off", resolution.Mode);
        Assert.False(resolution.IsEnabled);
    }
}
