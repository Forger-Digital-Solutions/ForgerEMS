using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

// Coverage for the legacy-Windows UX cleanup pass (no more "INFO - Windows XP (Wikipedia)" style
// shortcuts, manual-ISO drop zones are seeded by Update-ForgerEMS.ps1, no fake ISO files written).
public sealed class LegacyWindowsUxAndExtrasTests
{
    private static readonly string[] LegacyWindowsVersions =
    {
        "Windows 8.1",
        "Windows 8",
        "Windows 7",
        "Windows Vista",
        "Windows XP",
        "Windows 2000",
        "Windows ME",
        "Windows 98",
        "Windows 95",
    };

    private static readonly string[] ModernWindowsDownloadDestEndings =
    {
        @"ISO\Windows\DOWNLOAD - Windows 10.url",
        @"ISO\Windows\DOWNLOAD - Windows 11.url",
        @"ISO\Windows\DOWNLOAD - Windows Server Eval.url",
    };

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

            throw new InvalidOperationException("Could not locate ForgerEMS.sln.");
        }
    }

    private static string SourceManifestPath =>
        Path.Combine(RepoRoot, "manifests", "ForgerEMS.updates.json");

    private static JsonElement[] LegacyWindowsItems()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(SourceManifestPath));
        return document.RootElement.GetProperty("items")
            .EnumerateArray()
            .Where(item =>
            {
                var dest = GetString(item, "dest");
                return dest.StartsWith(@"ISO\Windows-Legacy\", StringComparison.OrdinalIgnoreCase);
            })
            .Select(item => item.Clone())
            .ToArray();
    }

    [Fact]
    public void LegacyWindows_AllNineEntriesPresent()
    {
        var items = LegacyWindowsItems();
        Assert.Equal(LegacyWindowsVersions.Length, items.Length);
    }

    [Theory]
    [InlineData("Windows 8.1")]
    [InlineData("Windows 8")]
    [InlineData("Windows 7")]
    [InlineData("Windows Vista")]
    [InlineData("Windows XP")]
    [InlineData("Windows 2000")]
    [InlineData("Windows ME")]
    [InlineData("Windows 98")]
    [InlineData("Windows 95")]
    public void LegacyWindows_ShortcutFilenameUsesManualIsoRequiredPrefix(string version)
    {
        var dest = LegacyDestFor(version);
        var fileName = Path.GetFileName(dest);
        Assert.StartsWith("MANUAL ISO REQUIRED - ", fileName, StringComparison.Ordinal);
        Assert.EndsWith(".url", fileName, StringComparison.Ordinal);
        // No more "(Wikipedia)" or "(Lifecycle)" suffix noise in the filename.
        Assert.DoesNotContain("(Wikipedia)", fileName, StringComparison.Ordinal);
        Assert.DoesNotContain("(Lifecycle)", fileName, StringComparison.Ordinal);
        Assert.DoesNotContain("INFO -", fileName, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyWindows_AllManualOnlyAndPageType()
    {
        foreach (var item in LegacyWindowsItems())
        {
            var name = GetString(item, "name");
            Assert.Equal("page", GetString(item, "type"));
            Assert.True(
                item.TryGetProperty("manualOnly", out var mo) && mo.GetBoolean(),
                $"{name} must be manualOnly=true.");
            Assert.False(
                string.IsNullOrWhiteSpace(GetString(item, "legacyWarning")),
                $"{name} must have a legacyWarning.");
        }
    }

    [Fact]
    public void LegacyWindows_NeverManagedFileDownload()
    {
        foreach (var item in LegacyWindowsItems())
        {
            var name = GetString(item, "name");
            // Catalog never auto-downloads legacy Windows.
            Assert.NotEqual("file", GetString(item, "type"));
            Assert.False(
                item.TryGetProperty("sha256", out _) ||
                item.TryGetProperty("sha256Url", out _) ||
                item.TryGetProperty("sha512", out _) ||
                item.TryGetProperty("sha512Url", out _),
                $"{name} must not have checksum fields (it must remain manual-only).");
        }
    }

    [Theory]
    [InlineData("Windows 8.1", "https://learn.microsoft.com/lifecycle/products/windows-81")]
    [InlineData("Windows 8",   "https://learn.microsoft.com/lifecycle/products/windows-8")]
    [InlineData("Windows 7",   "https://learn.microsoft.com/lifecycle/products/windows-7")]
    [InlineData("Windows Vista", "https://learn.microsoft.com/lifecycle/products/windows-vista")]
    [InlineData("Windows XP",  "https://learn.microsoft.com/lifecycle/products/windows-xp")]
    [InlineData("Windows 2000", "https://learn.microsoft.com/lifecycle/products/windows-2000")]
    public void LegacyWindows_LifecyclePagesPointAtMicrosoftLearn(string version, string expectedUrl)
    {
        var item = ItemAtLegacyDest(version);
        Assert.Equal(expectedUrl, GetString(item, "url"));
    }

    [Theory]
    [InlineData("Windows ME")]
    [InlineData("Windows 98")]
    [InlineData("Windows 95")]
    public void LegacyWindows_9xEntriesPointAtNeutralReferenceAndAreMarkedManualTrust(string version)
    {
        var item = ItemAtLegacyDest(version);
        var url = GetString(item, "url");
        Assert.StartsWith("https://en.wikipedia.org/wiki/", url, StringComparison.OrdinalIgnoreCase);
        // sourceTrust must reflect that the URL is not an official Microsoft page.
        Assert.Equal("manual", GetString(item, "sourceTrust"));
    }

    [Fact]
    public void LegacyWindows_NoEntryUsesUnofficialIsoMirrorOrPiracyHost()
    {
        var bannedHostFragments = new[]
        {
            "archive.org/details/MS_",          // common abandonware indexes
            "winworldpc",
            "winboard",
            "msdn-files.com",
            "msdn-windows.com",
            "softlay",
            "filehippo",
            "fileplanet",
            "mediafire",
            "thepiratebay",
            "1337x",
            "rutracker",
            "kickass",
        };

        foreach (var item in LegacyWindowsItems())
        {
            var name = GetString(item, "name");
            var url = GetString(item, "url");
            foreach (var fragment in bannedHostFragments)
            {
                Assert.DoesNotContain(fragment, url, StringComparison.OrdinalIgnoreCase);
            }

            // URLs must be HTTPS to either Microsoft (official) or Wikipedia (neutral reference).
            Assert.True(Uri.TryCreate(url, UriKind.Absolute, out var parsed), $"{name} url not absolute.");
            Assert.Equal("https", parsed!.Scheme);
            var host = parsed.Host;
            Assert.True(
                host.EndsWith("microsoft.com", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("wikipedia.org", StringComparison.OrdinalIgnoreCase),
                $"{name} url host '{host}' is not on the allow-list (microsoft.com / wikipedia.org).");
        }
    }

    [Theory]
    [MemberData(nameof(ModernWindowsDownloadDestEndingsData))]
    public void ModernWindows_DownloadShortcutsStillPresent(string expectedDest)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(SourceManifestPath));
        var hit = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .Any(item => string.Equals(GetString(item, "dest"), expectedDest, StringComparison.OrdinalIgnoreCase));
        Assert.True(hit, $"Expected DOWNLOAD shortcut for {expectedDest} is missing.");
    }

    public static IEnumerable<object[]> ModernWindowsDownloadDestEndingsData =>
        ModernWindowsDownloadDestEndings.Select(d => new object[] { d });

    [Fact]
    public void ManifestExtras_HasSeedDirectoriesAndReadmes()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(SourceManifestPath));
        Assert.True(document.RootElement.TryGetProperty("extras", out var extras), "Manifest must declare extras.");
        Assert.True(extras.TryGetProperty("seedDirectories", out var dirs), "extras.seedDirectories missing.");
        Assert.True(extras.TryGetProperty("readmes", out var readmes), "extras.readmes missing.");
        Assert.True(dirs.GetArrayLength() >= 12, "Expect at least 12 manual-ISO drop directories.");
        Assert.True(readmes.GetArrayLength() >= 13, "Expect master + legacy READMEs plus one per drop folder.");
    }

    [Theory]
    [InlineData(@"ISO\Windows\Windows-Manual-ISO-Drop")]
    [InlineData(@"ISO\Windows\Windows-Manual-ISO-Drop\Windows 11")]
    [InlineData(@"ISO\Windows\Windows-Manual-ISO-Drop\Windows 10")]
    [InlineData(@"ISO\Windows\Windows-Manual-ISO-Drop\Windows 8.1")]
    [InlineData(@"ISO\Windows\Windows-Manual-ISO-Drop\Windows 8")]
    [InlineData(@"ISO\Windows\Windows-Manual-ISO-Drop\Windows 7")]
    [InlineData(@"ISO\Windows\Windows-Manual-ISO-Drop\Windows Vista")]
    [InlineData(@"ISO\Windows\Windows-Manual-ISO-Drop\Windows XP")]
    [InlineData(@"ISO\Windows\Windows-Manual-ISO-Drop\Windows 2000")]
    [InlineData(@"ISO\Windows\Windows-Manual-ISO-Drop\Windows ME")]
    [InlineData(@"ISO\Windows\Windows-Manual-ISO-Drop\Windows 98")]
    [InlineData(@"ISO\Windows\Windows-Manual-ISO-Drop\Windows 95")]
    public void ManifestExtras_DeclaresExpectedDropDirectory(string expected)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(SourceManifestPath));
        var dirs = document.RootElement.GetProperty("extras").GetProperty("seedDirectories")
            .EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray();
        Assert.Contains(expected, dirs);
    }

    [Theory]
    [InlineData(@"ISO\Windows\README - Windows ISO workflow.txt")]
    [InlineData(@"ISO\Windows-Legacy\README - Legacy Windows media.txt")]
    public void ManifestExtras_DeclaresExpectedToplevelReadme(string expectedDest)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(SourceManifestPath));
        var hit = document.RootElement.GetProperty("extras").GetProperty("readmes")
            .EnumerateArray()
            .Any(r => string.Equals(GetString(r, "dest"), expectedDest, StringComparison.OrdinalIgnoreCase));
        Assert.True(hit, $"Manifest extras must declare README at {expectedDest}.");
    }

    [Fact]
    public void ManifestExtras_AllReadmeDestinationsAreTxtOrMd()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(SourceManifestPath));
        foreach (var readme in document.RootElement.GetProperty("extras").GetProperty("readmes").EnumerateArray())
        {
            var dest = GetString(readme, "dest");
            var ext = Path.GetExtension(dest).ToLowerInvariant();
            Assert.True(
                ext == ".txt" || ext == ".md",
                $"README dest '{dest}' must end in .txt or .md (no fake ISO/executable allowed).");
        }
    }

    [Fact]
    public void ManifestExtras_NoReadmeBodyMentionsUnofficialMirror()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(SourceManifestPath));
        foreach (var readme in document.RootElement.GetProperty("extras").GetProperty("readmes").EnumerateArray())
        {
            var body = ConcatBody(readme);
            // Every README must explicitly tell technicians not to use mirrors.
            Assert.Contains("do not use random internet iso mirrors", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void UpdateScript_HasExtrasHandlerAndCallSite()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot, "backend", "Update-ForgerEMS.ps1"));
        Assert.Contains("function Invoke-ManifestExtras", script, StringComparison.Ordinal);
        Assert.Contains("Invoke-ManifestExtras -Manifest $manifest -Root $root", script, StringComparison.Ordinal);
        Assert.Contains("IncludedCategorySet $builderCategorySet", script, StringComparison.Ordinal);
        // Refuse non-text README destinations defensively.
        Assert.Contains("refusing to write non-text README", script, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateScript_NeverWritesFakeIsoFiles()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot, "backend", "Update-ForgerEMS.ps1"));
        // The extras path must guard against .iso/.exe/.wim destinations even if a future manifest
        // typo points at one.
        Assert.Contains(".txt", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("New-Item -ItemType File", script, StringComparison.OrdinalIgnoreCase);
    }

    private static string LegacyDestFor(string version) =>
        $@"ISO\Windows-Legacy\MANUAL ISO REQUIRED - {version}.url";

    private static JsonElement ItemAtLegacyDest(string version)
    {
        var expected = LegacyDestFor(version);
        using var document = JsonDocument.Parse(File.ReadAllText(SourceManifestPath));
        var hits = document.RootElement.GetProperty("items").EnumerateArray()
            .Where(i => string.Equals(GetString(i, "dest"), expected, StringComparison.OrdinalIgnoreCase))
            .Select(i => i.Clone())
            .ToArray();
        Assert.Single(hits);
        return hits[0];
    }

    private static string ConcatBody(JsonElement readme)
    {
        if (!readme.TryGetProperty("body", out var body))
        {
            return string.Empty;
        }

        if (body.ValueKind == JsonValueKind.String)
        {
            return body.GetString() ?? string.Empty;
        }

        if (body.ValueKind == JsonValueKind.Array)
        {
            return string.Join('\n', body.EnumerateArray().Select(b => b.GetString() ?? string.Empty));
        }

        return string.Empty;
    }

    private static string GetString(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
