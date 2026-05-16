namespace ForgerEMS.Kyra.HostAdapter;

public sealed class KyraHostPrivacyOptions
{
    public bool AllowCloudContextSharing { get; init; }

    public bool AllowWorkerEnrichment { get; init; }

    public string? RedactedCloudContextSummary { get; init; }

    public static KyraHostPrivacyOptions SafeDefaults { get; } = new();
}
