namespace VentoyToolkitSetup.Wpf.Models;

public sealed class UsbBuilderProfileSpaceEstimate
{
    public long? MinimumBytes { get; init; }

    public long? TypicalBytes { get; init; }

    public long? MaximumBytes { get; init; }

    public UsbBuilderPackSizeConfidence Confidence { get; init; } = UsbBuilderPackSizeConfidence.Estimated;

    public string DisplayHint { get; init; } = string.Empty;

    public static UsbBuilderProfileSpaceEstimate Fixed(long bytes, string? hint = null) =>
        new()
        {
            MinimumBytes = bytes,
            TypicalBytes = bytes,
            MaximumBytes = bytes,
            Confidence = UsbBuilderPackSizeConfidence.Known,
            DisplayHint = hint ?? string.Empty
        };

    public static UsbBuilderProfileSpaceEstimate Range(
        long minimumBytes,
        long typicalBytes,
        long? maximumBytes,
        UsbBuilderPackSizeConfidence confidence,
        string displayHint) =>
        new()
        {
            MinimumBytes = minimumBytes,
            TypicalBytes = typicalBytes,
            MaximumBytes = maximumBytes,
            Confidence = confidence,
            DisplayHint = displayHint
        };

    public static UsbBuilderProfileSpaceEstimate UserSupplied(string displayHint) =>
        new()
        {
            Confidence = UsbBuilderPackSizeConfidence.UserSupplied,
            DisplayHint = displayHint
        };
}
