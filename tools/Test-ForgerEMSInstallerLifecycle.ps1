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
    [Parameter(Mandatory)][ValidateSet('CleanInstall','Reinstall','Uninstall','PreviousInstall','SeedUpgradeState','Upgrade','PostUninstall')]
    [string]$Phase,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F-]{36}$')][string]$DisposableVmId,
    [Parameter(Mandatory)][string]$CandidateInstaller,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$CandidateSha256,
    [Parameter(Mandatory)][string]$PreviousInstaller,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$PreviousSha256,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$SourceHead,
    [ValidateSet('VirtualBox','QEMU','GitHubHosted')][string]$IsolationKind = 'VirtualBox',
    [string]$EvidenceRoot = 'C:\ForgerEMS-QA\Evidence'
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
$record = [ordered]@{
    Phase = $Phase; StartedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    SourceHead = $SourceHead; VmId = $product.UUID; ComputerModel = $computer.Model
    IsolationKind = $IsolationKind
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
    $process = Start-Process -FilePath $file -ArgumentList $arguments -PassThru `
        -RedirectStandardOutput $out -RedirectStandardError $err
    if (-not $process.WaitForExit($timeoutSeconds * 1000)) {
        # Only the process created by this phase is terminated on timeout.
        Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
        throw "$name timed out after $timeoutSeconds seconds"
    }
    $process.WaitForExit()
    $record.Processes += @{ File = $file; Arguments = $arguments; ExitCode = $process.ExitCode; Stdout = $out; Stderr = $err }
    Assert ($process.ExitCode -eq 0) "$name exits 0 without a reboot request"
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
    if ($version -eq '1.2.4') {
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
try {
    switch ($Phase) {
        'CleanInstall' {
            Assert (@(Get-Registrations).Count -eq 0) 'Fresh guest has no ForgerEMS product registration'
            Install $CandidateInstaller $CandidateSha256 'install'
            Assert-Installed '1.2.4'
            SelfTest
        }
        'Reinstall' {
            Assert-Installed '1.2.4'
            Install $CandidateInstaller $CandidateSha256 'reinstall'
            Assert-Installed '1.2.4'
            SelfTest
            Observe 'Same-version reinstall tested; no distinct repair mode is claimed'
        }
        'Uninstall' { Uninstall }
        'PreviousInstall' {
            Assert (@(Get-Registrations).Count -eq 0) 'Previous release starts without duplicate registration'
            Install $PreviousInstaller $PreviousSha256 'previous-install'
            Assert-Installed '1.2.3-preview.1'
        }
        'SeedUpgradeState' {
            Assert-Installed '1.2.3-preview.1'
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
            Assert-Installed '1.2.3-preview.1'
            $before = Get-Content (Join-Path $config 'update-settings.json') -Raw
            $consent = Join-Path $config 'terms-consent.json'
            $oldConsentHash = if (Test-Path $consent) { (Get-FileHash $consent -Algorithm SHA256).Hash } else { $null }
            Install $CandidateInstaller $CandidateSha256 'upgrade'
            Assert-Installed '1.2.4'
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
    }
    $record.Result = 'PASS'
}
catch {
    $record.Error = $_.Exception.Message
    throw
}
finally {
    $record.FinishedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    $record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $EvidenceRoot "$Phase.json") -Encoding UTF8
}
