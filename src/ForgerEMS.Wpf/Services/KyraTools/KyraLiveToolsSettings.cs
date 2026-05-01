namespace VentoyToolkitSetup.Wpf.Services.KyraTools;

/// <summary>Beta/local Kyra live tool configuration (stored in copilot-settings.json). Treat as user-controlled; never log API keys.</summary>
public sealed class KyraLiveToolsSettings
{
    public bool WeatherEnabled { get; set; } = true;

    /// <summary>e.g. openmeteo (no key), openweather</summary>
    public string WeatherProvider { get; set; } = "openmeteo";

    public string WeatherApiKey { get; set; } = string.Empty;

    public string WeatherBaseUrl { get; set; } = string.Empty;

    /// <summary>Optional default when /weather has no argument (user-set, privacy-safe).</summary>
    public string DefaultWeatherLocation { get; set; } = string.Empty;

    public bool NewsEnabled { get; set; }

    /// <summary>newsapi, gnews</summary>
    public string NewsProvider { get; set; } = "newsapi";

    public string NewsApiKey { get; set; } = string.Empty;

    public string NewsBaseUrl { get; set; } = string.Empty;

    public bool StocksEnabled { get; set; }

    /// <summary>finnhub</summary>
    public string StocksProvider { get; set; } = "finnhub";

    public string StocksApiKey { get; set; } = string.Empty;

    public string StocksBaseUrl { get; set; } = string.Empty;

    public bool CryptoEnabled { get; set; } = true;

    /// <summary>coingecko (no key), optional future providers</summary>
    public string CryptoProvider { get; set; } = "coingecko";

    public string CryptoApiKey { get; set; } = string.Empty;

    public string CryptoBaseUrl { get; set; } = string.Empty;

    public bool SportsEnabled { get; set; }

    /// <summary>thesportsdb</summary>
    public string SportsProvider { get; set; } = "thesportsdb";

    public string SportsApiKey { get; set; } = string.Empty;

    public string SportsBaseUrl { get; set; } = string.Empty;

    public bool MarketplaceEnabled { get; set; }

    public string MarketplaceProvider { get; set; } = string.Empty;

    public string MarketplaceApiKey { get; set; } = string.Empty;

    public string MarketplaceBaseUrl { get; set; } = string.Empty;

    /// <summary>Shared HTTP timeout; 0 uses CopilotSettings.TimeoutSeconds.</summary>
    public int TimeoutSeconds { get; set; }

    public int CacheMinutes { get; set; } = 10;
}
