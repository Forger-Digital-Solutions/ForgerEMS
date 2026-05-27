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
    // 2026-05-27 catalog-expansion (Batch 6) promotion pass added 18 managed file entries
    // (15 OS / ISO + 3 technician tool), bringing the active count from 32 to 50.
    // See docs/MANAGED_DOWNLOAD_EXPANSION_REPORT.md for the per-entry proof trail.
    private const int ExpectedActiveManagedDownloadCount = 50;

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

    [Theory]
    [InlineData("Rufus 4.14",            "4.14")]
    [InlineData("Ventoy",                "1.1.12")]
    [InlineData("balenaEtcher 2.1.6",    "2.1.6")]
    [InlineData("Rescuezilla 2.6.2",     "2.6.2")]
    [InlineData("MemTest86+ 8.10",       "8.10")]
    [InlineData("Alpine Linux 3.23.4",   "3.23.4")]
    [InlineData("AlmaLinux 10.2",        "10.2")]
    public void SourceManifest_ContainsLatestPromotedVersion(string nameFragment, string expectedLatestStableVersion)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(SourceManifestPath));
        var match = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .FirstOrDefault(item =>
                string.Equals(GetString(item, "type"), "file", StringComparison.OrdinalIgnoreCase) &&
                (item.TryGetProperty("enabled", out var enabled) ? enabled.GetBoolean() : true) &&
                GetString(item, "name").Contains(nameFragment, StringComparison.OrdinalIgnoreCase));

        Assert.True(
            match.ValueKind != JsonValueKind.Undefined,
            $"No active managed file item matched '{nameFragment}'. Did a promotion get reverted?");

        var freshness = match.GetProperty("freshness");
        Assert.Equal(expectedLatestStableVersion, GetString(freshness, "latestKnownStableVersion"));
        Assert.Equal(expectedLatestStableVersion, GetString(freshness, "currentPinnedVersion"));
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

    private static IEnumerable<string> PackagedManifestCopiesThatExist()
    {
        var candidates = new[]
        {
            Path.Combine(RepoRoot, "release", "current", "app", "manifests", "ForgerEMS.updates.json"),
            Path.Combine(RepoRoot, "release", "current", "app", "backend", "ForgerEMS.updates.json"),
            Path.Combine(RepoRoot, "dist", "backend-stage", "backend", "ForgerEMS.updates.json"),
        };

        return candidates.Where(File.Exists);
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
