using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

// Regression coverage for the v1.2.1-preview.1 packaged Update USB mismatch.
// A stale ForgerEMS.updates.json left on a previously seeded USB was shadowing
// the freshly packaged catalog. These tests pin (1) the source catalog shape,
// (2) packaging fidelity for every released manifest copy, and (3) the
// Update-ForgerEMS.ps1 resolution order so bundled wins over USB-side.
public sealed class PackagedManifestFreshnessTests
{
    // After the central-resource migration every managed file entry is a
    // requiresResolution item bound to a resource-policy descriptor; the active
    // managed file count is the descriptor-backed set, not the legacy pinned list.
    private const int ExpectedActiveManagedDownloadCount = 20;

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "ForgerEMS.sln")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException("Could not locate ForgerEMS.sln from test base directory.");
        }
    }

    private static string SourceManifestPath =>
        Path.Combine(RepoRoot, "manifests", "ForgerEMS.updates.json");

    [Fact]
    public void SourceManifest_HasExpectedActiveManagedDownloadCount()
    {
        var active = CountActiveManagedFileItems(SourceManifestPath);
        Assert.Equal(ExpectedActiveManagedDownloadCount, active);
    }

    // The promotion pass survives the resource-policy migration: each promoted
    // entry must still exist. Managed entries are runtime-resolved now, so the
    // version is chosen by the trusted overlay at download time — assert the
    // entry, its type, and its resource binding rather than a frozen version.
    [Theory]
    [InlineData("Rufus Portable",        "file",  "rufus")]
    [InlineData("Ventoy Windows Package","file",  "ventoy")]
    [InlineData("balenaEtcher",          "file",  "etcher")]
    [InlineData("Rescuezilla",           "file",  "rescuezilla")]
    [InlineData("MemTest86+",            "page",  "")]
    [InlineData("Alpine Linux",          "page",  "")]
    [InlineData("AlmaLinux",             "page",  "")]
    public void SourceManifest_ContainsPromotedEntry(string nameFragment, string expectedType, string expectedResourceId)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(SourceManifestPath));
        var match = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .FirstOrDefault(item =>
                string.Equals(GetString(item, "type"), expectedType, StringComparison.OrdinalIgnoreCase) &&
                (item.TryGetProperty("enabled", out var enabled) ? enabled.GetBoolean() : true) &&
                GetString(item, "name").Contains(nameFragment, StringComparison.OrdinalIgnoreCase));

        Assert.True(
            match.ValueKind != JsonValueKind.Undefined,
            $"No active {expectedType} item matched '{nameFragment}'. Did a promotion get reverted?");

        if (expectedType == "file")
        {
            Assert.True(match.TryGetProperty("requiresResolution", out var rr) && rr.GetBoolean(),
                $"Managed item '{nameFragment}' must be requiresResolution (overlay-bound).");
            Assert.Equal(expectedResourceId, GetString(match, "resourceId"));
        }
    }

    [Fact]
    public void PackagedManifests_MatchSourceByteForByte()
    {
        var sourceHash = HashFile(SourceManifestPath);

        foreach (var packagedPath in PackagedManifestCopiesThatExist())
        {
            var packagedHash = HashFile(packagedPath);
            Assert.True(
                string.Equals(sourceHash, packagedHash, StringComparison.OrdinalIgnoreCase),
                $"Packaged manifest diverges from source: {packagedPath}\nsource={sourceHash}\npackaged={packagedHash}");
        }
    }

    // A packaged copy belongs to the CURRENT catalog only when its sibling release
    // metadata names the source manifest's backendVersion. Older staging folders
    // are preserved historical artifacts (packaging refuses to erase them) and are
    // intentionally skipped rather than refreshed in place.
    private static bool IsCurrentVersionPackagedCopy(string packagedManifestPath, string sourceCoreVersion)
    {
        var dir = Path.GetDirectoryName(packagedManifestPath)!;
        var metadataCandidates = new[]
        {
            // release\current\app\manifests\x.json -> release\current\release.json
            Path.GetFullPath(Path.Combine(dir, "..", "..", "release.json")),
            // dist\backend-stage\backend\x.json -> ForgerEMS.bundled-backend.json
            Path.Combine(dir, "ForgerEMS.bundled-backend.json"),
        };

        foreach (var metadataPath in metadataCandidates)
        {
            if (!File.Exists(metadataPath))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(metadataPath));
                if (doc.RootElement.TryGetProperty("backendVersion", out var v) &&
                    v.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(v.GetString()))
                {
                    return string.Equals(v.GetString(), sourceCoreVersion, StringComparison.Ordinal);
                }
            }
            catch (JsonException)
            {
                // Unparseable sidecar: fall through to the next candidate.
            }
        }

        return true; // No metadata sibling — treat as a current copy and require the match.
    }

    [Fact]
    public void PackagedManifests_AllReportExpectedActiveManagedDownloadCount()
    {
        foreach (var packagedPath in PackagedManifestCopiesThatExist())
        {
            var active = CountActiveManagedFileItems(packagedPath);
            Assert.True(
                active == ExpectedActiveManagedDownloadCount,
                $"Packaged manifest {packagedPath} has {active} active managed downloads, expected {ExpectedActiveManagedDownloadCount}.");
        }
    }

    [Fact]
    public void UpdateForgerEms_ResolveManifestPath_PrefersBundledOverUsbSide()
    {
        var scriptText = File.ReadAllText(Path.Combine(RepoRoot, "backend", "Update-ForgerEMS.ps1"));

        var bundledLine = "Join-Path $PSScriptRoot $ManifestSpecifier";
        var usbRootLine = "Resolve-RootChildPath -Root $Root -RelativePath $ManifestSpecifier";

        var bundledIndex = scriptText.IndexOf(bundledLine, StringComparison.Ordinal);
        var usbIndex = scriptText.IndexOf(usbRootLine, StringComparison.Ordinal);

        Assert.True(bundledIndex >= 0, "Update-ForgerEMS.ps1 no longer references the bundled manifest candidate.");
        Assert.True(usbIndex >= 0, "Update-ForgerEMS.ps1 no longer references the USB-root manifest candidate.");
        Assert.True(
            bundledIndex < usbIndex,
            "Update-ForgerEMS.ps1 must list the bundled manifest candidate before the USB-root candidate. "
            + "A stale USB-side manifest must not shadow the freshly packaged catalog.");
    }

    [Fact]
    public void UpdateForgerEms_LogsManifestHashAndItemCounts()
    {
        var scriptText = File.ReadAllText(Path.Combine(RepoRoot, "backend", "Update-ForgerEMS.ps1"));
        Assert.Contains("Manifest SHA256:", scriptText, StringComparison.Ordinal);
        Assert.Contains("Manifest items: total=", scriptText, StringComparison.Ordinal);
    }

    private static int CountActiveManagedFileItems(string manifestPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        return document.RootElement.GetProperty("items")
            .EnumerateArray()
            .Count(item =>
                string.Equals(GetString(item, "type"), "file", StringComparison.OrdinalIgnoreCase) &&
                (item.TryGetProperty("enabled", out var enabled) ? enabled.GetBoolean() : true));
    }

    private static string SourceCoreVersion
    {
        get
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(SourceManifestPath));
            return doc.RootElement.TryGetProperty("coreVersion", out var v) ? v.GetString() ?? "" : "";
        }
    }

    private static IEnumerable<string> PackagedManifestCopiesThatExist()
    {
        var candidates = new[]
        {
            Path.Combine(RepoRoot, "release", "current", "app", "manifests", "ForgerEMS.updates.json"),
            Path.Combine(RepoRoot, "release", "current", "app", "backend", "ForgerEMS.updates.json"),
            Path.Combine(RepoRoot, "dist", "backend-stage", "backend", "ForgerEMS.updates.json"),
        };

        var coreVersion = SourceCoreVersion;
        return candidates
            .Where(File.Exists)
            .Where(path => IsCurrentVersionPackagedCopy(path, coreVersion));
    }

    private static string HashFile(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static string GetString(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
