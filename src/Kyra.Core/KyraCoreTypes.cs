namespace Kyra.Core;

/// <summary>Controls whether operator-facing provider fields are editable. Beta builds default to developer-managed configuration.</summary>
public enum KyraProviderConfigurationMode
{
    DeveloperManaged,
    UserManagedFuture
}

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

public enum KyraProviderHealth
{
    Healthy,
    Degraded,
    Unavailable
}

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

[Flags]
public enum KyraModelCapability
{
    None = 0,
    FastChat = 1,
    DeepReasoning = 2,
    CodeHelp = 4,
    WritingPolish = 8
}

public enum KyraStayLocalReason
{
    None = 0,
    MachineContextPrivacy = 1,
    DeviceToolkitRouting = 2,
    LiveDataNotConfigured = 3,
    CodeAssistIsolation = 4
}

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

public sealed class CopilotProviderResult
{
    public bool Succeeded { get; init; }

    public bool UsedOnlineData { get; init; }

    public bool IsTransientFailure { get; init; }

    public string UserMessage { get; init; } = string.Empty;

    public string DiagnosticMessage { get; init; } = string.Empty;

    public KyraProviderFailureReason FailureReason { get; init; } = KyraProviderFailureReason.None;
}

public sealed class KyraToolCallPlan
{
    public bool ShouldUseLocalToolAnswer { get; init; }
    public bool ShouldPolishWithProvider { get; init; }
    public string ToolName { get; init; } = string.Empty;
    public KyraStayLocalReason StayLocalReason { get; init; }
}

public sealed class KyraProviderException : Exception
{
    public KyraProviderException(string message, KyraProviderFailureReason reason)
        : base(message)
    {
        Reason = reason;
    }

    public KyraProviderFailureReason Reason { get; }
}

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
