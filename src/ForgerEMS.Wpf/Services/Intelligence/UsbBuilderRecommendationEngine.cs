using System;
using System.Collections.Generic;
using System.Linq;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public static class UsbBuilderRecommendationEngine
{
    public static UsbBuilderRecommendation Build(
        UsbTargetInfo? selectedTarget,
        UsbDeviceInfo? matchedDevice,
        IReadOnlyList<UsbControllerInfo> controllers,
        UsbTopologyDiffResult? diff,
        UsbMachineProfile? profile,
        UsbIntelligenceBenchmarkResult? benchmark,
        UsbKnownPortRecord? portRecord)
    {
        if (selectedTarget is null || string.IsNullOrWhiteSpace(selectedTarget.DriveLetter))
        {
            return BaselineUnknown("No USB target selected.", "Select a removable USB drive to analyze port speed and builder readiness.");
        }

        var speed = matchedDevice?.InferredSpeed ?? DominantControllerSpeed(controllers);
        var hasAssociation = matchedDevice is not null;
        var diffRisky = diff?.ChangedDevices.Any(c =>
            c.ChangeKind is "LikelyPortMove" or "ControllerPathChanged" or "SpeedChanged") == true &&
            diff.DiffConfidenceScore >= 55;

        var profileBoost = profile?.KnownStablePortKeys?.Count > 0 &&
                           matchedDevice is not null &&
                           !string.IsNullOrWhiteSpace(matchedDevice.StablePortKey) &&
                           profile.KnownStablePortKeys.Contains(matchedDevice.StablePortKey);

        var bench = UsbBenchmarkRefinery.Refine(benchmark, matchedDevice?.InferredSpeed);

        if (diffRisky && diff!.DiffConfidenceScore >= 60 &&
            bench?.Classification != UsbSpeedMeasurementClass.Usb3 &&
            bench?.Classification != UsbSpeedMeasurementClass.UsbC)
        {
            var (s, r) = UsbConfidenceAggregator.Combine(
                matchedDevice?.ConfidenceScore ?? 0,
                diff,
                bench,
                portRecord);
            return new UsbBuilderRecommendation
            {
                Summary = "USB path looks like it changed since the last scan.",
                Detail =
                    "This target's USB path looks unstable or changed since last scan. Reconnect before building.",
                Risk = UsbPortRiskLevel.High,
                Speed = speed,
                Quality = UsbBuilderQuality.Risky,
                ClassificationLine = "Quality: Risky — path changed since last scan.",
                ConfidenceScore = s,
                ConfidenceReason = r,
                MeasuredClassification = bench?.Classification
            };
        }

        if (bench is { Succeeded: true })
        {
            return bench.Classification switch
            {
                UsbSpeedMeasurementClass.Bottleneck => BenchRec(
                    "Measured throughput looks bottlenecked for this link.",
                    "Read/write speeds do not match a healthy USB 3 path—try another port, cable, or direct motherboard port.",
                    UsbPortRiskLevel.Medium,
                    speed,
                    UsbBuilderQuality.Risky,
                    "Quality: Risky — measured bottleneck.",
                    bench,
                    matchedDevice,
                    diff,
                    portRecord),
                UsbSpeedMeasurementClass.Usb2 => BenchRec(
                    "USB 2–class speeds measured.",
                    "Large ISO transfers will be slow. Try a blue USB 3 or USB-C port and rerun the benchmark.",
                    UsbPortRiskLevel.Medium,
                    UsbSpeedClassification.Usb2,
                    UsbBuilderQuality.Slow,
                    "Quality: Slow — measurement matches USB 2.",
                    bench,
                    matchedDevice,
                    diff,
                    portRecord),
                UsbSpeedMeasurementClass.UsbC => BenchRec(
                    "Strong measured speeds (Type-C / modern link).",
                    "This target is suitable for large Ventoy builds based on file throughput.",
                    UsbPortRiskLevel.Low,
                    UsbSpeedClassification.UsbC,
                    UsbBuilderQuality.Ideal,
                    "Quality: Ideal — measurement + topology favor USB-C class.",
                    bench,
                    matchedDevice,
                    diff,
                    portRecord),
                UsbSpeedMeasurementClass.Usb3 => BenchRec(
                    "Good USB 3–class speeds measured.",
                    "Builds should be reasonably quick; keep the same port for consistency.",
                    UsbPortRiskLevel.Low,
                    UsbSpeedClassification.Usb3,
                    profileBoost ? UsbBuilderQuality.Ideal : UsbBuilderQuality.Good,
                    profileBoost
                        ? "Quality: Ideal — benchmark + prior port familiarity."
                        : "Quality: Good — USB 3–class measurement.",
                    bench,
                    matchedDevice,
                    diff,
                    portRecord),
                _ => ContinueHeuristic(
                    selectedTarget,
                    matchedDevice,
                    controllers,
                    diff,
                    profile,
                    profileBoost,
                    speed,
                    hasAssociation,
                    diffRisky,
                    bench,
                    portRecord)
            };
        }

        return ContinueHeuristic(
            selectedTarget,
            matchedDevice,
            controllers,
            diff,
            profile,
            profileBoost,
            speed,
            hasAssociation,
            diffRisky,
            bench,
            portRecord);
    }

    private static UsbBuilderRecommendation BenchRec(
        string summary,
        string detail,
        UsbPortRiskLevel risk,
        UsbSpeedClassification speedClass,
        UsbBuilderQuality quality,
        string classificationLine,
        UsbIntelligenceBenchmarkResult bench,
        UsbDeviceInfo? matchedDevice,
        UsbTopologyDiffResult? diff,
        UsbKnownPortRecord? portRecord)
    {
        var (s, r) = UsbConfidenceAggregator.Combine(
            matchedDevice?.ConfidenceScore ?? 0,
            diff,
            bench,
            portRecord);
        return new UsbBuilderRecommendation
        {
            Summary = summary,
            Detail = detail,
            Risk = risk,
            Speed = speedClass,
            Quality = quality,
            ClassificationLine = classificationLine,
            ConfidenceScore = s,
            ConfidenceReason = r,
            MeasuredClassification = bench.Classification
        };
    }

    private static UsbBuilderRecommendation ContinueHeuristic(
        UsbTargetInfo? selectedTarget,
        UsbDeviceInfo? matchedDevice,
        IReadOnlyList<UsbControllerInfo> controllers,
        UsbTopologyDiffResult? diff,
        UsbMachineProfile? profile,
        bool profileBoost,
        UsbSpeedClassification speed,
        bool hasAssociation,
        bool diffRisky,
        UsbIntelligenceBenchmarkResult? bench,
        UsbKnownPortRecord? portRecord)
    {
        if (diffRisky && diff!.DiffConfidenceScore >= 60)
        {
            var (s, r) = UsbConfidenceAggregator.Combine(
                matchedDevice?.ConfidenceScore ?? 0,
                diff,
                bench,
                portRecord);
            return new UsbBuilderRecommendation
            {
                Summary = "USB path looks like it changed since the last scan.",
                Detail =
                    "This target's USB path looks unstable or changed since last scan. Reconnect before building.",
                Risk = UsbPortRiskLevel.High,
                Speed = speed,
                Quality = UsbBuilderQuality.Risky,
                ClassificationLine = "Quality: Risky — path changed since last scan.",
                ConfidenceScore = s,
                ConfidenceReason = r,
                MeasuredClassification = bench?.Classification
            };
        }

        if (speed is UsbSpeedClassification.Usb3 or UsbSpeedClassification.UsbC && hasAssociation)
        {
            var ideal = speed == UsbSpeedClassification.UsbC || profileBoost;
            var (s, r) = UsbConfidenceAggregator.Combine(
                matchedDevice?.ConfidenceScore ?? 0,
                diff,
                bench,
                portRecord);
            return new UsbBuilderRecommendation
            {
                Summary = ideal ? "Strong USB path for large builds." : "Good USB path for builds.",
                Detail = ideal
                    ? "This target appears connected through a USB 3+ path and is suitable for large Ventoy builds."
                    : "This target appears to use a USB 3-class path. Builds should be reasonably quick; prefer blue USB 3 or USB-C ports when moving cables.",
                Risk = UsbPortRiskLevel.Low,
                Speed = speed,
                Quality = ideal ? UsbBuilderQuality.Ideal : UsbBuilderQuality.Good,
                ClassificationLine = ideal ? "Quality: Ideal — USB 3+ class path." : "Quality: Good — USB 3-class path.",
                ConfidenceScore = s,
                ConfidenceReason = r,
                MeasuredClassification = bench?.Classification
            };
        }

        if (speed == UsbSpeedClassification.Usb2 && hasAssociation)
        {
            var (s, r) = UsbConfidenceAggregator.Combine(
                matchedDevice?.ConfidenceScore ?? 0,
                diff,
                bench,
                portRecord);
            return new UsbBuilderRecommendation
            {
                Summary = "USB 2 path — expect slower transfers.",
                Detail =
                    "This target appears to be running through USB 2. Large ISO transfers will be slow. Try a blue USB 3 port or USB-C port.",
                Risk = UsbPortRiskLevel.Medium,
                Speed = speed,
                Quality = UsbBuilderQuality.Slow,
                ClassificationLine = "Quality: Slow — USB 2-class path.",
                ConfidenceScore = s,
                ConfidenceReason = r,
                MeasuredClassification = bench?.Classification
            };
        }

        if (!hasAssociation)
        {
            var (s, r) = UsbConfidenceAggregator.Combine(
                matchedDevice?.ConfidenceScore ?? 0,
                diff,
                bench,
                portRecord);
            return new UsbBuilderRecommendation
            {
                Summary = "Target drive letter not matched to a USB disk heuristic.",
                Detail =
                    "ForgerEMS could not associate the selected letter with enumerated USB mass storage. Build can continue if the letter is correct—double-check in Disk Management.",
                Risk = UsbPortRiskLevel.Unknown,
                Speed = UsbSpeedClassification.Unknown,
                Quality = UsbBuilderQuality.Unknown,
                ClassificationLine = "Quality: Unknown — drive letter not linked to USB disk WMI row.",
                ConfidenceScore = s,
                ConfidenceReason = r,
                MeasuredClassification = bench?.Classification
            };
        }

        {
            var (s, r) = UsbConfidenceAggregator.Combine(
                matchedDevice?.ConfidenceScore ?? 0,
                diff,
                bench,
                portRecord);
            return new UsbBuilderRecommendation
            {
                Summary = "USB speed not confirmed.",
                Detail =
                    "ForgerEMS could not confirm this USB path speed. Build can continue, but benchmarking is recommended.",
                Risk = UsbPortRiskLevel.Unknown,
                Speed = UsbSpeedClassification.Unknown,
                Quality = UsbBuilderQuality.Unknown,
                ClassificationLine = "Quality: Unknown — speed heuristics inconclusive.",
                ConfidenceScore = s,
                ConfidenceReason = r,
                MeasuredClassification = bench?.Classification
            };
        }
    }

    private static UsbBuilderRecommendation BaselineUnknown(string summary, string detail) =>
        new()
        {
            Summary = summary,
            Detail = detail,
            Risk = UsbPortRiskLevel.Unknown,
            Speed = UsbSpeedClassification.Unknown,
            Quality = UsbBuilderQuality.Unknown,
            ClassificationLine = "Quality: Unknown — select a USB target.",
            ConfidenceScore = 25,
            ConfidenceReason = "No target selected.",
            MeasuredClassification = null
        };

    private static UsbSpeedClassification DominantControllerSpeed(IReadOnlyList<UsbControllerInfo> controllers)
    {
        if (controllers.Any(c => c.InferredSpeed is UsbSpeedClassification.UsbC or UsbSpeedClassification.Usb3))
        {
            return UsbSpeedClassification.Usb3;
        }

        if (controllers.Any(c => c.InferredSpeed == UsbSpeedClassification.Usb2))
        {
            return UsbSpeedClassification.Usb2;
        }

        return UsbSpeedClassification.Unknown;
    }
}
