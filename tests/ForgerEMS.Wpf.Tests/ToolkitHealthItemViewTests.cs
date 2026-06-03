using System;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
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

    [Theory]
    [InlineData(ManifestPromotionPolicy.ManagedDownload, "Managed Download", "Downloads and verifies checksum when available.")]
    [InlineData(ManifestPromotionPolicy.OfficialDownloadPage, "Official Download Page", "Opens the vendor/project page. Technician verifies/downloads manually.")]
    [InlineData(ManifestPromotionPolicy.ManualMediaRequired, "Manual Media Required", "User must supply legally obtained media.")]
    [InlineData(ManifestPromotionPolicy.ReviewFirst, "Review First", "Official/community page; verify licensing/provenance before use.")]
    [InlineData(ManifestPromotionPolicy.VendorPortal, "Vendor Portal", "Model-specific/OEM workflow. Use serial/model lookup.")]
    [InlineData(ManifestPromotionPolicy.OemSpecific, "Vendor Portal", "Model-specific/OEM workflow. Use serial/model lookup.")]
    [InlineData(ManifestPromotionPolicy.LicenseRestricted, "License / EULA Required", "Manual vendor flow required before download/use.")]
    [InlineData(ManifestPromotionPolicy.DynamicMirrorOnly, "Official Mirror Page", "Dynamic mirror/checksum flow prevents safe managed download.")]
    [InlineData(ManifestPromotionPolicy.FirmwareBlocked, "Firmware / BIOS Portal", "Firmware downloads are intentionally manual.")]
    [InlineData(ManifestPromotionPolicy.CommunityToolkit, "Community Toolkit Page", "Review provenance and licensing before client use.")]
    [InlineData(ManifestPromotionPolicy.Unsupported, "Unsupported / Reference Only", "Unsupported / reference-only entry.")]
    [InlineData(ManifestPromotionPolicy.InfoOnly, "Reference Info", "Reference information only.")]
    public void DownloadMode_MapsToTechnicianActionLabels(string mode, string expectedLabel, string expectedHelper)
    {
        var v = new ToolkitHealthItemView
        {
            Status = "MISSING_REQUIRED",
            Type = "manualDownload",
            DownloadMode = mode
        };

        Assert.Equal(expectedLabel, v.DownloadActionLabel);
        Assert.Equal(expectedHelper, v.ActionHelperText);
        Assert.Equal(expectedLabel, v.ActionDisplay);
    }

    [Theory]
    [InlineData("file", false, "", "official", "", "", "", "", ManifestPromotionPolicy.ManagedDownload)]
    [InlineData("page", true, "", "official", "Manual ISO required", "Unsupported by vendor", "", @"ISO\\Windows-Legacy\\MANUAL ISO REQUIRED.url", ManifestPromotionPolicy.ManualMediaRequired)]
    [InlineData("page", true, "driver-shortcut", "official", "model-specific driver lookup", "", "", @"Drivers\\Vendor\\DOWNLOAD - Dell.url", ManifestPromotionPolicy.OemSpecific)]
    [InlineData("page", false, "", "official", "Review first: verify provenance", "", "", @"Tools\\Portable\\DOWNLOAD.url", ManifestPromotionPolicy.ReviewFirst)]
    [InlineData("page", false, "", "official", "Vendor page", "", "", @"Tools\\Portable\\DOWNLOAD.url", ManifestPromotionPolicy.OfficialDownloadPage)]
    [InlineData("page", false, "", "community", "Community toolkit", "", "", @"MediCat.USB\\DOWNLOAD.url", ManifestPromotionPolicy.CommunityToolkit)]
    [InlineData("page", false, "", "", "", "", "", @"Docs\\REFERENCE.url", ManifestPromotionPolicy.InfoOnly)]
    public void DownloadMode_InferenceKeepsLegacyManifestCompatibility(
        string type,
        bool manualOnly,
        string kind,
        string sourceTrust,
        string notes,
        string legacyWarning,
        string licenseNote,
        string dest,
        string expected)
    {
        var actual = ManifestPromotionPolicy.InferDownloadMode(
            explicitDownloadMode: "",
            type,
            manualOnly,
            kind,
            sourceTrust,
            notes,
            legacyWarning,
            licenseNote,
            dest,
            family: "");

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(ManifestPromotionPolicy.OfficialDownloadPage)]
    [InlineData(ManifestPromotionPolicy.ManualMediaRequired)]
    [InlineData(ManifestPromotionPolicy.ReviewFirst)]
    public void DownloadMode_NonInfoModesDoNotUseGenericInfoLabel(string mode)
    {
        Assert.NotEqual("Info", ManifestPromotionPolicy.GetPrimaryActionLabel(mode));
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

    [Fact]
    public void DetailText_OmitsCatalogMetadataBlock_WhenNoFieldsSet()
    {
        var v = new ToolkitHealthItemView
        {
            Tool = "Legacy entry",
            Category = "General",
            Status = "INSTALLED"
        };

        Assert.DoesNotContain("Family:", v.DetailText, StringComparison.Ordinal);
        Assert.DoesNotContain("OS category:", v.DetailText, StringComparison.Ordinal);
        Assert.DoesNotContain("Manual ISO Required: yes", v.DetailText, StringComparison.Ordinal);
        Assert.DoesNotContain("Legacy warning:", v.DetailText, StringComparison.Ordinal);
    }

    [Fact]
    public void DetailText_IncludesCatalogMetadataBlock_WhenFieldsSet()
    {
        var v = new ToolkitHealthItemView
        {
            Tool = "Windows 7 Lifecycle Info",
            Category = "Windows-Legacy",
            Status = "MANUAL_REQUIRED",
            Kind = "os",
            Family = "Windows",
            OsCategory = "Legacy",
            Architecture = "amd64, x86",
            BootMode = "uefi, bios",
            RecommendedUse = "Legacy / lab use only.",
            TechnicianNotes = "ESU ended 2023-01-10.",
            LicenseNote = "Microsoft EULA. Discontinued / unsupported by vendor.",
            ManualOnly = true,
            LegacyWarning = "Unsupported by vendor. Lab use only.",
            SecureBootNote = "Disable Secure Boot to install.",
            SourceTrust = "official"
        };

        Assert.Contains("Family: Windows", v.DetailText, StringComparison.Ordinal);
        Assert.Contains("OS category: Legacy", v.DetailText, StringComparison.Ordinal);
        Assert.Contains("Architecture: amd64, x86", v.DetailText, StringComparison.Ordinal);
        Assert.Contains("Boot mode: uefi, bios", v.DetailText, StringComparison.Ordinal);
        Assert.Contains("Recommended use: Legacy / lab use only.", v.DetailText, StringComparison.Ordinal);
        Assert.Contains("License note: Microsoft EULA", v.DetailText, StringComparison.Ordinal);
        Assert.Contains("Manual ISO Required: yes", v.DetailText, StringComparison.Ordinal);
        Assert.Contains("Legacy warning: Unsupported by vendor", v.DetailText, StringComparison.Ordinal);
        Assert.Contains("Secure Boot: Disable Secure Boot to install.", v.DetailText, StringComparison.Ordinal);
        Assert.Contains("Source trust: official", v.DetailText, StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogBadgesDisplay_JoinsFamilyAndCategoryAndArchAndBootMode()
    {
        var v = new ToolkitHealthItemView
        {
            Family = "Linux",
            OsCategory = "Server",
            Architecture = "amd64, arm64",
            BootMode = "uefi, bios, secure-boot"
        };

        Assert.Equal("Linux · Server · amd64, arm64 · uefi, bios, secure-boot", v.CatalogBadgesDisplay);
    }

    [Fact]
    public void CatalogBadgesDisplay_IsEmpty_WhenNoMetadata()
    {
        var v = new ToolkitHealthItemView();
        Assert.Equal(string.Empty, v.CatalogBadgesDisplay);
    }

    [Fact]
    public void CatalogStatusTag_LegacyWarning_BeatsEverything()
    {
        var v = new ToolkitHealthItemView
        {
            LegacyWarning = "Unsupported by vendor.",
            LicenseNote = "Paid - vendor licence required.",
            ManualOnly = true,
            SourceTrust = "official"
        };

        Assert.Equal("Legacy / Lab Only", v.CatalogStatusTag);
    }

    [Fact]
    public void CatalogStatusTag_PaidLicense_DetectedFromLicenseNote()
    {
        var v = new ToolkitHealthItemView
        {
            LicenseNote = "Paid - vendor licence required (trial available).",
            ManualOnly = true,
            SourceTrust = "official"
        };

        Assert.Equal("Paid - vendor licence", v.CatalogStatusTag);
    }

    [Fact]
    public void CatalogStatusTag_ManualOnly_WhenNoLicenseOrLegacy()
    {
        var v = new ToolkitHealthItemView
        {
            ManualOnly = true,
            SourceTrust = "official"
        };

        Assert.Equal("Manual ISO Required", v.CatalogStatusTag);
    }

    [Fact]
    public void CatalogStatusTag_CommunitySource_Detected()
    {
        var v = new ToolkitHealthItemView
        {
            SourceTrust = "community"
        };

        Assert.Equal("Community source", v.CatalogStatusTag);
    }

    [Fact]
    public void CatalogStatusTag_OfficialSource_Detected()
    {
        var v = new ToolkitHealthItemView
        {
            SourceTrust = "official"
        };

        Assert.Equal("Official source", v.CatalogStatusTag);
    }

    [Fact]
    public void CatalogStatusTag_ReturnsNull_WhenNoMetadata()
    {
        var v = new ToolkitHealthItemView();
        Assert.Null(v.CatalogStatusTag);
    }

    [Fact]
    public void SafetyBadgesDisplay_SurfacesManagedFreshnessAndChecksumState()
    {
        var v = new ToolkitHealthItemView
        {
            Type = "managedAutoDownload",
            SourceTrust = "official",
            FreshnessStatus = "MinorUpdateAvailable",
            ChecksumVerificationMode = "github-asset-digest"
        };

        Assert.Contains("Managed download", v.SafetyBadgesDisplay, StringComparison.Ordinal);
        Assert.Contains("Official source", v.SafetyBadgesDisplay, StringComparison.Ordinal);
        Assert.Contains("Checksum verified", v.SafetyBadgesDisplay, StringComparison.Ordinal);
        Assert.Contains("Update available", v.SafetyBadgesDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public void SafetyBadgesDisplay_SurfacesManualLimitedAndPaidStates()
    {
        var v = new ToolkitHealthItemView
        {
            Type = "manualDownload",
            ManualOnly = true,
            SourceTrust = "community",
            LicenseNote = "Paid vendor license required.",
            LegacyWarning = "Legacy/lab only.",
            ChecksumVerificationMode = "sha256-pinned",
            FreshnessStatus = "UpToDate"
        };

        Assert.Contains("Manual required", v.SafetyBadgesDisplay, StringComparison.Ordinal);
        Assert.Contains("Community source", v.SafetyBadgesDisplay, StringComparison.Ordinal);
        Assert.Contains("Checksum limited", v.SafetyBadgesDisplay, StringComparison.Ordinal);
        Assert.Contains("Up to date", v.SafetyBadgesDisplay, StringComparison.Ordinal);
        Assert.Contains("Legacy/lab only", v.SafetyBadgesDisplay, StringComparison.Ordinal);
        Assert.Contains("Paid/manual", v.SafetyBadgesDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public void FreshnessDetailDisplay_IncludesPinnedLatestAuditAndRecommendation()
    {
        var v = new ToolkitHealthItemView
        {
            CurrentPinnedVersion = "9.8.0",
            LatestKnownStableVersion = "9.9.0",
            FreshnessStatus = "MinorUpdateAvailable",
            LastFreshnessAuditUtc = "2026-05-21T00:00:00Z",
            ChecksumVerificationMode = "sha256-pinned",
            UpdateRecommendation = "Review checksum before promoting."
        };

        Assert.Contains("Pinned 9.8.0", v.FreshnessDetailDisplay, StringComparison.Ordinal);
        Assert.Contains("latest stable 9.9.0", v.FreshnessDetailDisplay, StringComparison.Ordinal);
        Assert.Contains("Update available", v.FreshnessDetailDisplay, StringComparison.Ordinal);
        Assert.Contains("sha256-pinned", v.FreshnessDetailDisplay, StringComparison.Ordinal);
        Assert.Contains("Review checksum before promoting.", v.FreshnessDetailDisplay, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Unsupported by vendor.", null, false, null, null, "Legacy / Lab Only")]
    [InlineData(null, "Paid - vendor licence required.", false, null, null, "Paid - vendor licence")]
    [InlineData(null, null, true, "manualDownload", null, "Manual ISO Required")]
    [InlineData(null, null, false, "manualDownload", null, "Manual ISO Required")]
    [InlineData(null, null, false, "managedAutoDownload", "community", "Community source")]
    [InlineData(null, null, false, "managedAutoDownload", "official", "Official source")]
    [InlineData(null, null, false, "managedAutoDownload", null, null)]
    public void BuildCatalogStatusTag_RoutesByMetadata(string? legacyWarning, string? licenseNote, bool manualOnly, string? type, string? sourceTrust, string? expected)
    {
        Assert.Equal(expected, ToolkitDisplayClassification.BuildCatalogStatusTag(legacyWarning, licenseNote, manualOnly, type, sourceTrust));
    }
}
