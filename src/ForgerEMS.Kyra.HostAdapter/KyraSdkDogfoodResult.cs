namespace ForgerEMS.Kyra.HostAdapter;

/// <summary>Redacted dogfood outcome (safe for logs and diagnostic files).</summary>
public sealed class KyraSdkDogfoodResult
{
    public bool SdkFlagActive { get; init; }

    public bool Succeeded { get; init; }

    public string? ErrorCode { get; init; }

    public string? SafeMessage { get; init; }

    public KyraHostMode Mode { get; init; }

    public bool LocalInvoked { get; init; }

    public bool WorkerInvoked { get; init; }

    public bool GatewayUrlConfigured { get; init; }

    public bool GatewayTokenPresent { get; init; }

    public bool AllowCloudContextSharing { get; init; }

    public bool AllowWorkerEnrichment { get; init; }

    public string? ResponseTextPreview { get; init; }

    public IReadOnlyList<string> ToSafeReportLines()
    {
        var lines = new List<string>
        {
            "ForgerEMS Kyra SDK dogfood (hidden)",
            $"SdkFlagActive: {SdkFlagActive}",
            $"Mode: {Mode}",
            $"Succeeded: {Succeeded}",
            $"ErrorCode: {ErrorCode ?? "(none)"}",
            $"SafeMessage: {SafeMessage ?? "(none)"}",
            $"LocalInvoked: {LocalInvoked}",
            $"WorkerInvoked: {WorkerInvoked}",
            $"GatewayUrlConfigured: {GatewayUrlConfigured}",
            $"GatewayTokenPresent: {GatewayTokenPresent}",
            $"AllowCloudContextSharing: {AllowCloudContextSharing}",
            $"AllowWorkerEnrichment: {AllowWorkerEnrichment}",
        };

        if (!string.IsNullOrWhiteSpace(ResponseTextPreview))
            lines.Add($"ResponseTextPreview: {ResponseTextPreview}");

        return lines;
    }
}
