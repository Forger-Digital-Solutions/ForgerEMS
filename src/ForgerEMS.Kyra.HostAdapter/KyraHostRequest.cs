using System.Text.Json.Serialization;

namespace ForgerEMS.Kyra.HostAdapter;

public sealed class KyraHostRequest
{
    public KyraHostMode? Mode { get; init; }

    public string? UserPrompt { get; init; }

    public string? GatewayBaseUrl { get; init; }

    [JsonIgnore]
    public string? BearerToken { get; init; }

    public KyraHostPrivacyOptions? Privacy { get; init; }

    public string? HostApplicationId { get; init; }

    public string? HostSessionId { get; init; }

    public string? HostApplicationVersion { get; init; }

    public string? RedactedDeviceReportSummary { get; init; }
}
