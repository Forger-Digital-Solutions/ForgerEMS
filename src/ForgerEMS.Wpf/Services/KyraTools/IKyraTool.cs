using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.KyraTools;

public interface IKyraTool
{
    string Name { get; }

    string Description { get; }

    KyraToolSurfaceCategory SurfaceCategory { get; }

    bool CanHandle(KyraIntent intent, string prompt);

    Task<KyraToolResult> ExecuteAsync(KyraToolExecutionRequest request, CancellationToken cancellationToken);

    /// <summary>UI/provider status; must not include secrets or API key values.</summary>
    KyraToolOperationalStatus GetOperationalStatus(CopilotSettings settings, KyraToolHostFacts facts);
}

public sealed class KyraToolExecutionRequest
{
    public KyraIntent Intent { get; init; }

    public string Prompt { get; init; } = string.Empty;

    public CopilotContext Context { get; init; } = new();

    public CopilotSettings Settings { get; init; } = new();

    /// <summary>Slash or extracted argument line (e.g. ZIP); must not contain system diagnostics.</summary>
    public string ArgumentsLine { get; init; } = string.Empty;

    public KyraToolHostFacts HostFacts { get; init; }
}

public sealed class KyraToolResult
{
    public bool Success { get; init; }

    public string Status { get; init; } = string.Empty;

    public string ToolName { get; init; } = string.Empty;

    public string ProviderName { get; init; } = string.Empty;

    public DateTimeOffset TimestampUtc { get; init; }

    /// <summary>Safe to show in chat; never include secrets or raw JSON.</summary>
    public string UserFacingSummary { get; init; } = string.Empty;

    /// <summary>Sanitized summary for LLM augmentation; never include API keys or full URLs with secrets.</summary>
    public string ProviderAugmentation { get; init; } = string.Empty;

    public IReadOnlyList<KyraToolSourceEntry> Sources { get; init; } = Array.Empty<KyraToolSourceEntry>();

    public KyraLiveToolErrorKind ErrorKind { get; init; } = KyraLiveToolErrorKind.None;

    public string SafeErrorMessage { get; init; } = string.Empty;

    public bool IsRealtime { get; init; } = true;

    public string Disclaimer { get; init; } = string.Empty;

    /// <summary>True when served from in-memory cache (safe payload only).</summary>
    public bool FromCache { get; init; }

    public bool AugmentsProviderPrompt { get; init; }

    public static KyraToolResult Ok(
        string toolName,
        string providerName,
        string userText,
        string augmentation,
        bool augment = true,
        string disclaimer = "",
        IReadOnlyList<KyraToolSourceEntry>? sources = null,
        bool fromCache = false) =>
        new()
        {
            Success = true,
            Status = fromCache ? "Cached result used" : "Ready",
            ToolName = toolName,
            ProviderName = providerName,
            TimestampUtc = DateTimeOffset.UtcNow,
            UserFacingSummary = userText,
            ProviderAugmentation = augmentation,
            Sources = sources ?? Array.Empty<KyraToolSourceEntry>(),
            AugmentsProviderPrompt = augment,
            Disclaimer = disclaimer,
            FromCache = fromCache,
            IsRealtime = true
        };

    public static KyraToolResult Fail(
        string toolName,
        KyraLiveToolErrorKind kind,
        string userMessage,
        string safeAugmentation,
        string providerName = "") =>
        new()
        {
            Success = false,
            Status = kind switch
            {
                KyraLiveToolErrorKind.NotConfigured => "Not configured",
                KyraLiveToolErrorKind.Disabled => "Disabled",
                KyraLiveToolErrorKind.Timeout => "Timed out",
                KyraLiveToolErrorKind.BadInput => "Needs input",
                _ => "Failed"
            },
            ToolName = toolName,
            ProviderName = providerName,
            TimestampUtc = DateTimeOffset.UtcNow,
            UserFacingSummary = userMessage,
            ProviderAugmentation = safeAugmentation,
            ErrorKind = kind,
            SafeErrorMessage = userMessage,
            AugmentsProviderPrompt = !string.IsNullOrWhiteSpace(safeAugmentation)
        };

    /// <summary>Backward-compatible augmentation text for orchestrator.</summary>
    public string EffectiveProviderAugmentation()
    {
        if (!string.IsNullOrWhiteSpace(ProviderAugmentation))
        {
            return ProviderAugmentation.Trim();
        }

        if (!AugmentsProviderPrompt || !Success)
        {
            return string.Empty;
        }

        var lines = new List<string> { $"[{ToolName} | {ProviderName}] {UserFacingSummary.Trim()}" };
        foreach (var s in Sources)
        {
            var u = string.IsNullOrEmpty(s.Url) ? "" : $" ({s.Url})";
            lines.Add($"- {s.Title} — {s.Provider}{u}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
