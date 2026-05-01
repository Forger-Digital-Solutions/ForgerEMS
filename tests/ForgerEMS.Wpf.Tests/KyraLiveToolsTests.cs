using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.KyraTools;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraLiveToolsTests
{
    private static CopilotSettings BaseSettings() => new()
    {
        LiveTools = new KyraLiveToolsSettings
        {
            WeatherEnabled = true,
            WeatherProvider = "openmeteo",
            NewsEnabled = true,
            NewsProvider = "newsapi",
            NewsApiKey = "fake-news-key-123",
            StocksEnabled = true,
            StocksProvider = "finnhub",
            StocksApiKey = "fake-stock-token",
            CryptoEnabled = true,
            CryptoProvider = "coingecko",
            SportsEnabled = true,
            SportsProvider = "thesportsdb",
            SportsApiKey = "fake-sports-key",
            CacheMinutes = 10,
            TimeoutSeconds = 5
        }
    };

    [Fact]
    public async Task Weather_NotConfigured_WhenDisabled()
    {
        var s = BaseSettings();
        s.LiveTools!.WeatherEnabled = false;
        var tool = new KyraToolRegistry(new StubHandler()).Tools.First(t => t.Name == "Weather");
        var r = await tool.ExecuteAsync(MkReq(KyraIntent.Weather, "11710", s), default);
        Assert.False(r.Success);
        Assert.Contains("turned off", r.UserFacingSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Weather_OpenMeteo_ReturnsSummary_NoKeyInOutput()
    {
        var handler = new RoutingHandler(req =>
        {
            var u = req.RequestUri?.AbsoluteUri ?? "";
            if (u.Contains("geocoding-api.open-meteo.com", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        results = new[] { new { name = "Testville", latitude = 40.0, longitude = -73.0, country = "US" } }
                    }), Encoding.UTF8, "application/json")
                };
            }

            if (u.Contains("api.open-meteo.com", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        current = new
                        {
                            temperature_2m = 72.0,
                            apparent_temperature = 70.0,
                            precipitation_probability = 10.0,
                            weather_code = 0,
                            wind_speed_10m = 5.0
                        }
                    }), Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var tool = new KyraToolRegistry(handler).Tools.First(t => t.Name == "Weather");
        var r = await tool.ExecuteAsync(MkReq(KyraIntent.Weather, "11710", BaseSettings(), "11710"), default);
        Assert.True(r.Success);
        Assert.Contains("Open-Meteo", r.UserFacingSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-", r.UserFacingSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", r.ProviderAugmentation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Weather_CacheHit_SecondCall_NoSecondHttp_WhenSameInput()
    {
        var calls = 0;
        var handler = new RoutingHandler(req =>
        {
            calls++;
            var u = req.RequestUri?.AbsoluteUri ?? "";
            if (u.Contains("geocoding-api.open-meteo.com", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        results = new[] { new { name = "A", latitude = 1.0, longitude = 2.0, country = "US" } }
                    }), Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    current = new
                    {
                        temperature_2m = 1.0,
                        apparent_temperature = 1.0,
                        precipitation_probability = 0.0,
                        weather_code = 0,
                        wind_speed_10m = 0.0
                    }
                }), Encoding.UTF8, "application/json")
            };
        });

        var tool = new KyraToolRegistry(handler).Tools.First(t => t.Name == "Weather");
        var s = BaseSettings();
        var req = MkReq(KyraIntent.Weather, "zip", s, "99999");
        var a = await tool.ExecuteAsync(req, default);
        var b = await tool.ExecuteAsync(req, default);
        Assert.True(a.Success && b.Success);
        Assert.True(b.FromCache);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task News_NotConfigured_WithoutKey()
    {
        var s = BaseSettings();
        s.LiveTools!.NewsApiKey = "";
        var tool = new KyraToolRegistry(new StubHandler()).Tools.First(t => t.Name == "News");
        var r = await tool.ExecuteAsync(MkReq(KyraIntent.News, "tech", s, "tech"), default);
        Assert.False(r.Success);
        Assert.Equal(KyraLiveToolErrorKind.NotConfigured, r.ErrorKind);
    }

    [Fact]
    public async Task Stocks_NotConfigured_WithoutKey()
    {
        var s = BaseSettings();
        s.LiveTools!.StocksApiKey = "";
        var tool = new KyraToolRegistry(new StubHandler()).Tools.First(t => t.Name == "Stocks");
        var r = await tool.ExecuteAsync(MkReq(KyraIntent.StockPrice, "NVDA", s, "NVDA"), default);
        Assert.False(r.Success);
        Assert.Contains("not configured", r.UserFacingSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stocks_RequiresSymbol()
    {
        var tool = new KyraToolRegistry(new StubHandler()).Tools.First(t => t.Name == "Stocks");
        var r = await tool.ExecuteAsync(MkReq(KyraIntent.StockPrice, "/stocks", BaseSettings(), ""), default);
        Assert.Equal(KyraLiveToolErrorKind.BadInput, r.ErrorKind);
    }

    [Fact]
    public async Task Crypto_Success_DisclaimerPresent()
    {
        var handler = new RoutingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                bitcoin = new { usd = 50000.12, usd_24h_change = 1.23 }
            }), Encoding.UTF8, "application/json")
        });
        var tool = new KyraToolRegistry(handler).Tools.First(t => t.Name == "Crypto");
        var r = await tool.ExecuteAsync(MkReq(KyraIntent.CryptoPrice, "/crypto BTC", BaseSettings(), "BTC"), default);
        Assert.True(r.Success);
        Assert.Contains("Informational only", r.UserFacingSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CoinGecko", r.UserFacingSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sports_NotConfigured_WithoutKey()
    {
        var s = BaseSettings();
        s.LiveTools!.SportsApiKey = "";
        var tool = new KyraToolRegistry(new StubHandler()).Tools.First(t => t.Name == "Sports");
        var r = await tool.ExecuteAsync(MkReq(KyraIntent.Sports, "Yankees", s, "Yankees"), default);
        Assert.False(r.Success);
    }

    [Fact]
    public async Task Marketplace_NotConfigured_Message()
    {
        var tool = new KyraToolRegistry(new StubHandler()).Tools.First(t => t.Name == "Marketplace");
        var r = await tool.ExecuteAsync(MkReq(KyraIntent.GeneralTechQuestion, "", BaseSettings(), ""), default);
        Assert.False(r.Success);
        Assert.Contains("not configured", r.UserFacingSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stock_Finnhub_Response_DoesNotLeakTokenInSummary()
    {
        var handler = new RoutingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                c = 100.5,
                d = 1.2,
                dp = 1.1,
                pc = 99.0,
                t = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            }), Encoding.UTF8, "application/json")
        });
        var s = BaseSettings();
        s.LiveTools!.StocksApiKey = "finnhub_super_secret_abc";
        var tool = new KyraToolRegistry(handler).Tools.First(t => t.Name == "Stocks");
        var r = await tool.ExecuteAsync(MkReq(KyraIntent.StockPrice, "NVDA", s, "NVDA"), default);
        Assert.True(r.Success);
        Assert.DoesNotContain("finnhub_super_secret", r.UserFacingSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("finnhub_super_secret", r.ProviderAugmentation, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderGrid_DoesNotContainKeys()
    {
        var s = BaseSettings();
        s.LiveTools!.NewsApiKey = "sk-test-openai-999";
        s.LiveTools!.WeatherApiKey = "ow-secret";
        var reg = new KyraToolRegistry(new StubHandler());
        var facts = new KyraToolHostFacts { HasSystemIntelligenceScan = true, HasToolkitHealthReport = true };
        var text = reg.BuildProviderToolDetailText(s, facts, false);
        Assert.DoesNotContain("sk-test", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ow-secret", text, StringComparison.Ordinal);
    }

    private static KyraToolExecutionRequest MkReq(KyraIntent intent, string prompt, CopilotSettings settings, string args = "") =>
        new()
        {
            Intent = intent,
            Prompt = prompt,
            ArgumentsLine = args,
            Settings = settings,
            HostFacts = default,
            Context = new CopilotContext { Intent = intent, UserQuestion = prompt }
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _route;

        public RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> route) => _route = route;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_route(request));
    }
}
