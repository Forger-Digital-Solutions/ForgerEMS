using System.Text.RegularExpressions;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.KyraTools;

internal sealed class CalculatorKyraTool : IKyraTool
{
    public string Name => "Calculator";

    public string Description => "Local deterministic calculator for simple arithmetic.";

    public KyraToolSurfaceCategory SurfaceCategory => KyraToolSurfaceCategory.LocalContext;

    public bool CanHandle(KyraIntent intent, string prompt) =>
        Regex.IsMatch(prompt, @"(?i)\b(calculate|calculator|what\s+is|what's|math)\b") &&
        Regex.IsMatch(prompt, @"[\d][\d\.\s\+\-\*/\(\)%]+[\d]");

    public KyraToolOperationalStatus GetOperationalStatus(CopilotSettings settings, KyraToolHostFacts facts) =>
        KyraToolOperationalStatus.Ready;

    public Task<KyraToolResult> ExecuteAsync(KyraToolExecutionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(KyraToolResult.Ok(
            Name,
            "ForgerEMS local",
            "Calculator is available locally.",
            "[Kyra calculator] Use local deterministic arithmetic; do not guess numeric results.",
            augment: true,
            disclaimer: "Local utility; no network required."));
    }
}

internal sealed class DateTimeKyraTool : IKyraTool
{
    public string Name => "Date / Time";

    public string Description => "Local date/time utility.";

    public KyraToolSurfaceCategory SurfaceCategory => KyraToolSurfaceCategory.LocalContext;

    public bool CanHandle(KyraIntent intent, string prompt) =>
        Regex.IsMatch(prompt, @"(?i)\b(time|date|today|tomorrow|yesterday)\b");

    public KyraToolOperationalStatus GetOperationalStatus(CopilotSettings settings, KyraToolHostFacts facts) =>
        KyraToolOperationalStatus.Ready;

    public Task<KyraToolResult> ExecuteAsync(KyraToolExecutionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.Now;
        return Task.FromResult(KyraToolResult.Ok(
            Name,
            "ForgerEMS local clock",
            $"Local time: {now:yyyy-MM-dd HH:mm zzz}",
            $"[Kyra date/time] Local clock reports {now:yyyy-MM-dd HH:mm zzz}.",
            augment: true,
            disclaimer: "Uses the device local clock."));
    }
}

internal sealed class FinanceDataKyraTool : IKyraTool
{
    public string Name => "Finance";

    public string Description => "Finance/economic data provider shell; avoids invented live figures.";

    public KyraToolSurfaceCategory SurfaceCategory => KyraToolSurfaceCategory.LiveData;

    public bool CanHandle(KyraIntent intent, string prompt) =>
        intent is KyraIntent.LiveOnlineQuestion or KyraIntent.StockPrice &&
        prompt.Contains("finance", StringComparison.OrdinalIgnoreCase);

    public KyraToolOperationalStatus GetOperationalStatus(CopilotSettings settings, KyraToolHostFacts facts) =>
        KyraToolOperationalStatus.NotConfigured;

    public Task<KyraToolResult> ExecuteAsync(KyraToolExecutionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(KyraToolResult.Fail(
            Name,
            KyraLiveToolErrorKind.NotConfigured,
            "Finance live data is not configured. Kyra needs a configured current-data provider before giving live prices or market figures.",
            "[Kyra finance] No configured finance provider; do not invent live market data."));
    }
}

internal sealed class StatsDataKyraTool : IKyraTool
{
    public string Name => "Stats / Economic Data";

    public string Description => "Statistics/economic data provider shell.";

    public KyraToolSurfaceCategory SurfaceCategory => KyraToolSurfaceCategory.LiveData;

    public bool CanHandle(KyraIntent intent, string prompt) =>
        prompt.Contains("economic data", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("statistics", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("fred", StringComparison.OrdinalIgnoreCase);

    public KyraToolOperationalStatus GetOperationalStatus(CopilotSettings settings, KyraToolHostFacts facts) =>
        KyraToolOperationalStatus.NotConfigured;

    public Task<KyraToolResult> ExecuteAsync(KyraToolExecutionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(KyraToolResult.Fail(
            Name,
            KyraLiveToolErrorKind.NotConfigured,
            "Stats/economic data is not configured. Kyra will not invent current statistics without a configured provider.",
            "[Kyra stats] No configured stats provider; do not invent live economic/statistical data."));
    }
}
