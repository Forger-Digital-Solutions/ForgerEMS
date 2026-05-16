using System;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public sealed class UsbBenchmarkAccuracyAssessment
{
    public bool ReadLikelyCached { get; init; }

    public bool ReadIsEstimate { get; init; }

    public string ConfidenceLabel { get; init; } = "Measured";

    public int ConfidencePenalty { get; init; }

    public string Reason { get; init; } = string.Empty;

    public string ReadDisplaySuffix => ReadLikelyCached || ReadIsEstimate ? " (cache suspected)" : string.Empty;
}

public static class UsbBenchmarkAccuracy
{
    public static int SelectTestSizeMb(long freeBytes)
    {
        const long mib = 1024L * 1024L;
        const long gib = 1024L * mib;

        if (freeBytes >= 8L * gib)
        {
            return 1024;
        }

        if (freeBytes >= 3L * gib)
        {
            return 512;
        }

        return freeBytes >= 512L * mib ? 128 : 64;
    }

    public static UsbBenchmarkAccuracyAssessment Assess(
        double writeMbps,
        double readMbps,
        UsbSpeedClassification? speedHint,
        UsbTargetInfo? target = null)
    {
        if (writeMbps <= 0 || readMbps <= 0 || double.IsNaN(writeMbps) || double.IsNaN(readMbps))
        {
            return new UsbBenchmarkAccuracyAssessment
            {
                ConfidenceLabel = "Invalid",
                ConfidencePenalty = 50,
                Reason = "Benchmark sample was invalid."
            };
        }

        var plausibleCeiling = GetPlausibleReadCeiling(speedHint, target);
        var ratio = writeMbps > 0.001 ? readMbps / writeMbps : double.PositiveInfinity;
        var cacheByCeiling = readMbps > plausibleCeiling;
        var cacheByRatio = readMbps > 800 && ratio >= 8.0;

        if (cacheByCeiling || cacheByRatio)
        {
            var reason = cacheByCeiling
                ? $"Read sample {readMbps:0.0} MB/s exceeds the plausible {DescribeSpeedHint(speedHint, target)} ceiling ({plausibleCeiling:0} MB/s)."
                : $"Read sample is {ratio:0.0}x faster than write and likely came from Windows file cache.";

            return new UsbBenchmarkAccuracyAssessment
            {
                ReadLikelyCached = true,
                ReadIsEstimate = true,
                ConfidenceLabel = "Read may be cached",
                ConfidencePenalty = 35,
                Reason = reason
            };
        }

        if (ratio >= 5.0 && readMbps > 400)
        {
            return new UsbBenchmarkAccuracyAssessment
            {
                ConfidenceLabel = "Measured with caution",
                ConfidencePenalty = 15,
                Reason = $"Read sample is {ratio:0.0}x faster than write; retest after reconnecting if the number looks surprising."
            };
        }

        return new UsbBenchmarkAccuracyAssessment
        {
            ConfidenceLabel = "Measured",
            Reason = "Read and write samples are within plausible USB storage limits."
        };
    }

    private static double GetPlausibleReadCeiling(UsbSpeedClassification? speedHint, UsbTargetInfo? target)
    {
        if (target is not null &&
            target.BusType.Contains("USB", StringComparison.OrdinalIgnoreCase) &&
            target.DeviceModel.Contains("SSD", StringComparison.OrdinalIgnoreCase))
        {
            return speedHint == UsbSpeedClassification.UsbC ? 2500 : 1400;
        }

        return speedHint switch
        {
            UsbSpeedClassification.Usb2 => 70,
            UsbSpeedClassification.UsbC => 2200,
            UsbSpeedClassification.Usb3 => 1200,
            _ => 1200
        };
    }

    private static string DescribeSpeedHint(UsbSpeedClassification? speedHint, UsbTargetInfo? target)
    {
        if (target is not null &&
            target.DeviceModel.Contains("SSD", StringComparison.OrdinalIgnoreCase))
        {
            return "USB SSD/enclosure";
        }

        return speedHint switch
        {
            UsbSpeedClassification.Usb2 => "USB 2",
            UsbSpeedClassification.Usb3 => "USB 3",
            UsbSpeedClassification.UsbC => "USB-C/modern USB",
            _ => "USB storage"
        };
    }
}
