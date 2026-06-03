using System;
using VentoyToolkitSetup.Wpf.Models;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Audit Part B: verify the WPF model now surfaces backend-provided
/// verificationMode honestly. Cached entries must never be labeled "fresh".
/// </summary>
public sealed class ToolkitHealthItemVerificationModeTests
{
    [Fact]
    public void CachedInstalled_BadgeReadsCachedMatch()
    {
        var item = new ToolkitHealthItemView
        {
            Status = "INSTALLED",
            VerificationMode = "cached"
        };

        Assert.Equal("Cached match", item.VerificationModeBadge);
        Assert.Contains("unchanged since previous verified scan",
            item.VerificationModeTooltip, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fresh", item.VerificationModeTooltip, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FreshInstalled_BadgeReadsFreshMatch()
    {
        var item = new ToolkitHealthItemView
        {
            Status = "INSTALLED",
            VerificationMode = "fresh"
        };

        Assert.Equal("Fresh match", item.VerificationModeBadge);
        Assert.Contains("re-hashed this run", item.VerificationModeTooltip, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstalledWithEmptyMode_FallsBackToVerified()
    {
        // Legacy reports (no verificationMode field) must keep producing the
        // existing one-word "Verified" badge so older snapshots still render
        // sensibly.
        var item = new ToolkitHealthItemView
        {
            Status = "INSTALLED",
            VerificationMode = string.Empty
        };

        Assert.Equal("Verified", item.VerificationModeBadge);
        Assert.Equal("Verified.", item.VerificationModeTooltip);
    }

    [Fact]
    public void NonInstalledStatuses_HaveHonestBadges()
    {
        Assert.Equal("Manual shortcut",
            new ToolkitHealthItemView { Status = "MANUAL_REQUIRED" }.VerificationModeBadge);
        Assert.Equal("Covered shortcut",
            new ToolkitHealthItemView { Status = "COVERED_BY_MANAGED" }.VerificationModeBadge);
        Assert.Equal("Pending",
            new ToolkitHealthItemView { Status = "VERIFICATION_PENDING" }.VerificationModeBadge);
        Assert.Equal("Checksum mismatch",
            new ToolkitHealthItemView { Status = "HASH_FAILED" }.VerificationModeBadge);
        Assert.Equal("Not present",
            new ToolkitHealthItemView { Status = "MISSING_REQUIRED" }.VerificationModeBadge);
    }

    [Fact]
    public void ExistingVerificationDisplay_StaysSingleWord()
    {
        // The grid uses VerificationDisplay; keeping the "Verified" wording
        // there is part of the low-risk constraint — cached vs fresh lives in
        // the badge/tooltip/detail panel, not the table column.
        var cached = new ToolkitHealthItemView { Status = "INSTALLED", VerificationMode = "cached" };
        Assert.Equal("Verified", cached.VerificationDisplay);
    }

    [Fact]
    public void DetailText_IncludesVerificationModeLine()
    {
        var cached = new ToolkitHealthItemView
        {
            Tool = "Cached Tool",
            Status = "INSTALLED",
            VerificationMode = "cached"
        };

        Assert.Contains("Verification mode: Cached match", cached.DetailText, StringComparison.Ordinal);
        Assert.Contains("unchanged since previous verified scan",
            cached.DetailText, StringComparison.OrdinalIgnoreCase);

        var fresh = new ToolkitHealthItemView
        {
            Tool = "Fresh Tool",
            Status = "INSTALLED",
            VerificationMode = "fresh"
        };

        Assert.Contains("Verification mode: Fresh match", fresh.DetailText, StringComparison.Ordinal);
    }
}
