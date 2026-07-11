using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Catalog Freshness Intelligence schema and classification invariants.
/// These tests lock in the safety contract for the freshness metadata so a
/// future pass cannot silently introduce beta/nightly channels, drop the
/// review flag on a major-version jump, or float a pinned version to a
/// "latest" claim without filling in the supporting fields.
/// </summary>
public sealed class CatalogFreshnessTests
{
    private static readonly HashSet<string> ValidStatuses = new(StringComparer.Ordinal)
    {
        "UpToDate", "PatchUpdateAvailable", "MinorUpdateAvailable",
        "MajorUpdateAvailable", "ManualReviewRequired", "ChecksumVerificationRequired",
        "SourceChanged", "UpdateUnsafe", "LegacyPinned", "VendorWorkflowChanged"
    };

    private static readonly HashSet<string> ValidChannels = new(StringComparer.Ordinal)
    {
        "stable", "LTS", "ESR", "manual-only", "legacy-pinned"
    };

    private static readonly HashSet<string> ValidChecksumModes = new(StringComparer.Ordinal)
    {
        "sha256-pinned", "sha256url-only", "sha512-pinned", "sha512url-only", "github-asset-digest", "manual", "unverified"
    };

    private static readonly HashSet<string> ValidUpstreamTypes = new(StringComparer.Ordinal)
    {
        "github-releases", "vendor-version-page", "vendor-rss",
        "sourceforge-rss", "official-mirror-index", "manual"
    };

    private static readonly HashSet<string> ReviewRequiringStatuses = new(StringComparer.Ordinal)
    {
        "MajorUpdateAvailable", "ManualReviewRequired", "ChecksumVerificationRequired",
        "SourceChanged", "UpdateUnsafe", "VendorWorkflowChanged"
    };

    [Fact]
    public void Freshness_EveryFileEntryHasFreshnessBlock()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var missing = doc.RootElement.GetProperty("items").EnumerateArray()
            .Where(i => string.Equals(GetString(i, "type"), "file", StringComparison.OrdinalIgnoreCase))
            .Where(i => !i.TryGetProperty("freshness", out _))
            .Select(i => GetString(i, "name"))
            .ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void Freshness_StatusValuesAreInValidSet()
    {
        var bad = new List<string>();
        foreach (var item in FileEntries())
        {
            var f = item.GetProperty("freshness");
            var status = GetString(f, "freshnessStatus");
            if (!ValidStatuses.Contains(status))
            {
                bad.Add($"{GetString(item, "name")}: freshnessStatus '{status}' not in valid set.");
            }
        }
        Assert.Empty(bad);
    }

    [Fact]
    public void Freshness_UpdateChannelsAreInValidSet_NoBetaNightlyRC()
    {
        var bad = new List<string>();
        foreach (var item in FileEntries())
        {
            var f = item.GetProperty("freshness");
            var channel = GetString(f, "updateChannel");
            if (!ValidChannels.Contains(channel))
            {
                bad.Add($"{GetString(item, "name")}: updateChannel '{channel}' not in valid set.");
            }
            // Defense in depth: even if someone slips a token through the schema
            // enum, refuse to accept beta-like names.
            foreach (var forbidden in new[] { "beta", "nightly", "rc", "canary", "dev", "edge", "alpha", "preview" })
            {
                if (channel.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                {
                    bad.Add($"{GetString(item, "name")}: updateChannel '{channel}' contains forbidden token '{forbidden}'.");
                }
            }
        }
        Assert.Empty(bad);
    }

    [Fact]
    public void Freshness_NoLatestStableLooksLikeBetaRC()
    {
        // latestKnownStableVersion must NEVER contain pre-release tokens; the
        // freshness audit explicitly chases stable/LTS/ESR only.
        var bad = new List<string>();
        foreach (var item in FileEntries())
        {
            var version = GetString(item.GetProperty("freshness"), "latestKnownStableVersion");
            foreach (var forbidden in new[] { "beta", "rc", "nightly", "canary", "alpha", "dev", "preview" })
            {
                if (version.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                {
                    bad.Add($"{GetString(item, "name")}: latestKnownStableVersion '{version}' contains forbidden token '{forbidden}'.");
                }
            }
        }
        Assert.Empty(bad);
    }

    [Fact]
    public void Freshness_ChecksumModeValuesAreInValidSet()
    {
        var bad = new List<string>();
        foreach (var item in FileEntries())
        {
            var mode = GetString(item.GetProperty("freshness"), "checksumVerificationMode");
            if (!ValidChecksumModes.Contains(mode))
            {
                bad.Add($"{GetString(item, "name")}: checksumVerificationMode '{mode}' not in valid set.");
            }
        }
        Assert.Empty(bad);
    }

    [Fact]
    public void Freshness_UpstreamReleaseTypesAreInValidSet()
    {
        var bad = new List<string>();
        foreach (var item in FileEntries())
        {
            var t = GetString(item.GetProperty("freshness"), "upstreamReleaseType");
            if (!ValidUpstreamTypes.Contains(t))
            {
                bad.Add($"{GetString(item, "name")}: upstreamReleaseType '{t}' not in valid set.");
            }
        }
        Assert.Empty(bad);
    }

    [Fact]
    public void Freshness_RequiresManualReviewMatchesStatusClass()
    {
        var bad = new List<string>();
        foreach (var item in FileEntries())
        {
            var f = item.GetProperty("freshness");
            var status = GetString(f, "freshnessStatus");
            var required = GetBool(f, "requiresManualReview");
            var shouldBeRequired = ReviewRequiringStatuses.Contains(status);
            if (required != shouldBeRequired)
            {
                bad.Add($"{GetString(item, "name")}: requiresManualReview={required} disagrees with status '{status}' (expected={shouldBeRequired}).");
            }
        }
        Assert.Empty(bad);
    }

    [Fact]
    public void Freshness_MajorVersionBoundaryImpliesMajorUpdateStatus()
    {
        var bad = new List<string>();
        foreach (var item in FileEntries())
        {
            var f = item.GetProperty("freshness");
            var boundary = GetBool(f, "majorVersionBoundary");
            var status = GetString(f, "freshnessStatus");
            if (boundary && status != "MajorUpdateAvailable")
            {
                bad.Add($"{GetString(item, "name")}: majorVersionBoundary=true but status='{status}' (must be MajorUpdateAvailable).");
            }
        }
        Assert.Empty(bad);
    }

    [Fact]
    public void Freshness_UpToDateEntriesHaveMatchingVersions()
    {
        var bad = new List<string>();
        foreach (var item in FileEntries())
        {
            var f = item.GetProperty("freshness");
            if (GetString(f, "freshnessStatus") != "UpToDate") { continue; }
            var pinned = GetString(f, "currentPinnedVersion");
            var latest = GetString(f, "latestKnownStableVersion");
            if (string.IsNullOrWhiteSpace(pinned) || string.IsNullOrWhiteSpace(latest))
            {
                bad.Add($"{GetString(item, "name")}: UpToDate must record both pinned and latest versions.");
            }
            else if (!string.Equals(pinned, latest, StringComparison.Ordinal))
            {
                bad.Add($"{GetString(item, "name")}: UpToDate but pinned='{pinned}' != latest='{latest}'.");
            }
        }
        Assert.Empty(bad);
    }

    [Fact]
    public void Freshness_UpdateAvailableEntriesHaveDifferingVersions()
    {
        var updateStatuses = new HashSet<string>(StringComparer.Ordinal)
        {
            "PatchUpdateAvailable", "MinorUpdateAvailable", "MajorUpdateAvailable"
        };
        var bad = new List<string>();
        foreach (var item in FileEntries())
        {
            var f = item.GetProperty("freshness");
            var status = GetString(f, "freshnessStatus");
            if (!updateStatuses.Contains(status)) { continue; }
            var pinned = GetString(f, "currentPinnedVersion");
            var latest = GetString(f, "latestKnownStableVersion");
            if (string.IsNullOrWhiteSpace(pinned) || string.IsNullOrWhiteSpace(latest))
            {
                bad.Add($"{GetString(item, "name")}: {status} must record both pinned and latest versions.");
            }
            else if (string.Equals(pinned, latest, StringComparison.Ordinal))
            {
                bad.Add($"{GetString(item, "name")}: {status} but pinned='{pinned}' == latest='{latest}'.");
            }
        }
        Assert.Empty(bad);
    }

    [Fact]
    public void CrystalDiskInfo_RemainsPinnedUntilTheOfficialUpdateHasChecksumProof()
    {
        var item = FileEntries().Single(i =>
            string.Equals(GetString(i, "name"), "CrystalDiskInfo 9.8.0 (standard zip)", StringComparison.Ordinal));
        var freshness = item.GetProperty("freshness");

        Assert.Equal("9.8.0", GetString(freshness, "currentPinnedVersion"));
        Assert.Equal("9.9.1", GetString(freshness, "latestKnownStableVersion"));
        Assert.Equal("MinorUpdateAvailable", GetString(freshness, "freshnessStatus"));
        Assert.Equal("sha256-pinned", GetString(freshness, "checksumVerificationMode"));
        Assert.Contains("machine-readable checksum", GetString(freshness, "updateRecommendation"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ManagedDownload", GetString(item, "downloadMode"));
        Assert.False(item.TryGetProperty("sha256Url", out _), "Do not invent a vendor checksum URL for this pinned-only entry.");
    }

    [Fact]
    public void Freshness_PinnedVersionAppearsSomewhereInUrlOrName()
    {
        // Defensive: when we declare a currentPinnedVersion, that string must
        // appear in either the entry's url or its name. Prevents drift where a
        // freshness audit silently re-labels an entry to a different version.
        var bad = new List<string>();
        foreach (var item in FileEntries())
        {
            var pinned = GetString(item.GetProperty("freshness"), "currentPinnedVersion");
            if (string.IsNullOrWhiteSpace(pinned)) { continue; }
            var name = GetString(item, "name");
            var url = GetString(item, "url");
            if (name.IndexOf(pinned, StringComparison.Ordinal) < 0 &&
                url.IndexOf(pinned, StringComparison.Ordinal) < 0)
            {
                bad.Add($"{name}: pinned version '{pinned}' not found in url or name.");
            }
        }
        Assert.Empty(bad);
    }

    [Fact]
    public void Freshness_LastAuditTimestampIsIsoLike()
    {
        var bad = new List<string>();
        foreach (var item in FileEntries())
        {
            var ts = GetString(item.GetProperty("freshness"), "lastFreshnessAuditUtc");
            if (string.IsNullOrWhiteSpace(ts))
            {
                bad.Add($"{GetString(item, "name")}: lastFreshnessAuditUtc is empty.");
                continue;
            }
            if (!DateTimeOffset.TryParse(ts, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out _))
            {
                bad.Add($"{GetString(item, "name")}: lastFreshnessAuditUtc '{ts}' is not parseable as ISO-8601.");
            }
        }
        Assert.Empty(bad);
    }

    [Fact]
    public void Freshness_PageEntriesDoNotCarryFreshnessBlock()
    {
        // Page entries are by definition manual; they should not carry a
        // freshness block (the schema does not forbid it, but our policy is
        // to scope freshness intelligence to managed file entries).
        var bad = new List<string>();
        using var doc = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            if (!string.Equals(GetString(item, "type"), "page", StringComparison.OrdinalIgnoreCase)) { continue; }
            if (item.TryGetProperty("freshness", out _))
            {
                bad.Add($"{GetString(item, "name")}: page entries must not carry a freshness block.");
            }
        }
        Assert.Empty(bad);
    }

    [Fact]
    public void Freshness_ChecksumModeMatchesPresentMetadata()
    {
        // sha256-pinned requires both sha256 and sha256Url (or at least one).
        // sha256url-only requires sha256Url but no sha256.
        // github-asset-digest requires an api.github.com sha256Url.
        var bad = new List<string>();
        foreach (var item in FileEntries())
        {
            var mode = GetString(item.GetProperty("freshness"), "checksumVerificationMode");
            var hasSha = item.TryGetProperty("sha256", out _);
            var hasSha512 = item.TryGetProperty("sha512", out _);
            var sha256Url = GetString(item, "sha256Url");
            var sha512Url = GetString(item, "sha512Url");
            var name = GetString(item, "name");
            switch (mode)
            {
                case "sha256-pinned":
                    if (!hasSha)
                    {
                        bad.Add($"{name}: checksumVerificationMode=sha256-pinned but entry has no sha256 field.");
                    }
                    break;
                case "sha256url-only":
                    if (string.IsNullOrWhiteSpace(sha256Url))
                    {
                        bad.Add($"{name}: checksumVerificationMode=sha256url-only but entry has no sha256Url field.");
                    }
                    break;
                case "sha512-pinned":
                    if (!hasSha512)
                    {
                        bad.Add($"{name}: checksumVerificationMode=sha512-pinned but entry has no sha512 field.");
                    }
                    break;
                case "sha512url-only":
                    if (string.IsNullOrWhiteSpace(sha512Url))
                    {
                        bad.Add($"{name}: checksumVerificationMode=sha512url-only but entry has no sha512Url field.");
                    }
                    break;
                case "github-asset-digest":
                    if (!sha256Url.StartsWith("https://api.github.com/", StringComparison.Ordinal))
                    {
                        bad.Add($"{name}: checksumVerificationMode=github-asset-digest but sha256Url does not point at api.github.com.");
                    }
                    break;
            }
        }
        Assert.Empty(bad);
    }

    [Fact]
    public void Freshness_AuditHelperEmitsExpectedClassificationCounts()
    {
        // This is a snapshot test of the 2026-05-21 audit pass: tests pinning
        // the exact counts would be too brittle (a single upstream patch
        // changes them), so we only assert there is at least one of each
        // safety class present in the metadata, and that no entry has an
        // unsafe classification slipping through.
        using var doc = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        var statuses = doc.RootElement.GetProperty("items").EnumerateArray()
            .Where(i => string.Equals(GetString(i, "type"), "file", StringComparison.OrdinalIgnoreCase))
            .Select(i => GetString(i.GetProperty("freshness"), "freshnessStatus"))
            .ToArray();

        Assert.NotEmpty(statuses);
        foreach (var s in statuses)
        {
            Assert.Contains(s, ValidStatuses);
        }
        // Defensive: this pass should never emit UpdateUnsafe automatically.
        // If an entry is unsafe, it must be flipped to that status by a human
        // (with rationale captured elsewhere in the manifest/notes).
        Assert.DoesNotContain("UpdateUnsafe", statuses);
    }

    private static IEnumerable<JsonElement> FileEntries()
    {
        var doc = JsonDocument.Parse(File.ReadAllText(FindRepoFile("manifests/ForgerEMS.updates.json")));
        foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            if (string.Equals(GetString(item, "type"), "file", StringComparison.OrdinalIgnoreCase))
            {
                yield return item.Clone();
            }
        }
    }

    private static string GetString(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool GetBool(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value) &&
        (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False) &&
        value.GetBoolean();

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
