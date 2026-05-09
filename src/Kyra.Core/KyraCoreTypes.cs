namespace Kyra.Core;

/// <summary>Controls whether operator-facing provider fields are editable. Beta builds default to developer-managed configuration.</summary>
public enum KyraProviderConfigurationMode
{
    DeveloperManaged,
    UserManagedFuture
}

/// <summary>Lifecycle state of a configured provider slot.</summary>
public enum KyraProviderStatus
{
    NotConfigured,
    Configured,
    Ok,
    RateLimited,
    CoolingDown,
    Failed,
    Disabled
}

/// <summary>Coarse health signal surfaced in diagnostics and status badges.</summary>
public enum KyraProviderHealth
{
    Healthy,
    Degraded,
    Unavailable
}

/// <summary>Categorised failure cause attached to <see cref="CopilotProviderResult"/> and <see cref="KyraProviderQuotaState"/> for routing and UI display.</summary>
public enum KyraProviderFailureReason
{
    None,
    NotConfigured,
    AuthFailed,
    RateLimited,
    Timeout,
    ModelUnavailable,
    ServiceUnavailable,
    NetworkError,
    PrivacyBlocked,
    SafetyBlocked,
    Unknown
}

/// <summary>Capability flags used when scoring providers against a request's needs.</summary>
[Flags]
public enum KyraModelCapability
{
    None = 0,
    FastChat = 1,
    DeepReasoning = 2,
    CodeHelp = 4,
    WritingPolish = 8
}

/// <summary>Reason a routing decision kept a turn local rather than forwarding to an online provider.</summary>
public enum KyraStayLocalReason
{
    None = 0,
    MachineContextPrivacy = 1,
    DeviceToolkitRouting = 2,
    LiveDataNotConfigured = 3,
    CodeAssistIsolation = 4
}

/// <summary>Per-provider runtime configuration: endpoint, credentials reference, rate-limit, and quota settings.</summary>
public sealed class CopilotProviderConfiguration
{
    public bool IsEnabled { get; set; }

    public string BaseUrl { get; set; } = string.Empty;

    public string ModelName { get; set; } = string.Empty;

    public string ApiKeyEnvironmentVariable { get; set; } = string.Empty;

    /// <summary>none, session, encrypted-local, environment. Non-secret metadata only.</summary>
    public string KeyStorageMode { get; set; } = "environment";

    public bool SavedKeyPresent { get; set; }

    public DateTimeOffset? LastTestedUtc { get; set; }

    public string LastTestResult { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 12;

    public int MaxRequestsPerMinute { get; set; } = 12;

    public int MaxRetries { get; set; } = 1;

    public int DailyRequestCap { get; set; } = 60;

    public int MaxInputCharacters { get; set; } = 4000;

    public int MaxOutputTokens { get; set; } = 700;
}

/// <summary>Outcome record returned by a single provider call: success flag, message, and structured failure reason for routing fallback.</summary>
public sealed class CopilotProviderResult
{
    public bool Succeeded { get; init; }

    public bool UsedOnlineData { get; init; }

    public bool IsTransientFailure { get; init; }

    public string UserMessage { get; init; } = string.Empty;

    public string DiagnosticMessage { get; init; } = string.Empty;

    public KyraProviderFailureReason FailureReason { get; init; } = KyraProviderFailureReason.None;
}

/// <summary>Routing decision for a turn: whether a local tool handles the answer and whether an online provider should polish the result.</summary>
public sealed class KyraToolCallPlan
{
    public bool ShouldUseLocalToolAnswer { get; init; }
    public bool ShouldPolishWithProvider { get; init; }
    public string ToolName { get; init; } = string.Empty;
    public KyraStayLocalReason StayLocalReason { get; init; }
}

/// <summary>Thrown by provider infrastructure when a call cannot be completed; carries a structured <see cref="KyraProviderFailureReason"/> for routing decisions.</summary>
public sealed class KyraProviderException : Exception
{
    public KyraProviderException(string message, KyraProviderFailureReason reason)
        : base(message)
    {
        Reason = reason;
    }

    public KyraProviderFailureReason Reason { get; }
}

/// <summary>Mutable per-provider quota and health tracking: daily request counts, consecutive failures, and cooldown window.</summary>
public sealed class KyraProviderQuotaState
{
    public bool IsConfigured { get; set; }

    public bool IsEnabled { get; set; }

    public DateTimeOffset? LastSuccessUtc { get; set; }

    public DateTimeOffset? LastFailureUtc { get; set; }

    public KyraProviderFailureReason LastFailureReason { get; set; } = KyraProviderFailureReason.None;

    public int DailyRequestCount { get; set; }

    public int EstimatedTokenUsage { get; set; }

    public int TimeoutCount { get; set; }

    public int ErrorCount { get; set; }

    public int ConsecutiveFailures { get; set; }

    public DateTimeOffset? CooldownUntilUtc { get; set; }
}
