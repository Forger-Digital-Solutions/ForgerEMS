#requires -Version 5.1

[CmdletBinding()]
param(
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$script:SystemIntelligenceLogPath = $null
$script:SystemIntelligenceLogFailed = $false

function Write-ScanLog {
    param(
        [Parameter(Mandatory)][string]$Message,
        [ValidateSet("INFO", "OK", "WARN", "ERROR")][string]$Level = "INFO"
    )

    $line = "[{0}] {1:yyyy-MM-dd HH:mm:ss} {2}" -f $Level, (Get-Date), $Message
    Write-Host $line
    if (-not $script:SystemIntelligenceLogFailed -and -not [string]::IsNullOrWhiteSpace($script:SystemIntelligenceLogPath)) {
        try {
            $logDirectory = Split-Path -Parent $script:SystemIntelligenceLogPath
            if (-not [string]::IsNullOrWhiteSpace($logDirectory)) {
                New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
            }

            Add-Content -LiteralPath $script:SystemIntelligenceLogPath -Value $line -Encoding UTF8
        }
        catch {
            $script:SystemIntelligenceLogFailed = $true
            Write-Host ("[WARN] Failed to write System Intelligence log: {0}" -f $_.Exception.Message)
        }
    }
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
        Write-ScanLog ("Optional provider failed: {0}" -f $_.Exception.Message) "WARN"
        return $Default
    }
}

function New-ProviderField {
    param(
        [object]$Value,
        [string]$Status,
        [string]$Source,
        [string]$Reason,
        [string]$FriendlyDisplayText
    )

    [ordered]@{
        value = $Value
        status = $Status
        source = $Source
        reason = $Reason
        friendlyDisplayText = $FriendlyDisplayText
    }
}

function Get-FirmwareTypeDisplay {
    $firmwareType = Invoke-Optional {
        (Get-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control" -Name PEFirmwareType -ErrorAction Stop).PEFirmwareType
    }

    switch ([int]$firmwareType) {
        1 { return "Legacy BIOS" }
        2 { return "UEFI" }
        default { return "Unknown firmware mode" }
    }
}

function Get-SecureBootInfo {
    Write-ScanLog "Checking Secure Boot state."
    try {
        $value = Confirm-SecureBootUEFI -ErrorAction Stop
        if ($value) {
            return New-ProviderField -Value $true -Status "READY" -Source "Confirm-SecureBootUEFI" -Reason "" -FriendlyDisplayText "Enabled"
        }

        return New-ProviderField -Value $false -Status "WARNING" -Source "Confirm-SecureBootUEFI" -Reason "Secure Boot is disabled in firmware." -FriendlyDisplayText "Disabled"
    }
    catch {
        $firmware = Get-FirmwareTypeDisplay
        $message = $_.Exception.Message
        if ($firmware -eq "Legacy BIOS" -or $message -match "Cmdlet not supported|not supported") {
            return New-ProviderField -Value $null -Status "UNKNOWN" -Source "Confirm-SecureBootUEFI + registry" -Reason "Secure Boot requires UEFI firmware." -FriendlyDisplayText "Unsupported / Legacy BIOS"
        }

        return New-ProviderField -Value $null -Status "UNKNOWN" -Source "Confirm-SecureBootUEFI + registry" -Reason $message -FriendlyDisplayText "Unknown - requires admin or unavailable"
    }
}

function Get-TpmInfo {
    Write-ScanLog "Checking TPM state."
    try {
        $value = Get-Tpm -ErrorAction Stop
        $friendly = if (-not $value.TpmPresent) {
            "TPM not detected"
        }
        elseif ($value.TpmReady) {
            "TPM ready for Windows 11"
        }
        elseif (-not $value.TpmEnabled) {
            "TPM disabled in firmware"
        }
        else {
            "TPM present but not ready"
        }

        $status = if ($value.TpmPresent -and $value.TpmReady) { "READY" } elseif ($value.TpmPresent) { "WARNING" } else { "CRITICAL" }
        return [ordered]@{
            present = [bool]$value.TpmPresent
            enabled = [bool]$value.TpmEnabled
            activated = [bool]$value.TpmActivated
            ready = [bool]$value.TpmReady
            manufacturer = [string]$value.ManufacturerIdTxt
            version = [string]$value.ManufacturerVersion
            status = $status
            source = "Get-Tpm"
            reason = if ($status -eq "READY") { "" } else { "TPM is not fully ready for Windows security features." }
            friendlyDisplayText = $friendly
        }
    }
    catch {
        $fallback = Invoke-Optional {
            Get-CimInstance -Namespace "root\CIMV2\Security\MicrosoftTpm" -ClassName Win32_Tpm -ErrorAction Stop | Select-Object -First 1
        }
        if ($null -ne $fallback) {
            $enabled = [bool]($fallback.IsEnabled_InitialValue)
            $activated = [bool]($fallback.IsActivated_InitialValue)
            $ready = $enabled -and $activated
            return [ordered]@{
                present = $true
                enabled = $enabled
                activated = $activated
                ready = $ready
                manufacturer = [string]$fallback.ManufacturerId
                version = [string]$fallback.ManufacturerVersion
                status = if ($ready) { "READY" } else { "WARNING" }
                source = "Win32_Tpm"
                reason = if ($ready) { "" } else { "TPM exists but is not enabled and activated." }
                friendlyDisplayText = if ($ready) { "TPM ready for Windows 11" } elseif (-not $enabled) { "TPM disabled in firmware" } else { "TPM present but not ready" }
            }
        }

        return [ordered]@{
            present = $null
            enabled = $null
            activated = $null
            ready = $null
            manufacturer = ""
            version = ""
            status = "UNKNOWN"
            source = "Get-Tpm + Win32_Tpm"
            reason = $_.Exception.Message
            friendlyDisplayText = "TPM status unavailable"
        }
    }
}

function Get-LicenseDisplay {
    param($LicenseProduct, $OperatingSystem)

    $osName = if ($null -ne $OperatingSystem) { [string]$OperatingSystem.Caption } else { "Windows" }
    if ($null -eq $LicenseProduct) {
        return [ordered]@{
            channel = "Unknown license channel"
            rawDescription = "Not reported"
            status = "UNKNOWN"
            friendlyDisplayText = ("{0} - license channel unavailable" -f $osName)
        }
    }

    $raw = [string]$LicenseProduct.Description
    $channel = switch -Regex ($raw) {
        "OEM_DM" { "OEM digital license"; break }
        "OEM" { "OEM license"; break }
        "RETAIL" { "Retail license"; break }
        "VOLUME_KMSCLIENT|VOLUME_KMS" { "Volume/KMS client"; break }
        "VOLUME_MAK" { "Volume/MAK license"; break }
        default { "License channel reported by Windows" }
    }

    [ordered]@{
        channel = $channel
        rawDescription = $raw
        status = [string]$LicenseProduct.LicenseStatus
        friendlyDisplayText = ("{0} - {1}" -f $osName, $channel)
    }
}

function Test-VirtualNetworkAdapter {
    param(
        [string]$Name,
        [string]$Description
    )

    $combined = ("{0} {1}" -f $Name, $Description)
    return $combined -match "(?i)virtual|hyper-v|virtualbox|vmware|vpn|tap|wintun|wireguard|tailscale|zerotier|loopback|host-only|bluetooth"
}

function Get-GpuType {
    param([string]$Name)

    if ($Name -match "(?i)intel|uhd|iris|vega\s+\d|radeon\(tm\)\s+graphics|amd radeon graphics") {
        return "Integrated"
    }

    if ($Name -match "(?i)nvidia|geforce|rtx|gtx|quadro|radeon\s+(rx|pro)|arc") {
        return "Dedicated"
    }

    return "Unknown"
}

function Get-WifiState {
    param([string]$NetshText)

    if ([string]::IsNullOrWhiteSpace($NetshText)) {
        return [ordered]@{
            connected = $false
            signalPercent = $null
            friendlyDisplayText = "Wi-Fi not connected"
            source = "netsh wlan show interfaces"
        }
    }

    $state = if ($NetshText -match "^\s*State\s+:\s+(.+)$") { $Matches[1].Trim() } else { "" }
    if ($state -notmatch "(?i)connected") {
        return [ordered]@{
            connected = $false
            signalPercent = $null
            friendlyDisplayText = "Wi-Fi not connected"
            source = "netsh wlan show interfaces"
        }
    }

    $signal = if ($NetshText -match "Signal\s+:\s+([0-9]+)%") { [int]$Matches[1] } else { $null }
    return [ordered]@{
        connected = $true
        signalPercent = $signal
        friendlyDisplayText = if ($null -ne $signal) { "Wi-Fi connected - {0}% signal" -f $signal } else { "Wi-Fi connected - signal unavailable" }
        source = "netsh wlan show interfaces"
    }
}

function Get-BatteryReportData {
    Write-ScanLog "Checking powercfg battery report fallback."
    $reportPath = Join-Path ([IO.Path]::GetTempPath()) "forgerems-battery.html"
    try {
        powercfg /batteryreport /output $reportPath /duration 1 | Out-Null
        if (-not (Test-Path -LiteralPath $reportPath)) {
            return $null
        }

        $html = Get-Content -LiteralPath $reportPath -Raw -ErrorAction Stop
        $design = $null
        $full = $null
        $cycle = $null
        if ($html -match "(?is)DESIGN CAPACITY.*?([0-9][0-9,\.]*)\s*mWh") {
            $design = [double](($Matches[1] -replace ",", ""))
        }
        if ($html -match "(?is)FULL CHARGE CAPACITY.*?([0-9][0-9,\.]*)\s*mWh") {
            $full = [double](($Matches[1] -replace ",", ""))
        }
        if ($html -match "(?is)CYCLE COUNT.*?([0-9][0-9,\.]*)") {
            $cycle = [int](($Matches[1] -replace ",", ""))
        }

        return [ordered]@{
            designCapacity = $design
            fullChargeCapacity = $full
            cycleCount = $cycle
            source = "powercfg /batteryreport"
        }
    }
    catch {
        Write-ScanLog ("Battery report fallback failed: {0}" -f $_.Exception.Message) "WARN"
        return $null
    }
}

function ConvertTo-StatusRank {
    param([string]$Status)

    switch ($Status) {
        "CRITICAL" { return 5 }
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
            name = "eBay active comps"
            key = "ebayActiveComps"
            configured = $false
            status = "Pricing provider not configured"
            notes = "Official API path only. Active comps can be supported when configured; sold comps are not configured in this beta."
        },
        [ordered]@{
            name = "OfferUp"
            key = "offerUp"
            configured = $false
            status = "Pricing provider not configured"
            notes = "Manual/future source only in this beta."
        },
        [ordered]@{
            name = "Facebook Marketplace"
            key = "facebookMarketplace"
            configured = $false
            status = "Pricing provider not configured"
            notes = "Manual/future source only in this beta."
        },
        [ordered]@{
            name = "Generic web price provider"
            key = "genericWeb"
            configured = $false
            status = "Pricing provider not configured"
            notes = "Optional future online provider hook. Disabled by default; offline estimator remains primary."
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
        missingInfoNeeded = @(
            "Cosmetic condition",
            "Screen condition",
            "Keyboard/trackpad condition",
            "Hinge condition",
            "Charger included",
            "Known defects/damage"
        )
        listingPhotoChecklist = @(
            "Front/lid and exterior corners",
            "Keyboard + touchpad close-up",
            "Screen on with no dead pixels",
            "System specs screen",
            "Ports and charger"
        )
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

$localAppData = [Environment]::GetFolderPath("LocalApplicationData")
if ([string]::IsNullOrWhiteSpace($localAppData)) {
    $localAppData = [IO.Path]::GetTempPath()
}

$script:SystemIntelligenceLogPath = Join-Path $localAppData "ForgerEMS\logs\system-intelligence.log"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {

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
$tpmInfo = Get-TpmInfo
$secureBootInfo = Get-SecureBootInfo
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
$wifiState = Get-WifiState -NetshText $wifiInterfaceText
$batteryReportFallback = Get-BatteryReportData

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
$memoryRatedSpeeds = @($memoryModules | Where-Object { $_.Speed } | ForEach-Object { [int]$_.Speed } | Select-Object -Unique | Sort-Object)
$memoryConfiguredSpeeds = @($memoryModules | Where-Object { $_.ConfiguredClockSpeed } | ForEach-Object { [int]$_.ConfiguredClockSpeed } | Select-Object -Unique | Sort-Object)
$memoryTypeCode = @($memoryModules | Where-Object { $_.SMBIOSMemoryType } | Select-Object -First 1).SMBIOSMemoryType
$memoryType = switch ([int]$memoryTypeCode) {
    20 { "DDR" }
    21 { "DDR2" }
    24 { "DDR3" }
    26 { "DDR4" }
    34 { "DDR5" }
    default { "RAM" }
}
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
$memoryConfiguredDisplay = if ($memoryConfiguredSpeeds.Count -gt 0) { (($memoryConfiguredSpeeds | ForEach-Object { "{0} MT/s" -f $_ }) -join ", ") } else { "Not exposed by SMBIOS" }
$memoryRatedDisplay = if ($memoryRatedSpeeds.Count -gt 0) { (($memoryRatedSpeeds | ForEach-Object { "{0} MT/s" -f $_ }) -join ", ") } else { "Not exposed by SMBIOS" }
$memoryInstalledDisplay = if ($null -ne $totalMemoryBytes -and $totalMemoryBytes -gt 0) { "{0} {1}" -f (Format-Bytes -Bytes $totalMemoryBytes), $memoryType } else { "Installed RAM not reported" }
$memorySlotsDisplay = if ($null -ne $memorySlotsTotal -and $memorySlotsTotal -gt 0) { "Slots: {0}/{1} used" -f $memorySlotsUsed, $memorySlotsTotal } else { "Slot count not reported" }
$memoryModuleReports = @($memoryModules | ForEach-Object {
    [ordered]@{
        bankLabel = [string]$_.BankLabel
        capacity = Format-Bytes -Bytes ([double]$_.Capacity)
        configuredSpeed = if ($_.ConfiguredClockSpeed) { "{0} MT/s" -f $_.ConfiguredClockSpeed } else { "Not exposed by SMBIOS" }
        ratedSpeed = if ($_.Speed) { "{0} MT/s" -f $_.Speed } else { "Module rated speed: Not exposed by SMBIOS" }
        manufacturer = if ([string]::IsNullOrWhiteSpace([string]$_.Manufacturer)) { "Manufacturer not reported" } else { [string]$_.Manufacturer }
        partNumber = if ([string]::IsNullOrWhiteSpace([string]$_.PartNumber)) { "Part number not reported" } else { ([string]$_.PartNumber).Trim() }
    }
})
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
if ($secureBootInfo.value -eq $false) {
    Add-Recommendation -Recommendations $recommendations -Text "Secure Boot is disabled. Confirm this is intentional before trusting boot-chain security."
    Add-UniqueText -Items $obviousProblems -Text "Secure Boot is disabled."
}
if ($tpmInfo.present -eq $false -or ($tpmInfo.present -eq $true -and $tpmInfo.ready -ne $true)) {
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
        health = if ([string]::IsNullOrWhiteSpace($health)) { "Health not reported" } else { $health }
        healthDisplay = if ([string]::IsNullOrWhiteSpace($health)) { "Health not reported by Windows storage stack" } else { $health }
        operationalStatus = $operational
        temperatureC = $temperature
        temperatureDisplay = if ($null -ne $temperature) { "{0} C" -f $temperature } else { "Temp: Not exposed" }
        wearPercent = $wear
        wearDisplay = if ($null -ne $wear) { "{0}%" -f $wear } else { "Wear: Not exposed" }
        readErrorsTotal = $readErrors
        writeErrorsTotal = $writeErrors
        status = $diskStatus
        reason = if ($null -eq $temperature -or $null -eq $wear) { "Some reliability counters are not exposed by this drive or driver." } else { "" }
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
    if (($null -eq $designCapacity -or [double]$designCapacity -le 0) -and $null -ne $batteryReportFallback) {
        $designCapacity = $batteryReportFallback.designCapacity
    }
    if ($null -eq $fullChargeCapacity -and $batteryFullChargedCapacity.Count -gt 0) {
        $fullChargeCapacity = ($batteryFullChargedCapacity | Select-Object -First 1).FullChargedCapacity
    }
    if (($null -eq $fullChargeCapacity -or [double]$fullChargeCapacity -le 0) -and $null -ne $batteryReportFallback) {
        $fullChargeCapacity = $batteryReportFallback.fullChargeCapacity
    }
    $cycleCount = if ($battery.PSObject.Properties.Name -contains "CycleCount") { $battery.CycleCount } else { $null }
    if ($null -eq $cycleCount -and $batteryCycleCount.Count -gt 0) {
        $cycleCount = ($batteryCycleCount | Select-Object -First 1).CycleCount
    }
    if (($null -eq $cycleCount -or [int]$cycleCount -le 0) -and $null -ne $batteryReportFallback) {
        $cycleCount = $batteryReportFallback.cycleCount
    }
    if ($null -ne $designCapacity -and [double]$designCapacity -le 0) { $designCapacity = $null }
    if ($null -ne $fullChargeCapacity -and [double]$fullChargeCapacity -le 0) { $fullChargeCapacity = $null }
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
        designCapacityDisplay = if ($null -ne $designCapacity) { "{0:N0} mWh" -f [double]$designCapacity } else { "Not exposed by firmware/Windows" }
        fullChargeCapacity = $fullChargeCapacity
        fullChargeCapacityDisplay = if ($null -ne $fullChargeCapacity) { "{0:N0} mWh" -f [double]$fullChargeCapacity } else { "Not exposed by firmware/Windows" }
        wearPercent = $wearPercent
        wearDisplay = if ($null -ne $wearPercent) { "{0}%" -f $wearPercent } else { "Battery wear: Not exposed by firmware/Windows" }
        cycleCount = $cycleCount
        cycleCountDisplay = if ($null -ne $cycleCount) { [string]$cycleCount } else { "Not exposed by firmware/Windows" }
        acConnected = if ($null -ne $battery.BatteryStatus) { $battery.BatteryStatus -in @(2, 6, 7, 8, 9) } else { $null }
        batteryStatusCode = $battery.BatteryStatus
        status = $batteryStatus
        healthDisplay = if ($batteryStatus -eq "READY") { "Battery health looks acceptable" } elseif ($null -ne $wearPercent) { "Battery wear is {0}%" -f $wearPercent } else { "Battery health limited - capacity data unavailable" }
        source = if ($null -ne $batteryReportFallback) { "Win32_Battery + WMI + powercfg" } else { "Win32_Battery + WMI" }
    }
}
$batteryOverallStatus = if ($batteryReports.Count -eq 0) { "UNKNOWN" } else { Get-WorstStatus -Statuses @($batteryReports | ForEach-Object { $_.status }) }

Write-ScanLog "Checking network adapters."
$networkStatus = if ($networkAdapters.Count -gt 0) { "READY" } else { "WATCH" }
if ($networkAdapters.Count -eq 0) {
    Add-Recommendation -Recommendations $recommendations -Text "No active IP-enabled network adapter was detected."
}
$defaultRouteRaw = Invoke-Optional {
    Get-NetRoute -DestinationPrefix "0.0.0.0/0" -ErrorAction Stop |
        Where-Object { $_.NextHop -and $_.NextHop -ne "0.0.0.0" } |
        Sort-Object RouteMetric |
        Select-Object -First 1
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
    $name = if ($matchingNetAdapter) { [string]$matchingNetAdapter.Name } else { [string]$_.Description }
    $description = [string]$_.Description
    $isVirtual = Test-VirtualNetworkAdapter -Name $name -Description $description
    $isDefaultRoute = $null -ne $defaultRouteRaw -and $matchingNetAdapter -and [int]$matchingNetAdapter.ifIndex -eq [int]$defaultRouteRaw.ifIndex
    if ((-not $isVirtual) -and ($isDefaultRoute -or $hasGateway) -and ($hasApipa -or -not $hasGateway)) {
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
        description = $description
        name = $name
        macAddress = [string]$_.MACAddress
        linkSpeed = if ($matchingNetAdapter) { [string]$matchingNetAdapter.LinkSpeed } else { "" }
        driverInterface = if ($matchingNetAdapter) { [string]$matchingNetAdapter.InterfaceDescription } else { "" }
        dhcpEnabled = [bool]$_.DHCPEnabled
        ipAddresses = $ipAddresses
        gateways = $gateways
        dnsServers = @($_.DNSServerSearchOrder)
        apipaDetected = $hasApipa
        gatewayPresent = $hasGateway
        isVirtual = $isVirtual
        isDefaultRoute = $isDefaultRoute
        adapterRole = if ($isVirtual) { "Virtual adapter" } elseif ($description -match "(?i)wi-fi|wireless|wlan|802\.11") { "Wi-Fi" } else { "Physical adapter" }
        wifiSignalPercent = if ($description -match "(?i)wi-fi|wireless|wlan|802\.11" -and $wifiState.connected) { $wifiState.signalPercent } else { $null }
        wifiDisplay = if ($description -match "(?i)wi-fi|wireless|wlan|802\.11") { $wifiState.friendlyDisplayText } else { "Not a Wi-Fi adapter" }
    }
})
$physicalNetworkReport = @($networkReport | Where-Object { -not $_.isVirtual })
$virtualNetworkReport = @($networkReport | Where-Object { $_.isVirtual })
$defaultRouteAdapter = if ($null -ne $defaultRouteRaw) {
    $networkReport | Where-Object { $_.isDefaultRoute } | Select-Object -First 1
}
else {
    $null
}
# IfIndex match often fails across Win32_NetworkAdapterConfiguration vs Get-NetAdapter; fall back to gateway match on physical NICs.
if ($null -eq $defaultRouteAdapter -and $null -ne $defaultRouteRaw) {
    $nh = [string]$defaultRouteRaw.NextHop
    if (-not [string]::IsNullOrWhiteSpace($nh)) {
        $gwMatch = $physicalNetworkReport | Where-Object {
            $gw = $_.gateways
            $null -ne $gw -and (@($gw) | Where-Object { $_ -eq $nh }).Count -gt 0
        } | Select-Object -First 1
        if ($gwMatch) {
            $defaultRouteAdapter = $gwMatch
        }
    }
}
$internetCheck = Invoke-Optional { Test-NetConnection -ComputerName "1.1.1.1" -Port 443 -InformationLevel Quiet -WarningAction SilentlyContinue -ErrorAction Stop } $false
if (-not $internetCheck) {
    $networkStatus = if ($networkStatus -eq "READY") { "WATCH" } else { $networkStatus }
    Add-Recommendation -Recommendations $recommendations -Text "Internet connectivity check did not pass against 1.1.1.1:443. Confirm network before downloads."
    Add-UniqueText -Items $obviousProblems -Text "Internet connectivity check failed."
}
$internetDisplay = if ($internetCheck) { "Internet: Working" } else { "Internet: Check failed" }
$defaultRouteDisplay = if ($null -ne $defaultRouteAdapter) {
    $nh = if ($null -ne $defaultRouteRaw) { [string]$defaultRouteRaw.NextHop } else { "" }
    if (-not [string]::IsNullOrWhiteSpace($nh)) {
        "Default route: {0} via {1}" -f $defaultRouteAdapter.name, $nh
    }
    else {
        "Default route: {0}" -f $defaultRouteAdapter.name
    }
}
elseif ($null -ne $defaultRouteRaw) {
    "Default route: interface {0} (next hop {1})" -f $defaultRouteRaw.ifIndex, $defaultRouteRaw.NextHop
}
else {
    $gwHint = ($physicalNetworkReport | Where-Object { $_.gatewayPresent } | Select-Object -First 1)
    if ($gwHint) {
        $gws = @($gwHint.gateways | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
        if ($gws.Count -gt 0) {
            $gw0 = [string]$gws[0]
            "Default route: {0} via {1}" -f $gwHint.name, $gw0
        }
        elseif ($internetCheck) {
            "Default route: {0} (internet check passed; Get-NetRoute match incomplete)" -f $gwHint.name
        }
        else {
            "Default route: not detected"
        }
    }
    else {
        "Default route: not detected"
    }
}
$virtualIgnoredDisplay = if ($virtualNetworkReport.Count -gt 0) {
    "Virtual adapters ignored: {0}" -f (($virtualNetworkReport | Select-Object -ExpandProperty name) -join ", ")
}
else {
    "Virtual adapters ignored: none"
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
$manageBdeStatusText = $null
$bitLockerUnavailableReason = ""
if ($bitLockerVolumes.Count -eq 0) {
    $manageBdeStatusText = Invoke-Optional { manage-bde -status $env:SystemDrive 2>&1 | Out-String } ""
    if ([string]::IsNullOrWhiteSpace($manageBdeStatusText)) {
        $bitLockerUnavailableReason = "Unavailable - requires admin, unsupported Windows edition, or BitLocker command not present."
    }
    elseif ($manageBdeStatusText -match "(?i)access is denied|administrator") {
        $bitLockerUnavailableReason = "Unavailable - requires admin."
    }
    elseif ($manageBdeStatusText -match "(?i)not recognized|not found") {
        $bitLockerUnavailableReason = "Unavailable on this Windows edition."
    }
}
$bitLockerReport = @($bitLockerVolumes | ForEach-Object {
    [ordered]@{
        mountPoint = [string]$_.MountPoint
        volumeStatus = [string]$_.VolumeStatus
        protectionStatus = [string]$_.ProtectionStatus
        encryptionPercentage = $_.EncryptionPercentage
    }
})
$bitLockerSummary = if ($bitLockerReport.Count -gt 0) {
    $osVolume = $bitLockerReport | Where-Object { $_.mountPoint -eq $env:SystemDrive } | Select-Object -First 1
    if ($null -eq $osVolume) {
        New-ProviderField -Value "Unknown" -Status "UNKNOWN" -Source "Get-BitLockerVolume" -Reason "OS volume was not returned by BitLocker provider." -FriendlyDisplayText "BitLocker status unavailable for OS volume"
    }
    elseif ([string]$osVolume.protectionStatus -match "On") {
        New-ProviderField -Value "Enabled" -Status "READY" -Source "Get-BitLockerVolume" -Reason "" -FriendlyDisplayText "Enabled"
    }
    elseif ([string]$osVolume.volumeStatus -match "Suspended") {
        New-ProviderField -Value "Suspended" -Status "WARNING" -Source "Get-BitLockerVolume" -Reason "OS volume protection is suspended." -FriendlyDisplayText "Suspended"
    }
    else {
        New-ProviderField -Value "Disabled" -Status "WATCH" -Source "Get-BitLockerVolume" -Reason "OS volume protection is not enabled." -FriendlyDisplayText "Disabled"
    }
}
elseif ($manageBdeStatusText -match "(?i)Protection Status:\s+Protection On") {
    New-ProviderField -Value "Enabled" -Status "READY" -Source "manage-bde" -Reason "" -FriendlyDisplayText "Enabled"
}
elseif ($manageBdeStatusText -match "(?i)Protection Status:\s+Protection Off") {
    New-ProviderField -Value "Disabled" -Status "WATCH" -Source "manage-bde" -Reason "OS volume protection is not enabled." -FriendlyDisplayText "Disabled"
}
else {
    New-ProviderField -Value $null -Status "UNKNOWN" -Source "Get-BitLockerVolume + manage-bde" -Reason $bitLockerUnavailableReason -FriendlyDisplayText ("Unavailable - {0}" -f ($(if ([string]::IsNullOrWhiteSpace($bitLockerUnavailableReason)) { "reason not reported by Windows" } else { $bitLockerUnavailableReason -replace "^Unavailable - ", "" })))
}
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
    bitLockerSummary = $bitLockerSummary
    status = $securityStatus
}

if ($recommendations.Count -eq 0) {
    Add-Recommendation -Recommendations $recommendations -Text "System is ready for standard ForgerEMS field work. Re-scan after major updates or hardware changes."
}

$cpuStatus = if ($null -eq $processor) { "UNKNOWN" } else { "READY" }
$overallStatus = Get-WorstStatus -Statuses @($osStatus, $cpuStatus, $ramStatus, $gpuStatus, $diskOverallStatus, $networkStatus, $securityStatus)
$serviceTag = if ($null -ne $bios) { [string]$bios.SerialNumber } else { "" }
$serviceTagRedacted = if ([string]::IsNullOrWhiteSpace($serviceTag)) { "" } else { "REDACTED" }
$licenseInfo = Get-LicenseDisplay -LicenseProduct $licenseProduct -OperatingSystem $operatingSystem
$windowsLicenseChannel = $licenseInfo.channel
$windowsLicenseStatus = $licenseInfo.status
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
    releaseIdentifier = ([string]::Concat("ForgerEMS Beta v1.1.4 ", [char]0x2014, " Whole-App Intelligence Preview"))
    generatedUtc = (Get-Date).ToUniversalTime().ToString("o")
    overallStatus = $overallStatus
    summary = [ordered]@{
        computerName = $env:COMPUTERNAME
        manufacturer = if ($null -ne $computerSystem) { [string]$computerSystem.Manufacturer } else { "Unknown" }
        model = if ($null -ne $computerSystem) { [string]$computerSystem.Model } else { "Unknown" }
        serviceTag = $serviceTagRedacted
        serialNumber = $serviceTagRedacted
        os = if ($null -ne $operatingSystem) { ("{0} {1}" -f $operatingSystem.Caption, $operatingSystem.Version).Trim() } else { "Unknown OS" }
        osBuild = if ($null -ne $operatingSystem) { [string]$operatingSystem.BuildNumber } else { "UNKNOWN" }
        osArchitecture = if ($null -ne $operatingSystem) { [string]$operatingSystem.OSArchitecture } else { "UNKNOWN" }
        windowsLicenseChannel = $windowsLicenseChannel
        windowsLicenseStatus = $windowsLicenseStatus
        windowsLicense = $licenseInfo
        bios = if ($null -ne $bios) { ("{0} {1}" -f $bios.Manufacturer, $bios.SMBIOSBIOSVersion).Trim() } else { "UNKNOWN" }
        biosDate = if ($null -ne $bios) { Format-DateValue -Value $bios.ReleaseDate } else { "UNKNOWN" }
        secureBoot = if ($null -ne $secureBootInfo.value) { [bool]$secureBootInfo.value } else { $null }
        secureBootInfo = $secureBootInfo
        tpmPresent = if ($null -ne $tpmInfo.present) { [bool]$tpmInfo.present } else { $null }
        tpmReady = if ($null -ne $tpmInfo.ready) { [bool]$tpmInfo.ready } else { $null }
        tpmInfo = $tpmInfo
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
        ramSpeed = $memoryConfiguredDisplay
        ramInstalledDisplay = $memoryInstalledDisplay
        ramConfiguredSpeedDisplay = $memoryConfiguredDisplay
        ramModuleRatedSpeedDisplay = $memoryRatedDisplay
        ramSlotsDisplay = $memorySlotsDisplay
        ramModules = $memoryModuleReports
        ramSlotsTotal = $memorySlotsTotal
        ramSlotsUsed = $memorySlotsUsed
        ramSlotsFree = $memorySlotsFree
        ramUpgradePath = $memoryUpgradePath
        ramStatus = $ramStatus
        gpus = @($gpus | ForEach-Object { [ordered]@{ name = [string]$_.Name; type = Get-GpuType -Name ([string]$_.Name); driverVersion = [string]$_.DriverVersion } })
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
        internetDisplay = $internetDisplay
        defaultRoute = [ordered]@{
            friendlyDisplayText = $defaultRouteDisplay
            ifIndex = if ($null -ne $defaultRouteRaw) { $defaultRouteRaw.ifIndex } else { $null }
            nextHop = if ($null -ne $defaultRouteRaw) { [string]$defaultRouteRaw.NextHop } else { "" }
            adapterName = if ($null -ne $defaultRouteAdapter) { [string]$defaultRouteAdapter.name } else { "" }
        }
        wifi = $wifiState
        physicalAdapters = $physicalNetworkReport
        virtualAdapters = $virtualNetworkReport
        virtualAdaptersIgnored = $virtualIgnoredDisplay
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
[void]$markdown.Add("- Service tag / serial: REDACTED (stored session-local only when available)")
[void]$markdown.Add(("- OS: {0}" -f $report.summary.os))
[void]$markdown.Add(("- OS build: {0}" -f $report.summary.osBuild))
[void]$markdown.Add(("- Windows license: {0}" -f $report.summary.windowsLicense.friendlyDisplayText))
[void]$markdown.Add(("- BIOS: {0}, date {1}" -f $report.summary.bios, $report.summary.biosDate))
[void]$markdown.Add(("- Secure Boot: {0}" -f $report.summary.secureBootInfo.friendlyDisplayText))
[void]$markdown.Add(("- TPM: {0}" -f $report.summary.tpmInfo.friendlyDisplayText))
[void]$markdown.Add(("- Last boot: {0}" -f $report.summary.lastBoot))
[void]$markdown.Add(("- Uptime: {0}" -f $report.summary.uptime))
[void]$markdown.Add(("- CPU: {0}, {1} cores / {2} threads, base {3} MHz, max {4} MHz" -f $report.summary.cpu, $report.summary.cpuCores, $report.summary.cpuLogicalProcessors, $report.summary.cpuBaseClockMhz, $report.summary.cpuMaxClockMhz))
[void]$markdown.Add(("- RAM: {0}; configured {1}; rated {2}; {3}; upgrade path: {4} ({5})" -f $report.summary.ramInstalledDisplay, $report.summary.ramConfiguredSpeedDisplay, $report.summary.ramModuleRatedSpeedDisplay, $report.summary.ramSlotsDisplay, $report.summary.ramUpgradePath, $report.summary.ramStatus))
[void]$markdown.Add(("- GPU: {0}" -f (($report.summary.gpus | ForEach-Object { ("{0}: {1} driver {2}" -f $_.type, $_.name, $_.driverVersion) }) -join "; ")))
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
    [void]$markdown.Add(("- {0}: {1}, {2}, {3}, {4}, temp {5}, wear {6}, status {7}" -f $disk.name, $disk.interfaceType, $disk.mediaType, $disk.size, $disk.healthDisplay, $disk.temperatureDisplay, $disk.wearDisplay, $disk.status))
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
        [void]$markdown.Add(("- {0}: {1}% charge, design {2}, full {3}, wear {4}, cycle count {5}, AC connected {6}, status {7}" -f $battery.name, $battery.estimatedChargeRemaining, $battery.designCapacityDisplay, $battery.fullChargeCapacityDisplay, $battery.wearDisplay, $battery.cycleCountDisplay, $battery.acConnected, $battery.status))
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
    [void]$markdown.Add(("- {0}: {1}; role {2}; link {3}; IP {4}; gateway {5}; DNS {6}; Wi-Fi {7}; APIPA {8}" -f $adapter.name, $adapter.description, $adapter.adapterRole, $adapter.linkSpeed, (($adapter.ipAddresses | Where-Object { $_ }) -join ", "), (($adapter.gateways | Where-Object { $_ }) -join ", "), (($adapter.dnsServers | Where-Object { $_ }) -join ", "), $adapter.wifiDisplay, $adapter.apipaDetected))
}
[void]$markdown.Add(("- Internet check: {0}" -f $internetCheck))
[void]$markdown.Add(("- {0}" -f $defaultRouteDisplay))
[void]$markdown.Add(("- {0}" -f $virtualIgnoredDisplay))
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
if ($securityReport.bitLockerVolumes.Count -eq 0) {
    [void]$markdown.Add(("- BitLocker: {0}" -f $securityReport.bitLockerSummary.friendlyDisplayText))
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
