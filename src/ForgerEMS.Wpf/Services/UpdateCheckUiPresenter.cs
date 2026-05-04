using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;

namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>
/// Pure mapping from update-check results to Settings/banner UI fields (no side effects).
/// </summary>
public sealed record UpdateCheckViewState(
    string StatusText,
    /// <summary>When null, the previous &quot;Latest release&quot; line should be left unchanged (e.g. failed check).</summary>
    string? LatestChannelSummary,
    Visibility BannerVisibility,
    /// <summary>When null, do not overwrite the existing banner title (background failure / quiet success).</summary>
    string? BannerTitle,
    /// <summary>When null, do not overwrite the existing banner detail.</summary>
    string? BannerDetail,
    Visibility DownloadButtonVisibility,
    Visibility IgnoreButtonVisibility,
    Visibility ReleaseNotesVisibility,
    Visibility DiagnosticsHintVisibility,
    /// <summary>Primary download URL (HTTPS ZIP preferred, else installer EXE).</summary>
    string PendingInstallerUrl,
    string PendingReleaseNotesUrl,
    string PendingVersionLabel,
    /// <summary>Safe, non-secret text for Diagnostics logs; null when nothing extra should be logged.</summary>
    string? SafeDiagnosticText,
    string InstalledVersionNormalized,
    string? LatestVersionNormalized,
    string PendingAdvancedInstallerUrl = "",
    Visibility AdvancedInstallerDownloadVisibility = Visibility.Collapsed,
    Visibility CopyZipLinkVisibility = Visibility.Collapsed,
    string PendingZipUrlForClipboard = "",
    Visibility CopyChecksumInstructionsVisibility = Visibility.Collapsed,
    string ChecksumInstructionsClipboardText = "");

public static class UpdateCheckUiPresenter
{
    public const string DefaultChecksumInstructions =
        "Download CHECKSUMS.sha256 from the same GitHub Release, then verify the ZIP or EXE hash on your PC before running anything.";

    public static UpdateCheckViewState Map(
        UpdateCheckResult result,
        bool isManualCheck,
        string installedVersionLabel)
    {
        var installedNorm = ReleaseVersionParser.NormalizeLabel(installedVersionLabel);
        var latestNorm = string.IsNullOrWhiteSpace(result.LatestVersionLabel)
            ? null
            : ReleaseVersionParser.NormalizeLabel(result.LatestVersionLabel);

        if (!result.Succeeded)
        {
            return MapFailure(result, isManualCheck, installedNorm, latestNorm);
        }

        if (result is { Outcome: UpdateCheckOutcome.UpdateAvailable, UpdateAvailable: true })
        {
            return MapUpdateAvailable(result, installedNorm, latestNorm);
        }

        return MapSuccessQuiet(result, installedNorm, latestNorm);
    }

    private static UpdateCheckViewState MapFailure(
        UpdateCheckResult result,
        bool isManualCheck,
        string installedNorm,
        string? latestNorm)
    {
        var status = result.FailureKind switch
        {
            UpdateCheckFailureKind.Network => "Could not check for updates (offline or network unavailable).",
            UpdateCheckFailureKind.Timeout => "Update check timed out. Try again later.",
            UpdateCheckFailureKind.ReleaseEndpointNotFound => "Update source could not be reached.",
            UpdateCheckFailureKind.UpdateSourceUnreachable => "Update source could not be reached (GitHub or TLS/proxy).",
            UpdateCheckFailureKind.AccessDeniedOrRateLimited =>
                (result.ErrorMessage ?? string.Empty).Contains("rate", StringComparison.OrdinalIgnoreCase)
                    ? "GitHub rate-limited this update check. Wait and try again."
                    : "GitHub denied access to the update endpoint (403).",
            UpdateCheckFailureKind.ReleaseMetadataInvalid => "Update check: invalid release metadata from GitHub.",
            UpdateCheckFailureKind.Cancelled => "Update check was cancelled.",
            UpdateCheckFailureKind.HttpError => "Update check: GitHub returned an HTTP error.",
            _ => string.IsNullOrWhiteSpace(result.ErrorMessage) ? "Update check failed." : result.ErrorMessage!
        };

        string? diag = null;
        if (!string.IsNullOrWhiteSpace(result.DiagnosticDetail))
        {
            diag = $"[Update] {result.FailureKind}: {result.DiagnosticDetail}";
        }

        var detail = !string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? result.ErrorMessage!
            : result.DiagnosticDetail ?? "Unknown error.";

        if (isManualCheck)
        {
            return new UpdateCheckViewState(
                StatusText: status,
                LatestChannelSummary: "Latest release: could not refresh — see status below · GitHub Releases",
                BannerVisibility: Visibility.Visible,
                BannerTitle: "Update check failed",
                BannerDetail: detail,
                DownloadButtonVisibility: Visibility.Collapsed,
                IgnoreButtonVisibility: Visibility.Collapsed,
                ReleaseNotesVisibility: Visibility.Collapsed,
                DiagnosticsHintVisibility: Visibility.Visible,
                PendingInstallerUrl: string.Empty,
                PendingReleaseNotesUrl: string.Empty,
                PendingVersionLabel: string.Empty,
                SafeDiagnosticText: diag,
                InstalledVersionNormalized: installedNorm,
                LatestVersionNormalized: latestNorm);
        }

        return new UpdateCheckViewState(
            StatusText: status,
            LatestChannelSummary: null,
            BannerVisibility: Visibility.Collapsed,
            BannerTitle: null,
            BannerDetail: null,
            DownloadButtonVisibility: Visibility.Collapsed,
            IgnoreButtonVisibility: Visibility.Collapsed,
            ReleaseNotesVisibility: Visibility.Collapsed,
            DiagnosticsHintVisibility: Visibility.Collapsed,
            PendingInstallerUrl: string.Empty,
            PendingReleaseNotesUrl: string.Empty,
            PendingVersionLabel: string.Empty,
            SafeDiagnosticText: diag,
            InstalledVersionNormalized: installedNorm,
            LatestVersionNormalized: latestNorm);
    }

    private static UpdateCheckViewState MapUpdateAvailable(
        UpdateCheckResult result,
        string installedNorm,
        string? latestNorm)
    {
        var safeReleasePage = Uri.TryCreate(result.ReleaseNotesUrl, UriKind.Absolute, out var notesUri) &&
                              string.Equals(notesUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                              string.Equals(notesUri.Host, "github.com", StringComparison.OrdinalIgnoreCase);

        var primaryUrl = result.HasRecommendedZipDownload
            ? result.RecommendedZipDownloadUrl!
            : string.Empty;

        var advancedExe = result.HasActionableInstaller
            ? result.InstallerDownloadUrl ?? string.Empty
            : string.Empty;

        var extras = new List<string>();
        if (!string.IsNullOrWhiteSpace(result.ErrorMessage) &&
            (result.VersionComparisonUncertain || result.RecommendedZipAssetMissing))
        {
            extras.Add(result.ErrorMessage);
        }

        if (result.RecommendedZipAssetMissing && !string.IsNullOrWhiteSpace(result.ReleaseNotesUrl))
        {
            extras.Add("Recommended ZIP asset was not found.");
        }

        if (result.HasRecommendedZipDownload && !string.IsNullOrWhiteSpace(result.RecommendedZipAssetName))
        {
            extras.Add($"Recommended download (ZIP): {result.RecommendedZipAssetName}");
        }

        if (!string.IsNullOrWhiteSpace(advancedExe) && !string.IsNullOrWhiteSpace(result.InstallerAssetName))
        {
            extras.Add($"Advanced — direct installer (EXE, SmartScreen may be stricter): {result.InstallerAssetName}");
        }
        else if (result.HasActionableInstaller && !result.HasRecommendedZipDownload &&
                 !string.IsNullOrWhiteSpace(result.InstallerAssetName))
        {
            extras.Add(
                $"No ZIP on this release — use Advanced to download {result.InstallerAssetName}, or open the GitHub Release page.");
        }

        if (!string.IsNullOrWhiteSpace(result.ChecksumsDownloadUrl))
        {
            extras.Add("Checksums available on the release (CHECKSUMS.sha256).");
        }

        if (!string.IsNullOrWhiteSpace(result.DownloadInstructionsUrl))
        {
            extras.Add("Download instructions available (DOWNLOAD_BETA.txt on the release).");
        }

        if (extras.Count == 0)
        {
            extras.Add("Open the GitHub Release and download the ZIP bundle (recommended) or installer from published assets.");
        }

        var detail = string.Join(Environment.NewLine, extras);
        var showPrimaryDownload = result.HasRecommendedZipDownload;
        var zipForCopy = result.HasRecommendedZipDownload ? result.RecommendedZipDownloadUrl! : string.Empty;
        var showCopyZip = result.HasRecommendedZipDownload ? Visibility.Visible : Visibility.Collapsed;
        var showChecksumCopy = !string.IsNullOrWhiteSpace(result.ChecksumsDownloadUrl) ||
                               result.HasRecommendedZipDownload ||
                               result.HasActionableInstaller
            ? Visibility.Visible
            : Visibility.Collapsed;

        var statusText = result.VersionComparisonUncertain
            ? "A newer release may be available (release version could not be compared to this build)."
            : $"Update available: v{ReleaseVersionParser.NormalizeLabel(result.LatestVersionLabel)}";

        var bannerTitle = result.VersionComparisonUncertain
            ? UpdateNotificationTextBuilder.BuildUncertainHeadline()
            : UpdateNotificationTextBuilder.BuildHeadline(result.LatestVersionLabel);

        return new UpdateCheckViewState(
            StatusText: statusText,
            LatestChannelSummary: ComputeLatestChannelSummary(result),
            BannerVisibility: Visibility.Visible,
            BannerTitle: bannerTitle,
            BannerDetail: detail,
            DownloadButtonVisibility: showPrimaryDownload ? Visibility.Visible : Visibility.Collapsed,
            IgnoreButtonVisibility: Visibility.Visible,
            ReleaseNotesVisibility: safeReleasePage ? Visibility.Visible : Visibility.Collapsed,
            DiagnosticsHintVisibility: Visibility.Collapsed,
            PendingInstallerUrl: primaryUrl,
            PendingReleaseNotesUrl: result.ReleaseNotesUrl,
            PendingVersionLabel: result.LatestVersionLabel,
            SafeDiagnosticText: null,
            InstalledVersionNormalized: installedNorm,
            LatestVersionNormalized: latestNorm,
            PendingAdvancedInstallerUrl: advancedExe,
            AdvancedInstallerDownloadVisibility: string.IsNullOrWhiteSpace(advancedExe)
                ? Visibility.Collapsed
                : Visibility.Visible,
            CopyZipLinkVisibility: showCopyZip,
            PendingZipUrlForClipboard: zipForCopy,
            CopyChecksumInstructionsVisibility: showChecksumCopy,
            ChecksumInstructionsClipboardText: BuildChecksumClipboardText(result));
    }

    private static string BuildChecksumClipboardText(UpdateCheckResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ChecksumsDownloadUrl))
        {
            return DefaultChecksumInstructions + Environment.NewLine + "Checksum file: " + result.ChecksumsDownloadUrl;
        }

        return DefaultChecksumInstructions;
    }

    private static UpdateCheckViewState MapSuccessQuiet(
        UpdateCheckResult result,
        string installedNorm,
        string? latestNorm)
    {
        var latestChannel = ComputeLatestChannelSummary(result);
        string status;
        switch (result.Outcome)
        {
            case UpdateCheckOutcome.NoPublishedRelease:
                status = result.ErrorMessage ?? "No published ForgerEMS release was found yet.";
                latestChannel = "Latest release: — · GitHub Releases";
                break;
            case UpdateCheckOutcome.NoSuitableAssets:
                status = result.ErrorMessage ??
                         "The latest GitHub release has no downloadable assets yet. Open the release page from Diagnostics or try again later.";
                latestChannel = ComputeLatestChannelSummary(result);
                break;
            case UpdateCheckOutcome.IgnoredVersion:
                status = result.ErrorMessage ??
                         UpdateCheckDisplay.FormatIgnoredVersion(ReleaseVersionParser.NormalizeLabel(result.LatestVersionLabel));
                break;
            case UpdateCheckOutcome.AlreadyLatest:
                status = UpdateCheckDisplay.FormatInstalledAlreadyLatest(installedNorm);
                break;
            case UpdateCheckOutcome.InstalledNewerThanLatestPublic:
                status = UpdateCheckDisplay.FormatInstalledNewerThanPublic(
                    installedNorm,
                    ReleaseVersionParser.NormalizeLabel(result.LatestVersionLabel));
                break;
            case UpdateCheckOutcome.None:
                status = !string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? result.ErrorMessage!
                    : "Update check completed with no comparable version.";
                if (string.IsNullOrWhiteSpace(result.LatestVersionLabel))
                {
                    latestChannel = "Latest release: — · GitHub Releases";
                }

                break;
            default:
                status = !string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? result.ErrorMessage!
                    : UpdateCheckDisplay.FormatInstalledAlreadyLatest(installedNorm);
                break;
        }

        return new UpdateCheckViewState(
            StatusText: status,
            LatestChannelSummary: latestChannel,
            BannerVisibility: Visibility.Collapsed,
            BannerTitle: null,
            BannerDetail: null,
            DownloadButtonVisibility: Visibility.Collapsed,
            IgnoreButtonVisibility: Visibility.Collapsed,
            ReleaseNotesVisibility: Visibility.Collapsed,
            DiagnosticsHintVisibility: Visibility.Collapsed,
            PendingInstallerUrl: string.Empty,
            PendingReleaseNotesUrl: string.Empty,
            PendingVersionLabel: string.Empty,
            SafeDiagnosticText: null,
            InstalledVersionNormalized: installedNorm,
            LatestVersionNormalized: latestNorm);
    }

    private static string ComputeLatestChannelSummary(UpdateCheckResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Latest channel summary is only defined for successful checks.");
        }

        if (!string.IsNullOrWhiteSpace(result.LatestVersionLabel))
        {
            var published = result.SelectedReleasePublishedAt is { } u
                ? $" · published {u.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC"
                : string.Empty;
            return
                $"Latest release: v{ReleaseVersionParser.NormalizeLabel(result.LatestVersionLabel)}{published} · GitHub Releases";
        }

        return "Latest release: — · GitHub Releases";
    }
}
