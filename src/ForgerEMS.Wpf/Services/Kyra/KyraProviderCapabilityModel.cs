namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Capability flags for provider engines (routing / future UI diagnostics).</summary>
[Flags]
public enum KyraProviderCapabilities
{
    None = 0,
    SupportsChat = 1 << 0,
    SupportsStreaming = 1 << 1,
    SupportsToolUse = 1 << 2,
    SupportsVision = 1 << 3,
    SupportsCodeHelp = 1 << 4,
    SupportsGeneralKnowledge = 1 << 5,
    SupportsSummarization = 1 << 6,
    SupportsOnlineSearch = 1 << 7,
    SupportsSystemEnhancement = 1 << 8,
    SupportsLocalOnly = 1 << 9,
    RequiresNetwork = 1 << 10,
    RequiresSecret = 1 << 11,
    IsBetaHidden = 1 << 12,
    SupportsLiveData = 1 << 13,
    IsDeveloperOnly = 1 << 14
}

/// <summary>Semantic role of a provider engine under Kyra (never user-facing identity).</summary>
public enum KyraProviderEngineRole
{
    LocalTruthEngine,
    LanguageEnhancer,
    GeneralKnowledgeEngine,
    CodeHelper,
    LiveSearchEngine,
    Summarizer,
    OfflineFallback
}

/// <summary>Structured outcome from a single provider attempt (diagnostics / merge pipeline).</summary>
public sealed class KyraProviderResult
{
    public string ProviderId { get; init; } = string.Empty;

    public bool Success { get; init; }

    public string Text { get; init; } = string.Empty;

    public double Confidence { get; init; }

    public bool UsedContext { get; init; }

    public bool Refused { get; init; }

    public string ErrorCategory { get; init; } = string.Empty;

    public int LatencyMs { get; init; }

    public bool WasDiscarded { get; init; }

    public string DiscardReason { get; init; } = string.Empty;

    /// <summary>True when an online engine contributed wording that was kept in the final Kyra reply.</summary>
    public bool EnhancementApplied { get; init; }

    /// <summary>True when Kyra fell back to offline/local after provider failure or policy.</summary>
    public bool RequiresFallback { get; init; }
}
