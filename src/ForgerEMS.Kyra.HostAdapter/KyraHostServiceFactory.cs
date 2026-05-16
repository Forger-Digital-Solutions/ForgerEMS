using KyraSdkOptions = global::Kyra.Sdk.KyraSdkOptions;
using KyraSdkPrivacyOptions = global::Kyra.Sdk.KyraSdkPrivacyOptions;
using KyraSdkMode = global::Kyra.Sdk.KyraSdkMode;

namespace ForgerEMS.Kyra.HostAdapter;

/// <summary>Selects NotWired vs SDK host implementation from FORGEREMS_KYRA_SDK_ENABLED (default off).</summary>
public static class KyraHostServiceFactory
{
    public static IKyraHostService Create() =>
        ForgerEmsKyraSdkFeatureFlags.IsSdkEnabledFromEnvironment()
            ? new KyraSdkHostService()
            : new KyraHostServiceNotWired();

    public static IKyraHostService Create(KyraSdkHostServiceOptions? options) =>
        ForgerEmsKyraSdkFeatureFlags.IsSdkEnabledFromEnvironment()
            ? new KyraSdkHostService(options?.ToSdkOptions())
            : new KyraHostServiceNotWired();
}

/// <summary>Optional SDK client configuration (gateway URL only; no token storage).</summary>
public sealed class KyraSdkHostServiceOptions
{
    public string? GatewayBaseUrl { get; init; }

    internal KyraSdkOptions ToSdkOptions() =>
        new()
        {
            GatewayBaseUrl = string.IsNullOrWhiteSpace(GatewayBaseUrl) ? null : GatewayBaseUrl.Trim(),
            Privacy = KyraSdkPrivacyOptions.SafeDefaults,
            DefaultMode = KyraSdkMode.LocalOnly,
        };
}
