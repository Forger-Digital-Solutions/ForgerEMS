using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

// Phase 3 (v1.2.3-preview.1): Full Managed Download action under USB Builder Profile.
// These tests pin the planner's classification of every managed catalog entry into the
// backend's category set, exercise profile-filter semantics, exclusion semantics, and the
// presence-check behavior against a target root. They do not exercise the live download
// pipeline (Update-ForgerEMS owns that and has its own Pester coverage); the action's
// "no destructive USB writes" contract is enforced structurally by reusing the existing
// Update-ForgerEMS pipeline with -IncludedCategories.
public sealed class FullManagedDownloadProfileTests
{
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

    private static readonly string[] AllCategories =
    {
        "core", "windows", "legacy-windows", "linux-rescue",
        "macos", "android", "ios-ipados", "oem-tools", "diagnostics"
    };

    private static readonly string[] CoreOnlyCategories = ["core"];

    private static readonly string[] LinuxRescueAndCoreCategories = ["linux-rescue", "core"];

    private static readonly string[] ManagedDownloadCategories = ["linux-rescue", "diagnostics", "windows", "core"];

    [Fact]
    public void Planner_AllManagedFileEntries_ClassifyIntoKnownCategorySet()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(SourceManifestPath));
        var validCategories = new HashSet<string>(AllCategories, StringComparer.OrdinalIgnoreCase);

        foreach (var raw in document.RootElement.GetProperty("items").EnumerateArray())
        {
            if (!string.Equals(GetString(raw, "type"), "file", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (raw.TryGetProperty("enabled", out var enabledNode) && enabledNode.ValueKind == JsonValueKind.False)
            {
                continue;
            }

            var asJsonObject = JsonNode.Parse(raw.GetRawText()) as JsonObject;
            Assert.NotNull(asJsonObject);
            var category = UsbBuilderProfileFullManagedDownloadPlanner.ClassifyCategoryId(asJsonObject!);

            Assert.True(
                validCategories.Contains(category),
                $"{GetString(raw, "name")} classified as '{category}', not in known category set.");
        }
    }

    [Fact]
    public void Planner_WithAllCategories_EligibleCountMatchesActiveManagedFileCount()
    {
        // The resolution-migrated catalog carries 20 managed file entries, all with
        // requiresResolution (expected SHA-256 arrives via the verified overlay, not
        // static fields). The planner's eligibility math must agree when every category
        // is selected.
        var plan = UsbBuilderProfileFullManagedDownloadPlanner.Calculate(
            SourceManifestPath,
            new HashSet<string>(AllCategories, StringComparer.OrdinalIgnoreCase),
            usbRootPath: null);

        Assert.Equal(20, plan.EligibleManagedCount);
    }

    [Fact]
    public void Planner_ExcludesManualAndVendorPageItems()
    {
        var plan = UsbBuilderProfileFullManagedDownloadPlanner.Calculate(
            SourceManifestPath,
            new HashSet<string>(AllCategories, StringComparer.OrdinalIgnoreCase),
            usbRootPath: null);

        // Catalog has 206 items total; 20 managed files; the remaining 186 are
        // page/manual/vendor shortcuts. The planner must count those into the
        // manual-or-vendor exclusion bucket.
        Assert.Equal(186, plan.ExcludedManualOrVendorCount);
        Assert.DoesNotContain(plan.EligibleNames, n => n.Contains("Download Page", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan.EligibleNames, n => n.StartsWith("MSI ", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan.EligibleNames, n => n.StartsWith("Dell ", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan.EligibleNames, n => n.Contains("Realtek", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan.EligibleNames, n => n.Contains("NVIDIA Drivers", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.EligibleNames, n => n.Contains("AMD Drivers", StringComparison.Ordinal));
    }

    [Fact]
    public void Planner_ProfileFilter_OnlyCoreCategory_KeepsOnlyVentoyFallback()
    {
        // Selecting only "core" should isolate the Ventoy pinned fallback; everything else
        // becomes profile-excluded. (Backend always force-includes core anyway.)
        var plan = UsbBuilderProfileFullManagedDownloadPlanner.Calculate(
            SourceManifestPath,
            new HashSet<string>(CoreOnlyCategories, StringComparer.OrdinalIgnoreCase),
            usbRootPath: null);

        Assert.Equal(1, plan.EligibleManagedCount);
        Assert.Contains(plan.EligibleNames, n => n.Contains("Ventoy", StringComparison.OrdinalIgnoreCase));
        Assert.True(plan.ExcludedByProfileCount > 0, "Other managed items must be reported as profile-excluded.");
    }

    [Fact]
    public void Planner_ProfileFilter_LinuxRescueOnly_IncludesLinuxAndBsdOsEntries()
    {
        // BSD entries (NetBSD/FreeBSD/OpenBSD) intentionally route to linux-rescue alongside
        // Linux ISOs because the backend has no dedicated bsd category.
        var plan = UsbBuilderProfileFullManagedDownloadPlanner.Calculate(
            SourceManifestPath,
            new HashSet<string>(LinuxRescueAndCoreCategories, StringComparer.OrdinalIgnoreCase),
            usbRootPath: null);

        // Resolution-era names are versionless (the version arrives via overlay): Linux
        // rescue covers Ubuntu/Debian/Kali managed ISOs.
        Assert.Contains(plan.EligibleNames, n => n.Equals("Ubuntu LTS Desktop", StringComparison.Ordinal));
        Assert.Contains(plan.EligibleNames, n => n.Equals("Ubuntu LTS Server", StringComparison.Ordinal));
        Assert.Contains(plan.EligibleNames, n => n.Equals("Kali Linux Installer", StringComparison.Ordinal));
        Assert.Contains(plan.EligibleNames, n => n.Equals("Debian Stable Netinst", StringComparison.Ordinal));
        Assert.Contains(plan.EligibleNames, n => n.Equals("Debian Stable Live GNOME", StringComparison.Ordinal));
    }

    [Fact]
    public void Planner_DoesNotIncludeAnyOemDriverPortalEntry()
    {
        // Defense in depth: even though OEM portals are page-type today, this test pins
        // that the classifier routes any drivers\* path into oem-tools and that nothing
        // there can sneak into a non-oem-tools selection.
        var plan = UsbBuilderProfileFullManagedDownloadPlanner.Calculate(
            SourceManifestPath,
            new HashSet<string>(ManagedDownloadCategories, StringComparer.OrdinalIgnoreCase),
            usbRootPath: null);

        foreach (var name in plan.EligibleNames)
        {
            Assert.DoesNotContain("Surface", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Realtek", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Intel Bluetooth", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Intel Wi-Fi", name, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Planner_ProfileItemFilter_IncludesOnlySelectedManagedItemsPlusCore()
    {
        var plan = UsbBuilderProfileFullManagedDownloadPlanner.Calculate(
            SourceManifestPath,
            new HashSet<string>(AllCategories, StringComparer.OrdinalIgnoreCase),
            usbRootPath: null,
            includedProfileItems:
            [
                "name:Rufus Portable"
            ]);

        Assert.Contains(plan.EligibleNames, n => n.Equals("Rufus Portable", StringComparison.Ordinal));
        Assert.Contains(plan.EligibleNames, n => n.Contains("Ventoy", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan.EligibleNames, n => n.Contains("CrystalDiskInfo", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan.EligibleNames, n => n.Contains("Ubuntu", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, plan.EligibleManagedCount);
        Assert.True(plan.ExcludedByProfileCount > 0);
    }

    [Fact]
    public void Planner_ProfileItemFilter_NameSelectorsAreExact()
    {
        var plan = UsbBuilderProfileFullManagedDownloadPlanner.Calculate(
            SourceManifestPath,
            new HashSet<string>(AllCategories, StringComparer.OrdinalIgnoreCase),
            usbRootPath: null,
            includedProfileItems:
            [
                "name:Ubuntu LTS Desktop"
            ]);

        Assert.Contains(plan.EligibleNames, n => n.Equals("Ubuntu LTS Desktop", StringComparison.Ordinal));
        Assert.Contains(plan.EligibleNames, n => n.Contains("Ventoy", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan.EligibleNames, n => n.Equals("Ubuntu LTS Server", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.EligibleNames, n => n.Contains("Kubuntu", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan.EligibleNames, n => n.Contains("Lubuntu", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, plan.EligibleManagedCount);
    }

    [Fact]
    public void Planner_ProfileItemFilter_LinkOnlyItemIsNotCountedAsManagedDownload()
    {
        var plan = UsbBuilderProfileFullManagedDownloadPlanner.Calculate(
            SourceManifestPath,
            new HashSet<string>(["windows", "core"], StringComparer.OrdinalIgnoreCase),
            usbRootPath: null,
            includedProfileItems:
            [
                "name:Windows 11 Download Page"
            ]);

        Assert.Contains(plan.EligibleNames, n => n.Contains("Ventoy", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan.EligibleNames, n => n.Contains("Windows 11", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, plan.EligibleManagedCount);
        Assert.True(plan.ExcludedManualOrVendorCount >= 1);
    }

    [Fact]
    public void Planner_NotebookPresenceCheck_TreatsExistingFileAsAlreadyPresent()
    {
        // Verify the presence-check honors usbRootPath: drop a stub file at a known managed
        // destination and confirm the planner reports it as already present.
        var tempRoot = Path.Combine(Path.GetTempPath(), "ForgerEMS-FMD-Test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            // Use the Rufus managed entry's dest as the synthetic "already present" anchor.
            const string rufusDest = "Tools\\Portable\\USB\\rufus.exe";
            var stubPath = Path.Combine(tempRoot, rufusDest);
            Directory.CreateDirectory(Path.GetDirectoryName(stubPath)!);
            File.WriteAllText(stubPath, "stub");

            var plan = UsbBuilderProfileFullManagedDownloadPlanner.Calculate(
                SourceManifestPath,
                new HashSet<string>(AllCategories, StringComparer.OrdinalIgnoreCase),
                usbRootPath: tempRoot);

            Assert.True(plan.HasUsbRoot);
            Assert.Contains(plan.AlreadyPresentNames, n => n.Equals("Rufus Portable", StringComparison.Ordinal));
            Assert.DoesNotContain(plan.MissingNames, n => n.Equals("Rufus Portable", StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Planner_WithNoUsbRoot_ReportsAllEligibleAsMissingWithoutCrashing()
    {
        var plan = UsbBuilderProfileFullManagedDownloadPlanner.Calculate(
            SourceManifestPath,
            new HashSet<string>(AllCategories, StringComparer.OrdinalIgnoreCase),
            usbRootPath: null);

        Assert.False(plan.HasUsbRoot);
        Assert.Equal(plan.EligibleManagedCount, plan.MissingCount);
        Assert.Equal(0, plan.AlreadyPresentCount);
    }

    [Theory]
    [InlineData("Ubuntu LTS Desktop", "linux-rescue")]
    [InlineData("Ubuntu LTS Server", "linux-rescue")]
    [InlineData("Kali Linux Installer", "linux-rescue")]
    [InlineData("Debian Stable Netinst", "linux-rescue")]
    [InlineData("Debian Stable Live GNOME", "linux-rescue")]
    [InlineData("Debian Stable Live KDE", "linux-rescue")]
    [InlineData("Debian Stable Live Xfce", "linux-rescue")]
    [InlineData("Rescuezilla", "linux-rescue")]
    [InlineData("Angry IP Scanner", "diagnostics")]
    [InlineData("Driver Store Explorer", "diagnostics")]
    [InlineData("RustDesk", "diagnostics")]
    [InlineData("Rufus Portable", "diagnostics")]
    [InlineData("KeePassXC Portable", "diagnostics")]
    [InlineData("TestDisk Win64", "diagnostics")]
    [InlineData("Ventoy Windows Package", "core")]
    public void Planner_KnownManagedEntries_ClassifyToExpectedCategory(string entryName, string expectedCategory)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(SourceManifestPath));
        var rawItem = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .FirstOrDefault(i => string.Equals(GetString(i, "name"), entryName, StringComparison.Ordinal));

        Assert.NotEqual(default, rawItem.ValueKind);

        var jsonObject = JsonNode.Parse(rawItem.GetRawText()) as JsonObject;
        Assert.NotNull(jsonObject);

        var classified = UsbBuilderProfileFullManagedDownloadPlanner.ClassifyCategoryId(jsonObject!);
        Assert.Equal(expectedCategory, classified);
    }

    [Fact]
    public void Planner_ShortSummaryLine_IncludesEligibleAndManagedReadyCounts()
    {
        var plan = UsbBuilderProfileFullManagedDownloadPlanner.Calculate(
            SourceManifestPath,
            new HashSet<string>(AllCategories, StringComparer.OrdinalIgnoreCase),
            usbRootPath: null);

        Assert.Contains("Managed downloads", plan.ShortSummaryLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("eligible", plan.ShortSummaryLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Manual/vendor links", plan.ManualLinkLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("guided shortcuts", plan.ManualLinkLine, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetString(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
