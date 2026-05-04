using System.Net;
using System.Net.Http;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Kyra;
using VentoyToolkitSetup.Wpf.Services.KyraTools;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraLiveDataBoundaryTests
{
    private static KyraToolRegistry Registry() => new(new StubHandler());

    private static KyraToolHostFacts Facts() => new();

    [Fact]
    public void RequiresUnavailableLiveData_LocalAnswer_WhenWeatherToolOff()
    {
        var settings = new CopilotSettings();
        settings.LiveTools ??= new KyraLiveToolsSettings();
        settings.LiveTools.WeatherEnabled = false;

        Assert.True(KyraLiveToolRouter.RequiresUnavailableLiveDataLocalAnswer(
            KyraIntent.Weather,
            "What's the weather today?",
            Registry(),
            settings,
            Facts()));
    }

    [Fact]
    public void RequiresUnavailableLiveData_LocalAnswer_WhenNewsNotOperational()
    {
        var settings = new CopilotSettings { LiveTools = new KyraLiveToolsSettings { NewsEnabled = false } };
        Assert.True(KyraLiveToolRouter.RequiresUnavailableLiveDataLocalAnswer(
            KyraIntent.News,
            "What is in the news today?",
            Registry(),
            settings,
            Facts()));
    }

    [Fact]
    public void RequiresUnavailableLiveData_LocalAnswer_ForCryptoWhenToolNotReady()
    {
        var settings = new CopilotSettings { LiveTools = new KyraLiveToolsSettings { CryptoEnabled = false } };
        Assert.True(KyraLiveToolRouter.RequiresUnavailableLiveDataLocalAnswer(
            KyraIntent.CryptoPrice,
            "BTC price",
            Registry(),
            settings,
            Facts()));
    }

    [Fact]
    public void RequiresUnavailableLiveData_LocalAnswer_ForStockWhenToolNotReady()
    {
        var settings = new CopilotSettings { LiveTools = new KyraLiveToolsSettings { StocksEnabled = false } };
        Assert.True(KyraLiveToolRouter.RequiresUnavailableLiveDataLocalAnswer(
            KyraIntent.StockPrice,
            "AAPL price",
            Registry(),
            settings,
            Facts()));
    }

    [Fact]
    public void GeneralPrompt_LatestNonForgerEMSRelease_TreatedAsLiveFacts()
    {
        Assert.True(KyraLiveToolRouter.PromptLooksLikeEphemeralExternalFacts("What is the newest Chrome release?"));
    }

    [Fact]
    public void GeneralPrompt_LatestForgerEMSRelease_NotTreatedAsEphemeralLiveFacts()
    {
        Assert.False(KyraLiveToolRouter.PromptLooksLikeEphemeralExternalFacts("What is the newest ForgerEMS release?"));
    }

    [Fact]
    public void LiveToolsUnavailableMessage_IsStable()
    {
        Assert.Contains("don't have live data tools", KyraLiveToolRouter.LiveToolsUnavailableMessage, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }
}
