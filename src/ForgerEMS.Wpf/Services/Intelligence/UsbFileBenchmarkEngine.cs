using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

/// <summary>Native safe sequential write/read benchmark on the USB volume (no PowerShell).</summary>
public static class UsbFileBenchmarkEngine
{
    public static async Task<UsbIntelligenceBenchmarkResult> RunAsync(
        UsbTargetInfo target,
        UsbSpeedClassification? wmiHeuristic,
        CancellationToken cancellationToken = default)
    {
        if (!UsbTargetSafety.IsSafeForBenchmark(target, out var blockReason))
        {
            return UsbIntelligenceBenchmarkResult.Failed(blockReason, UsbNativeBenchmarkEndKind.ValidationBlocked);
        }

        var testSizeMb = UsbBenchmarkAccuracy.SelectTestSizeMb(target.FreeBytes);
        var marginMb = 128L;
        if (target.FreeBytes < (testSizeMb + marginMb) * 1024L * 1024)
        {
            return UsbIntelligenceBenchmarkResult.Failed(
                $"Not enough free space for a {testSizeMb} MB test plus safety margin.",
                UsbNativeBenchmarkEndKind.ValidationBlocked);
        }

        var root = target.RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fileName = ".forgerems-benchmark-" + Guid.NewGuid().ToString("N") + ".tmp";
        var path = Path.Combine(root, fileName);
        var targetBytes = (long)testSizeMb * 1024L * 1024L;
        var buffer = new byte[4 * 1024 * 1024];
        Random.Shared.NextBytes(buffer);

        double writeMbps;
        double readMbps;
        long writeMs;
        long readMs;
        long actualReadBytes;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var writeWatch = Stopwatch.StartNew();
            await using (var stream = new FileStream(
                             path,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             buffer.Length,
                             FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough))
            {
                var written = 0L;
                while (written < targetBytes)
                {
                    var count = (int)Math.Min(buffer.Length, targetBytes - written);
                    await stream.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    written += count;
                }

                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            writeWatch.Stop();
            writeMs = writeWatch.ElapsedMilliseconds;
            var writeSec = Math.Max(writeWatch.Elapsed.TotalSeconds, 0.001);
            writeMbps = Math.Round((targetBytes / (1024.0 * 1024.0)) / writeSec, 1);

            cancellationToken.ThrowIfCancellationRequested();

            var offsets = BuildRandomReadOffsets(targetBytes, buffer.Length);
            var readBytes = 0L;
            var readWatch = Stopwatch.StartNew();
            await using (var stream = new FileStream(
                             path,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             buffer.Length,
                             FileOptions.Asynchronous | FileOptions.RandomAccess))
            {
                foreach (var offset in offsets)
                {
                    stream.Seek(offset, SeekOrigin.Begin);
                    var remaining = Math.Min(buffer.Length, targetBytes - offset);
                    while (remaining > 0)
                    {
                        var read = await stream.ReadAsync(
                                      buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)),
                                      cancellationToken)
                                  .ConfigureAwait(false);
                        if (read <= 0)
                        {
                            break;
                        }

                        readBytes += read;
                        remaining -= read;
                    }
                }
            }

            readWatch.Stop();
            readMs = readWatch.ElapsedMilliseconds;
            actualReadBytes = readBytes;
            var readSec = Math.Max(readWatch.Elapsed.TotalSeconds, 0.001);
            readMbps = Math.Round((readBytes / (1024.0 * 1024.0)) / readSec, 1);
        }
        catch (OperationCanceledException)
        {
            TryDelete(path);
            var msg = cancellationToken.IsCancellationRequested
                ? "Benchmark stopped: cancellation was requested."
                : "Benchmark stopped: operation was canceled before completion.";
            return UsbIntelligenceBenchmarkResult.Failed(msg, UsbNativeBenchmarkEndKind.OperationCanceled);
        }
        catch (Exception ex)
        {
            TryDelete(path);
            return UsbIntelligenceBenchmarkResult.Failed(ex.Message, UsbNativeBenchmarkEndKind.IoOrSystemError);
        }

        TryDelete(path);

        var durationMs = (int)Math.Min(int.MaxValue, writeMs + readMs);
        var accuracy = UsbBenchmarkAccuracy.Assess(writeMbps, readMbps, wmiHeuristic, target);
        var (cls, conf, reason) = UsbMeasurementClassifier.Classify(writeMbps, readMbps, wmiHeuristic);
        var benchConf = Math.Clamp(Math.Min(95, conf + 12) - accuracy.ConfidencePenalty, 20, 95);
        var detailReason = string.IsNullOrWhiteSpace(accuracy.Reason)
            ? reason
            : $"{reason} {accuracy.Reason}".Trim();
        var summarySuffix = accuracy.ReadLikelyCached || accuracy.ReadIsEstimate
            ? " Read may be cached; treat read speed as an estimate."
            : string.Empty;

        return new UsbIntelligenceBenchmarkResult
        {
            Succeeded = true,
            EndKind = UsbNativeBenchmarkEndKind.Success,
            WriteSpeedMBps = writeMbps,
            ReadSpeedMBps = readMbps,
            DurationMs = durationMs,
            TestSizeMb = testSizeMb,
            Classification = cls,
            ConfidenceScore = benchConf,
            Timestamp = DateTimeOffset.UtcNow,
            SummaryLine =
                $"Measured {writeMbps:0.0} MB/s write, {readMbps:0.0} MB/s read ({testSizeMb} MB sample). {cls}. Confidence: {accuracy.ConfidenceLabel}.{summarySuffix}",
            DetailReason = detailReason,
            ActualBytesWritten = targetBytes,
            ActualBytesRead = actualReadBytes,
            WriteElapsedMs = writeMs,
            ReadElapsedMs = readMs,
            ReadLikelyCached = accuracy.ReadLikelyCached,
            ReadIsEstimate = accuracy.ReadIsEstimate,
            BenchmarkConfidence = accuracy.ConfidenceLabel,
            AccuracyWarning = accuracy.Reason
        };
    }

    private static long[] BuildRandomReadOffsets(long targetBytes, int blockSize)
    {
        var blockCount = Math.Max(1, (int)(targetBytes / blockSize));
        var offsets = new long[blockCount];
        for (var i = 0; i < offsets.Length; i++)
        {
            offsets[i] = (long)i * blockSize;
        }

        var rng = new Random(unchecked((int)targetBytes ^ 0x5f3759df));
        for (var i = offsets.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (offsets[i], offsets[j]) = (offsets[j], offsets[i]);
        }

        return offsets;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best effort
        }
    }
}
