namespace Kyra.Core;

/// <summary>Sources for stable facts Kyra treats as authoritative over provider prose.</summary>
public enum KyraFactSource
{
    SystemIntelligence,
    UsbBuilder,
    ToolkitManager,
    UpdateSystem,
    UserMessage,
    KyraLocalAnalysis,
    ProviderEnhancement
}

/// <summary>Trust ordering: higher numeric value = lower authority vs local app facts.</summary>
public enum KyraTrustLevel
{
    TrustedLocalFact = 1,
    UserProvidedFact = 2,
    KyraDerivedLocalAnalysis = 3,
    ProviderSuggestion = 4,
    GeneralKnowledge = 5
}

/// <summary>Compact ledger of machine facts extracted from local context (never from API text).</summary>
public sealed class KyraFactsLedger
{
    public bool HasSystemIntelligenceProfile { get; init; }

    public string DeviceSummary { get; init; } = string.Empty;

    public string CpuSummary { get; init; } = string.Empty;

    public string GpuSummary { get; init; } = string.Empty;

    public string RamSummary { get; init; } = string.Empty;

    public string StorageSummary { get; init; } = string.Empty;

    public string OsSummary { get; init; } = string.Empty;

    public string UsbHeadline { get; init; } = string.Empty;

    public string ToolkitHeadline { get; init; } = string.Empty;

    public int? HealthScore { get; init; }

    /// <summary>True when we have enough structured hardware facts to reject "I can't see your PC" API answers.</summary>
    public bool HasTrustedLocalHardwareFacts =>
        HasSystemIntelligenceProfile ||
        (!string.IsNullOrWhiteSpace(CpuSummary) && !CpuSummary.Contains("Unknown", StringComparison.OrdinalIgnoreCase)) ||
        (!string.IsNullOrWhiteSpace(DeviceSummary) && !DeviceSummary.Contains("Unknown device", StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns a compact multi-line text block suitable for injecting into provider prompts as an authoritative local hardware summary.</summary>
    public string ToPromptSummaryBlock()
    {
        if (!HasTrustedLocalHardwareFacts)
        {
            return "Facts ledger: no full hardware profile — treat hardware specifics as uncertain until a scan exists.";
        }

        var lines = new List<string>
        {
            "Facts ledger (local — authoritative over any model guess):",
            $"Device: {DeviceSummary}",
            $"CPU: {CpuSummary}",
            $"GPU: {GpuSummary}",
            $"RAM: {RamSummary}",
            $"Storage: {StorageSummary}",
            $"OS: {OsSummary}",
            HealthScore is { } hs ? $"Health score: {hs}/100" : "Health score: (not loaded)"
        };

        if (!string.IsNullOrWhiteSpace(UsbHeadline))
        {
            lines.Add($"USB: {UsbHeadline}");
        }

        if (!string.IsNullOrWhiteSpace(ToolkitHeadline))
        {
            lines.Add($"Toolkit: {ToolkitHeadline}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
