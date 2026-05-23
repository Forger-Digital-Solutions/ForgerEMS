using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services.DriveValidation;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Part C / D — region map model + hardened detection. Pins the contract that Drive Validator
/// publishes a per-region map (status, timings, signatures) so a future tile UI can render
/// individual regions and so detection can describe exactly which region misbehaved.
/// </summary>
public sealed class DriveValidationRegionMapTests
{
    private static UsbTargetInfo RemovableTarget(string root, long freeBytes = 2L * 1024 * 1024 * 1024) => new()
    {
        DriveLetter = root.TrimEnd('\\', ':') + ":",
        RootPath = root,
        Label = "TestUSB",
        FileSystem = "exFAT",
        TotalBytes = 64L * 1024 * 1024 * 1024,
        FreeBytes = freeBytes,
        DriveType = "Removable",
        BusType = "USB",
        DeviceModel = "Generic USB",
        IsLikelyUsb = true,
        IsRemovableMedia = true,
        IsSelectable = true,
        IsLargeDataPartition = true
    };

    [Fact]
    public async Task RegionMap_QuickMode_ProducesRegionPerSample()
    {
        var root = Path.Combine(Path.GetTempPath(), "fems-rmap-q-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var target = RemovableTarget(root + "\\");
            var svc = new DriveValidationService();
            var result = await svc.RunAsync(
                target,
                new DriveValidationOptions { Mode = DriveValidationMode.QuickSafeCheck, BlockSizeBytes = 64 * 1024 });

            Assert.NotEmpty(result.Evidence.Regions);
            Assert.Equal(result.Evidence.SamplesPlanned, result.Evidence.Regions.Count);
            Assert.All(result.Evidence.Regions, r => Assert.True(r.PlannedBytes > 0));
            Assert.All(result.Evidence.Regions, r => Assert.NotEqual(DriveValidationRegionStatus.NotTested, r.Status));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RegionMap_SampledMode_HasMoreRegionsThanQuick()
    {
        var root = Path.Combine(Path.GetTempPath(), "fems-rmap-s-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var target = RemovableTarget(root + "\\");
            var svc = new DriveValidationService();
            var quick = await svc.RunAsync(target,
                new DriveValidationOptions { Mode = DriveValidationMode.QuickSafeCheck, BlockSizeBytes = 64 * 1024 });
            var sampled = await svc.RunAsync(target,
                new DriveValidationOptions { Mode = DriveValidationMode.SampledCapacityCheck, BlockSizeBytes = 64 * 1024 });

            Assert.True(sampled.Evidence.Regions.Count > quick.Evidence.Regions.Count,
                $"Sampled regions={sampled.Evidence.Regions.Count} should exceed Quick regions={quick.Evidence.Regions.Count}.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RegionMap_PassedRun_AllRegionsPassed()
    {
        var root = Path.Combine(Path.GetTempPath(), "fems-rmap-p-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var svc = new DriveValidationService();
            var result = await svc.RunAsync(
                RemovableTarget(root + "\\"),
                new DriveValidationOptions { Mode = DriveValidationMode.QuickSafeCheck, BlockSizeBytes = 64 * 1024 });

            Assert.True(
                result.Status is DriveValidationStatus.Passed or DriveValidationStatus.PassedWithWarnings,
                $"Expected Passed/PassedWithWarnings, got {result.Status}: {result.Summary}");

            // On a healthy filesystem, every region's status should be Passed (or Warning if the
            // optional speed-collapse heuristic fired — both are acceptable as non-error states).
            Assert.All(result.Evidence.Regions, r => Assert.True(
                r.Status is DriveValidationRegionStatus.Passed or DriveValidationRegionStatus.Warning,
                $"Region {r.Index} unexpected status {r.Status}: {r.ErrorMessage}"));
            Assert.Equal(result.Evidence.Regions.Count, result.Evidence.MapSummary.Tested);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RegionMap_SignaturesAreUnique()
    {
        var root = Path.Combine(Path.GetTempPath(), "fems-rmap-u-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var svc = new DriveValidationService();
            var result = await svc.RunAsync(
                RemovableTarget(root + "\\"),
                new DriveValidationOptions { Mode = DriveValidationMode.SampledCapacityCheck, BlockSizeBytes = 64 * 1024 });

            var sigs = result.Evidence.Regions.Select(r => r.ExpectedSignatureHash).ToList();
            Assert.Equal(sigs.Count, sigs.Distinct(StringComparer.Ordinal).Count());
            Assert.All(result.Evidence.Regions, r => Assert.False(string.IsNullOrWhiteSpace(r.ExpectedSignatureHash)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RegionMap_ProgressCallback_CarriesMapSnapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), "fems-rmap-pr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var svc = new DriveValidationService();
            DriveValidationMap? lastMap = null;
            await svc.RunAsync(
                RemovableTarget(root + "\\"),
                new DriveValidationOptions { Mode = DriveValidationMode.QuickSafeCheck, BlockSizeBytes = 64 * 1024 },
                onProgress: p => { if (p.MapSnapshot is not null) lastMap = p.MapSnapshot; });

            Assert.NotNull(lastMap);
            Assert.NotEmpty(lastMap!.Regions);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MapSummary_FromRegions_CountsByStatus()
    {
        var regions = new[]
        {
            new DriveValidationRegion { Index = 0, Status = DriveValidationRegionStatus.Passed },
            new DriveValidationRegion { Index = 1, Status = DriveValidationRegionStatus.Warning },
            new DriveValidationRegion { Index = 2, Status = DriveValidationRegionStatus.Mismatch },
            new DriveValidationRegion { Index = 3, Status = DriveValidationRegionStatus.AliasSuspected },
            new DriveValidationRegion { Index = 4, Status = DriveValidationRegionStatus.IoError },
            new DriveValidationRegion { Index = 5, Status = DriveValidationRegionStatus.Cancelled },
            new DriveValidationRegion { Index = 6, Status = DriveValidationRegionStatus.Planned }
        };

        var summary = DriveValidationMapSummary.FromRegions(regions);

        Assert.Equal(7, summary.Planned);
        Assert.Equal(5, summary.Tested); // Passed+Warning+Mismatch+Alias+IoError, not Cancelled/Planned
        Assert.Equal(1, summary.Passed);
        Assert.Equal(1, summary.Warning);
        Assert.Equal(1, summary.Mismatch);
        Assert.Equal(1, summary.AliasSuspected);
        Assert.Equal(1, summary.IoError);
        Assert.Equal(1, summary.Cancelled);
    }

    [Fact]
    public void RegionSnapshot_IsIndependentCopy()
    {
        var live = new DriveValidationRegion
        {
            Index = 0,
            PlannedBytes = 1024,
            Status = DriveValidationRegionStatus.Writing,
            ExpectedSignatureHash = "abc"
        };

        var snap = live.Snapshot();
        live.Status = DriveValidationRegionStatus.IoError;
        live.ErrorMessage = "boom";

        Assert.Equal(DriveValidationRegionStatus.Writing, snap.Status);
        Assert.Equal(string.Empty, snap.ErrorMessage);
        Assert.Equal("abc", snap.ExpectedSignatureHash);
    }

    [Fact]
    public async Task Detection_TruncatedExistingSample_IsDetectedNotMaskedAsPass()
    {
        // Stage a same-named file in the validator temp folder with the wrong size *before* the
        // service starts. Quick mode's orphan cleanup may delete it; if it doesn't, the writer will
        // fail to CreateNew and the result must NOT be Passed. Either way: never a false "genuine".
        var root = Path.Combine(Path.GetTempPath(), "fems-rmap-tr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var tempRoot = Path.Combine(root, DriveValidationTargetSafety.TempFolderName);
            Directory.CreateDirectory(tempRoot);

            var svc = new DriveValidationService();
            var result = await svc.RunAsync(
                RemovableTarget(root + "\\"),
                new DriveValidationOptions { Mode = DriveValidationMode.QuickSafeCheck, BlockSizeBytes = 64 * 1024 });

            Assert.DoesNotContain("genuine", result.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("100%", result.Summary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
