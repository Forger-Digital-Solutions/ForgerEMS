namespace ForgerEMS.Kyra.HostAdapter;

/// <summary>Per-invocation dogfood options (memory-only; no persistence).</summary>
public sealed class KyraSdkDogfoodOptions
{
    public const string DefaultUserPrompt = "Kyra SDK dogfood ping from ForgerEMS.";

    public const string DogfoodFeatureId = "hidden-sdk-dogfood";

    public string? UserPrompt { get; init; }

    public string? HostApplicationVersion { get; init; }

    public string? GatewayBaseUrl { get; init; }

    public string? BearerToken { get; init; }

    public KyraHostPrivacyOptions? Privacy { get; init; }

    public KyraHostMode Mode { get; init; } = KyraHostMode.LocalOnly;
}
