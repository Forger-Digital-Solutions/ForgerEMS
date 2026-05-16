using System.Text.Json;

namespace ForgerEMS.Kyra.HostAdapter.Tests;

[Collection(nameof(KyraSdkEnvironmentCollection))]
public class KyraSdkDogfoodInvokerTests : IDisposable
{
    private readonly string? _sdkFlag;
    private readonly string? _gatewayUrl;
    private readonly string? _gatewayToken;

    public KyraSdkDogfoodInvokerTests()
    {
        _sdkFlag = Environment.GetEnvironmentVariable(ForgerEmsKyraSdkFeatureFlags.EnabledEnvironmentVariable);
        _gatewayUrl = Environment.GetEnvironmentVariable(KyraSdkDogfoodEnvironment.GatewayUrlVariable);
        _gatewayToken = Environment.GetEnvironmentVariable(KyraSdkDogfoodEnvironment.GatewayBetaTokenVariable);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(ForgerEmsKyraSdkFeatureFlags.EnabledEnvironmentVariable, _sdkFlag);
        Environment.SetEnvironmentVariable(KyraSdkDogfoodEnvironment.GatewayUrlVariable, _gatewayUrl);
        Environment.SetEnvironmentVariable(KyraSdkDogfoodEnvironment.GatewayBetaTokenVariable, _gatewayToken);
    }

    [Fact]
    public async Task Flag_false_returns_NotWired()
    {
        Environment.SetEnvironmentVariable(ForgerEmsKyraSdkFeatureFlags.EnabledEnvironmentVariable, null);
        Environment.SetEnvironmentVariable(ForgerEmsKyraSdkFeatureFlags.EnabledEnvironmentVariable, "false");

        var result = await KyraSdkDogfoodInvoker.InvokeAsync();

        Assert.False(ForgerEmsKyraSdkFeatureFlags.IsSdkEnabledFromEnvironment());
        Assert.False(result.SdkFlagActive);
        Assert.False(result.Succeeded);
        Assert.Equal("NotWired", result.ErrorCode);
        Assert.False(result.AllowCloudContextSharing);
        Assert.False(result.AllowWorkerEnrichment);
    }

    [Fact]
    public async Task Flag_true_LocalOnly_returns_SDK_local_response()
    {
        Environment.SetEnvironmentVariable(ForgerEmsKyraSdkFeatureFlags.EnabledEnvironmentVariable, "true");
        Environment.SetEnvironmentVariable(KyraSdkDogfoodEnvironment.GatewayUrlVariable, null);
        Environment.SetEnvironmentVariable(KyraSdkDogfoodEnvironment.GatewayBetaTokenVariable, null);

        var result = await KyraSdkDogfoodInvoker.InvokeAsync(new KyraSdkDogfoodOptions
        {
            HostApplicationVersion = "1.2.1-test",
        });

        Assert.True(result.SdkFlagActive);
        Assert.True(result.Succeeded);
        Assert.Equal(KyraHostMode.LocalOnly, result.Mode);
        Assert.True(result.LocalInvoked);
        Assert.False(result.WorkerInvoked);
        Assert.False(result.AllowCloudContextSharing);
    }

    [Fact]
    public void Dogfood_metadata_is_minimal_and_safe()
    {
        var meta = KyraHostContextMapper.MapSafeMetadata(new KyraHostRequest
        {
            HostApplicationId = "ForgerEMS",
            HostSessionId = KyraSdkDogfoodOptions.DogfoodFeatureId,
            HostApplicationVersion = "1.2.1",
            Privacy = KyraHostPrivacyOptions.SafeDefaults,
            RedactedDeviceReportSummary = "should not appear without sharing",
        });

        Assert.NotNull(meta);
        Assert.Equal("ForgerEMS", meta!["hostApplicationId"]);
        Assert.Equal(KyraSdkDogfoodOptions.DogfoodFeatureId, meta["feature"]);
        Assert.Equal("1.2.1", meta["appVersion"]);
        Assert.False(meta.ContainsKey("emsRedactedReportSummary"));
    }

    [Fact]
    public void Dogfood_report_lines_never_contain_gateway_token()
    {
        var result = new KyraSdkDogfoodResult
        {
            SdkFlagActive = true,
            GatewayTokenPresent = true,
            Succeeded = true,
            ResponseTextPreview = "ok",
        };

        var text = string.Join('\n', result.ToSafeReportLines());
        Assert.DoesNotContain("super-secret-token", text, StringComparison.Ordinal);
        Assert.Contains("GatewayTokenPresent: True", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Gateway_url_env_without_token_does_not_serialize_token_fields()
    {
        var json = JsonSerializer.Serialize(new KyraHostRequest
        {
            GatewayBaseUrl = "https://gateway.example/v1/",
            BearerToken = "beta-token-value",
        });
        Assert.DoesNotContain("beta-token-value", json, StringComparison.Ordinal);
        Assert.Contains("gateway.example", json, StringComparison.Ordinal);
    }
}
