using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services;

public sealed record UsbBuilderProfileCategoryDefinition(
    string CategoryId,
    string DisplayName,
    string ShortDescription,
    string Platform,
    bool IsRequired,
    bool DefaultIncluded,
    bool RequiresManualMedia,
    UsbBuilderPackDownloadMode DownloadMode,
    UsbBuilderProfileSpaceEstimate SpaceEstimate,
    string ManualMediaExplanation,
    IReadOnlyList<string> MediaScanRelativePaths);

public static class UsbBuilderProfileCatalog
{
    private static readonly IReadOnlyDictionary<string, UsbBuilderProfileCategoryDefinition> Definitions =
        BuildDefinitions().ToDictionary(d => d.CategoryId, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<UsbBuilderProfileCategoryDefinition> All => Definitions.Values.ToList();

    public static UsbBuilderProfileCategoryDefinition GetRequired(string categoryId) =>
        Definitions.TryGetValue(categoryId, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown USB builder category: {categoryId}");

    public static bool TryGet(string categoryId, out UsbBuilderProfileCategoryDefinition definition) =>
        Definitions.TryGetValue(categoryId, out definition!);

    public static string GetSummaryLabel(string categoryId) =>
        categoryId.ToLowerInvariant() switch
        {
            "windows" => "Windows",
            "legacy-windows" => "Legacy Windows",
            "linux-rescue" => "Linux Rescue",
            "macos" => "macOS",
            "android" => "Android",
            "ios-ipados" => "iOS/iPadOS",
            "oem-tools" => "OEM Tools",
            "diagnostics" => "Diagnostics",
            "core" => "Core",
            _ => GetRequired(categoryId).DisplayName
        };

    private static IEnumerable<UsbBuilderProfileCategoryDefinition> BuildDefinitions()
    {
        const long Mb = 1024L * 1024;
        const long Gb = 1024L * 1024 * 1024;

        yield return new(
            "core",
            "Core ForgerEMS USB structure",
            "Required folders, logs, docs, manifest, and Ventoy safety structure.",
            "Core",
            IsRequired: true,
            DefaultIncluded: true,
            RequiresManualMedia: false,
            UsbBuilderPackDownloadMode.Required,
            UsbBuilderProfileSpaceEstimate.Range(40 * Mb, 120 * Mb, 400 * Mb, UsbBuilderPackSizeConfidence.Known, "structure and docs"),
            "Core structure is created by ForgerEMS; no user ISOs are required.",
            ["_docs", "_logs", "_reports"]);

        yield return new(
            "windows",
            "Windows installers and recovery",
            "Official Microsoft Windows 10/11/Server links, WinPE/ADK guidance, and current Windows workflow folders.",
            "Windows",
            IsRequired: false,
            DefaultIncluded: true,
            RequiresManualMedia: false,
            UsbBuilderPackDownloadMode.Mixed,
            UsbBuilderProfileSpaceEstimate.Range(200 * Mb, 2 * Gb, 8 * Gb, UsbBuilderPackSizeConfidence.Dynamic, "depending on managed downloads and ISOs"),
            "Current Windows media may be guided from official Microsoft pages; some items auto-download when the catalog pins a file.",
            ["ISO\\Windows"]);

        yield return new(
            "legacy-windows",
            "Legacy Windows manual media drop folders",
            "Organized drop folders for Windows 8.1 and older user-supplied recovery media.",
            "Windows",
            IsRequired: false,
            DefaultIncluded: true,
            RequiresManualMedia: true,
            UsbBuilderPackDownloadMode.UserSuppliedMedia,
            UsbBuilderProfileSpaceEstimate.UserSupplied("size varies by ISO"),
            "ForgerEMS cannot download unsupported or unlicensed legacy Windows ISOs. Supply legitimate installation media you are licensed to use.",
            ["ISO\\Windows\\Windows-Manual-ISO-Drop"]);

        yield return new(
            "linux-rescue",
            "Linux rescue and installer tools",
            "Linux rescue ISOs and installer/recovery workflows managed by the catalog when enabled.",
            "Linux",
            IsRequired: false,
            DefaultIncluded: true,
            RequiresManualMedia: false,
            UsbBuilderPackDownloadMode.AutoDownloadable,
            UsbBuilderProfileSpaceEstimate.Range(2 * Gb, 6 * Gb, 14 * Gb, UsbBuilderPackSizeConfidence.Estimated, "depending on selected Linux ISOs"),
            "Rescue ISOs download from official sources when pinned in the manifest.",
            ["ISO\\Linux", "ISO\\Tools"]);

        yield return new(
            "macos",
            "macOS installer workflow",
            "Apple support links and drop folders for user-supplied installers, DMGs, or PKGs.",
            "macOS",
            IsRequired: false,
            DefaultIncluded: false,
            RequiresManualMedia: true,
            UsbBuilderPackDownloadMode.GuidedOfficialDownload,
            UsbBuilderProfileSpaceEstimate.Range(5 * Mb, 12 * Gb, 24 * Gb, UsbBuilderPackSizeConfidence.UserSupplied, "user-supplied installers vary by macOS version"),
            "ForgerEMS cannot redistribute macOS installers. Use Apple App Store, recovery, or createinstallmedia workflows and place media in the prepared drop folders.",
            ["ISO\\macOS"]);

        yield return new(
            "android",
            "Android platform tools and firmware workflow",
            "Official adb/fastboot links, Pixel guidance, and OEM firmware drop folders.",
            "Android",
            IsRequired: false,
            DefaultIncluded: false,
            RequiresManualMedia: true,
            UsbBuilderPackDownloadMode.Mixed,
            UsbBuilderProfileSpaceEstimate.Range(50 * Mb, 1 * Gb, 8 * Gb, UsbBuilderPackSizeConfidence.Dynamic, "platform tools plus optional OEM firmware"),
            "Platform tools may be guided or managed where allowed; firmware varies by OEM/model/carrier and must be verified from official sources.",
            ["ISO\\Android", "Tools\\Android"]);

        yield return new(
            "ios-ipados",
            "iOS / iPadOS restore workflow",
            "Apple restore/recovery links and manual IPSW tracking folders.",
            "Apple Mobile",
            IsRequired: false,
            DefaultIncluded: false,
            RequiresManualMedia: true,
            UsbBuilderPackDownloadMode.GuidedOfficialDownload,
            UsbBuilderProfileSpaceEstimate.Range(1 * Mb, 6 * Gb, 16 * Gb, UsbBuilderPackSizeConfidence.UserSupplied, "IPSW size varies by device and iOS version"),
            "Signed IPSW files are user-supplied from official Apple workflows. ForgerEMS prepares folders and guides; it does not host Apple firmware.",
            ["ISO\\iOS-iPadOS", "Tools\\Apple-Mobile"]);

        yield return new(
            "oem-tools",
            "OEM recovery links and vendor tools",
            // v1.2.3: the subtitle states exactly what is on the USB — manual/vendor shortcuts —
            // and never implies drivers, firmware, or BIOS were auto-downloaded.
            "Official vendor support, drivers, firmware lookup, and recovery utility shortcuts. Manual/vendor links — not auto-downloaded.",
            "OEM",
            IsRequired: false,
            DefaultIncluded: true,
            RequiresManualMedia: false,
            UsbBuilderPackDownloadMode.LinkOnlyLicenseRestricted,
            UsbBuilderProfileSpaceEstimate.Range(20 * Mb, 80 * Mb, 300 * Mb, UsbBuilderPackSizeConfidence.Estimated, "manual/vendor shortcuts and guides"),
            "OEM recovery images and model-specific drivers stay with the vendor. ForgerEMS writes manual/vendor link shortcuts (.url) under \\Tools\\Portable\\Vendor and points the user at official OEM support, driver, firmware, and Microsoft Surface pages. BIOS/firmware is never auto-downloaded.",
            ["Tools\\Portable\\Vendor", "Tools\\Portable", "Drivers"]);

        yield return new(
            "diagnostics",
            "Diagnostics and rescue utilities",
            "Disk, imaging, hardware, USB, network, security, and portable technician utilities.",
            "Tools",
            IsRequired: false,
            DefaultIncluded: true,
            RequiresManualMedia: false,
            UsbBuilderPackDownloadMode.AutoDownloadable,
            UsbBuilderProfileSpaceEstimate.Range(1 * Gb, 4 * Gb, 12 * Gb, UsbBuilderPackSizeConfidence.Estimated, "depending on enabled portable tools"),
            "Catalog-managed portable utilities download when pinned; large optional tools may remain link-only.",
            ["Tools\\Portable"]);
    }
}
