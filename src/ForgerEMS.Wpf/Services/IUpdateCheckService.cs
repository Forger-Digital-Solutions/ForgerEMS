using System;
using System.Threading;
using System.Threading.Tasks;

namespace VentoyToolkitSetup.Wpf.Services;

public sealed class UpdateCheckResult
{
    public bool Succeeded { get; init; }

    public bool UpdateAvailable { get; init; }

    public Version? LatestVersion { get; init; }

    public string LatestVersionLabel { get; init; } = string.Empty;

    public string ReleaseNotesUrl { get; init; } = string.Empty;

    public string? InstallerAssetName { get; init; }

    public string? InstallerDownloadUrl { get; init; }

    public string? ErrorMessage { get; init; }

    public bool IsOfflineOrFailed => !Succeeded;
}

public interface IUpdateCheckService
{
    Task<UpdateCheckResult> CheckForNewerReleaseAsync(Version currentVersion, string? ignoredVersionNormalized, CancellationToken cancellationToken = default);
}
