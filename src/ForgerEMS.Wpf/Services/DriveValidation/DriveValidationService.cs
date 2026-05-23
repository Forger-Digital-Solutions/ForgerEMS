using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services.DriveValidation;

public sealed class DriveValidationService : IDriveValidationService
{
    public async Task<DriveValidationResult> RunAsync(
        UsbTargetInfo target,
        DriveValidationOptions options,
        string? portPathHint = null,
        Action<DriveValidationProgress>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var runId = Guid.NewGuid();
        var started = DateTimeOffset.UtcNow;
        var overall = Stopwatch.StartNew();
        Report(onProgress, DriveValidationPhase.Preparing, "Preparing Drive Validator…", 0, 0, 0);

        var phaseWatch = Stopwatch.StartNew();
        Report(onProgress, DriveValidationPhase.SafetyCheckingTarget, "Checking target safety…", 0, 0, 0.05);
        if (!DriveValidationTargetSafety.IsSafeToStart(target, options, out var blockReason))
        {
            return DriveValidationResult.Blocked(
                DriveValidationStatus.UnsafeTargetBlocked,
                $"Target blocked for Drive Validator. (safety-check {phaseWatch.ElapsedMilliseconds} ms)",
                blockReason,
                target,
                options.Mode);
        }

        phaseWatch.Restart();
        Report(onProgress, DriveValidationPhase.PlanningSamples, "Planning validation samples…", 0, 0, 0.1);
        var plan = DriveValidationPlanner.Plan(target, options);
        if (!string.IsNullOrWhiteSpace(plan.BlockReason))
        {
            return DriveValidationResult.Blocked(
                DriveValidationStatus.InsufficientFreeSpace,
                "Cannot plan validation for this target.",
                plan.BlockReason,
                target,
                options.Mode);
        }

        if (target.FreeBytes < plan.ReservedBytes)
        {
            return DriveValidationResult.Blocked(
                DriveValidationStatus.InsufficientFreeSpace,
                "Not enough free space for the selected validation mode.",
                $"Need about {UsbTargetInfo.FormatBytes(plan.ReservedBytes)} free; available {target.DisplayFreeBytes}.",
                target,
                options.Mode);
        }

        var temp = new DriveValidationTempFileManager();
        temp.EnsureTempRoot(target.RootPath);
        temp.CleanupOrphansBeforeRun();

        var identity = DriveValidationIdentity.Compute(target);
        var evidence = new DriveValidationEvidence
        {
            SamplesPlanned = plan.Samples.Count,
            TempFolder = temp.TempRoot,
            TargetVolume = target.RootPath,
            TargetDriveModel = target.DeviceModel,
            BusType = target.BusType,
            PortPath = portPathHint ?? string.Empty,
            TargetTotalBytes = target.TotalBytes,
            TargetLabel = target.Label,
            VolumeSerial = identity.VolumeSerial,
            IdentityFingerprint = identity.Hash,
            IdentityConfidence = identity.ConfidenceText
        };

        var mismatchCount = 0;
        var ioErrors = 0;
        var aliasCount = 0;
        long bytesWritten = 0;
        long bytesVerified = 0;
        var writeWatch = new Stopwatch();
        var readWatch = new Stopwatch();
        var sampleHeads = new System.Collections.Generic.List<byte[]>();
        var samplesWritten = 0;
        var samplesVerified = 0;
        var slowWarning = false;
        var blockSize = Math.Clamp(options.BlockSizeBytes, 64 * 1024, 4 * 1024 * 1024);
        var buffer = new byte[blockSize];
        var total = plan.Samples.Count;

        try
        {
            for (var i = 0; i < plan.Samples.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sample = plan.Samples[i];
                var path = temp.GetSamplePath(sample);
                var fracBase = 0.15 + 0.65 * (i / (double)Math.Max(1, total));
                var sampleWatch = Stopwatch.StartNew();
                var sizeLabel = UsbTargetInfo.FormatBytes(sample.ByteLength);

                Report(onProgress, DriveValidationPhase.WritingSample, $"Writing sample {i + 1}/{total} ({sizeLabel})…", i + 1, total, fracBase);
                writeWatch.Start();
                try
                {
                    await WriteSampleAsync(path, sample, buffer, cancellationToken).ConfigureAwait(false);
                    temp.Track(path);
                    samplesWritten++;
                    bytesWritten += sample.ByteLength;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    ioErrors++;
                    if (i > 0 && samplesVerified == i)
                    {
                        aliasCount++;
                    }

                    return BuildFailed(
                        runId,
                        options.Mode,
                        target,
                        started,
                        evidence,
                        samplesWritten,
                        samplesVerified,
                        bytesWritten,
                        bytesVerified,
                        writeWatch,
                        readWatch,
                        mismatchCount,
                        ioErrors,
                        aliasCount,
                        temp,
                        $"I/O error while writing sample {i + 1}: {ex.Message}",
                        "Failed verification — do not trust this drive.");
                }

                writeWatch.Stop();
                var writeMs = sampleWatch.ElapsedMilliseconds;
                Report(onProgress, DriveValidationPhase.Flushing, $"Flushing writes to media… (sample {i + 1} write {writeMs} ms)", i + 1, total, fracBase + 0.05);

                Report(onProgress, DriveValidationPhase.ReadingSample, $"Reading sample {i + 1}/{total} ({sizeLabel})…", i + 1, total, fracBase + 0.1);
                readWatch.Start();
                try
                {
                    var (verified, head, mismatch) = await VerifySampleAsync(path, sample, buffer, cancellationToken)
                        .ConfigureAwait(false);
                    readWatch.Stop();
                    bytesVerified += sample.ByteLength;
                    if (!verified)
                    {
                        mismatchCount += mismatch;
                        if (i >= total / 2 && samplesVerified > 0)
                        {
                            aliasCount++;
                        }

                        return BuildFailed(
                            runId,
                            options.Mode,
                            target,
                            started,
                            evidence,
                            samplesWritten,
                            samplesVerified,
                            bytesWritten,
                            bytesVerified,
                            writeWatch,
                            readWatch,
                            mismatchCount,
                            ioErrors,
                            aliasCount,
                            temp,
                            $"Verification failed on sample {i + 1}.",
                            i >= total / 2
                                ? "Suspicious capacity behavior detected. Full free-space validation recommended."
                                : "Failed verification — do not trust this drive.");
                    }

                    samplesVerified++;
                    sampleHeads.Add(head);
                    var totalMs = sampleWatch.ElapsedMilliseconds;
                    Report(onProgress, DriveValidationPhase.Verifying, $"Sample {i + 1}/{total} verified in {totalMs} ms.", i + 1, total, fracBase + 0.13);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    readWatch.Stop();
                    ioErrors++;
                    return BuildFailed(
                        runId,
                        options.Mode,
                        target,
                        started,
                        evidence,
                        samplesWritten,
                        samplesVerified,
                        bytesWritten,
                        bytesVerified,
                        writeWatch,
                        readWatch,
                        mismatchCount,
                        ioErrors,
                        aliasCount,
                        temp,
                        $"I/O error while reading sample {i + 1}: {ex.Message}",
                        "Failed verification — do not trust this drive.");
                }
            }

            Report(onProgress, DriveValidationPhase.Verifying, "Analyzing results…", total, total, 0.85);
            aliasCount += DriveValidationSignature.CountAliasedHeadPairs(sampleHeads);

            var writeMbps = ToMbps(bytesWritten, writeWatch.Elapsed.TotalSeconds);
            var readMbps = ToMbps(bytesVerified, readWatch.Elapsed.TotalSeconds);
            if (writeMbps < 2 || readMbps < 2)
            {
                slowWarning = true;
            }

            Report(onProgress, DriveValidationPhase.CleaningUp, "Removing temporary validation files…", total, total, 0.92);
            var cleanupWatch = Stopwatch.StartNew();
            var cleanup = temp.Cleanup();
            cleanupWatch.Stop();

            var status = DriveValidationStatus.Passed;
            var summary = "No issues found in sampled validation.";
            if (aliasCount > 0 || mismatchCount > 0)
            {
                status = DriveValidationStatus.Failed;
                summary = "Suspicious capacity behavior detected.";
            }
            else if (cleanup.LeftoverPaths.Count > 0)
            {
                status = DriveValidationStatus.CleanupWarning;
                summary = "Validation completed but some temporary files remain.";
            }
            else if (slowWarning)
            {
                status = DriveValidationStatus.PassedWithWarnings;
                summary = "No verification errors, but the drive looks slow for a technician toolkit.";
            }

            var detail = BuildDetail(status, aliasCount, mismatchCount, writeMbps, readMbps, options.Mode);
            detail += $" · total {overall.ElapsedMilliseconds} ms (write {writeWatch.ElapsedMilliseconds} ms · read {readWatch.ElapsedMilliseconds} ms · cleanup {cleanupWatch.ElapsedMilliseconds} ms)";
            if (cleanup.LeftoverPaths.Count > 0)
            {
                detail += Environment.NewLine + "Leftover temp paths: " +
                          string.Join("; ", cleanup.LeftoverPaths);
            }

            return new DriveValidationResult
            {
                RunId = runId,
                Status = status,
                Mode = options.Mode,
                Phase = DriveValidationPhase.Complete,
                Summary = summary,
                Detail = detail,
                StartedAtUtc = started,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                TargetRootPath = target.RootPath,
                Evidence = evidence with
                {
                    SamplesWritten = samplesWritten,
                    SamplesVerified = samplesVerified,
                    BytesWritten = bytesWritten,
                    BytesVerified = bytesVerified,
                    WriteSpeedMBps = writeMbps,
                    ReadSpeedMBps = readMbps,
                    MismatchCount = mismatchCount,
                    IoErrorCount = ioErrors,
                    SuspiciousAliasCount = aliasCount,
                    CleanupStatus = cleanup.Status,
                    LeftoverTempPaths = cleanup.LeftoverPaths
                }
            };
        }
        catch (OperationCanceledException)
        {
            var cleanup = temp.Cleanup();
            return new DriveValidationResult
            {
                RunId = runId,
                Status = DriveValidationStatus.Cancelled,
                Mode = options.Mode,
                Phase = DriveValidationPhase.Cancelled,
                Summary = "Drive validation cancelled.",
                Detail = cleanup.LeftoverPaths.Count > 0
                    ? "Cancelled. Leftover temp paths: " + string.Join("; ", cleanup.LeftoverPaths)
                    : "Cancelled before completion.",
                StartedAtUtc = started,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                TargetRootPath = target.RootPath,
                Evidence = evidence with
                {
                    SamplesWritten = samplesWritten,
                    SamplesVerified = samplesVerified,
                    BytesWritten = bytesWritten,
                    BytesVerified = bytesVerified,
                    CleanupStatus = cleanup.Status,
                    LeftoverTempPaths = cleanup.LeftoverPaths
                }
            };
        }
    }

    private static async Task WriteSampleAsync(
        string path,
        DriveValidationSample sample,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            buffer.Length,
            FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);

        var remaining = sample.ByteLength;
        var blockIndex = 0;
        while (remaining > 0)
        {
            var count = (int)Math.Min(buffer.Length, remaining);
            var block = DriveValidationSignature.BuildBlock(sample.Index, blockIndex, sample.Seed, count);
            await stream.WriteAsync(block.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            remaining -= count;
            blockIndex++;
        }

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(bool Ok, byte[] Head, int MismatchBlocks)> VerifySampleAsync(
        string path,
        DriveValidationSample sample,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var headLen = Math.Min(512, buffer.Length);
        var head = new byte[headLen];
        var mismatchBlocks = 0;
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            buffer.Length,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (stream.Length != sample.ByteLength)
        {
            // Short write or truncated readback file: the drive lied about completing the write.
            return (false, head, mismatchBlocks + 1);
        }

        var remaining = sample.ByteLength;
        var blockIndex = 0;
        while (remaining > 0)
        {
            var count = (int)Math.Min(buffer.Length, remaining);

            // Drain the requested block fully so a partial read does not get mistaken for a successful verify.
            var totalRead = 0;
            while (totalRead < count)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(totalRead, count - totalRead), cancellationToken)
                    .ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                totalRead += read;
            }

            if (totalRead <= 0)
            {
                mismatchBlocks++;
                break;
            }

            if (blockIndex == 0 && totalRead >= headLen)
            {
                Array.Copy(buffer, 0, head, 0, headLen);
            }

            if (totalRead < count)
            {
                // Short read partway through the file is a failure even if header bytes matched.
                mismatchBlocks++;
                break;
            }

            if (DriveValidationSignature.IsUniformPattern(buffer, totalRead))
            {
                mismatchBlocks++;
                break;
            }

            if (!DriveValidationSignature.VerifyBlock(buffer.AsSpan(0, totalRead).ToArray(), sample.Index, blockIndex, sample.Seed))
            {
                mismatchBlocks++;
            }

            remaining -= totalRead;
            blockIndex++;
        }

        return (mismatchBlocks == 0, head, mismatchBlocks);
    }

    private static DriveValidationResult BuildFailed(
        Guid runId,
        DriveValidationMode mode,
        UsbTargetInfo target,
        DateTimeOffset started,
        DriveValidationEvidence evidence,
        int samplesWritten,
        int samplesVerified,
        long bytesWritten,
        long bytesVerified,
        Stopwatch writeWatch,
        Stopwatch readWatch,
        int mismatchCount,
        int ioErrors,
        int aliasCount,
        DriveValidationTempFileManager temp,
        string detail,
        string summary)
    {
        var cleanup = temp.Cleanup();
        return new DriveValidationResult
        {
            RunId = runId,
            Status = DriveValidationStatus.Failed,
            Mode = mode,
            Phase = DriveValidationPhase.Failed,
            Summary = summary,
            Detail = detail,
            StartedAtUtc = started,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            TargetRootPath = target.RootPath,
            Evidence = evidence with
            {
                SamplesWritten = samplesWritten,
                SamplesVerified = samplesVerified,
                BytesWritten = bytesWritten,
                BytesVerified = bytesVerified,
                WriteSpeedMBps = ToMbps(bytesWritten, writeWatch.Elapsed.TotalSeconds),
                ReadSpeedMBps = ToMbps(bytesVerified, readWatch.Elapsed.TotalSeconds),
                MismatchCount = mismatchCount,
                IoErrorCount = ioErrors,
                SuspiciousAliasCount = aliasCount,
                CleanupStatus = cleanup.Status,
                LeftoverTempPaths = cleanup.LeftoverPaths
            }
        };
    }

    private static string BuildDetail(
        DriveValidationStatus status,
        int aliasCount,
        int mismatchCount,
        double writeMbps,
        double readMbps,
        DriveValidationMode mode) =>
        $"{status} · mode={mode} · write={writeMbps:0.0} MB/s · read={readMbps:0.0} MB/s · mismatches={mismatchCount} · aliasFlags={aliasCount}";

    private static double ToMbps(long bytes, double seconds)
    {
        if (bytes <= 0 || seconds <= 0)
        {
            return 0;
        }

        return Math.Round((bytes / (1024.0 * 1024.0)) / Math.Max(seconds, 0.001), 1);
    }

    private static void Report(
        Action<DriveValidationProgress>? onProgress,
        DriveValidationPhase phase,
        string message,
        int sampleIndex,
        int sampleCount,
        double fraction)
    {
        onProgress?.Invoke(new DriveValidationProgress
        {
            Phase = phase,
            Message = message,
            SampleIndex = sampleIndex,
            SampleCount = sampleCount,
            ProgressFraction = Math.Clamp(fraction, 0, 1)
        });
    }
}
