using VentoyToolkitSetup.Wpf.Models;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class ToolkitHealthItemViewTests
{
    [Fact]
    public void StatusDisplayUi_ManualRequired_IsNotGenericMissing()
    {
        var v = new ToolkitHealthItemView { Status = "MANUAL_REQUIRED", Tool = "x", Category = "y" };
        Assert.Equal("Manual shortcut missing", v.StatusDisplayUi);
        Assert.DoesNotContain("Managed missing", v.StatusDisplayUi, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StatusDisplayUi_ManagedMissing_IsReadable()
    {
        var v = new ToolkitHealthItemView { Status = "MISSING_REQUIRED" };
        Assert.Equal("Missing required file", v.StatusDisplayUi);
    }

    [Fact]
    public void StatusDisplayUi_VerificationPending_IsNotMissing()
    {
        var v = new ToolkitHealthItemView { Status = "VERIFICATION_PENDING" };
        Assert.Equal("Present / verification pending", v.StatusDisplayUi);
        Assert.DoesNotContain("missing", v.StatusDisplayUi, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StatusDisplayUi_Installed_UsesReadableLabel()
    {
        var v = new ToolkitHealthItemView { Status = "INSTALLED" };
        Assert.Equal("Installed", v.StatusDisplayUi);
    }

    [Fact]
    public void CoveredByManagedShortcut_IsNotManualBlocker()
    {
        var v = new ToolkitHealthItemView
        {
            Status = "COVERED_BY_MANAGED",
            Type = "manualDownload",
            ExpectedPath = @"ISO\Linux\DOWNLOAD - SystemRescue.url",
            Recommendation = "Shortcut suppressed because managed item is installed. No action needed."
        };

        Assert.Equal("Covered by managed download", v.StatusDisplayUi);
        Assert.Equal("Covered", v.VerificationDisplay);
        Assert.Equal("No action needed", v.ActionDisplay);
        Assert.Contains("Covered by managed download", v.LocationDisplay, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocationDisplay_UsesCompactRelativePath_NotAbsolutePath()
    {
        var v = new ToolkitHealthItemView
        {
            Status = "INSTALLED",
            Exists = true,
            SizeBytes = 474_112L * 1024L,
            ExpectedPath = @"ISO\Tools\clonezilla-live-3.3.1-35-amd64.iso",
            ResolvedExpectedPath = @"D:\ISO\Tools\clonezilla-live-3.3.1-35-amd64.iso",
            MatchedPath = @"D:\ISO\Tools\clonezilla-live-3.3.1-35-amd64.iso"
        };

        Assert.Contains(@"ISO\Tools\clonezilla-live-3.3.1-35-amd64.iso", v.LocationDisplay);
        Assert.DoesNotContain(@"D:\", v.LocationDisplay, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Present", v.LocationDisplay);
    }

    [Fact]
    public void ActionDisplay_AndRecommendationShort_AreCompact()
    {
        var v = new ToolkitHealthItemView
        {
            Status = "MISSING_REQUIRED",
            Recommendation = "Run Setup USB Toolkit to download and place required files automatically."
        };

        Assert.Equal("Run Setup USB Toolkit", v.ActionDisplay);
        Assert.NotEmpty(v.RecommendationShort);
    }

    [Fact]
    public void DetailText_IncludesCatalogMetadataFields()
    {
        var v = new ToolkitHealthItemView
        {
            Tool = "Clonezilla",
            Category = "Backup / Imaging",
            Status = "INSTALLED",
            Purpose = "Disk imaging and cloning",
            OfficialUrl = "https://clonezilla.org",
            LicenseRedistributionNote = "GPL; include notices when redistributing.",
            DistributionModel = "Managed download",
            DownloadStatus = "Downloaded",
            ChecksumStatus = "Verified",
            BetaSafetyRating = "Ready"
        };

        Assert.Contains("Purpose: Disk imaging and cloning", v.DetailText, StringComparison.Ordinal);
        Assert.Contains("Official URL: https://clonezilla.org", v.DetailText, StringComparison.Ordinal);
        Assert.Contains("License / redistribution: GPL", v.DetailText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Distribution model: Managed download", v.DetailText, StringComparison.Ordinal);
        Assert.Contains("Download status: Downloaded", v.DetailText, StringComparison.Ordinal);
        Assert.Contains("Checksum status: Verified", v.DetailText, StringComparison.Ordinal);
        Assert.Contains("Beta safety rating: Ready", v.DetailText, StringComparison.Ordinal);
    }
}
