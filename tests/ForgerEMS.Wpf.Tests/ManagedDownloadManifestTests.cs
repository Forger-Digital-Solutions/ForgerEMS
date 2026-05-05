using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class ManagedDownloadManifestTests
{
    [Fact]
    public void ManagedDownloadManifest_HasOnlyAbsoluteOfficialLookingUrls()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var items = document.RootElement.GetProperty("items").EnumerateArray().ToArray();

        Assert.NotEmpty(items);
        foreach (var item in items)
        {
            var name = item.GetProperty("name").GetString() ?? "(unnamed)";
            var url = item.GetProperty("url").GetString();
            Assert.True(Uri.TryCreate(url, UriKind.Absolute, out var parsed), $"{name} has a malformed URL.");
            Assert.Equal("https", parsed!.Scheme);
            Assert.DoesNotContain("placeholder", url!, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("example.com", url!, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("seed", url!, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ManagedDownloadManifest_FileItemsHaveChecksumCoverage()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var missing = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .Where(item => string.Equals(GetString(item, "type"), "file", StringComparison.OrdinalIgnoreCase))
            .Where(item => string.IsNullOrWhiteSpace(GetString(item, "sha256")) &&
                           string.IsNullOrWhiteSpace(GetString(item, "sha256Url")))
            .Select(item => GetString(item, "name"))
            .ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void ManagedDownloadManifest_DestinationsStayRelative()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            var name = GetString(item, "name");
            var dest = GetString(item, "dest");

            Assert.False(string.IsNullOrWhiteSpace(dest), $"{name} is missing dest.");
            Assert.False(Path.IsPathRooted(dest), $"{name} destination must be relative.");
            Assert.DoesNotContain("..", dest, StringComparison.Ordinal);
            Assert.DoesNotContain(":", dest, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("Debian Live Images Download Page")]
    [InlineData("Arch Linux Download Page")]
    [InlineData("FreeDOS Download Page")]
    [InlineData("7-Zip Download Page")]
    [InlineData("Nmap Download Page")]
    [InlineData("Microsoft Visual C++ Redistributable Download Page")]
    [InlineData(".NET 8 Desktop Runtime Download Page")]
    [InlineData("Firefox All Languages Download Page")]
    [InlineData("Chrome Enterprise Browser Download Page")]
    public void ManagedDownloadManifest_2026TechnicianAdditionsStayManualOnly(string itemName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var item = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .SingleOrDefault(e => string.Equals(GetString(e, "name"), itemName, StringComparison.Ordinal));

        Assert.NotEqual(default, item.ValueKind);
        Assert.Equal("page", GetString(item, "type"));
        Assert.Contains("Manual", GetString(item, "notes"), StringComparison.OrdinalIgnoreCase);
    }

    private static string GetString(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find repo file {relativePath}");
    }
}
