using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services;

public sealed record UsbBuilderProfileEstimateTotals(
    int SelectedPackCount,
    long MinimumBytes,
    long TypicalBytes,
    long? MaximumBytes,
    int UserSuppliedPackCount,
    int AutoOrGuidedPackCount,
    bool HasUncertainUserSuppliedSpace,
    string TypicalRangeDisplay,
    string MinimumDisplay,
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
            return "Estimated: varies";
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
        var userSupplied = 0;
        var autoOrGuided = 0;
        var uncertainUser = false;

        foreach (var option in selected)
        {
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
        }

        var minDisplay = UsbTargetInfo.FormatBytes(minimum);
        var typicalDisplay = UsbTargetInfo.FormatBytes(typical);
        var rangeDisplay = maximum is > 0 && maximum != typical
            ? $"{minDisplay}–{UsbTargetInfo.FormatBytes(maximum.Value)}"
            : typicalDisplay;

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
            minimum,
            typical,
            maximum,
            userSupplied,
            autoOrGuided,
            uncertainUser,
            rangeDisplay,
            minDisplay,
            optionalNote);
    }
}
