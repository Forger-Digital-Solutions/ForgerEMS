using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services;

public static class UsbBuilderProfileStatusResolver
{
    public static UsbBuilderProfilePackStatus Resolve(
        UsbBuilderProfileCategoryDefinition definition,
        bool isIncluded,
        long detectedBytes,
        int detectedFileCount)
    {
        if (definition.IsRequired)
        {
            return UsbBuilderProfilePackStatus.Required;
        }

        if (!isIncluded)
        {
            return UsbBuilderProfilePackStatus.NotSelected;
        }

        if (detectedBytes > 0 || detectedFileCount > 0)
        {
            return definition.RequiresManualMedia
                ? UsbBuilderProfilePackStatus.AlreadyPresent
                : UsbBuilderProfilePackStatus.Included;
        }

        return definition.DownloadMode switch
        {
            UsbBuilderPackDownloadMode.AutoDownloadable => UsbBuilderProfilePackStatus.AutoDownloadable,
            UsbBuilderPackDownloadMode.GuidedOfficialDownload => UsbBuilderProfilePackStatus.GuidedOfficialDownload,
            UsbBuilderPackDownloadMode.UserSuppliedMedia => UsbBuilderProfilePackStatus.UserSuppliedMedia,
            UsbBuilderPackDownloadMode.LinkOnlyLicenseRestricted => UsbBuilderProfilePackStatus.LinkOnlyLicenseRestricted,
            UsbBuilderPackDownloadMode.Mixed when definition.RequiresManualMedia => UsbBuilderProfilePackStatus.GuidedOfficialDownload,
            UsbBuilderPackDownloadMode.Mixed => UsbBuilderProfilePackStatus.AutoDownloadable,
            UsbBuilderPackDownloadMode.Optional => UsbBuilderProfilePackStatus.Optional,
            UsbBuilderPackDownloadMode.Required => UsbBuilderProfilePackStatus.Required,
            _ => UsbBuilderProfilePackStatus.Included
        };
    }

    public static string ToDisplayLabel(UsbBuilderProfilePackStatus status) =>
        status switch
        {
            UsbBuilderProfilePackStatus.Required => "Required",
            UsbBuilderProfilePackStatus.Included => "Included",
            UsbBuilderProfilePackStatus.AutoDownloadable => "Auto-downloadable",
            UsbBuilderProfilePackStatus.GuidedOfficialDownload => "Guided official download",
            UsbBuilderProfilePackStatus.UserSuppliedMedia => "User-supplied media",
            // v1.2.3: these packs are vendor support / OEM / firmware lookup / licensed-tool
            // shortcuts. Call them what they are so users do not assume drivers were
            // auto-downloaded.
            UsbBuilderProfilePackStatus.LinkOnlyLicenseRestricted => "Manual / vendor links available",
            UsbBuilderProfilePackStatus.Optional => "Optional",
            UsbBuilderProfilePackStatus.AlreadyPresent => "Already present",
            UsbBuilderProfilePackStatus.NotSelected => "Not selected",
            UsbBuilderProfilePackStatus.Missing => "Missing",
            UsbBuilderProfilePackStatus.NeedsReview => "Needs review",
            _ => status.ToString()
        };

    public static string ToAcquisitionChip(UsbBuilderPackDownloadMode mode) =>
        mode switch
        {
            UsbBuilderPackDownloadMode.AutoDownloadable => "Auto / catalog",
            UsbBuilderPackDownloadMode.GuidedOfficialDownload => "Guided official",
            UsbBuilderPackDownloadMode.UserSuppliedMedia => "User-supplied",
            // v1.2.3: was "Link-only" — make explicit that these are manual / vendor / OEM
            // shortcuts, not managed downloads.
            UsbBuilderPackDownloadMode.LinkOnlyLicenseRestricted => "Manual / vendor",
            UsbBuilderPackDownloadMode.Mixed => "Auto + guided",
            UsbBuilderPackDownloadMode.Required => "Required",
            UsbBuilderPackDownloadMode.Optional => "Optional",
            _ => "Pack"
        };
}
