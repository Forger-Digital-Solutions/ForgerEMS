using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.KyraTools;

internal sealed class WeatherKyraTool(HttpClient http, KyraLiveToolCache cache) : IKyraTool
{
    public string Name => "Weather";

    public string Description => "Weather via Open-Meteo (no key) or OpenWeather (API key).";

    public KyraToolSurfaceCategory SurfaceCategory => KyraToolSurfaceCategory.LiveData;

    public bool CanHandle(KyraIntent intent, string prompt) => intent == KyraIntent.Weather;

    public KyraToolOperationalStatus GetOperationalStatus(CopilotSettings settings, KyraToolHostFacts facts)
    {
        var lt = settings.LiveTools ?? new KyraLiveToolsSettings();
        if (!lt.WeatherEnabled)
        {
            return KyraToolOperationalStatus.Disabled;
        }

        var p = (lt.WeatherProvider ?? "openmeteo").Trim();
        if (p.Equals("openmeteo", StringComparison.OrdinalIgnoreCase))
        {
            return KyraToolOperationalStatus.Ready;
        }

        if (p.Equals("openweather", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(lt.WeatherApiKey))
        {
            return KyraToolOperationalStatus.Ready;
        }

        return KyraToolOperationalStatus.NotConfigured;
    }

    public async Task<KyraToolResult> ExecuteAsync(KyraToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var settings = request.Settings;
        var lt = settings.LiveTools ?? new KyraLiveToolsSettings();
        if (!lt.WeatherEnabled)
        {
            return DisabledResult();
        }

        var loc = FirstNonEmpty(request.ArgumentsLine, ExtractLocation(request.Prompt), request.HostFacts.DefaultWeatherLocation, lt.DefaultWeatherLocation);

        if (string.IsNullOrWhiteSpace(loc))
        {
            var r = KyraToolResult.Fail(Name, KyraLiveToolErrorKind.BadInput,
                "Weather needs a place to look up. Try:\n`/weather 11710`\n`/weather Bellmore NY`\n`/weather New York City`\n\nOr set a default location in **Kyra Advanced → Live APIs**.",
                "[Kyra weather] No location provided; do not invent conditions.");
            KyraLiveToolTelemetry.Record(Name, KyraToolOperationalStatus.NotConfigured, "", false, "needs location");
            return r;
        }

        var st = GetOperationalStatus(settings, default);
        if (st == KyraToolOperationalStatus.NotConfigured)
        {
            return NotConfiguredResult(lt);
        }

        var provider = (lt.WeatherProvider ?? "openmeteo").Trim();
        var timeout = KyraLiveToolHttp.EffectiveTimeoutSeconds(settings);
        var ttl = KyraLiveToolHttp.CacheTtl(settings);
        var cacheKey = KyraLiveToolCache.MakeKey("weather", provider, loc.Trim().ToLowerInvariant());
        if (cache.TryGet(cacheKey, ttl, out var cached))
        {
            KyraLiveToolTelemetry.Record(Name, KyraToolOperationalStatus.Ready, provider, true);
            return cached;
        }

        KyraToolResult result;
        if (provider.Equals("openweather", StringComparison.OrdinalIgnoreCase))
        {
            result = await QueryOpenWeatherAsync(http, lt, loc, timeout, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            result = await QueryOpenMeteoAsync(http, loc, timeout, cancellationToken).ConfigureAwait(false);
        }

        if (result.Success)
        {
            cache.Set(cacheKey, result);
        }

        KyraLiveToolTelemetry.Record(Name,
            result.Success ? KyraToolOperationalStatus.Ready :
            result.ErrorKind == KyraLiveToolErrorKind.Timeout ? KyraToolOperationalStatus.TimedOut :
            KyraToolOperationalStatus.Failed,
            provider,
            false,
            result.SafeErrorMessage);
        return result;
    }

    private KyraToolResult DisabledResult()
    {
        var r = KyraToolResult.Fail(Name, KyraLiveToolErrorKind.Disabled,
            "Weather live data is turned off in Kyra Advanced → Live APIs.",
            "[Kyra weather] Disabled in settings.");
        KyraLiveToolTelemetry.Record(Name, KyraToolOperationalStatus.Disabled, "", false);
        return r;
    }

    private static KyraToolResult NotConfiguredResult(KyraLiveToolsSettings lt)
    {
        var p = (lt.WeatherProvider ?? "").Trim();
        var msg = p.Equals("openweather", StringComparison.OrdinalIgnoreCase)
            ? "Weather is not configured yet. Add an **OpenWeather** API key in **Kyra Advanced → Live APIs**, then try again."
            : "Weather is not configured yet. Choose **openmeteo** (no key) or **openweather** with a key in Kyra settings.";
        return KyraToolResult.Fail("Weather", KyraLiveToolErrorKind.NotConfigured, msg,
            "[Kyra weather] Provider not configured; do not invent conditions.");
    }

    private static async Task<KyraToolResult> QueryOpenMeteoAsync(HttpClient http, string location, int timeout, CancellationToken ct)
    {
        var geoUrl =
            "https://geocoding-api.open-meteo.com/v1/search?name=" + Uri.EscapeDataString(location) + "&count=1";
        var (geoOk, geoTo, geoBody, _) = await KyraLiveToolHttp.GetStringAsync(http, geoUrl, timeout, ct).ConfigureAwait(false);
        if (geoTo)
        {
            return KyraToolResult.Fail("Weather", KyraLiveToolErrorKind.Timeout,
                "Weather lookup timed out. Try again.",
                "[Kyra weather] Timeout; do not invent conditions.");
        }

        if (!geoOk)
        {
            return KyraToolResult.Fail("Weather", KyraLiveToolErrorKind.HttpError,
                "Could not reach the weather geocoding service. Check your network and try again.",
                "[Kyra weather] Geocoding request failed.");
        }

        using var geoDoc = KyraLiveToolHttp.TryParseJson(geoBody);
        if (geoDoc is null || !geoDoc.RootElement.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
        {
            return KyraToolResult.Fail("Weather", KyraLiveToolErrorKind.ParseError,
                $"No match found for “{location}”. Try a ZIP or larger city name.",
                "[Kyra weather] Geocoding returned no results.");
        }

        var r0 = results[0];
        var lat = r0.GetProperty("latitude").GetDouble();
        var lon = r0.GetProperty("longitude").GetDouble();
        var label = r0.GetProperty("name").GetString() ?? location;
        var region = r0.TryGetProperty("admin1", out var a1) ? a1.GetString() : null;
        var country = r0.TryGetProperty("country", out var c) ? c.GetString() : null;
        var place = string.Join(", ", new[] { label, region, country }.Where(s => !string.IsNullOrWhiteSpace(s)));

        var fcUrl =
            "https://api.open-meteo.com/v1/forecast?latitude=" + lat.ToString(CultureInfo.InvariantCulture) +
            "&longitude=" + lon.ToString(CultureInfo.InvariantCulture) +
            "&current=temperature_2m,apparent_temperature,precipitation_probability,weather_code,wind_speed_10m" +
            "&daily=precipitation_probability_max&timezone=auto&temperature_unit=fahrenheit&wind_speed_unit=mph";
        var (fcOk, fcTo, fcBody, _) = await KyraLiveToolHttp.GetStringAsync(http, fcUrl, timeout, ct).ConfigureAwait(false);
        if (fcTo)
        {
            return KyraToolResult.Fail("Weather", KyraLiveToolErrorKind.Timeout,
                "Weather forecast timed out. Try again.",
                "[Kyra weather] Timeout.");
        }

        if (!fcOk)
        {
            return KyraToolResult.Fail("Weather", KyraLiveToolErrorKind.HttpError,
                "Could not load the forecast. Try again in a moment.",
                "[Kyra weather] Forecast request failed.");
        }

        using var fc = KyraLiveToolHttp.TryParseJson(fcBody);
        if (fc is null || !fc.RootElement.TryGetProperty("current", out var cur))
        {
            return KyraToolResult.Fail("Weather", KyraLiveToolErrorKind.ParseError,
                "Weather data was unreadable. The provider may have changed format.",
                "[Kyra weather] Parse error.");
        }

        var temp = cur.TryGetProperty("temperature_2m", out var tEl) ? tEl.GetDouble() : double.NaN;
        var feel = cur.TryGetProperty("apparent_temperature", out var fEl) ? fEl.GetDouble() : double.NaN;
        var pop = cur.TryGetProperty("precipitation_probability", out var pEl) ? pEl.GetDouble() : double.NaN;
        var code = cur.TryGetProperty("weather_code", out var wEl) ? wEl.GetInt32() : -1;
        var wind = cur.TryGetProperty("wind_speed_10m", out var windEl) ? windEl.GetDouble() : double.NaN;
        var cond = DescribeWmo(code);
        var sb = new StringBuilder();
        sb.AppendLine(place);
        sb.AppendLine();
        sb.AppendLine($"Conditions: {cond}");
        if (!double.IsNaN(temp))
        {
            sb.AppendLine($"Temperature: {temp:0.#} °F");
        }

        if (!double.IsNaN(feel))
        {
            sb.AppendLine($"Feels like: {feel:0.#} °F");
        }

        if (!double.IsNaN(pop))
        {
            sb.AppendLine($"Precipitation chance: {pop:0.#}%");
        }

        if (!double.IsNaN(wind))
        {
            sb.AppendLine($"Wind: {wind:0.#} mph");
        }

        sb.AppendLine();
        sb.AppendLine("Provider: Open-Meteo (public, no API key)");
        sb.AppendLine($"Updated: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC");
        var user = sb.ToString().TrimEnd();
        var aug =
            $"[Kyra weather | Open-Meteo] {place}: {cond}, temp {temp:0.#}°F, feels {feel:0.#}°F, POP {pop:0.#}%, wind {wind:0.#}mph. " +
            "Do not add facts beyond this block.";
        return KyraToolResult.Ok("Weather", "Open-Meteo", user, aug, sources: []);
    }

    private static async Task<KyraToolResult> QueryOpenWeatherAsync(HttpClient http, KyraLiveToolsSettings lt, string location, int timeout, CancellationToken ct)
    {
        var key = lt.WeatherApiKey.Trim();
        if (string.IsNullOrEmpty(key))
        {
            return NotConfiguredResult(lt);
        }

        var baseUrl = string.IsNullOrWhiteSpace(lt.WeatherBaseUrl)
            ? "https://api.openweathermap.org/data/2.5/weather"
            : lt.WeatherBaseUrl.TrimEnd('/');
        var url = $"{baseUrl}?q={Uri.EscapeDataString(location)}&appid={key}&units=imperial";
        var (ok, timedOut, body, code) = await KyraLiveToolHttp.GetStringAsync(http, url, timeout, ct).ConfigureAwait(false);
        if (timedOut)
        {
            return KyraToolResult.Fail("Weather", KyraLiveToolErrorKind.Timeout,
                "Weather request timed out. Try again later.",
                "[Kyra weather] Timeout; do not invent readings.");
        }

        if (!ok)
        {
            return KyraToolResult.Fail("Weather", KyraLiveToolErrorKind.HttpError,
                "Weather request failed. Try again later.",
                "[Kyra weather] HTTP failure; do not invent readings.");
        }

        using var doc = KyraLiveToolHttp.TryParseJson(body);
        if (doc is null)
        {
            return KyraToolResult.Fail("Weather", KyraLiveToolErrorKind.ParseError,
                "Could not read weather response.",
                "[Kyra weather] Parse error.");
        }

        var root = doc.RootElement;
        var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? location : location;
        var main = root.TryGetProperty("main", out var m) ? m : default;
        var temp = main.ValueKind == JsonValueKind.Object && main.TryGetProperty("temp", out var te) ? te.GetDouble() : double.NaN;
        var feels = main.ValueKind == JsonValueKind.Object && main.TryGetProperty("feels_like", out var fl) ? fl.GetDouble() : double.NaN;
        var pop = main.ValueKind == JsonValueKind.Object && main.TryGetProperty("humidity", out var hum) ? hum.GetDouble() : double.NaN;
        var windSpeed = root.TryGetProperty("wind", out var w) && w.TryGetProperty("speed", out var ws) ? ws.GetDouble() : double.NaN;
        var desc = "";
        if (root.TryGetProperty("weather", out var wa) && wa.ValueKind == JsonValueKind.Array && wa.GetArrayLength() > 0 &&
            wa[0].TryGetProperty("description", out var d0))
        {
            desc = d0.GetString() ?? "";
        }

        var sb = new StringBuilder();
        sb.AppendLine(name);
        sb.AppendLine();
        if (!string.IsNullOrEmpty(desc))
        {
            sb.AppendLine($"Conditions: {desc}");
        }

        if (!double.IsNaN(temp))
        {
            sb.AppendLine($"Temperature: {temp:0.#} °F");
        }

        if (!double.IsNaN(feels))
        {
            sb.AppendLine($"Feels like: {feels:0.#} °F");
        }

        if (!double.IsNaN(pop))
        {
            sb.AppendLine($"Humidity: {pop:0.#}%");
        }

        if (!double.IsNaN(windSpeed))
        {
            sb.AppendLine($"Wind: {windSpeed:0.#} mph");
        }

        sb.AppendLine();
        sb.AppendLine("Provider: OpenWeather");
        sb.AppendLine($"Updated: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC");
        var user = sb.ToString().TrimEnd();
        var aug = $"[Kyra weather | OpenWeather] {name}: {desc}, temp {temp:0.#}°F. Do not add beyond this summary.";
        return KyraToolResult.Ok("Weather", "OpenWeather", user, aug);
    }

    private static string DescribeWmo(int code) => code switch
    {
        0 => "Clear",
        1 or 2 or 3 => "Mainly clear to overcast",
        45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle",
        61 or 63 or 65 => "Rain",
        71 or 73 or 75 => "Snow",
        80 or 81 or 82 => "Rain showers",
        95 or 96 or 99 => "Thunderstorm",
        _ => code < 0 ? "Unknown" : $"Weather code {code}"
    };

    private static string FirstNonEmpty(params string?[] parts)
    {
        foreach (var p in parts)
        {
            if (!string.IsNullOrWhiteSpace(p))
            {
                return p.Trim();
            }
        }

        return string.Empty;
    }

    private static string ExtractLocation(string prompt)
    {
        var t = prompt.Trim();
        if (t.StartsWith("/weather", StringComparison.OrdinalIgnoreCase))
        {
            return t.Length > 8 ? t[8..].TrimStart() : "";
        }

        return "";
    }
}

internal sealed class NewsKyraTool(HttpClient http, KyraLiveToolCache cache) : IKyraTool
{
    public string Name => "News";

    public string Description => "Headlines via NewsAPI or GNews (API key).";

    public KyraToolSurfaceCategory SurfaceCategory => KyraToolSurfaceCategory.LiveData;

    public bool CanHandle(KyraIntent intent, string prompt) => intent == KyraIntent.News;

    public KyraToolOperationalStatus GetOperationalStatus(CopilotSettings settings, KyraToolHostFacts facts)
    {
        var lt = settings.LiveTools ?? new KyraLiveToolsSettings();
        if (!lt.NewsEnabled)
        {
            return KyraToolOperationalStatus.Disabled;
        }

        var p = (lt.NewsProvider ?? "newsapi").Trim();
        if (string.IsNullOrWhiteSpace(lt.NewsApiKey))
        {
            return KyraToolOperationalStatus.NotConfigured;
        }

        return p.Equals("newsapi", StringComparison.OrdinalIgnoreCase) || p.Equals("gnews", StringComparison.OrdinalIgnoreCase)
            ? KyraToolOperationalStatus.Ready
            : KyraToolOperationalStatus.NotConfigured;
    }

    public async Task<KyraToolResult> ExecuteAsync(KyraToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var settings = request.Settings;
        var lt = settings.LiveTools ?? new KyraLiveToolsSettings();
        if (!lt.NewsEnabled)
        {
            return Fail(KyraLiveToolErrorKind.Disabled, "News live data is turned off in Kyra Advanced → Live APIs.");
        }

        if (string.IsNullOrWhiteSpace(lt.NewsApiKey))
        {
            return Fail(KyraLiveToolErrorKind.NotConfigured,
                "News live data is not configured yet.\nAdd a **NewsAPI** or **GNews** key in **Kyra Advanced → Live APIs**, then try:\n`/news tech`");
        }

        var topic = FirstArg(request);
        var provider = (lt.NewsProvider ?? "newsapi").Trim();
        var timeout = KyraLiveToolHttp.EffectiveTimeoutSeconds(settings);
        var ttl = KyraLiveToolHttp.CacheTtl(settings);
        var cacheKey = KyraLiveToolCache.MakeKey("news", provider, (topic ?? "").ToLowerInvariant());
        if (cache.TryGet(cacheKey, ttl, out var cached))
        {
            KyraLiveToolTelemetry.Record(Name, KyraToolOperationalStatus.Ready, provider, true);
            return cached;
        }

        KyraToolResult result;
        if (provider.Equals("gnews", StringComparison.OrdinalIgnoreCase))
        {
            result = await QueryGNewsAsync(http, lt, topic, timeout, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            result = await QueryNewsApiAsync(http, lt, topic, timeout, cancellationToken).ConfigureAwait(false);
        }

        if (result.Success)
        {
            cache.Set(cacheKey, result);
        }

        KyraLiveToolTelemetry.Record(Name,
            result.Success ? KyraToolOperationalStatus.Ready :
            result.ErrorKind == KyraLiveToolErrorKind.Timeout ? KyraToolOperationalStatus.TimedOut :
            KyraToolOperationalStatus.Failed,
            provider,
            false);
        return result;
    }

    private KyraToolResult Fail(KyraLiveToolErrorKind k, string msg) =>
        KyraToolResult.Fail(Name, k, msg, "[Kyra news] " + msg);

    private static string? FirstArg(KyraToolExecutionRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ArgumentsLine))
        {
            return request.ArgumentsLine.Trim();
        }

        var p = request.Prompt.Trim();
        if (p.StartsWith("/news", StringComparison.OrdinalIgnoreCase) && p.Length > 5)
        {
            return p[5..].TrimStart();
        }

        return null;
    }

    private static async Task<KyraToolResult> QueryNewsApiAsync(HttpClient http, KyraLiveToolsSettings lt, string? topic, int timeout, CancellationToken ct)
    {
        var key = lt.NewsApiKey.Trim();
        string url;
        if (string.IsNullOrWhiteSpace(topic))
        {
            url = "https://newsapi.org/v2/top-headlines?country=us&pageSize=6&apiKey=" + Uri.EscapeDataString(key);
        }
        else
        {
            url = "https://newsapi.org/v2/everything?q=" + Uri.EscapeDataString(topic) + "&sortBy=publishedAt&pageSize=6&apiKey=" +
                  Uri.EscapeDataString(key);
        }

        var (ok, to, body, _) = await KyraLiveToolHttp.GetStringAsync(http, url, timeout, ct).ConfigureAwait(false);
        if (to)
        {
            return KyraToolResult.Fail("News", KyraLiveToolErrorKind.Timeout, "News request timed out.", "[Kyra news] Timeout.");
        }

        if (!ok)
        {
            return KyraToolResult.Fail("News", KyraLiveToolErrorKind.HttpError, "Could not load news. Check the API key and network.",
                "[Kyra news] HTTP error.");
        }

        using var doc = KyraLiveToolHttp.TryParseJson(body);
        if (doc is null || !doc.RootElement.TryGetProperty("articles", out var arts) || arts.ValueKind != JsonValueKind.Array)
        {
            return KyraToolResult.Fail("News", KyraLiveToolErrorKind.ParseError, "News response was unreadable.",
                "[Kyra news] Parse error.");
        }

        return FormatArticles("NewsAPI", arts);
    }

    private static async Task<KyraToolResult> QueryGNewsAsync(HttpClient http, KyraLiveToolsSettings lt, string? topic, int timeout, CancellationToken ct)
    {
        var key = lt.NewsApiKey.Trim();
        var q = string.IsNullOrWhiteSpace(topic) ? "headlines" : topic.Trim();
        var url = "https://gnews.io/api/v4/search?q=" + Uri.EscapeDataString(q) + "&lang=en&max=6&token=" + Uri.EscapeDataString(key);
        var (ok, to, body, _) = await KyraLiveToolHttp.GetStringAsync(http, url, timeout, ct).ConfigureAwait(false);
        if (to)
        {
            return KyraToolResult.Fail("News", KyraLiveToolErrorKind.Timeout, "News request timed out.", "[Kyra news] Timeout.");
        }

        if (!ok)
        {
            return KyraToolResult.Fail("News", KyraLiveToolErrorKind.HttpError, "Could not load news. Check the API key and network.",
                "[Kyra news] HTTP error.");
        }

        using var doc = KyraLiveToolHttp.TryParseJson(body);
        if (doc is null || !doc.RootElement.TryGetProperty("articles", out var arts) || arts.ValueKind != JsonValueKind.Array)
        {
            return KyraToolResult.Fail("News", KyraLiveToolErrorKind.ParseError, "News response was unreadable.",
                "[Kyra news] Parse error.");
        }

        return FormatArticles("GNews", arts);
    }

    private static KyraToolResult FormatArticles(string providerName, JsonElement arts)
    {
        var lines = new StringBuilder();
        lines.AppendLine(string.IsNullOrEmpty(providerName) ? "Headlines" : $"Top stories ({providerName})");
        lines.AppendLine();
        var sources = new List<KyraToolSourceEntry>();
        var i = 1;
        foreach (var a in arts.EnumerateArray())
        {
            if (i > 6)
            {
                break;
            }

            var title = a.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var src = "";
            if (a.TryGetProperty("source", out var s))
            {
                src = s.ValueKind == JsonValueKind.Object && s.TryGetProperty("name", out var n)
                    ? n.GetString() ?? ""
                    : s.GetString() ?? "";
            }

            string? link = null;
            if (a.TryGetProperty("url", out var u))
            {
                link = u.GetString();
            }

            lines.AppendLine($"{i}. {title} — {src}");
            sources.Add(new KyraToolSourceEntry
            {
                Title = title,
                Provider = src,
                Url = link,
                RetrievedAtUtc = DateTimeOffset.UtcNow
            });
            i++;
        }

        if (i == 1)
        {
            return KyraToolResult.Fail("News", KyraLiveToolErrorKind.ParseError, "No headlines returned for that query.",
                "[Kyra news] Empty result.");
        }

        lines.AppendLine();
        lines.AppendLine($"Provider: {providerName}");
        lines.AppendLine($"Updated: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC");
        var user = lines.ToString().TrimEnd();
        var aug = "[Kyra news | " + providerName + "] Headlines only as listed; do not invent articles or URLs.";
        return KyraToolResult.Ok("News", providerName, user, aug, sources: sources);
    }
}

internal sealed class StockPriceKyraTool(HttpClient http, KyraLiveToolCache cache) : IKyraTool
{
    public string Name => "Stocks";

    public string Description => "Stock quotes via Finnhub (API key).";

    public KyraToolSurfaceCategory SurfaceCategory => KyraToolSurfaceCategory.LiveData;

    public bool CanHandle(KyraIntent intent, string prompt) => intent == KyraIntent.StockPrice;

    public KyraToolOperationalStatus GetOperationalStatus(CopilotSettings settings, KyraToolHostFacts facts)
    {
        var lt = settings.LiveTools ?? new KyraLiveToolsSettings();
        if (!lt.StocksEnabled)
        {
            return KyraToolOperationalStatus.Disabled;
        }

        if (string.IsNullOrWhiteSpace(lt.StocksApiKey) || !(lt.StocksProvider ?? "finnhub").Trim().Equals("finnhub", StringComparison.OrdinalIgnoreCase))
        {
            return KyraToolOperationalStatus.NotConfigured;
        }

        return KyraToolOperationalStatus.Ready;
    }

    public async Task<KyraToolResult> ExecuteAsync(KyraToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var settings = request.Settings;
        var lt = settings.LiveTools ?? new KyraLiveToolsSettings();
        var sym = NormalizeSymbol(FirstSym(request));
        if (string.IsNullOrEmpty(sym))
        {
            return KyraToolResult.Fail(Name, KyraLiveToolErrorKind.BadInput,
                "Stock quotes need a ticker. Try:\n`/stocks NVDA`",
                "[Kyra stocks] No symbol; do not invent prices.");
        }

        if (!lt.StocksEnabled)
        {
            return KyraToolResult.Fail(Name, KyraLiveToolErrorKind.Disabled,
                "Stock live data is turned off in Kyra Advanced → Live APIs.",
                "[Kyra stocks] Disabled.");
        }

        if (string.IsNullOrWhiteSpace(lt.StocksApiKey))
        {
            return KyraToolResult.Fail(Name, KyraLiveToolErrorKind.NotConfigured,
                "Stock live data is not configured yet.\nAdd a **Finnhub** API key in **Kyra Advanced → Live APIs**, then try:\n`/stocks " + sym + "`\n\nInformational only, not financial advice.",
                "[Kyra stocks] Not configured.");
        }

        var timeout = KyraLiveToolHttp.EffectiveTimeoutSeconds(settings);
        var ttl = KyraLiveToolHttp.CacheTtl(settings);
        var cacheKey = KyraLiveToolCache.MakeKey("stock", "finnhub", sym);
        if (cache.TryGet(cacheKey, ttl, out var cached))
        {
            KyraLiveToolTelemetry.Record(Name, KyraToolOperationalStatus.Ready, "Finnhub", true);
            return cached;
        }

        var baseUrl = string.IsNullOrWhiteSpace(lt.StocksBaseUrl) ? "https://finnhub.io/api/v1/quote" : lt.StocksBaseUrl.TrimEnd('/');
        var url = $"{baseUrl}?symbol={Uri.EscapeDataString(sym)}&token={Uri.EscapeDataString(lt.StocksApiKey.Trim())}";
        var (ok, to, body, _) = await KyraLiveToolHttp.GetStringAsync(http, url, timeout, cancellationToken).ConfigureAwait(false);
        if (to)
        {
            KyraLiveToolTelemetry.Record(Name, KyraToolOperationalStatus.TimedOut, "Finnhub", false);
            return KyraToolResult.Fail(Name, KyraLiveToolErrorKind.Timeout,
                "Stock quote timed out. Try again.",
                "[Kyra stocks] Timeout; do not invent prices.");
        }

        if (!ok)
        {
            KyraLiveToolTelemetry.Record(Name, KyraToolOperationalStatus.Failed, "Finnhub", false);
            return KyraToolResult.Fail(Name, KyraLiveToolErrorKind.HttpError,
                "Could not load stock quote. Check the API key or symbol.",
                "[Kyra stocks] HTTP error.");
        }

        using var doc = KyraLiveToolHttp.TryParseJson(body);
        if (doc is null || !doc.RootElement.TryGetProperty("c", out var cEl))
        {
            return KyraToolResult.Fail(Name, KyraLiveToolErrorKind.ParseError,
                "Stock data was unreadable.",
                "[Kyra stocks] Parse error.");
        }

        var price = cEl.GetDouble();
        var prev = doc.RootElement.TryGetProperty("pc", out var pcEl) ? pcEl.GetDouble() : double.NaN;
        var chgPct = doc.RootElement.TryGetProperty("dp", out var dpEl) ? dpEl.GetDouble() : double.NaN;
        var tUnix = doc.RootElement.TryGetProperty("t", out var te) ? te.GetInt64() : 0;
        var updated = tUnix > 0
            ? DateTimeOffset.FromUnixTimeSeconds(tUnix).ToUniversalTime().ToString("yyyy-MM-dd HH:mm UTC", CultureInfo.InvariantCulture)
            : DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm UTC", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.AppendLine(sym.ToUpperInvariant());
        sb.AppendLine();
        sb.AppendLine($"Price: {price:0.##} USD");
        if (!double.IsNaN(chgPct))
        {
            sb.AppendLine($"Change (day %): {chgPct:0.##}%");
        }

        if (!double.IsNaN(prev))
        {
            sb.AppendLine($"Previous close: {prev:0.##}");
        }

        sb.AppendLine();
        sb.AppendLine("Provider: Finnhub");
        sb.AppendLine($"Updated: {updated}");
        sb.AppendLine();
        sb.AppendLine("Informational only, not financial advice.");
        var user = sb.ToString().TrimEnd();
        var aug =
            $"[Kyra stocks | Finnhub] {sym.ToUpperInvariant()} last {price:0.##} USD, change {chgPct:0.##}%. Informational only.";
        var disc = "Informational only, not financial advice.";
        var result = KyraToolResult.Ok(Name, "Finnhub", user, aug, disclaimer: disc);
        cache.Set(cacheKey, result);
        KyraLiveToolTelemetry.Record(Name, KyraToolOperationalStatus.Ready, "Finnhub", false);
        return result;
    }

    private static string? FirstSym(KyraToolExecutionRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ArgumentsLine))
        {
            return request.ArgumentsLine.Trim();
        }

        var p = request.Prompt.Trim();
        if (p.StartsWith("/stocks", StringComparison.OrdinalIgnoreCase))
        {
            var rest = p.Length > "/stocks".Length ? p["/stocks".Length..].Trim() : string.Empty;
            return string.IsNullOrEmpty(rest) ? null : rest;
        }

        if (p.StartsWith("/stock", StringComparison.OrdinalIgnoreCase))
        {
            var rest = p.Length > "/stock".Length ? p["/stock".Length..].Trim() : string.Empty;
            return string.IsNullOrEmpty(rest) ? null : rest;
        }

        return null;
    }

    private static string NormalizeSymbol(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return string.Empty;
        }

        return s.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
    }
}

internal sealed class CryptoPriceKyraTool(HttpClient http, KyraLiveToolCache cache) : IKyraTool
{
    public string Name => "Crypto";

    public string Description => "Crypto prices via CoinGecko (public) or stub future providers.";

    public KyraToolSurfaceCategory SurfaceCategory => KyraToolSurfaceCategory.LiveData;

    public bool CanHandle(KyraIntent intent, string prompt) => intent == KyraIntent.CryptoPrice;

    public KyraToolOperationalStatus GetOperationalStatus(CopilotSettings settings, KyraToolHostFacts facts)
    {
        var lt = settings.LiveTools ?? new KyraLiveToolsSettings();
        if (!lt.CryptoEnabled)
        {
            return KyraToolOperationalStatus.Disabled;
        }

        var p = (lt.CryptoProvider ?? "coingecko").Trim();
        return p.Equals("coingecko", StringComparison.OrdinalIgnoreCase)
            ? KyraToolOperationalStatus.Ready
            : KyraToolOperationalStatus.NotConfigured;
    }

    public async Task<KyraToolResult> ExecuteAsync(KyraToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var settings = request.Settings;
        var lt = settings.LiveTools ?? new KyraLiveToolsSettings();
        var raw = FirstCoin(request);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return KyraToolResult.Fail(Name, KyraLiveToolErrorKind.BadInput,
                "Crypto quotes need a symbol. Try:\n`/crypto BTC`",
                "[Kyra crypto] No symbol.");
        }

        if (!lt.CryptoEnabled)
        {
            return KyraToolResult.Fail(Name, KyraLiveToolErrorKind.Disabled,
                "Crypto live data is turned off in Kyra Advanced → Live APIs.",
                "[Kyra crypto] Disabled.");
        }

        var provider = (lt.CryptoProvider ?? "coingecko").Trim();
        if (!provider.Equals("coingecko", StringComparison.OrdinalIgnoreCase))
        {
            return KyraToolResult.Fail(Name, KyraLiveToolErrorKind.NotConfigured,
                "Crypto live data is not configured for this provider yet.\nUse **coingecko** (no key) in Kyra Advanced → Live APIs.",
                "[Kyra crypto] Provider not supported.");
        }

        if (!TryMapCoin(raw.Trim(), out var id, out var label))
        {
            return KyraToolResult.Fail(Name, KyraLiveToolErrorKind.BadInput,
                $"Unknown coin “{raw}”. Try BTC, ETH, or SOL.",
                "[Kyra crypto] Unknown id.");
        }

        var timeout = KyraLiveToolHttp.EffectiveTimeoutSeconds(settings);
        var ttl = KyraLiveToolHttp.CacheTtl(settings);
        var cacheKey = KyraLiveToolCache.MakeKey("crypto", "coingecko", id);
        if (cache.TryGet(cacheKey, ttl, out var cached))
        {
            KyraLiveToolTelemetry.Record(Name, KyraToolOperationalStatus.Ready, "CoinGecko", true);
            return cached;
        }

        var url = "https://api.coingecko.com/api/v3/simple/price?ids=" + Uri.EscapeDataString(id) +
                  "&vs_currencies=usd&include_24hr_change=true";
        var (ok, to, body, _) = await KyraLiveToolHttp.GetStringAsync(http, url, timeout, cancellationToken).ConfigureAwait(false);
        if (to)
        {
            KyraLiveToolTelemetry.Record(Name, KyraToolOperationalStatus.TimedOut, "CoinGecko", false);
            return KyraToolResult.Fail(Name, KyraLiveToolErrorKind.Timeout,
                "Crypto quote timed out. Try again.",
                "[Kyra crypto] Timeout.");
        }

        if (!ok)
        {
            KyraLiveToolTelemetry.Record(Name, KyraToolOperationalStatus.Failed, "CoinGecko", false);
            return KyraToolResult.Fail(Name, KyraLiveToolErrorKind.HttpError,
                "Could not load crypto price. Try again later.",
                "[Kyra crypto] HTTP error.");
        }

        using var doc = KyraLiveToolHttp.TryParseJson(body);
        if (doc is null || !doc.RootElement.TryGetProperty(id, out var coin) || coin.ValueKind != JsonValueKind.Object)
        {
            return KyraToolResult.Fail(Name, KyraLiveToolErrorKind.ParseError,
                "Crypto data was unreadable.",
                "[Kyra crypto] Parse error.");
        }

        var usd = coin.TryGetProperty("usd", out var u) ? u.GetDouble() : double.NaN;
        var chg = coin.TryGetProperty("usd_24h_change", out var ch) ? ch.GetDouble() : double.NaN;
        var sb = new StringBuilder();
        sb.AppendLine($"{label} ({raw.Trim().ToUpperInvariant()})");
        sb.AppendLine();
        if (!double.IsNaN(usd))
        {
            sb.AppendLine($"Price: ${usd:0.##} USD");
        }

        if (!double.IsNaN(chg))
        {
            sb.AppendLine($"24h: {chg:0.##}%");
        }

        sb.AppendLine();
        sb.AppendLine("Provider: CoinGecko (public)");
        sb.AppendLine($"Updated: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();
        sb.AppendLine("Informational only, not financial advice.");
        var user = sb.ToString().TrimEnd();
        var aug = $"[Kyra crypto | CoinGecko] {label} ~${usd:0.##} USD, 24h {chg:0.##}%. Informational only.";
        var disc = "Informational only, not financial advice.";
        var result = KyraToolResult.Ok(Name, "CoinGecko", user, aug, disclaimer: disc);
        cache.Set(cacheKey, result);
        KyraLiveToolTelemetry.Record(Name, KyraToolOperationalStatus.Ready, "CoinGecko", false);
        return result;
    }

    private static string? FirstCoin(KyraToolExecutionRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ArgumentsLine))
        {
            return request.ArgumentsLine.Trim();
        }

        var p = request.Prompt.Trim();
        return p.StartsWith("/crypto", StringComparison.OrdinalIgnoreCase) && p.Length > 7 ? p[7..].TrimStart() : null;
    }

    private static bool TryMapCoin(string token, out string id, out string label)
    {
        id = "";
        label = "";
        var t = token.ToUpperInvariant();
        if (t is "BTC" or "BITCOIN")
        {
            id = "bitcoin";
            label = "Bitcoin";
            return true;
        }

        if (t is "ETH" or "ETHEREUM")
        {
            id = "ethereum";
            label = "Ethereum";
            return true;
        }

        if (t is "SOL" or "SOLANA")
        {
            id = "solana";
            label = "Solana";
            return true;
        }

        return false;
    }
}

internal sealed class SportsKyraTool(HttpClient http, KyraLiveToolCache cache) : IKyraTool
{
    public string Name => "Sports";

    public string Description => "Team schedules via TheSportsDB (API key).";

    public KyraToolSurfaceCategory SurfaceCategory => KyraToolSurfaceCategory.LiveData;

    public bool CanHandle(KyraIntent intent, string prompt) => intent == KyraIntent.Sports;

    public KyraToolOperationalStatus GetOperationalStatus(CopilotSettings settings, KyraToolHostFacts facts)
    {
        var lt = settings.LiveTools ?? new KyraLiveToolsSettings();
        if (!lt.SportsEnabled)
        {
            return KyraToolOperationalStatus.Disabled;
        }

        if (string.IsNullOrWhiteSpace(lt.SportsApiKey))
        {
            return KyraToolOperationalStatus.NotConfigured;
        }

        return (lt.SportsProvider ?? "thesportsdb").Trim().Equals("thesportsdb", StringComparison.OrdinalIgnoreCase)
            ? KyraToolOperationalStatus.Ready
            : KyraToolOperationalStatus.NotConfigured;
    }

    public async Task<KyraToolResult> ExecuteAsync(KyraToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var settings = request.Settings;
        var lt = settings.LiveTools ?? new KyraLiveToolsSettings();
        var q = FirstQuery(request);
        if (string.IsNullOrWhiteSpace(q))
        {
            return KyraToolResult.Fail(Name, KyraLiveToolErrorKind.BadInput,
                "Sports lookup needs a team or league keyword. Try:\n`/sports Yankees`",
                "[Kyra sports] No query.");
        }

        if (!lt.SportsEnabled)
        {
            return KyraToolResult.Fail(Name, KyraLiveToolErrorKind.Disabled,
                "Sports live data is turned off in Kyra Advanced → Live APIs.",
                "[Kyra sports] Disabled.");
        }

        if (string.IsNullOrWhiteSpace(lt.SportsApiKey))
        {
            return KyraToolResult.Fail(Name, KyraLiveToolErrorKind.NotConfigured,
                "Sports live data is not configured yet.\nAdd a **TheSportsDB** API key in **Kyra Advanced → Live APIs**, then try:\n`/sports " + q + "`",
                "[Kyra sports] Not configured.");
        }

        var timeout = KyraLiveToolHttp.EffectiveTimeoutSeconds(settings);
        var ttl = KyraLiveToolHttp.CacheTtl(settings);
        var cacheKey = KyraLiveToolCache.MakeKey("sports", "tsdb", q.ToLowerInvariant());
        if (cache.TryGet(cacheKey, ttl, out var cached))
        {
            KyraLiveToolTelemetry.Record(Name, KyraToolOperationalStatus.Ready, "TheSportsDB", true);
            return cached;
        }

        var key = lt.SportsApiKey.Trim();
        var root = string.IsNullOrWhiteSpace(lt.SportsBaseUrl)
            ? "https://www.thesportsdb.com/api/v1/json"
            : lt.SportsBaseUrl.TrimEnd('/');
        var searchUrl = $"{root}/{Uri.EscapeDataString(key)}/searchteams.php?t={Uri.EscapeDataString(q)}";
        var (ok, to, body, _) = await KyraLiveToolHttp.GetStringAsync(http, searchUrl, timeout, cancellationToken).ConfigureAwait(false);
        if (to)
        {
            return KyraToolResult.Fail(Name, KyraLiveToolErrorKind.Timeout, "Sports request timed out.", "[Kyra sports] Timeout.");
        }

        if (!ok)
        {
            return KyraToolResult.Fail(Name, KyraLiveToolErrorKind.HttpError, "Could not reach sports API.", "[Kyra sports] HTTP error.");
        }

        using var doc = KyraLiveToolHttp.TryParseJson(body);
        if (doc is null || !doc.RootElement.TryGetProperty("teams", out var teams) || teams.ValueKind != JsonValueKind.Array ||
            teams.GetArrayLength() == 0)
        {
            return KyraToolResult.Fail(Name, KyraLiveToolErrorKind.ParseError,
                $"No team match for “{q}”. Try another spelling.",
                "[Kyra sports] No team.");
        }

        var team = teams[0];
        var teamName = team.TryGetProperty("strTeam", out var st) ? st.GetString() ?? q : q;
        string? teamId = null;
        if (team.TryGetProperty("idTeam", out var idt))
        {
            teamId = idt.ValueKind == JsonValueKind.String ? idt.GetString() : idt.GetInt32().ToString(CultureInfo.InvariantCulture);
        }
        var sb = new StringBuilder();
        sb.AppendLine(teamName);
        sb.AppendLine();
        if (!string.IsNullOrEmpty(teamId))
        {
            var nextUrl = $"{root}/{Uri.EscapeDataString(key)}/eventsnext.php?id={Uri.EscapeDataString(teamId)}";
            var (nOk, nTo, nBody, _) = await KyraLiveToolHttp.GetStringAsync(http, nextUrl, timeout, cancellationToken).ConfigureAwait(false);
            if (nOk && !nTo)
            {
                using var nd = KyraLiveToolHttp.TryParseJson(nBody);
                if (nd?.RootElement.TryGetProperty("events", out var ev) == true && ev.ValueKind == JsonValueKind.Array)
                {
                    sb.AppendLine("Upcoming:");
                    var c = 0;
                    foreach (var e in ev.EnumerateArray())
                    {
                        if (c++ >= 3)
                        {
                            break;
                        }

                        var evName = e.TryGetProperty("strEvent", out var se) ? se.GetString() : "";
                        var date = e.TryGetProperty("dateEvent", out var de) ? de.GetString() : "";
                        if (!string.IsNullOrEmpty(evName))
                        {
                            sb.AppendLine($"- {date} — {evName}");
                        }
                    }
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("Provider: TheSportsDB");
        sb.AppendLine($"Updated: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();
        sb.AppendLine("Scores and schedules come only from the API response—Kyra does not invent results.");
        var user = sb.ToString().TrimEnd();
        var aug = "[Kyra sports | TheSportsDB] Schedule snippet only; do not invent scores.";
        var result = KyraToolResult.Ok(Name, "TheSportsDB", user, aug);
        cache.Set(cacheKey, result);
        KyraLiveToolTelemetry.Record(Name, KyraToolOperationalStatus.Ready, "TheSportsDB", false);
        return result;
    }

    private static string? FirstQuery(KyraToolExecutionRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ArgumentsLine))
        {
            return request.ArgumentsLine.Trim();
        }

        var p = request.Prompt.Trim();
        return p.StartsWith("/sports", StringComparison.OrdinalIgnoreCase) && p.Length > 7 ? p[7..].TrimStart() : null;
    }
}

internal sealed class MarketplaceKyraTool : IKyraTool
{
    public string Name => "Marketplace";

    public string Description => "Live marketplace comparison (future); local pricing engine remains primary.";

    public KyraToolSurfaceCategory SurfaceCategory => KyraToolSurfaceCategory.Marketplace;

    public bool CanHandle(KyraIntent intent, string prompt) => false;

    public KyraToolOperationalStatus GetOperationalStatus(CopilotSettings settings, KyraToolHostFacts facts) =>
        KyraToolOperationalStatus.NotConfigured;

    public Task<KyraToolResult> ExecuteAsync(KyraToolExecutionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(KyraToolResult.Fail(
            Name,
            KyraLiveToolErrorKind.NotConfigured,
            "Live marketplace comparison is not configured yet. I can still estimate from the scanned specs and local pricing rules.",
            "[Kyra marketplace] Not configured; use local PricingEngine only."));
}

