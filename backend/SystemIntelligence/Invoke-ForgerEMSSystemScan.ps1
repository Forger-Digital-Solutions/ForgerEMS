#requires -Version 5.1

[CmdletBinding()]
param(
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"

function Write-ScanLog {
    param(
        [Parameter(Mandatory)][string]$Message,
        [ValidateSet("INFO", "OK", "WARN", "ERROR")][string]$Level = "INFO"
    )

    Write-Host ("[{0}] {1}" -f $Level, $Message)
}

function Invoke-Optional {
    param(
        [Parameter(Mandatory)][scriptblock]$ScriptBlock,
        [object]$Default = $null
    )

    try {
        return & $ScriptBlock
    }
    catch {
        return $Default
    }
}

function ConvertTo-StatusRank {
    param([string]$Status)

    switch ($Status) {
        "WARNING" { return 4 }
        "WATCH" { return 3 }
        "UNKNOWN" { return 2 }
        "READY" { return 1 }
        default { return 2 }
    }
}

function Get-WorstStatus {
    param([string[]]$Statuses)

    $winner = "READY"
    foreach ($status in $Statuses) {
        if ((ConvertTo-StatusRank -Status $status) -gt (ConvertTo-StatusRank -Status $winner)) {
            $winner = $status
        }
    }

    return $winner
}

function Format-Bytes {
    param([Nullable[double]]$Bytes)

    if ($null -eq $Bytes -or $Bytes -le 0) {
        return "Unknown"
    }

    $units = @("B", "KB", "MB", "GB", "TB", "PB")
    $value = [double]$Bytes
    $unitIndex = 0
    while ($value -ge 1024 -and $unitIndex -lt ($units.Count - 1)) {
        $value = $value / 1024
        $unitIndex++
    }

    return ("{0:N1} {1}" -f $value, $units[$unitIndex])
}

function Add-Recommendation {
    param(
        [System.Collections.Generic.List[string]]$Recommendations,
        [Parameter(Mandatory)][string]$Text
    )

    if (-not $Recommendations.Contains($Text)) {
        [void]$Recommendations.Add($Text)
    }
}

function Add-UniqueText {
    param(
        [System.Collections.Generic.List[string]]$Items,
        [Parameter(Mandatory)][string]$Text
    )

    if (-not $Items.Contains($Text)) {
        [void]$Items.Add($Text)
    }
}

function Get-ProcessorName {
    param($Processor)

    if ($null -eq $Processor) {
        return "Unknown CPU"
    }

    return ([string]$Processor.Name).Trim()
}

function Convert-BytesToGigabytes {
    param([Nullable[double]]$Bytes)

    if ($null -eq $Bytes -or $Bytes -le 0) {
        return 0
    }

    return [math]::Round(([double]$Bytes / 1GB), 1)
}

function Get-PricingProviders {
    return @(
        [ordered]@{
            name = "eBay sold listings"
            key = "ebaySoldListings"
            configured = $false
            status = "Pricing provider not configured"
            notes = "Provider interface reserved for future API-backed sold-listing comps."
        },
        [ordered]@{
            name = "OfferUp"
            key = "offerUp"
            configured = $false
            status = "Pricing provider not configured"
            notes = "Provider interface reserved for future local marketplace comps."
        },
        [ordered]@{
            name = "Facebook Marketplace"
            key = "facebookMarketplace"
            configured = $false
            status = "Pricing provider not configured"
            notes = "Provider interface reserved for future marketplace comps."
        },
        [ordered]@{
            name = "Generic web price provider"
            key = "genericWeb"
            configured = $false
            status = "Pricing provider not configured"
            notes = "Optional future online provider hook. Disabled by default."
        }
    )
}

function New-FlipValueReport {
    param(
        [object]$ComputerSystem,
        [object]$Processor,
        [Nullable[double]]$TotalMemoryBytes,
        [object[]]$Gpus,
        [object[]]$DiskReports,
        [object[]]$BatteryReports,
        [object[]]$Problems
    )

    $valueDrivers = New-Object System.Collections.Generic.List[string]
    $valueReducers = New-Object System.Collections.Generic.List[string]
    $upgradeRecommendations = New-Object System.Collections.Generic.List[string]

    $base = 110
    $cpuName = Get-ProcessorName -Processor $Processor
    if ($cpuName -match '(?i)\bi[79]-|Ryzen\s+[79]|Xeon|Core\(TM\)\s+Ultra\s+[79]') {
        $base += 220
        Add-UniqueText -Items $valueDrivers -Text "Higher-tier CPU improves resale demand."
    }
    elseif ($cpuName -match '(?i)\bi5-|Ryzen\s+5|Core\(TM\)\s+Ultra\s+5') {
        $base += 130
        Add-UniqueText -Items $valueDrivers -Text "Midrange CPU is attractive for general resale."
    }
    elseif ($cpuName -match '(?i)\bi3-|Ryzen\s+3|Pentium|Celeron|Athlon') {
        $base += 45
        Add-UniqueText -Items $valueReducers -Text "Entry-level CPU limits top-end resale."
    }
    else {
        $base += 75
    }

    $ramGb = Convert-BytesToGigabytes -Bytes $TotalMemoryBytes
    if ($ramGb -ge 32) {
        $base += 120
        Add-UniqueText -Items $valueDrivers -Text "32 GB or more RAM helps premium listing appeal."
    }
    elseif ($ramGb -ge 16) {
        $base += 70
        Add-UniqueText -Items $valueDrivers -Text "16 GB RAM meets a strong resale baseline."
    }
    elseif ($ramGb -gt 0 -and $ramGb -lt 16) {
        $base -= 35
        Add-UniqueText -Items $valueReducers -Text "Less than 16 GB RAM reduces resale appeal."
        Add-UniqueText -Items $upgradeRecommendations -Text "Upgrade to at least 16 GB RAM before selling if the platform supports it."
    }

    $primaryDisk = @($DiskReports | Sort-Object @{ Expression = { if ($_.size -match 'TB') { 2 } else { 1 } }; Descending = $true } | Select-Object -First 1)
    if ($primaryDisk.Count -gt 0) {
        $disk = $primaryDisk[0]
        if ([string]$disk.mediaType -match '(?i)SSD|NVMe') {
            $base += 90
            Add-UniqueText -Items $valueDrivers -Text "SSD/NVMe storage improves perceived speed and resale value."
        }
        else {
            $base -= 45
            Add-UniqueText -Items $valueReducers -Text "Spinning or unknown storage lowers buyer confidence."
            Add-UniqueText -Items $upgradeRecommendations -Text "Install a known-good SSD and rerun SMART checks before listing."
        }

        if ([string]$disk.status -in @("WARNING", "WATCH") -or [string]$disk.health -notin @("Healthy", "OK", "")) {
            $base -= 85
            Add-UniqueText -Items $valueReducers -Text "Storage health warning materially reduces resale value."
            Add-UniqueText -Items $upgradeRecommendations -Text "Replace questionable storage before selling or list as parts/repair."
        }
    }
    else {
        $base -= 40
        Add-UniqueText -Items $valueReducers -Text "Storage health is unknown."
        Add-UniqueText -Items $upgradeRecommendations -Text "Run elevated SMART/storage diagnostics before pricing."
    }

    $dgpu = @($Gpus | Where-Object { $_.Name -match '(?i)NVIDIA|GeForce|RTX|GTX|Quadro|AMD Radeon|RX\s|Arc' -and $_.Name -notmatch '(?i)Intel\(R\)|UHD|Iris' })
    if ($dgpu.Count -gt 0) {
        $base += 120
        Add-UniqueText -Items $valueDrivers -Text "Dedicated GPU adds resale upside for creator/gaming buyers."
    }

    if ($BatteryReports.Count -gt 0) {
        foreach ($battery in $BatteryReports) {
            if ($null -ne $battery.wearPercent -and [double]$battery.wearPercent -ge 35) {
                $base -= 60
                Add-UniqueText -Items $valueReducers -Text "High battery wear affects laptop resale value."
                Add-UniqueText -Items $upgradeRecommendations -Text "Replace the battery or disclose wear clearly in the listing."
            }
        }
    }

    foreach ($problem in $Problems) {
        $base -= 20
        Add-UniqueText -Items $valueReducers -Text ([string]$problem)
    }

    $base = [math]::Max(45, [math]::Round($base / 5) * 5)
    $low = [math]::Max(35, [math]::Round(($base * 0.82) / 5) * 5)
    $high = [math]::Round(($base * 1.18) / 5) * 5
    $quick = [math]::Max(30, [math]::Round(($base * 0.72) / 5) * 5)
    $parts = [math]::Max(20, [math]::Round(($base * 0.38) / 5) * 5)
    $confidence = 0.52
    if ($null -ne $Processor) { $confidence += 0.08 }
    if ($TotalMemoryBytes -gt 0) { $confidence += 0.08 }
    if ($DiskReports.Count -gt 0) { $confidence += 0.08 }
    if ($BatteryReports.Count -gt 0) { $confidence += 0.04 }
    $confidence = [math]::Min(0.8, [math]::Round($confidence, 2))

    if ($valueDrivers.Count -eq 0) {
        Add-UniqueText -Items $valueDrivers -Text "Baseline system profile is complete enough for local pricing."
    }
    if ($valueReducers.Count -eq 0) {
        Add-UniqueText -Items $valueReducers -Text "No major resale reducers were detected locally."
    }
    if ($upgradeRecommendations.Count -eq 0) {
        Add-UniqueText -Items $upgradeRecommendations -Text "Clean install, update Windows, verify drivers, and include charger/photos before listing."
    }

    $manufacturer = if ($null -ne $ComputerSystem) { [string]$ComputerSystem.Manufacturer } else { "" }
    $model = if ($null -ne $ComputerSystem) { [string]$ComputerSystem.Model } else { "" }
    $titleParts = @($manufacturer, $model, $cpuName, ("{0:g}GB RAM" -f $ramGb)) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_ -ne "0GB RAM" }
    $title = ($titleParts -join " ").Trim()
    if ([string]::IsNullOrWhiteSpace($title)) {
        $title = "Windows laptop - tested by ForgerEMS"
    }

    return [ordered]@{
        estimateType = "local estimate only"
        providerStatus = "Pricing provider not configured"
        estimatedResaleRange = ('$' + $low + ' - $' + $high)
        recommendedListPrice = ('$' + $high)
        quickSalePrice = ('$' + $quick)
        partsRepairPrice = ('$' + $parts)
        confidenceScore = $confidence
        valueDrivers = @($valueDrivers)
        valueReducers = @($valueReducers)
        suggestedListingTitle = $title
        suggestedListingDescription = "Local ForgerEMS estimate only. Include exact condition, photos, battery/storage health, charger status, Windows activation state, and any defects. Marketplace comps are not configured yet."
        suggestedUpgradeRecommendations = @($upgradeRecommendations)
        pricingProviders = @(Get-PricingProviders)
    }
}

function Format-DateValue {
    param([object]$Value)

    if ($null -eq $Value) {
        return "UNKNOWN"
    }

    try {
        if ($Value -is [datetime]) {
            return $Value.ToString("yyyy-MM-dd HH:mm:ss")
        }

        return ([System.Management.ManagementDateTimeConverter]::ToDateTime([string]$Value)).ToString("yyyy-MM-dd HH:mm:ss")
    }
    catch {
        return [string]$Value
    }
}

function Format-TimeSpanValue {
    param([timespan]$Value)

    if ($null -eq $Value) {
        return "UNKNOWN"
    }

    return ("{0}d {1}h {2}m" -f [int]$Value.TotalDays, $Value.Hours, $Value.Minutes)
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $localAppData = [Environment]::GetFolderPath("LocalApplicationData")
    if ([string]::IsNullOrWhiteSpace($localAppData)) {
        $localAppData = [IO.Path]::GetTempPath()
    }

    $OutputDirectory = Join-Path $localAppData "ForgerEMS\Runtime\reports"
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$jsonPath = Join-Path $OutputDirectory "system-intelligence-latest.json"
$markdownPath = Join-Path $OutputDirectory "flip-report-latest.md"
$recommendations = New-Object System.Collections.Generic.List[string]
$obviousProblems = New-Object System.Collections.Generic.List[string]

Write-ScanLog "ForgerEMS System Intelligence scan started."
Write-ScanLog "Collecting OS, CPU, RAM, GPU, disk, battery, network, and security data."

$computerSystem = Invoke-Optional { Get-CimInstance -ClassName Win32_ComputerSystem -ErrorAction Stop }
$operatingSystem = Invoke-Optional { Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction Stop }
$bios = Invoke-Optional { Get-CimInstance -ClassName Win32_BIOS -ErrorAction Stop }
$tpm = Invoke-Optional { Get-Tpm -ErrorAction Stop }
$secureBoot = Invoke-Optional { Confirm-SecureBootUEFI -ErrorAction Stop }
$processor = Invoke-Optional { Get-CimInstance -ClassName Win32_Processor -ErrorAction Stop | Select-Object -First 1 }
$gpus = @(Invoke-Optional { Get-CimInstance -ClassName Win32_VideoController -ErrorAction Stop } @())
$batteries = @(Invoke-Optional { Get-CimInstance -ClassName Win32_Battery -ErrorAction Stop } @())
$batteryStaticData = @(Invoke-Optional { Get-CimInstance -Namespace "root\wmi" -ClassName BatteryStaticData -ErrorAction Stop } @())
$batteryFullChargedCapacity = @(Invoke-Optional { Get-CimInstance -Namespace "root\wmi" -ClassName BatteryFullChargedCapacity -ErrorAction Stop } @())
$batteryCycleCount = @(Invoke-Optional { Get-CimInstance -Namespace "root\wmi" -ClassName BatteryCycleCount -ErrorAction Stop } @())
$networkAdapters = @(Invoke-Optional { Get-CimInstance -ClassName Win32_NetworkAdapterConfiguration -Filter "IPEnabled = True" -ErrorAction Stop } @())
$netAdapters = @(Invoke-Optional { Get-NetAdapter -ErrorAction Stop } @())
$physicalDisks = @(Invoke-Optional { Get-PhysicalDisk -ErrorAction Stop } @())
$smartPredictFailures = @(Invoke-Optional { Get-CimInstance -Namespace "root\wmi" -ClassName MSStorageDriver_FailurePredictStatus -ErrorAction Stop } @())
$logicalDisks = @(Invoke-Optional { Get-CimInstance -ClassName Win32_LogicalDisk -Filter "DriveType = 3" -ErrorAction Stop } @())
$memoryModules = @(Invoke-Optional { Get-CimInstance -ClassName Win32_PhysicalMemory -ErrorAction Stop } @())
$memoryArrays = @(Invoke-Optional { Get-CimInstance -ClassName Win32_PhysicalMemoryArray -ErrorAction Stop } @())
$displays = @(Invoke-Optional { Get-CimInstance -ClassName Win32_DesktopMonitor -ErrorAction Stop } @())
$bitLockerVolumes = @(Invoke-Optional { Get-BitLockerVolume -ErrorAction Stop } @())
$licenseProduct = Invoke-Optional {
    Get-CimInstance -ClassName SoftwareLicensingProduct -ErrorAction Stop |
        Where-Object { $_.PartialProductKey -and $_.Name -match 'Windows' } |
        Select-Object -First 1
}
$wifiInterfaceText = Invoke-Optional { netsh wlan show interfaces 2>$null | Out-String } ""

$lastBoot = Invoke-Optional {
    if ($null -ne $operatingSystem -and $null -ne $operatingSystem.LastBootUpTime) {
        if ($operatingSystem.LastBootUpTime -is [datetime]) {
            return $operatingSystem.LastBootUpTime
        }

        return [System.Management.ManagementDateTimeConverter]::ToDateTime([string]$operatingSystem.LastBootUpTime)
    }

    return $null
}
$uptime = if ($null -ne $lastBoot) { New-TimeSpan -Start $lastBoot -End (Get-Date) } else { $null }
$biosReleaseDate = Invoke-Optional {
    if ($null -ne $bios -and $null -ne $bios.ReleaseDate) {
        if ($bios.ReleaseDate -is [datetime]) { return $bios.ReleaseDate }
        return [System.Management.ManagementDateTimeConverter]::ToDateTime([string]$bios.ReleaseDate)
    }
    return $null
}
if ($null -ne $biosReleaseDate -and $biosReleaseDate -lt (Get-Date).AddYears(-3)) {
    Add-Recommendation -Recommendations $recommendations -Text ("BIOS appears older than three years ({0}). Check the vendor support site before resale or Windows 11 setup." -f $biosReleaseDate.ToString("yyyy-MM-dd"))
    Add-UniqueText -Items $obviousProblems -Text ("BIOS may be outdated ({0})." -f $biosReleaseDate.ToString("yyyy-MM-dd"))
}

$totalMemoryBytes = if ($null -ne $computerSystem) { [double]$computerSystem.TotalPhysicalMemory } else { $null }
$freeMemoryBytes = if ($null -ne $operatingSystem) { [double]$operatingSystem.FreePhysicalMemory * 1KB } else { $null }
$usedMemoryBytes = if ($null -ne $totalMemoryBytes -and $null -ne $freeMemoryBytes) { $totalMemoryBytes - $freeMemoryBytes } else { $null }
$usedMemoryPercent = if ($null -ne $totalMemoryBytes -and $totalMemoryBytes -gt 0 -and $null -ne $usedMemoryBytes) { [math]::Round(($usedMemoryBytes / $totalMemoryBytes) * 100, 1) } else { $null }
$memorySpeeds = @($memoryModules | Where-Object { $_.Speed } | ForEach-Object { $_.Speed } | Select-Object -Unique)
$memorySlotsTotal = @($memoryArrays | Where-Object { $_.MemoryDevices } | Select-Object -First 1).MemoryDevices
$memorySlotsUsed = @($memoryModules | Where-Object { $_.Capacity -gt 0 }).Count
$memorySlotsFree = if ($null -ne $memorySlotsTotal -and [int]$memorySlotsTotal -ge $memorySlotsUsed) { [int]$memorySlotsTotal - $memorySlotsUsed } else { $null }
$memoryUpgradePath = if ($null -ne $memorySlotsFree -and $memorySlotsFree -gt 0) {
    "{0} free RAM slot(s) detected; upgrade may be possible." -f $memorySlotsFree
}
elseif ($memorySlotsUsed -gt 0) {
    "All detected RAM slots are populated; upgrade may require replacing modules."
}
else {
    "RAM upgrade path could not be detected."
}
$ramStatus = "UNKNOWN"
if ($null -ne $totalMemoryBytes -and $totalMemoryBytes -gt 0 -and $null -ne $freeMemoryBytes) {
    $freePercent = [math]::Round(($freeMemoryBytes / $totalMemoryBytes) * 100, 1)
    if ($freePercent -lt 10) {
        $ramStatus = "WARNING"
        Add-Recommendation -Recommendations $recommendations -Text "Available RAM is low. Close heavy applications or plan a memory upgrade if this is typical."
    }
    elseif ($freePercent -lt 20) {
        $ramStatus = "WATCH"
        Add-Recommendation -Recommendations $recommendations -Text "Available RAM is below 20 percent. Watch performance during technician workloads."
    }
    else {
        $ramStatus = "READY"
    }
}

$osStatus = if ($null -eq $operatingSystem) { "UNKNOWN" } else { "READY" }
if ($osStatus -eq "UNKNOWN") {
    Add-Recommendation -Recommendations $recommendations -Text "OS inventory could not be read. Run the scan from an elevated Windows PowerShell session if details are missing."
}
if ($secureBoot -eq $false) {
    Add-Recommendation -Recommendations $recommendations -Text "Secure Boot is disabled. Confirm this is intentional before trusting boot-chain security."
    Add-UniqueText -Items $obviousProblems -Text "Secure Boot is disabled."
}
if ($null -ne $tpm -and (-not $tpm.TpmPresent -or -not $tpm.TpmReady)) {
    Add-Recommendation -Recommendations $recommendations -Text "TPM is missing or not ready. Review device security and BitLocker readiness."
    Add-UniqueText -Items $obviousProblems -Text "TPM is missing or not ready."
}

$gpuStatus = if ($gpus.Count -gt 0) { "READY" } else { "UNKNOWN" }
if ($gpuStatus -eq "UNKNOWN") {
    Add-Recommendation -Recommendations $recommendations -Text "GPU inventory was not detected through WMI."
}

Write-ScanLog "Checking physical disk health."
$diskReports = @()
foreach ($disk in $physicalDisks) {
    $reliability = Invoke-Optional { $disk | Get-StorageReliabilityCounter -ErrorAction Stop }
    $diskStatus = "READY"
    $health = [string]$disk.HealthStatus
    $operational = [string]($disk.OperationalStatus -join ", ")
    $temperature = if ($null -ne $reliability) { $reliability.Temperature } else { $null }
    $wear = if ($null -ne $reliability) { $reliability.Wear } else { $null }
    $readErrors = if ($null -ne $reliability) { $reliability.ReadErrorsTotal } else { $null }
    $writeErrors = if ($null -ne $reliability) { $reliability.WriteErrorsTotal } else { $null }

    if ($health -and $health -notin @("Healthy", "OK")) {
        $diskStatus = "WARNING"
        Add-Recommendation -Recommendations $recommendations -Text ("Review disk '{0}' immediately. Windows reports health as {1}." -f $disk.FriendlyName, $health)
        Add-UniqueText -Items $obviousProblems -Text ("Storage health issue on {0}: {1}." -f $disk.FriendlyName, $health)
    }
    elseif ($operational -and $operational -notmatch "OK") {
        $diskStatus = "WATCH"
        Add-Recommendation -Recommendations $recommendations -Text ("Review disk '{0}'. Operational status is {1}." -f $disk.FriendlyName, $operational)
    }

    if ($null -ne $temperature -and $temperature -ge 60) {
        $diskStatus = "WARNING"
        Add-Recommendation -Recommendations $recommendations -Text ("Disk '{0}' is hot at {1} C. Check airflow and workload." -f $disk.FriendlyName, $temperature)
        Add-UniqueText -Items $obviousProblems -Text ("Storage temperature is high on {0}: {1} C." -f $disk.FriendlyName, $temperature)
    }
    elseif ($null -ne $temperature -and $temperature -ge 50 -and $diskStatus -eq "READY") {
        $diskStatus = "WATCH"
        Add-Recommendation -Recommendations $recommendations -Text ("Disk '{0}' is warm at {1} C. Watch cooling under load." -f $disk.FriendlyName, $temperature)
    }

    if ($null -ne $wear -and $wear -ge 80) {
        $diskStatus = "WATCH"
        Add-Recommendation -Recommendations $recommendations -Text ("Disk '{0}' reports {1}% wear. Plan replacement before heavy field use." -f $disk.FriendlyName, $wear)
        Add-UniqueText -Items $obviousProblems -Text ("Storage wear is elevated on {0}: {1}%." -f $disk.FriendlyName, $wear)
    }

    $diskReports += [ordered]@{
        name = [string]$disk.FriendlyName
        serialNumber = [string]$disk.SerialNumber
        interfaceType = [string]$disk.BusType
        mediaType = [string]$disk.MediaType
        size = Format-Bytes -Bytes ([double]$disk.Size)
        health = $health
        operationalStatus = $operational
        temperatureC = $temperature
        wearPercent = $wear
        readErrorsTotal = $readErrors
        writeErrorsTotal = $writeErrors
        status = $diskStatus
    }
}

$volumeReports = @()
foreach ($logicalDisk in $logicalDisks) {
    $freePercent = if ($logicalDisk.Size -gt 0) { [math]::Round(([double]$logicalDisk.FreeSpace / [double]$logicalDisk.Size) * 100, 1) } else { $null }
    $volumeStatus = if ($null -eq $freePercent) { "UNKNOWN" } elseif ($freePercent -lt 10) { "WARNING" } elseif ($freePercent -lt 20) { "WATCH" } else { "READY" }
    if ($volumeStatus -eq "WARNING") {
        Add-Recommendation -Recommendations $recommendations -Text ("Volume {0} is below 10% free space. Free space before downloads, updates, or imaging work." -f $logicalDisk.DeviceID)
    }
    elseif ($volumeStatus -eq "WATCH") {
        Add-Recommendation -Recommendations $recommendations -Text ("Volume {0} is below 20% free space. Watch capacity during technician work." -f $logicalDisk.DeviceID)
    }

    $volumeReports += [ordered]@{
        drive = [string]$logicalDisk.DeviceID
        label = [string]$logicalDisk.VolumeName
        fileSystem = [string]$logicalDisk.FileSystem
        size = Format-Bytes -Bytes ([double]$logicalDisk.Size)
        free = Format-Bytes -Bytes ([double]$logicalDisk.FreeSpace)
        freePercent = $freePercent
        status = $volumeStatus
    }
}

if ($diskReports.Count -eq 0) {
    Add-Recommendation -Recommendations $recommendations -Text "Physical disk health counters were unavailable. Run elevated if disk detail is required."
}
$diskStatusInputs = @($diskReports | ForEach-Object { $_.status }) + @($volumeReports | ForEach-Object { $_.status })
$diskOverallStatus = if ($diskStatusInputs.Count -eq 0) { "UNKNOWN" } else { Get-WorstStatus -Statuses $diskStatusInputs }

Write-ScanLog "Checking battery state."
$batteryReports = @()
foreach ($battery in $batteries) {
    $batteryStatus = "READY"
    $charge = $battery.EstimatedChargeRemaining
    $designCapacity = if ($battery.PSObject.Properties.Name -contains "DesignCapacity") { $battery.DesignCapacity } else { $null }
    $fullChargeCapacity = if ($battery.PSObject.Properties.Name -contains "FullChargeCapacity") { $battery.FullChargeCapacity } else { $null }
    if ($null -eq $designCapacity -and $batteryStaticData.Count -gt 0) {
        $designCapacity = ($batteryStaticData | Select-Object -First 1).DesignedCapacity
    }
    if ($null -eq $fullChargeCapacity -and $batteryFullChargedCapacity.Count -gt 0) {
        $fullChargeCapacity = ($batteryFullChargedCapacity | Select-Object -First 1).FullChargedCapacity
    }
    $cycleCount = if ($battery.PSObject.Properties.Name -contains "CycleCount") { $battery.CycleCount } else { $null }
    if ($null -eq $cycleCount -and $batteryCycleCount.Count -gt 0) {
        $cycleCount = ($batteryCycleCount | Select-Object -First 1).CycleCount
    }
    $wearPercent = if ($null -ne $designCapacity -and [double]$designCapacity -gt 0 -and $null -ne $fullChargeCapacity) {
        [math]::Round((1 - ([double]$fullChargeCapacity / [double]$designCapacity)) * 100, 1)
    }
    else {
        $null
    }

    if ($null -ne $charge -and $charge -lt 15) {
        $batteryStatus = "WARNING"
        Add-Recommendation -Recommendations $recommendations -Text "Battery charge is critically low. Connect AC power before long scans or USB build operations."
    }
    elseif ($null -ne $charge -and $charge -lt 30) {
        $batteryStatus = "WATCH"
        Add-Recommendation -Recommendations $recommendations -Text "Battery charge is low. Connect AC power before technician work."
    }
    if ($null -ne $wearPercent -and $wearPercent -ge 35) {
        $batteryStatus = "WATCH"
        Add-Recommendation -Recommendations $recommendations -Text ("Battery wear is high at {0}%. Plan a battery replacement if runtime matters." -f $wearPercent)
        Add-UniqueText -Items $obviousProblems -Text ("Battery health is reduced: {0}% wear." -f $wearPercent)
    }

    $batteryReports += [ordered]@{
        name = [string]$battery.Name
        estimatedChargeRemaining = $charge
        designCapacity = $designCapacity
        fullChargeCapacity = $fullChargeCapacity
        wearPercent = $wearPercent
        cycleCount = $cycleCount
        acConnected = if ($null -ne $battery.BatteryStatus) { $battery.BatteryStatus -in @(2, 6, 7, 8, 9) } else { $null }
        batteryStatusCode = $battery.BatteryStatus
        status = $batteryStatus
    }
}
$batteryOverallStatus = if ($batteryReports.Count -eq 0) { "UNKNOWN" } else { Get-WorstStatus -Statuses @($batteryReports | ForEach-Object { $_.status }) }

Write-ScanLog "Checking network adapters."
$networkStatus = if ($networkAdapters.Count -gt 0) { "READY" } else { "WATCH" }
if ($networkAdapters.Count -eq 0) {
    Add-Recommendation -Recommendations $recommendations -Text "No active IP-enabled network adapter was detected."
}
$wifiSignal = $null
if (-not [string]::IsNullOrWhiteSpace($wifiInterfaceText) -and $wifiInterfaceText -match "Signal\s+:\s+([0-9]+)%") {
    $wifiSignal = [int]$Matches[1]
}
$networkReport = @($networkAdapters | ForEach-Object {
    $configAdapter = $_
    $ipAddresses = @($_.IPAddress)
    $gateways = @($_.DefaultIPGateway)
    $matchingNetAdapter = $netAdapters |
        Where-Object {
            ($_.MacAddress -and $_.MacAddress.Replace('-', ':') -ieq ([string]$configAdapter.MACAddress)) -or
            ($_.InterfaceDescription -and $_.InterfaceDescription -eq $configAdapter.Description)
        } |
        Select-Object -First 1
    $hasApipa = @($ipAddresses | Where-Object { $_ -like "169.254.*" }).Count -gt 0
    $hasGateway = @($gateways | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }).Count -gt 0
    if ($hasApipa -or -not $hasGateway) {
        $script:networkStatus = if ($script:networkStatus -eq "READY") { "WATCH" } else { $script:networkStatus }
        if ($hasApipa) {
            Add-Recommendation -Recommendations $recommendations -Text ("Adapter '{0}' has an APIPA address. Check DHCP, cable, Wi-Fi association, or static IP settings." -f $_.Description)
            Add-UniqueText -Items $obviousProblems -Text ("Network adapter '{0}' has an APIPA address." -f $_.Description)
        }
        if (-not $hasGateway) {
            Add-Recommendation -Recommendations $recommendations -Text ("Adapter '{0}' has no default gateway. Internet and update downloads may fail." -f $_.Description)
            Add-UniqueText -Items $obviousProblems -Text ("Network adapter '{0}' has no default gateway." -f $_.Description)
        }
    }

    [ordered]@{
        description = [string]$_.Description
        name = if ($matchingNetAdapter) { [string]$matchingNetAdapter.Name } else { [string]$_.Description }
        macAddress = [string]$_.MACAddress
        linkSpeed = if ($matchingNetAdapter) { [string]$matchingNetAdapter.LinkSpeed } else { "" }
        driverInterface = if ($matchingNetAdapter) { [string]$matchingNetAdapter.InterfaceDescription } else { "" }
        dhcpEnabled = [bool]$_.DHCPEnabled
        ipAddresses = $ipAddresses
        gateways = $gateways
        dnsServers = @($_.DNSServerSearchOrder)
        apipaDetected = $hasApipa
        gatewayPresent = $hasGateway
        wifiSignalPercent = $wifiSignal
    }
})
$internetCheck = Invoke-Optional { Test-NetConnection -ComputerName "1.1.1.1" -Port 443 -InformationLevel Quiet -WarningAction SilentlyContinue -ErrorAction Stop } $false
if (-not $internetCheck) {
    $networkStatus = if ($networkStatus -eq "READY") { "WATCH" } else { $networkStatus }
    Add-Recommendation -Recommendations $recommendations -Text "Internet connectivity check did not pass against 1.1.1.1:443. Confirm network before downloads."
    Add-UniqueText -Items $obviousProblems -Text "Internet connectivity check failed."
}

Write-ScanLog "Checking Defender and registered antivirus state."
$defender = Invoke-Optional { Get-MpComputerStatus -ErrorAction Stop }
$avProducts = @(Invoke-Optional { Get-CimInstance -Namespace "root\SecurityCenter2" -ClassName AntiVirusProduct -ErrorAction Stop } @())
$firewallProfiles = @(Invoke-Optional { Get-NetFirewallProfile -ErrorAction Stop } @())
$firewallEnabled = if ($firewallProfiles.Count -gt 0) { -not ($firewallProfiles | Where-Object { -not $_.Enabled }) } else { $null }
$securityStatus = "UNKNOWN"
if ($null -ne $defender) {
    if ($defender.AntivirusEnabled -and $defender.RealTimeProtectionEnabled) {
        $securityStatus = "READY"
    }
    elseif ($defender.AntivirusEnabled -or $defender.RealTimeProtectionEnabled) {
        $securityStatus = "WATCH"
        Add-Recommendation -Recommendations $recommendations -Text "Defender is partially enabled. Confirm real-time protection before remediation work."
    }
    else {
        $securityStatus = "WARNING"
        Add-Recommendation -Recommendations $recommendations -Text "Defender real-time protection appears disabled. Confirm security posture before connecting customer media."
        Add-UniqueText -Items $obviousProblems -Text "Defender real-time protection appears disabled."
    }
}
elseif ($avProducts.Count -gt 0) {
    $securityStatus = "WATCH"
    Add-Recommendation -Recommendations $recommendations -Text "Third-party antivirus is registered, but Defender status could not be read."
}
else {
    Add-Recommendation -Recommendations $recommendations -Text "Security provider status could not be determined."
}

if ($firewallEnabled -eq $false) {
    $securityStatus = if ($securityStatus -eq "WARNING") { "WARNING" } else { "WATCH" }
    Add-Recommendation -Recommendations $recommendations -Text "One or more Windows Firewall profiles are disabled."
    Add-UniqueText -Items $obviousProblems -Text "One or more Windows Firewall profiles are disabled."
}
$bitLockerReport = @($bitLockerVolumes | ForEach-Object {
    [ordered]@{
        mountPoint = [string]$_.MountPoint
        volumeStatus = [string]$_.VolumeStatus
        protectionStatus = [string]$_.ProtectionStatus
        encryptionPercentage = $_.EncryptionPercentage
    }
})
$osBitLocker = $bitLockerVolumes | Where-Object { $_.MountPoint -eq $env:SystemDrive } | Select-Object -First 1
if ($null -ne $osBitLocker -and [string]$osBitLocker.ProtectionStatus -notmatch "On") {
    $securityStatus = if ($securityStatus -eq "WARNING") { "WARNING" } else { "WATCH" }
    Add-Recommendation -Recommendations $recommendations -Text "BitLocker protection is not enabled on the OS volume. Confirm this matches customer/security policy."
}

$securityReport = [ordered]@{
    defenderAvailable = ($null -ne $defender)
    antivirusEnabled = if ($null -ne $defender) { [bool]$defender.AntivirusEnabled } else { $null }
    realTimeProtectionEnabled = if ($null -ne $defender) { [bool]$defender.RealTimeProtectionEnabled } else { $null }
    antispywareEnabled = if ($null -ne $defender) { [bool]$defender.AntispywareEnabled } else { $null }
    firewallEnabled = $firewallEnabled
    firewallProfiles = @($firewallProfiles | ForEach-Object { [ordered]@{ name = [string]$_.Name; enabled = [bool]$_.Enabled } })
    avProducts = @($avProducts | ForEach-Object { [string]$_.displayName })
    bitLockerVolumes = $bitLockerReport
    status = $securityStatus
}

if ($recommendations.Count -eq 0) {
    Add-Recommendation -Recommendations $recommendations -Text "System is ready for standard ForgerEMS field work. Re-scan after major updates or hardware changes."
}

$cpuStatus = if ($null -eq $processor) { "UNKNOWN" } else { "READY" }
$overallStatus = Get-WorstStatus -Statuses @($osStatus, $cpuStatus, $ramStatus, $gpuStatus, $diskOverallStatus, $networkStatus, $securityStatus)
$serviceTag = if ($null -ne $bios) { [string]$bios.SerialNumber } else { "" }
$windowsLicenseChannel = if ($null -ne $licenseProduct) { [string]$licenseProduct.Description } else { "UNKNOWN" }
$windowsLicenseStatus = if ($null -ne $licenseProduct) { [string]$licenseProduct.LicenseStatus } else { "UNKNOWN" }
$displayReport = @($displays | ForEach-Object {
    [ordered]@{
        name = [string]$_.Name
        screenWidth = $_.ScreenWidth
        screenHeight = $_.ScreenHeight
        monitorManufacturer = [string]$_.MonitorManufacturer
        availability = [string]$_.Availability
    }
})
$smartReport = @($smartPredictFailures | ForEach-Object {
    [ordered]@{
        instanceName = [string]$_.InstanceName
        predictFailure = [bool]$_.PredictFailure
        reason = $_.Reason
    }
})
if (@($smartReport | Where-Object { $_.predictFailure }).Count -gt 0) {
    Add-UniqueText -Items $obviousProblems -Text "SMART predicts a storage failure."
    Add-Recommendation -Recommendations $recommendations -Text "SMART predicts a storage failure. Back up data and replace the affected drive before resale."
}
if ($obviousProblems.Count -eq 0) {
    Add-UniqueText -Items $obviousProblems -Text "No obvious blocking problems detected locally."
}
$flipValue = New-FlipValueReport `
    -ComputerSystem $computerSystem `
    -Processor $processor `
    -TotalMemoryBytes $totalMemoryBytes `
    -Gpus $gpus `
    -DiskReports $diskReports `
    -BatteryReports $batteryReports `
    -Problems $obviousProblems

$report = [ordered]@{
    schemaVersion = 1
    product = "ForgerEMS"
    releaseIdentifier = ([string]::Concat("ForgerEMS v1.1.1 ", [char]0x2013, " Flip Intelligence Update"))
    generatedUtc = (Get-Date).ToUniversalTime().ToString("o")
    overallStatus = $overallStatus
    summary = [ordered]@{
        computerName = $env:COMPUTERNAME
        manufacturer = if ($null -ne $computerSystem) { [string]$computerSystem.Manufacturer } else { "Unknown" }
        model = if ($null -ne $computerSystem) { [string]$computerSystem.Model } else { "Unknown" }
        serviceTag = $serviceTag
        serialNumber = $serviceTag
        os = if ($null -ne $operatingSystem) { ("{0} {1}" -f $operatingSystem.Caption, $operatingSystem.Version).Trim() } else { "Unknown OS" }
        osBuild = if ($null -ne $operatingSystem) { [string]$operatingSystem.BuildNumber } else { "UNKNOWN" }
        osArchitecture = if ($null -ne $operatingSystem) { [string]$operatingSystem.OSArchitecture } else { "UNKNOWN" }
        windowsLicenseChannel = $windowsLicenseChannel
        windowsLicenseStatus = $windowsLicenseStatus
        bios = if ($null -ne $bios) { ("{0} {1}" -f $bios.Manufacturer, $bios.SMBIOSBIOSVersion).Trim() } else { "UNKNOWN" }
        biosDate = if ($null -ne $bios) { Format-DateValue -Value $bios.ReleaseDate } else { "UNKNOWN" }
        secureBoot = if ($null -ne $secureBoot) { [bool]$secureBoot } else { $null }
        tpmPresent = if ($null -ne $tpm) { [bool]$tpm.TpmPresent } else { $null }
        tpmReady = if ($null -ne $tpm) { [bool]$tpm.TpmReady } else { $null }
        lastBoot = if ($null -ne $lastBoot) { $lastBoot.ToString("yyyy-MM-dd HH:mm:ss") } else { "UNKNOWN" }
        uptime = if ($null -ne $uptime) { Format-TimeSpanValue -Value $uptime } else { "UNKNOWN" }
        cpu = Get-ProcessorName -Processor $processor
        cpuCores = if ($null -ne $processor) { $processor.NumberOfCores } else { $null }
        cpuLogicalProcessors = if ($null -ne $processor) { $processor.NumberOfLogicalProcessors } else { $null }
        cpuBaseClockMhz = if ($null -ne $processor) { $processor.CurrentClockSpeed } else { $null }
        cpuMaxClockMhz = if ($null -ne $processor) { $processor.MaxClockSpeed } else { $null }
        ramTotal = Format-Bytes -Bytes $totalMemoryBytes
        ramFree = Format-Bytes -Bytes $freeMemoryBytes
        ramUsed = Format-Bytes -Bytes $usedMemoryBytes
        ramUsedPercent = $usedMemoryPercent
        ramSpeed = if ($memorySpeeds.Count -gt 0) { (($memorySpeeds | ForEach-Object { "{0} MHz" -f $_ }) -join ", ") } else { "UNKNOWN" }
        ramSlotsTotal = $memorySlotsTotal
        ramSlotsUsed = $memorySlotsUsed
        ramSlotsFree = $memorySlotsFree
        ramUpgradePath = $memoryUpgradePath
        ramStatus = $ramStatus
        gpus = @($gpus | ForEach-Object { [ordered]@{ name = [string]$_.Name; driverVersion = [string]$_.DriverVersion } })
        gpuStatus = $gpuStatus
    }
    disks = $diskReports
    smart = $smartReport
    volumes = $volumeReports
    diskStatus = $diskOverallStatus
    batteryPresent = ($batteryReports.Count -gt 0)
    batteries = $batteryReports
    batteryStatus = $batteryOverallStatus
    displays = $displayReport
    network = [ordered]@{
        status = $networkStatus
        internetCheck = [bool]$internetCheck
        adapters = $networkReport
    }
    security = $securityReport
    obviousProblems = @($obviousProblems)
    flipValue = $flipValue
    recommendations = @($recommendations)
    reportPaths = [ordered]@{
        json = $jsonPath
        markdown = $markdownPath
    }
}

$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$markdown = New-Object System.Collections.Generic.List[string]
[void]$markdown.Add("# ForgerEMS System Intelligence")
[void]$markdown.Add("")
[void]$markdown.Add(("Generated UTC: {0}" -f $report.generatedUtc))
[void]$markdown.Add(("Overall status: **{0}**" -f $overallStatus))
[void]$markdown.Add(("Pricing basis: **{0}**" -f $report.flipValue.estimateType))
[void]$markdown.Add("")
[void]$markdown.Add("## System Summary")
[void]$markdown.Add(("- Computer: {0}" -f $report.summary.computerName))
[void]$markdown.Add(("- Model: {0} {1}" -f $report.summary.manufacturer, $report.summary.model))
[void]$markdown.Add(("- Service tag / serial: {0}" -f $report.summary.serviceTag))
[void]$markdown.Add(("- OS: {0}" -f $report.summary.os))
[void]$markdown.Add(("- OS build: {0}" -f $report.summary.osBuild))
[void]$markdown.Add(("- Windows license channel: {0}" -f $report.summary.windowsLicenseChannel))
[void]$markdown.Add(("- BIOS: {0}, date {1}" -f $report.summary.bios, $report.summary.biosDate))
[void]$markdown.Add(("- Secure Boot: {0}" -f $report.summary.secureBoot))
[void]$markdown.Add(("- TPM Present/Ready: {0}/{1}" -f $report.summary.tpmPresent, $report.summary.tpmReady))
[void]$markdown.Add(("- Last boot: {0}" -f $report.summary.lastBoot))
[void]$markdown.Add(("- Uptime: {0}" -f $report.summary.uptime))
[void]$markdown.Add(("- CPU: {0}, {1} cores / {2} threads, base {3} MHz, max {4} MHz" -f $report.summary.cpu, $report.summary.cpuCores, $report.summary.cpuLogicalProcessors, $report.summary.cpuBaseClockMhz, $report.summary.cpuMaxClockMhz))
[void]$markdown.Add(("- RAM: {0} total, {1} free, {2}% used, speed {3}, slots {4}/{5}, upgrade path: {6} ({7})" -f $report.summary.ramTotal, $report.summary.ramFree, $report.summary.ramUsedPercent, $report.summary.ramSpeed, $report.summary.ramSlotsUsed, $report.summary.ramSlotsTotal, $report.summary.ramUpgradePath, $report.summary.ramStatus))
[void]$markdown.Add(("- GPU: {0}" -f (($report.summary.gpus | ForEach-Object { ("{0} driver {1}" -f $_.name, $_.driverVersion) }) -join "; ")))
[void]$markdown.Add("")
[void]$markdown.Add("## Flip Value")
[void]$markdown.Add(("- Estimated resale range: {0}" -f $report.flipValue.estimatedResaleRange))
[void]$markdown.Add(("- Recommended list price: {0}" -f $report.flipValue.recommendedListPrice))
[void]$markdown.Add(("- Quick-sale price: {0}" -f $report.flipValue.quickSalePrice))
[void]$markdown.Add(("- Parts/repair price: {0}" -f $report.flipValue.partsRepairPrice))
[void]$markdown.Add(("- Confidence score: {0}" -f $report.flipValue.confidenceScore))
[void]$markdown.Add(("- Provider status: {0}" -f $report.flipValue.providerStatus))
[void]$markdown.Add(("- Suggested listing title: {0}" -f $report.flipValue.suggestedListingTitle))
[void]$markdown.Add(("- Suggested listing description: {0}" -f $report.flipValue.suggestedListingDescription))
[void]$markdown.Add("")
[void]$markdown.Add("### Value Drivers")
foreach ($item in $report.flipValue.valueDrivers) {
    [void]$markdown.Add(("- {0}" -f $item))
}
[void]$markdown.Add("")
[void]$markdown.Add("### Value Reducers")
foreach ($item in $report.flipValue.valueReducers) {
    [void]$markdown.Add(("- {0}" -f $item))
}
[void]$markdown.Add("")
[void]$markdown.Add("### Upgrade Recommendations Before Selling")
foreach ($item in $report.flipValue.suggestedUpgradeRecommendations) {
    [void]$markdown.Add(("- {0}" -f $item))
}
[void]$markdown.Add("")
[void]$markdown.Add("### Pricing Providers")
foreach ($provider in $report.flipValue.pricingProviders) {
    [void]$markdown.Add(("- {0}: {1}" -f $provider.name, $provider.status))
}
[void]$markdown.Add("")
[void]$markdown.Add("## Disk Health")
foreach ($disk in $diskReports) {
    [void]$markdown.Add(("- {0}: {1}, {2}, {3}, {4}, temp {5} C, wear {6}%, status {7}" -f $disk.name, $disk.interfaceType, $disk.mediaType, $disk.size, $disk.health, $disk.temperatureC, $disk.wearPercent, $disk.status))
}
if ($diskReports.Count -eq 0) {
    [void]$markdown.Add("- No physical disk health data available.")
}
foreach ($volume in $volumeReports) {
    [void]$markdown.Add(("- Volume {0}: {1} free of {2}, status {3}" -f $volume.drive, $volume.free, $volume.size, $volume.status))
}
[void]$markdown.Add("")
[void]$markdown.Add("## Battery")
if ($batteryReports.Count -gt 0) {
    foreach ($battery in $batteryReports) {
        [void]$markdown.Add(("- {0}: {1}% charge, wear {2}%, cycle count {3}, AC connected {4}, status {5}" -f $battery.name, $battery.estimatedChargeRemaining, $battery.wearPercent, $battery.cycleCount, $battery.acConnected, $battery.status))
    }
}
else {
    [void]$markdown.Add("- No battery detected.")
}
[void]$markdown.Add("")
[void]$markdown.Add("## Display")
if ($displayReport.Count -gt 0) {
    foreach ($display in $displayReport) {
        [void]$markdown.Add(("- {0}: {1}x{2}, manufacturer {3}" -f $display.name, $display.screenWidth, $display.screenHeight, $display.monitorManufacturer))
    }
}
else {
    [void]$markdown.Add("- No display data available.")
}
[void]$markdown.Add("")
[void]$markdown.Add("## Network")
foreach ($adapter in $networkReport) {
    [void]$markdown.Add(("- {0}: {1}; link {2}; IP {3}; gateway {4}; DNS {5}; Wi-Fi signal {6}%; APIPA {7}" -f $adapter.name, $adapter.description, $adapter.linkSpeed, (($adapter.ipAddresses | Where-Object { $_ }) -join ", "), (($adapter.gateways | Where-Object { $_ }) -join ", "), (($adapter.dnsServers | Where-Object { $_ }) -join ", "), $adapter.wifiSignalPercent, $adapter.apipaDetected))
}
[void]$markdown.Add(("- Internet check: {0}" -f $internetCheck))
if ($networkReport.Count -eq 0) {
    [void]$markdown.Add("- No active IP-enabled adapter detected.")
}
[void]$markdown.Add("")
[void]$markdown.Add("## Security")
[void]$markdown.Add(("- Status: {0}" -f $securityStatus))
[void]$markdown.Add(("- Defender antivirus enabled: {0}" -f $securityReport.antivirusEnabled))
[void]$markdown.Add(("- Defender real-time protection: {0}" -f $securityReport.realTimeProtectionEnabled))
[void]$markdown.Add(("- Firewall enabled: {0}" -f $securityReport.firewallEnabled))
[void]$markdown.Add(("- Registered AV: {0}" -f (($securityReport.avProducts | Where-Object { $_ }) -join "; ")))
foreach ($volume in $securityReport.bitLockerVolumes) {
    [void]$markdown.Add(("- BitLocker {0}: {1}, protection {2}, {3}% encrypted" -f $volume.mountPoint, $volume.volumeStatus, $volume.protectionStatus, $volume.encryptionPercentage))
}
[void]$markdown.Add("")
[void]$markdown.Add("## Obvious Problems")
foreach ($problem in $obviousProblems) {
    [void]$markdown.Add(("- {0}" -f $problem))
}
[void]$markdown.Add("")
[void]$markdown.Add("## Recommendations")
foreach ($recommendation in $recommendations) {
    [void]$markdown.Add(("- {0}" -f $recommendation))
}

$markdown | Set-Content -LiteralPath $markdownPath -Encoding UTF8
$legacyMarkdownPath = Join-Path $OutputDirectory "system-intelligence-latest.md"
$markdown | Set-Content -LiteralPath $legacyMarkdownPath -Encoding UTF8

Write-ScanLog ("System scan complete. Overall status: {0}" -f $overallStatus) "OK"
Write-ScanLog ("JSON report: {0}" -f $jsonPath) "OK"
Write-ScanLog ("Markdown report: {0}" -f $markdownPath) "OK"
