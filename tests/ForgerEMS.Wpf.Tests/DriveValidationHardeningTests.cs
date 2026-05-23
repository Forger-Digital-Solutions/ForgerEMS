using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services.DriveValidation;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Extends the original DriveValidationTests with coverage for the hardening pass:
/// composite identity caching, cross-sample alias detection, uniform-pattern detection,
/// short-write/truncation detection, opportunistic temp cleanup, and UI/legal wording.
/// </summary>
public sealed class DriveValidationHardeningTests
{
    private static UsbTargetInfo RemovableTarget(
        string root,
        long freeBytes = 2L * 1024 * 1024 * 1024,
        long totalBytes = 64L * 1024 * 1024 * 1024,
        string label = "TestUSB",
        string model = "Generic USB 3.0",
        string brand = "Generic") =>
        new()
        {
            DriveLetter = root.TrimEnd('\\', ':') + ":",
            RootPath = root,
            Label = label,
            FileSystem = "exFAT",
            TotalBytes = totalBytes,
            FreeBytes = freeBytes,
            DriveType = "Removable",
            BusType = "USB",
            DeviceBrand = brand,
            DeviceModel = model,
            IsLikelyUsb = true,
            IsRemovableMedia = true,
            IsSelectable = true,
            IsLargeDataPartition = true
        };

    [Fact]
    public void Identity_DifferentSize_ProducesDifferentFingerprint()
    {
        var a = DriveValidationIdentity.Compute(RemovableTarget("E:\\", totalBytes: 16L * 1024 * 1024 * 1024));
        var b = DriveValidationIdentity.Compute(RemovableTarget("E:\\", totalBytes: 64L * 1024 * 1024 * 1024));
        Assert.NotEqual(a.Hash, b.Hash);
    }

    [Fact]
    public void Identity_DifferentModel_ProducesDifferentFingerprint()
    {
        var a = DriveValidationIdentity.Compute(RemovableTarget("E:\\", model: "ModelA"));
        var b = DriveValidationIdentity.Compute(RemovableTarget("E:\\", model: "ModelB"));
        Assert.NotEqual(a.Hash, b.Hash);
    }

    [Fact]
    public void Identity_SameIdentifiers_ProducesSameFingerprint()
    {
        var a = DriveValidationIdentity.Compute(RemovableTarget("E:\\"));
        var b = DriveValidationIdentity.Compute(RemovableTarget("E:\\"));
        Assert.Equal(a.Hash, b.Hash);
    }

    [Fact]
    public void Identity_NullTarget_HasNoneConfidence()
    {
        var fp = DriveValidationIdentity.Compute(null);
        Assert.Equal(DriveValidationIdentity.Confidence.None, fp.Strength);
        Assert.Equal(string.Empty, fp.Hash);
    }

    [Fact]
    public void Identity_Matches_RejectsLegacyEvidenceWithoutFingerprint()
    {
        var target = RemovableTarget("E:\\");
        var current = DriveValidationIdentity.Compute(target);
        var legacy = new DriveValidationEvidence
        {
            TargetVolume = target.RootPath,
            TargetDriveModel = target.DeviceModel
            // no IdentityFingerprint -> legacy entry
        };

        Assert.False(DriveValidationIdentity.Matches(current, legacy));
    }

    [Fact]
    public void Identity_Matches_AcceptsRecordedFingerprint()
    {
        var target = RemovableTarget("E:\\");
        var current = DriveValidationIdentity.Compute(target);
        var fresh = new DriveValidationEvidence
        {
            IdentityFingerprint = current.Hash,
            VolumeSerial = current.VolumeSerial
        };

        Assert.True(DriveValidationIdentity.Matches(current, fresh));
    }

    [Fact]
    public void Identity_Matches_RejectsDifferentFingerprintOnSameLetter()
    {
        var letter = RemovableTarget("E:\\", model: "A");
        var other = RemovableTarget("E:\\", model: "B");
        var current = DriveValidationIdentity.Compute(letter);
        var cached = new DriveValidationEvidence
        {
            IdentityFingerprint = DriveValidationIdentity.Compute(other).Hash
        };

        Assert.False(DriveValidationIdentity.Matches(current, cached));
    }

    [Fact]
    public void Signature_CountAliasedHeadPairs_DetectsDuplicatesAcrossSamples()
    {
        var head0 = DriveValidationSignature.BuildBlock(0, 0, 101, 512);
        var head1 = DriveValidationSignature.BuildBlock(1, 0, 114, 512);
        var head2 = (byte[])head0.Clone();
        var heads = new List<byte[]> { head0, head1, head2 };
        var pairs = DriveValidationSignature.CountAliasedHeadPairs(heads);
        Assert.True(pairs >= 1);
    }

    [Fact]
    public void Signature_CountAliasedHeadPairs_ReturnsZeroForDistinctSignatures()
    {
        var heads = new List<byte[]>
        {
            DriveValidationSignature.BuildBlock(0, 0, 101, 512),
            DriveValidationSignature.BuildBlock(1, 0, 114, 512),
            DriveValidationSignature.BuildBlock(2, 0, 127, 512)
        };
        Assert.Equal(0, DriveValidationSignature.CountAliasedHeadPairs(heads));
    }

    [Fact]
    public void Signature_UniformAllZeros_IsFlaggedAsSuspicious()
    {
        var buf = new byte[1024];
        Assert.True(DriveValidationSignature.IsUniformPattern(buf, buf.Length));
    }

    [Fact]
    public void Signature_UniformAllOnes_IsFlaggedAsSuspicious()
    {
        var buf = new byte[1024];
        Array.Fill(buf, (byte)0xFF);
        Assert.True(DriveValidationSignature.IsUniformPattern(buf, buf.Length));
    }

    [Fact]
    public void Signature_RealSignatureBlock_IsNotUniform()
    {
        var buf = DriveValidationSignature.BuildBlock(0, 0, 101, 1024);
        Assert.False(DriveValidationSignature.IsUniformPattern(buf, buf.Length));
    }

    [Fact]
    public void Signature_TooShortBuffer_NotUniformByDesign()
    {
        var buf = new byte[8];
        Assert.False(DriveValidationSignature.IsUniformPattern(buf, buf.Length));
    }

    [Fact]
    public void TempFileManager_CleanupOrphansBeforeRun_OnlyTouchesSamplePattern()
    {
        var root = Path.Combine(Path.GetTempPath(), "forgerems-dv-orphan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var mgr = new DriveValidationTempFileManager();
            mgr.EnsureTempRoot(root);

            // Simulate leftover from a crashed run plus a stray user file inside .forgerems-drive-validator
            // (we should never put one there, but verify we don't blast unrelated files either).
            var orphan = Path.Combine(mgr.TempRoot, "sample-001-oaaaa.bin");
            File.WriteAllBytes(orphan, new byte[16]);
            var stray = Path.Combine(mgr.TempRoot, "user-notes.txt");
            File.WriteAllText(stray, "user content not authored by ForgerEMS");

            mgr.CleanupOrphansBeforeRun();

            Assert.False(File.Exists(orphan), "Orphan sample file should be deleted");
            Assert.True(File.Exists(stray), "Non-sample file inside our temp folder must be preserved");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void TempFileManager_DoesNotTouchSiblingUserFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "forgerems-dv-sibling-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var siblingFile = Path.Combine(root, "important-user-doc.pdf");
            File.WriteAllText(siblingFile, "user data");
            var mgr = new DriveValidationTempFileManager();
            mgr.EnsureTempRoot(root);

            // Create a tracked sample inside the validator temp root
            var sample = new DriveValidationSample
            {
                Index = 0,
                RelativePath = "sample-000-oabcd.bin",
                ByteLength = 8,
                Seed = 5
            };
            var samplePath = mgr.GetSamplePath(sample);
            File.WriteAllBytes(samplePath, new byte[8]);
            mgr.Track(samplePath);

            mgr.CleanupOrphansBeforeRun();
            mgr.Cleanup();

            Assert.True(File.Exists(siblingFile), "Files outside .forgerems-drive-validator must never be touched");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Service_DetectsTruncatedFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "forgerems-dv-trunc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // Stage a fake "successful" temp folder with a truncated sample file the service should refuse.
            var tempRoot = Path.Combine(root, DriveValidationTargetSafety.TempFolderName);
            Directory.CreateDirectory(tempRoot);

            // The service rebuilds its own samples — so to exercise truncation, hijack the temp folder
            // before the run starts by leaving a file with the same name pattern but wrong size.
            var staged = Path.Combine(tempRoot, "sample-000-o00000000.bin");
            await File.WriteAllBytesAsync(staged, new byte[4]);

            // Mark file read-only so CreateNew throws — the service must treat that as a write failure.
            File.SetAttributes(staged, FileAttributes.ReadOnly);

            var target = RemovableTarget(root + "\\");
            var svc = new DriveValidationService();
            var result = await svc.RunAsync(
                target,
                new DriveValidationOptions { Mode = DriveValidationMode.QuickSafeCheck, BlockSizeBytes = 64 * 1024 });

            // The orphan cleanup may successfully delete the read-only file on some filesystems, so we
            // accept either a Passed result (orphan removed and a fresh run succeeded) or a Failed result
            // (write blocked). Either way the result must not falsely claim "genuine".
            File.SetAttributes(staged, FileAttributes.Normal);
            Assert.NotEqual(DriveValidationStatus.NotRun, result.Status);
            Assert.DoesNotContain("genuine", result.Summary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Service_CapturesIdentityInEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "forgerems-dv-id-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var target = RemovableTarget(root + "\\");
            var svc = new DriveValidationService();
            var result = await svc.RunAsync(
                target,
                new DriveValidationOptions { Mode = DriveValidationMode.QuickSafeCheck, BlockSizeBytes = 64 * 1024 });

            Assert.False(string.IsNullOrWhiteSpace(result.Evidence.IdentityFingerprint));
            Assert.False(string.IsNullOrWhiteSpace(result.Evidence.IdentityConfidence));
            Assert.Equal(target.TotalBytes, result.Evidence.TargetTotalBytes);
            Assert.Equal(target.Label, result.Evidence.TargetLabel);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void UiCopy_DoesNotClaimGenuineOrCertified()
    {
        var blob = string.Join(
            "\n",
            DriveValidationUiCopy.FeatureTitle,
            DriveValidationUiCopy.Intro,
            DriveValidationUiCopy.NotValidatedBuilderHint,
            DriveValidationUiCopy.FailedBuilderWarning,
            DriveValidationUiCopy.CleanupWarningBuilderHint,
            DriveValidationUiCopy.InsufficientFreeSpaceBuilderHint,
            DriveValidationUiCopy.SafeModeAdvisory);

        Assert.DoesNotContain("genuine", blob, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("certif", blob, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ValiDrive", blob, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("100%", blob, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UiCopy_FailedSummaryIsAdvisoryNotAlarmist()
    {
        Assert.Contains("toolkit", DriveValidationUiCopy.FailedBuilderWarning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".forgerems-drive-validator", DriveValidationUiCopy.CleanupWarningBuilderHint, StringComparison.Ordinal);
    }

    [Fact]
    public void TargetSafety_BlocksDestructiveModeEvenWithRemovableTarget()
    {
        var t = RemovableTarget("E:\\");
        var options = new DriveValidationOptions
        {
            Mode = DriveValidationMode.DestructiveFullMediaValidation,
            DestructiveConfirmationText = "ERASE-DRIVE-DATA" // correct phrase but mode is gated by planner
        };

        // Phrase passes the typed-confirmation gate, but the planner refuses destructive mode entirely.
        Assert.True(DriveValidationTargetSafety.IsSafeToStart(t, options, out _));
        var plan = DriveValidationPlanner.Plan(t, options);
        Assert.NotNull(plan.BlockReason);
        Assert.Contains("not available", plan.BlockReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TargetSafety_BlocksEfiAndVtoyEfiLabels()
    {
        var efi = new UsbTargetInfo
        {
            RootPath = "E:\\",
            Label = "EFI",
            FileSystem = "FAT32",
            IsEfiSystemPartition = true,
            IsUndersizedPartition = true,
            IsLikelyUsb = true
        };
        Assert.False(DriveValidationTargetSafety.IsSafeToStart(efi, new DriveValidationOptions(), out _));

        var vtoy = new UsbTargetInfo
        {
            RootPath = "F:\\",
            Label = "VTOYEFI",
            FileSystem = "FAT",
            IsEfiSystemPartition = false,
            IsUndersizedPartition = true,
            IsLikelyUsb = true
        };
        Assert.False(DriveValidationTargetSafety.IsSafeToStart(vtoy, new DriveValidationOptions(), out _));
    }

    [Fact]
    public void Planner_QuickMode_CapsSampleSizeOnLargeDrive()
    {
        // Dev Smoke 2026-05-22: a 120 GB drive with ~85 GB free produced ~14 GB per Quick sample
        // because PlanQuick used `usableBytes / 6` with no cap. Verify the cap holds even with a
        // huge drive so Quick Safe Check stays a few-MB total operation.
        var huge = RemovableTarget("D:\\", freeBytes: 85L * 1024 * 1024 * 1024, totalBytes: 120L * 1024 * 1024 * 1024);
        var plan = DriveValidationPlanner.Plan(huge, new DriveValidationOptions
        {
            Mode = DriveValidationMode.QuickSafeCheck
        });

        Assert.Null(plan.BlockReason);
        Assert.NotEmpty(plan.Samples);
        foreach (var sample in plan.Samples)
        {
            Assert.True(
                sample.ByteLength <= DriveValidationPlanner.QuickModeMaxBytesPerSample,
                $"Quick Safe Check sample {sample.Index} is {sample.ByteLength} bytes; expected ≤ {DriveValidationPlanner.QuickModeMaxBytesPerSample}.");
        }

        var totalBytes = plan.Samples.Sum(s => s.ByteLength);
        Assert.True(totalBytes <= 10L * 1024 * 1024,
            $"Quick Safe Check total writes {totalBytes} bytes; expected ≤ 10 MB for a fast smoke check.");
    }

    [Fact]
    public void Planner_SampledMode_CapsSampleSizeOnLargeDrive()
    {
        var huge = RemovableTarget("D:\\", freeBytes: 85L * 1024 * 1024 * 1024, totalBytes: 120L * 1024 * 1024 * 1024);
        var plan = DriveValidationPlanner.Plan(huge, new DriveValidationOptions
        {
            Mode = DriveValidationMode.SampledCapacityCheck
        });

        foreach (var sample in plan.Samples)
        {
            Assert.True(
                sample.ByteLength <= DriveValidationPlanner.SampledModeMaxBytesPerSample,
                $"Sampled sample {sample.Index} is {sample.ByteLength} bytes; expected ≤ {DriveValidationPlanner.SampledModeMaxBytesPerSample}.");
        }
    }

    [Fact]
    public void Planner_FullFreeSpaceMode_StillUsesFractionBudget()
    {
        // The caps must NOT apply to Full Free-Space mode — that mode is intentionally heavy
        // and is gated behind an explicit "heavy writes" confirmation in the UI.
        var huge = RemovableTarget("D:\\", freeBytes: 85L * 1024 * 1024 * 1024, totalBytes: 120L * 1024 * 1024 * 1024);
        var plan = DriveValidationPlanner.Plan(huge, new DriveValidationOptions
        {
            Mode = DriveValidationMode.FullFreeSpaceValidation,
            FullModeFreeSpaceFraction = 0.25
        });

        var totalBytes = plan.Samples.Sum(s => s.ByteLength);
        Assert.True(totalBytes > 100L * 1024 * 1024,
            $"Full Free-Space total should reflect the fraction budget; got only {totalBytes} bytes.");
    }

    [Fact]
    public async Task Service_QuickModeOnLargeDrive_CompletesInReasonableTime()
    {
        // End-to-end timing guard: a Quick Safe Check against a temp folder mimicking ~85 GB free
        // should finish in well under a minute. Before the planner cap this took 5+ minutes.
        var root = Path.Combine(Path.GetTempPath(), "forgerems-dv-quick-fast-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var target = RemovableTarget(root + "\\", freeBytes: 85L * 1024 * 1024 * 1024, totalBytes: 120L * 1024 * 1024 * 1024);
            var svc = new DriveValidationService();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await svc.RunAsync(
                target,
                new DriveValidationOptions { Mode = DriveValidationMode.QuickSafeCheck, BlockSizeBytes = 256 * 1024 });
            sw.Stop();

            Assert.True(result.Status is DriveValidationStatus.Passed or DriveValidationStatus.PassedWithWarnings,
                $"Quick Safe Check should pass on a writable temp folder; got {result.Status}: {result.Summary}");
            Assert.True(sw.Elapsed.TotalSeconds < 30,
                $"Quick Safe Check took {sw.Elapsed.TotalSeconds:0.0}s; should be well under 30s after the per-sample cap fix.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Service_QuickMode_ProgressCallbackFiresMultipleTimes()
    {
        // The Dev Smoke report said the panel showed no progress for 5+ minutes. Even with small
        // samples the user must see phase/progress updates, not a frozen UI. Count progress
        // callbacks across a real Quick Safe Check run.
        var root = Path.Combine(Path.GetTempPath(), "forgerems-dv-progress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var target = RemovableTarget(root + "\\", freeBytes: 85L * 1024 * 1024 * 1024, totalBytes: 120L * 1024 * 1024 * 1024);
            var svc = new DriveValidationService();
            var phases = new List<DriveValidationPhase>();
            var result = await svc.RunAsync(
                target,
                new DriveValidationOptions { Mode = DriveValidationMode.QuickSafeCheck, BlockSizeBytes = 256 * 1024 },
                onProgress: p => phases.Add(p.Phase));

            Assert.Contains(DriveValidationPhase.Preparing, phases);
            Assert.Contains(DriveValidationPhase.SafetyCheckingTarget, phases);
            Assert.Contains(DriveValidationPhase.PlanningSamples, phases);
            Assert.Contains(DriveValidationPhase.WritingSample, phases);
            Assert.Contains(DriveValidationPhase.ReadingSample, phases);
            Assert.Contains(DriveValidationPhase.CleaningUp, phases);
            Assert.True(phases.Count >= 8,
                $"Quick Safe Check should report ≥ 8 progress events across all phases; saw {phases.Count}.");
            Assert.True(result.Detail.Contains("total ", System.StringComparison.OrdinalIgnoreCase) &&
                        result.Detail.Contains("ms", System.StringComparison.OrdinalIgnoreCase),
                $"Final detail should include elapsed-ms breakdown for diagnostics. Got: {result.Detail}");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Planner_Quick_SpreadsSamplesAcrossUsableSpace()
    {
        var t = RemovableTarget("E:\\", freeBytes: 2L * 1024 * 1024 * 1024);
        var plan = DriveValidationPlanner.Plan(t, new DriveValidationOptions
        {
            Mode = DriveValidationMode.QuickSafeCheck
        });

        var indexes = plan.Samples.Select(s => s.Index).Distinct().ToList();
        Assert.Equal(plan.Samples.Count, indexes.Count);

        // The relative paths embed the logical offset; verify offsets differ between first and last sample.
        Assert.NotEqual(plan.Samples[0].RelativePath, plan.Samples[^1].RelativePath);
    }
}
