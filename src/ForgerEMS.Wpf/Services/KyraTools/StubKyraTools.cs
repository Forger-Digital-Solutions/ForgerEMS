using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.KyraTools;

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
