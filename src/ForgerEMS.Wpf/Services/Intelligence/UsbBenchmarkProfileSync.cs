using System;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

/// <summary>Maps UI/benchmark service results into Intelligence persistence models.</summary>
public static class UsbBenchmarkProfileSync
{
    public static UsbIntelligenceBenchmarkResult? FromServiceResult(UsbBenchmarkResult result)
    {
        if (!result.Succeeded)
        {
            return null;
        }

        if (result.WriteSpeedMBps > 0 && result.ReadSpeedMBps > 0)
        {
            var cls = ParseMeasurementClass(result.IntelligenceMeasurementClass);
            var (refinedCls, conf, reason) = UsbMeasurementClassifier.Classify(
                result.WriteSpeedMBps,
                result.ReadSpeedMBps,
                null);

            if (cls == UsbSpeedMeasurementClass.Unknown)
            {
                cls = refinedCls;
            }

            var baseConfidence = result.IntelligenceConfidenceScore > 0
                ? result.IntelligenceConfidenceScore
                : conf;

            return new UsbIntelligenceBenchmarkResult
            {
                Succeeded = true,
                EndKind = UsbNativeBenchmarkEndKind.Success,
                WriteSpeedMBps = result.WriteSpeedMBps,
                ReadSpeedMBps = result.ReadSpeedMBps,
                DurationMs = result.BenchmarkDurationMs,
                TestSizeMb = result.TestSizeMb,
                Classification = cls,
                ConfidenceScore = Math.Clamp(baseConfidence, 20, 95),
                Timestamp = result.LastTestedAt ?? DateTimeOffset.UtcNow,
                SummaryLine = result.Summary,
                DetailReason = string.IsNullOrWhiteSpace(result.AccuracyWarning)
                    ? (string.IsNullOrWhiteSpace(reason) ? result.Details : reason)
                    : $"{reason} {result.AccuracyWarning}".Trim(),
                ActualBytesWritten = result.ActualBytesWritten,
                ActualBytesRead = result.ActualBytesRead,
                WriteElapsedMs = result.WriteElapsedMs,
                ReadElapsedMs = result.ReadElapsedMs,
                ReadLikelyCached = result.ReadLikelyCached,
                ReadIsEstimate = result.ReadIsEstimate,
                BenchmarkConfidence = result.BenchmarkConfidence,
                AccuracyWarning = result.AccuracyWarning
            };
        }

        return null;
    }

    private static UsbSpeedMeasurementClass ParseMeasurementClass(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return UsbSpeedMeasurementClass.Unknown;
        }

        if (Enum.TryParse<UsbSpeedMeasurementClass>(raw, true, out var e))
        {
            return e;
        }

        return UsbSpeedMeasurementClass.Unknown;
    }
}
