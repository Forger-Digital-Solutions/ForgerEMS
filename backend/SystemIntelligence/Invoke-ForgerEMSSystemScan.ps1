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
        [string]$FriendlyDisplayText,
        [string]$Confidence = "Medium",
        [string]$TechnicianNote = ""
    )

    [ordered]@{
        value = $Value
        status = $Status
        confidence = $Confidence
        source = $Source
        reason = $Reason
        technicianNote = $TechnicianNote
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
            return New-ProviderField -Value $true -Status "READY" -Source "Confirm-SecureBootUEFI" -Reason "" -FriendlyDisplayText "Enabled" -Confidence "High" -TechnicianNote "Secure Boot state was reported by Windows firmware API."
        }

        return New-ProviderField -Value $false -Status "WARNING" -Source "Confirm-SecureBootUEFI" -Reason "Secure Boot is disabled in firmware." -FriendlyDisplayText "Disabled" -Confidence "High" -TechnicianNote "Windows explicitly reported Secure Boot disabled."
    }
    catch {
        $firmware = Get-FirmwareTypeDisplay
        $message = $_.Exception.Message
        if ($firmware -eq "Legacy BIOS" -or $message -match "Cmdlet not supported|not supported") {
            return New-ProviderField -Value $null -Status "UNKNOWN" -Source "Confirm-SecureBootUEFI + registry" -Reason "Secure Boot requires UEFI firmware." -FriendlyDisplayText "Unsupported / Legacy BIOS" -Confidence "Low" -TechnicianNote "Windows did not expose Secure Boot state. Verify in BIOS/UEFI before treating it as disabled."
        }

        return New-ProviderField -Value $null -Status "UNKNOWN" -Source "Confirm-SecureBootUEFI + registry" -Reason $message -FriendlyDisplayText "Unknown - requires admin or unavailable" -Confidence "Low" -TechnicianNote "Windows did not expose Secure Boot state. Verify in BIOS/UEFI before treating it as disabled."
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
            confidence = "High"
            source = "Get-Tpm"
            reason = if ($status -eq "READY") { "" } else { "TPM is not fully ready for Windows security features." }
            technicianNote = if ($status -eq "READY") { "TPM was reported ready by Get-Tpm." } else { "TPM was reported by Windows but needs firmware/Windows verification before listing." }
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
                confidence = "Medium"
                source = "Win32_Tpm"
                reason = if ($ready) { "" } else { "TPM exists but is not enabled and activated." }
                technicianNote = "TPM state came from the WMI fallback provider."
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
            confidence = "Low"
            source = "Get-Tpm + Win32_Tpm"
            reason = $_.Exception.Message
            technicianNote = "Windows did not expose TPM state. Verify in BIOS/UEFI or vendor diagnostics before treating it as missing."
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
        providerStatus = "LocalHeuristicProvider active; eBay active listing provider not configured; sold comps/manual providers unavailable until configured"
        locationBasis = "Location not configured; national/offline heuristic basis"
        saleMode = "NormalLocal"
        compsUsed = 0
        outliersRemoved = 0
        providerArchitecture = "LocalHeuristicProvider, EbayActiveListingProvider, EbaySoldCompProvider future, FacebookMarketplaceProvider manual/future, OfferUpProvider manual/future, ManualCompImportProvider"
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

function New-DeviceFitReport {
    param(
        [object]$ComputerSystem,
        [object]$OperatingSystem,
        [object]$Processor,
        [Nullable[double]]$TotalMemoryBytes,
        [object[]]$Gpus,
        [object[]]$DiskReports,
        [object[]]$BatteryReports
    )

    $cpuName = Get-ProcessorName -Processor $Processor
    $cores = if ($null -ne $Processor) { [int]$Processor.NumberOfCores } else { 0 }
    $threads = if ($null -ne $Processor) { [int]$Processor.NumberOfLogicalProcessors } else { 0 }
    $ramGb = Convert-BytesToGigabytes -Bytes $TotalMemoryBytes
    $gpuText = (($Gpus | ForEach-Object { [string]$_.Name }) -join " ")
    $hasDedicatedGpu = $gpuText -match '(?i)nvidia|quadro|geforce|rtx|gtx|radeon|arc'
    $hasWorkstationGpu = $gpuText -match '(?i)quadro|radeon\s+pro|firepro|rtx\s+a\d'
    $hasGamingGpu = $gpuText -match '(?i)rtx|gtx|geforce|radeon\s+rx'
    $hasFastStorage = @($DiskReports | Where-Object { [string]$_.mediaType -match '(?i)SSD|NVMe' -or [string]$_.name -match '(?i)SSD|NVMe' }).Count -gt 0
    $batteryWearKnown = @($BatteryReports | Where-Object { $null -ne $_.wearPercent -or $null -ne $_.cycleCount }).Count -gt 0
    $batteryUnknown = $BatteryReports.Count -gt 0 -and -not $batteryWearKnown
    $isPerformanceCpu = $cpuName -match '(?i)\bi7|\bi9|ryzen\s+[79]|xeon|ultra\s+[79]' -or $cores -ge 6

    $primary = "Office / School / General Productivity"
    if ($hasWorkstationGpu -and $ramGb -ge 24 -and $isPerformanceCpu) {
        $primary = "Developer / Creator Workstation + Light Gaming"
    }
    elseif ($hasGamingGpu -and $ramGb -ge 16 -and $isPerformanceCpu) {
        $primary = "Gaming / Creator Laptop"
    }
    elseif ($isPerformanceCpu -and $ramGb -ge 16 -and $hasFastStorage) {
        $primary = "Developer / Technician Workstation"
    }

    $strongFits = New-Object System.Collections.Generic.List[string]
    [void]$strongFits.Add("Office/school/productivity")
    [void]$strongFits.Add("Web, streaming, and general multitasking")
    if ($isPerformanceCpu -and $ramGb -ge 16) {
        [void]$strongFits.Add("Software development, WSL, diagnostics, repair tools")
        [void]$strongFits.Add("Technician/refurbisher workflows")
    }
    if ($hasDedicatedGpu) {
        [void]$strongFits.Add("Light gaming and older AAA titles")
        [void]$strongFits.Add("Light-to-medium content creation")
    }
    if ($hasWorkstationGpu) {
        [void]$strongFits.Add("CAD/workstation tasks")
    }

    $weakFits = New-Object System.Collections.Generic.List[string]
    if (-not $hasGamingGpu) {
        [void]$weakFits.Add("Modern AAA gaming at high settings")
    }
    if (-not $hasDedicatedGpu -or $ramGb -lt 32) {
        [void]$weakFits.Add("Heavy AI/GPU rendering")
    }
    if ($batteryUnknown) {
        [void]$weakFits.Add("Long battery sessions until battery wear/runtime is verified")
    }

    $upgradeAdvice = New-Object System.Collections.Generic.List[string]
    if ($ramGb -gt 0 -and $ramGb -lt 16) {
        [void]$upgradeAdvice.Add("Upgrade to at least 16 GB RAM before resale or development workloads.")
    }
    if (-not $hasFastStorage) {
        [void]$upgradeAdvice.Add("Install or verify SSD/NVMe storage before listing.")
    }
    if ($batteryUnknown) {
        [void]$upgradeAdvice.Add("Run battery report/vendor diagnostics before advertising unplugged runtime.")
    }
    if ($upgradeAdvice.Count -eq 0) {
        [void]$upgradeAdvice.Add("Clean install/update drivers, verify thermals, photograph condition, and include charger details.")
    }

    $confidence = "High"
    if ($null -eq $Processor -or $ramGb -le 0 -or $Gpus.Count -eq 0 -or $DiskReports.Count -eq 0) {
        $confidence = "Medium"
    }
    if ($batteryUnknown) {
        $confidence = if ($confidence -eq "High") { "Medium" } else { $confidence }
    }

    $listing = if ($hasGamingGpu) {
        "Market as an entry/mid gaming laptop; include tested games/settings if possible."
    }
    elseif ($hasWorkstationGpu -or $primary -match "Workstation") {
        "Market as a mobile workstation/dev laptop, not primarily as a gaming laptop."
    }
    else {
        "Market as a budget school/office laptop; emphasize SSD, clean Windows install, and verified battery if available."
    }

    [ordered]@{
        primaryFit = $primary
        machineClass = if ($hasWorkstationGpu) { "Mobile Workstation" } elseif ($hasGamingGpu) { "Gaming Laptop" } elseif ($BatteryReports.Count -gt 0) { "Business/Consumer Laptop" } else { "Desktop PC / Mini PC" }
        confidence = $confidence
        strongFits = @($strongFits)
        weakFits = @($weakFits)
        exampleWorkloads = @("Roblox/Minecraft/indie games", "Office/school/productivity", "WSL/diagnostics/repair tools", "Older AAA titles when GPU/thermals allow")
        upgradeFirstAdvice = @($upgradeAdvice)
        listingPositioning = $listing
        reasons = @(
            [ordered]@{ text = ("{0}-core / {1}-thread CPU signal" -f $cores, $threads); evidence = $cpuName },
            [ordered]@{ text = ("{0:g} GB RAM" -f $ramGb); evidence = "Win32_ComputerSystem/PhysicalMemory" },
            [ordered]@{ text = if ($hasFastStorage) { "SSD/NVMe storage detected" } else { "SSD/NVMe storage not confirmed" }; evidence = (($DiskReports | ForEach-Object { ("{0} {1}" -f $_.name, $_.mediaType) }) -join "; ") },
            [ordered]@{ text = if ($hasDedicatedGpu) { "Dedicated GPU detected" } else { "Dedicated GPU not detected" }; evidence = $gpuText },
            [ordered]@{ text = if ($batteryUnknown) { "Battery wear/runtime confidence is lower because wear data was not exposed" } else { "Battery data available or no battery reported" }; evidence = "Battery report/WMI" }
        )
    }
}

function New-MachineClassReport {
    param(
        [object]$ComputerSystem,
        [object]$Processor,
        [Nullable[double]]$TotalMemoryBytes,
        [object[]]$Gpus,
        [object[]]$DiskReports,
        [object[]]$BatteryReports
    )

    $manufacturer = if ($null -ne $ComputerSystem) { [string]$ComputerSystem.Manufacturer } else { "" }
    $model = if ($null -ne $ComputerSystem) { [string]$ComputerSystem.Model } else { "" }
    $text = ("{0} {1}" -f $manufacturer, $model).Trim()
    $gpuText = (($Gpus | ForEach-Object { [string]$_.Name }) -join " ")
    $cpuName = Get-ProcessorName -Processor $Processor
    $ramGb = Convert-BytesToGigabytes -Bytes $TotalMemoryBytes
    $isLaptop = $BatteryReports.Count -gt 0 -or $text -match '(?i)latitude|thinkpad|elitebook|probook|zbook|precision|inspiron|pavilion|ideapad|xps|legion|rog|tuf|omen|victus|nitro|predator|notebook|laptop|surface'
    $hasWorkstationGpu = $gpuText -match '(?i)quadro|rtx\s+a\d|radeon\s+pro|firepro'
    $hasGamingGpu = $gpuText -match '(?i)geforce|gtx|rtx|radeon\s+rx'
    $scores = [ordered]@{
        "Business Laptop" = 0
        "Consumer Laptop" = 0
        "Gaming Laptop" = 0
        "Mobile Workstation" = 0
        "Desktop Workstation" = 0
        "Desktop PC" = 0
        "Mini PC" = 0
        "All-in-One" = 0
        "Server / Homelab" = 0
        "Repair / Parts Machine" = 0
    }
    $signals = New-Object System.Collections.Generic.List[object]
    $addScore = {
        param([string]$Key, [int]$Amount)
        if ($scores.Contains($Key)) { $scores[$Key] = [int]$scores[$Key] + $Amount }
    }
    $addSignal = {
        param([string]$Name, [string]$Value, [int]$Weight, [string]$Source)
        if (-not [string]::IsNullOrWhiteSpace($Value)) {
            [void]$signals.Add([ordered]@{ name = $Name; value = $Value; weight = $Weight; source = $Source })
        }
    }

    & $addSignal "OEM/model line" $text 10 "Win32_ComputerSystem"
    if ($isLaptop) {
        & $addScore "Business Laptop" 10
        & $addScore "Consumer Laptop" 8
        & $addScore "Mobile Workstation" 8
        & $addSignal "Battery/mobile chassis signal" $(if ($BatteryReports.Count -gt 0) { "Battery present" } else { "Laptop model-line hint" }) 12 "Battery/model heuristic"
    }
    else {
        & $addScore "Desktop PC" 18
        & $addSignal "No battery signal" "No battery exposed; likely desktop/mini/server unless model says otherwise." 8 "Battery inventory"
    }

    if ($text -match '(?i)precision|zbook|thinkpad\s*p|thinkpadp|p\d{2}\b' -or $hasWorkstationGpu) {
        & $addScore $(if ($isLaptop) { "Mobile Workstation" } else { "Desktop Workstation" }) 48
        & $addSignal "Workstation signal" $(if ($hasWorkstationGpu) { $gpuText } else { $text }) 48 "GPU/model heuristic"
    }
    if ($text -match '(?i)latitude|thinkpad\s*[tx]|elitebook|probook|surface\s+pro|xps') {
        & $addScore "Business Laptop" 38
        & $addSignal "Business-class OEM line" $text 38 "Model heuristic"
    }
    if ($text -match '(?i)omen|legion|rog|tuf|victus|nitro|predator|alienware|razer|msi' -or ($isLaptop -and $hasGamingGpu -and -not $hasWorkstationGpu)) {
        & $addScore "Gaming Laptop" 42
        & $addSignal "Gaming signal" $(if ($hasGamingGpu) { $gpuText } else { $text }) 42 "GPU/model heuristic"
    }
    if ($text -match '(?i)inspiron|pavilion|ideapad|vivobook|aspire|envy') {
        & $addScore "Consumer Laptop" 34
        & $addSignal "Consumer OEM line" $text 34 "Model heuristic"
    }
    if ($text -match '(?i)optiplex|elitedesk|thinkcentre|prodesk|vostro' -and -not $isLaptop) {
        & $addScore "Desktop PC" 30
        & $addScore "Server / Homelab" $(if ($ramGb -ge 32) { 12 } else { 6 })
        & $addSignal "Business desktop OEM line" $text 30 "Model heuristic"
    }
    if ($text -match '(?i)mini|micro|tiny|nuc|deskmini|beelink|minisforum') {
        & $addScore "Mini PC" 58
        & $addSignal "Mini PC line/chassis hint" $text 44 "Model heuristic"
    }
    if ($text -match '(?i)all.in.one|aio|inspiron\s+one|ideacentre\s+aio|pavilion\s+all') {
        & $addScore "All-in-One" 44
        & $addSignal "All-in-one model hint" $text 44 "Model heuristic"
    }
    if ($cpuName -match '(?i)xeon|epyc' -or $ramGb -ge 64 -or $DiskReports.Count -ge 3) {
        & $addScore "Server / Homelab" 28
        & $addSignal "Server/homelab signal" ("{0}; {1:g} GB RAM; {2} disk(s)" -f $cpuName, $ramGb, $DiskReports.Count) 28 "CPU/RAM/storage heuristic"
    }

    $ranked = @($scores.GetEnumerator() | Sort-Object -Property @{ Expression = { $_.Value }; Descending = $true }, @{ Expression = { $_.Name }; Ascending = $true })
    $best = $ranked | Select-Object -First 1
    $primary = if ($best.Value -ge 24) { [string]$best.Name } else { "Unknown / Mixed" }
    $secondary = @($ranked | Where-Object { $_.Name -ne $primary -and $_.Value -ge [math]::Max(18, ([int]$best.Value - 14)) } | Select-Object -First 3 | ForEach-Object { [string]$_.Name })
    $confidence = if ($best.Value -ge 58) { "High" } elseif ($best.Value -ge 34) { "Medium" } else { "Low" }
    $note = switch ($primary) {
        "Mobile Workstation" { "Classified as a mobile workstation because workstation model/GPU/RAM signals dominate."; break }
        "Gaming Laptop" { "Classified as a gaming laptop only when gaming model/GPU signals dominate."; break }
        "Business Laptop" { "Business-class laptop signals are stronger than consumer/gaming signals."; break }
        "Consumer Laptop" { "Consumer laptop model-line signals dominate."; break }
        "Mini PC" { "Mini/micro chassis signals dominate."; break }
        "Server / Homelab" { "Server/homelab signals come from CPU/RAM/storage layout; verify chassis and cooling manually."; break }
        default { "Signals are mixed or incomplete; verify chassis/model manually." }
    }

    [ordered]@{
        primaryClass = $primary
        secondaryClasses = @($secondary)
        confidence = $confidence
        technicianNote = $note
        signals = @($signals | Sort-Object weight -Descending | Select-Object -First 8)
    }
}

function New-SensorReading {
    param(
        [string]$Name,
        [string]$Category,
        [string]$Value,
        [string]$Unit,
        [string]$Status,
        [string]$Confidence,
        [string]$Source,
        [bool]$IsLive,
        [bool]$IsInferred,
        [bool]$IsUnavailable,
        [string]$UnavailableReason,
        [string]$TechnicianNote
    )

    [ordered]@{
        name = $Name
        category = $Category
        value = if ([string]::IsNullOrWhiteSpace($Value)) { "Not exposed" } else { $Value }
        unit = $Unit
        status = $Status
        confidence = $Confidence
        source = $Source
        lastUpdatedUtc = (Get-Date).ToUniversalTime().ToString("o")
        isLive = $IsLive
        isInferred = $IsInferred
        isUnavailable = $IsUnavailable
        unavailableReason = $UnavailableReason
        technicianNote = $TechnicianNote
    }
}

function New-SensorGroup {
    param([string]$Category, [object[]]$Readings)
    $known = @($Readings | Where-Object { -not $_.isUnavailable }).Count
    [ordered]@{
        category = $Category
        knownFields = $known
        totalFields = $Readings.Count
        summary = ("{0}/{1} fields known" -f $known, $Readings.Count)
        readings = @($Readings)
    }
}

function New-SensorMatrixReport {
    param(
        [object]$Processor,
        [object[]]$Gpus,
        [object[]]$DiskReports,
        [object[]]$BatteryReports,
        [object]$SecurityReport,
        [object]$SecureBootInfo,
        [object]$TpmInfo,
        [int]$PhysicalAdapterCount,
        [int]$VirtualAdapterCount,
        [bool]$InternetCheck
    )

    $cpuReadings = @(
        New-SensorReading "CPU model" "CPU" (Get-ProcessorName -Processor $Processor) "" "Ready" "High" "WMI/CIM Win32_Processor" $false $false $false "" "Processor identity is inventory data, not a live sensor."
        New-SensorReading "CPU cores" "CPU" $(if ($null -ne $Processor) { [string]$Processor.NumberOfCores } else { "" }) "cores" $(if ($null -ne $Processor) { "Ready" } else { "Unknown" }) $(if ($null -ne $Processor) { "High" } else { "Low" }) "WMI/CIM Win32_Processor.NumberOfCores" $false $false ($null -eq $Processor) "ProbeFailed" "Core count is unavailable only if the processor probe failed."
        New-SensorReading "CPU temperature" "CPU" "" "C" "Unknown" "Low" "ForgerEMS safe scan" $false $false $true "RequiresExternalProvider" "Windows did not expose CPU package temperature in the safe scan."
        New-SensorReading "CPU package power" "CPU" "" "W" "Unknown" "Low" "ForgerEMS safe scan" $false $false $true "RequiresExternalProvider" "Package power usually needs vendor counters or optional deep sensor provider."
    )

    $gpuReadings = New-Object System.Collections.Generic.List[object]
    foreach ($gpu in @($Gpus | Select-Object -First 3)) {
        [void]$gpuReadings.Add((New-SensorReading "GPU" "GPU" ([string]$gpu.Name) "" "Ready" "High" "WMI/CIM Win32_VideoController" $false $false $false "" ("Driver: {0}; type: {1}" -f $gpu.DriverVersion, (Get-GpuType -Name ([string]$gpu.Name)))))
    }
    if ($gpuReadings.Count -eq 0) {
        [void]$gpuReadings.Add((New-SensorReading "GPU inventory" "GPU" "" "" "Unknown" "Low" "WMI/CIM Win32_VideoController" $false $false $true "NotExposedByFirmware" "No GPU list was exposed in the scan."))
    }
    [void]$gpuReadings.Add((New-SensorReading "GPU temperature" "GPU" "" "C" "Unknown" "Low" "ForgerEMS safe scan" $false $false $true "RequiresVendorDriver" "GPU temperature often requires vendor driver counters or deep sensor mode."))
    [void]$gpuReadings.Add((New-SensorReading "GPU clocks/load" "GPU" "" "" "Unknown" "Low" "ForgerEMS safe scan" $false $false $true "RequiresExternalProvider" "GPU clocks/load need driver counters or optional deep sensor provider."))

    $batteryReadings = New-Object System.Collections.Generic.List[object]
    if ($BatteryReports.Count -eq 0) {
        [void]$batteryReadings.Add((New-SensorReading "Battery" "Battery" "" "" "NotExposed" "Low" "Win32_Battery/powercfg" $false $false $true "NotApplicable" "No battery exposed; normal for desktops/mini PCs."))
    }
    else {
        $battery = $BatteryReports[0]
        [void]$batteryReadings.Add((New-SensorReading "Battery charge" "Battery" ([string]$battery.estimatedChargeRemaining) "%" "Ready" "Medium" "Win32_Battery/powercfg" $true $false $false "" "Charge can be live-ish but may lag Windows reporting."))
        [void]$batteryReadings.Add((New-SensorReading "Battery wear" "Battery" $(if ($null -ne $battery.wearPercent) { [string]$battery.wearPercent } else { "" }) "%" $(if ($null -ne $battery.wearPercent) { "Ready" } else { "Unknown" }) $(if ($null -ne $battery.wearPercent) { "High" } else { "Low" }) "powercfg /batteryreport" $false $false ($null -eq $battery.wearPercent) "NotExposedByFirmware" "Firmware/Windows did not expose battery wear; do not treat as failure."))
        [void]$batteryReadings.Add((New-SensorReading "Battery cycle count" "Battery" $(if ($null -ne $battery.cycleCount) { [string]$battery.cycleCount } else { "" }) "cycles" $(if ($null -ne $battery.cycleCount) { "Ready" } else { "Unknown" }) $(if ($null -ne $battery.cycleCount) { "High" } else { "Low" }) "powercfg /batteryreport" $false $false ($null -eq $battery.cycleCount) "NotExposedByFirmware" "Cycle count is often hidden by firmware."))
        [void]$batteryReadings.Add((New-SensorReading "Battery discharge rate" "Battery" "" "W" "Unknown" "Low" "ForgerEMS safe scan" $false $false $true "NotExposedByFirmware" "Discharge rate was not normalized by the safe scan."))
    }

    $storageReadings = New-Object System.Collections.Generic.List[object]
    foreach ($disk in @($DiskReports | Select-Object -First 4)) {
        [void]$storageReadings.Add((New-SensorReading "Disk" "Storage" ("{0} {1} {2}" -f $disk.name, $disk.size, $disk.mediaType) "" "Ready" "High" "MSFT_PhysicalDisk / Win32_DiskDrive" $false $false $false "" ("Health: {0}; status: {1}" -f $disk.health, $disk.status)))
        [void]$storageReadings.Add((New-SensorReading ("{0} temperature" -f $disk.name) "Storage" $(if ($null -ne $disk.temperatureC) { [string]$disk.temperatureC } else { "" }) "C" $(if ($null -ne $disk.temperatureC) { "Ready" } else { "Unknown" }) $(if ($null -ne $disk.temperatureC) { "High" } else { "Low" }) "SMART/NVMe health where exposed" $false $false ($null -eq $disk.temperatureC) "NotExposedByFirmware" "Storage temperature is often hidden by USB bridges or firmware."))
        [void]$storageReadings.Add((New-SensorReading ("{0} wear" -f $disk.name) "Storage" $(if ($null -ne $disk.wearPercent) { [string]$disk.wearPercent } else { "" }) "%" $(if ($null -ne $disk.wearPercent) { "Ready" } else { "Unknown" }) $(if ($null -ne $disk.wearPercent) { "High" } else { "Low" }) "SMART/NVMe wear where exposed" $false $false ($null -eq $disk.wearPercent) "NotExposedByFirmware" "Wear data is not exposed by every disk/bridge."))
    }
    if ($storageReadings.Count -eq 0) {
        [void]$storageReadings.Add((New-SensorReading "Storage inventory" "Storage" "" "" "Unknown" "Low" "Storage probe" $false $false $true "ProbeFailed" "No storage devices were exposed in the scan."))
    }

    $networkReadings = @(
        New-SensorReading "Internet connectivity" "Network" $(if ($InternetCheck) { "Working" } else { "Not confirmed" }) "" "Ready" "Medium" "Connectivity/default-route summary" $false $false $false "" "Connectivity is summarized from route/DNS/probe behavior."
        New-SensorReading "Physical adapters" "Network" ([string]$PhysicalAdapterCount) "adapters" "Ready" "High" "Get-NetAdapter / Win32_NetworkAdapter" $false $false $false "" ""
        New-SensorReading "Virtual adapters" "Network" ([string]$VirtualAdapterCount) "adapters" "Ready" "High" "Get-NetAdapter / classification" $false $false $false "" ""
        New-SensorReading "Wi-Fi signal/generation" "Network" "" "" "Unknown" "Low" "ForgerEMS safe scan" $false $false $true "NotExposedByFirmware" "Wi-Fi signal/generation is shown only when Windows exposes adapter details."
    )

    $tpmSensorStatus = if ([string]::IsNullOrWhiteSpace([string]$TpmInfo.status) -or [string]$TpmInfo.status -eq "UNKNOWN") { "Unknown" } elseif ([string]$TpmInfo.status -eq "READY") { "Ready" } else { "Warning" }
    $secureBootSensorStatus = if ([string]::IsNullOrWhiteSpace([string]$SecureBootInfo.status) -or [string]$SecureBootInfo.status -eq "UNKNOWN") { "Unknown" } elseif ([string]$SecureBootInfo.status -eq "READY") { "Ready" } else { "Warning" }
    $securityReadings = @(
        New-SensorReading "TPM" "Security" ([string]$TpmInfo.friendlyDisplayText) "" $tpmSensorStatus $(if ($tpmSensorStatus -eq "Unknown") { "Low" } else { "Medium" }) "Get-Tpm / WMI fallback" $false $false ($tpmSensorStatus -eq "Unknown") "NotExposedByFirmware" "Unknown TPM state should be verified in BIOS/UEFI before calling it failed."
        New-SensorReading "Secure Boot" "Security" ([string]$SecureBootInfo.friendlyDisplayText) "" $secureBootSensorStatus $(if ($secureBootSensorStatus -eq "Unknown") { "Low" } else { "Medium" }) "Confirm-SecureBootUEFI / registry fallback" $false $false ($secureBootSensorStatus -eq "Unknown") "PermissionDenied" "Unknown Secure Boot does not prove disabled."
        New-SensorReading "Defender/Firewall" "Security" ("Defender AV: {0}; Firewall: {1}" -f $SecurityReport.antivirusEnabled, $SecurityReport.firewallEnabled) "" "Ready" "High" "Get-MpComputerStatus / firewall profile" $false $false $false "" ""
    )

    $usbReadings = @(
        New-SensorReading "USB controller inventory" "USB" "" "" "NotExposed" "Low" "USB Intelligence" $false $false $true "NotApplicable" "USB controller/device speed details are collected by USB Intelligence when a target is selected."
        New-SensorReading "USB benchmark" "USB" "" "" "NotExposed" "Low" "USB Builder benchmark" $false $false $true "NotApplicable" "USB read/write benchmark appears only after a safe target benchmark is run."
    )
    $coolingReadings = @(
        New-SensorReading "Fan RPM" "Cooling" "" "RPM" "Unknown" "Low" "ForgerEMS safe scan" $false $false $true "RequiresVendorDriver" "Windows/firmware did not expose fan RPM. That does not mean the fan is broken."
        New-SensorReading "Fan curve/control" "Cooling" "" "" "Unknown" "Low" "ForgerEMS safe scan" $false $false $true "UnsupportedHardware" "ForgerEMS does not change fan control."
    )

    $groups = @(
        New-SensorGroup "CPU" $cpuReadings
        New-SensorGroup "GPU" ($gpuReadings.ToArray())
        New-SensorGroup "Battery" ($batteryReadings.ToArray())
        New-SensorGroup "Storage" ($storageReadings.ToArray())
        New-SensorGroup "Network" $networkReadings
        New-SensorGroup "Security" $securityReadings
        New-SensorGroup "USB" $usbReadings
        New-SensorGroup "Cooling" $coolingReadings
    )
    $known = @($groups | ForEach-Object { $_.knownFields } | Measure-Object -Sum).Sum
    $total = @($groups | ForEach-Object { $_.totalFields } | Measure-Object -Sum).Sum
    $confidence = if ($total -gt 0 -and ($known / $total) -ge 0.7) { "High" } elseif ($total -gt 0 -and ($known / $total) -ge 0.45) { "Medium" } else { "Low" }
    [ordered]@{
        groups = @($groups)
        confidence = $confidence
        coverageSummary = (($groups | ForEach-Object { ("{0}: {1}/{2} fields known" -f $_.category, $_.knownFields, $_.totalFields) }) -join "; ")
        deepSensorModeNote = "Some sensors require admin access, firmware support, vendor drivers, or an optional reviewed sensor provider."
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
if ($tpmInfo.present -eq $false) {
    Add-Recommendation -Recommendations $recommendations -Text "TPM was not detected. Confirm firmware security settings before Windows 11 readiness claims."
    Add-UniqueText -Items $obviousProblems -Text "TPM was not detected."
}
elseif ($tpmInfo.present -eq $true -and $tpmInfo.ready -ne $true) {
    Add-Recommendation -Recommendations $recommendations -Text "TPM is present but not ready. Verify firmware TPM settings before Windows 11 readiness claims."
    Add-UniqueText -Items $obviousProblems -Text "TPM is present but not ready."
}
elseif ($null -eq $tpmInfo.present -or $null -eq $tpmInfo.ready) {
    Add-Recommendation -Recommendations $recommendations -Text "Verify TPM in BIOS/UEFI or vendor diagnostics; Windows did not expose enough data to confirm readiness."
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
        adapterRole = if ($isVirtual) { "VirtualAdapter" } elseif ($isDefaultRoute -or ($hasGateway -and -not $hasApipa)) { "ActivePhysicalInternet" } elseif ($description -match "(?i)wi-fi|wireless|wlan|802\.11") { "PhysicalDisconnected/Wi-Fi" } else { "PhysicalDisconnected" }
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
    "Virtual adapters ignored: {0}" -f ((@($virtualNetworkReport | ForEach-Object { [string]$_.name }) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join ", ")
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

$deviceFit = New-DeviceFitReport `
    -ComputerSystem $computerSystem `
    -OperatingSystem $operatingSystem `
    -Processor $processor `
    -TotalMemoryBytes $totalMemoryBytes `
    -Gpus $gpus `
    -DiskReports $diskReports `
    -BatteryReports $batteryReports

$machineClass = New-MachineClassReport `
    -ComputerSystem $computerSystem `
    -Processor $processor `
    -TotalMemoryBytes $totalMemoryBytes `
    -Gpus $gpus `
    -DiskReports $diskReports `
    -BatteryReports $batteryReports

$sensorMatrix = New-SensorMatrixReport `
    -Processor $processor `
    -Gpus $gpus `
    -DiskReports $diskReports `
    -BatteryReports $batteryReports `
    -SecurityReport $securityReport `
    -SecureBootInfo $secureBootInfo `
    -TpmInfo $tpmInfo `
    -PhysicalAdapterCount $physicalNetworkReport.Count `
    -VirtualAdapterCount $virtualNetworkReport.Count `
    -InternetCheck ([bool]$internetCheck)

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
        physicalAdapterCount = $physicalNetworkReport.Count
        virtualAdapterCount = $virtualNetworkReport.Count
        virtualAdaptersIgnored = $virtualIgnoredDisplay
        adapters = $networkReport
    }
    security = $securityReport
    obviousProblems = @($obviousProblems)
    flipValue = $flipValue
    machineClass = $machineClass
    sensorMatrix = $sensorMatrix
    deviceFit = $deviceFit
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
[void]$markdown.Add("## Machine Class / Hardware X-Ray")
[void]$markdown.Add(("- Machine class: {0} ({1} confidence)" -f $report.machineClass.primaryClass, $report.machineClass.confidence))
[void]$markdown.Add(("- Secondary classes: {0}" -f (($report.machineClass.secondaryClasses | Where-Object { $_ }) -join "; ")))
[void]$markdown.Add(("- Technician note: {0}" -f $report.machineClass.technicianNote))
[void]$markdown.Add(("- Sensor coverage: {0}" -f $report.sensorMatrix.coverageSummary))
[void]$markdown.Add(("- Sensor confidence: {0}" -f $report.sensorMatrix.confidence))
[void]$markdown.Add(("- Deep sensor note: {0}" -f $report.sensorMatrix.deepSensorModeNote))
[void]$markdown.Add("")
[void]$markdown.Add("### Machine Class Signals")
foreach ($signal in $report.machineClass.signals) {
    [void]$markdown.Add(("- {0}: {1} (weight {2}; source {3})" -f $signal.name, $signal.value, $signal.weight, $signal.source))
}
[void]$markdown.Add("")
[void]$markdown.Add("### Sensor Availability Matrix")
foreach ($group in $report.sensorMatrix.groups) {
    [void]$markdown.Add(("#### {0} ({1}/{2} fields known)" -f $group.category, $group.knownFields, $group.totalFields))
    foreach ($reading in $group.readings) {
        $value = if ($reading.isUnavailable) { ("{0}: {1}" -f $reading.unavailableReason, $reading.technicianNote) } else { ("{0}{1}" -f $reading.value, $(if ($reading.unit) { " " + $reading.unit } else { "" })) }
        [void]$markdown.Add(("- {0}: {1} [{2}, confidence {3}, source {4}]" -f $reading.name, $value, $reading.status, $reading.confidence, $reading.source))
    }
}
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
[void]$markdown.Add("## Best Use / Device Fit")
[void]$markdown.Add(("- Primary fit: {0}" -f $report.deviceFit.primaryFit))
[void]$markdown.Add(("- Machine class: {0}" -f $report.deviceFit.machineClass))
[void]$markdown.Add(("- Confidence: {0}" -f $report.deviceFit.confidence))
[void]$markdown.Add(("- Strong fits: {0}" -f (($report.deviceFit.strongFits | Where-Object { $_ }) -join "; ")))
[void]$markdown.Add(("- Weak fits: {0}" -f (($report.deviceFit.weakFits | Where-Object { $_ }) -join "; ")))
[void]$markdown.Add(("- Example workloads: {0}" -f (($report.deviceFit.exampleWorkloads | Where-Object { $_ }) -join "; ")))
[void]$markdown.Add(("- Upgrade-first advice: {0}" -f (($report.deviceFit.upgradeFirstAdvice | Where-Object { $_ }) -join "; ")))
[void]$markdown.Add(("- Listing angle: {0}" -f $report.deviceFit.listingPositioning))
[void]$markdown.Add("")
[void]$markdown.Add("### Device Fit Reasons")
foreach ($reason in $report.deviceFit.reasons) {
    [void]$markdown.Add(("- {0} ({1})" -f $reason.text, $reason.evidence))
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
