#Requires -Version 5.1
<#
.SYNOPSIS
Captures a pure READ-ONLY guest system inventory for installer lifecycle audits.

.DESCRIPTION
Runs ONLY inside the explicitly identified disposable guest (same guard as the
lifecycle harness). Enumerates services, kernel drivers, scheduled tasks,
startup surfaces, the full HKLM/HKCU Software\Classes registration surface,
firewall application filters, and the Driver Store (pnputil /enum-drivers,
read-only). Emits a JSON inventory to a fresh output file (exclusive create —
never overwrites).

Diff semantics (-BaselinePath):
  * Baseline must be a schemaVersion=1 guest-audit-inventory for the SAME vmId;
    otherwise verdict=BLOCKED.
  * Each inventory section carries a 'sectionErrors' entry when its query could
    not be executed; any section error (baseline or current) => verdict=BLOCKED
    rather than a silent empty-PASS.
  * Comparison keys are stable configuration fields only: services/drivers by
    name+pathName+startMode; tasks by taskPath+taskName+actions; startup by
    hive+name+command; firewall rules by stable Name+direction+action+program;
    Classes fingerprints by hive-relative key path+value name+data. Runtime
    State/Status fields are recorded as observations, never counted as config
    changes.
  * verdict: EXPECTED-ONLY (no unexplained deltas) | REVIEW (drift that is not
    provably QA-owned but outside product/security surfaces — still blocks
    promotion) | UNEXPECTED (product-named or security-surface delta) | BLOCKED.
    QA-owned evidence paths under C:\ForgerEMS-QA\ are classified explicitly as
    expected campaign artifacts, not silently ignored.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F-]{36}$')][string]$DisposableVmId,
    [ValidateSet('VirtualBox','QEMU','GitHubHosted')][string]$IsolationKind = 'VirtualBox',
    [Parameter(Mandatory)][string]$OutputPath,
    [ValidateSet('baseline','after-install','after-uninstall','after-reboot')][string]$Label = 'baseline',
    [string]$BaselinePath = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Disposable-guest guard — identical gate to the lifecycle harness; refuse on
# host BEFORE touching the filesystem.
$computer = Get-CimInstance Win32_ComputerSystem
$product = Get-CimInstance Win32_ComputerSystemProduct
$marker = 'C:\ForgerEMS-QA\DISPOSABLE_VM_ID.txt'
$isVirtualBox = $computer.Model -eq 'VirtualBox' -and $IsolationKind -eq 'VirtualBox'
$isQemu = $IsolationKind -eq 'QEMU' -and $computer.Manufacturer -eq 'QEMU' -and $computer.Model -eq 'ForgerEMS-QA'
$isHostedRunner = $IsolationKind -eq 'GitHubHosted' -and $env:GITHUB_ACTIONS -eq 'true' -and
    $env:RUNNER_ENVIRONMENT -eq 'github-hosted' -and $env:RUNNER_OS -eq 'Windows' -and
    $computer.Model -eq 'Virtual Machine' -and $computer.Manufacturer -eq 'Microsoft Corporation'
if ((-not $isVirtualBox -and -not $isQemu -and -not $isHostedRunner) -or $product.UUID -ne $DisposableVmId -or
    -not (Test-Path -LiteralPath $marker -PathType Leaf) -or
    (Get-Content -LiteralPath $marker -Raw).Trim() -ne $DisposableVmId) {
    throw 'Refusing guest audit outside the identified disposable Windows guest.'
}
if (Test-Path -LiteralPath $OutputPath) {
    throw "Audit output already exists: $OutputPath. Refusing to overwrite; use a fresh path."
}

$sectionErrors = [System.Collections.Generic.List[string]]::new()

function Get-GaProp {
    # StrictMode-safe property read on PSCustomObject or IDictionary entries
    # ([hashtable] and [ordered] OrderedDictionary both implement IDictionary;
    # PSObject.Properties does not surface OrderedDictionary keys). Returns the
    # RAW value — callers that need a string cast at the call site so structured
    # values such as task action arrays survive intact.
    param([object]$Item, [string]$Name)
    if ($null -eq $Item) { return $null }
    if ($Item -is [System.Collections.IDictionary]) {
        return $(if ($Item.Contains($Name)) { $Item[$Name] } else { $null })
    }
    $prop = $Item.PSObject.Properties[$Name]
    if ($null -eq $prop) { return $null }
    return $prop.Value
}

# Stable identity + comparable config per section. State/Status are explicitly
# excluded from the comparison payload so a service merely starting or stopping
# never surfaces as a configuration change.
function Get-GaStableKey {
    param([string]$Section, [object]$Item)
    switch ($Section) {
        'services'             { return ('svc:' + (Get-GaProp $Item 'name')) }
        'drivers'              { return ('drv:' + (Get-GaProp $Item 'name')) }
        'scheduledTasks'       { return ('task:' + (Get-GaProp $Item 'taskPath') + '|' + (Get-GaProp $Item 'taskName')) }
        'startupEntries'       { return ('startup:' + (Get-GaProp $Item 'hive') + '|' + (Get-GaProp $Item 'name')) }
        'classesFingerprints'  { return ('classes:' + (Get-GaProp $Item 'hive') + '|' + (Get-GaProp $Item 'keyPath') + '|' + (Get-GaProp $Item 'valueName')) }
        'firewallAppRules'     { return ('fw:' + (Get-GaProp $Item 'ruleName') + '|' + (Get-GaProp $Item 'program')) }
        default                { return ($Section + ':' + ($Item | ConvertTo-Json -Compress -Depth 4)) }
    }
}
function Get-GaConfigPayload {
    param([string]$Section, [object]$Item)
    switch ($Section) {
        'services'            { return [ordered]@{ name=(Get-GaProp $Item 'name'); pathName=(Get-GaProp $Item 'pathName'); startMode=(Get-GaProp $Item 'startMode') } }
        'drivers'             { return [ordered]@{ name=(Get-GaProp $Item 'name'); pathName=(Get-GaProp $Item 'pathName'); startMode=(Get-GaProp $Item 'startMode') } }
        'scheduledTasks'      { return [ordered]@{ taskPath=(Get-GaProp $Item 'taskPath'); taskName=(Get-GaProp $Item 'taskName'); actions=@(Get-GaProp $Item 'actions') } }
        'startupEntries'      { return [ordered]@{ hive=(Get-GaProp $Item 'hive'); name=(Get-GaProp $Item 'name'); command=(Get-GaProp $Item 'command') } }
        'classesFingerprints' { return [ordered]@{ hive=(Get-GaProp $Item 'hive'); keyPath=(Get-GaProp $Item 'keyPath'); valueName=(Get-GaProp $Item 'valueName'); data=(Get-GaProp $Item 'data') } }
        'firewallAppRules'    { return [ordered]@{ ruleName=(Get-GaProp $Item 'ruleName'); direction=(Get-GaProp $Item 'direction'); action=(Get-GaProp $Item 'action'); program=(Get-GaProp $Item 'program'); enabled=(Get-GaProp $Item 'enabled') } }
        default               { return $Item }
    }
}

$inventory = [ordered]@{
    schemaVersion = 1
    kind = 'guest-audit-inventory'
    label = $Label
    vmId = $product.UUID
    isolationKind = $IsolationKind
    capturedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    sectionErrors = @()
    stateObservations = @()
    services = @()
    drivers = @()
    scheduledTasks = @()
    startupEntries = @()
    classesFingerprints = @()
    firewallAppRules = @()
    driverStore = @()
}

try {
    $inventory.services = @(
        Get-CimInstance Win32_Service -ErrorAction Stop | Sort-Object Name | ForEach-Object {
            [ordered]@{ name = $_.Name; pathName = [string]$_.PathName; startMode = [string]$_.StartMode; state = [string]$_.State }
        })
} catch { $sectionErrors.Add("services query failed: $($_.Exception.Message)") }
try {
    $inventory.drivers = @(
        Get-CimInstance Win32_SystemDriver -ErrorAction Stop | Sort-Object Name | ForEach-Object {
            [ordered]@{ name = $_.Name; pathName = [string]$_.PathName; startMode = [string]$_.StartMode; state = [string]$_.State }
        })
} catch { $sectionErrors.Add("drivers query failed: $($_.Exception.Message)") }
try {
    $inventory.scheduledTasks = @(
        Get-ScheduledTask -ErrorAction Stop | Sort-Object TaskPath, TaskName | ForEach-Object {
            [ordered]@{
                taskPath = $_.TaskPath; taskName = $_.TaskName; state = [string]$_.State
                actions = @($_.Actions | ForEach-Object { "$($_.Execute) $($_.Arguments)".Trim() })
            }
        })
} catch { $sectionErrors.Add("scheduledTasks query failed: $($_.Exception.Message)") }

# Startup: Run/RunOnce across HKLM/HKCU and the 32-bit view, plus Startup folders.
$runKeys = @(
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run',
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce',
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run',
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce',
    'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run',
    'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce'
)
foreach ($key in $runKeys) {
    if (-not (Test-Path $key)) { continue }
    $props = Get-ItemProperty $key
    foreach ($name in $props.PSObject.Properties.Name) {
        if ($name.StartsWith('PS')) { continue }
        $inventory.startupEntries += [ordered]@{ hive = $key; name = $name; command = [string]$props.$name }
    }
}
foreach ($folder in @([Environment]::GetFolderPath('CommonStartup'), [Environment]::GetFolderPath('Startup'))) {
    foreach ($file in @(Get-ChildItem -LiteralPath $folder -Force -ErrorAction SilentlyContinue)) {
        $inventory.startupEntries += [ordered]@{ hive = 'StartupFolder'; name = $file.Name; command = $file.FullName }
    }
}

# Full HKLM/HKCU Software\Classes registration fingerprints: url protocols,
# shell extensions (shellex), CLSID registrations, AllFileSystemObjects — the
# whole writeable Classes surface, captured via read-only `reg export` into a
# FRESH evidence directory keyed to the unique output file. Export SHA256 is
# bound into the inventory so a raw-export delta can never look identical to
# the fingerprint parse.
$classesDir = Join-Path (Split-Path -Parent $OutputPath) ("classes-" + [IO.Path]::GetFileNameWithoutExtension($OutputPath))
if (Test-Path -LiteralPath $classesDir) {
    throw "Classes export directory already exists: $classesDir. Use a fresh output path."
}
New-Item -ItemType Directory -Path $classesDir | Out-Null
$classesFp = [System.Collections.Generic.List[object]]::new()
$classesExportEvidence = [ordered]@{}
foreach ($hiveSpec in @(
    @{ Hive = 'HKLM'; Root = 'HKLM\SOFTWARE\Classes' },
    @{ Hive = 'HKCU'; Root = 'HKCU\SOFTWARE\Classes' })) {
    $export = Join-Path $classesDir ("$($hiveSpec.Hive)-classes.reg")
    if (Test-Path -LiteralPath $export) {
        $sectionErrors.Add("classes export target already exists: $export")
        continue
    }
    $regEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & reg.exe export $hiveSpec.Root $export 2>$null | Out-Null   # no /y: never overwrite a preexisting export
    $regExit = $LASTEXITCODE
    $ErrorActionPreference = $regEap
    if ($regExit -ne 0 -or -not (Test-Path -LiteralPath $export -PathType Leaf)) {
        $sectionErrors.Add("classes export failed for $($hiveSpec.Root) (reg exit $regExit)")
        continue
    }
    $classesExportEvidence[$hiveSpec.Hive] = [ordered]@{
        path = $export
        sha256 = (Get-FileHash -LiteralPath $export -Algorithm SHA256).Hash
    }
    # Parse key|value|data fingerprints. Handles multi-line hex continuations
    # (a data line ending in '\' continues on the next line) and records empty
    # keys explicitly so a key surviving with no values still fingerprints.
    $currentKey = ''
    $keyHadValues = $true
    $pendingName = $null
    $pendingData = $null
    foreach ($line in [IO.File]::ReadLines($export, [Text.Encoding]::Unicode)) {
        $trim = $line.Trim()
        if ($null -ne $pendingName) {
            if ([string]::IsNullOrEmpty($trim)) { continue }
            $pendingData += $trim
            if ($trim.EndsWith('\')) { continue }
            $classesFp.Add([ordered]@{
                hive = $hiveSpec.Hive; keyPath = $currentKey; valueName = $pendingName; data = $pendingData })
            $pendingName = $null; $pendingData = $null
            continue
        }
        if ([string]::IsNullOrEmpty($trim)) { continue }
        if ($trim.StartsWith('[') -and $trim.EndsWith(']')) {
            if (-not $keyHadValues -and -not [string]::IsNullOrWhiteSpace($currentKey)) {
                $classesFp.Add([ordered]@{
                    hive = $hiveSpec.Hive; keyPath = $currentKey; valueName = '(key)'; data = 'key-present' })
            }
            $currentKey = $trim.Substring(1, $trim.Length - 2)
            $keyHadValues = $false
            continue
        }
        if (($trim.StartsWith('"') -or $trim.StartsWith('@')) -and -not [string]::IsNullOrWhiteSpace($currentKey)) {
            $eq = $trim.IndexOf('=')
            if ($eq -lt 0) { continue }
            $keyHadValues = $true
            $valueName = $trim.Substring(0, $eq)
            $data = $trim.Substring($eq + 1)
            if ($data.EndsWith('\')) { $pendingName = $valueName; $pendingData = $data; continue }
            $classesFp.Add([ordered]@{
                hive = $hiveSpec.Hive; keyPath = $currentKey; valueName = $valueName; data = $data })
        }
    }
    if (-not $keyHadValues -and -not [string]::IsNullOrWhiteSpace($currentKey)) {
        $classesFp.Add([ordered]@{
            hive = $hiveSpec.Hive; keyPath = $currentKey; valueName = '(key)'; data = 'key-present' })
    }
    if ($null -ne $pendingName) {
        $sectionErrors.Add("classes export truncated mid-continuation in $($hiveSpec.Root) key $currentKey")
    }
}
$inventory.classesFingerprints = @($classesFp)
$inventory.classesExportEvidence = $classesExportEvidence
if ($inventory.classesFingerprints.Count -eq 0 -and
    -not @($sectionErrors | Where-Object { $_ -like 'classes*' }).Count) {
    $sectionErrors.Add('classes fingerprint produced zero entries; surface not measured')
}

# Firewall application filters — stable rule Name (not DisplayName) + program.
try {
    $appFilters = @(Get-NetFirewallApplicationFilter -ErrorAction Stop)
    foreach ($filter in $appFilters) {
        $rule = $filter | Get-NetFirewallRule -ErrorAction SilentlyContinue
        if ($null -eq $rule) { continue }
        $inventory.firewallAppRules += [ordered]@{
            ruleName = [string]$rule.Name; direction = [string]$rule.Direction
            action = [string]$rule.Action; program = [string]$filter.Program; enabled = [string]$rule.Enabled
        }
    }
} catch {
    $sectionErrors.Add("firewallAppRules query failed: $($_.Exception.Message)")
}

# Driver Store inventory (read-only enumeration); a nonzero pnputil exit means
# the store was NOT measured — section failure, never an empty pass.
$pnpEap = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$pnp = (& pnputil.exe /enum-drivers 2>$null) -join "`n"
$pnpExit = $LASTEXITCODE
$ErrorActionPreference = $pnpEap
if ($pnpExit -ne 0) {
    $sectionErrors.Add("driverStore query failed: pnputil exit $pnpExit")
} else {
    $inventory.driverStore = @([ordered]@{ tool = 'pnputil /enum-drivers'; raw = $pnp })
}

$inventory.sectionErrors = @($sectionErrors)

# --- Optional baseline diff ------------------------------------------------------
$verdict = if ($sectionErrors.Count -gt 0) { 'BLOCKED' } else { 'BASELINE-ONLY' }
$diff = $null
if (-not [string]::IsNullOrWhiteSpace($BaselinePath)) {
    $baselineError = $null
    $baseline = $null
    try { $baseline = Get-Content -LiteralPath $BaselinePath -Raw | ConvertFrom-Json -ErrorAction Stop }
    catch { $baselineError = "baseline unreadable/malformed: $($_.Exception.Message)" }
    if ($null -eq $baselineError) {
        if ((Get-GaProp $baseline 'schemaVersion') -ne '1' -or (Get-GaProp $baseline 'kind') -ne 'guest-audit-inventory') {
            $baselineError = 'baseline is not a schemaVersion=1 guest-audit-inventory'
        } elseif ((Get-GaProp $baseline 'vmId') -ne $product.UUID) {
            $baselineError = "baseline vmId $((Get-GaProp $baseline 'vmId')) does not match this guest $($product.UUID)"
        }
    }
    if ($null -ne $baselineError) {
        $diff = [ordered]@{ blockedReason = $baselineError }
        $verdict = 'BLOCKED'
    } else {
        $diff = [ordered]@{
            additions = @{}; removals = @{}; configChanges = @{}
            stateObservations = @(); unexpectedProductChanges = @()
            qaOwnedChanges = @(); reviewDrift = @()
        }
        $sections = @('services','drivers','scheduledTasks','startupEntries','classesFingerprints','firewallAppRules','driverStore')
        foreach ($section in $sections) {
            $baseItems = $baseline.PSObject.Properties[$section]
            if ($null -eq $baseItems) {
                $diff.reviewDrift += "$section`: missing from baseline; section unmeasured"
                continue
            }
            $before = @{}; $after = @{}; $beforeState = @{}; $afterState = @{}
            foreach ($item in @($baseItems.Value)) {
                $id = Get-GaStableKey -Section $section -Item $item
                $before[$id] = ((Get-GaConfigPayload -Section $section -Item $item) | ConvertTo-Json -Compress -Depth 4)
                $state = Get-GaProp $item 'state'
                if ($null -ne $state) { $beforeState[$id] = $state }
            }
            foreach ($item in @($inventory[$section])) {
                $id = Get-GaStableKey -Section $section -Item $item
                $after[$id] = ((Get-GaConfigPayload -Section $section -Item $item) | ConvertTo-Json -Compress -Depth 4)
                $state = Get-GaProp $item 'state'
                if ($null -ne $state) { $afterState[$id] = $state }
            }
            $adds = @($after.Keys | Where-Object { -not $before.ContainsKey($_) })
            $removes = @($before.Keys | Where-Object { -not $after.ContainsKey($_) })
            $changed = @($after.Keys | Where-Object { $before.ContainsKey($_) -and $before[$_] -ne $after[$_] })
            if ($adds.Count) { $diff.additions[$section] = $adds }
            if ($removes.Count) { $diff.removals[$section] = $removes }
            if ($changed.Count) { $diff.configChanges[$section] = $changed }
            # Runtime state flips are observations only — never configuration drift.
            foreach ($id in @($afterState.Keys | Where-Object { $beforeState.ContainsKey($_) -and $beforeState[$_] -ne $afterState[$_] })) {
                $diff.stateObservations += "$section`: $id state $($beforeState[$id]) -> $($afterState[$id])"
            }
            foreach ($id in @($adds + $removes + $changed)) {
                # QA-campaign ownership is recognized only when the stable id or
                # the item's payload carries the QA root path or a known helper
                # id. Even then it is NOT a free pass: QA-owned deltas are also
                # recorded as reviewDrift so they can never produce an automatic
                # EXPECTED-ONLY verdict — clearing them requires explicit signed
                # operator attestation, not a name match.
                $cfg = if ($after.ContainsKey($id)) { $after[$id] } else { $before[$id] }
                $haystack = "$id $cfg"
                $qaOwned = ($haystack -match '(?i)ForgerEMS-QA[/\\]' -or
                    $id -match '(?i)Run-GuestCampaign|Resume-ForgerQA|FORGERREL|guest-audit')
                if ($qaOwned) {
                    $diff.qaOwnedChanges += "$section`: $id"
                    $diff.reviewDrift += "$section`: $id (qa-owned delta; requires explicit operator review)"
                } elseif ($haystack -match '(?i)forgerems|kyra' -or
                    $section -in @('drivers','services','firewallAppRules','classesFingerprints','driverStore')) {
                    # Product-named or security-surface deltas block promotion.
                    $diff.unexpectedProductChanges += "$section`: $id"
                } else {
                    # Grounded, non-security drift (e.g. an OS task updated by a
                    # servicing pass) — surfaced for review, never silent-passed.
                    $diff.reviewDrift += "$section`: $id"
                }
            }
        }
        # A missing/null sectionErrors member on the baseline means the baseline
        # file predates error tracking — unmeasurable, so BLOCKED, never a null
        # .Value dereference under StrictMode.
        $baselineErrs = $baseline.PSObject.Properties['sectionErrors']
        $baselineErrCount = if ($null -eq $baselineErrs -or $null -eq $baselineErrs.Value) {
            1
        } else {
            @($baselineErrs.Value).Count
        }
        if ($sectionErrors.Count -gt 0 -or $baselineErrCount -gt 0) {
            $verdict = 'BLOCKED'
            $diff.blockedReason = 'one or more inventory sections failed to measure'
        } elseif ($diff.unexpectedProductChanges.Count -gt 0) {
            $verdict = 'UNEXPECTED'
        } elseif ($diff.reviewDrift.Count -gt 0) {
            $verdict = 'REVIEW'
        } else {
            $verdict = 'EXPECTED-ONLY'
        }
        # Raw export-hash fallback: if the raw Classes .reg export changed but
        # the per-value fingerprint shows no delta, the parser missed something
        # (continuation, encoding, value form). That is unmeasured change —
        # block rather than pass. Runs after the verdict chain so a BLOCKED here
        # can never be overwritten by an EXPECTED-ONLY.
        $baseExp = Get-GaProp $baseline 'classesExportEvidence'
        foreach ($hive in @('HKLM','HKCU')) {
            $bSha = Get-GaProp (Get-GaProp $baseExp $hive) 'sha256'
            $cSha = Get-GaProp (Get-GaProp $classesExportEvidence $hive) 'sha256'
            $noFpDelta = (-not $diff.additions['classesFingerprints']) -and
                (-not $diff.removals['classesFingerprints']) -and
                (-not $diff.configChanges['classesFingerprints'])
            if ($null -ne $cSha -and ($null -eq $bSha -or $bSha -ne $cSha) -and $noFpDelta -and
                $verdict -ne 'UNEXPECTED' -and $verdict -ne 'BLOCKED') {
                $verdict = 'BLOCKED'
                $diff.blockedReason = "classes $hive raw export hash changed without a fingerprint delta - surface not fully measured"
            }
        }
    }
    $inventory.diff = $diff
}
$inventory.verdict = $verdict

$json = $inventory | ConvertTo-Json -Depth 8
$parent = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($parent) -and -not (Test-Path -LiteralPath $parent -PathType Container)) {
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
}
$fs = [IO.FileStream]::new($OutputPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
try {
    $bytes = [Text.Encoding]::UTF8.GetBytes($json)
    $fs.Write($bytes, 0, $bytes.Length)
} finally { $fs.Dispose() }
Write-Host "Guest audit inventory ($Label) written: $OutputPath verdict=$verdict"
exit 0
