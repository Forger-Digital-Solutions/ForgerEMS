using System;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services.DriveValidation;

public static class DriveValidationTargetSafety
{
    public const string DestructiveConfirmationPhrase = "ERASE-DRIVE-DATA";

    public const string TempFolderName = ".forgerems-drive-validator";

    public static string? GetBlockReason(UsbTargetInfo? target, DriveValidationOptions options)
    {
        if (options.Mode == DriveValidationMode.DestructiveFullMediaValidation)
        {
            if (!string.Equals(
                    options.DestructiveConfirmationText?.Trim(),
                    DestructiveConfirmationPhrase,
                    StringComparison.Ordinal))
            {
                return
                    "Destructive full media validation requires typing the exact confirmation phrase shown in the UI.";
            }
        }

        var executionBlock = UsbTargetSafety.GetExecutionBlockReason(target);
        if (!string.IsNullOrWhiteSpace(executionBlock))
        {
            return executionBlock;
        }

        if (target is null)
        {
            return "Select a USB target before running Drive Validator.";
        }

        if (UsbTargetSafety.IsProtectedSystemDrive(target))
        {
            return UsbTargetSafety.WindowsOsDriveBlockedExplanation;
        }

        if (IsProtectedVolumeLabel(target))
        {
            return "This volume label suggests a system, recovery, or protected partition. Drive Validator will not run here.";
        }

        if (target.IsSystemDrive && !target.IsRemovableMedia && !target.IsLargeDataPartition && !target.IsPreferredUsbTarget)
        {
            return "Internal system partitions are blocked. Select removable USB media or a recognized Ventoy data volume.";
        }

        if (!target.IsRemovableMedia &&
            !target.IsPreferredUsbTarget &&
            !target.IsLargeDataPartition &&
            !target.HasVentoyStyleLargeDataPartition)
        {
            return "Drive Validator runs only on removable USB volumes or a recognized large Ventoy/USB data partition by default.";
        }

        if (target.FileSystem.Contains("BitLocker", StringComparison.OrdinalIgnoreCase))
        {
            return "BitLocker or encrypted volumes cannot be validated safely from this panel.";
        }

        return null;
    }

    public static bool IsSafeToStart(UsbTargetInfo? target, DriveValidationOptions options, out string blockReason)
    {
        blockReason = GetBlockReason(target, options) ?? string.Empty;
        return string.IsNullOrWhiteSpace(blockReason);
    }

    public static bool IsProtectedVolumeLabel(UsbTargetInfo target)
    {
        var label = target.Label ?? string.Empty;
        if (label.Contains("RECOVERY", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("EFI", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("SYSTEM", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("RESERVED", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("VTOYEFI", StringComparison.OrdinalIgnoreCase))
        {
            return target.IsEfiSystemPartition || target.IsUndersizedPartition || !target.IsLargeDataPartition;
        }

        return false;
    }
}
