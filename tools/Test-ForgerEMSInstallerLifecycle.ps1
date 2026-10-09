#Requires -Version 5.1
<#
.SYNOPSIS
Runs one installer lifecycle phase ONLY inside an explicitly identified disposable Windows guest.
.DESCRIPTION
No host bypass exists. Run phases separately so interactive GUI/consent QA can take place
between them. This harness does not certify GUI accessibility or production signing.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('CleanInstall','Reinstall','Uninstall','PreviousInstall','SeedUpgradeState','Upgrade','PostUninstall','ResidueAudit','DriverServiceTaskAudit')]
    [string]$Phase,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F-]{36}$')][string]$DisposableVmId,
    [Parameter(Mandatory)][string]$CandidateInstaller,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$CandidateSha256,
    [Parameter(Mandatory)][string]$PreviousInstaller,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$PreviousSha256,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$SourceHead,
    [ValidateSet('VirtualBox','QEMU','GitHubHosted')][string]$IsolationKind = 'VirtualBox',
    [string]$EvidenceRoot = 'C:\ForgerEMS-QA\Evidence',
    # Optional baseline guest-audit inventory (produced by
    # tools/Get-ForgerEMSGuestAudit.ps1 in the same campaign). Audit phases that
    # lack one report PARTIAL — a name-filter alone is not promotion evidence.
    [string]$BaselineAuditPath = '',
    [string]$GuestAuditScriptPath = '',
    # Candidate binding fields for promotion-compatible raw receipts. BuildId is
    # required for new promotion-compatible receipts; omitting it stays legal for
    # legacy calls, which are then marked engineering-only.
    [string]$CandidateVersion = '1.2.4',
    [string]$PreviousVersion = '1.2.3-preview.1',
    [ValidateSet('x64')][string]$CandidateArchitecture = 'x64',
    [ValidatePattern('^$|^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$')][string]$BuildId = '',
    [ValidatePattern('^$|^[0-9A-Fa-f]{64}$')][string]$CandidateManifestSha256 = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

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
    throw 'Refusing installer execution outside the identified disposable Windows guest.'
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'The disposable guest phase requires an administrator token. Do not elevate the development host.'
}
if (Test-Path -LiteralPath (Join-Path $EvidenceRoot "$Phase.json")) {
    throw 'Phase evidence already exists. Use a fresh evidence directory; never overwrite lifecycle history.'
}
New-Item -ItemType Directory -Path $EvidenceRoot -Force | Out-Null
$installDir = Join-Path $env:ProgramFiles 'ForgerEMS'
$app = Join-Path $installDir 'ForgerEMS.exe'
$runtime = Join-Path $env:LOCALAPPDATA 'ForgerEMS\Runtime'
$config = Join-Path $runtime 'config'
$canary = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'ForgerEMS-unrelated-QA.txt'
$registrationRoots = @(
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall',
    'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'
)
$os = Get-CimInstance Win32_OperatingSystem
$osArch = if ($env:PROCESSOR_ARCHITEW6432 -eq 'AMD64' -or $env:PROCESSOR_ARCHITECTURE -eq 'AMD64') { 'x64' } else { 'x86' }
$record = [ordered]@{
    schemaVersion = 1
    Phase = $Phase; StartedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    SourceHead = $SourceHead; VmId = $product.UUID; ComputerModel = $computer.Model
    IsolationKind = $IsolationKind
    OsVersion = $os.Version; OsBuild = $os.BuildNumber; OsArchitecture = $osArch
    CandidateVersion = $CandidateVersion; CandidateArchitecture = $CandidateArchitecture
    CandidateFileName = Split-Path -Leaf $CandidateInstaller; CandidateSha256 = $CandidateSha256
    BuildId = $BuildId
    CandidateBinding = [ordered]@{
        manifestSha256 = $CandidateManifestSha256
        # All promotion binding fields were supplied (or not) — information only.
        bindingComplete = (-not [string]::IsNullOrWhiteSpace($CandidateManifestSha256) -and
            -not [string]::IsNullOrWhiteSpace($BuildId))
        # Raw guest records are unsigned and are NEVER promotion-authoritative,
        # regardless of which binding fields were supplied.
        trustedForPromotion = $false
    }
    # Every record this harness emits is unsigned raw engineering evidence.
    EngineeringOnly = $true
    GuestIdentity = $identity.Name; InstallDirectory = $installDir
    Result = 'FAIL'; Observations = @(); Processes = @()
}
function Observe([string]$message) { $record.Observations += $message }
function Assert([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
    Observe $message
}
function Get-Registrations {
    foreach ($root in $registrationRoots) {
        if (-not (Test-Path $root)) { continue }
        foreach ($key in Get-ChildItem $root) {
            $entry = Get-ItemProperty $key.PSPath
            if ($entry.PSObject.Properties['DisplayName'] -and $entry.DisplayName -like 'ForgerEMS*') {
                $entry
            }
        }
    }
}
function Invoke-OwnedProcess([string]$file, [string]$arguments, [string]$name, [int]$timeoutSeconds = 180) {
    $out = Join-Path $EvidenceRoot "$Phase-$name.stdout.txt"
    $err = Join-Path $EvidenceRoot "$Phase-$name.stderr.txt"
    # UseShellExecute=$false keeps a real process handle so ExitCode is always
    # observable; Start-Process can yield a null ExitCode for elevated children.
    $psi = [System.Diagnostics.ProcessStartInfo]::new($file, $arguments)
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::Start($psi)
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($timeoutSeconds * 1000)) {
        # Only the process created by this phase is terminated on timeout.
        Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
        throw "$name timed out after $timeoutSeconds seconds"
    }
    $process.WaitForExit()
    [System.IO.File]::WriteAllText($out, $stdoutTask.GetAwaiter().GetResult())
    [System.IO.File]::WriteAllText($err, $stderrTask.GetAwaiter().GetResult())
    $exitCode = $process.ExitCode
    $record.Processes += @{ File = $file; Arguments = $arguments; ExitCode = $exitCode; Stdout = $out; Stderr = $err }
    Assert ($null -ne $exitCode -and $exitCode -eq 0) "$name exits 0 without a reboot request"
}
function Assert-Artifact([string]$file, [string]$expected) {
    Assert (Test-Path -LiteralPath $file -PathType Leaf) "Artifact exists: $file"
    Assert ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -eq $expected) 'Artifact matches independently supplied expected SHA-256'
    $signature = Get-AuthenticodeSignature -LiteralPath $file
    Observe ("Artifact signature: {0}; signer: {1}" -f $signature.Status,
        $(if ($signature.SignerCertificate) { $signature.SignerCertificate.Subject } else { '<none>' }))
}
function Install([string]$file, [string]$hash, [string]$name) {
    Assert-Artifact $file $hash
    $log = Join-Path $EvidenceRoot "$Phase-inno.log"
    Invoke-OwnedProcess $file "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /DIR=`"$installDir`" /LOG=`"$log`"" $name
}
function Assert-Installed([string]$version) {
    $entries = @(Get-Registrations)
    Assert ($entries.Count -eq 1) 'Exactly one product registration exists across HKLM/HKCU/32-bit roots'
    Assert ($entries[0].DisplayVersion -eq $version) "ARP DisplayVersion equals $version"
    Assert (Test-Path -LiteralPath $app -PathType Leaf) 'Installed main executable exists'
    Assert ((Get-Item $app).VersionInfo.ProductVersion -eq $version) "Executable product version equals $version"
    $shortcuts = @(
        (Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'ForgerEMS.lnk'),
        (Join-Path ([Environment]::GetFolderPath('Programs')) 'ForgerEMS.lnk')
    )
    Assert (@($shortcuts | Where-Object { Test-Path -LiteralPath $_ }).Count -eq 1) 'One intended Start Menu shortcut exists'
    $record.Registration = @($entries | Select-Object DisplayName,DisplayVersion,Publisher,InstallLocation,UninstallString)
    $record.InstalledFiles = @(Get-ChildItem $installDir -Recurse -File | Select-Object FullName,Length)
    if ($version -eq $CandidateVersion) {
        Assert (@(Get-ChildItem $installDir -Recurse -File | Where-Object Name -match '(?i)kyra').Count -eq 0) 'No Kyra-named installed runtime files exist'
        Assert (Test-Path (Join-Path $installDir 'backend\ForgerEMS.bundled-backend.json')) 'Bundled backend manifest exists'
    }
}
function SelfTest {
    $env:FORGEREMS_ENV = 'Production'
    $env:FORGEREMS_RELEASE_CHANNEL = 'stable'
    $env:FORGEREMS_DEEP_SENSOR_MODE = 'Off'
    Invoke-OwnedProcess $app '--self-test' 'self-test'
}
function Uninstall {
    $entries = @(Get-Registrations)
    Assert ($entries.Count -eq 1) 'One registered product is selected for uninstall'
    $uninstaller = Join-Path $installDir 'unins000.exe'
    Assert (Test-Path -LiteralPath $uninstaller -PathType Leaf) 'Installer-owned uninstaller exists'
    $log = Join-Path $EvidenceRoot "$Phase-uninstall.log"
    Invoke-OwnedProcess $uninstaller "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=`"$log`"" 'uninstall'
    # Inno unins000.exe hands off to a detached second-phase copy that deletes
    # the install dir; the spawned process exits before that cleanup finishes.
    $cleanupDeadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    while ([DateTimeOffset]::UtcNow -lt $cleanupDeadline -and
           (Test-Path -LiteralPath $uninstaller -PathType Leaf)) {
        Start-Sleep -Seconds 2
    }
    Assert (@(Get-Registrations).Count -eq 0) 'Product registration removed'
    Assert (-not (Test-Path $app)) 'Main executable removed'
    $ownedPaths = @('backend','manifests','docs','providers','LibreHardwareMonitorLib.dll','unins000.exe')
    foreach ($path in $ownedPaths) { Assert (-not (Test-Path (Join-Path $installDir $path))) "Installer-owned $path removed" }
    foreach ($folder in @('CommonPrograms','Programs','CommonDesktopDirectory','DesktopDirectory')) {
        Assert (-not (Test-Path (Join-Path ([Environment]::GetFolderPath($folder)) 'ForgerEMS.lnk'))) "$folder shortcut absent"
    }
    if (Test-Path $canary) { Assert ((Get-Content $canary -Raw).Trim() -eq 'Unrelated guest user file') 'Unrelated user document preserved' }
    $record.RetainedUserFiles = @(Get-ChildItem $runtime -Recurse -File -ErrorAction SilentlyContinue | Select-Object FullName,Length)
    $record.InstallResidue = @(Get-ChildItem $installDir -Recurse -Force -ErrorAction SilentlyContinue | Select-Object FullName,Length)
    $record.ProductServices = @(Get-Service | Where-Object { $_.Name -match '(?i)forgerems|kyra' } | Select-Object Name,Status)
    $record.ProductTasks = @(Get-ScheduledTask | Where-Object { $_.TaskName -match '(?i)forgerems|kyra' } | Select-Object TaskName,State)
    Assert ($record.ProductServices.Count -eq 0) 'No product/legacy services remain'
    Assert ($record.ProductTasks.Count -eq 0) 'No product/legacy scheduled tasks remain'
}
$script:PartialResult = $false
try {
    # The exact candidate bits are re-validated before EVERY phase — including
    # Uninstall and PostUninstall — so a swapped installer can never ride along
    # on a phase that does not execute it directly.
    Assert-Artifact $CandidateInstaller $CandidateSha256
    switch ($Phase) {
        'CleanInstall' {
            Assert (@(Get-Registrations).Count -eq 0) 'Fresh guest has no ForgerEMS product registration'
            Install $CandidateInstaller $CandidateSha256 'install'
            Assert-Installed $CandidateVersion
            SelfTest
        }
        'Reinstall' {
            $prior = @(Get-Registrations)
            Assert ($prior.Count -le 1) 'At most one prior product registration exists before reinstall'
            if ($prior.Count -eq 0) {
                Observe 'Reinstall runs against a fresh post-uninstall state'
            }
            else {
                Assert ($prior[0].DisplayVersion -eq $CandidateVersion) 'Existing registration is the same candidate version'
                Observe 'Same-version reinstall tested; no distinct repair mode is claimed'
            }
            Install $CandidateInstaller $CandidateSha256 'reinstall'
            Assert-Installed $CandidateVersion
            SelfTest
        }
        'Uninstall' { Uninstall }
        'PreviousInstall' {
            Assert (@(Get-Registrations).Count -eq 0) 'Previous release starts without duplicate registration'
            Install $PreviousInstaller $PreviousSha256 'previous-install'
            Assert-Installed $PreviousVersion
        }
        'SeedUpgradeState' {
            Assert-Installed $PreviousVersion
            New-Item -ItemType Directory -Path $config -Force | Out-Null
            $settings = '{"CheckAutomatically":false,"IgnoredVersion":"1.2.0","IncludeBetaRcChannels":true,"Kyra":{"Legacy":true}}'
            Set-Content -LiteralPath (Join-Path $config 'update-settings.json') -Value $settings -Encoding UTF8
            Set-Content -LiteralPath (Join-Path $config 'kyra-settings.json') -Value '{"Enabled":true,"Provider":"Legacy"}' -Encoding UTF8
            Set-Content -LiteralPath $canary -Value 'Unrelated guest user file' -Encoding UTF8
            $record.StateFiles = @(Get-ChildItem $config -File | ForEach-Object {
                @{ Name = $_.Name; Sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash }
            })
            Observe 'Representative known settings and inert legacy configuration seeded after previous GUI launch'
        }
        'Upgrade' {
            Assert-Installed $PreviousVersion
            $before = Get-Content (Join-Path $config 'update-settings.json') -Raw
            $consent = Join-Path $config 'terms-consent.json'
            $oldConsentHash = if (Test-Path $consent) { (Get-FileHash $consent -Algorithm SHA256).Hash } else { $null }
            Install $CandidateInstaller $CandidateSha256 'upgrade'
            Assert-Installed $CandidateVersion
            Assert ((Get-Content (Join-Path $config 'update-settings.json') -Raw) -eq $before) 'Installer preserves existing update configuration bytes'
            Assert ((Get-Content $canary -Raw).Trim() -eq 'Unrelated guest user file') 'Upgrade preserves unrelated user document'
            if ($oldConsentHash) { Assert ((Get-FileHash $consent -Algorithm SHA256).Hash -eq $oldConsentHash) 'Installer does not silently rewrite prior consent' }
            SelfTest
            Observe 'Changed-version terms must be explicitly reaccepted in separate interactive QA'
        }
        'PostUninstall' {
            Uninstall
            Assert (Test-Path (Join-Path $config 'update-settings.json')) 'User preferences deliberately retained'
            Assert (Test-Path (Join-Path $config 'kyra-settings.json')) 'Inert historical user configuration is retained, not destructively deleted'
        }
        'ResidueAudit' {
            # Every residue item must be explicitly classified: retained user
            # data is expected; anything installer-owned or product-named that
            # survives is a failure, never a silent pass.
            Assert (@(Get-Registrations).Count -eq 0) 'No product registration remains for residue audit'
            $expectedRetained = [System.Collections.Generic.List[string]]::new()
            $unexpected = [System.Collections.Generic.List[string]]::new()
            foreach ($f in @(Get-ChildItem $installDir -Recurse -Force -ErrorAction SilentlyContinue)) {
                $unexpected.Add("installer-owned residue: $($f.FullName)")
            }
            # Retained runtime data is allowed only as user-owned settings /
            # logs / reports. Executables, credential material, or security
            # payloads surviving under the runtime root are blocking residue.
            $allowedRetained = '\.(json|log|txt|etl|csv|md|dmp-candidate)$'
            $blockingTypes = '\.(exe|dll|sys|com|bat|cmd|ps1|psm1|msi|pfx|p12|key|pem|cer)$'
            foreach ($f in @(Get-ChildItem $runtime -Recurse -Force -File -ErrorAction SilentlyContinue)) {
                if ($f.Name -match $blockingTypes) {
                    $unexpected.Add("retained executable/credential residue: $($f.FullName)")
                } elseif ($f.Name -match $allowedRetained -or $f.PSIsContainer) {
                    $expectedRetained.Add("retained user data ($($f.Extension)): $($f.FullName)")
                } else {
                    $unexpected.Add("unclassified retained file: $($f.FullName)")
                }
            }
            foreach ($f in @(Get-ChildItem $runtime -Recurse -Force -Directory -ErrorAction SilentlyContinue)) {
                $expectedRetained.Add("retained user directory: $($f.FullName)")
            }
            $programData = Join-Path $env:ProgramData 'ForgerEMS'
            foreach ($f in @(Get-ChildItem $programData -Recurse -Force -ErrorAction SilentlyContinue)) {
                $unexpected.Add("ProgramData residue: $($f.FullName)")
            }
            foreach ($folder in @('CommonPrograms','Programs','CommonDesktopDirectory','DesktopDirectory')) {
                $link = Join-Path ([Environment]::GetFolderPath($folder)) 'ForgerEMS.lnk'
                if (Test-Path -LiteralPath $link) { $unexpected.Add("shortcut residue: $link") }
            }
            # TEMP leftovers: Inno Setup writes is-*.tmp during install; those
            # are installer-process-owned and classify explicitly — a name-only
            # ForgerEMS sweep would miss them. Actual owned paths are captured
            # on the record; nothing unrelated is deleted.
            $tempResidue = [System.Collections.Generic.List[string]]::new()
            foreach ($f in @(Get-ChildItem $env:TEMP -Force -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match '(?i)forgerems|kyra' -or $_.Name -match '^is-[A-Z0-9]+\.tmp$' })) {
                $class = if ($f.Name -match '^is-') { 'inno installer temp' } else { 'product-named temp' }
                $tempResidue.Add("$class`: $($f.FullName)")
                $unexpected.Add("$class residue: $($f.FullName)")
            }
            $record.TempResidueClassified = @($tempResidue)
            # Product registry roots: an empty leftover key is explained residue
            # (Inno removes values without uninsdeletekeyifempty); a key holding
            # real values/subkeys is blocking residue.
            foreach ($regRoot in @('HKLM:\SOFTWARE\ForgerEMS','HKCU:\SOFTWARE\ForgerEMS','HKLM:\SOFTWARE\WOW6432Node\ForgerEMS')) {
                if (-not (Test-Path $regRoot)) { continue }
                $regKey = Get-Item $regRoot
                $subKeyCount = [int]$regKey.SubKeyCount
                $valueCount = [int]$regKey.ValueCount
                if ($subKeyCount -eq 0 -and $valueCount -eq 0) {
                    $expectedRetained.Add("benign empty product key (values removed, key shell retained): $regRoot")
                } else {
                    $unexpected.Add("product registry residue ($subKeyCount subkeys, $valueCount values): $regRoot")
                }
            }
            if (Test-Path $canary) {
                Assert ((Get-Content $canary -Raw).Trim() -eq 'Unrelated guest user file') 'Unrelated user document preserved'
                $expectedRetained.Add("unrelated user file: $canary")
            }
            $record.ExpectedRetained = @($expectedRetained)
            $record.UnexpectedResidue = @($unexpected)
            Assert ($unexpected.Count -eq 0) 'All surviving files classified: only expected retained user data remains'
        }
        'DriverServiceTaskAudit' {
            $auditOutput = Join-Path $EvidenceRoot "$Phase-guestaudit.json"
            $canDiff = (-not [string]::IsNullOrWhiteSpace($BaselineAuditPath)) -and (Test-Path -LiteralPath $BaselineAuditPath) -and
                (-not [string]::IsNullOrWhiteSpace($GuestAuditScriptPath)) -and (Test-Path -LiteralPath $GuestAuditScriptPath)
            if (-not $canDiff) {
                # Name-filter enumeration alone is observational, never a pass.
                $record.ProductServices = @(Get-Service | Where-Object { $_.Name -match '(?i)forgerems|kyra' } | Select-Object Name,Status)
                $record.ProductTasks = @(Get-ScheduledTask | Where-Object { $_.TaskName -match '(?i)forgerems|kyra' } | Select-Object TaskName,State)
                $record.ProductDrivers = @(Get-CimInstance Win32_SystemDriver | Where-Object {
                    ($_.Name -match '(?i)forgerems|kyra') -or ([string]$_.PathName -match '(?i)forgerems|kyra')
                } | Select-Object Name,State,PathName)
                $script:PartialResult = $true
                Observe 'PARTIAL: no baseline guest-audit inventory supplied — name-filter enumeration only; promotion requires a baseline-diffed audit'
            }
            else {
                & $GuestAuditScriptPath -DisposableVmId $DisposableVmId -IsolationKind $IsolationKind `
                    -OutputPath $auditOutput -Label 'after-uninstall' -BaselinePath $BaselineAuditPath
                if ($LASTEXITCODE -ne 0) { throw "Guest audit inventory failed (exit $LASTEXITCODE)" }
                $audit = Get-Content -LiteralPath $auditOutput -Raw | ConvertFrom-Json
                $record.AuditVerdict = $audit.verdict
                $record.AuditDiff = $audit.diff
                Assert ($audit.verdict -eq 'EXPECTED-ONLY') 'Baseline-diffed guest audit found no unexpected product or security-surface changes'
            }
        }
    }
    $record.Result = if ($script:PartialResult) { 'PARTIAL' } else { 'PASS' }
}
catch {
    $record.Error = $_.Exception.Message
    throw
}
finally {
    $record.FinishedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    $record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $EvidenceRoot "$Phase.json") -Encoding UTF8
}
