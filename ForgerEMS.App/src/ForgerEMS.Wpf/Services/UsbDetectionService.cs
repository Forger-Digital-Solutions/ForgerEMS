using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services;

public interface IUsbDetectionService
{
    Task<IReadOnlyList<UsbTargetInfo>> GetUsbTargetsAsync(CancellationToken cancellationToken = default);
}

public sealed class UsbDetectionService : IUsbDetectionService
{
    private readonly IPowerShellRunnerService _powerShellRunnerService;

    public UsbDetectionService(IPowerShellRunnerService powerShellRunnerService)
    {
        _powerShellRunnerService = powerShellRunnerService;
    }

    public async Task<IReadOnlyList<UsbTargetInfo>> GetUsbTargetsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new PowerShellRunRequest
            {
                DisplayName = "USB detection",
                WorkingDirectory = AppContext.BaseDirectory,
                InlineCommand = """
                    $ErrorActionPreference = 'Stop'
                    $items = New-Object System.Collections.Generic.List[object]
                    $diskDrives = @(Get-CimInstance Win32_DiskDrive -ErrorAction SilentlyContinue)
                    $logicalDisks = Get-CimInstance Win32_LogicalDisk | Where-Object { $_.DeviceID -and $_.DriveType -in 2, 3 }

                    foreach ($logicalDisk in $logicalDisks) {
                        $driveLetter = $logicalDisk.DeviceID.TrimEnd(':')
                        $busType = ''
                        $deviceBrand = ''
                        $deviceModel = ''
                        $isSystemDrive = $false
                        $isBootDrive = $false
                        $isSelectable = $true
                        $warning = ''
                        $isRemovableMedia = ($logicalDisk.DriveType -eq 2)

                        try {
                            $partition = Get-Partition -DriveLetter $driveLetter -ErrorAction Stop | Select-Object -First 1
                            $disk = $partition | Get-Disk -ErrorAction Stop | Select-Object -First 1
                            $busType = [string]$disk.BusType
                            $isSystemDrive = [bool]$disk.IsSystem -or [bool]$partition.IsSystem
                            $isBootDrive = [bool]$disk.IsBoot -or [bool]$partition.IsBoot

                            $diskDrive = $diskDrives | Where-Object { [int]$_.Index -eq [int]$disk.Number } | Select-Object -First 1
                            if ($diskDrive) {
                                $deviceBrand = [string]$diskDrive.Manufacturer
                                $deviceModel = [string]$diskDrive.Model
                            }

                            if ([string]::IsNullOrWhiteSpace($deviceModel) -and $disk.FriendlyName) {
                                $deviceModel = [string]$disk.FriendlyName
                            }
                        }
                        catch {
                        }

                        $isLikelyUsb = ($logicalDisk.DriveType -eq 2) -or ($busType -eq 'USB')
                        if (-not $isLikelyUsb) {
                            continue
                        }

                        if ($deviceBrand -eq '(Standard disk drives)') {
                            $deviceBrand = ''
                        }

                        if ($isSystemDrive -or $isBootDrive) {
                            $isSelectable = $false
                            $warning = 'Blocked because Windows reports this volume as a system or boot drive.'
                        }
                        elseif ($logicalDisk.DriveType -eq 3) {
                            $warning = 'USB fixed disk detected. Double-check the drive letter before running Setup, Update, or Ventoy install actions.'
                        }
                        else {
                            $warning = 'Removable USB target detected. Confirm the drive letter before destructive actions.'
                        }

                        $items.Add([pscustomobject]@{
                            DriveLetter      = $driveLetter
                            RootPath         = ($driveLetter + ':\')
                            Label            = [string]$logicalDisk.VolumeName
                            FileSystem       = [string]$logicalDisk.FileSystem
                            TotalBytes       = if ($logicalDisk.Size) { [int64]$logicalDisk.Size } else { 0 }
                            FreeBytes        = if ($logicalDisk.FreeSpace) { [int64]$logicalDisk.FreeSpace } else { 0 }
                            DriveType        = if ($logicalDisk.DriveType -eq 2) { 'Removable' } else { 'Fixed' }
                            BusType          = if ([string]::IsNullOrWhiteSpace($busType)) { 'Unknown' } else { $busType }
                            IsLikelyUsb      = $true
                            DeviceBrand      = $deviceBrand
                            DeviceModel      = $deviceModel
                            SpeedDisplay     = 'Not available'
                            IsSystemDrive    = $isSystemDrive
                            IsBootDrive      = $isBootDrive
                            IsRemovableMedia = $isRemovableMedia
                            IsSelectable     = $isSelectable
                            SelectionWarning = $warning
                        })
                    }

                    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
                    ConvertTo-Json -InputObject @($items) -Depth 3 -Compress
                    """
            };

            var result = await _powerShellRunnerService.RunAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded || string.IsNullOrWhiteSpace(result.StandardOutputText))
            {
                return GetFallbackTargets();
            }

            return ParseTargets(result.StandardOutputText);
        }
        catch
        {
            return GetFallbackTargets();
        }
    }

    private static IReadOnlyList<UsbTargetInfo> ParseTargets(string json)
    {
        using var document = JsonDocument.Parse(json);
        var items = new List<UsbTargetInfo>();

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            items.AddRange(document.RootElement.EnumerateArray().Select(ToUsbTarget));
        }
        else if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            items.Add(ToUsbTarget(document.RootElement));
        }

        return items
            .OrderBy(item => item.RootPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static UsbTargetInfo ToUsbTarget(JsonElement element)
    {
        return new UsbTargetInfo
        {
            DriveLetter = GetString(element, "DriveLetter"),
            RootPath = GetString(element, "RootPath"),
            Label = GetString(element, "Label"),
            FileSystem = GetString(element, "FileSystem"),
            TotalBytes = GetInt64(element, "TotalBytes"),
            FreeBytes = GetInt64(element, "FreeBytes"),
            DriveType = GetString(element, "DriveType"),
            BusType = GetString(element, "BusType"),
            IsLikelyUsb = GetBoolean(element, "IsLikelyUsb"),
            DeviceBrand = GetString(element, "DeviceBrand"),
            DeviceModel = GetString(element, "DeviceModel"),
            SpeedDisplay = GetString(element, "SpeedDisplay", "Not available"),
            IsSystemDrive = GetBoolean(element, "IsSystemDrive"),
            IsBootDrive = GetBoolean(element, "IsBootDrive"),
            IsRemovableMedia = GetBoolean(element, "IsRemovableMedia"),
            IsSelectable = GetBoolean(element, "IsSelectable", defaultValue: true),
            SelectionWarning = GetString(element, "SelectionWarning")
        };
    }

    private static IReadOnlyList<UsbTargetInfo> GetFallbackTargets()
    {
        return DriveInfo.GetDrives()
            .Where(drive => drive.IsReady && drive.DriveType == DriveType.Removable)
            .Select(drive => new UsbTargetInfo
            {
                DriveLetter = drive.Name[..1],
                RootPath = drive.RootDirectory.FullName,
                Label = drive.VolumeLabel,
                FileSystem = drive.DriveFormat,
                TotalBytes = drive.TotalSize,
                FreeBytes = drive.AvailableFreeSpace,
                DriveType = drive.DriveType.ToString(),
                BusType = "Unknown",
                IsLikelyUsb = true,
                DeviceBrand = string.Empty,
                DeviceModel = string.Empty,
                SpeedDisplay = "Not available",
                IsSystemDrive = false,
                IsBootDrive = false,
                IsRemovableMedia = true,
                IsSelectable = true,
                SelectionWarning = "Fallback detection is active. Verify the drive letter before destructive actions."
            })
            .OrderBy(item => item.RootPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetString(JsonElement element, string propertyName, string defaultValue = "")
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.GetString() ?? defaultValue
            : defaultValue;
    }

    private static long GetInt64(JsonElement element, string propertyName, long defaultValue = 0)
    {
        return element.TryGetProperty(propertyName, out var property) && property.TryGetInt64(out var value)
            ? value
            : defaultValue;
    }

    private static bool GetBoolean(JsonElement element, string propertyName, bool defaultValue = false)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : defaultValue;
    }
}
