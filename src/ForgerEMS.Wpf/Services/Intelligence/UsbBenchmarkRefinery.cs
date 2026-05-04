using System;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public static class UsbBenchmarkRefinery
{
    public static UsbIntelligenceBenchmarkResult? Refine(
        UsbIntelligenceBenchmarkResult? benchmark,
        UsbSpeedClassification? wmiHeuristic)
    {
        if (benchmark is not { Succeeded: true })
        {
            return benchmark;
        }

        var (cls, conf, reason) = UsbMeasurementClassifier.Classify(
            benchmark.WriteSpeedMBps,
            benchmark.ReadSpeedMBps,
            wmiHeuristic);

        return new UsbIntelligenceBenchmarkResult
        {
            Succeeded = benchmark.Succeeded,
            WriteSpeedMBps = benchmark.WriteSpeedMBps,
            ReadSpeedMBps = benchmark.ReadSpeedMBps,
            DurationMs = benchmark.DurationMs,
            TestSizeMb = benchmark.TestSizeMb,
            Classification = cls,
            ConfidenceScore = Math.Max(benchmark.ConfidenceScore, conf),
            Timestamp = benchmark.Timestamp,
            SummaryLine = benchmark.SummaryLine,
            DetailReason = reason
        };
    }
}
