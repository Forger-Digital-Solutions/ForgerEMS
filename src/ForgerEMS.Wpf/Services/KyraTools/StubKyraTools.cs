using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.KyraTools;

internal sealed class WeatherKyraTool : IKyraTool
{
    public string Name => "Weather";

    public string Description => "Live weather (requires external API configuration).";

    public KyraToolSurfaceCategory SurfaceCategory => KyraToolSurfaceCategory.LiveData;

    public bool CanHandle(KyraIntent intent, string prompt) => intent == KyraIntent.Weather;

    public KyraToolOperationalStatus GetOperationalStatus(CopilotSettings settings, KyraToolHostFacts facts) =>
        KyraToolOperationalStatus.NotConfigured;

    public Task<KyraToolResult> ExecuteAsync(KyraToolExecutionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new KyraToolResult
        {
            AugmentsProviderPrompt = true,
            ProviderAugmentation =
                "[Kyra real-time tools] Weather: not configured yet. " +
                "Kyra should answer honestly that live weather needs a configured provider (e.g. OpenWeather-style API in a future build). " +
                "Do not invent temperatures. Suggest checking a trusted weather site or enabling the tool when available."
        });
}

internal sealed class NewsKyraTool : IKyraTool
{
    public string Name => "News";

    public string Description => "News headlines (requires external API).";

    public KyraToolSurfaceCategory SurfaceCategory => KyraToolSurfaceCategory.LiveData;

    public bool CanHandle(KyraIntent intent, string prompt) => intent == KyraIntent.News;

    public KyraToolOperationalStatus GetOperationalStatus(CopilotSettings settings, KyraToolHostFacts facts) =>
        KyraToolOperationalStatus.NotConfigured;

    public Task<KyraToolResult> ExecuteAsync(KyraToolExecutionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new KyraToolResult
        {
            AugmentsProviderPrompt = true,
            ProviderAugmentation =
                "[Kyra real-time tools] News: not configured yet. Do not fabricate headlines. " +
                "Suggest reputable news sources or RSS when the user needs current events."
        });
}

internal sealed class CryptoPriceKyraTool : IKyraTool
{
    public string Name => "Crypto";

    public string Description => "Crypto prices (requires external API).";

    public KyraToolSurfaceCategory SurfaceCategory => KyraToolSurfaceCategory.LiveData;

    public bool CanHandle(KyraIntent intent, string prompt) => intent == KyraIntent.CryptoPrice;

    public KyraToolOperationalStatus GetOperationalStatus(CopilotSettings settings, KyraToolHostFacts facts) =>
        KyraToolOperationalStatus.NotConfigured;

    public Task<KyraToolResult> ExecuteAsync(KyraToolExecutionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new KyraToolResult
        {
            AugmentsProviderPrompt = true,
            ProviderAugmentation =
                "[Kyra real-time tools] Crypto quotes: not configured yet. " +
                "Any price discussion must include that numbers are informational only, not financial advice, and live quotes need a configured market data provider."
        });
}

internal sealed class StockPriceKyraTool : IKyraTool
{
    public string Name => "Stocks";

    public string Description => "Stock prices (requires external API).";

    public KyraToolSurfaceCategory SurfaceCategory => KyraToolSurfaceCategory.LiveData;

    public bool CanHandle(KyraIntent intent, string prompt) => intent == KyraIntent.StockPrice;

    public KyraToolOperationalStatus GetOperationalStatus(CopilotSettings settings, KyraToolHostFacts facts) =>
        KyraToolOperationalStatus.NotConfigured;

    public Task<KyraToolResult> ExecuteAsync(KyraToolExecutionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new KyraToolResult
        {
            AugmentsProviderPrompt = true,
            ProviderAugmentation =
                "[Kyra real-time tools] Stock quotes: not configured yet. " +
                "Do not invent tickers or prices. Note estimates are informational only, not investment advice."
        });
}

internal sealed class SportsKyraTool : IKyraTool
{
    public string Name => "Sports";

    public string Description => "Sports scores (requires external API).";

    public KyraToolSurfaceCategory SurfaceCategory => KyraToolSurfaceCategory.LiveData;

    public bool CanHandle(KyraIntent intent, string prompt) => intent == KyraIntent.Sports;

    public KyraToolOperationalStatus GetOperationalStatus(CopilotSettings settings, KyraToolHostFacts facts) =>
        KyraToolOperationalStatus.NotConfigured;

    public Task<KyraToolResult> ExecuteAsync(KyraToolExecutionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new KyraToolResult
        {
            AugmentsProviderPrompt = true,
            ProviderAugmentation =
                "[Kyra real-time tools] Sports scores: not configured yet. Do not invent scores or standings."
        });
}

internal sealed class WebSearchKyraTool : IKyraTool
{
    public string Name => "WebSearch";

    public string Description => "Web search (requires external search API).";

    public KyraToolSurfaceCategory SurfaceCategory => KyraToolSurfaceCategory.LiveData;

    public bool CanHandle(KyraIntent intent, string prompt) =>
        intent == KyraIntent.LiveOnlineQuestion &&
        (prompt.Contains("search", StringComparison.OrdinalIgnoreCase) ||
         prompt.Contains("google", StringComparison.OrdinalIgnoreCase) ||
         prompt.Contains("look up online", StringComparison.OrdinalIgnoreCase));

    public KyraToolOperationalStatus GetOperationalStatus(CopilotSettings settings, KyraToolHostFacts facts) =>
        KyraToolOperationalStatus.NotConfigured;

    public Task<KyraToolResult> ExecuteAsync(KyraToolExecutionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new KyraToolResult
        {
            AugmentsProviderPrompt = true,
            ProviderAugmentation =
                "[Kyra real-time tools] Web search: not configured yet. Answer from general knowledge only and say live crawl/search is unavailable until wired."
        });
}

internal sealed class SystemContextKyraTool : IKyraTool
{
    public string Name => "System Context";

    public string Description => "Summarized local system context (already in CopilotContext).";

    public KyraToolSurfaceCategory SurfaceCategory => KyraToolSurfaceCategory.LocalContext;

    public bool CanHandle(KyraIntent intent, string prompt) => false;

    public KyraToolOperationalStatus GetOperationalStatus(CopilotSettings settings, KyraToolHostFacts facts) =>
        facts.HasSystemIntelligenceScan ? KyraToolOperationalStatus.Available : KyraToolOperationalStatus.MissingScan;

    public Task<KyraToolResult> ExecuteAsync(KyraToolExecutionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new KyraToolResult { AugmentsProviderPrompt = false });
}

internal sealed class ToolkitHealthKyraTool : IKyraTool
{
    public string Name => "Toolkit Health";

    public string Description => "Toolkit health JSON summary (in context).";

    public KyraToolSurfaceCategory SurfaceCategory => KyraToolSurfaceCategory.LocalContext;

    public bool CanHandle(KyraIntent intent, string prompt) => false;

    public KyraToolOperationalStatus GetOperationalStatus(CopilotSettings settings, KyraToolHostFacts facts) =>
        facts.HasToolkitHealthReport ? KyraToolOperationalStatus.Available : KyraToolOperationalStatus.MissingScan;

    public Task<KyraToolResult> ExecuteAsync(KyraToolExecutionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new KyraToolResult { AugmentsProviderPrompt = false });
}

internal sealed class DiagnosticsKyraTool : IKyraTool
{
    public string Name => "Diagnostics";

    public string Description => "Diagnostics (in context).";

    public KyraToolSurfaceCategory SurfaceCategory => KyraToolSurfaceCategory.LocalContext;

    public bool CanHandle(KyraIntent intent, string prompt) => false;

    public KyraToolOperationalStatus GetOperationalStatus(CopilotSettings settings, KyraToolHostFacts facts) =>
        KyraToolOperationalStatus.Available;

    public Task<KyraToolResult> ExecuteAsync(KyraToolExecutionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new KyraToolResult { AugmentsProviderPrompt = false });
}

internal sealed class CodeAssistKyraTool : IKyraTool
{
    public string Name => "Code Assist";

    public string Description => "Code / snippet assistance routing.";

    public KyraToolSurfaceCategory SurfaceCategory => KyraToolSurfaceCategory.CodeAssist;

    public bool CanHandle(KyraIntent intent, string prompt) => intent == KyraIntent.CodeAssist;

    public KyraToolOperationalStatus GetOperationalStatus(CopilotSettings settings, KyraToolHostFacts facts) =>
        KyraToolOperationalStatus.Ready;

    public Task<KyraToolResult> ExecuteAsync(KyraToolExecutionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new KyraToolResult
        {
            AugmentsProviderPrompt = true,
            ProviderAugmentation =
                "[Kyra code assist] User may have pasted code. " +
                "Do not execute code. Explain issues, offer a corrected snippet, warn if destructive. " +
                "Prefer concise steps; match likely language: " + KyraCodeSnippetDetector.GuessLanguageHint(request.Prompt) + "."
        });
}

internal sealed class MarketplaceKyraTool : IKyraTool
{
    public string Name => "Marketplace";

    public string Description => "Live marketplace comps (requires API).";

    public KyraToolSurfaceCategory SurfaceCategory => KyraToolSurfaceCategory.Marketplace;

    public bool CanHandle(KyraIntent intent, string prompt) => false;

    public KyraToolOperationalStatus GetOperationalStatus(CopilotSettings settings, KyraToolHostFacts facts) =>
        KyraToolOperationalStatus.NotConfigured;

    public Task<KyraToolResult> ExecuteAsync(KyraToolExecutionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new KyraToolResult { AugmentsProviderPrompt = false });
}
