namespace VentoyToolkitSetup.Wpf.Services;

public enum UsbBuilderProfileMediaScanState
{
    NotScanned,
    Scanning,
    Completed,
    Skipped,
    Cancelled,
    PathMissing
}

public sealed class UsbBuilderProfileMediaScanResult
{
    public string CategoryId { get; init; } = string.Empty;

    public UsbBuilderProfileMediaScanState State { get; init; } = UsbBuilderProfileMediaScanState.NotScanned;

    public int FileCount { get; init; }

    public long TotalBytes { get; init; }

    public string? Note { get; init; }
}
