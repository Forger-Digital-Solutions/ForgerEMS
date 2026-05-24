using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Catalog presence / classification tests for the MSI and OEM/vendor
/// support shortcut additions (Part C of the v1.2.3-preview.1 follow-up pass).
/// These entries are info shortcuts pointing at official vendor pages — they
/// must never count as required managed-download failures.
/// </summary>
public sealed class ToolkitCatalogVendorShortcutsTests
{
    private static readonly string[] RequiredVendorEntries =
    {
        "MSI Support / Drivers",
        "MSI Laptop Support",
        "MSI Motherboard Support",
        "MSI GPU Support",
        "MSI Center Download",
        "MSI Afterburner",
        "Dell Support / Drivers",
        "HP Support / Drivers",
        "Lenovo Support / Drivers",
        "ASUS Support / Drivers",
        "Acer Support / Drivers",
        "Gigabyte Support / Downloads",
        "ASRock Support / Downloads",
        "Microsoft Surface Drivers and Firmware",
        "Intel Driver and Support Assistant",
        "Crucial Storage Executive",
        "Seagate SeaTools",
        "SanDisk / Western Digital Support",
    };

    [Fact]
    public void Manifest_ContainsExpectedVendorShortcuts()
    {
        var document = LoadManifest();
        var items = document.RootElement.GetProperty("items");
        var byName = items.EnumerateArray()
            .Where(item => item.TryGetProperty("name", out _))
            .ToDictionary(item => item.GetProperty("name").GetString()!, StringComparer.Ordinal);

        foreach (var expected in RequiredVendorEntries)
        {
            Assert.True(byName.ContainsKey(expected), $"Manifest is missing vendor shortcut '{expected}'.");
        }
    }

    [Fact]
    public void VendorShortcuts_AreManualPageType_OfficialAndNonManagedRequired()
    {
        var document = LoadManifest();
        var items = document.RootElement.GetProperty("items");

        foreach (var name in RequiredVendorEntries)
        {
            var item = items.EnumerateArray().Single(x =>
                x.TryGetProperty("name", out var n) &&
                string.Equals(n.GetString(), name, StringComparison.Ordinal));

            // type=page guarantees ForgerEMS treats it as a manual download
            // shortcut, never as a required managed item.
            Assert.Equal("page", item.GetProperty("type").GetString());

            // Manual / official only — protects against accidentally promoting
            // OEM links into the managed auto-download path.
            Assert.True(item.GetProperty("manualOnly").GetBoolean(), $"{name} must be manualOnly.");
            Assert.Equal("official", item.GetProperty("sourceTrust").GetString());

            // URL must be a real https vendor page; no random mirrors.
            var url = item.GetProperty("url").GetString() ?? string.Empty;
            Assert.StartsWith("https://", url, StringComparison.OrdinalIgnoreCase);
            Assert.True(IsApprovedVendorHost(url), $"Vendor shortcut '{name}' uses non-approved host: {url}");
        }
    }

    [Fact]
    public void VendorShortcuts_DestinationsLandInPageFriendlyFolders()
    {
        var document = LoadManifest();
        var items = document.RootElement.GetProperty("items");

        foreach (var name in RequiredVendorEntries)
        {
            var item = items.EnumerateArray().Single(x =>
                x.TryGetProperty("name", out var n) &&
                string.Equals(n.GetString(), name, StringComparison.Ordinal));

            var dest = item.GetProperty("dest").GetString() ?? string.Empty;
            Assert.EndsWith(".url", dest, StringComparison.Ordinal);
            // DOWNLOAD shortcut naming so technicians can find them on the USB.
            Assert.Contains("DOWNLOAD - ", dest, StringComparison.Ordinal);
            // Vendor support links live under Drivers\Vendor or Tools\Portable\*.
            // Anything else would slip into the OS / ISO trees by accident.
            var underDrivers = dest.StartsWith("Drivers\\", StringComparison.Ordinal);
            var underTools = dest.StartsWith("Tools\\Portable\\", StringComparison.Ordinal);
            Assert.True(underDrivers || underTools,
                $"Vendor shortcut '{name}' has unexpected destination root: {dest}");
        }
    }

    [Fact]
    public void RequiredManagedPolicy_StillRequiresAndDoesNotIncludeManualShortcuts()
    {
        // Sanity check: the manifest's release policy stays require-for-release
        // (which protects the managed-download pipeline) even after the OEM
        // shortcut additions, because shortcuts are page-type and excluded.
        var document = LoadManifest();
        Assert.Equal(
            "require-for-release",
            document.RootElement.GetProperty("managedChecksumPolicy").GetString());
    }

    [Fact]
    public void EligibilityAudit_NoVendorShortcutSilentlyBecameManagedDownload()
    {
        // Final pre-package audit: none of the 18 vendor entries surveyed by
        // this pass should ever flip to type="file" without an explicit audit
        // that supplies a stable direct URL AND a vendor checksum (sha256 or
        // sha256Url). require-for-release would catch this at build time, but
        // we lock it in at unit-test time so a casual edit can't smuggle one
        // through.
        var document = LoadManifest();
        foreach (var name in RequiredVendorEntries)
        {
            var item = document.RootElement.GetProperty("items").EnumerateArray().Single(x =>
                x.TryGetProperty("name", out var n) &&
                string.Equals(n.GetString(), name, StringComparison.Ordinal));
            Assert.Equal("page", item.GetProperty("type").GetString());
            Assert.False(item.TryGetProperty("sha256", out _),
                $"{name}: vendor shortcut must not carry a managed-download sha256.");
            Assert.False(item.TryGetProperty("sha256Url", out _),
                $"{name}: vendor shortcut must not carry a managed-download sha256Url.");
            Assert.False(item.TryGetProperty("sha512", out _),
                $"{name}: vendor shortcut must not carry a managed-download sha512.");
            Assert.False(item.TryGetProperty("sha512Url", out _),
                $"{name}: vendor shortcut must not carry a managed-download sha512Url.");
        }
    }

    [Fact]
    public void EligibilityAudit_VendorShortcutsCarryTechnicianRationale()
    {
        // The audit pass added a technicianNotes line on every entry that
        // explains why it stays manual. Keep this in place so the rationale
        // shows up in toolkit reports and on hover, and so future maintainers
        // see it before considering a managed promotion.
        var document = LoadManifest();
        foreach (var name in RequiredVendorEntries)
        {
            var item = document.RootElement.GetProperty("items").EnumerateArray().Single(x =>
                x.TryGetProperty("name", out var n) &&
                string.Equals(n.GetString(), name, StringComparison.Ordinal));
            Assert.True(item.TryGetProperty("technicianNotes", out var technicianNotes),
                $"{name}: technicianNotes is required so the manual rationale travels with the entry.");
            var text = technicianNotes.GetString() ?? string.Empty;
            Assert.False(string.IsNullOrWhiteSpace(text),
                $"{name}: technicianNotes must not be empty.");
            // Sanity: the rationale should mention why it is manual.
            Assert.Contains("Stays manual", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void EligibilityAudit_ManualVendorEntriesDoNotIncreaseManagedFileCount()
    {
        // The packaged manifest tracks 30 active managed file downloads. Vendor
        // shortcuts must not change that count — if a future change accidentally
        // promotes one to type="file", this test makes it visible immediately.
        var document = LoadManifest();
        var activeManagedFileCount = 0;
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            var type = item.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (!string.Equals(type, "file", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var enabled = !item.TryGetProperty("enabled", out var e) || e.GetBoolean();
            if (enabled)
            {
                activeManagedFileCount++;
            }
        }

        Assert.Equal(30, activeManagedFileCount);
    }

    private static bool IsApprovedVendorHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        string[] approvedSuffixes =
        {
            "msi.com",
            "dell.com",
            "hp.com",
            "lenovo.com",
            "asus.com",
            "acer.com",
            "gigabyte.com",
            "asrock.com",
            "microsoft.com",
            "learn.microsoft.com",
            "intel.com",
            "amd.com",
            "nvidia.com",
            "realtek.com",
            "crucial.com",
            "seagate.com",
            "westerndigital.com",
            "sandisk.com",
            "samsung.com",
            "wd.com",
        };
        foreach (var suffix in approvedSuffixes)
        {
            if (host.Equals(suffix, StringComparison.Ordinal) ||
                host.EndsWith("." + suffix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static JsonDocument LoadManifest()
    {
        var path = FindRepoFile("manifests", "ForgerEMS.updates.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static string FindRepoFile(params string[] segments)
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate repo file.", Path.Combine(segments));
    }
}
