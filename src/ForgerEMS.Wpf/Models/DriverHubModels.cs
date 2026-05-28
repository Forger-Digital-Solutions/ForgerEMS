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

    public string OfficialUrl { get; init; } = string.Empty;

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

    public string OfficialUrl => Entry.OfficialUrl;

    public string PlatformBadgesText => string.Join("  ", Entry.Platforms.Select(DriverHubDisplay.FormatPlatform));

    public string TagsText => string.Join(", ", Entry.Tags);

    public string TrustBadgeText => Entry.SourceTrust;

    public string SafetyNote => Entry.SafetyNote;

    public bool HasSafetyNote => !string.IsNullOrWhiteSpace(Entry.SafetyNote);

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
                return "Requires model lookup";
            }

            if (Entry.IsManualVendorPortal)
            {
                return "Manual/vendor shortcut";
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
