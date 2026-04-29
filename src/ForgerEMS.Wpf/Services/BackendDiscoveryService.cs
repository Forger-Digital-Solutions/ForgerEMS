using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services;

public interface IBackendDiscoveryService
{
    BackendContext Discover(string? preferredStartPath = null);
}

public sealed class BackendDiscoveryService : IBackendDiscoveryService
{
    private const string BundledBackendFolderName = "backend";
    private const string BundledBackendMetadataFileName = "ForgerEMS.bundled-backend.json";
    private const string BackendOverrideEnvironmentVariable = "FORGEREMS_BACKEND_ROOT";

    private static readonly string[] RequiredScriptNames =
    [
        "Verify-VentoyCore.ps1",
        "Setup-ForgerEMS.ps1",
        "Update-ForgerEMS.ps1"
    ];

    private static readonly string[] RequiredBundledRelativePaths =
    [
        "Verify-VentoyCore.ps1",
        "Setup-ForgerEMS.ps1",
        "Update-ForgerEMS.ps1",
        "ForgerEMS.Runtime.ps1",
        "Setup_Toolkit.ps1",
        "Setup_USB_Toolkit.ps1",
        "SystemIntelligence\\Invoke-ForgerEMSSystemScan.ps1",
        "ToolkitManager\\Get-ForgerEMSToolkitHealth.ps1",
        "ForgerEMS.updates.json",
        "VERSION.txt",
        "RELEASE-BUNDLE.txt",
        "CHECKSUMS.sha256",
        "SIGNATURE.txt",
        "manifests\\ForgerEMS.updates.schema.json",
        "manifests\\vendor.inventory.json",
        "manifests\\vendor.inventory.schema.json",
        BundledBackendMetadataFileName
    ];

    private static readonly string[] RequiredChecksummedRelativePaths =
    [
        "Verify-VentoyCore.ps1",
        "Setup-ForgerEMS.ps1",
        "Update-ForgerEMS.ps1",
        "ForgerEMS.Runtime.ps1",
        "Setup_Toolkit.ps1",
        "Setup_USB_Toolkit.ps1",
        "SystemIntelligence\\Invoke-ForgerEMSSystemScan.ps1",
        "ToolkitManager\\Get-ForgerEMSToolkitHealth.ps1",
        "ForgerEMS.updates.json",
        "VERSION.txt",
        "RELEASE-BUNDLE.txt",
        "manifests\\ForgerEMS.updates.schema.json",
        "manifests\\vendor.inventory.json",
        "manifests\\vendor.inventory.schema.json"
    ];

    private readonly string _frontendVersion;

    public BackendDiscoveryService(string? frontendVersion = null)
    {
        _frontendVersion = string.IsNullOrWhiteSpace(frontendVersion)
            ? GetCurrentFrontendVersion()
            : frontendVersion;
    }

    public BackendContext Discover(string? preferredStartPath = null)
    {
        var diagnostics = new List<string>();

        var overrideRoot = ResolveOverrideRoot(preferredStartPath);
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            var overrideRoots = GetCandidateRoots([overrideRoot]).ToList();
            var overrideContext = TryDiscoverAcrossRoots(overrideRoots, "Advanced override", diagnostics);
            if (overrideContext is not null)
            {
                return overrideContext;
            }

            diagnostics.Add($"Advanced override did not resolve a valid backend from '{overrideRoot}'.");
        }

        var bundledRoot = Path.Combine(AppContext.BaseDirectory, BundledBackendFolderName);
        if (Directory.Exists(bundledRoot))
        {
            if (TryCreateBundledBackendContext(bundledRoot, out var bundledContext, out var bundledFailure))
            {
                return bundledContext;
            }

            diagnostics.Add(bundledFailure);
        }

        var searchedRoots = GetCandidateRoots([Directory.GetCurrentDirectory(), AppContext.BaseDirectory]).ToList();
        var discoveredContext = TryDiscoverAcrossRoots(searchedRoots, string.Empty, diagnostics);
        if (discoveredContext is not null)
        {
            return discoveredContext;
        }

        var message = searchedRoots.Count == 0
            ? "No searchable roots were available."
            : "Could not find Verify-VentoyCore.ps1, Setup-ForgerEMS.ps1, and Update-ForgerEMS.ps1 in bundled, repo, or release-bundle mode. Searched: " +
              string.Join(" | ", searchedRoots.Take(8));

        if (diagnostics.Count > 0)
        {
            message = string.Join(" ", diagnostics.Append(message));
        }

        return BackendContext.Unavailable(message, _frontendVersion);
    }

    private BackendContext? TryDiscoverAcrossRoots(
        IReadOnlyList<string> searchedRoots,
        string discoveryLabel,
        List<string> diagnostics)
    {
        foreach (var root in searchedRoots)
        {
            if (TryCreateRepoContext(root, discoveryLabel, diagnostics, out var repoContext))
            {
                return repoContext;
            }
        }

        foreach (var root in searchedRoots)
        {
            if (TryCreateReleaseBundleContext(root, discoveryLabel, diagnostics, out var releaseContext))
            {
                return releaseContext;
            }
        }

        return null;
    }

    private bool TryCreateBundledBackendContext(
        string bundledRoot,
        out BackendContext context,
        out string failureMessage)
    {
        context = BackendContext.Unavailable("Bundled backend validation did not run.", _frontendVersion);

        var missingPaths = RequiredBundledRelativePaths
            .Select(relativePath => new
            {
                RelativePath = relativePath,
                FullPath = Path.Combine(bundledRoot, relativePath.Replace('\\', Path.DirectorySeparatorChar))
            })
            .Where(entry => !File.Exists(entry.FullPath))
            .Select(entry => entry.RelativePath)
            .ToList();

        if (missingPaths.Count > 0)
        {
            failureMessage =
                $"Bundled backend at '{bundledRoot}' is incomplete and will be ignored. Missing: {string.Join(", ", missingPaths)}.";
            return false;
        }

        BundledBackendMetadata? metadata;
        var metadataPath = Path.Combine(bundledRoot, BundledBackendMetadataFileName);
        try
        {
            metadata = JsonSerializer.Deserialize<BundledBackendMetadata>(
                File.ReadAllText(metadataPath),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }
        catch (Exception exception)
        {
            failureMessage =
                $"Bundled backend metadata could not be read from '{metadataPath}'. {exception.Message}";
            return false;
        }

        if (metadata is null || metadata.SchemaVersion != 1)
        {
            failureMessage =
                $"Bundled backend metadata at '{metadataPath}' is invalid and will be ignored.";
            return false;
        }

        if (!TryValidateBundledChecksums(bundledRoot, out var checksumFailure))
        {
            failureMessage =
                $"Bundled backend checksum validation failed and the bundle will not be used. {checksumFailure}";
            return false;
        }

        var compatibilityMessage = string.Equals(metadata.FrontendVersion, _frontendVersion, StringComparison.OrdinalIgnoreCase)
            ? $"Frontend {_frontendVersion} is aligned with backend {metadata.BackendVersion}."
            : $"Frontend {_frontendVersion} is running with backend {metadata.BackendVersion}; bundled metadata expected frontend {metadata.FrontendVersion}. Status: Warning.";

        context = new BackendContext
        {
            IsAvailable = true,
            Mode = BackendMode.Bundled,
            RootPath = bundledRoot,
            WorkingDirectory = bundledRoot,
            VerifyScriptPath = Path.Combine(bundledRoot, "Verify-VentoyCore.ps1"),
            SetupScriptPath = Path.Combine(bundledRoot, "Setup-ForgerEMS.ps1"),
            UpdateScriptPath = Path.Combine(bundledRoot, "Update-ForgerEMS.ps1"),
            FrontendVersion = _frontendVersion,
            BackendVersion = metadata.BackendVersion,
            DiagnosticMessage =
                $"Bundled backend verified. {compatibilityMessage}"
        };

        failureMessage = string.Empty;
        return true;
    }

    private bool TryCreateRepoContext(
        string root,
        string discoveryLabel,
        IReadOnlyCollection<string> diagnostics,
        out BackendContext context)
    {
        var repoScriptsRoot = Path.Combine(root, "backend");
        if (!HasScripts(repoScriptsRoot))
        {
            context = BackendContext.Unavailable(string.Empty, _frontendVersion);
            return false;
        }

        var prefix = string.IsNullOrWhiteSpace(discoveryLabel)
            ? "Repo-mode backend discovered."
            : $"{discoveryLabel} selected repo-mode backend.";
        var backendVersion = TryReadBackendVersion(Path.Combine(root, "manifests", "ForgerEMS.updates.json"), Path.Combine(root, "VERSION.txt"));

        context = new BackendContext
        {
            IsAvailable = true,
            Mode = BackendMode.Repo,
            RootPath = root,
            WorkingDirectory = root,
            VerifyScriptPath = Path.Combine(repoScriptsRoot, "Verify-VentoyCore.ps1"),
            SetupScriptPath = Path.Combine(repoScriptsRoot, "Setup-ForgerEMS.ps1"),
            UpdateScriptPath = Path.Combine(repoScriptsRoot, "Update-ForgerEMS.ps1"),
            FrontendVersion = _frontendVersion,
            BackendVersion = backendVersion,
            DiagnosticMessage = BuildDiagnosticMessage(prefix, backendVersion, diagnostics)
        };

        return true;
    }

    private bool TryCreateReleaseBundleContext(
        string root,
        string discoveryLabel,
        IReadOnlyCollection<string> diagnostics,
        out BackendContext context)
    {
        if (!(HasScripts(root) && HasReleaseBundleMarkers(root)))
        {
            context = BackendContext.Unavailable(string.Empty, _frontendVersion);
            return false;
        }

        var prefix = string.IsNullOrWhiteSpace(discoveryLabel)
            ? "External release-bundle backend discovered."
            : $"{discoveryLabel} selected release-bundle backend.";
        var backendVersion = TryReadBackendVersion(Path.Combine(root, "ForgerEMS.updates.json"), Path.Combine(root, "VERSION.txt"));

        context = new BackendContext
        {
            IsAvailable = true,
            Mode = BackendMode.ReleaseBundle,
            RootPath = root,
            WorkingDirectory = root,
            VerifyScriptPath = Path.Combine(root, "Verify-VentoyCore.ps1"),
            SetupScriptPath = Path.Combine(root, "Setup-ForgerEMS.ps1"),
            UpdateScriptPath = Path.Combine(root, "Update-ForgerEMS.ps1"),
            FrontendVersion = _frontendVersion,
            BackendVersion = backendVersion,
            DiagnosticMessage = BuildDiagnosticMessage(prefix, backendVersion, diagnostics)
        };

        return true;
    }

    private static bool HasScripts(string root)
    {
        return RequiredScriptNames.All(scriptName => File.Exists(Path.Combine(root, scriptName)));
    }

    private static bool HasReleaseBundleMarkers(string root)
    {
        return File.Exists(Path.Combine(root, "VERSION.txt")) ||
               File.Exists(Path.Combine(root, "RELEASE-BUNDLE.txt"));
    }

    private static string ResolveOverrideRoot(string? preferredStartPath)
    {
        if (!string.IsNullOrWhiteSpace(preferredStartPath))
        {
            return preferredStartPath;
        }

        return Environment.GetEnvironmentVariable(BackendOverrideEnvironmentVariable) ?? string.Empty;
    }

    private static IEnumerable<string> GetCandidateRoots(IEnumerable<string?> seeds)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var seed in seeds)
        {
            var normalizedSeed = NormalizeSearchSeed(seed);
            if (string.IsNullOrWhiteSpace(normalizedSeed) || !Directory.Exists(normalizedSeed))
            {
                continue;
            }

            var current = Path.GetFullPath(normalizedSeed);
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (seen.Add(current))
                {
                    yield return current;
                }

                var parent = Directory.GetParent(current);
                if (parent is null)
                {
                    break;
                }

                current = parent.FullName;
            }
        }
    }

    private static string NormalizeSearchSeed(string? seed)
    {
        if (string.IsNullOrWhiteSpace(seed))
        {
            return string.Empty;
        }

        if (Directory.Exists(seed))
        {
            return seed;
        }

        return File.Exists(seed)
            ? Path.GetDirectoryName(Path.GetFullPath(seed)) ?? string.Empty
            : string.Empty;
    }

    private static string BuildDiagnosticMessage(string prefix, string backendVersion, IReadOnlyCollection<string> diagnostics)
    {
        var parts = new List<string> { prefix };

        if (!string.IsNullOrWhiteSpace(backendVersion))
        {
            parts.Add($"Backend version {backendVersion}.");
        }

        if (diagnostics.Count > 0)
        {
            parts.Add("Fallback notes: " + string.Join(" ", diagnostics));
        }

        return string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string GetCurrentFrontendVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(BackendDiscoveryService).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        var version = assembly.GetName().Version;
        return version is null
            ? "unknown"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static string TryReadBackendVersion(string manifestPath, string versionFilePath)
    {
        if (File.Exists(manifestPath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                if (document.RootElement.TryGetProperty("coreVersion", out var property))
                {
                    var coreVersion = property.GetString();
                    if (!string.IsNullOrWhiteSpace(coreVersion))
                    {
                        return coreVersion;
                    }
                }
            }
            catch
            {
            }
        }

        if (File.Exists(versionFilePath))
        {
            try
            {
                var line = File.ReadLines(versionFilePath)
                    .FirstOrDefault(candidate => candidate.StartsWith("Version:", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(line))
                {
                    return line.Split(':', 2).Last().Trim();
                }
            }
            catch
            {
            }
        }

        return string.Empty;
    }

    private static bool TryValidateBundledChecksums(string bundledRoot, out string failureMessage)
    {
        var checksumPath = Path.Combine(bundledRoot, "CHECKSUMS.sha256");
        if (!File.Exists(checksumPath))
        {
            failureMessage = "CHECKSUMS.sha256 is missing.";
            return false;
        }

        Dictionary<string, string> catalog;
        try
        {
            catalog = File.ReadLines(checksumPath)
                .Select(ParseChecksumLine)
                .Where(entry => entry.HasValue)
                .ToDictionary(
                    entry => NormalizeRelativePath(entry!.Value.RelativePath),
                    entry => entry!.Value.Hash,
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception)
        {
            failureMessage = $"CHECKSUMS.sha256 could not be parsed. {exception.Message}";
            return false;
        }

        foreach (var relativePath in RequiredChecksummedRelativePaths)
        {
            var normalizedRelativePath = NormalizeRelativePath(relativePath);
            if (!catalog.TryGetValue(normalizedRelativePath, out var expectedHash))
            {
                failureMessage = $"Checksum entry is missing for '{relativePath}'.";
                return false;
            }

            var fullPath = Path.Combine(bundledRoot, relativePath.Replace('\\', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                failureMessage = $"Required bundled file '{relativePath}' is missing.";
                return false;
            }

            using var stream = File.OpenRead(fullPath);
            var actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                failureMessage =
                    $"Checksum mismatch for '{relativePath}'. Expected {expectedHash}, received {actualHash}.";
                return false;
            }
        }

        failureMessage = string.Empty;
        return true;
    }

    private static (string RelativePath, string Hash)? ParseChecksumLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var match = Regex.Match(line.Trim(), "^(?<hash>[a-fA-F0-9]{64})\\s+\\*?(?<path>.+)$");
        if (!match.Success)
        {
            return null;
        }

        return (match.Groups["path"].Value.Trim(), match.Groups["hash"].Value.ToLowerInvariant());
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return relativePath.Replace('\\', '/');
    }
}
