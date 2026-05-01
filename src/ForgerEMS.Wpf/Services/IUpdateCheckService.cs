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
    ReleaseEndpointNotFound,
    NoPublishedRelease,
    AccessDeniedOrRateLimited,
    ReleaseMetadataInvalid,
    HttpError,
    Unknown
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

    /// <summary>Short, user-facing explanation (error, hint, or secondary success detail).</summary>
    public string? ErrorMessage { get; init; }

    public UpdateCheckFailureKind FailureKind { get; init; } = UpdateCheckFailureKind.None;

    /// <summary>Optional technical detail for logs / support (HTTP body snippet, exception type, etc.).</summary>
    public string? DiagnosticDetail { get; init; }

    public bool IsOfflineOrFailed => !Succeeded;

    public bool HasActionableInstaller =>
        !string.IsNullOrWhiteSpace(InstallerDownloadUrl) &&
        Uri.TryCreate(InstallerDownloadUrl, UriKind.Absolute, out var uri) &&
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
}

public interface IUpdateCheckService
{
    Task<UpdateCheckResult> CheckForNewerReleaseAsync(
        string installedVersionLabel,
        string? ignoredVersionNormalized,
        CancellationToken cancellationToken = default);
}
