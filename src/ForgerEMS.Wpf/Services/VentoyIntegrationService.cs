#pragma warning disable CA1001 // VentoyIntegrationService.SemaphoreSlim is long-lived; disposal handled by host
#pragma warning disable CS0414 // _ownsHttpClient: reserved for future disposal path
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
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
    private static readonly TimeSpan LatestVentoyTtl = TimeSpan.FromMinutes(20);
    private static readonly Regex VersionPattern = new(@"\d+\.\d+\.\d+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex VentoyWindowsAssetPattern = new(
        @"^ventoy-(\d+\.\d+\.\d+)-windows\.zip$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ShaLinePattern = new(
        @"(?<sha>[a-fA-F0-9]{64})\s+[* ]?(?<file>ventoy-\d+\.\d+\.\d+-windows\.zip)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IPowerShellRunnerService _powerShellRunnerService;
    private readonly IAppRuntimeService _appRuntimeService;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly SemaphoreSlim _resolutionLock = new(1, 1);
    private VentoyPackageResolution? _cachedLatestResolution;
    private DateTimeOffset _cachedLatestAtUtc;

    public VentoyIntegrationService(
        IPowerShellRunnerService powerShellRunnerService,
        IAppRuntimeService appRuntimeService,
        HttpClient? httpClient = null)
    {
        _powerShellRunnerService = powerShellRunnerService;
        _appRuntimeService = appRuntimeService;
        if (httpClient is not null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(20)
            };
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "ForgerEMS-Wpf/1.2");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            _ownsHttpClient = true;
        }
    }

    public async Task<VentoyStatusInfo> GetStatusAsync(
        BackendContext backendContext,
        UsbTargetInfo? target,
        CancellationToken cancellationToken = default)
    {
        var package = await ResolvePackageAsync(backendContext, cancellationToken).ConfigureAwait(false);
        var packageText = package is null
            ? "Official Ventoy package source was not found in the backend manifest."
            : $"{package.DisplayName} | SHA-256 verified | Source: {package.Url} ({package.SourceLabel})";

        if (target is null)
        {
            return new VentoyStatusInfo
            {
                PackageAvailable = package is not null,
                HasTarget = false,
                StatusText = "Select a USB target",
                DetailText = "Choose a USB target to inspect whether Ventoy already appears to be installed on that device.",
                PackageText = packageText,
                PackageVersion = package?.Package.Version ?? string.Empty,
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
            PackageVersion = package?.Package.Version ?? string.Empty,
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
        var package = await ResolvePackageAsync(backendContext, cancellationToken).ConfigureAwait(false);
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
            InlineCommand = BuildPreparationCommand(package.Url, packagePath, extractRoot, package.Sha256, package.SourceLabel, package.ResolutionNote),
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

    private async Task<VentoyPackageResolution?> ResolvePackageAsync(BackendContext backendContext, CancellationToken cancellationToken)
    {
        if (_cachedLatestResolution is not null &&
            DateTimeOffset.UtcNow - _cachedLatestAtUtc < LatestVentoyTtl)
        {
            return _cachedLatestResolution;
        }

        await _resolutionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedLatestResolution is not null &&
                DateTimeOffset.UtcNow - _cachedLatestAtUtc < LatestVentoyTtl)
            {
                return _cachedLatestResolution;
            }

            var pinned = await TryLoadPinnedPackageAsync(backendContext, cancellationToken).ConfigureAwait(false);
            var latest = await TryResolveLatestGitHubReleaseAsync(backendContext, pinned, cancellationToken).ConfigureAwait(false);
            if (latest is not null)
            {
                _cachedLatestResolution = latest;
                _cachedLatestAtUtc = DateTimeOffset.UtcNow;
                return latest;
            }

            if (pinned is null)
            {
                return null;
            }

            var fallback = new VentoyPackageResolution(
                pinned,
                "Pinned fallback",
                $"Latest lookup unavailable; using pinned verified Ventoy package {pinned.Version}.");
            _cachedLatestResolution = fallback;
            _cachedLatestAtUtc = DateTimeOffset.UtcNow;
            return fallback;
        }
        finally
        {
            _resolutionLock.Release();
        }
    }

    private async Task<VentoyPackageResolution?> TryResolveLatestGitHubReleaseAsync(
        BackendContext backendContext,
        ManifestVentoyPackage? pinned,
        CancellationToken cancellationToken)
    {
        try
        {
            using var releaseResponse = await _httpClient
                .GetAsync("https://api.github.com/repos/ventoy/Ventoy/releases/latest", cancellationToken)
                .ConfigureAwait(false);
            if (!releaseResponse.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await releaseResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var releaseDoc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = releaseDoc.RootElement;
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            string? zipUrl = null;
            string? zipName = null;
            string? releaseTag = GetString(root, "tag_name");
            string? checksumUrl = null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = GetString(asset, "name");
                var url = GetString(asset, "browser_download_url");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                if (VentoyWindowsAssetPattern.IsMatch(name))
                {
                    zipName = name;
                    zipUrl = url;
                }
                else if (name.Contains("sha256", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("checksum", StringComparison.OrdinalIgnoreCase))
                {
                    checksumUrl ??= url;
                }
            }

            if (string.IsNullOrWhiteSpace(zipUrl) || string.IsNullOrWhiteSpace(zipName))
            {
                return null;
            }

            var version = ExtractVersion(releaseTag ?? string.Empty, zipName);
            var sha = await TryReadReleaseSha256Async(zipName, checksumUrl, root, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(sha))
            {
                return null;
            }

            var latest = new ManifestVentoyPackage
            {
                DisplayName = $"Ventoy {version} (Windows package)",
                Version = version,
                Url = zipUrl,
                Sha256 = sha,
                FileName = zipName,
                ManualNotePath = FindManualNotePath(backendContext)
            };

            var source = pinned is not null &&
                         string.Equals(pinned.Version, latest.Version, StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(pinned.Sha256, latest.Sha256, StringComparison.OrdinalIgnoreCase)
                ? "Cached latest"
                : "Latest official release";
            return new VentoyPackageResolution(latest, source, string.Empty);
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> TryReadReleaseSha256Async(
        string zipName,
        string? checksumUrl,
        JsonElement releaseRoot,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(checksumUrl))
        {
            try
            {
                var checksumBody = await _httpClient.GetStringAsync(checksumUrl, cancellationToken).ConfigureAwait(false);
                var parsedFromAsset = ParseSha256FromText(checksumBody, zipName);
                if (!string.IsNullOrWhiteSpace(parsedFromAsset))
                {
                    return parsedFromAsset;
                }
            }
            catch
            {
            }
        }

        var body = GetString(releaseRoot, "body");
        return ParseSha256FromText(body, zipName);
    }

    private static string? ParseSha256FromText(string? text, string zipName)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (Match match in ShaLinePattern.Matches(text))
        {
            var file = match.Groups["file"].Value;
            if (string.Equals(file, zipName, StringComparison.OrdinalIgnoreCase))
            {
                return match.Groups["sha"].Value.ToLowerInvariant();
            }
        }

        return null;
    }

    private static async Task<ManifestVentoyPackage?> TryLoadPinnedPackageAsync(BackendContext backendContext, CancellationToken cancellationToken)
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

    private static string BuildPreparationCommand(
        string packageUrl,
        string packagePath,
        string extractRoot,
        string expectedSha256,
        string sourceLabel,
        string resolutionNote)
    {
        return $$"""
            $ErrorActionPreference = 'Stop'
            $ProgressPreference = 'SilentlyContinue'
            try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch {}

            $packageUrl = {{ToSingleQuotedPowerShellLiteral(packageUrl)}}
            $packagePath = {{ToSingleQuotedPowerShellLiteral(packagePath)}}
            $extractRoot = {{ToSingleQuotedPowerShellLiteral(extractRoot)}}
            $expectedSha256 = {{ToSingleQuotedPowerShellLiteral(expectedSha256)}}
            $sourceLabel = {{ToSingleQuotedPowerShellLiteral(sourceLabel)}}
            $resolutionNote = {{ToSingleQuotedPowerShellLiteral(resolutionNote)}}

            Write-Host ('[INFO] Ventoy package source: ' + $sourceLabel)
            if (-not [string]::IsNullOrWhiteSpace($resolutionNote)) {
                Write-Host ('[WARN] ' + $resolutionNote)
            }

            $runtimeCandidates = @(
                (Join-Path (Get-Location).Path 'ForgerEMS.Runtime.ps1'),
                (Join-Path (Get-Location).Path 'backend\ForgerEMS.Runtime.ps1')
            ) | Select-Object -Unique

            $runtimeLoaded = $false
            foreach ($candidate in $runtimeCandidates) {
                if (Test-Path -LiteralPath $candidate) {
                    . $candidate
                    $runtimeLoaded = $true
                    break
                }
            }

            if (-not $runtimeLoaded -or -not (Get-Command -Name Get-ForgerSha256 -ErrorAction SilentlyContinue)) {
                throw 'Could not load the ForgerEMS SHA-256 helper needed to verify the Ventoy package.'
            }

            function Write-ForgerHashProviderLog {
                param([Parameter(Mandatory = $true)][string]$Path)

                $provider = Get-ForgerLastHashProvider
                if ([string]::IsNullOrWhiteSpace($provider)) {
                    $provider = 'Unknown'
                }

                $friendlyProvider = switch ($provider) {
                    'DotNetFallback' { 'Built-in .NET (large-file safe)' }
                    'Get-FileHash' { 'Windows Get-FileHash' }
                    default { $provider }
                }

                $safePath = Get-ForgerSafePathForLog -Path $Path
                Write-Host ('[INFO] SHA256 hash provider: ' + $friendlyProvider + ' file=' + $safePath)
            }

            function Get-VerifiedVentoyPackageHash {
                param([Parameter(Mandatory = $true)][string]$Path)

                try {
                    $hash = Get-ForgerSha256 -LiteralPath $Path
                    Write-ForgerHashProviderLog -Path $Path
                    return $hash
                }
                catch {
                    throw ('Could not verify Ventoy package checksum. ' + $_.Exception.Message)
                }
            }

            $packageDirectory = Split-Path -Parent $packagePath
            New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
            New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null

            $needsDownload = $true
            if (Test-Path -LiteralPath $packagePath) {
                $existingHash = Get-VerifiedVentoyPackageHash -Path $packagePath
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

                $actualHash = Get-VerifiedVentoyPackageHash -Path $packagePath
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
            var match = VersionPattern.Match(candidate);
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

    private sealed class VentoyPackageResolution
    {
        public VentoyPackageResolution(ManifestVentoyPackage package, string sourceLabel, string resolutionNote)
        {
            Package = package;
            SourceLabel = sourceLabel;
            ResolutionNote = resolutionNote;
        }

        public ManifestVentoyPackage Package { get; }

        public string SourceLabel { get; }

        public string ResolutionNote { get; }

        public string DisplayName => Package.DisplayName;

        public string Version => Package.Version;

        public string Url => Package.Url;

        public string Sha256 => Package.Sha256;

        public string FileName => Package.FileName;

        public string ManualNotePath => Package.ManualNotePath;
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
