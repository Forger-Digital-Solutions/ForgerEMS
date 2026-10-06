using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
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
            // requiresResolution entries carry no static checksum: the expected SHA-256 is bound at
            // runtime by the resolved overlay, which the backend gate verifies before any download.
            .Where(item => !(item.TryGetProperty("requiresResolution", out var rr) && rr.ValueKind == JsonValueKind.True))
            .Where(item => string.IsNullOrWhiteSpace(GetString(item, "sha256")) &&
                           string.IsNullOrWhiteSpace(GetString(item, "sha256Url")) &&
                           string.IsNullOrWhiteSpace(GetString(item, "sha512")) &&
                           string.IsNullOrWhiteSpace(GetString(item, "sha512Url")))
            .Select(item => GetString(item, "name"))
            .ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void ManagedDownloadManifest_ResolvedResourcesHavePolicyDescriptors()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        using var policy = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/resource-policy.json")));
        var policyProviders = policy.RootElement.GetProperty("resources")
            .EnumerateArray()
            .ToDictionary(
                r => r.GetProperty("id").GetString() ?? string.Empty,
                r => r.GetProperty("provider").GetString() ?? string.Empty,
                StringComparer.Ordinal);

        var violations = new List<string>();
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            if (!item.TryGetProperty("requiresResolution", out var rr) || rr.ValueKind != JsonValueKind.True)
            {
                continue;
            }

            var name = GetString(item, "name");
            if (!string.Equals(GetString(item, "type"), "file", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add($"{name}: requiresResolution must be a file item.");
            }

            var resourceId = GetString(item, "resourceId");
            if (string.IsNullOrWhiteSpace(resourceId) || !policyProviders.TryGetValue(resourceId, out var provider))
            {
                violations.Add($"{name}: resourceId '{resourceId}' has no resource-policy descriptor.");
                continue;
            }

            var strategy = GetString(item, "resolveStrategy");
            if (!string.Equals(strategy, provider, StringComparison.Ordinal))
            {
                violations.Add($"{name}: resolveStrategy '{strategy}' does not match policy provider '{provider}'.");
            }

            if (!GetString(item, "url").StartsWith("https://", StringComparison.Ordinal))
            {
                violations.Add($"{name}: resolved resource must keep an https canonical URL.");
            }

            foreach (var checksumField in new[] { "sha256", "sha256Url", "sha512", "sha512Url" })
            {
                if (item.TryGetProperty(checksumField, out _))
                {
                    violations.Add($"{name}: must not pin {checksumField}; checksum arrives via the resolved overlay.");
                }
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void ManagedDownloadManifestSchema_AcceptsDownloadModeEnum()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.schema.json")));
        var enumValues = document.RootElement
            .GetProperty("properties")
            .GetProperty("items")
            .GetProperty("items")
            .GetProperty("properties")
            .GetProperty("downloadMode")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(e => e.GetString() ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var expected in ManifestPromotionPolicy.ValidDownloadModes)
        {
            Assert.Contains(expected, enumValues);
        }
    }

    [Fact]
    public void ManagedDownloadManifest_EveryItemHasValidExplicitOrInferredDownloadMode()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var invalid = new List<string>();

        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            var name = GetString(item, "name");
            var inferred = ManifestPromotionPolicy.InferDownloadMode(
                GetString(item, "downloadMode"),
                GetString(item, "type"),
                GetBool(item, "manualOnly"),
                GetString(item, "kind"),
                GetString(item, "sourceTrust"),
                string.Join(' ', GetString(item, "notes"), GetString(item, "technicianNotes"), GetString(item, "actionReason")),
                GetString(item, "legacyWarning"),
                GetString(item, "licenseNote"),
                GetString(item, "dest"),
                GetString(item, "family"));

            if (!ManifestPromotionPolicy.IsValidDownloadMode(inferred))
            {
                invalid.Add($"{name}: {inferred}");
            }
        }

        Assert.Empty(invalid);
    }

    [Fact]
    public void ManagedDownloadManifest_DownloadModePolicyKeepsUnsafeModesPageOnly()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var violations = new List<string>();

        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            var name = GetString(item, "name");
            var type = GetString(item, "type");
            var mode = ManifestPromotionPolicy.CanonicalizeDownloadMode(GetString(item, "downloadMode"));
            var hasChecksum = HasChecksumProof(item);

            if (mode == ManifestPromotionPolicy.ManagedDownload && !string.Equals(type, "file", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add($"{name}: ManagedDownload must be type=file.");
            }

            if (string.Equals(type, "file", StringComparison.OrdinalIgnoreCase) && mode != ManifestPromotionPolicy.ManagedDownload)
            {
                violations.Add($"{name}: type=file must map to ManagedDownload.");
            }

            if (mode == ManifestPromotionPolicy.ManagedDownload && !hasChecksum &&
                !(item.TryGetProperty("requiresResolution", out var rr) && rr.ValueKind == JsonValueKind.True))
            {
                violations.Add($"{name}: ManagedDownload lacks checksum proof.");
            }

            var pageOnlyModes = new[]
            {
                ManifestPromotionPolicy.ManualMediaRequired,
                ManifestPromotionPolicy.FirmwareBlocked,
                ManifestPromotionPolicy.VendorPortal,
                ManifestPromotionPolicy.OemSpecific,
                ManifestPromotionPolicy.LicenseRestricted
            };

            if (pageOnlyModes.Contains(mode, StringComparer.Ordinal) &&
                !string.Equals(type, "page", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add($"{name}: {mode} must stay type=page.");
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void ManagedDownloadManifest_PageItemsDoNotCarryChecksumFields()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var violations = new List<string>();

        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            if (!string.Equals(GetString(item, "type"), "page", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var checksumField in new[] { "sha256", "sha256Url", "sha512", "sha512Url" })
            {
                if (item.TryGetProperty(checksumField, out _))
                {
                    violations.Add($"{GetString(item, "name")}.{checksumField}");
                }
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void ManagedDownloadManifest_RestrictedPlatformsNeverUseManagedDownload()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var violations = new List<string>();

        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            var mode = GetString(item, "downloadMode");
            if (!string.Equals(mode, ManifestPromotionPolicy.ManagedDownload, StringComparison.Ordinal))
            {
                continue;
            }

            var name = GetString(item, "name");
            var text = string.Join(' ',
                name,
                GetString(item, "dest"),
                GetString(item, "categoryId"),
                GetString(item, "family"),
                GetString(item, "notes"),
                GetString(item, "technicianNotes"),
                GetString(item, "legacyWarning")).ToLowerInvariant();

            if (text.Contains("windows-legacy", StringComparison.Ordinal) ||
                text.Contains("manual iso required", StringComparison.Ordinal) ||
                text.Contains("macos", StringComparison.Ordinal) ||
                text.Contains("ios-ipados", StringComparison.Ordinal) ||
                text.Contains("manual ipsw", StringComparison.Ordinal) ||
                text.Contains("android-manual-firmware-drop", StringComparison.Ordinal) ||
                text.Contains("manual firmware", StringComparison.Ordinal) ||
                text.Contains("bios", StringComparison.Ordinal) ||
                text.Contains("firmware required", StringComparison.Ordinal))
            {
                violations.Add(name);
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void ManagedDownloadManifest_NoGenericInfoActionLabelOutsideInfoOnly()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var violations = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .Where(item => string.Equals(GetString(item, "actionLabel"), "Info", StringComparison.OrdinalIgnoreCase) &&
                           !string.Equals(GetString(item, "downloadMode"), ManifestPromotionPolicy.InfoOnly, StringComparison.Ordinal))
            .Select(item => GetString(item, "name"))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void ManagedDownloadManifest_HasNoDuplicateNamesOrDestinations()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var duplicateNames = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .GroupBy(item => GetString(item, "name"), StringComparer.Ordinal)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();
        var duplicateDestinations = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .GroupBy(item => GetString(item, "dest"), StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        Assert.Empty(duplicateNames);
        Assert.Empty(duplicateDestinations);
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
        var mode = GetString(item, "downloadMode");
        Assert.True(
            GetString(item, "notes").Contains("Manual", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mode, ManifestPromotionPolicy.OfficialDownloadPage, StringComparison.Ordinal) ||
            string.Equals(mode, ManifestPromotionPolicy.LicenseRestricted, StringComparison.Ordinal),
            $"{itemName} must stay page-only with either manual wording or an explicit non-managed downloadMode.");
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
        // Versioned per-release entries were migrated to resolution-backed managed downloads;
        // VeraCrypt demoted to a page entry (no machine-readable checksum source it can bind).
        var promoted = new[]
        {
            "Notepad++ Portable",
            "System Informer",
            "PuTTY 64-bit Installer"
        };

        var policyProviders = LoadPolicyProviders();
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        foreach (var name in promoted)
        {
            var item = document.RootElement.GetProperty("items")
                .EnumerateArray()
                .SingleOrDefault(e => string.Equals(GetString(e, "name"), name, StringComparison.Ordinal));

            Assert.NotEqual(default, item.ValueKind);
            AssertResolutionBackedEntry(item, name, policyProviders);
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
        // Wireshark demoted to a page entry; the surviving batch entries are resolution-backed.
        var promoted = new[]
        {
            "Rufus Portable"
        };

        var policyProviders = LoadPolicyProviders();
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        foreach (var name in promoted)
        {
            var item = document.RootElement.GetProperty("items")
                .EnumerateArray()
                .SingleOrDefault(e => string.Equals(GetString(e, "name"), name, StringComparison.Ordinal));

            Assert.NotEqual(default, item.ValueKind);
            AssertResolutionBackedEntry(item, name, policyProviders);
        }
    }

    [Fact]
    public void ManagedDownloadManifest_Batch3PromotedSourceUrlsAreCanonical()
    {
        // Resolution-backed entries keep the official canonical entry-point URL the provider
        // resolver starts from (a github-stable releases/latest URL is expected, not a stale pin).
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var expectations = new Dictionary<string, string>
        {
            ["Rufus Portable"] = "github.com/pbatard/rufus/releases/latest"
        };

        foreach (var (name, expectedFragment) in expectations)
        {
            var item = document.RootElement.GetProperty("items")
                .EnumerateArray()
                .SingleOrDefault(e => string.Equals(GetString(e, "name"), name, StringComparison.Ordinal));
            var url = GetString(item, "url");

            Assert.StartsWith("https://", url, StringComparison.Ordinal);
            Assert.Contains(expectedFragment, url, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ManagedDownloadManifest_Batch2PromotedSourceUrlsAreCanonical()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var expectations = new Dictionary<string, string>
        {
            ["Notepad++ Portable"] = "github.com/notepad-plus-plus/notepad-plus-plus/releases/latest",
            ["System Informer"] = "github.com/winsiderss/systeminformer/releases/latest",
            ["PuTTY 64-bit Installer"] = "the.earth.li"
        };

        foreach (var (name, expectedFragment) in expectations)
        {
            var item = document.RootElement.GetProperty("items")
                .EnumerateArray()
                .SingleOrDefault(e => string.Equals(GetString(e, "name"), name, StringComparison.Ordinal));
            var url = GetString(item, "url");

            Assert.StartsWith("https://", url, StringComparison.Ordinal);
            Assert.Contains(expectedFragment, url, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ManagedDownloadManifest_Batch4PromotedIsoEntriesHaveValidChecksumAndMetadata()
    {
        // Versioned per-release ISO entries were migrated to resolution-backed managed downloads;
        // the version pin and expected SHA-256 are bound at runtime by the resolved overlay.
        var promoted = new[]
        {
            "Ubuntu LTS Desktop",
            "Ubuntu LTS Server",
            "Debian Stable Netinst",
            "Kali Linux Installer",
            "Rescuezilla"
        };

        var policyProviders = LoadPolicyProviders();
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        foreach (var name in promoted)
        {
            var item = document.RootElement.GetProperty("items")
                .EnumerateArray()
                .SingleOrDefault(e => string.Equals(GetString(e, "name"), name, StringComparison.Ordinal));

            Assert.NotEqual(default, item.ValueKind);
            AssertResolutionBackedEntry(item, name, policyProviders);
        }
    }

    [Fact]
    public void ManagedDownloadManifest_Batch4PromotedSourceUrlsAreCanonicalAndStableOnly()
    {
        var expectations = new Dictionary<string, string>
        {
            ["Ubuntu LTS Desktop"] = "changelogs.ubuntu.com/meta-release-lts",
            ["Ubuntu LTS Server"] = "changelogs.ubuntu.com/meta-release-lts",
            ["Debian Stable Netinst"] = "cdimage.debian.org/debian-cd/current/amd64/iso-cd/SHA256SUMS",
            ["Kali Linux Installer"] = "cdimage.kali.org/current/SHA256SUMS",
            ["Rescuezilla"] = "github.com/rescuezilla/rescuezilla/releases/latest"
        };

        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        foreach (var (name, expectedFragment) in expectations)
        {
            var item = document.RootElement.GetProperty("items")
                .EnumerateArray()
                .SingleOrDefault(e => string.Equals(GetString(e, "name"), name, StringComparison.Ordinal));

            var url = GetString(item, "url");
            Assert.StartsWith("https://", url, StringComparison.Ordinal);
            Assert.Contains(expectedFragment, url, StringComparison.Ordinal);
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
        // NetBSD/openSUSE per-release pins were demoted to page entries; the surviving
        // checksum-index managed entries bind version + SHA-256 at resolution time.
        var promoted = new[]
        {
            "Debian Stable Live GNOME",
            "Debian Stable Live KDE",
            "Debian Stable Live Xfce"
        };

        var policyProviders = LoadPolicyProviders();
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        foreach (var name in promoted)
        {
            var item = document.RootElement.GetProperty("items")
                .EnumerateArray()
                .SingleOrDefault(e => string.Equals(GetString(e, "name"), name, StringComparison.Ordinal));

            Assert.NotEqual(default, item.ValueKind);
            AssertResolutionBackedEntry(item, name, policyProviders);
        }
    }

    [Fact]
    public void ManagedDownloadManifest_Batch5PromotedSourceUrlsAreCanonicalAndStableOnly()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var expectations = new Dictionary<string, string>
        {
            ["Debian Stable Live GNOME"] = "cdimage.debian.org/debian-cd/current-live/amd64/iso-hybrid/SHA256SUMS",
            ["Debian Stable Live KDE"] = "cdimage.debian.org/debian-cd/current-live/amd64/iso-hybrid/SHA256SUMS",
            ["Debian Stable Live Xfce"] = "cdimage.debian.org/debian-cd/current-live/amd64/iso-hybrid/SHA256SUMS"
        };

        foreach (var (name, expectedFragment) in expectations)
        {
            var item = document.RootElement.GetProperty("items")
                .EnumerateArray()
                .SingleOrDefault(e => string.Equals(GetString(e, "name"), name, StringComparison.Ordinal));

            var url = GetString(item, "url");
            Assert.StartsWith("https://", url, StringComparison.Ordinal);
            Assert.Contains(expectedFragment, url, StringComparison.Ordinal);
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
    [InlineData("Windows 8.1 Lifecycle Info", ManifestPromotionPolicy.ManualMediaRequired)]
    [InlineData("Windows 7 Lifecycle Info", ManifestPromotionPolicy.ManualMediaRequired)]
    [InlineData("Apple macOS Download and Install Guide", ManifestPromotionPolicy.ManualMediaRequired)]
    [InlineData("iPhone Manual IPSW", ManifestPromotionPolicy.ManualMediaRequired)]
    [InlineData("Google Pixel Factory Images", ManifestPromotionPolicy.FirmwareBlocked)]
    [InlineData("Microsoft Surface Drivers and Firmware", ManifestPromotionPolicy.FirmwareBlocked)]
    [InlineData("Dell Support / Drivers", ManifestPromotionPolicy.OemSpecific)]
    [InlineData("Parted Magic Download Page", ManifestPromotionPolicy.LicenseRestricted)]
    [InlineData("Hiren's BootCD PE Download Page", ManifestPromotionPolicy.CommunityToolkit)]
    public void ManagedDownloadManifest_KnownRestrictedEntriesUseExpectedDownloadMode(string itemName, string expectedMode)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var item = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .Single(e => string.Equals(GetString(e, "name"), itemName, StringComparison.Ordinal));

        Assert.Equal("page", GetString(item, "type"));
        Assert.Equal(expectedMode, GetString(item, "downloadMode"));
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

    [Fact]
    public void ManagedDownloadManifest_Batch6PromotedExpansionEntriesHaveValidChecksumAndMetadata()
    {
        // The per-version pinned expansion entries were migrated to resolution-backed managed
        // downloads (or demoted to pages). The surviving file entries assert the resolution
        // contract: policy descriptor, matching resolveStrategy, canonical https entry point.
        var promoted = new[]
        {
            "Angry IP Scanner",
            "Driver Store Explorer",
            "RustDesk",
            "balenaEtcher",
            "KeePassXC Portable",
            "TestDisk Win64",
            "Microsoft PowerToys"
        };

        var policyProviders = LoadPolicyProviders();
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        foreach (var name in promoted)
        {
            var item = document.RootElement.GetProperty("items")
                .EnumerateArray()
                .SingleOrDefault(e => string.Equals(GetString(e, "name"), name, StringComparison.Ordinal));

            Assert.NotEqual(default, item.ValueKind);
            AssertResolutionBackedEntry(item, name, policyProviders);
        }
    }

    public void ManagedDownloadManifest_Batch6PromotedSourceUrlsAreCanonicalAndStableOnly()
    {
        var expectations = new Dictionary<string, string>
        {
            ["Angry IP Scanner"] = "github.com/angryip/ipscan/releases/latest",
            ["Driver Store Explorer"] = "github.com/lostindark/DriverStoreExplorer/releases/latest",
            ["RustDesk"] = "github.com/rustdesk/rustdesk/releases/latest",
            ["balenaEtcher"] = "github.com/balena-io/etcher/releases/latest",
            ["KeePassXC Portable"] = "github.com/keepassxreboot/keepassxc/releases/latest",
            ["TestDisk Win64"] = "cgsecurity.org/testdisk_sha256.txt",
            ["Microsoft PowerToys"] = "github.com/microsoft/PowerToys/releases/latest"
        };

        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        foreach (var (name, expectedFragment) in expectations)
        {
            var item = document.RootElement.GetProperty("items")
                .EnumerateArray()
                .SingleOrDefault(e => string.Equals(GetString(e, "name"), name, StringComparison.Ordinal));

            Assert.NotEqual(default, item.ValueKind);
            var url = GetString(item, "url");
            Assert.StartsWith("https://", url, StringComparison.Ordinal);
            Assert.Contains(expectedFragment, url, StringComparison.Ordinal);
            foreach (var forbidden in new[] { "nightly", "beta", "rc-", "snapshot", "/development/" })
            {
                Assert.DoesNotContain(forbidden, url, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void ManagedDownloadManifest_Batch6GithubReleasePromotedEntriesUseGitHubStableStrategy()
    {
        // KeePassXC and PowerToys resolve through the github-stable provider; the per-asset
        // digest/SHA-256 is bound into the overlay at runtime rather than pinned here.
        var promoted = new[]
        {
            "KeePassXC Portable",
            "Microsoft PowerToys"
        };

        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        foreach (var name in promoted)
        {
            var item = document.RootElement.GetProperty("items")
                .EnumerateArray()
                .SingleOrDefault(e => string.Equals(GetString(e, "name"), name, StringComparison.Ordinal));

            Assert.NotEqual(default, item.ValueKind);
            Assert.Equal("github-release", GetString(item, "sourceType"));
            Assert.Equal("github-stable", GetString(item, "resolveStrategy"));
            Assert.StartsWith("https://github.com/", GetString(item, "url"), StringComparison.Ordinal);
        }
    }

    private static string GetString(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool GetBool(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        value.GetBoolean();

    private static IReadOnlyDictionary<string, string> LoadPolicyProviders()
    {
        using var policy = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/resource-policy.json")));
        return policy.RootElement.GetProperty("resources")
            .EnumerateArray()
            .ToDictionary(
                r => r.GetProperty("id").GetString() ?? string.Empty,
                r => r.GetProperty("provider").GetString() ?? string.Empty,
                StringComparer.Ordinal);
    }

    // Resolution-backed managed entries no longer pin per-version artifact URLs or checksums:
    // the backend resolver binds the current version + expected SHA-256 into an overlay at
    // runtime, and Update-ForgerEMS.ps1 fails closed unless that overlay validates. The static
    // contract is therefore the policy linkage, not a frozen payload.
    private static void AssertResolutionBackedEntry(JsonElement item, string name, IReadOnlyDictionary<string, string> policyProviders)
    {
        Assert.Equal("file", GetString(item, "type"));
        Assert.True(item.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean(), $"{name} must be enabled.");
        Assert.True(item.TryGetProperty("requiresResolution", out var rr) && rr.GetBoolean(), $"{name} must require resolution.");
        Assert.Equal(ManifestPromotionPolicy.ManagedDownload, GetString(item, "downloadMode"));
        Assert.Equal("official", GetString(item, "sourceTrust"));
        Assert.False(string.IsNullOrWhiteSpace(GetString(item, "fallbackRule")), $"{name}: fallbackRule is required.");
        Assert.False(string.IsNullOrWhiteSpace(GetString(item, "recommendedUse")), $"{name}: recommendedUse is required.");
        Assert.False(string.IsNullOrWhiteSpace(GetString(item, "licenseNote")), $"{name}: licenseNote is required.");
        Assert.False(string.IsNullOrWhiteSpace(GetString(item, "architecture")), $"{name}: architecture is required.");
        Assert.True(item.TryGetProperty("maintenanceRank", out var rank) && rank.ValueKind == JsonValueKind.Number,
            $"{name}: maintenanceRank is required.");

        var resourceId = GetString(item, "resourceId");
        Assert.False(string.IsNullOrWhiteSpace(resourceId), $"{name}: resourceId is required.");
        Assert.True(policyProviders.TryGetValue(resourceId, out var provider),
            $"{name}: resourceId '{resourceId}' has no resource-policy descriptor.");
        Assert.Equal(provider, GetString(item, "resolveStrategy"));

        Assert.StartsWith("https://", GetString(item, "url"), StringComparison.Ordinal);
        foreach (var checksumField in new[] { "sha256", "sha256Url", "sha512", "sha512Url" })
        {
            Assert.False(item.TryGetProperty(checksumField, out _), $"{name} must not pin {checksumField}; it arrives via the resolved overlay.");
        }
    }

    private static bool HasChecksumProof(JsonElement item) =>
        !string.IsNullOrWhiteSpace(GetString(item, "sha256")) ||
        !string.IsNullOrWhiteSpace(GetString(item, "sha256Url")) ||
        !string.IsNullOrWhiteSpace(GetString(item, "sha512")) ||
        !string.IsNullOrWhiteSpace(GetString(item, "sha512Url"));

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
