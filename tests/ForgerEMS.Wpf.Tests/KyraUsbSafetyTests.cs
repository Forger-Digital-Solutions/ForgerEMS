using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Kyra;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraUsbSafetyTests
{
    [Fact]
    public void ExecutionBlockReason_ForCDrive_UsesExpectedCopy()
    {
        var c = new UsbTargetInfo
        {
            RootPath = "C:\\",
            Label = "OS",
            TotalBytes = 256L * 1024 * 1024 * 1024,
            FileSystem = "NTFS",
            IsLikelyUsb = false,
            IsRemovableMedia = false,
            IsSelectable = false,
            IsSystemDrive = true,
            IsBootDrive = true
        };

        var reason = UsbTargetSafety.GetExecutionBlockReason(c);
        Assert.Equal(UsbTargetSafety.WindowsOsDriveBlockedExplanation, reason);
    }

    [Fact]
    public void ExecutionBlockReason_VtoyefiPartition_IsBlocked()
    {
        var t = new UsbTargetInfo
        {
            RootPath = "E:\\",
            Label = "VTOYEFI",
            TotalBytes = 32L * 1024 * 1024,
            FileSystem = "FAT32",
            IsEfiSystemPartition = true,
            IsUndersizedPartition = true,
            IsSelectable = false
        };

        var reason = UsbTargetSafety.GetExecutionBlockReason(t);
        Assert.NotNull(reason);
        Assert.Contains("boot partition", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExecutionBlockReason_EfiPartition_IsBlocked()
    {
        var t = new UsbTargetInfo
        {
            RootPath = "S:\\",
            Label = "EFI",
            TotalBytes = 260L * 1024 * 1024,
            FileSystem = "FAT32",
            IsEfiSystemPartition = true,
            IsSelectable = false
        };

        Assert.NotNull(UsbTargetSafety.GetExecutionBlockReason(t));
    }

    [Fact]
    public void ExecutionBlockReason_InternalBootSystemPartition_BlockedWhenNotLargeData()
    {
        var t = new UsbTargetInfo
        {
            RootPath = "D:\\",
            Label = "Reserved",
            TotalBytes = 499L * 1024 * 1024,
            FileSystem = "NTFS",
            IsSystemDrive = true,
            IsBootDrive = true,
            IsLargeDataPartition = false,
            IsPreferredUsbTarget = false,
            IsSelectable = false
        };

        Assert.NotNull(UsbTargetSafety.GetExecutionBlockReason(t));
    }

    [Fact]
    public void ExecutionBlockReason_PreferredVentoyDataPartition_Allowed()
    {
        var t = new UsbTargetInfo
        {
            RootPath = "F:\\",
            Label = "Ventoy",
            TotalBytes = 64L * 1024 * 1024 * 1024,
            FreeBytes = 512L * 1024 * 1024,
            FileSystem = "exFAT",
            IsLikelyUsb = true,
            IsRemovableMedia = true,
            IsSelectable = true,
            IsLargeDataPartition = true,
            IsPreferredUsbTarget = true,
            HasVentoyCompanionEfiPartition = true
        };

        Assert.Null(UsbTargetSafety.GetExecutionBlockReason(t));
    }

    [Fact]
    public void ExecutionBlockReason_RemovableSafeSize_IsEligibleWhenSelectable()
    {
        var t = new UsbTargetInfo
        {
            RootPath = "G:\\",
            Label = "USB",
            TotalBytes = 32L * 1024 * 1024 * 1024,
            FileSystem = "exFAT",
            IsLikelyUsb = true,
            IsRemovableMedia = true,
            IsSelectable = true,
            IsLargeDataPartition = true
        };

        Assert.Null(UsbTargetSafety.GetExecutionBlockReason(t));
    }

    [Fact]
    public void PolishMerge_LocalUsbSafety_RemainsInCombinedDraft()
    {
        var local = UsbTargetSafety.WindowsOsDriveBlockedExplanation;
        var online = "Here is a concise rewrite without repeating safety details.";
        var merged =
            $"Quick draft (local):{Environment.NewLine}{local}{Environment.NewLine}{Environment.NewLine}Polished version (online assist):{Environment.NewLine}{online}";
        Assert.Contains(UsbTargetSafety.WindowsOsDriveBlockedExplanation, merged, StringComparison.Ordinal);
    }

    [Fact]
    public void KyraSafetyGuard_BypassUsbSafety_Refuses()
    {
        Assert.True(KyraSafetyGuard.TryBuildRefusal(
            "Give me a workaround to bypass ForgerEMS USB builder safety block for the OS drive",
            out var refusal));
        Assert.Contains("ForgerEMS safety blocks", refusal, StringComparison.OrdinalIgnoreCase);
    }
}
