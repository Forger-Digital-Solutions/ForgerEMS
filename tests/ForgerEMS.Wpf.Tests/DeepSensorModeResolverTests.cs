using VentoyToolkitSetup.Wpf.Configuration;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class DeepSensorModeResolverTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "forgerems-deep-sensor-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void BuiltInDefault_IsOffWhenNothingConfigured()
    {
        var resolution = DeepSensorModeResolver.Resolve(Options(env: null, installDefault: null));

        Assert.Equal(DeepSensorModeValues.Off, resolution.Mode);
        Assert.Equal(DeepSensorModeSources.BuiltInDefault, resolution.Source);
        Assert.False(resolution.IsEnabled);
    }

    [Fact]
    public void InstallerDefault_IsUsedWhenEnvAndUserSettingAreMissing()
    {
        var resolution = DeepSensorModeResolver.Resolve(Options(env: null, installDefault: DeepSensorModeValues.ReadOnly));

        Assert.Equal(DeepSensorModeValues.ReadOnly, resolution.Mode);
        Assert.Equal(DeepSensorModeSources.InstallerDefault, resolution.Source);
        Assert.True(resolution.IsEnabled);
    }

    [Fact]
    public void UserSetting_OverridesInstallerDefault()
    {
        DeepSensorModeResolver.SaveUserMode(DeepSensorModeValues.Off, _tempRoot);

        var resolution = DeepSensorModeResolver.Resolve(Options(env: null, installDefault: DeepSensorModeValues.ReadOnly));

        Assert.Equal(DeepSensorModeValues.Off, resolution.Mode);
        Assert.Equal(DeepSensorModeSources.UserSetting, resolution.Source);
        Assert.False(resolution.IsEnabled);
    }

    [Fact]
    public void Environment_OverridesUserAndInstallerDefaults()
    {
        DeepSensorModeResolver.SaveUserMode(DeepSensorModeValues.Off, _tempRoot);

        var resolution = DeepSensorModeResolver.Resolve(Options(env: DeepSensorModeValues.ReadOnly, installDefault: DeepSensorModeValues.Off));

        Assert.Equal(DeepSensorModeValues.ReadOnly, resolution.Mode);
        Assert.Equal(DeepSensorModeSources.Environment, resolution.Source);
        Assert.True(resolution.IsEnabled);
    }

    [Fact]
    public void InvalidValue_FallsBackToOffAndWarns()
    {
        var warnings = new List<string>();
        var resolution = DeepSensorModeResolver.Resolve(new DeepSensorModeResolverOptions
        {
            EnvironmentReader = _ => "definitely-not-valid",
            LocalAppDataRoot = _tempRoot,
            InstallDefaultReader = () => DeepSensorModeValues.ReadOnly,
            WarningSink = warnings.Add
        });

        Assert.Equal(DeepSensorModeValues.Off, resolution.Mode);
        Assert.Equal(DeepSensorModeSources.Environment, resolution.Source);
        Assert.True(resolution.IsInvalid);
        Assert.False(resolution.IsEnabled);
        Assert.Contains(warnings, warning => warning.Contains("Invalid Deep Sensor Mode", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SaveUserMode_WritesLocalAppDataSetting()
    {
        DeepSensorModeResolver.SaveUserMode(DeepSensorModeValues.ReadOnly, _tempRoot);

        var resolution = DeepSensorModeResolver.Resolve(Options(env: null, installDefault: null));

        Assert.Equal(DeepSensorModeValues.ReadOnly, resolution.Mode);
        Assert.Equal(DeepSensorModeSources.UserSetting, resolution.Source);
        Assert.True(File.Exists(DeepSensorModeResolver.GetUserSettingPath(_tempRoot)));
    }

    private DeepSensorModeResolverOptions Options(string? env, string? installDefault) => new()
    {
        EnvironmentReader = name => name == DeepSensorModeResolver.EnvironmentVariableName ? env : null,
        LocalAppDataRoot = _tempRoot,
        InstallDefaultReader = () => installDefault
    };
}
