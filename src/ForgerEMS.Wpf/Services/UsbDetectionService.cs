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
    Task<UsbDetectionResult> GetUsbTargetsAsync(CancellationToken cancellationToken = default);
}

public sealed class UsbDetectionResult
{
    public IReadOnlyList<UsbTargetInfo> Targets { get; init; } = Array.Empty<UsbTargetInfo>();

    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
}

public sealed class UsbDetectionService : IUsbDetectionService
{
    private readonly IPowerShellRunnerService _powerShellRunnerService;

    public UsbDetectionService(IPowerShellRunnerService powerShellRunnerService)
    {
        _powerShellRunnerService = powerShellRunnerService;
    }

    public async Task<UsbDetectionResult> GetUsbTargetsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new PowerShellRunRequest
            {
                DisplayName = "USB detection",
                WorkingDirectory = AppContext.BaseDirectory,
                InlineCommand = """
                    $ErrorActionPreference = 'Stop'
                    function Escape-WmiValue {
                        param([string]$Value)

                        if ([string]::IsNullOrWhiteSpace($Value)) {
                            return ''
                        }

                        return $Value.Replace('\', '\\').Replace("'", "''")
                    }

                    function Get-AssociatedPartitions {
                        param([string]$LogicalDiskDeviceId)

                        if ([string]::IsNullOrWhiteSpace($LogicalDiskDeviceId)) {
                            return @()
                        }

                        $escapedDeviceId = Escape-WmiValue -Value $LogicalDiskDeviceId
                        return @(Get-CimInstance -Query "ASSOCIATORS OF {Win32_LogicalDisk.DeviceID='$escapedDeviceId'} WHERE AssocClass=Win32_LogicalDiskToPartition" -ErrorAction SilentlyContinue)
                    }

                    function Get-AssociatedDiskDrives {
                        param([string]$PartitionDeviceId)

                        if ([string]::IsNullOrWhiteSpace($PartitionDeviceId)) {
                            return @()
                        }

                        $escapedPartitionId = Escape-WmiValue -Value $PartitionDeviceId
                        return @(Get-CimInstance -Query "ASSOCIATORS OF {Win32_DiskPartition.DeviceID='$escapedPartitionId'} WHERE AssocClass=Win32_DiskDriveToDiskPartition" -ErrorAction SilentlyContinue)
                    }

                    function Get-LogicalDiskByLetter {
                        param(
                            [object[]]$LogicalDisks,
                            [string]$DriveLetter
                        )

                        if ([string]::IsNullOrWhiteSpace($DriveLetter)) {
                            return $null
                        }

                        return $LogicalDisks |
                            Where-Object { $_.DeviceID -and $_.DeviceID.TrimEnd(':') -ieq $DriveLetter.TrimEnd(':') } |
                            Select-Object -First 1
                    }

                    function Format-SizeBytes {
                        param([int64]$Bytes)

                        if ($Bytes -le 0) {
                            return '0 B'
                        }

                        $units = @('B', 'KB', 'MB', 'GB', 'TB')
                        $value = [double]$Bytes
                        $unitIndex = 0
                        while ($value -ge 1024 -and $unitIndex -lt ($units.Count - 1)) {
                            $value /= 1024
                            $unitIndex++
                        }

                        return ('{0:0.#} {1}' -f $value, $units[$unitIndex])
                    }

                    function Get-PartitionTypeSignature {
                        param($Partition)

                        if ($null -eq $Partition) {
                            return ''
                        }

                        $typeParts = @(
                            [string]$Partition.Type,
                            [string]$Partition.GptType,
                            [string]$Partition.MbrType
                        ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

                        if ($typeParts.Count -eq 0) {
                            return ''
                        }

                        return ($typeParts -join ' | ')
                    }

                    function Test-UsbPartitionClassification {
                        param(
                            $Partition,
                            [string]$VolumeLabel,
                            [string]$FileSystem,
                            [int64]$SizeBytes
                        )

                        $partitionType = Get-PartitionTypeSignature -Partition $Partition
                        $normalizedType = if ([string]::IsNullOrWhiteSpace($partitionType)) { '' } else { $partitionType.ToUpperInvariant() }
                        $normalizedLabel = if ([string]::IsNullOrWhiteSpace($VolumeLabel)) { '' } else { $VolumeLabel.ToUpperInvariant() }
                        $normalizedFileSystem = if ([string]::IsNullOrWhiteSpace($FileSystem)) { '' } else { $FileSystem.ToUpperInvariant() }
                        $isUndersized = $SizeBytes -gt 0 -and $SizeBytes -lt 1GB
                        $hasVentoyEfiLabel = $normalizedLabel.Contains('VTOYEFI')
                        $matchesEfiType = $normalizedType -match 'EFI SYSTEM PARTITION|\bEFI\b|\bESP\b|C12A7328-F81F-11D2-BA4B-00A0C93EC93B'
                        $isFatLike = $normalizedFileSystem -match '^FAT'
                        $isEfiSystemPartition = $hasVentoyEfiLabel -or $matchesEfiType -or ($isUndersized -and $isFatLike)

                        [pscustomobject]@{
                            PartitionType         = $partitionType
                            IsUndersized          = $isUndersized
                            HasVentoyEfiLabel     = $hasVentoyEfiLabel
                            MatchesEfiType        = $matchesEfiType
                            IsEfiSystemPartition  = $isEfiSystemPartition
                        }
                    }

                    $items = New-Object System.Collections.Generic.List[object]
                    $diagnostics = New-Object System.Collections.Generic.List[string]
                    $logicalDisks = @(Get-CimInstance Win32_LogicalDisk -ErrorAction SilentlyContinue | Where-Object { $_.DeviceID -and $_.DriveType -in 2, 3 })
                    $systemDriveLetter = ''

                    try {
                        $systemDrive = [string](Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue).SystemDrive
                        if (-not [string]::IsNullOrWhiteSpace($systemDrive)) {
                            $systemDriveLetter = $systemDrive.TrimEnd(':')
                        }
                    }
                    catch {
                    }

                    foreach ($logicalDisk in $logicalDisks) {
                        $driveLetter = $logicalDisk.DeviceID.TrimEnd(':')
                        $busType = 'Unknown'
                        $deviceBrand = ''
                        $deviceModel = ''
                        $pnpDeviceId = ''
                        $partitionType = ''
                        $isSystemDrive = ($driveLetter -ieq $systemDriveLetter)
                        $isBootDrive = $false
                        $isSelectable = $true
                        $warning = ''
                        $isRemovableMedia = ($logicalDisk.DriveType -eq 2)
                        $isEfiSystemPartition = $false
                        $isUndersizedPartition = $false
                        $isLargeDataPartition = $false
                        $isPreferredUsbTarget = $false
                        $hasVentoyCompanionEfiPartition = $false
                        $diskHasLargeDataPartition = $false
                        $volumeLabel = [string]$logicalDisk.VolumeName
                        $fileSystem = [string]$logicalDisk.FileSystem
                        $partitionSizeBytes = if ($logicalDisk.Size) { [int64]$logicalDisk.Size } else { 0 }
                        $classificationDetails = ''

                        try {
                            $partitions = @(Get-AssociatedPartitions -LogicalDiskDeviceId $logicalDisk.DeviceID)
                            $diskDrive = $null
                            $wmiPartitionBoot = $false

                            foreach ($partition in $partitions) {
                                if (-not $wmiPartitionBoot -and ([bool]$partition.BootPartition -or [bool]$partition.Bootable)) {
                                    $wmiPartitionBoot = $true
                                }

                                $associatedDiskDrive = @(Get-AssociatedDiskDrives -PartitionDeviceId ([string]$partition.DeviceID) | Select-Object -First 1)
                                if ($associatedDiskDrive.Count -gt 0 -and $null -eq $diskDrive) {
                                    $diskDrive = $associatedDiskDrive[0]
                                }
                            }

                            if ($diskDrive) {
                                $deviceBrand = [string]$diskDrive.Manufacturer
                                $deviceModel = [string]$diskDrive.Model
                                $pnpDeviceId = [string]$diskDrive.PNPDeviceID

                                $interfaceType = [string]$diskDrive.InterfaceType
                                if (-not [string]::IsNullOrWhiteSpace($interfaceType)) {
                                    $busType = $interfaceType.Trim()
                                }

                                if ([string]::IsNullOrWhiteSpace($deviceModel) -and $diskDrive.Caption) {
                                    $deviceModel = [string]$diskDrive.Caption
                                }
                            }

                            try {
                                $partition = Get-Partition -DriveLetter $driveLetter -ErrorAction Stop | Select-Object -First 1
                                $disk = $partition | Get-Disk -ErrorAction Stop | Select-Object -First 1
                                if ($partition.Size) {
                                    $partitionSizeBytes = [int64]$partition.Size
                                }

                                $isSystemDrive = $isSystemDrive -or [bool]$partition.IsSystem
                                $isBootDrive = $isBootDrive -or [bool]$partition.IsBoot -or $wmiPartitionBoot

                                $partitionAssessment = Test-UsbPartitionClassification -Partition $partition -VolumeLabel $volumeLabel -FileSystem $fileSystem -SizeBytes $partitionSizeBytes
                                $partitionType = [string]$partitionAssessment.PartitionType
                                $isUndersizedPartition = [bool]$partitionAssessment.IsUndersized
                                $isEfiSystemPartition = [bool]$partitionAssessment.IsEfiSystemPartition

                                if ($disk) {
                                    if (-not [string]::IsNullOrWhiteSpace([string]$disk.BusType) -and [string]$disk.BusType -ne 'Unknown') {
                                        $busType = [string]$disk.BusType
                                    }

                                    if (-not [string]::IsNullOrWhiteSpace([string]$disk.FriendlyName) -and [string]::IsNullOrWhiteSpace($deviceModel)) {
                                        $deviceModel = [string]$disk.FriendlyName
                                    }

                                    $diskPartitions = @(Get-Partition -DiskNumber $disk.Number -ErrorAction SilentlyContinue)
                                    foreach ($diskPartition in $diskPartitions) {
                                        $diskPartitionSizeBytes = if ($diskPartition.Size) { [int64]$diskPartition.Size } else { 0 }
                                        $diskPartitionLetter = [string]$diskPartition.DriveLetter
                                        $diskPartitionLogicalDisk = Get-LogicalDiskByLetter -LogicalDisks $logicalDisks -DriveLetter $diskPartitionLetter
                                        $diskPartitionLabel = if ($diskPartitionLogicalDisk) { [string]$diskPartitionLogicalDisk.VolumeName } else { '' }
                                        $diskPartitionFileSystem = if ($diskPartitionLogicalDisk) { [string]$diskPartitionLogicalDisk.FileSystem } else { '' }
                                        $diskPartitionAssessment = Test-UsbPartitionClassification -Partition $diskPartition -VolumeLabel $diskPartitionLabel -FileSystem $diskPartitionFileSystem -SizeBytes $diskPartitionSizeBytes

                                        if ([bool]$diskPartitionAssessment.IsEfiSystemPartition) {
                                            $hasVentoyCompanionEfiPartition = $true
                                        }
                                        elseif ($diskPartitionSizeBytes -ge 10GB) {
                                            $diskHasLargeDataPartition = $true
                                        }
                                    }
                                }
                            }
                            catch {
                                $partitionAssessment = Test-UsbPartitionClassification -Partition $null -VolumeLabel $volumeLabel -FileSystem $fileSystem -SizeBytes $partitionSizeBytes
                                $partitionType = [string]$partitionAssessment.PartitionType
                                $isUndersizedPartition = [bool]$partitionAssessment.IsUndersized
                                $isEfiSystemPartition = [bool]$partitionAssessment.IsEfiSystemPartition
                            }
                        }
                        catch {
                        }

                        if ($deviceBrand -eq '(Standard disk drives)') {
                            $deviceBrand = ''
                        }

                        if ([string]::IsNullOrWhiteSpace($busType) -or $busType -eq 'Unknown') {
                            if ($pnpDeviceId -match 'USBSTOR|VID_[0-9A-F]{4}') {
                                $busType = 'USB'
                            }
                        }

                        if (-not [string]::IsNullOrWhiteSpace($deviceModel)) {
                            $deviceModel = ($deviceModel -replace '\s*USB Device\s*$', '').Trim()
                        }

                        $isSupportedDataFileSystem = $fileSystem -match '^(?i:exFAT|NTFS)$'
                        $isLargeDataPartition = -not $isEfiSystemPartition -and $partitionSizeBytes -ge 4GB -and $isSupportedDataFileSystem
                        $isPreferredUsbTarget = $hasVentoyCompanionEfiPartition -and $diskHasLargeDataPartition -and -not $isEfiSystemPartition -and $partitionSizeBytes -ge 10GB

                        $isLikelyUsb = ($logicalDisk.DriveType -eq 2) -or ($busType -eq 'USB') -or ($pnpDeviceId -match 'USBSTOR|VID_[0-9A-F]{4}')
                        $isProtectedSystemDrive = ($driveLetter -ieq 'C')
                        if (-not $isLikelyUsb) {
                            continue
                        }

                        if ($isProtectedSystemDrive) {
                            $isSelectable = $false
                            $warning = 'Blocked protected Windows system drive. C:\ can never be used by ForgerEMS.'
                        }
                        elseif ($isEfiSystemPartition -or $isUndersizedPartition) {
                            $isSelectable = $false
                            $warning = 'Blocked EFI/boot partition excluded from targeting.'
                        }
                        elseif ($isPreferredUsbTarget) {
                            $warning = 'Preferred Ventoy data partition detected. Windows boot/system flags do not block this target.'
                        }
                        elseif ($isLargeDataPartition) {
                            $warning = 'Large non-EFI USB data partition detected. Windows boot/system flags do not block this target.'
                        }
                        elseif ($logicalDisk.DriveType -eq 3) {
                            $warning = 'USB fixed disk detected. Double-check the drive letter before running Setup, Update, or Ventoy install actions.'
                        }
                        else {
                            $warning = 'USB target detected from live device metadata. Confirm the drive letter before destructive actions.'
                        }

                        $labelForDiagnostics = if ([string]::IsNullOrWhiteSpace($volumeLabel)) { '(no label)' } else { $volumeLabel }
                        $partitionTypeDisplay = if ([string]::IsNullOrWhiteSpace($partitionType)) { 'Unknown' } else { $partitionType }
                        $fileSystemDisplay = if ([string]::IsNullOrWhiteSpace($fileSystem)) { 'Unknown' } else { $fileSystem }
                        $classificationReason =
                            if (-not $isSelectable) {
                                'EFI/system partition'
                            }
                            elseif ($isPreferredUsbTarget) {
                                'Ventoy data partition preferred'
                            }
                            elseif ($isLargeDataPartition) {
                                'large non-EFI data partition'
                            }
                            elseif ($logicalDisk.DriveType -eq 3) {
                                'USB fixed disk allowed with caution'
                            }
                            else {
                                'USB target allowed'
                            }

                        $classificationDetails =
                            ('size={0}; filesystem={1}; IsBoot={2}; IsSystem={3}; partitionType={4}; companionEfi={5}' -f
                                (Format-SizeBytes -Bytes $partitionSizeBytes),
                                $fileSystemDisplay,
                                $isBootDrive,
                                $isSystemDrive,
                                $partitionTypeDisplay,
                                $hasVentoyCompanionEfiPartition)

                        $diagnostics.Add(
                            ('USB target {0}: {1}:\ ({2}) -> {3}; {4}' -f
                                ($(if ($isSelectable) { 'allowed' } else { 'excluded' })),
                                $driveLetter,
                                $labelForDiagnostics,
                                $classificationReason,
                                $classificationDetails))

                        if ($isSelectable) {
                            $items.Add([pscustomobject]@{
                                DriveLetter      = $driveLetter
                                RootPath         = ($driveLetter + ':\')
                                Label            = [string]$logicalDisk.VolumeName
                                FileSystem       = $fileSystem
                                TotalBytes       = if ($logicalDisk.Size) { [int64]$logicalDisk.Size } else { 0 }
                                FreeBytes        = if ($logicalDisk.FreeSpace) { [int64]$logicalDisk.FreeSpace } else { 0 }
                                DriveType        = if ($logicalDisk.DriveType -eq 2) { 'Removable USB' } else { 'Fixed USB' }
                                BusType          = if ([string]::IsNullOrWhiteSpace($busType)) { 'Unknown' } else { $busType }
                                IsLikelyUsb      = $true
                                DeviceBrand      = $deviceBrand
                                DeviceModel      = $deviceModel
                                ReadSpeedDisplay = 'Not tested'
                                WriteSpeedDisplay = 'Not tested'
                                BenchmarkStatus = 'Queued'
                                BenchmarkTestSizeMb = 0
                                BenchmarkLastTestedAt = $null
                                PartitionType    = $partitionType
                                IsSystemDrive    = $isSystemDrive
                                IsBootDrive      = $isBootDrive
                                IsRemovableMedia = $isRemovableMedia
                                IsEfiSystemPartition = $isEfiSystemPartition
                                IsUndersizedPartition = $isUndersizedPartition
                                HasVentoyCompanionEfiPartition = $hasVentoyCompanionEfiPartition
                                IsLargeDataPartition = $isLargeDataPartition
                                IsPreferredUsbTarget = $isPreferredUsbTarget
                                IsSelectable     = $isSelectable
                                SelectionWarning = $warning
                                ClassificationDetails = $classificationDetails
                            })
                        }
                    }

                    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
                    $resultObject = [pscustomobject]@{
                        targets = @($items.ToArray())
                        diagnostics = @($diagnostics.ToArray())
                    }
                    ConvertTo-Json -InputObject $resultObject -Depth 5 -Compress
                    """
            };

            var result = await _powerShellRunnerService.RunAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded || string.IsNullOrWhiteSpace(result.StandardOutputText))
            {
                return GetFallbackTargets();
            }

            var parsedTargets = ParseDetectionResult(result.StandardOutputText);
            return parsedTargets.Targets.Count > 0 || parsedTargets.Diagnostics.Count > 0
                ? parsedTargets
                : GetFallbackTargets();
        }
        catch
        {
            return GetFallbackTargets();
        }
    }

    private static UsbDetectionResult ParseDetectionResult(string json)
    {
        using var document = JsonDocument.Parse(json);
        var items = new List<UsbTargetInfo>();
        var diagnostics = new List<string>();

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            items.AddRange(document.RootElement.EnumerateArray().Select(ToUsbTarget));
        }
        else if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            if (document.RootElement.TryGetProperty("targets", out var targetsElement))
            {
                if (targetsElement.ValueKind == JsonValueKind.Array)
                {
                    items.AddRange(targetsElement.EnumerateArray().Select(ToUsbTarget));
                }
                else if (targetsElement.ValueKind == JsonValueKind.Object)
                {
                    items.Add(ToUsbTarget(targetsElement));
                }
            }
            else
            {
                items.Add(ToUsbTarget(document.RootElement));
            }

            if (document.RootElement.TryGetProperty("diagnostics", out var diagnosticsElement) &&
                diagnosticsElement.ValueKind == JsonValueKind.Array)
            {
                diagnostics.AddRange(
                    diagnosticsElement
                        .EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString())
                        .Where(item => !string.IsNullOrWhiteSpace(item))!
                        .Cast<string>());
            }
        }

        return new UsbDetectionResult
        {
            Targets = items
                .OrderByDescending(item => item.IsSelectable)
                .ThenByDescending(item => item.IsPreferredUsbTarget)
                .ThenByDescending(item => item.HasVentoyCompanionEfiPartition)
                .ThenByDescending(item => item.IsRemovableMedia)
                .ThenByDescending(item => item.TotalBytes)
                .ThenBy(item => item.RootPath, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Diagnostics = diagnostics.ToArray()
        };
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
            ReadSpeedDisplay = GetString(element, "ReadSpeedDisplay", "Not tested"),
            WriteSpeedDisplay = GetString(element, "WriteSpeedDisplay", "Not tested"),
            BenchmarkStatus = GetString(element, "BenchmarkStatus", "Queued"),
            BenchmarkTestSizeMb = GetInt32(element, "BenchmarkTestSizeMb"),
            BenchmarkLastTestedAt = GetDateTimeOffset(element, "BenchmarkLastTestedAt"),
            PartitionType = GetString(element, "PartitionType"),
            IsSystemDrive = GetBoolean(element, "IsSystemDrive"),
            IsBootDrive = GetBoolean(element, "IsBootDrive"),
            IsRemovableMedia = GetBoolean(element, "IsRemovableMedia"),
            IsEfiSystemPartition = GetBoolean(element, "IsEfiSystemPartition"),
            IsUndersizedPartition = GetBoolean(element, "IsUndersizedPartition"),
            HasVentoyCompanionEfiPartition = GetBoolean(element, "HasVentoyCompanionEfiPartition"),
            IsLargeDataPartition = GetBoolean(element, "IsLargeDataPartition"),
            IsPreferredUsbTarget = GetBoolean(element, "IsPreferredUsbTarget"),
            IsSelectable = GetBoolean(element, "IsSelectable", defaultValue: true),
            SelectionWarning = GetString(element, "SelectionWarning"),
            ClassificationDetails = GetString(element, "ClassificationDetails")
        };
    }

    private static UsbDetectionResult GetFallbackTargets()
    {
        var targets = new List<UsbTargetInfo>();
        var diagnostics = new List<string>();

        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady && drive.DriveType == DriveType.Removable))
        {
            var isEfiLikeLabel = drive.VolumeLabel.Contains("VTOYEFI", StringComparison.OrdinalIgnoreCase);
            var isUndersized = drive.TotalSize > 0 && drive.TotalSize < UsbTargetInfo.MinimumTargetBytes;
            var isLargeDataPartition = !isEfiLikeLabel &&
                                       drive.TotalSize >= UsbTargetInfo.LargeDataPartitionBytes &&
                                       (drive.DriveFormat.Equals("exFAT", StringComparison.OrdinalIgnoreCase) ||
                                        drive.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase));
            var isSelectable = !isEfiLikeLabel && !isUndersized;
            var classificationDetails =
                $"size={UsbTargetInfo.FormatBytes(drive.TotalSize)}; filesystem={drive.DriveFormat}; IsBoot=False; IsSystem=False; partitionType=Unknown; companionEfi=Unknown";

            diagnostics.Add(
                $"USB target {(isSelectable ? "allowed" : "excluded")}: {drive.Name} ({(string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "(no label)" : drive.VolumeLabel)}) -> " +
                $"{(isSelectable ? (isLargeDataPartition ? "large non-EFI data partition" : "USB target allowed") : "EFI/system partition")}; {classificationDetails}");

            if (!isSelectable)
            {
                continue;
            }

            targets.Add(new UsbTargetInfo
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
                ReadSpeedDisplay = "Not tested",
                WriteSpeedDisplay = "Not tested",
                BenchmarkStatus = "Queued",
                BenchmarkTestSizeMb = 0,
                BenchmarkLastTestedAt = null,
                PartitionType = string.Empty,
                IsSystemDrive = false,
                IsBootDrive = false,
                IsRemovableMedia = true,
                IsEfiSystemPartition = isEfiLikeLabel,
                IsUndersizedPartition = isUndersized,
                HasVentoyCompanionEfiPartition = false,
                IsLargeDataPartition = isLargeDataPartition,
                IsPreferredUsbTarget = false,
                IsSelectable = true,
                SelectionWarning = "Fallback detection is active. Live USB metadata could not be fully resolved, so verify the drive letter before destructive actions.",
                ClassificationDetails = classificationDetails
            });
        }

        return new UsbDetectionResult
        {
            Targets = targets
                .OrderByDescending(item => item.IsSelectable)
                .ThenByDescending(item => item.IsPreferredUsbTarget)
                .ThenByDescending(item => item.HasVentoyCompanionEfiPartition)
                .ThenByDescending(item => item.IsRemovableMedia)
                .ThenByDescending(item => item.TotalBytes)
                .ThenBy(item => item.RootPath, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Diagnostics = diagnostics.ToArray()
        };
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

    private static int GetInt32(JsonElement element, string propertyName, int defaultValue = 0)
    {
        return element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : defaultValue;
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               DateTimeOffset.TryParse(property.GetString(), out var value)
            ? value
            : null;
    }

    private static bool GetBoolean(JsonElement element, string propertyName, bool defaultValue = false)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : defaultValue;
    }
}
