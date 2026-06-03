using System;
using System.Collections.Generic;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public static class ManifestPromotionPolicy
{
    public const string ManagedDownload = "ManagedDownload";
    public const string OfficialDownloadPage = "OfficialDownloadPage";
    public const string ManualMediaRequired = "ManualMediaRequired";
    public const string ReviewFirst = "ReviewFirst";
    public const string VendorPortal = "VendorPortal";
    public const string LicenseRestricted = "LicenseRestricted";
    public const string DynamicMirrorOnly = "DynamicMirrorOnly";
    public const string OemSpecific = "OEMSpecific";
    public const string FirmwareBlocked = "FirmwareBlocked";
    public const string CommunityToolkit = "CommunityToolkit";
    public const string Unsupported = "Unsupported";
    public const string InfoOnly = "InfoOnly";

    public const string PromoteToManaged = "PromoteToManaged";
    public const string KeepOfficialDownloadPage = "KeepOfficialDownloadPage";
    public const string KeepManualMediaRequired = "KeepManualMediaRequired";
    public const string KeepReviewFirst = "KeepReviewFirst";
    public const string KeepVendorPortal = "KeepVendorPortal";
    public const string KeepLicenseRestricted = "KeepLicenseRestricted";
    public const string KeepDynamicMirrorOnly = "KeepDynamicMirrorOnly";
    public const string KeepFirmwareBlocked = "KeepFirmwareBlocked";
    public const string NeedsHumanReview = "NeedsHumanReview";

    public static readonly IReadOnlySet<string> ValidDownloadModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ManagedDownload,
        OfficialDownloadPage,
        ManualMediaRequired,
        ReviewFirst,
        VendorPortal,
        LicenseRestricted,
        DynamicMirrorOnly,
        OemSpecific,
        FirmwareBlocked,
        CommunityToolkit,
        Unsupported,
        InfoOnly
    };

    public static bool IsValidDownloadMode(string? downloadMode) =>
        !string.IsNullOrWhiteSpace(downloadMode) && ValidDownloadModes.Contains(downloadMode.Trim());

    public static string CanonicalizeDownloadMode(string? downloadMode)
    {
        if (string.IsNullOrWhiteSpace(downloadMode))
        {
            return string.Empty;
        }

        foreach (var valid in ValidDownloadModes)
        {
            if (string.Equals(valid, downloadMode.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return valid;
            }
        }

        return string.Empty;
    }

    public static string InferDownloadMode(
        string? explicitDownloadMode,
        string? type,
        bool manualOnly,
        string? kind,
        string? sourceTrust,
        string? notes,
        string? legacyWarning,
        string? licenseNote,
        string? destination,
        string? family)
    {
        var canonical = CanonicalizeDownloadMode(explicitDownloadMode);
        if (!string.IsNullOrWhiteSpace(canonical))
        {
            return canonical;
        }

        var normalizedType = (type ?? string.Empty).Trim();
        if (string.Equals(normalizedType, "file", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedType, "managedAutoDownload", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedType, "managed", StringComparison.OrdinalIgnoreCase))
        {
            return ManagedDownload;
        }

        var normalizedKind = (kind ?? string.Empty).Trim();
        var normalizedTrust = (sourceTrust ?? string.Empty).Trim();
        var text = string.Join(" ", notes, legacyWarning, licenseNote, destination, family).ToLowerInvariant();

        if (IsFirmwareBlocked(text, normalizedKind))
        {
            return FirmwareBlocked;
        }

        if (manualOnly && IsManualMediaText(text))
        {
            return ManualMediaRequired;
        }

        if (string.Equals(normalizedKind, "driver-shortcut", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(normalizedTrust, "official", StringComparison.OrdinalIgnoreCase))
        {
            return IsOemSpecificText(text) ? OemSpecific : VendorPortal;
        }

        if (text.Contains("review first", StringComparison.Ordinal) ||
            text.Contains("provenance", StringComparison.Ordinal))
        {
            return ReviewFirst;
        }

        if (text.Contains("dynamic mirror", StringComparison.Ordinal) ||
            text.Contains("mirror selection", StringComparison.Ordinal) ||
            text.Contains("rotating mirror", StringComparison.Ordinal))
        {
            return DynamicMirrorOnly;
        }

        if (IsLicenseRestrictedText(text))
        {
            return LicenseRestricted;
        }

        if (manualOnly && text.Contains("unsupported", StringComparison.Ordinal))
        {
            return Unsupported;
        }

        if (string.Equals(normalizedTrust, "official", StringComparison.OrdinalIgnoreCase))
        {
            return OfficialDownloadPage;
        }

        if (string.Equals(normalizedTrust, "community", StringComparison.OrdinalIgnoreCase))
        {
            return CommunityToolkit;
        }

        return InfoOnly;
    }

    public static string GetPrimaryActionLabel(string? downloadMode) =>
        CanonicalizeDownloadMode(downloadMode) switch
        {
            ManagedDownload => "Managed Download",
            OfficialDownloadPage => "Official Download Page",
            ManualMediaRequired => "Manual Media Required",
            ReviewFirst => "Review First",
            VendorPortal or OemSpecific => "Vendor Portal",
            LicenseRestricted => "License / EULA Required",
            DynamicMirrorOnly => "Official Mirror Page",
            FirmwareBlocked => "Firmware / BIOS Portal",
            CommunityToolkit => "Community Toolkit Page",
            Unsupported => "Unsupported / Reference Only",
            InfoOnly => "Reference Info",
            _ => "Reference Info"
        };

    public static string GetHelperText(string? downloadMode) =>
        CanonicalizeDownloadMode(downloadMode) switch
        {
            ManagedDownload => "Downloads and verifies checksum when available.",
            OfficialDownloadPage => "Opens the vendor/project page. Technician verifies/downloads manually.",
            ManualMediaRequired => "User must supply legally obtained media.",
            ReviewFirst => "Official/community page; verify licensing/provenance before use.",
            VendorPortal or OemSpecific => "Model-specific/OEM workflow. Use serial/model lookup.",
            LicenseRestricted => "Manual vendor flow required before download/use.",
            DynamicMirrorOnly => "Dynamic mirror/checksum flow prevents safe managed download.",
            FirmwareBlocked => "Firmware downloads are intentionally manual.",
            CommunityToolkit => "Review provenance and licensing before client use.",
            Unsupported => "Unsupported / reference-only entry.",
            InfoOnly => "Reference information only.",
            _ => "Reference information only."
        };

    public static bool IsManagedPromotionAllowed(
        string? downloadMode,
        string? type,
        bool hasChecksumProof,
        bool requireChecksumForRelease)
    {
        var mode = InferDownloadMode(
            downloadMode,
            type,
            manualOnly: false,
            kind: null,
            sourceTrust: null,
            notes: null,
            legacyWarning: null,
            licenseNote: null,
            destination: null,
            family: null);

        return string.Equals(mode, ManagedDownload, StringComparison.Ordinal) &&
               (string.Equals(type, "file", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "managedAutoDownload", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "managed", StringComparison.OrdinalIgnoreCase)) &&
               (!requireChecksumForRelease || hasChecksumProof);
    }

    public static string ClassifyPromotion(
        string? downloadMode,
        string? type,
        bool hasChecksumProof,
        bool requireChecksumForRelease)
    {
        var mode = CanonicalizeDownloadMode(downloadMode);
        if (string.IsNullOrWhiteSpace(mode))
        {
            mode = InferDownloadMode(
                explicitDownloadMode: null,
                type,
                manualOnly: false,
                kind: null,
                sourceTrust: null,
                notes: null,
                legacyWarning: null,
                licenseNote: null,
                destination: null,
                family: null);
        }

        return mode switch
        {
            ManagedDownload when IsManagedPromotionAllowed(mode, type, hasChecksumProof, requireChecksumForRelease) => PromoteToManaged,
            ManagedDownload => NeedsHumanReview,
            OfficialDownloadPage => KeepOfficialDownloadPage,
            ManualMediaRequired => KeepManualMediaRequired,
            ReviewFirst or CommunityToolkit => KeepReviewFirst,
            VendorPortal or OemSpecific => KeepVendorPortal,
            LicenseRestricted => KeepLicenseRestricted,
            DynamicMirrorOnly => KeepDynamicMirrorOnly,
            FirmwareBlocked => KeepFirmwareBlocked,
            Unsupported or InfoOnly => NeedsHumanReview,
            _ => NeedsHumanReview
        };
    }

    private static bool IsManualMediaText(string text) =>
        text.Contains("manual iso", StringComparison.Ordinal) ||
        text.Contains("manual media", StringComparison.Ordinal) ||
        text.Contains("manual installer", StringComparison.Ordinal) ||
        text.Contains("manual ipsw", StringComparison.Ordinal) ||
        text.Contains("user-supplied", StringComparison.Ordinal) ||
        text.Contains("legally obtained", StringComparison.Ordinal) ||
        text.Contains("macos", StringComparison.Ordinal) ||
        text.Contains("ios-ipados", StringComparison.Ordinal);

    private static bool IsFirmwareBlocked(string text, string kind)
    {
        if (text.Contains("android-manual-firmware-drop", StringComparison.Ordinal) ||
            text.Contains("manual firmware", StringComparison.Ordinal) ||
            text.Contains("firmware required", StringComparison.Ordinal) ||
            text.Contains("bios portal", StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(kind, "driver-shortcut", StringComparison.OrdinalIgnoreCase) &&
               (text.Contains("bios", StringComparison.Ordinal) ||
                text.Contains("uefi", StringComparison.Ordinal) ||
                text.Contains("firmware", StringComparison.Ordinal));
    }

    private static bool IsOemSpecificText(string text) =>
        text.Contains("model-specific", StringComparison.Ordinal) ||
        text.Contains("serial", StringComparison.Ordinal) ||
        text.Contains("oem", StringComparison.Ordinal) ||
        text.Contains("drivers\\vendor", StringComparison.Ordinal);

    private static bool IsLicenseRestrictedText(string text) =>
        text.Contains("paid", StringComparison.Ordinal) ||
        text.Contains("commercial", StringComparison.Ordinal) ||
        text.Contains("trial", StringComparison.Ordinal) ||
        text.Contains("eula required", StringComparison.Ordinal) ||
        text.Contains("licence required", StringComparison.Ordinal) ||
        text.Contains("license required", StringComparison.Ordinal);
}
