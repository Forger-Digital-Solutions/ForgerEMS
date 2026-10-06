using System;
using System.Collections.Generic;

namespace ForgerEMS.Wpf.Services.Resources;

/// <summary>Provider strategies declared by the lead-authored resource policy.</summary>
public enum ResourceProviderKind
{
    GitHubStable,
    ChecksumIndex,
    UbuntuLts,
    OfficialPage
}

/// <summary>Resolution state for a managed resource descriptor.</summary>
public enum ResourceResolutionState
{
    /// <summary>Fresh metadata with a verified expected SHA-256; eligible for download overlay.</summary>
    ResolvedMetadata,

    /// <summary>Explicit manual/vendor-page exception or ambiguous upstream selection; never auto-downloaded.</summary>
    RequiresUserAction,

    /// <summary>Requested target architecture/platform is not supported by this descriptor.</summary>
    Unsupported,

    /// <summary>Metadata could not be fetched or validated; nothing may be downloaded.</summary>
    UnableToVerify,

    /// <summary>Cached metadata older than the policy TTL; informational only, not eligible for downloads.</summary>
    CachedStale
}

public enum ResourceFailureCategory
{
    None,
    Network,
    Timeout,
    UpstreamUnavailable,
    MetadataInvalid,
    AmbiguousSelection,
    NoExpectedHash,
    PolicyUnsupported,
    Cancelled
}

/// <summary>
/// A policy descriptor loaded from manifests/resource-policy.json. Describes how a resource's
/// metadata is resolved; never carries downloaded artifact trust itself.
/// </summary>
public sealed record ResourceDescriptor
{
    public required string ResourceId { get; init; }
    public required string DisplayName { get; init; }
    public required ResourceProviderKind Provider { get; init; }
    public required string SourceUri { get; init; }
    public string? Repository { get; init; }
    public string? AssetPattern { get; init; }
    public string? BaseUrl { get; init; }
    public required string Architecture { get; init; }
    public string Platform { get; init; } = string.Empty;
    public string Channel { get; init; } = ResourcePolicyValues.StableChannel;
    public string? Role { get; init; }
    public string? InstalledVersion { get; init; }
    public IReadOnlyList<string> AllowedHosts { get; init; } = Array.Empty<string>();
    public string? ExceptionReason { get; init; }
}

/// <summary>
/// Result of resolving one resource descriptor. ArtifactUri/ExpectedSha256 are only
/// populated for <see cref="ResourceResolutionState.ResolvedMetadata"/>.
/// </summary>
public sealed record ResourceResolution
{
    public required ResourceDescriptor Descriptor { get; init; }
    public required ResourceResolutionState State { get; init; }
    public string? Version { get; init; }
    public string? ArtifactUri { get; init; }
    public string? ArtifactFileName { get; init; }
    public string? ExpectedSha256 { get; init; }
    public long? SizeBytes { get; init; }
    public DateTimeOffset? PublishedAtUtc { get; init; }
    public DateTimeOffset CheckedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public bool FromCache { get; init; }
    public ResourceFailureCategory FailureCategory { get; init; } = ResourceFailureCategory.None;
    public string? Reason { get; init; }

    // Fixed product-safety facts; never imply automation that does not exist.
    public string InstallMethod { get; init; } = "Manual";
    public string RebootRequirement { get; init; } = "Unknown";
    public string Rollback { get; init; } = "NotAutomated";
    public string Compatibility { get; init; } = "Unknown";

    public string ResourceId => Descriptor.ResourceId;

    /// <summary>True only when the resolution is fresh, verified, hash-bound, and unexpired.</summary>
    public bool IsDownloadEligible =>
        State == ResourceResolutionState.ResolvedMetadata
        && !string.IsNullOrWhiteSpace(ArtifactUri)
        && ResourcePolicyValues.IsHexSha256(ExpectedSha256)
        && ExpiresAtUtc is { } exp
        && exp > DateTimeOffset.UtcNow;
}

public static class ResourcePolicyValues
{
    public const string StableChannel = "stable";

    public static bool IsHexSha256(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value!.Length == 64
        && Uri.IsHexDigit(value[0])
        && System.Text.RegularExpressions.Regex.IsMatch(value, "^[0-9a-fA-F]{64}$");

    /// <summary>Normalize architecture spellings; never guesses — unknown values are returned lower-cased as-is.</summary>
    public static string NormalizeArchitecture(string? architecture)
    {
        var a = (architecture ?? string.Empty).Trim().ToLowerInvariant();
        return a switch
        {
            "amd64" or "x86_64" or "64bit" or "x64" => "x64",
            "x86" or "i386" or "i686" or "32bit" => "x86",
            "arm64" or "aarch64" => "arm64",
            _ => a
        };
    }

    public static bool IsSupportedArchitecture(string? architecture) =>
        NormalizeArchitecture(architecture) is "x64" or "x86" or "arm64";
}
