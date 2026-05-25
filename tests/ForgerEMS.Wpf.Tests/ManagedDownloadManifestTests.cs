using System;
using System.Collections.Generic;
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
                           string.IsNullOrWhiteSpace(GetString(item, "sha256Url")) &&
                           string.IsNullOrWhiteSpace(GetString(item, "sha512")) &&
                           string.IsNullOrWhiteSpace(GetString(item, "sha512Url")))
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

    [Theory]
    [InlineData("ReactOS Download Page")]
    [InlineData("KeePass Download Page")]
    [InlineData("TestDisk and PhotoRec Download Page")]
    [InlineData("Smartmontools Download Page")]
    public void ManagedDownloadManifest_Batch2UnsafeCandidatesStayManualOnly(string itemName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var item = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .SingleOrDefault(e => string.Equals(GetString(e, "name"), itemName, StringComparison.Ordinal));

        Assert.NotEqual(default, item.ValueKind);
        Assert.Equal("page", GetString(item, "type"));
        Assert.True(item.TryGetProperty("manualOnly", out var manualOnly) && manualOnly.GetBoolean(),
            $"{itemName} must remain manualOnly=true until official SHA-256 evidence is verified.");
        Assert.False(item.TryGetProperty("sha256", out _), $"{itemName} must not carry guessed checksum metadata.");
        Assert.False(item.TryGetProperty("sha256Url", out _), $"{itemName} must not carry guessed checksum metadata.");
        Assert.False(item.TryGetProperty("sha512", out _), $"{itemName} must not carry guessed checksum metadata.");
        Assert.False(item.TryGetProperty("sha512Url", out _), $"{itemName} must not carry guessed checksum metadata.");
        Assert.False(item.TryGetProperty("sha512", out _), $"{itemName} must not carry guessed checksum metadata.");
        Assert.False(item.TryGetProperty("sha512Url", out _), $"{itemName} must not carry guessed checksum metadata.");
        Assert.Contains("Manual", GetString(item, "notes"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManagedDownloadManifest_Batch2PromotedEntriesHaveValidChecksumAndMetadata()
    {
        var promoted = new[]
        {
            "Notepad++ 8.9.6 Portable (x64)",
            "System Informer 3.2.25011 Portable",
            "VeraCrypt 1.26.24 Setup (x64)",
            "PuTTY 0.83 64-bit Installer"
        };

        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        foreach (var name in promoted)
        {
            var item = document.RootElement.GetProperty("items")
                .EnumerateArray()
                .SingleOrDefault(e => string.Equals(GetString(e, "name"), name, StringComparison.Ordinal));

            Assert.NotEqual(default, item.ValueKind);
            Assert.Equal("file", GetString(item, "type"));
            Assert.True(item.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean(), $"{name} must be enabled.");

            var sha256 = GetString(item, "sha256");
            Assert.Equal(64, sha256.Length);
            Assert.All(sha256, c => Assert.True(Uri.IsHexDigit(c), $"{name}: sha256 must be hex."));
            Assert.False(string.IsNullOrWhiteSpace(GetString(item, "sourceType")), $"{name}: sourceType is required.");
            Assert.False(string.IsNullOrWhiteSpace(GetString(item, "fragilityLevel")), $"{name}: fragilityLevel is required.");
            Assert.False(string.IsNullOrWhiteSpace(GetString(item, "fallbackRule")), $"{name}: fallbackRule is required.");
            Assert.False(string.IsNullOrWhiteSpace(GetString(item, "licenseNote")), $"{name}: licenseNote is required.");
            Assert.False(string.IsNullOrWhiteSpace(GetString(item, "recommendedUse")), $"{name}: recommendedUse is required.");
            Assert.False(string.IsNullOrWhiteSpace(GetString(item, "technicianNotes")), $"{name}: technicianNotes is required.");
            Assert.False(string.IsNullOrWhiteSpace(GetString(item, "architecture")), $"{name}: architecture is required.");
            Assert.Equal("official", GetString(item, "sourceTrust"));
            Assert.True(item.TryGetProperty("maintenanceRank", out var rank) && rank.ValueKind == JsonValueKind.Number,
                $"{name}: maintenanceRank is required.");
        }
    }

    [Theory]
    [InlineData("7-Zip Download Page", "Vendor publishes no machine-readable SHA-256 checksum file on the download page; promotion would require fabricating or scraping hashes.")]
    [InlineData("WinSCP Download Page", "Vendor publishes per-release SHA-256 only inside the prose ReadMe (\"SHA-256: <hash>\" lines); not a standard GNU/BSD/digest format the resolver can safely consume.")]
    [InlineData("Nmap Download Page", "Vendor digest files use a non-standard byte-grouped multi-line layout (`name: SHA256 = HH HH HH HH ...`) plus the installer bundles Npcap under a separate EULA.")]
    [InlineData("Advanced IP Scanner Download Page", "Vendor portal selects build/region; no stable versioned URL or machine-readable checksum file.")]
    [InlineData("Everything Search Download Page", "Vendor portal selects per-architecture installer; no machine-readable checksum file at a stable URL.")]
    [InlineData("GPU-Z Download Page", "TechPowerUp vendor portal with mirror/CDN selection; no machine-readable checksum file.")]
    [InlineData("DDU Download Page", "Guru3D vendor portal with rotating mirror selection; no machine-readable checksum file.")]
    [InlineData("NVCleanInstall Download Page", "TechPowerUp vendor portal with mirror selection; no machine-readable checksum file.")]
    public void ManagedDownloadManifest_Batch3UnsafeCandidatesStayManualOnly(string itemName, string reasonDocumented)
    {
        // The reason string is asserted non-empty so that future edits to the [InlineData]
        // rows cannot accidentally drop the documented "why this stayed manual" justification.
        Assert.False(string.IsNullOrWhiteSpace(reasonDocumented), $"{itemName}: documented reason is required.");

        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var item = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .SingleOrDefault(e => string.Equals(GetString(e, "name"), itemName, StringComparison.Ordinal));

        Assert.NotEqual(default, item.ValueKind);
        Assert.Equal("page", GetString(item, "type"));
        Assert.False(item.TryGetProperty("sha256", out _), $"{itemName} must not carry guessed checksum metadata.");
        Assert.False(item.TryGetProperty("sha256Url", out _), $"{itemName} must not carry guessed checksum metadata.");
        Assert.False(item.TryGetProperty("sha512", out _), $"{itemName} must not carry guessed checksum metadata.");
        Assert.False(item.TryGetProperty("sha512Url", out _), $"{itemName} must not carry guessed checksum metadata.");
        Assert.False(item.TryGetProperty("sha512", out _), $"{itemName} must not carry guessed checksum metadata.");
        Assert.False(item.TryGetProperty("sha512Url", out _), $"{itemName} must not carry guessed checksum metadata.");
    }

    [Fact]
    public void ManagedDownloadManifest_Batch3PromotedEntriesHaveValidChecksumAndMetadata()
    {
        var promoted = new[]
        {
            "Wireshark 4.6.6 Win64 Installer"
        };

        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        foreach (var name in promoted)
        {
            var item = document.RootElement.GetProperty("items")
                .EnumerateArray()
                .SingleOrDefault(e => string.Equals(GetString(e, "name"), name, StringComparison.Ordinal));

            Assert.NotEqual(default, item.ValueKind);
            Assert.Equal("file", GetString(item, "type"));
            Assert.True(item.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean(), $"{name} must be enabled.");

            var sha256 = GetString(item, "sha256");
            Assert.Equal(64, sha256.Length);
            Assert.All(sha256, c => Assert.True(Uri.IsHexDigit(c), $"{name}: sha256 must be hex."));

            var sha256Url = GetString(item, "sha256Url");
            Assert.StartsWith("https://", sha256Url, StringComparison.Ordinal);

            Assert.False(string.IsNullOrWhiteSpace(GetString(item, "sourceType")), $"{name}: sourceType is required.");
            Assert.False(string.IsNullOrWhiteSpace(GetString(item, "fragilityLevel")), $"{name}: fragilityLevel is required.");
            Assert.False(string.IsNullOrWhiteSpace(GetString(item, "fallbackRule")), $"{name}: fallbackRule is required.");
            Assert.False(string.IsNullOrWhiteSpace(GetString(item, "licenseNote")), $"{name}: licenseNote is required.");
            Assert.False(string.IsNullOrWhiteSpace(GetString(item, "recommendedUse")), $"{name}: recommendedUse is required.");
            Assert.False(string.IsNullOrWhiteSpace(GetString(item, "technicianNotes")), $"{name}: technicianNotes is required.");
            Assert.False(string.IsNullOrWhiteSpace(GetString(item, "architecture")), $"{name}: architecture is required.");
            Assert.Equal("official", GetString(item, "sourceTrust"));
            Assert.True(item.TryGetProperty("maintenanceRank", out var rank) && rank.ValueKind == JsonValueKind.Number,
                $"{name}: maintenanceRank is required.");
        }
    }

    [Fact]
    public void ManagedDownloadManifest_Batch3PromotedSourceUrlsArePinned()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var expectations = new Dictionary<string, (string urlFragment, string checksumFragment)>
        {
            ["Wireshark 4.6.6 Win64 Installer"] = ("/win64/Wireshark-4.6.6-x64.exe", "/SIGNATURES-4.6.6.txt")
        };

        foreach (var (name, (urlFragment, checksumFragment)) in expectations)
        {
            var item = document.RootElement.GetProperty("items")
                .EnumerateArray()
                .SingleOrDefault(e => string.Equals(GetString(e, "name"), name, StringComparison.Ordinal));
            var url = GetString(item, "url");
            var sha256Url = GetString(item, "sha256Url");

            Assert.StartsWith("https://", url, StringComparison.Ordinal);
            Assert.Contains(urlFragment, url, StringComparison.Ordinal);
            Assert.DoesNotContain("/latest", url, StringComparison.OrdinalIgnoreCase);

            Assert.StartsWith("https://", sha256Url, StringComparison.Ordinal);
            Assert.Contains(checksumFragment, sha256Url, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ManagedDownloadManifest_Batch2PromotedSourceUrlsArePinned()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var expectations = new Dictionary<string, string>
        {
            ["Notepad++ 8.9.6 Portable (x64)"] = "/releases/download/v8.9.6/",
            ["System Informer 3.2.25011 Portable"] = "/releases/download/v3.2.25011.2103/",
            ["VeraCrypt 1.26.24 Setup (x64)"] = "/releases/download/VeraCrypt_1.26.24/",
            ["PuTTY 0.83 64-bit Installer"] = "/putty/0.83/"
        };

        foreach (var (name, expectedFragment) in expectations)
        {
            var item = document.RootElement.GetProperty("items")
                .EnumerateArray()
                .SingleOrDefault(e => string.Equals(GetString(e, "name"), name, StringComparison.Ordinal));
            var url = GetString(item, "url");

            Assert.StartsWith("https://", url, StringComparison.Ordinal);
            Assert.Contains(expectedFragment, url, StringComparison.Ordinal);
            Assert.DoesNotContain("/latest", url, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("releases/latest", url, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ManagedDownloadManifest_Batch4PromotedIsoEntriesHaveValidChecksumAndMetadata()
    {
        var promoted = new[]
        {
            "Proxmox VE 9.2-1 ISO Installer",
            "Ubuntu Server 24.04.4 LTS (amd64)",
            "Debian GNU/Linux 13.5.0 netinst (amd64)",
            "Fedora Server 44-1.7 DVD (x86_64)",
            "FreeBSD 15.0-RELEASE amd64 disc1 ISO",
            "OpenBSD 7.9 amd64 install ISO",
            "Rocky Linux 10.1 Minimal (x86_64)",
            "AlmaLinux 10.1 Minimal (x86_64)"
        };

        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        foreach (var name in promoted)
        {
            var item = document.RootElement.GetProperty("items")
                .EnumerateArray()
                .SingleOrDefault(e => string.Equals(GetString(e, "name"), name, StringComparison.Ordinal));

            Assert.NotEqual(default, item.ValueKind);
            Assert.Equal("file", GetString(item, "type"));
            Assert.True(item.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean(), $"{name} must be enabled.");

            var sha256 = GetString(item, "sha256");
            Assert.Equal(64, sha256.Length);
            Assert.All(sha256, c => Assert.True(Uri.IsHexDigit(c), $"{name}: sha256 must be hex."));
            Assert.Equal("official", GetString(item, "sourceTrust"));
            Assert.False(string.IsNullOrWhiteSpace(GetString(item, "fallbackRule")), $"{name}: fallbackRule is required.");
            Assert.False(string.IsNullOrWhiteSpace(GetString(item, "technicianNotes")), $"{name}: technicianNotes is required.");
            Assert.False(string.IsNullOrWhiteSpace(GetString(item, "secureBootNote")), $"{name}: secureBootNote is required.");
            Assert.False(string.IsNullOrWhiteSpace(GetString(item, "ventoyNotes")), $"{name}: ventoyNotes is required.");
            Assert.Equal("UpToDate", GetString(item.GetProperty("freshness"), "freshnessStatus"));
            Assert.Equal("stable", GetString(item.GetProperty("freshness"), "updateChannel"));
        }
    }

    [Fact]
    public void ManagedDownloadManifest_Batch4PromotedSourceUrlsArePinnedAndStableOnly()
    {
        var promoted = new[]
        {
            "Proxmox VE 9.2-1 ISO Installer",
            "Ubuntu Server 24.04.4 LTS (amd64)",
            "Debian GNU/Linux 13.5.0 netinst (amd64)",
            "Fedora Server 44-1.7 DVD (x86_64)",
            "FreeBSD 15.0-RELEASE amd64 disc1 ISO",
            "OpenBSD 7.9 amd64 install ISO",
            "Rocky Linux 10.1 Minimal (x86_64)",
            "AlmaLinux 10.1 Minimal (x86_64)"
        };

        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        foreach (var name in promoted)
        {
            var item = document.RootElement.GetProperty("items")
                .EnumerateArray()
                .SingleOrDefault(e => string.Equals(GetString(e, "name"), name, StringComparison.Ordinal));

            var url = GetString(item, "url");
            Assert.StartsWith("https://", url, StringComparison.Ordinal);
            foreach (var forbidden in new[] { "/latest", "nightly", "beta", "rc", "snapshot", "development" })
            {
                Assert.DoesNotContain(forbidden, url, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(forbidden, GetString(item, "name"), StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Theory]
    [InlineData("Haiku Download Page")]
    [InlineData("Smartmontools Download Page")]
    [InlineData("KeePass Download Page")]
    [InlineData("NetBSD Download Page")]
    [InlineData("openSUSE Download Page")]
    [InlineData("Gentoo Linux Download Page")]
    [InlineData("Slackware Download Page")]
    public void ManagedDownloadManifest_UnsafePromotionCandidatesCarryManualFallbackGuidance(string itemName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var item = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .SingleOrDefault(e => string.Equals(GetString(e, "name"), itemName, StringComparison.Ordinal));

        Assert.NotEqual(default, item.ValueKind);
        Assert.Equal("page", GetString(item, "type"));
        Assert.True(item.TryGetProperty("manualOnly", out var manualOnly) && manualOnly.GetBoolean(),
            $"{itemName} must remain manualOnly=true.");
        Assert.Equal("official", GetString(item, "sourceTrust"));
        Assert.False(string.IsNullOrWhiteSpace(GetString(item, "recommendedUse")), $"{itemName}: recommendedUse is required.");
        Assert.False(string.IsNullOrWhiteSpace(GetString(item, "technicianNotes")), $"{itemName}: technicianNotes is required.");
        Assert.False(string.IsNullOrWhiteSpace(GetString(item, "licenseNote")), $"{itemName}: licenseNote is required.");
        Assert.False(string.IsNullOrWhiteSpace(GetString(item, "secureBootNote")), $"{itemName}: secureBootNote is required.");
        Assert.False(string.IsNullOrWhiteSpace(GetString(item, "ventoyNotes")), $"{itemName}: ventoyNotes is required.");
        Assert.False(string.IsNullOrWhiteSpace(GetString(item, "fallbackRule")), $"{itemName}: fallbackRule is required.");
        Assert.False(item.TryGetProperty("sha256", out _), $"{itemName} must not carry guessed checksum metadata.");
        Assert.False(item.TryGetProperty("sha256Url", out _), $"{itemName} must not carry guessed checksum metadata.");
    }

    [Theory]
    [InlineData("CrystalDiskInfo Download Page")]
    [InlineData("GPU-Z Download Page")]
    [InlineData("TestDisk and PhotoRec Download Page")]
    [InlineData("WinSCP Download Page")]
    [InlineData("Advanced IP Scanner Download Page")]
    [InlineData("7-Zip Download Page")]
    [InlineData("Nmap Download Page")]
    [InlineData("Everything Search Download Page")]
    [InlineData("DDU Download Page")]
    [InlineData("NVCleanInstall Download Page")]
    [InlineData("ReactOS Download Page")]
    public void ManagedDownloadManifest_RecheckedUnsafeCandidatesStayManualWithTechnicianGuidance(string itemName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var item = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .SingleOrDefault(e => string.Equals(GetString(e, "name"), itemName, StringComparison.Ordinal));

        Assert.NotEqual(default, item.ValueKind);
        Assert.Equal("page", GetString(item, "type"));
        Assert.True(item.TryGetProperty("manualOnly", out var manualOnly) && manualOnly.GetBoolean(),
            $"{itemName} must remain manualOnly=true.");
        Assert.False(string.IsNullOrWhiteSpace(GetString(item, "sourceTrust")), $"{itemName}: sourceTrust is required.");
        Assert.False(string.IsNullOrWhiteSpace(GetString(item, "recommendedUse")), $"{itemName}: recommendedUse is required.");
        Assert.False(string.IsNullOrWhiteSpace(GetString(item, "technicianNotes")), $"{itemName}: technicianNotes is required.");
        Assert.False(string.IsNullOrWhiteSpace(GetString(item, "licenseNote")), $"{itemName}: licenseNote is required.");
        Assert.False(string.IsNullOrWhiteSpace(GetString(item, "secureBootNote")), $"{itemName}: secureBootNote is required.");
        Assert.False(string.IsNullOrWhiteSpace(GetString(item, "ventoyNotes")), $"{itemName}: ventoyNotes is required.");
        Assert.False(string.IsNullOrWhiteSpace(GetString(item, "fallbackRule")), $"{itemName}: fallbackRule is required.");
        Assert.False(item.TryGetProperty("sha256", out _), $"{itemName} must not carry guessed checksum metadata.");
        Assert.False(item.TryGetProperty("sha256Url", out _), $"{itemName} must not carry guessed checksum metadata.");
    }

    [Fact]
    public void ManagedDownloadManifest_Batch5PromotedIsoEntriesHaveValidChecksumAndMetadata()
    {
        // 2026-05-25 follow-up promotion pass: NetBSD 10.1 amd64 and openSUSE Leap 16.0 x86_64 offline installer.
        // NetBSD uses SHA-512 coverage (vendor publishes only SHA512); openSUSE uses SHA-256 coverage
        // (vendor publishes a per-file .iso.sha256 companion).
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));

        var netbsd = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .SingleOrDefault(e => string.Equals(GetString(e, "name"), "NetBSD 10.1 amd64 ISO Installer", StringComparison.Ordinal));
        Assert.NotEqual(default, netbsd.ValueKind);
        Assert.Equal("file", GetString(netbsd, "type"));
        Assert.True(netbsd.TryGetProperty("enabled", out var nbEnabled) && nbEnabled.GetBoolean(), "NetBSD entry must be enabled.");

        var sha512 = GetString(netbsd, "sha512");
        Assert.Equal(128, sha512.Length);
        Assert.All(sha512, c => Assert.True(Uri.IsHexDigit(c), "NetBSD sha512 must be hex."));
        var sha512Url = GetString(netbsd, "sha512Url");
        Assert.StartsWith("https://cdn.netbsd.org/", sha512Url, StringComparison.Ordinal);
        Assert.False(netbsd.TryGetProperty("sha256", out _), "NetBSD entry must not carry sha256 alongside sha512.");
        Assert.False(netbsd.TryGetProperty("sha256Url", out _), "NetBSD entry must not carry sha256Url alongside sha512Url.");
        Assert.Equal("official", GetString(netbsd, "sourceTrust"));
        Assert.False(string.IsNullOrWhiteSpace(GetString(netbsd, "fallbackRule")), "NetBSD entry: fallbackRule is required.");
        Assert.False(string.IsNullOrWhiteSpace(GetString(netbsd, "technicianNotes")), "NetBSD entry: technicianNotes is required.");
        Assert.False(string.IsNullOrWhiteSpace(GetString(netbsd, "secureBootNote")), "NetBSD entry: secureBootNote is required.");
        Assert.False(string.IsNullOrWhiteSpace(GetString(netbsd, "ventoyNotes")), "NetBSD entry: ventoyNotes is required.");
        Assert.Equal("UpToDate", GetString(netbsd.GetProperty("freshness"), "freshnessStatus"));
        Assert.Equal("sha512-pinned", GetString(netbsd.GetProperty("freshness"), "checksumVerificationMode"));

        var opensuse = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .SingleOrDefault(e => string.Equals(GetString(e, "name"), "openSUSE Leap 16.0 Offline Installer (x86_64)", StringComparison.Ordinal));
        Assert.NotEqual(default, opensuse.ValueKind);
        Assert.Equal("file", GetString(opensuse, "type"));
        Assert.True(opensuse.TryGetProperty("enabled", out var osEnabled) && osEnabled.GetBoolean(), "openSUSE entry must be enabled.");

        var sha256 = GetString(opensuse, "sha256");
        Assert.Equal(64, sha256.Length);
        Assert.All(sha256, c => Assert.True(Uri.IsHexDigit(c), "openSUSE sha256 must be hex."));
        var sha256Url = GetString(opensuse, "sha256Url");
        Assert.StartsWith("https://download.opensuse.org/", sha256Url, StringComparison.Ordinal);
        Assert.EndsWith(".iso.sha256", sha256Url, StringComparison.Ordinal);
        Assert.Equal("official", GetString(opensuse, "sourceTrust"));
        Assert.False(string.IsNullOrWhiteSpace(GetString(opensuse, "fallbackRule")), "openSUSE entry: fallbackRule is required.");
        Assert.False(string.IsNullOrWhiteSpace(GetString(opensuse, "technicianNotes")), "openSUSE entry: technicianNotes is required.");
        Assert.False(string.IsNullOrWhiteSpace(GetString(opensuse, "secureBootNote")), "openSUSE entry: secureBootNote is required.");
        Assert.False(string.IsNullOrWhiteSpace(GetString(opensuse, "ventoyNotes")), "openSUSE entry: ventoyNotes is required.");
        Assert.Equal("UpToDate", GetString(opensuse.GetProperty("freshness"), "freshnessStatus"));
        Assert.Equal("sha256-pinned", GetString(opensuse.GetProperty("freshness"), "checksumVerificationMode"));
    }

    [Fact]
    public void ManagedDownloadManifest_Batch5PromotedSourceUrlsArePinnedAndStableOnly()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var promoted = new[]
        {
            "NetBSD 10.1 amd64 ISO Installer",
            "openSUSE Leap 16.0 Offline Installer (x86_64)"
        };

        foreach (var name in promoted)
        {
            var item = document.RootElement.GetProperty("items")
                .EnumerateArray()
                .SingleOrDefault(e => string.Equals(GetString(e, "name"), name, StringComparison.Ordinal));

            var url = GetString(item, "url");
            Assert.StartsWith("https://", url, StringComparison.Ordinal);
            foreach (var forbidden in new[] { "/latest", "nightly", "beta", "rc", "snapshot", "development", "tumbleweed" })
            {
                Assert.DoesNotContain(forbidden, url, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void ManagedDownloadManifest_HasNoDuplicateNames()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var duplicates = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item => GetString(item, "name"))
            .GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void ManagedDownloadManifest_HasNoDuplicateDestinations()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var duplicates = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item => GetString(item, "dest"))
            .GroupBy(dest => dest, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void ManagedDownloadManifest_PageItemsDoNotCarryFileOnlyMetadata()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var violations = new List<string>();
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            var type = GetString(item, "type");
            if (!string.Equals(type, "page", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = GetString(item, "name");
            foreach (var fileOnlyField in new[] { "sha256Url", "sha512Url", "sourceType", "fragilityLevel", "maintenanceRank", "borderline" })
            {
                if (item.TryGetProperty(fileOnlyField, out _))
                {
                    violations.Add($"{name}.{fileOnlyField}");
                }
            }
        }

        Assert.Empty(violations);
    }

    [Theory]
    [InlineData("Windows 8.1 Lifecycle Info")]
    [InlineData("Windows 8 Lifecycle Info")]
    [InlineData("Windows 7 Lifecycle Info")]
    [InlineData("Windows Vista Lifecycle Info")]
    [InlineData("Windows XP Lifecycle Info")]
    [InlineData("Windows 2000 Lifecycle Info")]
    [InlineData("Windows ME Reference Info")]
    [InlineData("Windows 98 Reference Info")]
    [InlineData("Windows 95 Reference Info")]
    public void ManagedDownloadManifest_LegacyWindowsEntriesAreManualOnlyWithLegacyWarning(string itemName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var item = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .SingleOrDefault(e => string.Equals(GetString(e, "name"), itemName, StringComparison.Ordinal));

        Assert.NotEqual(default, item.ValueKind);
        Assert.Equal("page", GetString(item, "type"));
        Assert.Equal("os", GetString(item, "kind"));
        Assert.Equal("Windows", GetString(item, "family"));
        Assert.True(item.TryGetProperty("manualOnly", out var manualOnly) && manualOnly.GetBoolean(),
            $"{itemName} must be manualOnly=true.");
        Assert.False(string.IsNullOrWhiteSpace(GetString(item, "legacyWarning")),
            $"{itemName} must carry a legacyWarning.");
        Assert.Contains("manual iso required", GetString(item, "notes"), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Pop!_OS Download Page", "Linux", "Desktop")]
    [InlineData("Zorin OS Download Page", "Linux", "Desktop")]
    [InlineData("Tails Download Page", "Linux", "Security")]
    [InlineData("Qubes OS Download Page", "Linux", "Security")]
    [InlineData("Proxmox VE Download Page", "Linux", "Hypervisor")]
    [InlineData("TrueNAS SCALE Download Page", "Linux", "Server")]
    [InlineData("pfSense Community Edition Download Page", "BSD", "Network-Appliance")]
    [InlineData("OPNsense Download Page", "BSD", "Network-Appliance")]
    [InlineData("ReactOS Download Page", "Hobby", "Hobby")]
    [InlineData("Haiku Download Page", "Hobby", "Hobby")]
    [InlineData("FreeBSD Download Page", "BSD", "Server")]
    [InlineData("OpenBSD Download Page", "BSD", "Server")]
    [InlineData("NetBSD Download Page", "BSD", "Server")]
    public void ManagedDownloadManifest_NewOsEntriesCarryFamilyAndCategoryMetadata(string itemName, string expectedFamily, string expectedCategory)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var item = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .SingleOrDefault(e => string.Equals(GetString(e, "name"), itemName, StringComparison.Ordinal));

        Assert.NotEqual(default, item.ValueKind);
        Assert.Equal("os", GetString(item, "kind"));
        Assert.Equal(expectedFamily, GetString(item, "family"));
        Assert.Equal(expectedCategory, GetString(item, "osCategory"));
        Assert.False(string.IsNullOrWhiteSpace(GetString(item, "licenseNote")),
            $"{itemName} must declare a licenseNote.");
        Assert.False(string.IsNullOrWhiteSpace(GetString(item, "sourceTrust")),
            $"{itemName} must declare a sourceTrust.");
    }

    [Fact]
    public void ManagedDownloadManifest_OsEntriesUseValidArchitectureValues()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var bad = new List<string>();
        var validTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "amd64", "x86", "arm64", "armhf", "i386", "ppc64", "ppc64le", "s390x", "riscv", "powerpc", "many"
        };

        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            if (!string.Equals(GetString(item, "kind"), "os", StringComparison.Ordinal))
            {
                continue;
            }

            if (!item.TryGetProperty("architecture", out var arch))
            {
                continue;
            }

            var name = GetString(item, "name");
            if (arch.ValueKind == JsonValueKind.String)
            {
                if (!validTokens.Contains(arch.GetString() ?? string.Empty))
                {
                    bad.Add($"{name}: architecture token '{arch.GetString()}' is not in the known set.");
                }
            }
            else if (arch.ValueKind == JsonValueKind.Array)
            {
                foreach (var token in arch.EnumerateArray())
                {
                    if (token.ValueKind != JsonValueKind.String || !validTokens.Contains(token.GetString() ?? string.Empty))
                    {
                        bad.Add($"{name}: architecture token '{token.GetString()}' is not in the known set.");
                    }
                }
            }
            else
            {
                bad.Add($"{name}: architecture must be a string or an array of strings.");
            }
        }

        Assert.Empty(bad);
    }

    [Fact]
    public void ManagedDownloadManifest_OsEntriesUseValidBootModeValues()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var bad = new List<string>();
        var validTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bios", "uefi", "secure-boot", "secure-boot-not-supported", "uefi-csm", "legacy-only"
        };

        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            if (!string.Equals(GetString(item, "kind"), "os", StringComparison.Ordinal))
            {
                continue;
            }

            if (!item.TryGetProperty("bootMode", out var bootMode))
            {
                continue;
            }

            var name = GetString(item, "name");
            if (bootMode.ValueKind == JsonValueKind.String)
            {
                if (!validTokens.Contains(bootMode.GetString() ?? string.Empty))
                {
                    bad.Add($"{name}: bootMode token '{bootMode.GetString()}' is not in the known set.");
                }
            }
            else if (bootMode.ValueKind == JsonValueKind.Array)
            {
                foreach (var token in bootMode.EnumerateArray())
                {
                    if (token.ValueKind != JsonValueKind.String || !validTokens.Contains(token.GetString() ?? string.Empty))
                    {
                        bad.Add($"{name}: bootMode token '{token.GetString()}' is not in the known set.");
                    }
                }
            }
            else
            {
                bad.Add($"{name}: bootMode must be a string or an array of strings.");
            }
        }

        Assert.Empty(bad);
    }

    [Fact]
    public void ManagedDownloadManifest_HasOsCatalogCoverageAcrossExpectedFamilies()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var familiesPresent = new HashSet<string>(
            document.RootElement.GetProperty("items")
                .EnumerateArray()
                .Where(i => string.Equals(GetString(i, "kind"), "os", StringComparison.Ordinal))
                .Select(i => GetString(i, "family")),
            StringComparer.Ordinal);

        foreach (var expectedFamily in new[] { "Windows", "Linux", "BSD", "Hobby", "DOS", "Other-Unix" })
        {
            Assert.Contains(expectedFamily, familiesPresent);
        }
    }

    [Fact]
    public void ManagedDownloadManifest_NoFileEntryDeclaresPaidLicence()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var violations = new List<string>();
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            var type = GetString(item, "type");
            if (!string.Equals(type, "file", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var licenseNote = GetString(item, "licenseNote");
            if (licenseNote.Contains("Paid", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add($"{GetString(item, "name")}: file-type entry must not declare a paid licence.");
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void ManagedDownloadManifest_SourceTrustValuesAreInValidSet()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var validTrust = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "official", "community", "manual" };
        var bad = new List<string>();
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            if (!item.TryGetProperty("sourceTrust", out var trustElement))
            {
                continue;
            }

            var trust = trustElement.ValueKind == JsonValueKind.String ? (trustElement.GetString() ?? string.Empty) : string.Empty;
            if (!validTrust.Contains(trust))
            {
                bad.Add($"{GetString(item, "name")}: sourceTrust '{trust}' is not in the valid set.");
            }
        }

        Assert.Empty(bad);
    }

    [Fact]
    public void ManagedDownloadManifest_ManagedChecksumPolicyUnchanged()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        Assert.True(document.RootElement.TryGetProperty("managedChecksumPolicy", out var policy));
        Assert.Equal("require-for-release", policy.GetString());
    }

    [Fact]
    public void ManagedDownloadManifest_FileItemsUseHttpsOnly()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            var type = GetString(item, "type");
            if (!string.Equals(type, "file", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var url = GetString(item, "url");
            Assert.StartsWith("https://", url, StringComparison.Ordinal);

            var sha256Url = GetString(item, "sha256Url");
            if (!string.IsNullOrWhiteSpace(sha256Url))
            {
                // Allow http(s) URLs only. (Local file paths are valid in test fixtures but should never appear in the real manifest.)
                Assert.True(sha256Url.StartsWith("https://", StringComparison.Ordinal) ||
                            sha256Url.StartsWith("http://", StringComparison.Ordinal),
                    $"{GetString(item, "name")}: sha256Url must be an HTTP(S) URL.");
            }

            var sha512Url = GetString(item, "sha512Url");
            if (!string.IsNullOrWhiteSpace(sha512Url))
            {
                Assert.True(sha512Url.StartsWith("https://", StringComparison.Ordinal) ||
                            sha512Url.StartsWith("http://", StringComparison.Ordinal),
                    $"{GetString(item, "name")}: sha512Url must be an HTTP(S) URL.");
            }
        }
    }

    [Fact]
    public void ManagedDownloadManifest_FileItemsHaveContiguousMaintenanceRanks()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var fileItems = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .Where(i => string.Equals(GetString(i, "type"), "file", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var ranks = fileItems
            .Where(i => i.TryGetProperty("maintenanceRank", out var r) && r.ValueKind == JsonValueKind.Number)
            .Select(i => i.GetProperty("maintenanceRank").GetInt32())
            .OrderBy(r => r)
            .ToArray();

        Assert.Equal(fileItems.Length, ranks.Length);
        for (int i = 0; i < ranks.Length; i++)
        {
            Assert.Equal(i + 1, ranks[i]);
        }
    }

    [Fact]
    public void ManagedDownloadManifest_DisabledPromotionScaffold_StillRequiresCompleteResilienceMetadata()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            if (!string.Equals(GetString(item, "type"), "file", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = GetString(item, "name");
            // Every file entry, enabled or disabled, must carry the full resilience contract so a
            // future operator can promote it to enabled:true without having to re-derive metadata.
            Assert.True(item.TryGetProperty("sourceType", out var sourceType) && sourceType.ValueKind == JsonValueKind.String,
                $"{name}: sourceType is required on file entries (even disabled scaffolds).");
            Assert.True(item.TryGetProperty("fragilityLevel", out var fragility) && fragility.ValueKind == JsonValueKind.String,
                $"{name}: fragilityLevel is required on file entries.");
            Assert.True(item.TryGetProperty("fallbackRule", out var fallback) && fallback.ValueKind == JsonValueKind.String,
                $"{name}: fallbackRule is required on file entries.");
            Assert.True(item.TryGetProperty("maintenanceRank", out var rank) && rank.ValueKind == JsonValueKind.Number,
                $"{name}: maintenanceRank is required on file entries.");
        }
    }

    [Fact]
    public void ManagedDownloadManifest_FileEntriesCarryCatalogMetadataBackfill()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var missing = new List<string>();
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            if (!string.Equals(GetString(item, "type"), "file", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = GetString(item, "name");
            // Backfilled metadata so every managed entry participates in chip-tag routing.
            if (string.IsNullOrWhiteSpace(GetString(item, "kind"))) { missing.Add($"{name}: kind"); }
            if (string.IsNullOrWhiteSpace(GetString(item, "family"))) { missing.Add($"{name}: family"); }
            if (string.IsNullOrWhiteSpace(GetString(item, "licenseNote"))) { missing.Add($"{name}: licenseNote"); }
            if (string.IsNullOrWhiteSpace(GetString(item, "sourceTrust"))) { missing.Add($"{name}: sourceTrust"); }
            if (string.IsNullOrWhiteSpace(GetString(item, "recommendedUse"))) { missing.Add($"{name}: recommendedUse"); }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void ManagedDownloadManifest_GitHubReleaseEntries_UseAssetDigestApi()
    {
        // GitHub-release entries currently rely on per-asset digest endpoints. This test does
        // not assert any particular asset ID (those rotate); it only asserts that github-release
        // file entries that use a sha256Url do route through the documented per-asset digest API
        // shape. If a future entry switches to a release-asset SHA256SUMS file pattern (the
        // safer long-term shape unlocked by the filename-aware resolver), update this test in
        // step with that promotion.
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            if (!string.Equals(GetString(item, "sourceType"), "github-release", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = GetString(item, "name");
            var sha256Url = GetString(item, "sha256Url");
            if (string.IsNullOrWhiteSpace(sha256Url)) { continue; }

            Assert.True(
                sha256Url.StartsWith("https://api.github.com/repos/", StringComparison.Ordinal) ||
                sha256Url.StartsWith("https://github.com/", StringComparison.Ordinal),
                $"{name}: github-release sha256Url should use the api.github.com asset-digest endpoint or a github.com release-asset URL.");
        }
    }

    [Theory]
    [InlineData("Parted Magic Download Page")]
    [InlineData("AIDA64 Extreme Download Page")]
    [InlineData("Macrium Reflect Home Info")]
    public void ManagedDownloadManifest_KnownPaidEntriesStayManualOnly(string itemName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var item = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .SingleOrDefault(e => string.Equals(GetString(e, "name"), itemName, StringComparison.Ordinal));

        Assert.NotEqual(default, item.ValueKind);
        Assert.Equal("page", GetString(item, "type"));
        Assert.Contains("Paid", GetString(item, "licenseNote"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManagedDownloadManifest_OsEntriesWithLegacyWarningAreManualOnly()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var violations = new List<string>();
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            if (string.IsNullOrWhiteSpace(GetString(item, "legacyWarning")))
            {
                continue;
            }

            var name = GetString(item, "name");
            if (!string.Equals(GetString(item, "type"), "page", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add($"{name}: items with legacyWarning must be page-type (manual only).");
            }
        }

        Assert.Empty(violations);
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
