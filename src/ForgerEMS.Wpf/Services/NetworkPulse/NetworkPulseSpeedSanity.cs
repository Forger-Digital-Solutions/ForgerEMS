using System;

namespace VentoyToolkitSetup.Wpf.Services.NetworkPulse;

public static class NetworkPulseSpeedSanity
{
    /// <summary>Reject absurd or NaN throughput so UI never shows junk Gbps from timing glitches.</summary>
    public const double MaxPlausibleInternetMbps = 8000;

    public static bool IsPlausibleMeasuredMbps(double? mbps) =>
        mbps is > 0 and < MaxPlausibleInternetMbps && !double.IsNaN(mbps.Value) && !double.IsInfinity(mbps.Value);

    public static double? FilterSampleMbps(double? mbps) =>
        IsPlausibleMeasuredMbps(mbps) ? mbps : null;
}
