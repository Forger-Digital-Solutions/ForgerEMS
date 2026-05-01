using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraModeConnectivityTests
{
    [Theory]
    [InlineData(CopilotMode.OnlineWhenAvailable)]
    [InlineData(CopilotMode.OnlineAssisted)]
    [InlineData(CopilotMode.FreeApiPool)]
    [InlineData(CopilotMode.BringYourOwnKey)]
    [InlineData(CopilotMode.ForgerEmsCloudFuture)]
    public void NormalizeFallsBackToHybridWhenNoOnlineOrLocalAi(CopilotMode mode)
    {
        var result = KyraModeConnectivity.NormalizeModeForAvailableProviders(
            mode,
            anyOnlineConfigured: false,
            localOllamaEnabled: false,
            localLmStudioEnabled: false);

        Assert.Equal(CopilotMode.HybridAuto, result);
    }

    [Fact]
    public void NormalizePreservesOfflineOnly()
    {
        var result = KyraModeConnectivity.NormalizeModeForAvailableProviders(
            CopilotMode.OfflineOnly,
            anyOnlineConfigured: false,
            localOllamaEnabled: false,
            localLmStudioEnabled: false);

        Assert.Equal(CopilotMode.OfflineOnly, result);
    }

    [Fact]
    public void NormalizePreservesOnlineModeWhenProviderConfigured()
    {
        var result = KyraModeConnectivity.NormalizeModeForAvailableProviders(
            CopilotMode.OnlineWhenAvailable,
            anyOnlineConfigured: true,
            localOllamaEnabled: false,
            localLmStudioEnabled: false);

        Assert.Equal(CopilotMode.OnlineWhenAvailable, result);
    }

    [Fact]
    public void NormalizePreservesOnlineModeWhenOllamaEnabled()
    {
        var result = KyraModeConnectivity.NormalizeModeForAvailableProviders(
            CopilotMode.OnlineWhenAvailable,
            anyOnlineConfigured: false,
            localOllamaEnabled: true,
            localLmStudioEnabled: false);

        Assert.Equal(CopilotMode.OnlineWhenAvailable, result);
    }
}
