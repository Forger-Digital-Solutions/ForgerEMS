using System.Text.Json.Serialization;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>POST /v1/kyra/research contract (sanitized; no provider secrets).</summary>
public sealed class KyraGatewayResearchRequestDto
{
    [JsonPropertyName("requestId")]
    public string RequestId { get; init; } = string.Empty;

    [JsonPropertyName("intent")]
    public string Intent { get; init; } = "chat";

    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = string.Empty;

    [JsonPropertyName("context")]
    public KyraGatewayResearchContextDto Context { get; init; } = new();

    [JsonPropertyName("consent")]
    public KyraGatewayResearchConsentDto Consent { get; init; } = new();
}

public sealed class KyraGatewayResearchContextDto
{
    [JsonPropertyName("machineClass")]
    public string? MachineClass { get; init; }

    [JsonPropertyName("healthScoreBand")]
    public string? HealthScoreBand { get; init; }

    [JsonPropertyName("issueCategory")]
    public string? IssueCategory { get; init; }

    [JsonPropertyName("usbState")]
    public string? UsbState { get; init; }

    [JsonPropertyName("privacyMode")]
    public string PrivacyMode { get; init; } = "local-only";

    /// <summary>Sanitized manufacturer label for parts research (no service tags).</summary>
    [JsonPropertyName("manufacturer")]
    public string? Manufacturer { get; init; }

    /// <summary>Sanitized model / family string (no serials).</summary>
    [JsonPropertyName("modelFamily")]
    public string? ModelFamily { get; init; }

    /// <summary>battery, ram, ssd, charger, dock, or generic part lookup.</summary>
    [JsonPropertyName("partCategory")]
    public string? PartCategory { get; init; }

    [JsonPropertyName("knownLocalFacts")]
    public KyraGatewayKnownLocalFactsDto? KnownLocalFacts { get; init; }
}

/// <summary>Sanitized capability bands only — safe for gateway research context.</summary>
public sealed class KyraGatewayKnownLocalFactsDto
{
    [JsonPropertyName("storageBus")]
    public string? StorageBusBand { get; init; }

    [JsonPropertyName("memoryType")]
    public string? MemoryTypeBand { get; init; }

    [JsonPropertyName("batteryWear")]
    public string? BatteryWearBand { get; init; }

    [JsonPropertyName("ramTotalBand")]
    public string? RamTotalGbBand { get; init; }
}

public sealed class KyraGatewayResearchConsentDto
{
    [JsonPropertyName("gatewayResearch")]
    public bool GatewayResearch { get; init; }

    [JsonPropertyName("communitySharing")]
    public bool CommunitySharing { get; init; }
}

public sealed class KyraGatewayResearchResponseDto
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("answer")]
    public string? Answer { get; init; }

    [JsonPropertyName("tool")]
    public string? Tool { get; init; }

    [JsonPropertyName("provider")]
    public string? Provider { get; init; }

    [JsonPropertyName("freshnessUtc")]
    public string? FreshnessUtc { get; init; }

    [JsonPropertyName("confidence")]
    public string? Confidence { get; init; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("safeMessage")]
    public string? SafeMessage { get; init; }

    [JsonPropertyName("retryAfterSeconds")]
    public int? RetryAfterSeconds { get; init; }
}

public sealed class KyraGatewayStatusResponseDto
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("providers")]
    public KyraGatewayStatusProvidersDto? Providers { get; init; }
}

public sealed class KyraGatewayStatusProvidersDto
{
    [JsonPropertyName("aiChat")]
    public string? AiChat { get; init; }

    [JsonPropertyName("crypto")]
    public string? Crypto { get; init; }

    [JsonPropertyName("weather")]
    public string? Weather { get; init; }

    [JsonPropertyName("finance")]
    public string? Finance { get; init; }

    [JsonPropertyName("news")]
    public string? News { get; init; }

    [JsonPropertyName("webResearch")]
    public string? WebResearch { get; init; }
}
