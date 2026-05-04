using System;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public static class UsbMeasurementClassifier
{
    /// <summary>Classify sequential write/read MB/s. Optionally pass WMI heuristic for USB-C / bottleneck detection.</summary>
    public static (UsbSpeedMeasurementClass Classification, int ConfidenceScore, string Reason) Classify(
        double writeMbps,
        double readMbps,
        UsbSpeedClassification? wmiHeuristic)
    {
        if (writeMbps <= 0 || readMbps <= 0 || double.IsNaN(writeMbps) || double.IsNaN(readMbps))
        {
            return (UsbSpeedMeasurementClass.Unknown, 20, "Invalid or zero speed sample.");
        }

        var min = Math.Min(writeMbps, readMbps);
        var max = Math.Max(writeMbps, readMbps);
        var imbalance = max > 0.001 ? Math.Abs(writeMbps - readMbps) / max : 0;

        var expectsFast =
            wmiHeuristic is UsbSpeedClassification.Usb3 or UsbSpeedClassification.UsbC;

        if (imbalance >= 0.52 && min < 55)
        {
            return (
                UsbSpeedMeasurementClass.Bottleneck,
                62,
                "Large read/write asymmetry with modest throughput—cable, hub, or controller may be limiting one direction.");
        }

        if (expectsFast && max < 48)
        {
            return (
                UsbSpeedMeasurementClass.Bottleneck,
                68,
                "WMI suggests a USB 3-class path, but measured speeds are far below typical USB 3—check cable, port, or hub.");
        }

        if (max < 38)
        {
            return (
                UsbSpeedMeasurementClass.Usb2,
                72,
                "Sequential speeds are consistent with USB 2–class throughput.");
        }

        if (wmiHeuristic == UsbSpeedClassification.UsbC && min >= 30)
        {
            return (
                UsbSpeedMeasurementClass.UsbC,
                74,
                "Topology hints Type-C / USB4-style path and speeds support a modern link.");
        }

        if (min >= 38 && max < 120)
        {
            return (
                UsbSpeedMeasurementClass.Usb3,
                70,
                "Throughput fits a typical USB 3.x mass-storage link.");
        }

        if (max >= 120)
        {
            return wmiHeuristic == UsbSpeedClassification.UsbC
                ? (UsbSpeedMeasurementClass.UsbC, 78, "High throughput with Type-C style topology hint.")
                : (UsbSpeedMeasurementClass.Usb3, 76, "High sequential throughput—USB 3 or better.");
        }

        return (
            UsbSpeedMeasurementClass.Usb2,
            55,
            "Moderate speeds—treat as slower USB unless you retest on another port.");
    }
}
