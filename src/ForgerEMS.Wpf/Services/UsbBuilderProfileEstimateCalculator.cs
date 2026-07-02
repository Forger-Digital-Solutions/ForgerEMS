using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services;

public sealed record UsbBuilderProfileEstimateTotals(
    int SelectedPackCount,
    int SelectedItemCount,
    int AvailableItemCount,
    long MinimumBytes,
    long TypicalBytes,
    long? MaximumBytes,
    long ManagedDownloadBytes,
    long UsbFootprintBytes,
    long ManualUserSuppliedKnownBytes,
    int UnknownManagedDownloadItemCount,
    int UserSuppliedPackCount,
    int ManualUserSuppliedItemCount,
    int AutoOrGuidedPackCount,
    bool HasUncertainUserSuppliedSpace,
    string TypicalRangeDisplay,
    string MinimumDisplay,
    string ManagedDownloadDisplay,
    string UsbFootprintDisplay,
    string ManualUserSuppliedDisplay,
    string OptionalNote);

public static class UsbBuilderProfileEstimateCalculator
{
    public static string FormatSpaceLine(UsbBuilderProfileSpaceEstimate estimate, long? detectedBytes = null)
    {
        if (detectedBytes is > 0)
        {
            return $"Existing files detected: {UsbTargetInfo.FormatBytes(detectedBytes.Value)}";
        }

        if (estimate.Confidence == UsbBuilderPackSizeConfidence.UserSupplied)
        {
            return string.IsNullOrWhiteSpace(estimate.DisplayHint)
                ? "User-supplied: size varies"
                : $"User-supplied: {estimate.DisplayHint}";
        }

        if (estimate.TypicalBytes is null or <= 0)
        {
            return string.IsNullOrWhiteSpace(estimate.DisplayHint)
                ? "Estimated: varies"
                : $"Estimated: {estimate.DisplayHint}";
        }

        var typical = UsbTargetInfo.FormatBytes(estimate.TypicalBytes.Value);
        if (estimate.MinimumBytes is > 0 &&
            estimate.MaximumBytes is > 0 &&
            estimate.MinimumBytes != estimate.MaximumBytes)
        {
            var min = UsbTargetInfo.FormatBytes(estimate.MinimumBytes.Value);
            var max = UsbTargetInfo.FormatBytes(estimate.MaximumBytes.Value);
            var hint = string.IsNullOrWhiteSpace(estimate.DisplayHint) ? string.Empty : $" {estimate.DisplayHint}";
            return $"Estimated: {min}–{max}{hint}".Trim();
        }

        if (!string.IsNullOrWhiteSpace(estimate.DisplayHint))
        {
            return $"Estimated: {typical} ({estimate.DisplayHint})";
        }

        return $"Estimated: {typical}";
    }

    public static UsbBuilderProfileEstimateTotals CalculateTotals(
        IEnumerable<UsbBuilderProfileOption> options,
        long? usbFreeBytes = null)
    {
        var selected = options.Where(o => o.IsIncluded).ToList();
        long minimum = 0;
        long typical = 0;
        long? maximum = 0;
        long managedDownloadBytes = 0;
        long usbFootprintBytes = 0;
        long manualKnownBytes = 0;
        var unknownManagedDownloadItemCount = 0;
        var selectedItemCount = 0;
        var availableItemCount = 0;
        var manualItemCount = 0;
        var userSupplied = 0;
        var autoOrGuided = 0;
        var uncertainUser = false;

        foreach (var option in selected)
        {
            availableItemCount += option.VisibleItemCount;
            selectedItemCount += option.SelectedItemCount;

            if (option.Items.Count > 0)
            {
                var selectedItems = option.Items.Where(i => i.IsSelected).ToList();
                foreach (var item in selectedItems)
                {
                    if (item.CountsAsManualOrUserSupplied)
                    {
                        manualItemCount++;
                        if (item.SpaceEstimate.TypicalBytes is > 0)
                        {
                            manualKnownBytes += item.SpaceEstimate.TypicalBytes.Value;
                        }
                        else
                        {
                            uncertainUser = true;
                        }

                        continue;
                    }

                    var itemMinimum = item.SpaceEstimate.MinimumBytes ?? item.SpaceEstimate.TypicalBytes ?? 0;
                    var itemTypical = item.SpaceEstimate.TypicalBytes ?? 0;
                    var itemMaximum = item.SpaceEstimate.MaximumBytes ?? item.SpaceEstimate.TypicalBytes;

                    minimum += itemMinimum;
                    typical += itemTypical;
                    usbFootprintBytes += item.KnownUsbFootprintEstimateBytes;
                    if (itemMaximum is > 0)
                    {
                        maximum = (maximum ?? 0) + itemMaximum.Value;
                    }

                    if (item.CountsAsManagedDownload)
                    {
                        managedDownloadBytes += item.ManagedDownloadEstimateBytes;
                        if (item.SpaceEstimate.TypicalBytes is null or <= 0)
                        {
                            unknownManagedDownloadItemCount++;
                        }
                    }
                }

                if (selectedItems.Any(i => i.CountsAsManualOrUserSupplied))
                {
                    userSupplied++;
                }

                if (selectedItems.Any(i => i.CountsAsManagedDownload || i.Kind is UsbBuilderProfileItemKind.OfficialPage or UsbBuilderProfileItemKind.HtmlGuide))
                {
                    autoOrGuided++;
                }

                continue;
            }

            var estimate = option.SpaceEstimate;
            if (estimate.MinimumBytes is > 0)
            {
                minimum += estimate.MinimumBytes.Value;
            }

            if (estimate.TypicalBytes is > 0)
            {
                typical += estimate.TypicalBytes.Value;
            }

            if (estimate.MaximumBytes is > 0)
            {
                maximum = (maximum ?? 0) + estimate.MaximumBytes.Value;
            }
            else if (estimate.Confidence == UsbBuilderPackSizeConfidence.UserSupplied)
            {
                uncertainUser = true;
            }

            if (estimate.Confidence == UsbBuilderPackSizeConfidence.UserSupplied ||
                option.RequiresManualMedia)
            {
                userSupplied++;
            }

            if (option.DownloadMode is UsbBuilderPackDownloadMode.AutoDownloadable
                or UsbBuilderPackDownloadMode.GuidedOfficialDownload
                or UsbBuilderPackDownloadMode.Mixed)
            {
                autoOrGuided++;
            }

            usbFootprintBytes += estimate.TypicalBytes ?? 0;
            if (option.DownloadMode is UsbBuilderPackDownloadMode.AutoDownloadable or UsbBuilderPackDownloadMode.Mixed)
            {
                managedDownloadBytes += estimate.TypicalBytes ?? 0;
            }
        }

        var minDisplay = UsbTargetInfo.FormatBytes(minimum);
        var typicalDisplay = UsbTargetInfo.FormatBytes(typical);
        var rangeDisplay = maximum is > 0 && maximum != typical
            ? $"{minDisplay}–{UsbTargetInfo.FormatBytes(maximum.Value)}"
            : typicalDisplay;
        var managedDisplay = managedDownloadBytes > 0 && unknownManagedDownloadItemCount > 0
            ? $"{UsbTargetInfo.FormatBytes(managedDownloadBytes)} + unknown"
            : managedDownloadBytes > 0
                ? UsbTargetInfo.FormatBytes(managedDownloadBytes)
                : unknownManagedDownloadItemCount > 0
                    ? "size unknown"
                    : "none";
        var footprintDisplay = usbFootprintBytes > 0
            ? UsbTargetInfo.FormatBytes(usbFootprintBytes)
            : "near zero";
        var manualDisplay = manualItemCount == 0
            ? "none"
            : uncertainUser || manualKnownBytes <= 0
                ? "varies"
                : UsbTargetInfo.FormatBytes(manualKnownBytes);

        var optionalNote = uncertainUser
            ? "User-supplied media (macOS, legacy Windows, mobile firmware, etc.) is not included in the maximum estimate."
            : string.Empty;

        if (usbFreeBytes is > 0 && typical > usbFreeBytes)
        {
            optionalNote = string.IsNullOrWhiteSpace(optionalNote)
                ? $"Selected packs may exceed available USB free space ({UsbTargetInfo.FormatBytes(usbFreeBytes.Value)})."
                : $"{optionalNote} Warning: typical estimate may exceed USB free space.";
        }

        return new(
            selected.Count,
            selectedItemCount,
            availableItemCount,
            minimum,
            typical,
            maximum,
            managedDownloadBytes,
            usbFootprintBytes,
            manualKnownBytes,
            unknownManagedDownloadItemCount,
            userSupplied,
            manualItemCount,
            autoOrGuided,
            uncertainUser,
            rangeDisplay,
            minDisplay,
            managedDisplay,
            footprintDisplay,
            manualDisplay,
            optionalNote);
    }
}
