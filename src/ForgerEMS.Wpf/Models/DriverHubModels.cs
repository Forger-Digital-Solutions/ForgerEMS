namespace VentoyToolkitSetup.Wpf.Models;

public enum DriverHubCategory
{
    Recommended,
    Gpu,
    OemSupport,
    ChipsetStorageNetwork,
    BiosFirmware,
    LinuxDrivers,
    ManualVendorPortals,
    UsbShortcuts
}

public enum DriverHubPlatform
{
    Windows,
    Linux,
    BiosFirmware,
    Oem,
    Gpu,
    Network,
    Chipset,
    Storage,
    Audio,
    Utility,
    ManualPortal
}

public enum DriverHubRecommendationReason
{
    None,
    DetectedManufacturer,
    DetectedGpu,
    DetectedCpu,
    DetectedNetwork,
    DetectedOperatingSystem,
    LinuxGuidanceFilter,
    UniversalStartingPoint
}

public enum DriverHubDownloadKind
{
    None,
    OfficialAppPage,
    OfficialAppInstaller,
    MicrosoftStore,
    DriverSearchPage,
    OemSupportPage,
    ManualVendorPage,
    LinuxGuidance,
    FirmwareGuidance
}

public enum DriverHubPrimaryActionKind
{
    OpenOfficialPage,
    OpenOfficialDownload,
    DownloadInstaller,
    OpenGuidance,
    AddShortcut,
    Unavailable
}

public sealed class DriverHubMatchRule
{
    public IReadOnlyList<string> ManufacturerContains { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ModelContains { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> GpuContains { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> CpuContains { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> NetworkContains { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> OperatingSystemContains { get; init; } = Array.Empty<string>();

    public DriverHubRecommendationReason Reason { get; init; } = DriverHubRecommendationReason.None;

    public string StatusText { get; init; } = string.Empty;
}

public sealed class DriverHubEntry
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Vendor { get; init; } = string.Empty;

    public DriverHubCategory Category { get; init; }

    public string Description { get; init; } = string.Empty;

    public string OfficialPageUrl { get; init; } = string.Empty;

    public string OfficialUrl { get; init; } = string.Empty;

    public string OfficialDownloadUrl { get; init; } = string.Empty;

    public string MicrosoftStoreUrl { get; init; } = string.Empty;

    public string InstallerFileName { get; init; } = string.Empty;

    public DriverHubDownloadKind DownloadKind { get; init; } = DriverHubDownloadKind.None;

    public bool CanDownloadOfficialApp { get; init; }

    public bool CanDirectDownloadInstaller { get; init; }

    public bool CanOpenOfficialDownload { get; init; }

    public bool CanOpenMicrosoftStore { get; init; }

    public bool CanAddShortcutToUsb { get; init; } = true;

    public bool CanDownloadToUsb { get; init; }

    public bool IsInstallerDownload { get; init; }

    public bool IsGuidanceOnly { get; init; }

    public bool RequiresModelLookup { get; init; }

    public bool DoesNotAutoInstall { get; init; } = true;

    public string PrimaryActionLabel { get; init; } = string.Empty;

    public DriverHubPrimaryActionKind PrimaryActionKind { get; init; } = DriverHubPrimaryActionKind.OpenOfficialPage;

    public string BrandTileText { get; init; } = string.Empty;

    public string BrandAccentHex { get; init; } = "#334B5563";

    public IReadOnlyList<DriverHubPlatform> Platforms { get; init; } = Array.Empty<DriverHubPlatform>();

    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    public IReadOnlyList<DriverHubMatchRule> MatchRules { get; init; } = Array.Empty<DriverHubMatchRule>();

    public bool IsFirmwareRelated { get; init; }

    public bool IsLinuxGuidance { get; init; }

    public bool IsManualVendorPortal { get; init; }

    public string UsbShortcutRelativePath { get; init; } = string.Empty;

    public IReadOnlyList<DriverHubRecommendationReason> RecommendationReasons { get; init; } = Array.Empty<DriverHubRecommendationReason>();

    public string SafetyNote { get; init; } = string.Empty;

    public string SourceTrust { get; init; } = DriverHubConstants.OfficialVendorSourceTrust;

    public string EffectiveOfficialPageUrl =>
        !string.IsNullOrWhiteSpace(OfficialPageUrl) ? OfficialPageUrl : OfficialUrl;

    public string EffectivePrimaryUrl =>
        PrimaryActionKind switch
        {
            DriverHubPrimaryActionKind.DownloadInstaller when !string.IsNullOrWhiteSpace(OfficialDownloadUrl) => OfficialDownloadUrl,
            DriverHubPrimaryActionKind.OpenOfficialDownload when !string.IsNullOrWhiteSpace(MicrosoftStoreUrl) => MicrosoftStoreUrl,
            DriverHubPrimaryActionKind.OpenOfficialDownload when !string.IsNullOrWhiteSpace(OfficialDownloadUrl) => OfficialDownloadUrl,
            _ => EffectiveOfficialPageUrl
        };
}

public sealed class DriverHubRecommendation
{
    public DriverHubRecommendation(DriverHubEntry entry, DriverHubRecommendationReason reason, string statusText)
    {
        Entry = entry;
        Reason = reason;
        StatusText = statusText;
    }

    public DriverHubEntry Entry { get; }

    public DriverHubRecommendationReason Reason { get; }

    public string StatusText { get; }
}

public sealed class DriverHubEntryView
{
    public DriverHubEntryView(DriverHubEntry entry)
    {
        Entry = entry;
    }

    public DriverHubEntry Entry { get; }

    public bool IsRecommended { get; set; }

    public string RecommendationStatusText { get; set; } = string.Empty;

    public string Id => Entry.Id;

    public string Name => Entry.Name;

    public string Vendor => Entry.Vendor;

    public DriverHubCategory Category => Entry.Category;

    public string CategoryDisplayName => DriverHubDisplay.FormatCategory(Entry.Category);

    public string Description => Entry.Description;

    public string OfficialUrl => Entry.EffectiveOfficialPageUrl;

    public string OfficialDownloadUrl => Entry.OfficialDownloadUrl;

    public string MicrosoftStoreUrl => Entry.MicrosoftStoreUrl;

    public string EffectivePrimaryUrl => Entry.EffectivePrimaryUrl;

    public string PrimaryActionLabel =>
        !string.IsNullOrWhiteSpace(Entry.PrimaryActionLabel)
            ? Entry.PrimaryActionLabel
            : DriverHubDisplay.BuildPrimaryActionLabel(Entry);

    public string PrimaryActionToolTip =>
        Entry.PrimaryActionKind switch
        {
            DriverHubPrimaryActionKind.DownloadInstaller => "Downloads the official installer file to USB only. ForgerEMS does not run installers.",
            DriverHubPrimaryActionKind.OpenOfficialDownload => "Opens the official app/download page. ForgerEMS does not run installers.",
            DriverHubPrimaryActionKind.OpenGuidance when Entry.IsFirmwareRelated => "Opens official firmware guidance. Confirm the exact model before using firmware packages.",
            DriverHubPrimaryActionKind.OpenGuidance => "Opens official Linux driver guidance.",
            _ => "Opens the official vendor page."
        };

    public bool HasOfficialDownloadAction =>
        !string.IsNullOrWhiteSpace(Entry.OfficialDownloadUrl) &&
        DriverHubUrlLike.IsHttpsUrl(Entry.OfficialDownloadUrl);

    public bool CanShowDownloadOfficialApp =>
        Entry.CanDownloadOfficialApp &&
        Entry.DownloadKind is DriverHubDownloadKind.OfficialAppPage
            or DriverHubDownloadKind.OfficialAppInstaller
            or DriverHubDownloadKind.MicrosoftStore;

    public bool CanShowDownloadInstaller =>
        Entry.PrimaryActionKind == DriverHubPrimaryActionKind.DownloadInstaller &&
        Entry.CanDirectDownloadInstaller &&
        Entry.IsInstallerDownload;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "WPF binding requires an instance property.")]
    public string SecondaryPageActionLabel => "Open Page";

    public bool HasDistinctSecondaryPageAction =>
        DriverHubUrlLike.IsHttpsUrl(Entry.EffectiveOfficialPageUrl) &&
        !string.Equals(Entry.EffectiveOfficialPageUrl, Entry.EffectivePrimaryUrl, StringComparison.OrdinalIgnoreCase);

    public string BrandTileText => Entry.BrandTileText;

    public string BrandAccentHex => Entry.BrandAccentHex;

    public string PlatformBadgesText => string.Join("  ", Entry.Platforms.Select(DriverHubDisplay.FormatPlatform));

    public string TagsText => string.Join(", ", Entry.Tags);

    public string TrustBadgeText => Entry.SourceTrust;

    public string SafetyNote => Entry.SafetyNote;

    public bool HasSafetyNote => !string.IsNullOrWhiteSpace(Entry.SafetyNote);

    public string FirmwareBadgeText =>
        Entry.IsFirmwareRelated
            ? "Firmware guidance only - confirm exact model and vendor instructions."
            : string.Empty;

    public string SafetyStatusText
    {
        get
        {
            if (Entry.IsFirmwareRelated)
            {
                return "Firmware guidance only";
            }

            if (Entry.RequiresModelLookup)
            {
                return "Requires model lookup";
            }

            if (Entry.IsInstallerDownload)
            {
                return "Installer is not run automatically";
            }

            if (Entry.IsGuidanceOnly)
            {
                return "Guidance only";
            }

            return Entry.SourceTrust;
        }
    }

    public string StatusLine
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(RecommendationStatusText))
            {
                return RecommendationStatusText;
            }

            if (Entry.IsLinuxGuidance)
            {
                return "Linux guidance";
            }

            if (Entry.IsFirmwareRelated)
            {
                return "Firmware guidance only";
            }

            if (Entry.RequiresModelLookup)
            {
                return "Requires model lookup";
            }

            if (Entry.IsInstallerDownload)
            {
                return "Installer is not run automatically";
            }

            if (Entry.IsManualVendorPortal)
            {
                return "Official vendor page";
            }

            return Entry.Platforms.Contains(DriverHubPlatform.Windows)
                ? "Windows only"
                : string.Empty;
        }
    }
}

public static class DriverHubConstants
{
    public const string OfficialVendorSourceTrust = "Official vendor source";

    public const string FirmwareSafetyWarning =
        "Confirm exact model, power, battery/AC, and vendor instructions before firmware updates.";
}

public static class DriverHubDisplay
{
    public static string BuildPrimaryActionLabel(DriverHubEntry entry)
    {
        return entry.PrimaryActionKind switch
        {
            DriverHubPrimaryActionKind.DownloadInstaller => "Download Installer",
            DriverHubPrimaryActionKind.OpenOfficialDownload when entry.CanOpenMicrosoftStore ||
                                                                 entry.DownloadKind == DriverHubDownloadKind.OfficialAppPage ||
                                                                 entry.DownloadKind == DriverHubDownloadKind.MicrosoftStore => "Get",
            DriverHubPrimaryActionKind.OpenOfficialDownload => "Open Official Download",
            DriverHubPrimaryActionKind.OpenGuidance when entry.IsFirmwareRelated ||
                                                        entry.DownloadKind == DriverHubDownloadKind.FirmwareGuidance => "Open Firmware Guidance",
            DriverHubPrimaryActionKind.OpenGuidance => "Open Guidance",
            DriverHubPrimaryActionKind.AddShortcut => "Add Shortcut",
            DriverHubPrimaryActionKind.Unavailable => "Unavailable",
            DriverHubPrimaryActionKind.OpenOfficialPage when entry.Category == DriverHubCategory.OemSupport => "Open Support Page",
            DriverHubPrimaryActionKind.OpenOfficialPage when entry.Category == DriverHubCategory.Gpu ||
                                                             entry.DownloadKind == DriverHubDownloadKind.DriverSearchPage ||
                                                             entry.Name.Contains("Drivers", StringComparison.OrdinalIgnoreCase) => "Open Driver Page",
            _ => "Open Official Page"
        };
    }

    public static string FormatCategory(DriverHubCategory category) =>
        category switch
        {
            DriverHubCategory.Gpu => "GPU",
            DriverHubCategory.OemSupport => "OEM Support",
            DriverHubCategory.ChipsetStorageNetwork => "Chipset / Storage / Network",
            DriverHubCategory.BiosFirmware => "BIOS & Firmware",
            DriverHubCategory.LinuxDrivers => "Linux Drivers",
            DriverHubCategory.ManualVendorPortals => "Manual Vendor Portals",
            DriverHubCategory.UsbShortcuts => "USB Shortcuts",
            _ => "Recommended"
        };

    public static string FormatPlatform(DriverHubPlatform platform) =>
        platform switch
        {
            DriverHubPlatform.BiosFirmware => "BIOS/Firmware",
            DriverHubPlatform.ManualPortal => "Manual Portal",
            _ => platform.ToString()
        };
}

public static class DriverHubUrlLike
{
    public static bool IsHttpsUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
}
