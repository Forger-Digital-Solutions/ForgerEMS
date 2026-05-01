using System;
using System.Collections.Generic;
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
    string PendingInstallerUrl,
    string PendingReleaseNotesUrl,
    string PendingVersionLabel,
    /// <summary>Safe, non-secret text for Diagnostics logs; null when nothing extra should be logged.</summary>
    string? SafeDiagnosticText,
    string InstalledVersionNormalized,
    string? LatestVersionNormalized);

public static class UpdateCheckUiPresenter
{
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

        if (result.Outcome == UpdateCheckOutcome.UpdateAvailable && result.UpdateAvailable)
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
            UpdateCheckFailureKind.Network => "Update check: offline or network issue.",
            UpdateCheckFailureKind.Timeout => "Update check timed out. Try again later.",
            UpdateCheckFailureKind.ReleaseEndpointNotFound => "Update check: GitHub release endpoint not found.",
            UpdateCheckFailureKind.AccessDeniedOrRateLimited => "Update check: access denied or rate limited.",
            UpdateCheckFailureKind.ReleaseMetadataInvalid => "Update check: invalid release metadata.",
            UpdateCheckFailureKind.Cancelled => "Update check was cancelled.",
            UpdateCheckFailureKind.HttpError => "Update check: GitHub returned an error.",
            _ => string.IsNullOrWhiteSpace(result.ErrorMessage) ? "Update check failed." : result.ErrorMessage!
        };

        string? diag = null;
        if (!string.IsNullOrWhiteSpace(result.DiagnosticDetail))
        {
            diag = $"[Update] {result.FailureKind}: {result.DiagnosticDetail}";
        }

        if (isManualCheck)
        {
            return new UpdateCheckViewState(
                StatusText: status,
                LatestChannelSummary: null,
                BannerVisibility: Visibility.Visible,
                BannerTitle: "Update check failed",
                BannerDetail: result.ErrorMessage ?? result.DiagnosticDetail ?? "Unknown error.",
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

        var extras = new List<string>();
        if (!string.IsNullOrWhiteSpace(result.InstallerAssetName))
        {
            extras.Add($"Installer asset: {result.InstallerAssetName}");
        }

        if (!result.HasActionableInstaller)
        {
            extras.Add("No verified .exe installer URL on this release — open release notes or retry after publishing.");
        }

        var detail = extras.Count == 0 ? string.Empty : string.Join(Environment.NewLine, extras);

        return new UpdateCheckViewState(
            StatusText: $"Update available: v{ReleaseVersionParser.NormalizeLabel(result.LatestVersionLabel)}",
            LatestChannelSummary: ComputeLatestChannelSummary(result),
            BannerVisibility: Visibility.Visible,
            BannerTitle: UpdateNotificationTextBuilder.BuildHeadline(result.LatestVersionLabel),
            BannerDetail: detail,
            DownloadButtonVisibility: result.HasActionableInstaller ? Visibility.Visible : Visibility.Collapsed,
            IgnoreButtonVisibility: Visibility.Visible,
            ReleaseNotesVisibility: safeReleasePage ? Visibility.Visible : Visibility.Collapsed,
            DiagnosticsHintVisibility: Visibility.Collapsed,
            PendingInstallerUrl: result.InstallerDownloadUrl ?? string.Empty,
            PendingReleaseNotesUrl: result.ReleaseNotesUrl,
            PendingVersionLabel: result.LatestVersionLabel,
            SafeDiagnosticText: null,
            InstalledVersionNormalized: installedNorm,
            LatestVersionNormalized: latestNorm);
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
                status = result.ErrorMessage ?? "No public release found.";
                latestChannel = "Latest release: —";
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
                    latestChannel = "Latest release: —";
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
            return $"Latest release: v{ReleaseVersionParser.NormalizeLabel(result.LatestVersionLabel)}";
        }

        return "Latest release: —";
    }
}
