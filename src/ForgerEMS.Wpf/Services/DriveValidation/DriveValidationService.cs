using System;
using System.Collections.Generic;
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
        Report(onProgress, DriveValidationPhase.Preparing, "Preparing Drive Validator…", 0, 0, 0, null);

        var phaseWatch = Stopwatch.StartNew();
        Report(onProgress, DriveValidationPhase.SafetyCheckingTarget, "Checking target safety…", 0, 0, 0.05, null);
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
        Report(onProgress, DriveValidationPhase.PlanningSamples, "Planning validation samples…", 0, 0, 0.1, null);
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

        var regions = BuildRegions(plan.Samples);
        var mismatchCount = 0;
        var ioErrors = 0;
        var aliasCount = 0;
        long bytesWritten = 0;
        long bytesVerified = 0;
        var writeWatch = new Stopwatch();
        var readWatch = new Stopwatch();
        var sampleHeads = new List<byte[]>();
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
                var region = regions[i];
                var path = temp.GetSamplePath(sample);
                var fracBase = 0.15 + 0.65 * (i / (double)Math.Max(1, total));
                var sampleWatch = Stopwatch.StartNew();
                var sizeLabel = UsbTargetInfo.FormatBytes(sample.ByteLength);

                region.Status = DriveValidationRegionStatus.Writing;
                Report(onProgress, DriveValidationPhase.WritingSample, $"Writing region {i + 1}/{total} ({sizeLabel})…", i + 1, total, fracBase, regions, i);
                var writeRegionWatch = Stopwatch.StartNew();
                writeWatch.Start();
                try
                {
                    await WriteSampleAsync(path, sample, buffer, cancellationToken).ConfigureAwait(false);
                    temp.Track(path);
                    samplesWritten++;
                    bytesWritten += sample.ByteLength;
                    region.BytesWritten = sample.ByteLength;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    writeWatch.Stop();
                    writeRegionWatch.Stop();
                    region.WriteMs = writeRegionWatch.ElapsedMilliseconds;
                    region.Status = DriveValidationRegionStatus.IoError;
                    region.Severity = DriveValidationRegionSeverity.Error;
                    region.ErrorMessage = ex.Message;
                    ioErrors++;
                    if (i > 0 && samplesVerified == i)
                    {
                        aliasCount++;
                    }

                    Report(onProgress, DriveValidationPhase.Failed, $"I/O error on region {i + 1}.", i + 1, total, fracBase, regions, i);
                    return BuildFailed(
                        runId, options.Mode, target, started, evidence,
                        samplesWritten, samplesVerified, bytesWritten, bytesVerified,
                        writeWatch, readWatch, mismatchCount, ioErrors, aliasCount,
                        regions, false,
                        temp,
                        $"I/O error while writing region {i + 1}: {ex.Message}",
                        "Failed verification — do not trust this drive for a toolkit.");
                }

                writeWatch.Stop();
                writeRegionWatch.Stop();
                region.WriteMs = writeRegionWatch.ElapsedMilliseconds;
                region.WriteMBps = ToMbps(sample.ByteLength, writeRegionWatch.Elapsed.TotalSeconds);
                region.Status = DriveValidationRegionStatus.Flushing;
                var writeMs = sampleWatch.ElapsedMilliseconds;
                Report(onProgress, DriveValidationPhase.Flushing, $"Flushing writes to media… (region {i + 1} write {writeMs} ms)", i + 1, total, fracBase + 0.05, regions, i);

                region.Status = DriveValidationRegionStatus.Verifying;
                Report(onProgress, DriveValidationPhase.ReadingSample, $"Verifying region {i + 1}/{total} ({sizeLabel})…", i + 1, total, fracBase + 0.1, regions, i);
                var readRegionWatch = Stopwatch.StartNew();
                readWatch.Start();
                try
                {
                    var (verified, head, mismatch) = await VerifySampleAsync(path, sample, buffer, cancellationToken)
                        .ConfigureAwait(false);
                    readWatch.Stop();
                    readRegionWatch.Stop();
                    region.ReadMs = readRegionWatch.ElapsedMilliseconds;
                    region.ReadMBps = ToMbps(sample.ByteLength, readRegionWatch.Elapsed.TotalSeconds);
                    region.BytesVerified = sample.ByteLength;
                    region.ObservedSignatureHash = DriveValidationSignature.ComputeHex(head);
                    bytesVerified += sample.ByteLength;
                    if (!verified)
                    {
                        mismatchCount += mismatch;
                        var lateRegion = i >= total / 2 && samplesVerified > 0;
                        region.Status = lateRegion
                            ? DriveValidationRegionStatus.AliasSuspected
                            : DriveValidationRegionStatus.Mismatch;
                        region.Severity = DriveValidationRegionSeverity.Error;
                        region.ErrorMessage = $"Verification failed ({mismatch} block(s) did not match expected signature).";
                        if (lateRegion)
                        {
                            aliasCount++;
                        }

                        Report(onProgress, DriveValidationPhase.Failed, $"Region {i + 1} failed verification.", i + 1, total, fracBase + 0.13, regions, i);
                        return BuildFailed(
                            runId, options.Mode, target, started, evidence,
                            samplesWritten, samplesVerified, bytesWritten, bytesVerified,
                            writeWatch, readWatch, mismatchCount, ioErrors, aliasCount,
                            regions, false,
                            temp,
                            $"Verification failed on region {i + 1}.",
                            lateRegion
                                ? "Suspicious capacity behavior detected. Full free-space validation recommended."
                                : "Failed verification — do not trust this drive for a toolkit.");
                    }

                    samplesVerified++;
                    sampleHeads.Add(head);
                    region.Status = DriveValidationRegionStatus.Passed;
                    region.Severity = DriveValidationRegionSeverity.Info;
                    var totalMs = sampleWatch.ElapsedMilliseconds;
                    Report(onProgress, DriveValidationPhase.Verifying, $"Region {i + 1}/{total} verified in {totalMs} ms.", i + 1, total, fracBase + 0.13, regions, i);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    readWatch.Stop();
                    readRegionWatch.Stop();
                    region.ReadMs = readRegionWatch.ElapsedMilliseconds;
                    region.Status = DriveValidationRegionStatus.IoError;
                    region.Severity = DriveValidationRegionSeverity.Error;
                    region.ErrorMessage = ex.Message;
                    ioErrors++;
                    Report(onProgress, DriveValidationPhase.Failed, $"I/O error on region {i + 1}.", i + 1, total, fracBase + 0.13, regions, i);
                    return BuildFailed(
                        runId, options.Mode, target, started, evidence,
                        samplesWritten, samplesVerified, bytesWritten, bytesVerified,
                        writeWatch, readWatch, mismatchCount, ioErrors, aliasCount,
                        regions, false,
                        temp,
                        $"I/O error while reading region {i + 1}: {ex.Message}",
                        "Failed verification — do not trust this drive for a toolkit.");
                }
            }

            Report(onProgress, DriveValidationPhase.Verifying, "Analyzing results…", total, total, 0.85, regions);
            var crossAlias = DriveValidationSignature.CountAliasedHeadPairs(sampleHeads);
            aliasCount += crossAlias;
            if (crossAlias > 0)
            {
                // Promote one or more passed regions to AliasSuspected so the map UI reflects it.
                MarkAliasedRegions(regions, sampleHeads);
            }

            var speedCollapse = DetectSpeedCollapse(regions);
            if (speedCollapse.collapsed)
            {
                foreach (var idx in speedCollapse.warnIndexes)
                {
                    var r = regions[idx];
                    if (r.Status == DriveValidationRegionStatus.Passed)
                    {
                        r.Status = DriveValidationRegionStatus.Warning;
                        r.Severity = DriveValidationRegionSeverity.Warning;
                        r.WarningReason = "Read speed dropped sharply versus other regions.";
                    }
                }
            }

            var writeMbps = ToMbps(bytesWritten, writeWatch.Elapsed.TotalSeconds);
            var readMbps = ToMbps(bytesVerified, readWatch.Elapsed.TotalSeconds);
            if (writeMbps < 2 || readMbps < 2)
            {
                slowWarning = true;
            }

            Report(onProgress, DriveValidationPhase.CleaningUp, "Removing temporary validation files…", total, total, 0.92, regions);
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
            else if (slowWarning || speedCollapse.collapsed)
            {
                status = DriveValidationStatus.PassedWithWarnings;
                summary = speedCollapse.collapsed
                    ? "Validation completed with warnings — read speed varied sharply between regions."
                    : "No verification errors, but the drive looks slow for a technician toolkit.";
            }

            var detail = BuildDetail(status, aliasCount, mismatchCount, writeMbps, readMbps, options.Mode);
            detail += $" · total {overall.ElapsedMilliseconds} ms (write {writeWatch.ElapsedMilliseconds} ms · read {readWatch.ElapsedMilliseconds} ms · cleanup {cleanupWatch.ElapsedMilliseconds} ms)";
            if (cleanup.LeftoverPaths.Count > 0)
            {
                detail += Environment.NewLine + "Leftover temp paths: " +
                          string.Join("; ", cleanup.LeftoverPaths);
            }

            var snapshot = DriveValidationMap.Snapshot(regions);

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
                    LeftoverTempPaths = cleanup.LeftoverPaths,
                    Regions = snapshot.Regions,
                    MapSummary = snapshot.Summary,
                    SpeedCollapseSuspected = speedCollapse.collapsed
                }
            };
        }
        catch (OperationCanceledException)
        {
            foreach (var r in regions)
            {
                if (r.Status is not DriveValidationRegionStatus.Passed
                              and not DriveValidationRegionStatus.Warning
                              and not DriveValidationRegionStatus.Mismatch
                              and not DriveValidationRegionStatus.AliasSuspected
                              and not DriveValidationRegionStatus.IoError)
                {
                    r.Status = DriveValidationRegionStatus.Cancelled;
                }
            }
            var cleanup = temp.Cleanup();
            var snapshot = DriveValidationMap.Snapshot(regions);
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
                    LeftoverTempPaths = cleanup.LeftoverPaths,
                    Regions = snapshot.Regions,
                    MapSummary = snapshot.Summary
                }
            };
        }
    }

    private static List<DriveValidationRegion> BuildRegions(IReadOnlyList<DriveValidationSample> samples)
    {
        var regions = new List<DriveValidationRegion>(samples.Count);
        for (var i = 0; i < samples.Count; i++)
        {
            var s = samples[i];
            // The relative path encodes the logical offset hint as the trailing "-oXXXXX" segment.
            var offsetHint = TryParseOffsetHint(s.RelativePath);
            regions.Add(new DriveValidationRegion
            {
                Index = i,
                LogicalOffsetHint = offsetHint,
                PlannedBytes = s.ByteLength,
                ExpectedSignatureHash = s.ExpectedSignatureHex,
                Status = DriveValidationRegionStatus.Planned
            });
        }
        return regions;
    }

    private static long TryParseOffsetHint(string relativePath)
    {
        var marker = "-o";
        var dot = relativePath.LastIndexOf('.');
        var oIdx = relativePath.LastIndexOf(marker, StringComparison.Ordinal);
        if (oIdx < 0 || dot <= oIdx)
        {
            return 0;
        }

        var hex = relativePath.Substring(oIdx + marker.Length, dot - oIdx - marker.Length);
        return long.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static void MarkAliasedRegions(List<DriveValidationRegion> regions, IReadOnlyList<byte[]> heads)
    {
        for (var i = 0; i < heads.Count; i++)
        {
            for (var j = i + 1; j < heads.Count; j++)
            {
                if (DriveValidationSignature.BlocksAppearAliased(heads[i], heads[j]))
                {
                    // Mark the later region (likely the aliased copy) as suspect; keep the earlier
                    // one passed so the user can see "region 5 read back data from region 2".
                    if (j < regions.Count)
                    {
                        regions[j].Status = DriveValidationRegionStatus.AliasSuspected;
                        regions[j].Severity = DriveValidationRegionSeverity.Error;
                        regions[j].ErrorMessage = $"Read-back matched another region's data (alias of region {i + 1}).";
                    }
                }
            }
        }
    }

    private static (bool collapsed, int[] warnIndexes) DetectSpeedCollapse(IReadOnlyList<DriveValidationRegion> regions)
    {
        // A counterfeit/failing drive often shows fine first-region speed and then collapses to
        // near-zero throughput as later writes hit the bad/aliased area. Compare each region's
        // read MBps against the median of regions with non-zero speed; if any region is below
        // 25% of the median AND the absolute drop is at least 5 MB/s, flag a warning.
        var speeds = regions
            .Where(r => r.Status == DriveValidationRegionStatus.Passed && r.ReadMBps > 0)
            .Select(r => r.ReadMBps)
            .OrderBy(v => v)
            .ToList();
        if (speeds.Count < 3)
        {
            return (false, Array.Empty<int>());
        }

        var median = speeds[speeds.Count / 2];
        var threshold = median * 0.25;
        if (median - threshold < 5)
        {
            return (false, Array.Empty<int>());
        }

        var warns = new List<int>();
        for (var i = 0; i < regions.Count; i++)
        {
            var r = regions[i];
            if (r.Status == DriveValidationRegionStatus.Passed && r.ReadMBps > 0 && r.ReadMBps < threshold)
            {
                warns.Add(i);
            }
        }

        return (warns.Count > 0, warns.ToArray());
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
            return (false, head, mismatchBlocks + 1);
        }

        var remaining = sample.ByteLength;
        var blockIndex = 0;
        while (remaining > 0)
        {
            var count = (int)Math.Min(buffer.Length, remaining);

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
        IReadOnlyList<DriveValidationRegion> regions,
        bool speedCollapse,
        DriveValidationTempFileManager temp,
        string detail,
        string summary)
    {
        var cleanup = temp.Cleanup();
        var snapshot = DriveValidationMap.Snapshot(regions);
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
                LeftoverTempPaths = cleanup.LeftoverPaths,
                Regions = snapshot.Regions,
                MapSummary = snapshot.Summary,
                SpeedCollapseSuspected = speedCollapse
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
        double fraction,
        IReadOnlyList<DriveValidationRegion>? regions,
        int changedRegionIndex = -1)
    {
        onProgress?.Invoke(new DriveValidationProgress
        {
            Phase = phase,
            Message = message,
            SampleIndex = sampleIndex,
            SampleCount = sampleCount,
            ProgressFraction = Math.Clamp(fraction, 0, 1),
            MapSnapshot = regions is null ? null : DriveValidationMap.Snapshot(regions),
            ChangedRegionIndex = changedRegionIndex
        });
    }
}
