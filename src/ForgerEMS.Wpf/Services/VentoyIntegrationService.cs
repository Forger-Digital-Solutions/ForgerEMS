#pragma warning disable CA1001 // VentoyIntegrationService.SemaphoreSlim is long-lived; disposal handled by host
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ForgerEMS.Wpf.Services.Resources;
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
    internal const string VentoyResourceId = "ventoy";

    private static readonly TimeSpan LatestVentoyTtl = TimeSpan.FromMinutes(20);

    private static readonly string[] GitHubArtifactHosts =
    {
        "github.com",
        "release-assets.githubusercontent.com",
        "objects.githubusercontent.com"
    };

    private readonly IPowerShellRunnerService _powerShellRunnerService;
    private readonly IAppRuntimeService _appRuntimeService;
    private readonly HttpClient? _httpClient;
    private readonly ResourceCheckService? _injectedCheckService;
    private readonly ArtifactDownloadService _artifactDownloads;

    /// <summary>
    /// Seam for the final user-confirmed Ventoy2Disk launch; tests substitute a capture
    /// so no process is ever started outside the real interactive flow.
    /// </summary>
    internal Func<ProcessStartInfo, Process?> ProcessStarter { get; set; } = info => Process.Start(info);
    private readonly SemaphoreSlim _resolutionLock = new(1, 1);
    private VentoyPackageResolution? _cachedLatestResolution;
    private DateTimeOffset _cachedLatestAtUtc;

    public VentoyIntegrationService(
        IPowerShellRunnerService powerShellRunnerService,
        IAppRuntimeService appRuntimeService,
        HttpClient? httpClient = null,
        ResourceCheckService? checkService = null,
        ArtifactDownloadService? artifactDownloads = null)
    {
        _powerShellRunnerService = powerShellRunnerService;
        _appRuntimeService = appRuntimeService;
        _httpClient = httpClient;
        _injectedCheckService = checkService;
        _artifactDownloads = artifactDownloads ?? new ArtifactDownloadService();
    }

    public async Task<VentoyStatusInfo> GetStatusAsync(
        BackendContext backendContext,
        UsbTargetInfo? target,
        CancellationToken cancellationToken = default)
    {
        var package = await ResolvePackageAsync(backendContext, cancellationToken).ConfigureAwait(false);
        var packageText = package is null
            ? "Official Ventoy package could not be resolved from the resource policy (offline or metadata unavailable)."
            : $"{package.DisplayName} | expected SHA-256 bound from official metadata | Source: {package.Url} ({package.SourceLabel})";

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
                Details =
                    "The official Ventoy package could not be resolved through the resource policy (offline, unavailable, or failed trust checks). ForgerEMS does not fall back to a frozen archive."
            };
        }

        _appRuntimeService.EnsureInitialized();
        Directory.CreateDirectory(_appRuntimeService.VentoyPackagesRoot);
        Directory.CreateDirectory(_appRuntimeService.VentoyExtractedRoot);

        onOutput?.Invoke(new LogLine(
            DateTimeOffset.Now,
            $"[INFO] Ventoy package source: {package.SourceLabel}; expected SHA-256 bound from official metadata.",
            LogSeverity.Info));

        var download = await _artifactDownloads.DownloadAsync(
            new ArtifactDownloadService.ArtifactDownloadRequest
            {
                RequestedId = VentoyResourceId,
                Version = package.Version,
                Source = package.SourceLabel,
                ArtifactUri = new Uri(package.Url),
                ExpectedSha256 = package.Sha256,
                ExpectedSizeBytes = package.SizeBytes,
                AllowedHosts = package.AllowedHosts
            },
            _appRuntimeService.VentoyPackagesRoot,
            cancellationToken).ConfigureAwait(false);

        if (!download.VerifiedArtifactPresent || download.FinalPath is null)
        {
            return new VentoyLaunchResult
            {
                Succeeded = false,
                Summary = "Ventoy package download failed verification",
                Details = download.Reason ?? download.State.ToString()
            };
        }

        onOutput?.Invoke(new LogLine(
            DateTimeOffset.Now,
            $"[OK] Ventoy package downloaded and SHA-256 verified: {download.ActualSha256}",
            LogSeverity.Success));

        var extraction = await SafeZipExtractor
            .ExtractToFreshDirectoryAsync(download.FinalPath, _appRuntimeService.VentoyExtractedRoot, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!extraction.Succeeded || extraction.OutputDirectory is null)
        {
            return new VentoyLaunchResult
            {
                Succeeded = false,
                Summary = "Ventoy package extraction failed",
                Details = extraction.FailureReason ?? "The archive could not be extracted safely."
            };
        }

        var ventoyExecutable = Directory
            .GetFiles(extraction.OutputDirectory, "Ventoy2Disk.exe", SearchOption.AllDirectories)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(ventoyExecutable))
        {
            return new VentoyLaunchResult
            {
                Succeeded = false,
                Summary = "Ventoy2Disk was not found",
                Details = "The official package was verified and extracted, but Ventoy2Disk.exe could not be located."
            };
        }

        try
        {
            ProcessStarter(new ProcessStartInfo
            {
                FileName = ventoyExecutable,
                WorkingDirectory = Path.GetDirectoryName(ventoyExecutable) ?? extraction.OutputDirectory,
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
                $"Official package {package.DisplayName} was hash-verified and extracted, and Ventoy2Disk.exe was launched. Complete the install/update in Ventoy2Disk for {target.RootPath}, then refresh the USB target list to inspect the device again."
        };
    }

    /// <summary>
    /// Resolves the official Ventoy Windows package through the shared resource policy
    /// ('ventoy' descriptor). There is no frozen/pinned fallback: when live official
    /// metadata cannot be resolved and verified, the package is unavailable.
    /// </summary>
    private async Task<VentoyPackageResolution?> ResolvePackageAsync(
        BackendContext backendContext,
        CancellationToken cancellationToken)
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

            var resolution = await TryResolveViaPolicyAsync(backendContext, cancellationToken)
                .ConfigureAwait(false);
            if (resolution is null)
            {
                return null;
            }

            _cachedLatestResolution = resolution;
            _cachedLatestAtUtc = DateTimeOffset.UtcNow;
            return resolution;
        }
        finally
        {
            _resolutionLock.Release();
        }
    }

    private async Task<VentoyPackageResolution?> TryResolveViaPolicyAsync(
        BackendContext backendContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var checkService = _injectedCheckService ?? BuildCheckService(backendContext);
            if (checkService is null)
            {
                return null;
            }

            var results = await checkService.CheckAsync(
                new ResourceCheckRequest
                {
                    ResourceIds = new[] { VentoyResourceId },
                    Architecture = "x64",
                    Channel = ResourcePolicyValues.StableChannel
                },
                cancellationToken).ConfigureAwait(false);

            var resolution = results.FirstOrDefault(r =>
                string.Equals(r.ResourceId, VentoyResourceId, StringComparison.Ordinal));
            if (resolution is null || !resolution.IsDownloadEligible)
            {
                return null;
            }

            var package = new ManifestVentoyPackage
            {
                DisplayName = resolution.Descriptor.DisplayName,
                Version = resolution.Version ?? string.Empty,
                Url = resolution.ArtifactUri!,
                Sha256 = resolution.ExpectedSha256!,
                FileName = resolution.ArtifactFileName ?? "ventoy-windows.zip",
                SizeBytes = resolution.SizeBytes,
                AllowedHosts = resolution.Descriptor.AllowedHosts
                    .Concat(GitHubArtifactHosts)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ManualNotePath = FindManualNotePath(backendContext)
            };

            var label = resolution.FromCache
                ? "Cached official metadata"
                : "Latest official release metadata";
            return new VentoyPackageResolution(package, label, string.Empty);
        }
        catch
        {
            return null;
        }
    }

    private ResourceCheckService? BuildCheckService(BackendContext backendContext)
    {
        var policyPath = ResourceCatalog.FindPolicyPath(backendContext);
        if (policyPath is null)
        {
            return null;
        }

        var catalog = ResourceCatalog.Parse(File.ReadAllText(policyPath));
        var providers = new ResourceProviderResolver(
            new OfficialMetadataClient(_httpClient),
            catalog.MaximumAttempts);
        var cache = new ResourceMetadataCache(ResourceCheckService.DefaultCacheDirectory());
        return new ResourceCheckService(catalog, providers, cache);
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

    private static string ToSingleQuotedPowerShellLiteral(string value)
    {
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
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

        public long? SizeBytes { get; init; }

        public IReadOnlyList<string> AllowedHosts { get; init; } = GitHubArtifactHosts;

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

        public long? SizeBytes => Package.SizeBytes;

        public IReadOnlyList<string> AllowedHosts => Package.AllowedHosts;

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
