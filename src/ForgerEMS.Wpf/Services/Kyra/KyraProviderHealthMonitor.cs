namespace VentoyToolkitSetup.Wpf.Services.Kyra;

public enum KyraProviderHealthState
{
    Available,
    Disabled,
    MissingKey,
    Offline,
    RateLimited,
    Error
}

public sealed class KyraProviderHealthStatus
{
    public KyraProviderHealthState State { get; set; } = KyraProviderHealthState.Available;

    public DateTimeOffset? LastSuccessUtc { get; set; }

    public DateTimeOffset? LastFailureUtc { get; set; }

    public string LastErrorCategory { get; set; } = string.Empty;

    public int LastLatencyMs { get; set; }
}

/// <summary>In-process provider health (no secrets; optional diagnostics).</summary>
public static class KyraProviderHealthMonitor
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, KyraProviderHealthStatus> Status = new(StringComparer.OrdinalIgnoreCase);

    public static KyraProviderHealthStatus GetStatus(string providerId)
    {
        lock (Sync)
        {
            if (!Status.TryGetValue(providerId, out var s))
            {
                s = new KyraProviderHealthStatus();
                Status[providerId] = s;
            }

            return s;
        }
    }

    public static void RecordOutcome(string providerId, bool success, int latencyMs, string errorCategory)
    {
        lock (Sync)
        {
            if (!Status.TryGetValue(providerId, out var s))
            {
                s = new KyraProviderHealthStatus();
                Status[providerId] = s;
            }

            s.LastLatencyMs = latencyMs;
            if (success)
            {
                s.State = KyraProviderHealthState.Available;
                s.LastSuccessUtc = DateTimeOffset.UtcNow;
                s.LastErrorCategory = string.Empty;
            }
            else
            {
                s.LastFailureUtc = DateTimeOffset.UtcNow;
                s.LastErrorCategory = errorCategory;
                s.State = errorCategory.Contains("rate", StringComparison.OrdinalIgnoreCase)
                    ? KyraProviderHealthState.RateLimited
                    : KyraProviderHealthState.Error;
            }
        }
    }
}
