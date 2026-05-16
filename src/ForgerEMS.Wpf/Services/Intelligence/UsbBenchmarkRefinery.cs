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
        var accuracy = UsbBenchmarkAccuracy.Assess(benchmark.WriteSpeedMBps, benchmark.ReadSpeedMBps, wmiHeuristic);

        return new UsbIntelligenceBenchmarkResult
        {
            Succeeded = benchmark.Succeeded,
            EndKind = benchmark.EndKind,
            WriteSpeedMBps = benchmark.WriteSpeedMBps,
            ReadSpeedMBps = benchmark.ReadSpeedMBps,
            VerifiedWriteMbps = benchmark.VerifiedWriteMbps > 0 ? benchmark.VerifiedWriteMbps : benchmark.WriteSpeedMBps,
            VerifiedReadMbps = benchmark.VerifiedReadMbps ?? ((benchmark.ReadLikelyCached || benchmark.ReadIsEstimate) ? null : benchmark.ReadSpeedMBps),
            RawReadMbps = benchmark.RawReadMbps ?? ((benchmark.ReadLikelyCached || benchmark.ReadIsEstimate) ? benchmark.ReadSpeedMBps : null),
            IsReadCacheSuspected = benchmark.IsReadCacheSuspected || benchmark.ReadLikelyCached || benchmark.ReadIsEstimate,
            ReadVerificationStatus = string.IsNullOrWhiteSpace(benchmark.ReadVerificationStatus)
                ? ((benchmark.ReadLikelyCached || benchmark.ReadIsEstimate) ? "Unverified / cache suspected" : "Verified")
                : benchmark.ReadVerificationStatus,
            DurationMs = benchmark.DurationMs,
            TestSizeMb = benchmark.TestSizeMb,
            Classification = cls,
            ConfidenceScore = Math.Clamp(Math.Max(benchmark.ConfidenceScore, conf) - accuracy.ConfidencePenalty, 20, 95),
            Timestamp = benchmark.Timestamp,
            SummaryLine = benchmark.SummaryLine,
            DetailReason = string.IsNullOrWhiteSpace(accuracy.Reason)
                ? reason
                : $"{reason} {accuracy.Reason}".Trim(),
            ActualBytesWritten = benchmark.ActualBytesWritten,
            ActualBytesRead = benchmark.ActualBytesRead,
            WriteElapsedMs = benchmark.WriteElapsedMs,
            ReadElapsedMs = benchmark.ReadElapsedMs,
            ReadLikelyCached = benchmark.ReadLikelyCached || accuracy.ReadLikelyCached,
            ReadIsEstimate = benchmark.ReadIsEstimate || accuracy.ReadIsEstimate,
            BenchmarkConfidence = string.IsNullOrWhiteSpace(benchmark.BenchmarkConfidence)
                ? accuracy.ConfidenceLabel
                : benchmark.BenchmarkConfidence,
            AccuracyWarning = string.IsNullOrWhiteSpace(benchmark.AccuracyWarning)
                ? accuracy.Reason
                : benchmark.AccuracyWarning
        };
    }
}
