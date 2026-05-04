using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Maps legacy <see cref="CopilotProviderResult"/> into <see cref="KyraProviderResult"/> for logging and tests.</summary>
public static class KyraProviderResultFactory
{
    public static KyraProviderResult FromLegacy(
        string providerId,
        CopilotProviderResult legacy,
        int latencyMs,
        bool usedContext,
        bool enhancementApplied,
        bool wasDiscarded,
        string discardReason,
        bool requiresFallback)
    {
        var refused = legacy.FailureReason == KyraProviderFailureReason.SafetyBlocked;
        return new KyraProviderResult
        {
            ProviderId = providerId ?? string.Empty,
            Success = legacy.Succeeded,
            Text = legacy.UserMessage ?? string.Empty,
            Confidence = legacy.Succeeded ? 1.0 : 0.0,
            UsedContext = usedContext,
            Refused = refused,
            ErrorCategory = legacy.FailureReason.ToString(),
            LatencyMs = latencyMs,
            WasDiscarded = wasDiscarded,
            DiscardReason = discardReason ?? string.Empty,
            EnhancementApplied = enhancementApplied,
            RequiresFallback = requiresFallback
        };
    }
}
