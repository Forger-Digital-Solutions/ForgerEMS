using System;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public static class UsbKyraNarrativeBuilder
{
    public static KyraUsbNarrative Build(UsbTopologySnapshot snapshot)
    {
        var rec = snapshot.SelectedTargetRecommendation;
        var diff = snapshot.TopologyDiff;

        var bench = snapshot.SelectedTargetBenchmark;
        var label = snapshot.SelectedTargetPortUserLabel?.Trim();
        var confPhrase = ConfidencePhrase(snapshot.CombinedConfidenceScore);

        var shortAnswer = bench is { Succeeded: true }
            ? BuildBenchmarkShortAnswer(bench, label, confPhrase)
            : rec?.Quality switch
            {
                UsbBuilderQuality.Ideal => "Short answer: this USB path looks ideal for big Ventoy-style builds.",
                UsbBuilderQuality.Good => "Short answer: this USB path looks good for normal toolkit work.",
                UsbBuilderQuality.Slow => "Short answer: this USB is probably on a slower USB 2-style link.",
                UsbBuilderQuality.Risky =>
                    "Short answer: Windows shows a different USB path than last scan—treat it like a fresh plug-in.",
                UsbBuilderQuality.Unknown =>
                    "Short answer: I don’t have enough benchmark data yet to rank this USB path.",
                _ => rec?.Summary ?? "Short answer: pick the large data partition in USB Builder, not the tiny EFI slice."
            };

        if (bench is not { Succeeded: true } && !string.IsNullOrWhiteSpace(label))
        {
            shortAnswer =
                $"Short answer: use the port labeled {label}. Run a USB benchmark on this stick for measured speeds.";
        }

        var likely = bench is { Succeeded: true }
            ? bench.DetailReason
            : diff?.SummaryLine ?? "Likely cause: WMI-only heuristics; physical port color and cable quality still matter.";
        if (bench is null || !bench.Succeeded)
        {
            if (rec?.Quality == UsbBuilderQuality.Slow)
            {
                likely = "Likely cause: the disk is enumerating on a USB 2-class controller path or through a hub that masks USB 3.";
            }
            else if (rec?.Quality == UsbBuilderQuality.Risky)
            {
                likely = "Likely cause: you replugged, switched ports, or Windows re-parented the device to another controller.";
            }
        }

        var next = rec?.Detail ?? diff?.RecommendationLine ??
                   "Next step: use a blue USB 3 or USB-C port on the PC, wait for the drive to mount, then retry.";
        if (!string.IsNullOrWhiteSpace(snapshot.CombinedConfidenceReason))
        {
            next = $"{next} ({snapshot.CombinedConfidenceReason})";
        }

        if (!string.IsNullOrWhiteSpace(rec?.ConfidenceReason))
        {
            next = $"{next} Builder confidence: {rec.ConfidenceReason}";
        }

        return new KyraUsbNarrative
        {
            ShortAnswer = shortAnswer,
            LikelyCause = likely,
            NextStep = next
        };
    }

    private static string BuildBenchmarkShortAnswer(
        UsbIntelligenceBenchmarkResult bench,
        string? label,
        string confPhrase)
    {
        var speeds =
            $"It benchmarked at ~{bench.WriteSpeedMBps:0.0} MB/s write and ~{bench.ReadSpeedMBps:0.0} MB/s read ({bench.Classification}).";
        if (!string.IsNullOrWhiteSpace(label))
        {
            return
                $"Short answer: use the port labeled {label}. {speeds} Confidence is {confPhrase}.";
        }

        return $"Short answer: last file benchmark measured ~{bench.WriteSpeedMBps:0.0} MB/s write and ~{bench.ReadSpeedMBps:0.0} MB/s read ({bench.Classification}). Confidence is {confPhrase}.";
    }

    private static string ConfidencePhrase(int score) =>
        score switch
        {
            >= 70 => "high",
            >= 40 => "medium",
            _ => "low"
        };
}
