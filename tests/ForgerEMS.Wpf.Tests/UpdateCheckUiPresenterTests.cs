using System.Windows;
using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class UpdateCheckUiPresenterTests
{
    private const string Installed = "1.1.4";

    [Fact]
    public void AlreadyLatest_QuietBanner_AndStatus()
    {
        var result = new UpdateCheckResult
        {
            Succeeded = true,
            Outcome = UpdateCheckOutcome.AlreadyLatest,
            UpdateAvailable = false,
            LatestVersionLabel = "v1.1.4"
        };

        var s = UpdateCheckUiPresenter.Map(result, isManualCheck: true, Installed);

        Assert.Contains("latest", s.StatusText, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1.1.4", s.StatusText);
        Assert.Equal(Visibility.Collapsed, s.BannerVisibility);
        Assert.Null(s.BannerTitle);
        Assert.Equal(Visibility.Collapsed, s.DownloadButtonVisibility);
        Assert.Equal(Visibility.Collapsed, s.IgnoreButtonVisibility);
        Assert.Equal(Visibility.Collapsed, s.ReleaseNotesVisibility);
        Assert.Equal(Visibility.Collapsed, s.DiagnosticsHintVisibility);
        Assert.Equal("Latest release: v1.1.4", s.LatestChannelSummary);
        Assert.Equal("1.1.4", s.InstalledVersionNormalized);
        Assert.Equal("1.1.4", s.LatestVersionNormalized);
    }

    [Fact]
    public void InstalledNewerThanLatestPublic_QuietBanner()
    {
        var result = new UpdateCheckResult
        {
            Succeeded = true,
            Outcome = UpdateCheckOutcome.InstalledNewerThanLatestPublic,
            UpdateAvailable = false,
            LatestVersionLabel = "1.1.4"
        };

        var s = UpdateCheckUiPresenter.Map(result, isManualCheck: true, "1.1.5-beta.1");

        Assert.Contains("newer", s.StatusText, System.StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Visibility.Collapsed, s.BannerVisibility);
        Assert.Equal("1.1.5-beta.1", s.InstalledVersionNormalized);
        Assert.Equal("1.1.4", s.LatestVersionNormalized);
    }

    [Fact]
    public void UpdateAvailable_WithInstaller_ShowsDownload_AndGithubNotes()
    {
        var result = new UpdateCheckResult
        {
            Succeeded = true,
            Outcome = UpdateCheckOutcome.UpdateAvailable,
            UpdateAvailable = true,
            LatestVersionLabel = "v1.2.0",
            ReleaseNotesUrl = "https://github.com/Forger-Digital-Solutions/ForgerEMS/releases/tag/v1.2.0",
            InstallerDownloadUrl = "https://github.com/x/y/releases/download/v1.2.0/ForgerEMS-Setup.exe",
            InstallerAssetName = "ForgerEMS-Setup.exe"
        };

        var s = UpdateCheckUiPresenter.Map(result, isManualCheck: true, Installed);

        Assert.Contains("Update available", s.StatusText, System.StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Visibility.Visible, s.BannerVisibility);
        Assert.NotNull(s.BannerTitle);
        Assert.Contains("ForgerEMS", s.BannerTitle, System.StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Visibility.Visible, s.DownloadButtonVisibility);
        Assert.Equal(Visibility.Visible, s.IgnoreButtonVisibility);
        Assert.Equal(Visibility.Visible, s.ReleaseNotesVisibility);
        Assert.Equal(Visibility.Collapsed, s.DiagnosticsHintVisibility);
        Assert.False(string.IsNullOrEmpty(s.PendingInstallerUrl));
        Assert.Contains("ForgerEMS-Setup.exe", s.BannerDetail, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateAvailable_WithoutInstaller_HidesDownload_ShowsHintInBanner()
    {
        var result = new UpdateCheckResult
        {
            Succeeded = true,
            Outcome = UpdateCheckOutcome.UpdateAvailable,
            UpdateAvailable = true,
            LatestVersionLabel = "v1.2.0",
            ReleaseNotesUrl = "https://github.com/x/y/releases/tag/v1.2.0",
            InstallerDownloadUrl = null,
            InstallerAssetName = null
        };

        var s = UpdateCheckUiPresenter.Map(result, isManualCheck: true, Installed);

        Assert.Equal(Visibility.Collapsed, s.DownloadButtonVisibility);
        Assert.Equal(Visibility.Visible, s.IgnoreButtonVisibility);
        Assert.Contains("No verified .exe", s.BannerDetail, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoPublishedRelease_QuietBanner_AndDashLatestLine()
    {
        var result = new UpdateCheckResult
        {
            Succeeded = true,
            Outcome = UpdateCheckOutcome.NoPublishedRelease,
            UpdateAvailable = false,
            ErrorMessage = "No public GitHub Release is published for this repo yet."
        };

        var s = UpdateCheckUiPresenter.Map(result, isManualCheck: false, Installed);

        Assert.Contains("No public", s.StatusText, System.StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Latest release: —", s.LatestChannelSummary);
        Assert.Equal(Visibility.Collapsed, s.BannerVisibility);
    }

    [Fact]
    public void IgnoredVersion_QuietBanner()
    {
        var result = new UpdateCheckResult
        {
            Succeeded = true,
            Outcome = UpdateCheckOutcome.IgnoredVersion,
            UpdateAvailable = false,
            LatestVersionLabel = "1.9.0",
            ErrorMessage = UpdateCheckDisplay.FormatIgnoredVersion("1.9.0")
        };

        var s = UpdateCheckUiPresenter.Map(result, isManualCheck: true, Installed);

        Assert.Contains("ignored", s.StatusText, System.StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Visibility.Collapsed, s.BannerVisibility);
        Assert.Equal("1.9.0", s.LatestVersionNormalized);
    }

    [Fact]
    public void Timeout_Manual_ShowsBanner_AndDiagnosticsHint_AndSafeDiagnosticText()
    {
        var result = new UpdateCheckResult
        {
            Succeeded = false,
            Outcome = UpdateCheckOutcome.Failed,
            FailureKind = UpdateCheckFailureKind.Timeout,
            ErrorMessage = "Update check timed out. Try again later.",
            DiagnosticDetail = "Overall deadline"
        };

        var s = UpdateCheckUiPresenter.Map(result, isManualCheck: true, Installed);

        Assert.Contains("timed out", s.StatusText, System.StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Visibility.Visible, s.BannerVisibility);
        Assert.Equal("Update check failed", s.BannerTitle);
        Assert.Equal(Visibility.Visible, s.DiagnosticsHintVisibility);
        Assert.NotNull(s.SafeDiagnosticText);
        Assert.Contains("Timeout", s.SafeDiagnosticText, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Overall deadline", s.SafeDiagnosticText);
        Assert.Null(s.LatestChannelSummary);
    }

    [Fact]
    public void NetworkFailure_Background_StaysQuiet_NoBannerMutationFields()
    {
        var result = new UpdateCheckResult
        {
            Succeeded = false,
            Outcome = UpdateCheckOutcome.Failed,
            FailureKind = UpdateCheckFailureKind.Network,
            ErrorMessage = "offline",
            DiagnosticDetail = "DNS"
        };

        var s = UpdateCheckUiPresenter.Map(result, isManualCheck: false, Installed);

        Assert.Equal(Visibility.Collapsed, s.BannerVisibility);
        Assert.Null(s.BannerTitle);
        Assert.Null(s.BannerDetail);
        Assert.Equal(Visibility.Collapsed, s.DiagnosticsHintVisibility);
        Assert.Null(s.LatestChannelSummary);
        Assert.NotNull(s.SafeDiagnosticText);
    }

    [Fact]
    public void ReleaseEndpointNotFound_StatusMentionsEndpoint()
    {
        var result = new UpdateCheckResult
        {
            Succeeded = false,
            Outcome = UpdateCheckOutcome.Failed,
            FailureKind = UpdateCheckFailureKind.ReleaseEndpointNotFound,
            DiagnosticDetail = "HTTP 404"
        };

        var s = UpdateCheckUiPresenter.Map(result, isManualCheck: true, Installed);

        Assert.Contains("endpoint", s.StatusText, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidMetadata_StatusMentionsMetadata()
    {
        var result = new UpdateCheckResult
        {
            Succeeded = false,
            Outcome = UpdateCheckOutcome.Failed,
            FailureKind = UpdateCheckFailureKind.ReleaseMetadataInvalid,
            DiagnosticDetail = "missing tag"
        };

        var s = UpdateCheckUiPresenter.Map(result, isManualCheck: true, Installed);

        Assert.Contains("metadata", s.StatusText, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManualFailure_ShowsBanner_AndHint()
    {
        var result = new UpdateCheckResult
        {
            Succeeded = false,
            Outcome = UpdateCheckOutcome.Failed,
            FailureKind = UpdateCheckFailureKind.Unknown,
            ErrorMessage = "Something broke."
        };

        var s = UpdateCheckUiPresenter.Map(result, isManualCheck: true, Installed);

        Assert.Equal(Visibility.Visible, s.BannerVisibility);
        Assert.Equal("Update check failed", s.BannerTitle);
        Assert.Equal(Visibility.Visible, s.DiagnosticsHintVisibility);
    }

    [Fact]
    public void BackgroundFailure_StaysQuiet()
    {
        var result = new UpdateCheckResult
        {
            Succeeded = false,
            Outcome = UpdateCheckOutcome.Failed,
            FailureKind = UpdateCheckFailureKind.Network,
            ErrorMessage = "offline"
        };

        var s = UpdateCheckUiPresenter.Map(result, isManualCheck: false, Installed);

        Assert.Equal(Visibility.Collapsed, s.BannerVisibility);
        Assert.Null(s.BannerTitle);
        Assert.Equal(Visibility.Collapsed, s.DiagnosticsHintVisibility);
    }
}
