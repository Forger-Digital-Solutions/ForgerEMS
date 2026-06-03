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
            UsbBuilderProfilePackStatus.AutoDownloadable => "Managed Download",
            UsbBuilderProfilePackStatus.GuidedOfficialDownload => "Official Download Page",
            UsbBuilderProfilePackStatus.UserSuppliedMedia => "Manual Media Required",
            // v1.2.3: these packs are vendor support / OEM / firmware lookup / licensed-tool
            // shortcuts. Call them what they are so users do not assume drivers were
            // auto-downloaded.
            UsbBuilderProfilePackStatus.LinkOnlyLicenseRestricted => "Vendor Portal / License Required",
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
            UsbBuilderPackDownloadMode.AutoDownloadable => "Managed Download",
            UsbBuilderPackDownloadMode.GuidedOfficialDownload => "Official Download Page",
            UsbBuilderPackDownloadMode.UserSuppliedMedia => "Manual Media Required",
            // v1.2.3: was "Link-only" — make explicit that these are manual / vendor / OEM
            // shortcuts, not managed downloads.
            UsbBuilderPackDownloadMode.LinkOnlyLicenseRestricted => "Vendor Portal",
            UsbBuilderPackDownloadMode.Mixed => "Managed + Official Page",
            UsbBuilderPackDownloadMode.Required => "Required",
            UsbBuilderPackDownloadMode.Optional => "Optional",
            _ => "Pack"
        };
}
