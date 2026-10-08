#requires -Version 5.1
[CmdletBinding()]
param([switch]$CheckUpdates, [switch]$Offline)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$report = [ordered]@{
    SchemaVersion = 1
    CapturedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    Source = 'Windows CIM, registry, PnPUtil/DISM and Windows Update Agent'
    OperatingSystem = $null
    Services = @()
    Reboot = @{ State = 'Unknown'; Indicators = @() }
    Policy = @{ State = 'Unknown'; Values = @() }
    Bindings = @()
    DeviceProblems = @()
    DriverStore = @{ State = 'UnableToVerify'; Source = ''; Packages = @(); Reason = '' }
    Updates = @{ State = 'NotChecked'; Source = 'Windows Update Agent default policy-selected service'; Items = @(); History = @(); Reason = '' }
    Errors = @()
}
function Add-ProbeError([string]$stage, $errorRecord) {
    $report.Errors += @{ Stage = $stage; Category = $errorRecord.Exception.GetType().Name; HResult = $errorRecord.Exception.HResult }
}
try {
    $os = Get-CimInstance Win32_OperatingSystem
    $version = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    $report.OperatingSystem = @{
        Name = [string]$os.Caption; Version = [string]$os.Version
        Build = [string]$os.BuildNumber; Revision = $version.UBR
        Edition = [string]$version.EditionID; FeatureRelease = [string]$version.DisplayVersion
        Architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        ServicingStatus = 'UnableToVerify'; SupportStatus = 'UnableToVerify'
    }
} catch { Add-ProbeError 'OperatingSystem' $_ }
foreach ($name in @('wuauserv', 'BITS', 'cryptsvc', 'DoSvc')) {
    try {
        $service = Get-Service -Name $name
        $report.Services += @{ Name = $name; Status = $service.Status.ToString(); StartType = $service.StartType.ToString() }
    } catch { Add-ProbeError ('Service:' + $name) $_ }
}
try {
    $indicators = @()
    if (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') { $indicators += 'ComponentServicing' }
    if (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired') { $indicators += 'WindowsUpdate' }
    $session = Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager'
    if ($null -ne $session.PendingFileRenameOperations) { $indicators += 'PendingFileRename' }
    $systemInfo = New-Object -ComObject Microsoft.Update.SystemInfo
    if ($systemInfo.RebootRequired) { $indicators += 'WindowsUpdateAgent' }
    $report.Reboot = @{ State = $(if ($indicators.Count) { 'PendingReboot' } else { 'NoKnownPendingReboot' }); Indicators = $indicators }
} catch { Add-ProbeError 'Reboot' $_ }
try {
    $policyValues = @()
    foreach ($path in @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate', 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU')) {
        if (-not (Test-Path $path)) { continue }
        $values = Get-ItemProperty $path
        foreach ($name in @('UseWUServer','NoAutoUpdate','DisableWindowsUpdateAccess','DoNotConnectToWindowsUpdateInternetLocations','DeferFeatureUpdates','DeferFeatureUpdatesPeriodInDays','DeferQualityUpdatesPeriodInDays','TargetReleaseVersion','TargetReleaseVersionInfo','ProductVersion','ExcludeWUDriversInQualityUpdate')) {
            if ($null -ne $values.$name) { $policyValues += @{ Name = $name; Value = [string]$values.$name } }
        }
    }
    $report.Policy = @{
        State = $(if ($policyValues.Count) { 'PolicyControlled' } else { 'NoListedPolicyDetected' })
        Values = $policyValues
        Completeness = 'Selected visible policies only; MDM, enterprise policy and safeguard holds may not be visible'
    }
} catch { Add-ProbeError 'Policy' $_ }
try {
    $entities = @{}
    foreach ($entity in Get-CimInstance Win32_PnPEntity) {
        $entities[[string]$entity.DeviceID] = $entity
        if ($null -ne $entity.ConfigManagerErrorCode -and $entity.ConfigManagerErrorCode -ne 0) {
            $report.DeviceProblems += @{ Name = [string]$entity.Name; Class = [string]$entity.PNPClass; Code = [int]$entity.ConfigManagerErrorCode }
        }
    }
    $systemDrivers = @{}
    foreach ($driver in Get-CimInstance Win32_SystemDriver) { $systemDrivers[[string]$driver.Name] = $driver }
    foreach ($driver in Get-CimInstance Win32_PnPSignedDriver) {
        $entity = $entities[[string]$driver.DeviceID]
        $service = if ($null -ne $entity) { $systemDrivers[[string]$entity.Service] } else { $null }
        $bootCritical = if ($null -ne $service -and $service.StartMode -eq 'Boot') { $true } else { $null }
        $report.Bindings += @{
            DeviceId = [string]$driver.DeviceID; Name = [string]$driver.DeviceName
            PublishedName = [string]$driver.InfName; Provider = [string]$driver.DriverProviderName
            Class = [string]$driver.DeviceClass; Version = [string]$driver.DriverVersion
            Date = $(if ($null -ne $driver.DriverDate) { $driver.DriverDate.ToUniversalTime().ToString('o') } else { '' })
            IsSigned = $driver.IsSigned; BootCritical = $bootCritical
            HardwareIds = @($entity.HardwareID); CompatibleIds = @($entity.CompatibleID)
            ProblemCode = $entity.ConfigManagerErrorCode
        }
    }
} catch { Add-ProbeError 'DeviceBindings' $_ }
try {
    $scratch = Join-Path ([IO.Path]::GetTempPath()) ('ForgerEMS-driver-inventory-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $scratch | Out-Null
    $xmlPath = Join-Path $scratch 'drivers.xml'
    $pnp = Join-Path $env:SystemRoot 'System32\pnputil.exe'
    $null = & $pnp /enum-drivers /devices /format xml /output-file $xmlPath 2>&1
    if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $xmlPath)) {
        $settings = New-Object System.Xml.XmlReaderSettings
        $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver = $null
        $reader = [System.Xml.XmlReader]::Create($xmlPath, $settings)
        try {
            $xml = New-Object System.Xml.XmlDocument
            $xml.XmlResolver = $null
            $xml.Load($reader)
        } finally { $reader.Dispose() }
        if ($xml.DocumentElement.LocalName -ne 'PnpUtil') { throw 'Unexpected PnPUtil schema' }
        $packages = @()
        foreach ($node in $xml.SelectNodes('/PnpUtil/Driver')) {
            $versionText = [string]$node.DriverVersion
            $packageVersion = ''
            if ($versionText -match '^\d{2}/\d{2}/\d{4}\s+(\d+(?:\.\d+){1,3})$') { $packageVersion = $Matches[1] }
            $packages += @{
                PublishedName = [string]$node.DriverName; OriginalName = [string]$node.OriginalName
                Provider = [string]$node.ProviderName; Class = [string]$node.ClassName
                ClassGuid = [string]$node.ClassGuid; Version = $packageVersion; VersionDateText = $versionText
                Signer = [string]$node.SignerName; SignatureState = 'ReportedSignerNotCryptographicallyVerified'
                BootCritical = $null; DeviceIds = @($node.SelectNodes('Devices/Device') | ForEach-Object { [string]$_.InstanceId })
                Architecture = 'Unknown'; PackageLocation = 'Windows managed Driver Store'
            }
        }
        $report.DriverStore = @{ State = 'Inventoried'; Source = 'PnPUtil structured XML'; Packages = $packages; Reason = 'Third-party packages only; in-box bindings collected separately' }
    } else {
        $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
        if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
            $packages = @(Get-WindowsDriver -Online | ForEach-Object {
                @{ PublishedName = [string]$_.Driver; OriginalName = [IO.Path]::GetFileName($_.OriginalFileName)
                   Provider = [string]$_.ProviderName; Class = [string]$_.ClassName; Version = [string]$_.Version
                   VersionDateText = [string]$_.Date; BootCritical = $_.BootCritical
                   Signer = ''; SignatureState = 'Unknown'; Architecture = 'Unknown'; DeviceIds = @()
                   PackageLocation = [string]$_.OriginalFileName }
            })
            $report.DriverStore = @{ State = 'Inventoried'; Source = 'Windows DISM Get-WindowsDriver'; Packages = $packages; Reason = 'Third-party packages only' }
        } else {
            $report.DriverStore.Reason = 'Structured PnPUtil unavailable on this Windows build. DISM inventory requires administrator permission; no elevation was requested.'
        }
    }
} catch { Add-ProbeError 'DriverStore' $_; $report.DriverStore.Reason = 'Driver Store inventory failed; packages are not assumed safe for removal.' }
finally {
    if ($null -ne $xmlPath -and (Test-Path -LiteralPath $xmlPath)) { Remove-Item -LiteralPath $xmlPath }
    if ($null -ne $scratch -and (Test-Path -LiteralPath $scratch)) { [IO.Directory]::Delete($scratch, $false) }
}

if ($CheckUpdates) {
    try {
        $updateSession = New-Object -ComObject Microsoft.Update.Session
        $updateSession.ClientApplicationID = 'ForgerEMS'
        $searcher = $updateSession.CreateUpdateSearcher()
        $searcher.Online = -not $Offline
        $result = $searcher.Search('IsInstalled=0 and IsHidden=0')
        $updates = @()
        foreach ($update in $result.Updates) {
            $categories = @($update.Categories | ForEach-Object { @{ Id = [string]$_.CategoryID; Name = [string]$_.Name; Type = [string]$_.Type } })
            $feature = @($categories | Where-Object { $_.Id -eq '3689bdc8-b205-4af4-8d4a-a63924c5e9d5' }).Count -gt 0
            $isDriver = [int]$update.Type -eq 2
            $kind = if ($isDriver) { 'Driver' } elseif ($feature) { 'Feature' } elseif ($update.BrowseOnly) { 'Optional' } else { 'Software' }
            $entry = @{
                Id = [string]$update.Identity.UpdateID; Revision = [int]$update.Identity.RevisionNumber
                Title = [string]$update.Title; Kind = $kind; Categories = $categories
                Optional = [bool]$update.BrowseOnly; SizeBytes = [long]$update.MaxDownloadSize
                RebootRequired = [bool]$update.RebootRequired; IsDownloaded = [bool]$update.IsDownloaded
                UserActionRequired = $true; ApplicabilitySource = 'Windows Update Agent policy-selected search'
                HardwareId = ''; Provider = ''; Model = ''; DriverClass = ''
            }
            if ($isDriver) {
                $entry.HardwareId = [string]$update.DriverHardwareID
                $entry.Provider = [string]$update.DriverProvider
                $entry.Model = [string]$update.DriverModel
                $entry.DriverClass = [string]$update.DriverClass
            }
            $updates += $entry
        }
        $history = @()
        $historyCount = [Math]::Min(20, $searcher.GetTotalHistoryCount())
        if ($historyCount -gt 0) {
            foreach ($item in $searcher.QueryHistory(0, $historyCount)) {
                $history += @{ Title = [string]$item.Title; Date = $item.Date.ToUniversalTime().ToString('o'); ResultCode = [int]$item.ResultCode; HResult = [int]$item.HResult }
            }
        }
        $state = if ($Offline) { 'Cached' } elseif ([int]$result.ResultCode -ne 2) { 'UnableToVerify' } elseif ($updates.Count) { 'UpdateAvailable' } else { 'NoApplicableUpdatesOffered' }
        $report.Updates = @{
            State = $state; Source = 'Windows Update Agent default policy-selected service'
            Items = $updates; History = $history; ResultCode = [int]$result.ResultCode
            CheckedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
            Reason = 'No install/download requested. Feature upgrades and OEM driver currency are not inferred from this result.'
        }
    } catch {
        Add-ProbeError 'WindowsUpdateSearch' $_
        $report.Updates.State = 'UnableToVerify'
        $report.Updates.Reason = 'Windows Update Agent search failed; no update/repair success is inferred.'
    }
}
$report | ConvertTo-Json -Depth 12 -Compress
