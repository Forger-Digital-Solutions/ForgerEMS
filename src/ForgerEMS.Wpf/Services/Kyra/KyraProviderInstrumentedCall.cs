using System.Diagnostics;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Wraps a provider call with timing, health telemetry, and normalized <see cref="KyraProviderResult"/>.</summary>
public static class KyraProviderInstrumentedCall
{
    public static async Task<(CopilotProviderResult Legacy, KyraProviderResult Normalized)> RunAsync(
        Func<Task<CopilotProviderResult>> execute,
        string providerId,
        bool isOnlineProvider,
        bool usedContext,
        bool enhancementApplied,
        bool wasDiscarded,
        string discardReason,
        bool requiresFallback)
    {
        var sw = Stopwatch.StartNew();
        var legacy = await execute().ConfigureAwait(false);
        sw.Stop();
        var ms = (int)sw.ElapsedMilliseconds;
        KyraProviderHealthMonitor.RecordOutcome(providerId, legacy.Succeeded, ms, legacy.FailureReason.ToString());
        var normalized = KyraProviderResultFactory.FromLegacy(
            providerId,
            legacy,
            ms,
            usedContext,
            enhancementApplied && legacy.Succeeded && isOnlineProvider && legacy.UsedOnlineData,
            wasDiscarded,
            discardReason,
            requiresFallback);
        return (legacy, normalized);
    }
}
