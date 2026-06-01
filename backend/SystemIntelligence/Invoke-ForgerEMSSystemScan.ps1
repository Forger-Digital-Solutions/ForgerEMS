#requires -Version 5.1

[CmdletBinding()]
param(
    [string]$OutputDirectory = "",
    [switch]$WriteElevatedScanMarkers
)

$ErrorActionPreference = "Stop"
$script:SystemIntelligenceLogPath = $null
$script:SystemIntelligenceLogFailed = $false
$script:OptionalProviderDiagnostics = New-Object System.Collections.Generic.List[object]

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

function Test-IsAdministrator {
    try {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = [Security.Principal.WindowsPrincipal]::new($identity)
        return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    catch {
        return $false
    }
}

function Resolve-OptionalProviderStatus {
    param(
        [string]$Message,
        [bool]$RequiresElevation,
        [bool]$AffectsReadiness
    )

    $safe = if ([string]::IsNullOrWhiteSpace($Message)) { "" } else { $Message.Trim() }
    if ($safe -match "(?i)access denied|unauthorized|not authorized|cim resource was not available|permission") {
        if ($RequiresElevation) {
            return "PermissionRequired"
        }

        return "ProviderUnavailable"
    }

    if ($safe -match "(?i)cannot find path|cannot find property|property .* does not exist|PEFirmwareType") {
        return "NotExposed"
    }

    if ($safe -match "(?i)timed out|timeout|operation has timed out") {
        return "Timeout"
    }

    if ($safe -match "(?i)generic failure|provider load failure|rpc server is unavailable|invalid class") {
        return "ProviderUnavailable"
    }

    if ($AffectsReadiness) {
        return "Failure"
    }

    return "ProviderUnavailable"
}

function New-OptionalProviderUserMessage {
    param(
        [string]$ProviderName,
        [string]$Status
    )

    $provider = if ([string]::IsNullOrWhiteSpace($ProviderName)) { "Optional provider" } else { $ProviderName }
    switch ($Status) {
        "PermissionRequired" { return ("{0} requires administrator permissions." -f $provider) }
        "NotExposed" { return ("{0} is not exposed by this Windows build/firmware." -f $provider) }
        "ProviderUnavailable" { return ("{0} provider unavailable; using safe fallback when possible." -f $provider) }
        "Timeout" { return ("{0} timed out; continuing with available scan data." -f $provider) }
        "Failure" { return ("{0} failed and affected required scan coverage." -f $provider) }
        default { return ("{0} was unavailable; continuing with available scan data." -f $provider) }
    }
}

function Add-OptionalProviderDiagnostic {
    param(
        [string]$ProviderName,
        [string]$Category,
        [string]$Status,
        [bool]$RequiresElevation,
        [bool]$AffectsReadiness,
        [Nullable[int]]$TimeoutSeconds,
        [string]$UserMessage,
        [string]$DiagnosticMessage
    )

    $script:OptionalProviderDiagnostics.Add([ordered]@{
        providerName = $ProviderName
        category = $Category
        status = $Status
        requiresElevation = $RequiresElevation
        impactsReadiness = $AffectsReadiness
        timeoutSeconds = $TimeoutSeconds
        userMessage = $UserMessage
        diagnosticMessage = $DiagnosticMessage
        timestampUtc = (Get-Date).ToUniversalTime().ToString("o")
    }) | Out-Null
}

function Invoke-Optional {
    param(
        [Parameter(Mandatory)][scriptblock]$ScriptBlock,
        [object]$Default = $null,
        [string]$ProviderName = "Optional provider",
        [string]$Category = "General",
        [Nullable[int]]$TimeoutSeconds = $null,
        [bool]$RequiresElevation = $false,
        [bool]$AffectsReadiness = $false
    )

    try {
        return & $ScriptBlock
    }
    catch {
        $diag = $_.Exception.Message
        $status = Resolve-OptionalProviderStatus -Message $diag -RequiresElevation $RequiresElevation -AffectsReadiness $AffectsReadiness
        $safeMessage = New-OptionalProviderUserMessage -ProviderName $ProviderName -Status $status
        $level = if ($status -eq "Failure") { "WARN" } else { "INFO" }
        Write-ScanLog $safeMessage $level
        Add-OptionalProviderDiagnostic `
            -ProviderName $ProviderName `
            -Category $Category `
            -Status $status `
            -RequiresElevation $RequiresElevation `
            -AffectsReadiness $AffectsReadiness `
            -TimeoutSeconds $TimeoutSeconds `
            -UserMessage $safeMessage `
            -DiagnosticMessage $diag
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
    $controlPath = "HKLM:\SYSTEM\CurrentControlSet\Control"
    $firmwareType = $null
    if (Test-Path -LiteralPath $controlPath) {
        try {
            $item = Get-ItemProperty -Path $controlPath -ErrorAction Stop
            if ($item.PSObject.Properties.Name -contains "PEFirmwareType") {
                $firmwareType = $item.PEFirmwareType
            }
            else {
                Write-ScanLog "Secure Boot firmware marker not exposed by this Windows build/firmware." "INFO"
                Add-OptionalProviderDiagnostic `
                    -ProviderName "Secure Boot firmware marker" `
                    -Category "Security" `
                    -Status "NotExposed" `
                    -RequiresElevation $false `
                    -AffectsReadiness $false `
                    -TimeoutSeconds $null `
                    -UserMessage "Secure Boot firmware marker not exposed by this Windows build/firmware." `
                    -DiagnosticMessage "PEFirmwareType registry value not present."
            }
        }
        catch {
            Write-ScanLog "Secure Boot firmware marker provider unavailable; using Windows firmware API fallback." "INFO"
            Add-OptionalProviderDiagnostic `
                -ProviderName "Secure Boot firmware marker" `
                -Category "Security" `
                -Status "ProviderUnavailable" `
                -RequiresElevation $false `
                -AffectsReadiness $false `
                -TimeoutSeconds $null `
                -UserMessage "Secure Boot firmware marker provider unavailable; using Windows firmware API fallback." `
                -DiagnosticMessage $_.Exception.Message
        }
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
        Write-ScanLog "Battery wear provider unavailable; using powercfg fallback." "INFO"
        Add-OptionalProviderDiagnostic `
            -ProviderName "Battery wear provider" `
            -Category "Battery" `
            -Status "ProviderUnavailable" `
            -RequiresElevation $false `
            -AffectsReadiness $false `
            -TimeoutSeconds $null `
            -UserMessage "Battery wear provider unavailable; using powercfg fallback." `
            -DiagnosticMessage $_.Exception.Message
        return $null
    }
}

function Convert-PortPowerMilliValue {
    param(
        [object]$Value,
        [double]$UpperBound
    )

    if ($null -eq $Value) {
        return $null
    }

    try {
        $number = [double]$Value
    }
    catch {
        return $null
    }

    if ($number -le 0) {
        return $null
    }

    $normalized = if ($number -gt 100) { $number / 1000.0 } else { $number }
    if ($normalized -gt 0 -and $normalized -lt $UpperBound) {
        return [math]::Round($normalized, 3)
    }

    return $null
}

function Get-PortPowerDeepTelemetry {
    param([bool]$IsElevated)

    $collectedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    if (-not $IsElevated) {
        return [ordered]@{
            collectedAtUtc = $collectedAtUtc
            source = "System Intelligence standard scan"
            status = "NotRun"
            confidence = "Unavailable"
            effectiveChargeRateWatts = $null
            adapterWattageWatts = $null
            adapterWattageClassWatts = $null
            voltageVolts = $null
            currentAmps = $null
            sourceHints = @()
            evidence = @("Standard scan intentionally does not collect admin-gated port power telemetry.")
            missingTelemetryReason = "Run Elevated Scan to unlock deeper port and charging telemetry."
        }
    }

    Write-ScanLog "Checking read-only port power telemetry exposed to elevated Windows APIs."
    $evidence = New-Object System.Collections.Generic.List[string]
    $sourceHints = New-Object System.Collections.Generic.List[string]
    $effectiveWatts = $null
    $voltageVolts = $null
    $currentAmps = $null

    $batteryStatuses = @(Invoke-Optional {
        Get-CimInstance -Namespace "root\wmi" -ClassName BatteryStatus -ErrorAction Stop
    } @() -ProviderName "Battery charge telemetry" -Category "Battery" -RequiresElevation $true)
    foreach ($batteryStatus in @($batteryStatuses | Select-Object -First 1)) {
        $chargeRate = if ($batteryStatus.PSObject.Properties.Name -contains "ChargeRate") { $batteryStatus.ChargeRate } else { $null }
        $dischargeRate = if ($batteryStatus.PSObject.Properties.Name -contains "DischargeRate") { $batteryStatus.DischargeRate } else { $null }
        if ($null -ne $chargeRate -and [double]$chargeRate -gt 0) {
            $effectiveWatts = [math]::Round(([double]$chargeRate / 1000.0), 3)
        }
        elseif ($null -ne $dischargeRate -and [double]$dischargeRate -gt 0) {
            $effectiveWatts = [math]::Round(-([double]$dischargeRate / 1000.0), 3)
        }

        $voltageVolts = Convert-PortPowerMilliValue -Value $(if ($batteryStatus.PSObject.Properties.Name -contains "Voltage") { $batteryStatus.Voltage } else { $null }) -UpperBound 60
        $currentAmps = Convert-PortPowerMilliValue -Value $(if ($batteryStatus.PSObject.Properties.Name -contains "Current") { $batteryStatus.Current } else { $null }) -UpperBound 30
        [void]$evidence.Add("root\wmi BatteryStatus exposed read-only battery charge telemetry.")
        break
    }

    $pnpPowerDevices = @(Invoke-Optional {
        Get-CimInstance -ClassName Win32_PnPEntity -Filter "Name LIKE '%USB%' OR Name LIKE '%Thunderbolt%' OR Name LIKE '%UCSI%' OR Name LIKE '%Dock%'" -ErrorAction Stop |
            Select-Object -First 12 Name, Description
    } @() -ProviderName "USB/Thunderbolt dock telemetry" -Category "USB" -RequiresElevation $true)
    foreach ($device in $pnpPowerDevices) {
        $label = ("{0} {1}" -f $device.Name, $device.Description).Trim()
        if ([string]::IsNullOrWhiteSpace($label)) {
            continue
        }

        if ($label -match "(?i)dock|docking station|usb-c|type-c|usb4|thunderbolt|ucsi|power delivery") {
            [void]$sourceHints.Add($label)
        }
    }

    if ($sourceHints.Count -gt 0) {
        [void]$evidence.Add("Elevated PnP enumeration exposed USB-C/Thunderbolt/dock source hints.")
    }

    $hasDirect = $null -ne $effectiveWatts -or $null -ne $voltageVolts -or $null -ne $currentAmps
    $confidence = if ($hasDirect) { "High" } elseif ($sourceHints.Count -gt 0) { "Medium" } else { "Unavailable" }
    $missing = if ($hasDirect) {
        ""
    }
    else {
        "Elevated Scan completed, but this device did not expose deeper port or charging telemetry. ForgerEMS can still estimate charging from battery behavior."
    }

    return [ordered]@{
        collectedAtUtc = $collectedAtUtc
        source = "System Intelligence Elevated Scan"
        status = if ($hasDirect -or $sourceHints.Count -gt 0) { "Ready" } else { "NotExposed" }
        confidence = $confidence
        effectiveChargeRateWatts = $effectiveWatts
        adapterWattageWatts = $null
        adapterWattageClassWatts = $null
        voltageVolts = $voltageVolts
        currentAmps = $currentAmps
        sourceHints = @($sourceHints.ToArray())
        evidence = @($evidence.ToArray())
        missingTelemetryReason = $missing
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

function Write-PhaseTiming {
    param(
        [Parameter(Mandatory)][string]$PhaseName,
        [Parameter(Mandatory)][System.Diagnostics.Stopwatch]$Stopwatch,
        [Parameter(Mandatory)][ref]$LastMs
    )

    $nowMs = [int64]$Stopwatch.ElapsedMilliseconds
    $phaseMs = [int64]($nowMs - $LastMs.Value)
    if ($phaseMs -lt 0) {
        $phaseMs = 0
    }

    Write-ScanLog ("Timing phase {0}: {1} ms (elapsed {2} ms)" -f $PhaseName, $phaseMs, $nowMs)
    $LastMs.Value = $nowMs
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
        Add-UniqueText -Items $valueDrivers -Text ("{0:0.#} GB RAM meets a strong resale baseline." -f $ramGb)
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

function New-ThirdPartyNotice {
    param(
        [string]$Name,
        [string]$Version,
        [string]$License,
        [string]$ProjectUrl,
        [string]$BundledPath,
        [string]$SourceOfferOrNotice,
        [bool]$ModifiedFilesDisclosureNeeded
    )

    [ordered]@{
        name = $Name
        version = $Version
        license = $License
        projectUrl = $ProjectUrl
        bundledPath = $BundledPath
        sourceOfferOrNotice = $SourceOfferOrNotice
        modifiedFilesDisclosureNeeded = $ModifiedFilesDisclosureNeeded
    }
}

function New-SensorProviderCapabilities {
    param(
        [string[]]$SupportedCapabilities,
        [string[]]$MissingCapabilities
    )

    [ordered]@{
        supportedCapabilities = @($SupportedCapabilities)
        missingCapabilities = @($MissingCapabilities)
        readOnlyGuarantees = @(
            "No fan control"
            "No voltage control"
            "No clock control"
            "No BIOS or firmware writes"
        )
    }
}

function New-SensorProviderManifest {
    param(
        [string]$ProviderName,
        [string]$ProviderVersion,
        [string]$ProviderKind,
        [bool]$IsBundled,
        [bool]$IsEnabled,
        [bool]$RequiresAdmin,
        [bool]$RequiresThirdPartyLicenseNotice,
        [string]$TrustLevel,
        [string]$RuntimeMode,
        [object]$Capabilities,
        [string]$FailureReason,
        [object[]]$Readings,
        [string[]]$TechnicianNotes,
        [object]$ThirdPartyNotice = $null
    )

    [ordered]@{
        providerName = $ProviderName
        providerVersion = $ProviderVersion
        providerKind = $ProviderKind
        isBundled = $IsBundled
        isEnabled = $IsEnabled
        requiresAdmin = $RequiresAdmin
        requiresThirdPartyLicenseNotice = $RequiresThirdPartyLicenseNotice
        isReadOnly = $true
        trustLevel = $TrustLevel
        runtimeMode = $RuntimeMode
        capabilities = $Capabilities
        failureReason = $FailureReason
        lastRunUtc = (Get-Date).ToUniversalTime().ToString("o")
        readings = @($Readings)
        technicianNotes = @($TechnicianNotes)
        thirdPartyNotice = $ThirdPartyNotice
    }
}

function Resolve-ForgerDeepSensorMode {
    $notice = "Deep Sensor Mode reads local hardware sensor data only while ForgerEMS is running or scanning. No sensor control or cloud service is used."

    function New-DeepSensorModeResolution {
        param(
            [string]$Mode,
            [string]$Source,
            [bool]$Invalid = $false,
            [string]$Note = ""
        )

        $enabled = $Mode -eq "ReadOnly"
        [ordered]@{
            value = $Mode
            source = $Source
            enabled = $enabled
            readOnly = $true
            noControlCapabilities = $true
            isInvalid = $Invalid
            noticeText = $notice
            technicianNote = $(if ($Note) { $Note } elseif ($enabled) { "ForgerEMS Deep Sensor Mode is ReadOnly via $Source. Sensors are local and read-only." } else { "Deep Sensor Mode is Off via $Source." })
        }
    }

    function Normalize-DeepSensorMode {
        param([string]$Value, [string]$Source)
        if ([string]::IsNullOrWhiteSpace($Value)) {
            return $null
        }

        $trimmed = $Value.Trim()
        if ($trimmed -ieq "Off") {
            return New-DeepSensorModeResolution -Mode "Off" -Source $Source
        }
        if ($trimmed -ieq "ReadOnly" -or $trimmed -ieq "ReadOnlyLocalSensors") {
            return New-DeepSensorModeResolution -Mode "ReadOnly" -Source $Source
        }
        if ($trimmed -ieq "AdminReadOnly") {
            return New-DeepSensorModeResolution -Mode "AdminReadOnly" -Source $Source -Note "AdminReadOnly is reserved for future explicit admin scans; current beta does not auto-elevate."
        }

        return New-DeepSensorModeResolution -Mode "Off" -Source $Source -Invalid $true -Note "Invalid Deep Sensor Mode value from $Source; using Off."
    }

    $envResolution = Normalize-DeepSensorMode -Value $env:FORGEREMS_DEEP_SENSOR_MODE -Source "Environment"
    if ($null -ne $envResolution) { return $envResolution }

    try {
        $userPath = Join-Path -Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) -ChildPath "ForgerEMS\settings\deep-sensor-mode.txt"
        if (Test-Path -LiteralPath $userPath) {
            $userResolution = Normalize-DeepSensorMode -Value (Get-Content -LiteralPath $userPath -Raw) -Source "UserSetting"
            if ($null -ne $userResolution) { return $userResolution }
        }
    }
    catch {
        Write-Warning "Deep Sensor Mode user setting could not be read: $($_.Exception.Message)"
    }

    try {
        $installValue = (Get-ItemProperty -Path "HKLM:\Software\ForgerEMS" -Name "DeepSensorMode" -ErrorAction Stop).DeepSensorMode
        $installResolution = Normalize-DeepSensorMode -Value ([string]$installValue) -Source "InstallerDefault"
        if ($null -ne $installResolution) { return $installResolution }
    }
    catch {
        # No installer default is normal for portable builds.
    }

    return New-DeepSensorModeResolution -Mode "Off" -Source "BuiltInDefault"
}

function New-SensorProviderReport {
    param([object[]]$BuiltInReadings)

    $deepProviderPath = Join-Path -Path $PSScriptRoot -ChildPath "..\..\providers\sensors\LibreHardwareMonitorLib.dll"
    $deepPackaged = Test-Path -LiteralPath $deepProviderPath
    $deepModeResolution = Resolve-ForgerDeepSensorMode
    $deepMode = [string]$deepModeResolution.value
    $deepEnabled = $deepPackaged -and [bool]$deepModeResolution.enabled
    @(
        New-SensorProviderManifest `
            -ProviderName "Windows Native" `
            -ProviderVersion "1.0" `
            -ProviderKind "WindowsBuiltInSensorProvider" `
            -IsBundled $true `
            -IsEnabled $true `
            -RequiresAdmin $false `
            -RequiresThirdPartyLicenseNotice $false `
            -TrustLevel "BuiltInWindows" `
            -RuntimeMode "DefaultSafe" `
            -Capabilities (New-SensorProviderCapabilities `
                -SupportedCapabilities @("WMI/CIM hardware inventory", "MSFT_PhysicalDisk and MSFT_StorageReliabilityCounter where exposed", "powercfg and Win32_Battery fields", "safe performance counters", "DX/WMI GPU inventory", "security posture APIs", "ForgerEMS USB Intelligence evidence") `
                -MissingCapabilities @("CPU/GPU temperatures on many systems", "Fan RPM without vendor/deep provider support", "Package power without vendor/deep provider support")) `
            -FailureReason "" `
            -Readings @($BuiltInReadings) `
            -TechnicianNotes @("Active by default. Uses local Windows APIs and ForgerEMS reports only.", "No internet, cloud service, or user-downloaded sensor tool is required.")

        New-SensorProviderManifest `
            -ProviderName "LibreHardwareMonitor" `
            -ProviderVersion "0.9.6" `
            -ProviderKind "LibreHardwareMonitorSensorProvider" `
            -IsBundled $deepPackaged `
            -IsEnabled $deepEnabled `
            -RequiresAdmin $false `
            -RequiresThirdPartyLicenseNotice $true `
            -TrustLevel $(if ($deepPackaged) { "BundledReviewed" } else { "ExperimentalDisabled" }) `
            -RuntimeMode $(if ($deepEnabled) { "DeepSensorReadOnly" } else { "Disabled" }) `
            -Capabilities (New-SensorProviderCapabilities `
                -SupportedCapabilities @("Read-only CPU temperature when exposed", "Read-only CPU package power when exposed", "Read-only CPU clocks/load when exposed", "Read-only GPU temperature/clocks/load when exposed", "Read-only fan RPM when exposed", "Read-only storage temperature/wear when exposed", "Read-only voltage display when exposed") `
                -MissingCapabilities @("Sensors blocked by firmware/vendor drivers", "Sensors requiring admin access", "Unsupported hardware sensors")) `
            -FailureReason $(if (-not $deepPackaged) { "LibreHardwareMonitor provider assembly is not packaged in this build." } elseif (-not $deepEnabled) { "Deep Sensor Mode is $deepMode via $($deepModeResolution.source). Enable Read-only local sensors in Settings or set FORGEREMS_DEEP_SENSOR_MODE=ReadOnly for testing." } else { "" }) `
            -Readings @() `
            -TechnicianNotes @($(if ($deepEnabled) { "LibreHardwareMonitor: active read-only through the WPF sensor host when System Intelligence runs in-app." } elseif ($deepPackaged) { "LibreHardwareMonitor: bundled but disabled." } else { "LibreHardwareMonitor: not packaged in this build." }), "Deep Sensor Mode is $deepMode via $($deepModeResolution.source).", "Deep Sensor Mode is local and read-only.", "ForgerEMS does not expose fan control, voltage control, clock control, overclocking, undervolting, firmware writes, or BIOS-write actions.") `
            -ThirdPartyNotice (New-ThirdPartyNotice "LibreHardwareMonitor" "0.9.6" "MPL-2.0" "https://github.com/LibreHardwareMonitor/LibreHardwareMonitor" "providers/sensors/LibreHardwareMonitorLib.dll" "ForgerEMS uses the unmodified NuGet package LibreHardwareMonitorLib and ships MPL-2.0 notices with installed and portable builds." $false)

        New-SensorProviderManifest `
            -ProviderName "ForgerEMS Admin Sensor Bridge" `
            -ProviderVersion "0.1-design" `
            -ProviderKind "AdminReadOnlyBridgeShell" `
            -IsBundled $false `
            -IsEnabled $false `
            -RequiresAdmin $true `
            -RequiresThirdPartyLicenseNotice $false `
            -TrustLevel "AdminRequired" `
            -RuntimeMode "Disabled" `
            -Capabilities (New-SensorProviderCapabilities -SupportedCapabilities @("Future on-demand admin read-only deep scan IPC") -MissingCapabilities @("Signed bridge binary not included", "UAC opt-in not implemented")) `
            -FailureReason "Design scaffold only; not enabled in this beta." `
            -Readings @() `
            -TechnicianNotes @("Deep Sensor Mode may require admin access. It only reads supported sensors and does not change fan, voltage, clock, or firmware settings.")

        New-SensorProviderManifest `
            -ProviderName "ForgerEMS Signed Driver Provider" `
            -ProviderVersion "roadmap" `
            -ProviderKind "FutureReadOnlyDriver" `
            -IsBundled $false `
            -IsEnabled $false `
            -RequiresAdmin $false `
            -RequiresThirdPartyLicenseNotice $false `
            -TrustLevel "ExperimentalDisabled" `
            -RuntimeMode "Disabled" `
            -Capabilities (New-SensorProviderCapabilities -SupportedCapabilities @("Future read-only sensors unavailable to user-mode providers") -MissingCapabilities @("No driver included in current beta")) `
            -FailureReason "Not included. Future releases would require Microsoft driver signing and installer-managed distribution." `
            -Readings @() `
            -TechnicianNotes @("Driver path is documentation-only for this beta. Users do not need to download it separately.")
    )
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
        [bool]$InternetCheck,
        [object]$UsbIntelligenceReport = $null
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

    $usbReadings = New-Object System.Collections.Generic.List[object]
    if ($null -ne $UsbIntelligenceReport) {
        $usbDiag = $UsbIntelligenceReport.usbDiagnostics
        if ($null -ne $usbDiag -and $usbDiag.usbProfileKnownPortsCount -gt 0) {
            [void]$usbReadings.Add((New-SensorReading "USB mapped ports" "USB" ([string]$usbDiag.usbProfileKnownPortsCount) "ports" "Ready" "Medium" "USB Intelligence profile" $false $false $false "" "Saved mapped port labels/profiles are available."))
        }
        if ($null -ne $usbDiag -and -not [string]::IsNullOrWhiteSpace([string]$usbDiag.usbCurrentTargetRiskSummary)) {
            [void]$usbReadings.Add((New-SensorReading "USB target risk" "USB" ([string]$usbDiag.usbCurrentTargetRiskSummary).TrimEnd('.') "" "Ready" "Medium" "USB Intelligence diagnostics" $false $false $false "" "Current safe target risk is summarized by USB Builder."))
        }
        if ($null -ne $usbDiag -and -not [string]::IsNullOrWhiteSpace([string]$usbDiag.usbBestKnownPortSummary)) {
            [void]$usbReadings.Add((New-SensorReading "Best measured port" "USB" ([string]$usbDiag.usbBestKnownPortSummary) "" "Ready" "Medium" "USB Builder benchmark/profile" $false $false $false "" "Best known write speed is based on ForgerEMS benchmark/profile data."))
        }
        if ($null -ne $usbDiag -and $null -ne $usbDiag.lastBenchmark -and $usbDiag.lastBenchmark.succeeded) {
            [void]$usbReadings.Add((New-SensorReading "USB benchmark" "USB" ([string]$usbDiag.lastBenchmark.summaryLine) "" "Ready" "Medium" "USB Builder benchmark" $false $false $false "" ([string]$usbDiag.lastBenchmark.benchmarkConfidence)))
        }
        if ($null -ne $UsbIntelligenceReport.topologyDiff -and -not [string]::IsNullOrWhiteSpace([string]$UsbIntelligenceReport.topologyDiff.summaryLine)) {
            [void]$usbReadings.Add((New-SensorReading "USB topology" "USB" ([string]$UsbIntelligenceReport.topologyDiff.summaryLine) "" "Ready" "Medium" "USB Intelligence topology diff" $false $false $false "" "Topology status was available from the USB Intelligence report."))
        }
    }
    if ($usbReadings.Count -eq 0) {
        [void]$usbReadings.Add((New-SensorReading "USB controller inventory" "USB" "" "" "NotExposed" "Low" "USB Intelligence" $false $false $true "NotApplicable" "USB controller/device speed details are collected by USB Intelligence when a target is selected."))
        [void]$usbReadings.Add((New-SensorReading "USB benchmark" "USB" "" "" "NotExposed" "Low" "USB Builder benchmark" $false $false $true "NotApplicable" "USB read/write benchmark appears only after a safe target benchmark is run."))
    }
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
        New-SensorGroup "USB" ($usbReadings.ToArray())
        New-SensorGroup "Cooling" $coolingReadings
    )
    $builtInReadings = @($groups | ForEach-Object { $_.readings })
    $sensorProviders = @(New-SensorProviderReport -BuiltInReadings $builtInReadings)
    $deepSensorMode = Resolve-ForgerDeepSensorMode
    $deepProvider = @($sensorProviders | Where-Object { $_.providerName -eq "LibreHardwareMonitor" } | Select-Object -First 1)
    $deepSensorMode["providerActive"] = if ($deepProvider.Count -gt 0) { [bool]$deepProvider[0].isEnabled } else { $false }
    $deepSensorMode["providerBundled"] = if ($deepProvider.Count -gt 0) { [bool]$deepProvider[0].isBundled } else { $false }
    $known = @($groups | ForEach-Object { $_.knownFields } | Measure-Object -Sum).Sum
    $total = @($groups | ForEach-Object { $_.totalFields } | Measure-Object -Sum).Sum
    $confidence = if ($total -gt 0 -and ($known / $total) -ge 0.7) { "High" } elseif ($total -gt 0 -and ($known / $total) -ge 0.45) { "Medium" } else { "Low" }
    [ordered]@{
        groups = @($groups)
        sensorProviders = @($sensorProviders)
        deepSensorMode = $deepSensorMode
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
if ($WriteElevatedScanMarkers) {
    $heartbeatPath = Join-Path $OutputDirectory "elevated-scan-heartbeat.json"
    $heartbeatPayload = [ordered]@{
        kind = "elevated-scan-heartbeat"
        utc  = (Get-Date).ToUniversalTime().ToString("o")
        pid  = $PID
        phase = "scan-body-started"
    }
    $heartbeatPayload | ConvertTo-Json | Set-Content -LiteralPath $heartbeatPath -Encoding UTF8
}
$jsonPath = Join-Path $OutputDirectory "system-intelligence-latest.json"
$markdownPath = Join-Path $OutputDirectory "flip-report-latest.md"
$usbIntelligencePath = Join-Path $OutputDirectory "usb-intelligence-latest.json"
$usbIntelligenceReport = Invoke-Optional {
    if (Test-Path -LiteralPath $usbIntelligencePath) {
        Get-Content -LiteralPath $usbIntelligencePath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    }
}
$recommendations = New-Object System.Collections.Generic.List[string]
$obviousProblems = New-Object System.Collections.Generic.List[string]

Write-ScanLog "ForgerEMS System Intelligence scan started."
Write-ScanLog "Collecting OS, CPU, RAM, GPU, disk, battery, network, and security data."
$scanStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$lastPhaseMs = 0L

$computerSystem = Invoke-Optional { Get-CimInstance -ClassName Win32_ComputerSystem -ErrorAction Stop } -ProviderName "Computer system inventory" -Category "OS/Hardware"
$operatingSystem = Invoke-Optional { Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction Stop } -ProviderName "Operating system inventory" -Category "OS/Hardware"
$bios = Invoke-Optional { Get-CimInstance -ClassName Win32_BIOS -ErrorAction Stop } -ProviderName "BIOS inventory" -Category "Firmware"
$tpmInfo = Get-TpmInfo
$secureBootInfo = Get-SecureBootInfo
Write-PhaseTiming -PhaseName "TPM/Secure Boot" -Stopwatch $scanStopwatch -LastMs ([ref]$lastPhaseMs)
$processor = Invoke-Optional { Get-CimInstance -ClassName Win32_Processor -ErrorAction Stop | Select-Object -First 1 } -ProviderName "CPU inventory" -Category "OS/CPU/RAM/GPU"
$gpus = @(Invoke-Optional { Get-CimInstance -ClassName Win32_VideoController -ErrorAction Stop } @() -ProviderName "GPU inventory" -Category "OS/CPU/RAM/GPU")
$batteries = @(Invoke-Optional { Get-CimInstance -ClassName Win32_Battery -ErrorAction Stop } @() -ProviderName "Battery base inventory" -Category "Battery")
$batteryStaticData = @(Invoke-Optional { Get-CimInstance -Namespace "root\wmi" -ClassName BatteryStaticData -ErrorAction Stop } @() -ProviderName "Battery static data" -Category "Battery")
$batteryFullChargedCapacity = @(Invoke-Optional { Get-CimInstance -Namespace "root\wmi" -ClassName BatteryFullChargedCapacity -ErrorAction Stop } @() -ProviderName "Battery full charge capacity" -Category "Battery")
$batteryCycleCount = @(Invoke-Optional { Get-CimInstance -Namespace "root\wmi" -ClassName BatteryCycleCount -ErrorAction Stop } @() -ProviderName "Battery cycle count provider" -Category "Battery")
$networkAdapters = @(Invoke-Optional { Get-CimInstance -ClassName Win32_NetworkAdapterConfiguration -Filter "IPEnabled = True" -ErrorAction Stop } @() -ProviderName "Network adapter configuration" -Category "Network")
$netAdapters = @(Invoke-Optional { Get-NetAdapter -ErrorAction Stop } @() -ProviderName "Network adapter inventory" -Category "Network")
$physicalDisks = @(Invoke-Optional { Get-PhysicalDisk -ErrorAction Stop } @() -ProviderName "Physical disk inventory" -Category "Disk inventory")
$smartPredictFailures = @(Invoke-Optional { Get-CimInstance -Namespace "root\wmi" -ClassName MSStorageDriver_FailurePredictStatus -ErrorAction Stop } @() -ProviderName "SMART failure predictor" -Category "Disk inventory" -RequiresElevation $true)
$logicalDisks = @(Invoke-Optional { Get-CimInstance -ClassName Win32_LogicalDisk -Filter "DriveType = 3" -ErrorAction Stop } @() -ProviderName "Logical disk inventory" -Category "Disk inventory")
$memoryModules = @(Invoke-Optional { Get-CimInstance -ClassName Win32_PhysicalMemory -ErrorAction Stop } @() -ProviderName "Memory module inventory" -Category "OS/CPU/RAM/GPU")
$memoryArrays = @(Invoke-Optional { Get-CimInstance -ClassName Win32_PhysicalMemoryArray -ErrorAction Stop } @() -ProviderName "Memory slot inventory" -Category "OS/CPU/RAM/GPU")
$displays = @(Invoke-Optional { Get-CimInstance -ClassName Win32_DesktopMonitor -ErrorAction Stop } @() -ProviderName "Display inventory" -Category "OS/CPU/RAM/GPU")
$bitLockerVolumes = @(Invoke-Optional { Get-BitLockerVolume -ErrorAction Stop } @() -ProviderName "BitLocker volume provider" -Category "Security" -RequiresElevation $true)
$licenseProduct = Invoke-Optional {
    Get-CimInstance -ClassName SoftwareLicensingProduct -ErrorAction Stop |
        Where-Object { $_.PartialProductKey -and $_.Name -match 'Windows' } |
        Select-Object -First 1
}
$wifiInterfaceText = Invoke-Optional { netsh wlan show interfaces 2>$null | Out-String } "" -ProviderName "Wi-Fi interface status" -Category "Network"
$wifiState = Get-WifiState -NetshText $wifiInterfaceText
$batteryReportFallback = Get-BatteryReportData
$portPowerDeepTelemetry = Get-PortPowerDeepTelemetry -IsElevated (Test-IsAdministrator)
Write-PhaseTiming -PhaseName "OS/CPU/RAM/GPU" -Stopwatch $scanStopwatch -LastMs ([ref]$lastPhaseMs)

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
    29 { "LPDDR3" }
    30 { "LPDDR4" }
    35 { "LPDDR5" }
    36 { "LPDDR5X" }
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
    $reliability = Invoke-Optional { $disk | Get-StorageReliabilityCounter -ErrorAction Stop } $null `
        -ProviderName ("Storage reliability detail ({0})" -f [string]$disk.FriendlyName) `
        -Category "Disk inventory" `
        -RequiresElevation $true
    $diskStatus = "READY"
    $health = [string]$disk.HealthStatus
    $operational = [string]($disk.OperationalStatus -join ", ")
    $temperature = if ($null -ne $reliability) { $reliability.Temperature } else { $null }
    $wear = if ($null -ne $reliability) { $reliability.Wear } else { $null }
    $diskHealthPercent = if ($null -ne $wear) { [math]::Max(0, [math]::Min(100, 100 - [double]$wear)) } else { $null }
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
        diskHealthPercent = if ($null -ne $diskHealthPercent) {
            [ordered]@{
                value = [math]::Round($diskHealthPercent, 1)
                confidence = "Medium"
                source = "MSFT_StorageReliabilityCounter.Wear"
                isEstimated = $true
                technicianNote = "Estimated as 100 minus Windows-reported wear/percentage-used data. No percentage is invented from Healthy status alone."
            }
        } else {
            $null
        }
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
Write-PhaseTiming -PhaseName "Disk inventory" -Stopwatch $scanStopwatch -LastMs ([ref]$lastPhaseMs)

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
Write-PhaseTiming -PhaseName "Battery" -Stopwatch $scanStopwatch -LastMs ([ref]$lastPhaseMs)

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
} $null -ProviderName "Default route probe" -Category "Network"
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
$internetCheck = Invoke-Optional { Test-NetConnection -ComputerName "1.1.1.1" -Port 443 -InformationLevel Quiet -WarningAction SilentlyContinue -ErrorAction Stop } $false -ProviderName "Internet connectivity probe" -Category "Network"
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
Write-PhaseTiming -PhaseName "Network" -Stopwatch $scanStopwatch -LastMs ([ref]$lastPhaseMs)

Write-ScanLog "Checking Defender and registered antivirus state."
$defender = Invoke-Optional { Get-MpComputerStatus -ErrorAction Stop } $null -ProviderName "Defender status provider" -Category "Defender/AV" -RequiresElevation $true
$avProducts = @(Invoke-Optional { Get-CimInstance -Namespace "root\SecurityCenter2" -ClassName AntiVirusProduct -ErrorAction Stop } @() -ProviderName "Registered AV products provider" -Category "Defender/AV")
$firewallProfiles = @(Invoke-Optional { Get-NetFirewallProfile -ErrorAction Stop } @() -ProviderName "Firewall profile provider" -Category "Defender/AV")
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
Write-PhaseTiming -PhaseName "Defender/AV" -Stopwatch $scanStopwatch -LastMs ([ref]$lastPhaseMs)

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
    -InternetCheck ([bool]$internetCheck) `
    -UsbIntelligenceReport $usbIntelligenceReport
Write-PhaseTiming -PhaseName "Deep sensors/provider status" -Stopwatch $scanStopwatch -LastMs ([ref]$lastPhaseMs)

$defaultRouteIfIndex = $null
if ($null -ne $defaultRouteRaw) {
    $defaultRouteIfIndex = $defaultRouteRaw.ifIndex
}

$defaultRouteNextHop = ""
if ($null -ne $defaultRouteRaw) {
    $defaultRouteNextHop = [string]$defaultRouteRaw.NextHop
}

$defaultRouteAdapterName = ""
if ($null -ne $defaultRouteAdapter) {
    $defaultRouteAdapterName = [string]$defaultRouteAdapter.name
}

$gpuSummaryRows = New-Object System.Collections.Generic.List[object]
foreach ($gpu in @($gpus)) {
    if ($null -eq $gpu) {
        continue
    }

    $gpuSummaryRows.Add([ordered]@{
        name = [string]$gpu.Name
        type = Get-GpuType -Name ([string]$gpu.Name)
        driverVersion = [string]$gpu.DriverVersion
    }) | Out-Null
}

$scanModeValue = "Standard"
if (Test-IsAdministrator) {
    $scanModeValue = "Elevated"
}

$cpuCoreCount = $null
$cpuLogicalProcessors = $null
$cpuBaseClock = $null
$cpuMaxClock = $null
$cpuDisplayName = "Unknown CPU"
if ($null -ne $processor) {
    $cpuCoreCount = $processor.NumberOfCores
    $cpuLogicalProcessors = $processor.NumberOfLogicalProcessors
    $cpuBaseClock = $processor.CurrentClockSpeed
    $cpuMaxClock = $processor.MaxClockSpeed
    $cpuDisplayName = Get-ProcessorName -Processor $processor
}

$ramTotalDisplayValue = Format-Bytes -Bytes $totalMemoryBytes
$ramFreeDisplayValue = Format-Bytes -Bytes $freeMemoryBytes
$ramUsedDisplayValue = Format-Bytes -Bytes $usedMemoryBytes
$secureBootValue = $null
if ($null -ne $secureBootInfo.value) {
    $secureBootValue = [bool]$secureBootInfo.value
}

$tpmPresentValue = $null
if ($null -ne $tpmInfo.present) {
    $tpmPresentValue = [bool]$tpmInfo.present
}

$tpmReadyValue = $null
if ($null -ne $tpmInfo.ready) {
    $tpmReadyValue = [bool]$tpmInfo.ready
}

$lastBootDisplay = "UNKNOWN"
if ($null -ne $lastBoot) {
    $lastBootDisplay = $lastBoot.ToString("yyyy-MM-dd HH:mm:ss")
}

$uptimeDisplay = "UNKNOWN"
if ($null -ne $uptime) {
    $uptimeDisplay = Format-TimeSpanValue -Value $uptime
}

$summaryManufacturer = "Unknown"
$summaryModel = "Unknown"
$summaryOs = "Unknown OS"
$summaryOsBuild = "UNKNOWN"
$summaryOsArchitecture = "UNKNOWN"
$summaryBios = "UNKNOWN"
$summaryBiosDate = "UNKNOWN"
if ($null -ne $computerSystem) {
    $summaryManufacturer = [string]$computerSystem.Manufacturer
    $summaryModel = [string]$computerSystem.Model
}

if ($null -ne $operatingSystem) {
    $summaryOs = ("{0} {1}" -f $operatingSystem.Caption, $operatingSystem.Version).Trim()
    $summaryOsBuild = [string]$operatingSystem.BuildNumber
    $summaryOsArchitecture = [string]$operatingSystem.OSArchitecture
}

if ($null -ne $bios) {
    $summaryBios = ("{0} {1}" -f $bios.Manufacturer, $bios.SMBIOSBIOSVersion).Trim()
    $summaryBiosDate = Format-DateValue -Value $bios.ReleaseDate
}

try {
    $summary = [ordered]@{}
    $summary["computerName"] = $env:COMPUTERNAME
    $summary["manufacturer"] = $summaryManufacturer
    $summary["model"] = $summaryModel
    $summary["serviceTag"] = $serviceTagRedacted
    $summary["serialNumber"] = $serviceTagRedacted
    $summary["os"] = $summaryOs
    $summary["osBuild"] = $summaryOsBuild
    $summary["osArchitecture"] = $summaryOsArchitecture
    $summary["windowsLicenseChannel"] = $windowsLicenseChannel
    $summary["windowsLicenseStatus"] = $windowsLicenseStatus
    $summary["windowsLicense"] = $licenseInfo
    $summary["bios"] = $summaryBios
    $summary["biosDate"] = $summaryBiosDate
    $summary["secureBoot"] = $secureBootValue
    $summary["secureBootInfo"] = $secureBootInfo
    $summary["tpmPresent"] = $tpmPresentValue
    $summary["tpmReady"] = $tpmReadyValue
    $summary["tpmInfo"] = $tpmInfo
    $summary["lastBoot"] = $lastBootDisplay
    $summary["uptime"] = $uptimeDisplay
    $summary["cpu"] = $cpuDisplayName
    $summary["cpuCores"] = $cpuCoreCount
    $summary["cpuLogicalProcessors"] = $cpuLogicalProcessors
    $summary["cpuBaseClockMhz"] = $cpuBaseClock
    $summary["cpuMaxClockMhz"] = $cpuMaxClock
    $summary["ramTotal"] = $ramTotalDisplayValue
    $summary["ramFree"] = $ramFreeDisplayValue
    $summary["ramUsed"] = $ramUsedDisplayValue
    $summary["ramUsedPercent"] = $usedMemoryPercent
    $summary["ramSpeed"] = $memoryConfiguredDisplay
    $summary["ramInstalledDisplay"] = $memoryInstalledDisplay
    $summary["ramConfiguredSpeedDisplay"] = $memoryConfiguredDisplay
    $summary["ramModuleRatedSpeedDisplay"] = $memoryRatedDisplay
    $summary["ramSlotsDisplay"] = $memorySlotsDisplay
    $summary["ramModules"] = $memoryModuleReports
    $summary["ramSlotsTotal"] = $memorySlotsTotal
    $summary["ramSlotsUsed"] = $memorySlotsUsed
    $summary["ramSlotsFree"] = $memorySlotsFree
    $summary["ramUpgradePath"] = $memoryUpgradePath
    $summary["ramStatus"] = $ramStatus
    $summary["memoryType"] = $memoryType
    $summary["gpus"] = [object[]]$gpuSummaryRows.ToArray()
    $summary["gpuStatus"] = $gpuStatus

    $network = [ordered]@{}
    $network["status"] = $networkStatus
    $network["internetCheck"] = [bool]$internetCheck
    $network["internetDisplay"] = $internetDisplay
    $network["defaultRoute"] = [ordered]@{
        friendlyDisplayText = $defaultRouteDisplay
        ifIndex = $defaultRouteIfIndex
        nextHop = $defaultRouteNextHop
        adapterName = $defaultRouteAdapterName
    }
    $network["wifi"] = $wifiState
    $network["physicalAdapters"] = $physicalNetworkReport
    $network["virtualAdapters"] = $virtualNetworkReport
    $network["physicalAdapterCount"] = $physicalNetworkReport.Count
    $network["virtualAdapterCount"] = $virtualNetworkReport.Count
    $network["virtualAdaptersIgnored"] = $virtualIgnoredDisplay
    $network["adapters"] = $networkReport

    $report = [ordered]@{}
    $report["schemaVersion"] = 1
    $report["product"] = "ForgerEMS"
    $report["releaseIdentifier"] = "ForgerEMS v1.2.3 Public Preview"
    $report["generatedUtc"] = (Get-Date).ToUniversalTime().ToString("o")
    $report["scanMode"] = $scanModeValue
    $report["overallStatus"] = $overallStatus
    $report["summary"] = $summary
    $report["disks"] = $diskReports
    $report["smart"] = $smartReport
    $report["volumes"] = $volumeReports
    $report["diskStatus"] = $diskOverallStatus
    $report["batteryPresent"] = ($batteryReports.Count -gt 0)
    $report["batteries"] = $batteryReports
    $report["batteryStatus"] = $batteryOverallStatus
    $report["portPowerTelemetry"] = $portPowerDeepTelemetry
    $report["displays"] = $displayReport
    $report["network"] = $network
    $report["security"] = $securityReport
    $report["obviousProblems"] = @($obviousProblems)
    $report["flipValue"] = $flipValue
    $report["machineClass"] = $machineClass
    $report["sensorMatrix"] = $sensorMatrix
    $report["deepSensorMode"] = $sensorMatrix.deepSensorMode
    $report["sensorProviders"] = $sensorMatrix.sensorProviders
    $report["optionalProviderStatus"] = if ($null -eq $script:OptionalProviderDiagnostics) { @() } else { [object[]]$script:OptionalProviderDiagnostics.ToArray() }
    $report["deviceFit"] = $deviceFit
    $report["recommendations"] = @($recommendations)
    $report["scanNotes"] = @(
        "Standard scan: safe non-admin scan."
        "Elevated scan: unlocks more hardware/security detail."
        "Some hardware details require administrator permission. No failure: standard scan completed with permission-limited deep details when needed."
    )
    $report["reportPaths"] = [ordered]@{
        json = $jsonPath
        markdown = $markdownPath
    }
}
catch {
    $position = if ($_.InvocationInfo -and $_.InvocationInfo.PositionMessage) { $_.InvocationInfo.PositionMessage.Trim() } else { "position unavailable" }
    Write-ScanLog ("Report assembly failed: {0} ({1})" -f $_.Exception.Message, $position) "ERROR"
    throw
}

try {
    $report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
    if ($WriteElevatedScanMarkers) {
        $elevatedResultPath = Join-Path $OutputDirectory "elevated-scan-result.json"
        $resultPayload = [ordered]@{
            kind = "elevated-scan-result"
            utc  = (Get-Date).ToUniversalTime().ToString("o")
            ok   = $true
            json = "system-intelligence-latest.json"
        }
        $resultPayload | ConvertTo-Json | Set-Content -LiteralPath $elevatedResultPath -Encoding UTF8
    }
}
catch {
    $position = if ($_.InvocationInfo -and $_.InvocationInfo.PositionMessage) { $_.InvocationInfo.PositionMessage.Trim() } else { "position unavailable" }
    Write-ScanLog ("Report JSON serialization failed: {0} ({1})" -f $_.Exception.Message, $position) "ERROR"
    throw
}
Write-PhaseTiming -PhaseName "Report write" -Stopwatch $scanStopwatch -LastMs ([ref]$lastPhaseMs)

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
[void]$markdown.Add(("- Deep Sensor Mode: {0} via {1}; enabled {2}; read-only {3}" -f $report.deepSensorMode.value, $report.deepSensorMode.source, $report.deepSensorMode.enabled, $report.deepSensorMode.readOnly))
[void]$markdown.Add(("- Safety: {0}" -f $report.deepSensorMode.noticeText))
[void]$markdown.Add(("- Deep sensor note: {0}" -f $report.sensorMatrix.deepSensorModeNote))
[void]$markdown.Add("")
[void]$markdown.Add("### Sensor Provider Host")
foreach ($provider in $report.sensorProviders) {
    $status = if ($provider.isEnabled) { "Active" } elseif ($provider.isBundled) { "Bundled but disabled" } elseif ($provider.providerName -match "Driver") { "Not included" } else { "Off" }
    [void]$markdown.Add(("- {0}: {1}; trust {2}; mode {3}; read-only {4}; admin required {5}; bundled {6}" -f $provider.providerName, $status, $provider.trustLevel, $provider.runtimeMode, $provider.isReadOnly, $provider.requiresAdmin, $provider.isBundled))
    if ($provider.failureReason) {
        [void]$markdown.Add(("  - Note: {0}" -f $provider.failureReason))
    }
    if ($provider.requiresThirdPartyLicenseNotice -and $null -ne $provider.thirdPartyNotice) {
        [void]$markdown.Add(("  - License notice: {0} {1} ({2}); {3}" -f $provider.thirdPartyNotice.name, $provider.thirdPartyNotice.version, $provider.thirdPartyNotice.license, $provider.thirdPartyNotice.sourceOfferOrNotice))
    }
}
[void]$markdown.Add("")
[void]$markdown.Add("### Optional Provider Status")
if ($report.optionalProviderStatus.Count -gt 0) {
    foreach ($provider in $report.optionalProviderStatus) {
        [void]$markdown.Add(("- {0} ({1}): {2} | elevation required: {3}" -f $provider.providerName, $provider.category, $provider.status, $provider.requiresElevation))
        [void]$markdown.Add(("  - {0}" -f $provider.userMessage))
    }
}
else {
    [void]$markdown.Add("- No optional provider limitations were recorded.")
}
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
