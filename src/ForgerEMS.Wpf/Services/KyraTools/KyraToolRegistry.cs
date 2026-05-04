using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.KyraTools;

public sealed class KyraToolRegistry
{
    private readonly IReadOnlyList<IKyraTool> _tools;

    public KyraToolRegistry(HttpMessageHandler? testHttpHandler = null)
    {
        var http = KyraLiveToolsSharedHttp.Create(testHttpHandler);
        var cache = new KyraLiveToolCache();
        _tools =
        [
            new SystemContextKyraTool(),
            new ToolkitHealthKyraTool(),
            new DiagnosticsKyraTool(),
            new WeatherKyraTool(http, cache),
            new NewsKyraTool(http, cache),
            new CryptoPriceKyraTool(http, cache),
            new StockPriceKyraTool(http, cache),
            new SportsKyraTool(http, cache),
            new WebSearchKyraTool(),
            new MarketplaceKyraTool(),
            new CodeAssistKyraTool()
        ];
    }

    public IReadOnlyList<IKyraTool> Tools => _tools;

    public static string FormatStatusLabel(KyraToolOperationalStatus status) =>
        status switch
        {
            KyraToolOperationalStatus.Ready => "Ready",
            KyraToolOperationalStatus.NotConfigured => "Not configured",
            KyraToolOperationalStatus.Disabled => "Disabled",
            KyraToolOperationalStatus.Failed => "Failed",
            KyraToolOperationalStatus.TimedOut => "Timed out",
            KyraToolOperationalStatus.MissingScan => "Missing scan",
            KyraToolOperationalStatus.Available => "Available",
            _ => "Unknown"
        };

    public static string FormatCategoryLabel(KyraToolSurfaceCategory category) =>
        category switch
        {
            KyraToolSurfaceCategory.LiveData => "Live data",
            KyraToolSurfaceCategory.LocalContext => "Local context",
            KyraToolSurfaceCategory.CodeAssist => "Code assist",
            KyraToolSurfaceCategory.Marketplace => "Marketplace",
            _ => "Other"
        };

    /// <summary>True when at least one live/marketplace tool is ready (e.g. Open-Meteo weather, CoinGecko).</summary>
    public bool HasConfiguredLiveDataCapability(CopilotSettings settings, KyraToolHostFacts facts)
    {
        foreach (var tool in _tools)
        {
            if (tool.SurfaceCategory is not (KyraToolSurfaceCategory.LiveData or KyraToolSurfaceCategory.Marketplace))
            {
                continue;
            }

            var st = tool.GetOperationalStatus(settings, facts);
            if (st is KyraToolOperationalStatus.Ready or KyraToolOperationalStatus.Available)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when a live-data Kyra tool is both eligible for <paramref name="intent"/> and operational (ready/available).</summary>
    public bool HasOperationalLiveDataToolForIntent(
        KyraIntent intent,
        string prompt,
        CopilotSettings settings,
        KyraToolHostFacts facts)
    {
        foreach (var tool in _tools)
        {
            if (tool.SurfaceCategory != KyraToolSurfaceCategory.LiveData)
            {
                continue;
            }

            if (!tool.CanHandle(intent, prompt))
            {
                continue;
            }

            var st = tool.GetOperationalStatus(settings, facts);
            if (st is KyraToolOperationalStatus.Ready or KyraToolOperationalStatus.Available)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Rows for Advanced panel grid (no API keys or secrets).</summary>
    public IReadOnlyList<KyraToolStatusRowView> BuildStatusGridRows(CopilotSettings settings, KyraToolHostFacts facts)
    {
        var list = new List<KyraToolStatusRowView>();
        foreach (var tool in _tools.OrderBy(t => t.SurfaceCategory).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            var st = tool.GetOperationalStatus(settings, facts);
            list.Add(new KyraToolStatusRowView
            {
                ToolName = tool.Name,
                Category = FormatCategoryLabel(tool.SurfaceCategory),
                Status = FormatStatusLabel(st),
                Provider = DescribeProviderColumn(tool, st, settings),
                LastChecked = KyraLiveToolTelemetry.FormatLastCheckedCell(tool.Name),
                Notes = BuildNotes(tool, st, settings)
            });
        }

        return list;
    }

    /// <summary>Multi-line block for /provider (and host snapshot).</summary>
    public string BuildProviderToolDetailText(CopilotSettings settings, KyraToolHostFacts facts, bool verboseLiveLogs)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Verbose beta logs: {(verboseLiveLogs ? "on" : "off")}");
        sb.AppendLine();
        foreach (var tool in _tools.OrderBy(t => t.SurfaceCategory).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            var st = tool.GetOperationalStatus(settings, facts);
            var prov = DescribeProviderColumn(tool, st, settings);
            var last = KyraLiveToolTelemetry.FormatLastCheckedCell(tool.Name);
            sb.AppendLine($"- **{tool.Name}** ({FormatCategoryLabel(tool.SurfaceCategory)}): {FormatStatusLabel(st)} — {prov} — last: {last}");
        }

        sb.AppendLine();
        sb.AppendLine("Weather (Open-Meteo) and crypto (CoinGecko) can work without API keys when enabled. News, stocks, and sports need provider keys in **Kyra Advanced → Live APIs**.");
        return sb.ToString().TrimEnd();
    }

    public string BuildStatusSummary() =>
        "See /provider or Kyra Advanced → Tools for per-tool status. Open-Meteo + CoinGecko are no-key options when enabled.";

    /// <summary>Builds a single augmentation block for API providers (disclaimers + stub status).</summary>
    public async Task<string?> BuildAugmentationAsync(
        KyraToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        foreach (var tool in _tools)
        {
            if (!tool.CanHandle(request.Intent, request.Prompt))
            {
                continue;
            }

            var result = await tool.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
            if (!result.AugmentsProviderPrompt)
            {
                continue;
            }

            var aug = result.EffectiveProviderAugmentation();
            if (!string.IsNullOrWhiteSpace(aug))
            {
                sb.AppendLine(aug.Trim());
                sb.AppendLine();
            }
        }

        var text = sb.ToString().Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    internal static KyraToolHostFacts BuildHostFacts(CopilotRequest request)
    {
        var scan = !string.IsNullOrWhiteSpace(request.SystemIntelligenceReportPath) &&
                   File.Exists(request.SystemIntelligenceReportPath);
        var toolkit = !string.IsNullOrWhiteSpace(request.ToolkitHealthReportPath) &&
                      File.Exists(request.ToolkitHealthReportPath);
        var loc = request.Settings.LiveTools?.DefaultWeatherLocation?.Trim();
        return new KyraToolHostFacts
        {
            HasSystemIntelligenceScan = scan,
            HasToolkitHealthReport = toolkit,
            DefaultWeatherLocation = string.IsNullOrEmpty(loc) ? null : loc
        };
    }

    private static string DescribeProviderColumn(IKyraTool tool, KyraToolOperationalStatus st, CopilotSettings settings)
    {
        var lt = settings.LiveTools ?? new KyraLiveToolsSettings();
        if (st == KyraToolOperationalStatus.NotConfigured || st == KyraToolOperationalStatus.Disabled)
        {
            if (tool.Name.Equals("Weather", StringComparison.OrdinalIgnoreCase) && lt.WeatherEnabled)
            {
                return (lt.WeatherProvider ?? "openmeteo").Trim();
            }

            if (tool.Name.Equals("Crypto", StringComparison.OrdinalIgnoreCase) && lt.CryptoEnabled)
            {
                return (lt.CryptoProvider ?? "coingecko").Trim();
            }

            return "—";
        }

        if (tool.SurfaceCategory is KyraToolSurfaceCategory.LiveData or KyraToolSurfaceCategory.Marketplace)
        {
            if (tool.Name.Equals("Weather", StringComparison.OrdinalIgnoreCase))
            {
                return (lt.WeatherProvider ?? "openmeteo").Trim();
            }

            if (tool.Name.Equals("News", StringComparison.OrdinalIgnoreCase))
            {
                return (lt.NewsProvider ?? "newsapi").Trim();
            }

            if (tool.Name.Equals("Stocks", StringComparison.OrdinalIgnoreCase))
            {
                return (lt.StocksProvider ?? "finnhub").Trim();
            }

            if (tool.Name.Equals("Crypto", StringComparison.OrdinalIgnoreCase))
            {
                return (lt.CryptoProvider ?? "coingecko").Trim();
            }

            if (tool.Name.Equals("Sports", StringComparison.OrdinalIgnoreCase))
            {
                return (lt.SportsProvider ?? "thesportsdb").Trim();
            }

            return st == KyraToolOperationalStatus.Ready ? "Configured" : "—";
        }

        return "ForgerEMS (local)";
    }

    private static string BuildNotes(IKyraTool tool, KyraToolOperationalStatus st, CopilotSettings settings)
    {
        var lt = settings.LiveTools ?? new KyraLiveToolsSettings();
        if (st == KyraToolOperationalStatus.MissingScan)
        {
            return tool.Name.Contains("Toolkit", StringComparison.OrdinalIgnoreCase)
                ? "Run Toolkit Manager scan to populate health context."
                : "Run System Intelligence scan for machine-specific context.";
        }

        if (tool.Name.Equals("Marketplace", StringComparison.OrdinalIgnoreCase))
        {
            return "Live marketplace comparison is not configured yet. Local PricingEngine still estimates from specs.";
        }

        if (st == KyraToolOperationalStatus.NotConfigured &&
            tool.SurfaceCategory is KyraToolSurfaceCategory.LiveData)
        {
            if (tool.Name.Equals("Weather", StringComparison.OrdinalIgnoreCase))
            {
                return "Use openmeteo (no key) or openweather + key in Kyra Advanced → Live APIs.";
            }

            if (tool.Name.Equals("News", StringComparison.OrdinalIgnoreCase))
            {
                return "Enable News and add NewsAPI or GNews key.";
            }

            if (tool.Name.Equals("Stocks", StringComparison.OrdinalIgnoreCase))
            {
                return "Enable Stocks and add Finnhub API key.";
            }

            if (tool.Name.Equals("Crypto", StringComparison.OrdinalIgnoreCase))
            {
                return "Use coingecko (no key) or configure another provider when available.";
            }

            if (tool.Name.Equals("Sports", StringComparison.OrdinalIgnoreCase))
            {
                return "Enable Sports and add TheSportsDB API key.";
            }

            return "Configure provider/API in Kyra Advanced → Live APIs.";
        }

        if (tool.SurfaceCategory == KyraToolSurfaceCategory.CodeAssist)
        {
            return "Routes pasted snippets to code-assist prompts; never executes code.";
        }

        return tool.Description;
    }
}
