using System;
using System.Threading;
using System.Threading.Tasks;

namespace VentoyToolkitSetup.Wpf.Services;

public enum UpdateCheckFailureKind
{
    None,
    Cancelled,
    Network,
    Timeout,
    /// <summary>Deprecated path; prefer <see cref="UpdateSourceUnreachable"/> or success with <see cref="UpdateCheckOutcome.NoPublishedRelease"/>.</summary>
    ReleaseEndpointNotFound,
    NoPublishedRelease,
    /// <summary>GitHub API could not be reached for this repo (404 on releases, moved repo, etc.).</summary>
    UpdateSourceUnreachable,
    AccessDeniedOrRateLimited,
    ReleaseMetadataInvalid,
    HttpError,
    Unknown
}

/// <summary>Which GitHub Releases are considered when comparing versions.</summary>
public enum UpdateReleaseChannel
{
    /// <summary>Only non-prerelease (stable) GitHub releases.</summary>
    StableOnly,

    /// <summary>Stable and prerelease (beta/RC) releases.</summary>
    BetaRcAllowed
}

/// <summary>Resolved outcome of an update check (success path and high-level classification).</summary>
public enum UpdateCheckOutcome
{
    None,
    UpdateAvailable,
    AlreadyLatest,
    InstalledNewerThanLatestPublic,
    NoPublishedRelease,
    IgnoredVersion,
    Cancelled,
    Failed
}

public sealed class UpdateCheckResult
{
    public bool Succeeded { get; init; }

    public bool UpdateAvailable { get; init; }

    public UpdateCheckOutcome Outcome { get; init; }

    public Version? LatestVersion { get; init; }

    public string LatestVersionLabel { get; init; } = string.Empty;

    public string ReleaseNotesUrl { get; init; } = string.Empty;

    public string? InstallerAssetName { get; init; }

    public string? InstallerDownloadUrl { get; init; }

    /// <summary>Preferred distribution: beta ZIP when present.</summary>
    public string? RecommendedZipAssetName { get; init; }

    public string? RecommendedZipDownloadUrl { get; init; }

    /// <summary>HTTPS URL to CHECKSUMS.sha256 when published on the release.</summary>
    public string? ChecksumsDownloadUrl { get; init; }

    /// <summary>HTTPS URL to DOWNLOAD_BETA.txt when published on the release.</summary>
    public string? DownloadInstructionsUrl { get; init; }

    /// <summary>True when the release tag/name could not be parsed to semver; compare used publish date only for selection.</summary>
    public bool VersionComparisonUncertain { get; init; }

    /// <summary>True when a Beta-style or ForgerEMS-v*.zip pattern was matched.</summary>
    public bool RecommendedZipPatternMatched { get; init; }

    /// <summary>True when an update is offered but no preferred-pattern ZIP was found (still may have a generic .zip or EXE).</summary>
    public bool RecommendedZipAssetMissing { get; init; }

    /// <summary>Short, user-facing explanation (error, hint, or secondary success detail).</summary>
    public string? ErrorMessage { get; init; }

    public UpdateCheckFailureKind FailureKind { get; init; } = UpdateCheckFailureKind.None;

    /// <summary>Optional technical detail for logs / support (HTTP body snippet, exception type, etc.).</summary>
    public string? DiagnosticDetail { get; init; }

    public bool IsOfflineOrFailed => !Succeeded;

    public bool HasRecommendedZipDownload =>
        !string.IsNullOrWhiteSpace(RecommendedZipDownloadUrl) &&
        Uri.TryCreate(RecommendedZipDownloadUrl, UriKind.Absolute, out var zipUri) &&
        string.Equals(zipUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
        zipUri.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    public bool HasActionableInstaller =>
        !string.IsNullOrWhiteSpace(InstallerDownloadUrl) &&
        Uri.TryCreate(InstallerDownloadUrl, UriKind.Absolute, out var uri) &&
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    public bool HasPrimaryDownload => HasRecommendedZipDownload || HasActionableInstaller;
}

public interface IUpdateCheckService
{
    Task<UpdateCheckResult> CheckForNewerReleaseAsync(
        string installedVersionLabel,
        string? ignoredVersionNormalized,
        UpdateReleaseChannel channel = UpdateReleaseChannel.BetaRcAllowed,
        CancellationToken cancellationToken = default);
}
