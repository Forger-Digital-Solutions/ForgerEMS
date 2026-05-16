using System;

namespace VentoyToolkitSetup.Wpf.Services.NetworkPulse;

/// <summary>Holds last successfully accepted measurements for honest UI when a single cycle fails.</summary>
public sealed class NetworkPulseLastKnownGood
{
    public double? PingMs { get; private set; }
    public DateTimeOffset PingUtc { get; private set; }

    public double? DownloadMbps { get; private set; }
    public DateTimeOffset DownloadUtc { get; private set; }

    public double? UploadMbps { get; private set; }
    public DateTimeOffset UploadUtc { get; private set; }

    public void AcceptPing(double? ms, DateTimeOffset utc)
    {
        if (ms is > 0 and < 5000)
        {
            PingMs = ms;
            PingUtc = utc;
        }
    }

    public void AcceptDownload(double? mbps, DateTimeOffset utc)
    {
        if (NetworkPulseSpeedSanity.IsPlausibleMeasuredMbps(mbps))
        {
            DownloadMbps = mbps;
            DownloadUtc = utc;
        }
    }

    public void AcceptUpload(double? mbps, DateTimeOffset utc)
    {
        if (NetworkPulseSpeedSanity.IsPlausibleMeasuredMbps(mbps))
        {
            UploadMbps = mbps;
            UploadUtc = utc;
        }
    }

    public void Clear()
    {
        PingMs = null;
        PingUtc = default;
        DownloadMbps = null;
        DownloadUtc = default;
        UploadMbps = null;
        UploadUtc = default;
    }
}
