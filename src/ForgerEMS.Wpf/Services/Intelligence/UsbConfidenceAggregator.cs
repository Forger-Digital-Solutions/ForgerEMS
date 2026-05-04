using System;
using System.Collections.Generic;
using System.Linq;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public static class UsbConfidenceAggregator
{
    public static (int Score, string Reason) Combine(
        int deviceHeuristicConfidence,
        UsbTopologyDiffResult? diff,
        UsbIntelligenceBenchmarkResult? benchmark,
        UsbKnownPortRecord? portRecord)
    {
        var score = 38;
        var parts = new List<string>();

        if (deviceHeuristicConfidence > 0)
        {
            score += Math.Clamp(deviceHeuristicConfidence / 4, 5, 22);
            parts.Add("WMI port heuristics");
        }

        if (benchmark?.Succeeded == true)
        {
            score += Math.Clamp(benchmark.ConfidenceScore / 3, 12, 28);
            parts.Add("sequential file benchmark");
        }

        if (diff is not null &&
            diff.DiffConfidenceReason != "No prior snapshot to compare." &&
            diff.DiffConfidenceScore >= 55)
        {
            score += 8;
            parts.Add("snapshot diff");
        }

        if (!string.IsNullOrWhiteSpace(portRecord?.UserLabel))
        {
            score += 12;
            parts.Add("user-confirmed port label");
        }

        if (portRecord?.MappingConfidenceScore > 0)
        {
            score += Math.Clamp(portRecord.MappingConfidenceScore / 5, 2, 10);
            parts.Add("guided mapping");
        }

        score = Math.Min(98, score);
        var reason = parts.Count == 0
            ? "Limited signals—run a USB benchmark and refresh."
            : "Confidence from: " + string.Join(", ", parts) + ".";

        return (score, reason);
    }

    public static int PortMappingConfidence(UsbTopologyDiffResult diff, bool labelSaved)
    {
        var c = diff.DiffConfidenceScore;
        if (labelSaved)
        {
            c += 25;
        }

        if (diff.ChangedDevices.Any(x => x.ChangeKind == "LikelyPortMove"))
        {
            c += 15;
        }

        return Math.Min(98, c);
    }
}
