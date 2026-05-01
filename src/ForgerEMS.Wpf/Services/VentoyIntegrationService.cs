using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services;

public interface IVentoyIntegrationService
{
    Task<VentoyStatusInfo> GetStatusAsync(
        BackendContext backendContext,
        UsbTargetInfo? target,
        CancellationToken cancellationToken = default);

    Task<VentoyLaunchResult> InstallOrUpdateAsync(
        BackendContext backendContext,
        UsbTargetInfo target,
        Action<LogLine>? onOutput = null,
        CancellationToken cancellationToken = default);
}

public sealed class VentoyIntegrationService : IVentoyIntegrationService
{
    private readonly IPowerShellRunnerService _powerShellRunnerService;
    private readonly IAppRuntimeService _appRuntimeService;

    public VentoyIntegrationService(
        IPowerShellRunnerService powerShellRunnerService,
        IAppRuntimeService appRuntimeService)
    {
        _powerShellRunnerService = powerShellRunnerService;
        _appRuntimeService = appRuntimeService;
    }

    public async Task<VentoyStatusInfo> GetStatusAsync(
        BackendContext backendContext,
        UsbTargetInfo? target,
        CancellationToken cancellationToken = default)
    {
        var package = await TryLoadPackageAsync(backendContext, cancellationToken).ConfigureAwait(false);
        var packageText = package is null
            ? "Official Ventoy package source was not found in the backend manifest."
            : $"{package.DisplayName} | SHA-256 pinned in manifest | Source: {package.Url}";

        if (target is null)
        {
            return new VentoyStatusInfo
            {
                PackageAvailable = package is not null,
                HasTarget = false,
                StatusText = "Select a USB target",
                DetailText = "Choose a USB target to inspect whether Ventoy already appears to be installed on that device.",
                PackageText = packageText,
                PackageVersion = package?.Version ?? string.Empty,
                OfficialDownloadUrl = package?.Url ?? string.Empty,
                ManualNotePath = package?.ManualNotePath ?? string.Empty
            };
        }

        var detection = await DetectVentoyAsync(target, backendContext, cancellationToken).ConfigureAwait(false);

        return new VentoyStatusInfo
        {
            PackageAvailable = package is not null,
            HasTarget = true,
            IsInstalled = detection.IsInstalled,
            InstalledVersion = detection.InstalledVersion,
            StatusText = detection.IsInstalled ? "Ventoy detected" : "Ventoy not detected",
            DetailText = detection.DetailText,
            PackageText = packageText,
            PackageVersion = package?.Version ?? string.Empty,
            OfficialDownloadUrl = package?.Url ?? string.Empty,
            ManualNotePath = package?.ManualNotePath ?? string.Empty
        };
    }

    public async Task<VentoyLaunchResult> InstallOrUpdateAsync(
        BackendContext backendContext,
        UsbTargetInfo target,
        Action<LogLine>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        var package = await TryLoadPackageAsync(backendContext, cancellationToken).ConfigureAwait(false);
        if (package is null)
        {
            return new VentoyLaunchResult
            {
                Succeeded = false,
                Summary = "Ventoy package source unavailable",
                Details = "The official Ventoy package entry could not be resolved from the current backend manifest."
            };
        }

        _appRuntimeService.EnsureInitialized();

        var extractRoot = Path.Combine(_appRuntimeService.VentoyExtractedRoot, Path.GetFileNameWithoutExtension(package.FileName));
        var packagePath = Path.Combine(_appRuntimeService.VentoyPackagesRoot, package.FileName);

        var request = new PowerShellRunRequest
        {
            DisplayName = "Prepare official Ventoy package",
            WorkingDirectory = backendContext.WorkingDirectory,
            InlineCommand = BuildPreparationCommand(package.Url, packagePath, extractRoot, package.Sha256),
            ProgressItemName = "Ventoy package"
        };

        var runResult = await _powerShellRunnerService.RunAsync(request, onOutput, cancellationToken).ConfigureAwait(false);
        if (!runResult.Succeeded)
        {
            return new VentoyLaunchResult
            {
                Succeeded = false,
                Summary = "Ventoy package preparation failed",
                Details = $"PowerShell exited with code {runResult.ExitCode}. Review the log pane for the download or extraction failure."
            };
        }

        var ventoyExecutable = Directory
            .GetFiles(extractRoot, "Ventoy2Disk.exe", SearchOption.AllDirectories)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(ventoyExecutable))
        {
            return new VentoyLaunchResult
            {
                Succeeded = false,
                Summary = "Ventoy2Disk was not found",
                Details = "The official package was prepared, but Ventoy2Disk.exe could not be located after extraction."
            };
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ventoyExecutable,
                WorkingDirectory = Path.GetDirectoryName(ventoyExecutable) ?? extractRoot,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            return new VentoyLaunchResult
            {
                Succeeded = false,
                Summary = "Ventoy2Disk could not be launched",
                Details = exception.Message
            };
        }

        return new VentoyLaunchResult
        {
            Succeeded = true,
            Summary = "Ventoy2Disk launched",
            Details =
                $"Official package {package.DisplayName} was prepared and Ventoy2Disk.exe was launched. Complete the install/update in Ventoy2Disk for {target.RootPath}, then refresh the USB target list to inspect the device again."
        };
    }

    private static async Task<ManifestVentoyPackage?> TryLoadPackageAsync(BackendContext backendContext, CancellationToken cancellationToken)
    {
        if (!backendContext.IsAvailable)
        {
            return null;
        }

        foreach (var manifestPath in GetManifestCandidatePaths(backendContext))
        {
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            await using var stream = File.OpenRead(manifestPath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in items.EnumerateArray())
            {
                var name = GetString(item, "name");
                var type = GetString(item, "type");
                var destination = GetString(item, "dest");
                var url = GetString(item, "url");
                var sha256 = GetString(item, "sha256");

                if (!string.Equals(type, "file", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!name.StartsWith("Ventoy ", StringComparison.OrdinalIgnoreCase) &&
                    !destination.Contains("ventoy-", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var fileName = Path.GetFileName(destination.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    fileName = Path.GetFileName(new Uri(url).AbsolutePath);
                }

                return new ManifestVentoyPackage
                {
                    DisplayName = name,
                    Version = ExtractVersion(name, fileName),
                    Url = url,
                    Sha256 = sha256,
                    FileName = fileName,
                    ManualNotePath = FindManualNotePath(backendContext)
                };
            }
        }

        return null;
    }

    private async Task<VentoyDetectionResult> DetectVentoyAsync(
        UsbTargetInfo target,
        BackendContext backendContext,
        CancellationToken cancellationToken)
    {
        var rootLiteral = ToSingleQuotedPowerShellLiteral(target.RootPath);
        var request = new PowerShellRunRequest
        {
            DisplayName = "Inspect Ventoy status",
            WorkingDirectory = backendContext.WorkingDirectory,
            InlineCommand = $$"""
                $ErrorActionPreference = 'Stop'
                $root = {{rootLiteral}}
                $driveLetter = ([System.IO.Path]::GetPathRoot($root)).TrimEnd('\', ':')
                $labels = New-Object System.Collections.Generic.List[string]
                $hasVentoyFolder = $false
                $version = 'Unknown'

                foreach ($candidate in @(
                    (Join-Path $root 'ventoy'),
                    (Join-Path $root 'EFI\ventoy'),
                    (Join-Path $root 'EFI\BOOT')
                )) {
                    if (Test-Path -LiteralPath $candidate) {
                        $hasVentoyFolder = $true
                    }
                }

                try {
                    $partition = Get-Partition -DriveLetter $driveLetter -ErrorAction Stop | Select-Object -First 1
                    $disk = $partition | Get-Disk -ErrorAction Stop | Select-Object -First 1
                    $allPartitions = @(Get-Partition -DiskNumber $disk.Number -ErrorAction SilentlyContinue)
                    foreach ($item in $allPartitions) {
                        try {
                            $volume = $item | Get-Volume -ErrorAction Stop
                            if ($volume -and $volume.FileSystemLabel) {
                                [void]$labels.Add([string]$volume.FileSystemLabel)
                            }
                        }
                        catch {
                        }
                    }
                }
                catch {
                }

                foreach ($candidate in @(
                    (Join-Path $root 'ventoy\version'),
                    (Join-Path $root 'ventoy\version.txt'),
                    (Join-Path $root 'EFI\ventoy\version'),
                    (Join-Path $root 'EFI\ventoy\version.txt')
                )) {
                    if (-not (Test-Path -LiteralPath $candidate)) {
                        continue
                    }

                    try {
                        $content = Get-Content -LiteralPath $candidate -Raw -ErrorAction Stop
                        $match = [regex]::Match($content, '\d+\.\d+\.\d+')
                        if ($match.Success) {
                            $version = $match.Value
                            break
                        }
                    }
                    catch {
                    }
                }

                $labelList = @($labels | Select-Object -Unique)
                $hasVentoyPartition = $labelList -contains 'Ventoy' -or $labelList -contains 'VTOYEFI'
                $isInstalled = $hasVentoyFolder -or $hasVentoyPartition
                $detail = if ($isInstalled) {
                    if ($labelList.Count -gt 0) {
                        'Detected Ventoy markers on disk labels: ' + ($labelList -join ', ')
                    }
                    elseif ($hasVentoyFolder) {
                        'Detected Ventoy-related folder structure on the selected volume.'
                    }
                    else {
                        'Detected Ventoy-related markers on the selected USB.'
                    }
                }
                else {
                    'No Ventoy partition labels or known folder markers were detected on the selected USB.'
                }

                [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
                [pscustomobject]@{
                    IsInstalled      = $isInstalled
                    InstalledVersion = $version
                    DetailText       = $detail
                } | ConvertTo-Json -Compress -Depth 3
                """
        };

        try
        {
            var result = await _powerShellRunnerService.RunAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded || string.IsNullOrWhiteSpace(result.StandardOutputText))
            {
                return VentoyDetectionResult.Missing("Ventoy detection could not confirm whether the selected USB already has Ventoy.");
            }

            using var document = JsonDocument.Parse(result.StandardOutputText);
            var root = document.RootElement;
            return new VentoyDetectionResult
            {
                IsInstalled = GetBoolean(root, "IsInstalled"),
                InstalledVersion = GetString(root, "InstalledVersion", "Unknown"),
                DetailText = GetString(root, "DetailText", "Ventoy detection completed.")
            };
        }
        catch
        {
            return VentoyDetectionResult.Missing("Ventoy detection could not confirm whether the selected USB already has Ventoy.");
        }
    }

    private static IEnumerable<string> GetManifestCandidatePaths(BackendContext backendContext)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in new[]
        {
            backendContext.PrimaryManifestPath,
            backendContext.RepoManifestPath,
            Path.Combine(backendContext.WorkingDirectory, "ForgerEMS.updates.json"),
            Path.Combine(backendContext.WorkingDirectory, "manifests", "ForgerEMS.updates.json")
        })
        {
            if (!string.IsNullOrWhiteSpace(path) && seen.Add(path))
            {
                yield return path;
            }
        }
    }

    private static string FindManualNotePath(BackendContext backendContext)
    {
        foreach (var path in new[]
        {
            backendContext.ReleaseVentoyManualNotePath,
            backendContext.RepoVentoyManualNotePath
        })
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                return path;
            }
        }

        return string.Empty;
    }

    private static string BuildPreparationCommand(string packageUrl, string packagePath, string extractRoot, string expectedSha256)
    {
        return $$"""
            $ErrorActionPreference = 'Stop'
            $ProgressPreference = 'SilentlyContinue'
            try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch {}

            $packageUrl = {{ToSingleQuotedPowerShellLiteral(packageUrl)}}
            $packagePath = {{ToSingleQuotedPowerShellLiteral(packagePath)}}
            $extractRoot = {{ToSingleQuotedPowerShellLiteral(extractRoot)}}
            $expectedSha256 = {{ToSingleQuotedPowerShellLiteral(expectedSha256)}}

            $packageDirectory = Split-Path -Parent $packagePath
            New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
            New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null

            $needsDownload = $true
            if (Test-Path -LiteralPath $packagePath) {
                $existingHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
                if ($existingHash -eq $expectedSha256) {
                    Write-Host '[OK] Reusing cached official Ventoy package.'
                    $needsDownload = $false
                }
                else {
                    Write-Host '[WARN] Cached Ventoy package hash mismatch. Re-downloading.'
                    Remove-Item -LiteralPath $packagePath -Force -ErrorAction SilentlyContinue
                }
            }

            if ($needsDownload) {
                try {
                    Start-BitsTransfer -Source $packageUrl -Destination $packagePath -ErrorAction Stop
                }
                catch {
                    Write-Host '[WARN] BITS download failed, falling back to Invoke-WebRequest.'
                    Invoke-WebRequest -Uri $packageUrl -OutFile $packagePath -UseBasicParsing -Headers @{ 'User-Agent' = 'ForgerEMS-Wpf/1.0' }
                }

                $actualHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
                if ($actualHash -ne $expectedSha256) {
                    throw ('SHA-256 mismatch for Ventoy package. Expected ' + $expectedSha256 + ' but received ' + $actualHash + '.')
                }

                Write-Host '[OK] Downloaded and verified the official Ventoy package.'
            }

            $ventoyExecutable = Get-ChildItem -Path $extractRoot -Filter 'Ventoy2Disk.exe' -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 1
            if (-not $ventoyExecutable) {
                Expand-Archive -LiteralPath $packagePath -DestinationPath $extractRoot -Force
                $ventoyExecutable = Get-ChildItem -Path $extractRoot -Filter 'Ventoy2Disk.exe' -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 1
            }

            if (-not $ventoyExecutable) {
                throw 'Ventoy2Disk.exe was not found after extracting the official package.'
            }

            Write-Host ('[OK] Ventoy package ready: ' + $ventoyExecutable.FullName)
            Write-Host '[WARN] Ventoy installation remains an operator-confirmed action inside Ventoy2Disk.'
            """;
    }

    private static string ToSingleQuotedPowerShellLiteral(string value)
    {
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private static string ExtractVersion(string displayName, string fileName)
    {
        foreach (var candidate in new[] { displayName, fileName })
        {
            var match = Regex.Match(candidate, @"\d+\.\d+\.\d+");
            if (match.Success)
            {
                return match.Value;
            }
        }

        return string.Empty;
    }

    private static string GetString(JsonElement element, string propertyName, string defaultValue = "")
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.GetString() ?? defaultValue
            : defaultValue;
    }

    private static bool GetBoolean(JsonElement element, string propertyName, bool defaultValue = false)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : defaultValue;
    }

    private sealed class ManifestVentoyPackage
    {
        public string DisplayName { get; init; } = string.Empty;

        public string Version { get; init; } = string.Empty;

        public string Url { get; init; } = string.Empty;

        public string Sha256 { get; init; } = string.Empty;

        public string FileName { get; init; } = string.Empty;

        public string ManualNotePath { get; init; } = string.Empty;
    }

    private sealed class VentoyDetectionResult
    {
        public bool IsInstalled { get; init; }

        public string InstalledVersion { get; init; } = "Unknown";

        public string DetailText { get; init; } = string.Empty;

        public static VentoyDetectionResult Missing(string detailText)
        {
            return new VentoyDetectionResult
            {
                IsInstalled = false,
                InstalledVersion = "Unknown",
                DetailText = detailText
            };
        }
    }
}
