using System.Text.Json;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class ToolkitWorkspacePlanningTests
{
    private static readonly JsonSerializerOptions CaseInsensitiveJsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void BuildPlan_PreventsDuplicateSelectionsByCatalogPath()
    {
        var a = Item("SystemRescue", "ISO\\Linux\\systemrescue.iso", selected: true);
        var duplicate = Item("SystemRescue mirror", "ISO/Linux/systemrescue.iso", selected: true);

        var plan = ToolkitWorkspacePlanner.BuildPlan([a, duplicate]);

        Assert.Single(plan.Items);
        Assert.Equal(1, plan.ManagedCount);
        Assert.Contains("Plan contains 1 item", plan.ValidationSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPlan_ClassifiesManualAndManagedWithoutExecutingDownloads()
    {
        var managed = Item("GParted", "ISO\\Tools\\gparted.iso", selected: true, sizeBytes: 512L * 1024 * 1024);
        var manual = Item("Windows 11", "ISO\\Windows\\DOWNLOAD - Windows 11.url", selected: true, manualOnly: true);

        var plan = ToolkitWorkspacePlanner.BuildPlan([managed, manual]);

        Assert.Equal(1, plan.ManagedCount);
        Assert.Equal(1, plan.ManualCount);
        Assert.Equal("Ready to download", plan.Items[0].PlanSectionLabel);
        Assert.Equal("Manual required", plan.Items[1].PlanSectionLabel);
        Assert.Equal("Checksum limited", plan.Items[0].ChecksumLabel);
        Assert.Contains("Official vendor source required", plan.Items[1].RequirementLabel, StringComparison.Ordinal);
        Assert.Contains("Ready to download: 1", plan.ValidationSummary, StringComparison.Ordinal);
        Assert.Contains("Manual required: 1", plan.ValidationSummary, StringComparison.Ordinal);
        Assert.Contains("no downloads or USB writes are executed by planning", plan.ValidationSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPlan_SurfacesUpdateAvailableAndChecksumVerifiedLabels()
    {
        var managed = new ToolkitHealthItemView
        {
            Tool = "CrystalDiskInfo",
            Category = "Disk",
            ExpectedPath = "Tools\\Portable\\Disk\\CrystalDiskInfo.zip",
            Type = "managedAutoDownload",
            Status = "MISSING_REQUIRED",
            Verification = "Pinned checksum",
            ChecksumStatus = "Pinned checksum",
            Family = "Disk",
            OsCategory = "Tools",
            Architecture = "amd64",
            BootMode = "windows",
            SourceTrust = "official",
            FreshnessStatus = "MinorUpdateAvailable",
            ChecksumVerificationMode = "github-asset-digest",
            SelectedForDownload = true
        };

        var plan = ToolkitWorkspacePlanner.BuildPlan([managed]);

        Assert.Equal("Ready to download", plan.Items[0].PlanSectionLabel);
        Assert.Equal("Update available", plan.Items[0].FreshnessLabel);
        Assert.Equal("Checksum verified", plan.Items[0].ChecksumLabel);
        Assert.Contains("Update available: 1", plan.ValidationSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPlan_UsesKnownSizesAndMarksUnknownSizes()
    {
        var known = Item("Clonezilla", "ISO\\Tools\\clonezilla.iso", selected: true, sizeBytes: 900L * 1024 * 1024);
        var unknown = Item("Windows 10", "ISO\\Windows\\DOWNLOAD - Windows 10.url", selected: true, manualOnly: true);

        var plan = ToolkitWorkspacePlanner.BuildPlan([known, unknown]);

        Assert.Equal(900L * 1024 * 1024, plan.Storage.KnownBytes);
        Assert.Equal(1, plan.Storage.KnownItemCount);
        Assert.Equal(1, plan.Storage.UnknownItemCount);
        Assert.Contains("unknown item", plan.Storage.CapacityWarningText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyProfile_SelectsMatchingEntriesAndClearsOthers()
    {
        var linux = Item("Linux", "ISO\\Linux\\linux.iso", selected: false);
        var windows = Item("Windows", "ISO\\Windows\\windows.iso", selected: true);
        var profile = new UsbWorkspaceProfile
        {
            Name = "Linux Admin Pack",
            SelectedEntries = [ToolkitWorkspacePlanner.GetSelectionId(linux)]
        };

        ToolkitWorkspacePlanner.ApplyProfile([linux, windows], profile);

        Assert.True(linux.SelectedForDownload);
        Assert.False(windows.SelectedForDownload);
    }

    [Fact]
    public void ProfileStore_SavesLoadsAndListsProfiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "ForgerEMS-profile-tests", Guid.NewGuid().ToString("N"));
        var store = new ToolkitWorkspaceProfileStore(root);
        var profile = new UsbWorkspaceProfile
        {
            Name = "Network Diagnostics Kit",
            Notes = "edge routers",
            TechnicianLabels = ["Network"],
            CategoryPreferences = ["Network-Appliance"],
            SelectedEntries = ["ISO\\Linux\\router.iso"]
        };

        var path = store.Save(profile);
        var loaded = store.TryLoad("Network Diagnostics Kit", out var fromDisk);

        Assert.True(File.Exists(path));
        Assert.True(loaded);
        Assert.Equal(ToolkitWorkspaceProfileStore.CurrentSchemaVersion, fromDisk.SchemaVersion);
        Assert.Contains("Network Diagnostics Kit", store.ListProfileNames());
        Assert.Contains("ISO\\Linux\\router.iso", fromDisk.SelectedEntries);
    }

    [Fact]
    public void ProfileMigration_ReadsLegacySelectedToolsExtension()
    {
        const string legacyJson = """
        {
          "schemaVersion": 0,
          "name": "Legacy profile",
          "selectedTools": [ "Windows 11", "SystemRescue" ]
        }
        """;
        var profile = JsonSerializer.Deserialize<UsbWorkspaceProfile>(
            legacyJson,
            CaseInsensitiveJsonOptions);

        var selected = ToolkitWorkspacePlanner.MigrateSelectedEntries(profile!);

        Assert.Contains("Windows 11", selected);
        Assert.Contains("SystemRescue", selected);
    }

    [Fact]
    public void BuildSelectedManagedManifest_IncludesOnlyEligibleManagedFileItems()
    {
        var root = TempRoot();
        var manifest = WriteManifest(root,
            FileItem("Safe ISO", "ISO\\Linux\\safe.iso", sha256: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
            PageItem("Windows 11", "ISO\\Windows\\DOWNLOAD - Windows 11.url", manualOnly: true),
            FileItem("Disabled Candidate", "ISO\\Linux\\disabled.iso", enabled: false, sha256Url: "https://vendor.example/SHA256SUMS"),
            FileItem("No Checksum", "ISO\\Linux\\nocheck.iso"),
            FileItem("Legacy Windows ISO", "ISO\\Windows-Legacy\\windows7.iso", family: "Windows", osCategory: "Legacy", legacyWarning: "Unsupported"),
            FileItem("Paid Tool", "Tools\\paid.zip", licenseNote: "Paid vendor license.", sha256Url: "https://vendor.example/SHA256SUMS"));
        var selected = new[]
        {
            Item("Safe ISO", "ISO\\Linux\\safe.iso", selected: true),
            Item("Windows 11", "ISO\\Windows\\DOWNLOAD - Windows 11.url", selected: true, manualOnly: true),
            Item("Disabled Candidate", "ISO\\Linux\\disabled.iso", selected: true),
            Item("No Checksum", "ISO\\Linux\\nocheck.iso", selected: true),
            Item("Legacy Windows ISO", "ISO\\Windows-Legacy\\windows7.iso", selected: true),
            Item("Paid Tool", "Tools\\paid.zip", selected: true)
        };

        var result = ToolkitWorkspacePlanner.BuildSelectedManagedManifest(
            manifest,
            Path.Combine(root, "selected.json"),
            selected);

        Assert.Equal(1, result.ReadyCount);
        Assert.Single(result.ManualItems);
        Assert.Equal(4, result.BlockedCount);
        Assert.Contains(result.BlockedItems, i => i.Status == SelectedManagedDownloadQueueStatus.SkippedDisabled);
        Assert.Contains(result.BlockedItems, i => i.Status == SelectedManagedDownloadQueueStatus.SkippedMissingChecksum);
        Assert.Contains(result.BlockedItems, i => i.Reason.Contains("Legacy", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.BlockedItems, i => i.Reason.Contains("Paid", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.QueueItems, i => i.Reason.StartsWith("Ready to download:", StringComparison.Ordinal));
        Assert.Contains(result.ManualItems, i => i.Reason.StartsWith("Manual required:", StringComparison.Ordinal));
        Assert.All(result.BlockedItems, i => Assert.Contains("Blocked / needs attention:", i.Reason, StringComparison.Ordinal));

        using var output = JsonDocument.Parse(File.ReadAllText(result.ManifestPath));
        var items = output.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Single(items);
        Assert.Equal("Safe ISO", items[0].GetProperty("name").GetString());
        Assert.Equal("selected-managed-downloads", output.RootElement.GetProperty("selectionMode").GetString());
    }

    [Fact]
    public void BuildSelectedManagedManifest_BlocksNonHttpsUrls()
    {
        var root = TempRoot();
        var manifest = WriteManifest(root,
            FileItem("Unsafe URL", "ISO\\Linux\\unsafe.iso", url: "http://vendor.example/unsafe.iso", sha256: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));
        var selected = new[] { Item("Unsafe URL", "ISO\\Linux\\unsafe.iso", selected: true) };

        var result = ToolkitWorkspacePlanner.BuildSelectedManagedManifest(
            manifest,
            Path.Combine(root, "selected.json"),
            selected);

        Assert.Empty(result.QueueItems);
        Assert.Contains(result.BlockedItems, item => item.Reason.Contains("HTTPS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildSelectedManagedManifest_QueuesSha512CoveredManagedItems()
    {
        var root = TempRoot();
        var manifest = WriteManifest(root,
            FileItem(
                "NetBSD 10.1 amd64 ISO",
                "ISO\\BSD\\NetBSD-10.1-amd64.iso",
                sha512Url: "https://cdn.netbsd.org/pub/NetBSD/images/10.1/SHA512"));
        var selected = new[] { Item("NetBSD 10.1 amd64 ISO", "ISO\\BSD\\NetBSD-10.1-amd64.iso", selected: true) };

        var result = ToolkitWorkspacePlanner.BuildSelectedManagedManifest(
            manifest,
            Path.Combine(root, "selected.json"),
            selected);

        Assert.Single(result.QueueItems);
        Assert.Empty(result.ManualItems);
        Assert.Empty(result.BlockedItems);
        Assert.Contains("Ready to download", result.QueueItems[0].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSelectedManagedManifest_IncludesBatch2PromotedEntriesFromLiveManifest()
    {
        var root = TempRoot();
        var manifest = FindRepoFile("manifests", "ForgerEMS.updates.json");
        var promotedNames = new[]
        {
            "Notepad++ 8.9.6 Portable (x64)",
            "System Informer 3.2.25011 Portable",
            "VeraCrypt 1.26.24 Setup (x64)",
            "PuTTY 0.83 64-bit Installer"
        };

        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        var selected = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .Where(item => promotedNames.Contains(GetJsonString(item, "name"), StringComparer.Ordinal))
            .Select(item => ItemFromManifest(item, selected: true))
            .ToArray();

        var result = ToolkitWorkspacePlanner.BuildSelectedManagedManifest(
            manifest,
            Path.Combine(root, "selected.json"),
            selected);

        Assert.Equal(promotedNames.Length, result.ReadyCount);
        Assert.Empty(result.ManualItems);
        Assert.Empty(result.BlockedItems);
        Assert.All(promotedNames, name => Assert.Contains(result.QueueItems, item => item.Name == name));

        using var output = JsonDocument.Parse(File.ReadAllText(result.ManifestPath));
        var outputNames = output.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .ToArray();
        Assert.All(promotedNames, name => Assert.Contains(name, outputNames));
    }

    [Fact]
    public void BuildSelectedManagedManifest_IncludesBatch4PromotedIsoEntriesFromLiveManifest()
    {
        var root = TempRoot();
        var manifest = FindRepoFile("manifests", "ForgerEMS.updates.json");
        var promotedNames = new[]
        {
            "Proxmox VE 9.2-1 ISO Installer",
            "Ubuntu Server 24.04.4 LTS (amd64)",
            "Debian GNU/Linux 13.5.0 netinst (amd64)",
            "Fedora Server 44-1.7 DVD (x86_64)",
            "FreeBSD 15.0-RELEASE amd64 disc1 ISO",
            "OpenBSD 7.9 amd64 install ISO",
            "Rocky Linux 10.1 Minimal (x86_64)",
            "AlmaLinux 10.2 Minimal (x86_64)"
        };

        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        var selected = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .Where(item => promotedNames.Contains(GetJsonString(item, "name"), StringComparer.Ordinal))
            .Select(item => ItemFromManifest(item, selected: true))
            .ToArray();

        var result = ToolkitWorkspacePlanner.BuildSelectedManagedManifest(
            manifest,
            Path.Combine(root, "selected.json"),
            selected);

        Assert.Equal(promotedNames.Length, result.ReadyCount);
        Assert.Empty(result.ManualItems);
        Assert.Empty(result.BlockedItems);
        Assert.All(promotedNames, name => Assert.Contains(result.QueueItems, item => item.Name == name));

        using var output = JsonDocument.Parse(File.ReadAllText(result.ManifestPath));
        var outputNames = output.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .ToArray();
        Assert.All(promotedNames, name => Assert.Contains(name, outputNames));
    }

    [Fact]
    public void BuildSelectedManagedManifest_ManualOnlyBatch2UnsafeCandidatesProduceInstructionsOnly()
    {
        var root = TempRoot();
        var manifest = FindRepoFile("manifests", "ForgerEMS.updates.json");
        var manualNames = new[]
        {
            "ReactOS Download Page",
            "KeePass Download Page",
            "TestDisk and PhotoRec Download Page",
            "Smartmontools Download Page"
        };

        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        var selected = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .Where(item => manualNames.Contains(GetJsonString(item, "name"), StringComparer.Ordinal))
            .Select(item => ItemFromManifest(item, selected: true))
            .ToArray();

        var result = ToolkitWorkspacePlanner.BuildSelectedManagedManifest(
            manifest,
            Path.Combine(root, "selected.json"),
            selected);

        Assert.Empty(result.QueueItems);
        Assert.Equal(manualNames.Length, result.ManualItems.Count);
        Assert.Empty(result.BlockedItems);
        Assert.All(manualNames, name => Assert.Contains(result.ManualItems, item => item.Name == name));
    }

    private static ToolkitHealthItemView Item(
        string name,
        string expectedPath,
        bool selected,
        long sizeBytes = 0,
        bool manualOnly = false)
    {
        return new ToolkitHealthItemView
        {
            Tool = name,
            Category = "Recovery",
            ExpectedPath = expectedPath,
            Type = manualOnly ? "manualDownload" : "managedAutoDownload",
            Status = manualOnly ? "MANUAL_REQUIRED" : "MISSING_REQUIRED",
            SizeBytes = sizeBytes,
            Verification = manualOnly ? "" : "Pinned checksum",
            ChecksumStatus = manualOnly ? "" : "Pinned checksum",
            Family = name.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "Windows" : "Linux",
            OsCategory = "Recovery",
            Architecture = "amd64",
            BootMode = "uefi, bios",
            SourceTrust = "official",
            ManualOnly = manualOnly,
            SelectedForDownload = selected
        };
    }

    private static ToolkitHealthItemView ItemFromManifest(JsonElement item, bool selected)
    {
        var manualOnly = item.TryGetProperty("manualOnly", out var manualOnlyElement) &&
                         manualOnlyElement.ValueKind == JsonValueKind.True;
        var type = GetJsonString(item, "type");

        return new ToolkitHealthItemView
        {
            Tool = GetJsonString(item, "name"),
            Category = GetJsonString(item, "family"),
            ExpectedPath = GetJsonString(item, "dest"),
            Type = manualOnly || type.Equals("page", StringComparison.OrdinalIgnoreCase)
                ? "manualDownload"
                : "managedAutoDownload",
            Status = manualOnly ? "MANUAL_REQUIRED" : "MISSING_REQUIRED",
            Verification = manualOnly ? "" : "Pinned checksum",
            ChecksumStatus = manualOnly ? "" : "Pinned checksum",
            Kind = GetJsonString(item, "kind"),
            Family = GetJsonString(item, "family"),
            OsCategory = GetJsonString(item, "osCategory"),
            Architecture = GetJsonString(item, "architecture"),
            RecommendedUse = GetJsonString(item, "recommendedUse"),
            TechnicianNotes = GetJsonString(item, "technicianNotes"),
            LicenseNote = GetJsonString(item, "licenseNote"),
            LegacyWarning = GetJsonString(item, "legacyWarning"),
            SourceTrust = GetJsonString(item, "sourceTrust"),
            ManualOnly = manualOnly,
            SelectedForDownload = selected
        };
    }

    private static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ForgerEMS-selected-download-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string WriteManifest(string root, params object[] items)
    {
        var path = Path.Combine(root, "manifest.json");
        var manifest = new
        {
            coreName = "ForgerEMS Test Core",
            coreVersion = "test",
            manifestVersion = 1,
            settings = new
            {
                downloadFolder = "_downloads",
                archiveFolder = "_archive",
                logFolder = "_logs",
                timeoutSec = 60,
                retryCount = 1,
                userAgent = "ForgerEMS-Test",
                maxArchivePerItem = 1
            },
            items
        };
        File.WriteAllText(path, JsonSerializer.Serialize(manifest));
        return path;
    }

    private static Dictionary<string, object?> FileItem(
        string name,
        string dest,
        bool enabled = true,
        string url = "https://vendor.example/file.iso",
        string sha256 = "",
        string sha256Url = "",
        string sha512 = "",
        string sha512Url = "",
        string family = "Linux",
        string osCategory = "Recovery",
        string licenseNote = "Free / open source.",
        string legacyWarning = "")
    {
        return new Dictionary<string, object?>
        {
            ["name"] = name,
            ["type"] = "file",
            ["dest"] = dest,
            ["url"] = url,
            ["enabled"] = enabled,
            ["sha256"] = sha256,
            ["sha256Url"] = sha256Url,
            ["sha512"] = sha512,
            ["sha512Url"] = sha512Url,
            ["family"] = family,
            ["osCategory"] = osCategory,
            ["licenseNote"] = licenseNote,
            ["sourceType"] = "official-mirror",
            ["fragilityLevel"] = "medium",
            ["fallbackRule"] = "Use official source.",
            ["maintenanceRank"] = 1,
            ["sourceTrust"] = "official",
            ["legacyWarning"] = legacyWarning
        };
    }

    private static Dictionary<string, object?> PageItem(string name, string dest, bool manualOnly)
    {
        return new Dictionary<string, object?>
        {
            ["name"] = name,
            ["type"] = "page",
            ["dest"] = dest,
            ["url"] = "https://vendor.example/download",
            ["enabled"] = true,
            ["manualOnly"] = manualOnly,
            ["sourceTrust"] = "official"
        };
    }

    private static string GetJsonString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Join(", ", value.EnumerateArray()
                .Where(static element => element.ValueKind == JsonValueKind.String)
                .Select(static element => element.GetString())),
            _ => string.Empty
        };
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
