using System;
using System.Collections.Generic;
using System.Linq;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services.DriveValidation;

public static class DriveValidationPlanner
{
    private const long SafetyMarginBytes = 32L * 1024 * 1024;
    private const long MinQuickBytes = 3L * 1024 * 1024;
    private const long MinSampledBytes = 12L * 1024 * 1024;
    private const long MinFullBytes = 128L * 1024 * 1024;

    // Hard upper bound on per-sample bytes for the non-Full modes. Without these caps,
    // a 120 GB target with ~85 GB free produces ~14 GB per Quick sample (~42 GB total),
    // which on a 60 MB/s USB stick is a multi-minute silent stall — exactly the Dev Smoke
    // report. Quick is intentionally tiny; Sampled is bounded so even an 8 TB drive finishes
    // within reasonable time. Full Free-Space mode keeps its fraction-based budget — it
    // is the heavy-write path and the UI warns the user before starting.
    internal const long QuickModeMaxBytesPerSample = 1L * 1024 * 1024;        // 1 MB
    internal const long SampledModeMaxBytesPerSample = 8L * 1024 * 1024;      // 8 MB

    public static DriveValidationPlan Plan(UsbTargetInfo target, DriveValidationOptions options)
    {
        if (options.Mode == DriveValidationMode.DestructiveFullMediaValidation)
        {
            return new DriveValidationPlan
            {
                BlockReason = "Destructive full media validation is not available in this build. Use sampled or full free-space modes."
            };
        }

        var blockSize = Math.Clamp(options.BlockSizeBytes, 64 * 1024, 4 * 1024 * 1024);
        var usable = Math.Max(0, target.FreeBytes - SafetyMarginBytes);
        if (usable <= 0)
        {
            return new DriveValidationPlan { BlockReason = "Not enough free space after the safety margin." };
        }

        return options.Mode switch
        {
            DriveValidationMode.QuickSafeCheck => PlanQuick(usable, blockSize),
            DriveValidationMode.SampledCapacityCheck => PlanSampled(usable, blockSize),
            DriveValidationMode.FullFreeSpaceValidation => PlanFull(usable, blockSize, options.FullModeFreeSpaceFraction),
            _ => new DriveValidationPlan { BlockReason = "Unknown validation mode." }
        };
    }

    internal static DriveValidationPlan PlanQuick(long usableBytes, int blockSize)
    {
        if (usableBytes < MinQuickBytes)
        {
            return new DriveValidationPlan { BlockReason = $"Quick Safe Check needs about {UsbTargetInfo.FormatBytes(MinQuickBytes)} free." };
        }

        // Quick mode targets a few MB total even on multi-TB drives so the user gets a
        // result in seconds, not minutes. The per-sample budget is capped — see field doc.
        var perSample = Math.Min(QuickModeMaxBytesPerSample, Math.Max((long)blockSize, usableBytes / 6));
        var offsets = SpreadOffsets(usableBytes, 3);
        return BuildPlan(offsets, perSample, blockSize, seedBase: 101);
    }

    internal static DriveValidationPlan PlanSampled(long usableBytes, int blockSize)
    {
        if (usableBytes < MinSampledBytes)
        {
            return new DriveValidationPlan { BlockReason = $"Sampled Capacity Check needs about {UsbTargetInfo.FormatBytes(MinSampledBytes)} free." };
        }

        // Sampled mode bounds per-sample writes so a 4 TB technician drive still finishes
        // in roughly the same time as a 16 GB stick. Real fake-capacity detection comes
        // from spread + signature uniqueness, not from writing huge files. Full Free-Space
        // mode is the right choice when the operator wants heavy writes.
        var perSample = Math.Min(SampledModeMaxBytesPerSample, Math.Max((long)blockSize * 2L, usableBytes / 8));
        var offsets = SpreadOffsets(usableBytes, 7);
        return BuildPlan(offsets, perSample, blockSize, seedBase: 303);
    }

    internal static DriveValidationPlan PlanFull(long usableBytes, int blockSize, double fraction)
    {
        fraction = Math.Clamp(fraction, 0.05, 0.90);
        var budget = (long)(usableBytes * fraction);
        if (budget < MinFullBytes)
        {
            return new DriveValidationPlan
            {
                BlockReason =
                    $"Full free-space validation needs about {UsbTargetInfo.FormatBytes(MinFullBytes)} free at the selected fraction ({fraction:P0})."
            };
        }

        var sampleCount = Math.Clamp((int)(budget / (blockSize * 4L)), 8, 64);
        var perSample = Math.Max(blockSize, budget / sampleCount);
        var offsets = SpreadOffsets(usableBytes, sampleCount);
        return BuildPlan(offsets, perSample, blockSize, seedBase: 707);
    }

    private static long[] SpreadOffsets(long usableBytes, int count)
    {
        count = Math.Max(1, count);
        if (count == 1)
        {
            return [usableBytes / 4];
        }

        var result = new long[count];
        for (var i = 0; i < count; i++)
        {
            var ratio = count == 1 ? 0.5 : (double)i / (count - 1);
            result[i] = (long)(usableBytes * ratio * 0.85) + (usableBytes / 20);
        }

        return result;
    }

    private static DriveValidationPlan BuildPlan(long[] logicalOffsets, long bytesPerSample, int blockSize, int seedBase)
    {
        var samples = new List<DriveValidationSample>();
        var reserved = 0L;
        for (var i = 0; i < logicalOffsets.Length; i++)
        {
            var length = Math.Max(blockSize, bytesPerSample);
            var seed = seedBase + i * 13;
            var probe = DriveValidationSignature.BuildBlock(i, 0, seed, Math.Min(blockSize, 4096));
            samples.Add(new DriveValidationSample
            {
                Index = i,
                RelativePath = $"sample-{i:D3}-o{logicalOffsets[i]:x}.bin",
                ByteLength = length,
                Seed = seed,
                ExpectedSignatureHex = DriveValidationSignature.ComputeHex(probe)
            });
            reserved += length;
        }

        return new DriveValidationPlan
        {
            Samples = samples,
            ReservedBytes = reserved + SafetyMarginBytes
        };
    }

    public static bool HasDistinctSignatures(IReadOnlyList<DriveValidationSample> samples)
    {
        var set = samples.Select(s => s.ExpectedSignatureHex).Distinct(StringComparer.Ordinal).ToList();
        return set.Count == samples.Count;
    }
}
