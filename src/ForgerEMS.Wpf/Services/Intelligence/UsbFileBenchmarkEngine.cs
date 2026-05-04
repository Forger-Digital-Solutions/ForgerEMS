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
            return UsbIntelligenceBenchmarkResult.Failed(blockReason);
        }

        var testSizeMb = target.FreeBytes >= 512L * 1024 * 1024 ? 128 : 64;
        var marginMb = 128L;
        if (target.FreeBytes < (testSizeMb + marginMb) * 1024L * 1024)
        {
            return UsbIntelligenceBenchmarkResult.Failed(
                $"Not enough free space for a {testSizeMb} MB test plus safety margin.");
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
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
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

            var readWatch = Stopwatch.StartNew();
            await using (var stream = new FileStream(
                             path,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             buffer.Length,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                int read;
                while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                            .ConfigureAwait(false)) > 0)
                {
                }
            }

            readWatch.Stop();
            readMs = readWatch.ElapsedMilliseconds;
            var readSec = Math.Max(readWatch.Elapsed.TotalSeconds, 0.001);
            var readBytes = targetBytes;
            readMbps = Math.Round((readBytes / (1024.0 * 1024.0)) / readSec, 1);
        }
        catch (OperationCanceledException)
        {
            TryDelete(path);
            return UsbIntelligenceBenchmarkResult.Failed("Benchmark cancelled.");
        }
        catch (Exception ex)
        {
            TryDelete(path);
            return UsbIntelligenceBenchmarkResult.Failed(ex.Message);
        }

        TryDelete(path);

        var durationMs = (int)Math.Min(int.MaxValue, writeMs + readMs);
        var (cls, conf, reason) = UsbMeasurementClassifier.Classify(writeMbps, readMbps, wmiHeuristic);
        var benchConf = Math.Min(95, conf + 12);

        return new UsbIntelligenceBenchmarkResult
        {
            Succeeded = true,
            WriteSpeedMBps = writeMbps,
            ReadSpeedMBps = readMbps,
            DurationMs = durationMs,
            Classification = cls,
            ConfidenceScore = benchConf,
            Timestamp = DateTimeOffset.UtcNow,
            SummaryLine =
                $"Measured {writeMbps:0.0} MB/s write, {readMbps:0.0} MB/s read ({testSizeMb} MB sample). {cls}.",
            DetailReason = reason,
            TestSizeMb = testSizeMb
        };
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
