using System;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services.Intelligence;

namespace ForgerEMS.Wpf.Tests;

public sealed class ToolkitReadinessScorerTests
{
    [Fact]
    public void Evaluate_MissingReport_ReturnsUnknownLimitedData()
    {
        var result = ToolkitReadinessScorer.Evaluate(
            items: [],
            selectedTarget: null,
            ventoyStatusText: "Unknown",
            toolkitReportAvailable: false,
            toolkitLogAvailable: false,
            missingRequiredCount: 0,
            verificationFailedCount: 0,
            updatesAvailableCount: 0,
            verificationPendingCount: 0);

        Assert.Equal(ToolkitReadinessLabel.UnknownLimitedData, result.Label);
        Assert.Equal(0, result.Score);
        Assert.Contains("missing", string.Join(' ', result.Blockers), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_HealthyToolkit_ReturnsReady()
    {
        var target = new UsbTargetInfo
        {
            IsLikelyUsb = true,
            IsRemovableMedia = true,
            IsSelectable = true,
            RootPath = @"E:\",
            TotalBytes = 64L * 1024 * 1024 * 1024,
            FreeBytes = 20L * 1024 * 1024 * 1024
        };
        var items = new[]
        {
            new ToolkitHealthItemView { Status = "INSTALLED", Type = "MANAGED", DownloadStatus = "Downloaded", ChecksumStatus = "Verified" },
            new ToolkitHealthItemView { Status = "INSTALLED", Type = "MANAGED", DownloadStatus = "Downloaded", ChecksumStatus = "Verified" }
        };

        var result = ToolkitReadinessScorer.Evaluate(
            items,
            target,
            ventoyStatusText: "Ready",
            toolkitReportAvailable: true,
            toolkitLogAvailable: true,
            missingRequiredCount: 0,
            verificationFailedCount: 0,
            updatesAvailableCount: 0,
            verificationPendingCount: 0);

        Assert.Equal(ToolkitReadinessLabel.Ready, result.Label);
        Assert.True(result.Score >= 85);
        Assert.DoesNotContain(result.Blockers, x => x.Contains("missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_UnsafeAndMissingToolkit_ReturnsNotReady()
    {
        var target = new UsbTargetInfo
        {
            IsLikelyUsb = false,
            IsSelectable = false,
            SelectionWarning = "Blocked",
            RootPath = @"C:\",
            TotalBytes = 512L * 1024 * 1024 * 1024,
            FreeBytes = 1L * 1024 * 1024 * 1024
        };
        var items = new[]
        {
            new ToolkitHealthItemView { Status = "MISSING_REQUIRED", Type = "MANAGED", DownloadStatus = "Unknown", ChecksumStatus = "Unknown" },
            new ToolkitHealthItemView { Status = "HASH_FAILED", Type = "MANAGED", DownloadStatus = "Downloaded", ChecksumStatus = "Checksum mismatch" }
        };

        var result = ToolkitReadinessScorer.Evaluate(
            items,
            target,
            ventoyStatusText: "Unavailable",
            toolkitReportAvailable: true,
            toolkitLogAvailable: false,
            missingRequiredCount: 3,
            verificationFailedCount: 2,
            updatesAvailableCount: 1,
            verificationPendingCount: 1);

        Assert.Equal(ToolkitReadinessLabel.NotReady, result.Label);
        Assert.True(result.Score < 45);
        Assert.Contains(result.Blockers, b => b.Contains("missing", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Blockers, b => b.Contains("checksum", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_ReportOnlyContext_HealthyToolkit_DoesNotForceUnknownLabel()
    {
        var items = new[]
        {
            new ToolkitHealthItemView { Status = "INSTALLED", Type = "MANAGED", DownloadStatus = "Downloaded", ChecksumStatus = "Verified" },
            new ToolkitHealthItemView { Status = "INSTALLED", Type = "MANAGED", DownloadStatus = "Downloaded", ChecksumStatus = "Verified" }
        };

        var result = ToolkitReadinessScorer.Evaluate(
            items,
            selectedTarget: null,
            ventoyStatusText: string.Empty,
            toolkitReportAvailable: true,
            toolkitLogAvailable: true,
            missingRequiredCount: 0,
            verificationFailedCount: 0,
            updatesAvailableCount: 0,
            verificationPendingCount: 0,
            omitLiveUsbVentoyContext: true);

        Assert.Equal(ToolkitReadinessLabel.Ready, result.Label);
        Assert.True(result.Score >= 85);
        Assert.DoesNotContain(
            result.Blockers,
            x => x.Contains("USB target", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            result.Blockers,
            x => x.Contains("Ventoy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_LinkVerificationNotRun_DoesNotPenalizeReadiness()
    {
        var items = Array.Empty<ToolkitHealthItemView>();

        var withoutVerifier = ToolkitReadinessScorer.Evaluate(
            items,
            selectedTarget: null,
            ventoyStatusText: string.Empty,
            toolkitReportAvailable: true,
            toolkitLogAvailable: true,
            missingRequiredCount: 0,
            verificationFailedCount: 0,
            updatesAvailableCount: 0,
            verificationPendingCount: 0,
            omitLiveUsbVentoyContext: true);

        var withVerifierPending = ToolkitReadinessScorer.Evaluate(
            items,
            selectedTarget: null,
            ventoyStatusText: string.Empty,
            toolkitReportAvailable: true,
            toolkitLogAvailable: true,
            missingRequiredCount: 0,
            verificationFailedCount: 0,
            updatesAvailableCount: 0,
            verificationPendingCount: 0,
            omitLiveUsbVentoyContext: true,
            linkVerification: new ToolkitLinkVerificationSummaryForReadiness { HasRun = false });

        Assert.Equal(withoutVerifier.Score, withVerifierPending.Score);
        Assert.Equal(withoutVerifier.Blockers.Count, withVerifierPending.Blockers.Count);
    }

    [Fact]
    public void Evaluate_LinkVerificationWhenHasRun_IgnoresLegacyBrokenUrlHeuristic()
    {
        var items = new[]
        {
            new ToolkitHealthItemView
            {
                Status = "INSTALLED",
                Type = "MANAGED",
                DownloadStatus = "Downloaded",
                ChecksumStatus = "Verified",
                OfficialUrl = "https://vendor.example/broken-marker-placeholder"
            }
        };

        var heuristicOnly = ToolkitReadinessScorer.Evaluate(
            items,
            selectedTarget: null,
            ventoyStatusText: string.Empty,
            toolkitReportAvailable: true,
            toolkitLogAvailable: true,
            missingRequiredCount: 0,
            verificationFailedCount: 0,
            updatesAvailableCount: 0,
            verificationPendingCount: 0,
            omitLiveUsbVentoyContext: true);

        var withVerifier = ToolkitReadinessScorer.Evaluate(
            items,
            selectedTarget: null,
            ventoyStatusText: string.Empty,
            toolkitReportAvailable: true,
            toolkitLogAvailable: true,
            missingRequiredCount: 0,
            verificationFailedCount: 0,
            updatesAvailableCount: 0,
            verificationPendingCount: 0,
            omitLiveUsbVentoyContext: true,
            linkVerification: new ToolkitLinkVerificationSummaryForReadiness
            {
                HasRun = true,
                UrlsChecked = 1,
                BrokenCount = 0,
                WarningCount = 0,
                VerifiedMetadataCount = 1,
                ReachableCount = 0,
                UnknownOfflineCount = 0
            });

        Assert.Contains(heuristicOnly.Blockers, b => b.Contains("marked broken", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(withVerifier.Blockers, b => b.Contains("marked broken", StringComparison.OrdinalIgnoreCase));
        Assert.True(withVerifier.Score > heuristicOnly.Score);
    }

    [Fact]
    public void Evaluate_AllUrlsOfflineMetadata_IsHonestWithoutStrongScorePenalty()
    {
        var items = Array.Empty<ToolkitHealthItemView>();

        var baseline = ToolkitReadinessScorer.Evaluate(
            items,
            selectedTarget: null,
            ventoyStatusText: string.Empty,
            toolkitReportAvailable: true,
            toolkitLogAvailable: true,
            missingRequiredCount: 0,
            verificationFailedCount: 0,
            updatesAvailableCount: 0,
            verificationPendingCount: 0,
            omitLiveUsbVentoyContext: true);

        var allOffline = ToolkitReadinessScorer.Evaluate(
            items,
            selectedTarget: null,
            ventoyStatusText: string.Empty,
            toolkitReportAvailable: true,
            toolkitLogAvailable: true,
            missingRequiredCount: 0,
            verificationFailedCount: 0,
            updatesAvailableCount: 0,
            verificationPendingCount: 0,
            omitLiveUsbVentoyContext: true,
            linkVerification: new ToolkitLinkVerificationSummaryForReadiness
            {
                HasRun = true,
                UrlsChecked = 3,
                UnknownOfflineCount = 3,
                BrokenCount = 0,
                WarningCount = 0,
                VerifiedMetadataCount = 0,
                ReachableCount = 0
            });

        Assert.Equal(baseline.Score, allOffline.Score);
        Assert.Contains("offline or timed out", allOffline.ConfidenceNote, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_LinkBrokenHitsScoreViaVerifier()
    {
        var items = Array.Empty<ToolkitHealthItemView>();
        var ok = ToolkitReadinessScorer.Evaluate(
            items,
            selectedTarget: null,
            ventoyStatusText: string.Empty,
            toolkitReportAvailable: true,
            toolkitLogAvailable: true,
            missingRequiredCount: 0,
            verificationFailedCount: 0,
            updatesAvailableCount: 0,
            verificationPendingCount: 0,
            omitLiveUsbVentoyContext: true,
            linkVerification: new ToolkitLinkVerificationSummaryForReadiness
            {
                HasRun = true,
                UrlsChecked = 2,
                BrokenCount = 1,
                WarningCount = 0,
                VerifiedMetadataCount = 1,
                ReachableCount = 0,
                UnknownOfflineCount = 0
            });

        Assert.Contains(ok.Blockers, b => b.Contains("failed HTTP metadata", StringComparison.OrdinalIgnoreCase));
        Assert.True(ok.Score < 100);
    }

    [Fact]
    public void Evaluate_LinkWarningsReduceConfidenceMoreThanScoreVsBroken()
    {
        var items = Array.Empty<ToolkitHealthItemView>();
        var warningsOnly = ToolkitReadinessScorer.Evaluate(
            items,
            selectedTarget: null,
            ventoyStatusText: string.Empty,
            toolkitReportAvailable: true,
            toolkitLogAvailable: true,
            missingRequiredCount: 0,
            verificationFailedCount: 0,
            updatesAvailableCount: 0,
            verificationPendingCount: 0,
            omitLiveUsbVentoyContext: true,
            linkVerification: new ToolkitLinkVerificationSummaryForReadiness
            {
                HasRun = true,
                UrlsChecked = 3,
                BrokenCount = 0,
                WarningCount = 2,
                VerifiedMetadataCount = 3,
                ReachableCount = 0,
                UnknownOfflineCount = 0
            });

        var brokenOnly = ToolkitReadinessScorer.Evaluate(
            items,
            selectedTarget: null,
            ventoyStatusText: string.Empty,
            toolkitReportAvailable: true,
            toolkitLogAvailable: true,
            missingRequiredCount: 0,
            verificationFailedCount: 0,
            updatesAvailableCount: 0,
            verificationPendingCount: 0,
            omitLiveUsbVentoyContext: true,
            linkVerification: new ToolkitLinkVerificationSummaryForReadiness
            {
                HasRun = true,
                UrlsChecked = 2,
                BrokenCount = 1,
                WarningCount = 0,
                VerifiedMetadataCount = 1,
                ReachableCount = 0,
                UnknownOfflineCount = 0
            });

        Assert.True(warningsOnly.Score > brokenOnly.Score);
        Assert.Contains("link warning", warningsOnly.ConfidenceNote, StringComparison.OrdinalIgnoreCase);
    }
}
