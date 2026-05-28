using System;
using System.IO;
using System.Linq;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class DriverHubCatalogTests
{
    [Fact]
    public void Catalog_ContainsRequiredCoreEntries()
    {
        string[] requiredNames =
        {
            "NVIDIA App",
            "NVIDIA Official Drivers / GeForce Drivers",
            "NVIDIA Studio Driver",
            "AMD Software: Adrenalin Edition",
            "AMD Drivers and Support",
            "Intel Driver & Support Assistant",
            "Intel Download Center",
            "Dell Drivers & Downloads",
            "Dell SupportAssist",
            "HP Drivers & Software",
            "HP Support Assistant",
            "Lenovo Vantage",
            "Lenovo System Update / Drivers & Software",
            "MSI Center",
            "MSI Support / Downloads",
            "ASUS MyASUS",
            "ASUS Download Center",
            "Acer Support / Drivers",
            "Microsoft Surface Drivers and Firmware",
            "Gigabyte Support / Downloads",
            "ASRock Support / Downloads",
            "Realtek Audio and Network Downloads",
            "Intel Killer Networking",
            "fwupd / LVFS Firmware Guidance"
        };

        var names = DriverHubCatalog.All.Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var name in requiredNames)
        {
            Assert.Contains(name, names);
        }
    }

    [Fact]
    public void CatalogEntries_AreOfficialCompleteHttpsShortcuts()
    {
        foreach (var entry in DriverHubCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Id), $"{entry.Name}: id must not be empty.");
            Assert.False(string.IsNullOrWhiteSpace(entry.Name), $"{entry.Id}: name must not be empty.");
            Assert.False(string.IsNullOrWhiteSpace(entry.Vendor), $"{entry.Name}: vendor must not be empty.");
            Assert.False(string.IsNullOrWhiteSpace(DriverHubDisplay.FormatCategory(entry.Category)), $"{entry.Name}: category must not be empty.");
            Assert.False(string.IsNullOrWhiteSpace(entry.OfficialUrl), $"{entry.Name}: url must not be empty.");
            Assert.True(DriverHubUrlSafety.IsSafeOfficialHttpUrl(entry.OfficialUrl), $"{entry.Name}: url must be safe HTTPS without device identifiers.");
            Assert.Equal(DriverHubConstants.OfficialVendorSourceTrust, entry.SourceTrust);
            Assert.NotEmpty(entry.Platforms);
            Assert.NotEmpty(entry.Tags);
            Assert.EndsWith(".url", entry.UsbShortcutRelativePath, StringComparison.OrdinalIgnoreCase);
            Assert.False(Path.IsPathRooted(entry.UsbShortcutRelativePath), $"{entry.Name}: USB shortcut must be relative.");
        }
    }

    [Theory]
    [InlineData("Dell Inc.", "XPS 13", "dell-drivers", "dell-supportassist")]
    [InlineData("HP", "EliteBook", "hp-drivers", "hp-support-assistant")]
    [InlineData("Hewlett-Packard", "ProBook", "hp-drivers", "hp-support-assistant")]
    [InlineData("Lenovo", "ThinkPad", "lenovo-vantage", "lenovo-system-update")]
    [InlineData("Micro-Star International", "MSI Laptop", "msi-center", "msi-support")]
    [InlineData("ASUSTeK COMPUTER INC.", "ROG", "asus-myasus", "asus-download-center")]
    [InlineData("Acer", "Swift", "acer-support", null)]
    [InlineData("Microsoft Corporation", "Surface Laptop", "surface-drivers-firmware", null)]
    public void RecommendationEngine_RecommendsOemSupportForDetectedManufacturer(
        string manufacturer,
        string model,
        string expectedId1,
        string? expectedId2)
    {
        var profile = new SystemProfile { Manufacturer = manufacturer, Model = model };
        var ids = DriverHubRecommendationEngine.Recommend(DriverHubCatalog.All, profile)
            .Select(item => item.Entry.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(expectedId1, ids);
        if (expectedId2 is not null)
        {
            Assert.Contains(expectedId2, ids);
        }
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4070", "nvidia-app", "nvidia-geforce-drivers")]
    [InlineData("AMD Radeon RX 7800 XT", "amd-adrenalin", "amd-drivers-support")]
    [InlineData("Radeon Graphics", "amd-adrenalin", "amd-drivers-support")]
    [InlineData("Intel Arc A770", "intel-dsa", "intel-download-center")]
    public void RecommendationEngine_RecommendsGpuVendorEntries(string gpuName, string expectedId1, string expectedId2)
    {
        var profile = new SystemProfile
        {
            Gpus = new[] { new SystemGpuProfile { Name = gpuName } }
        };

        var ids = DriverHubRecommendationEngine.Recommend(DriverHubCatalog.All, profile)
            .Select(item => item.Entry.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(expectedId1, ids);
        Assert.Contains(expectedId2, ids);
    }

    [Fact]
    public void RecommendationEngine_RecommendsIntelDsaAndDownloadCenterForIntelCpuOrNetwork()
    {
        var cpuProfile = new SystemProfile { Cpu = "Intel Core i7-13700K" };
        var cpuIds = DriverHubRecommendationEngine.Recommend(DriverHubCatalog.All, cpuProfile)
            .Select(item => item.Entry.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("intel-dsa", cpuIds);
        Assert.Contains("intel-download-center", cpuIds);

        var networkProfile = new SystemProfile
        {
            Cpu = "Unknown CPU",
            ObviousProblems = new[] { "Intel Killer Wi-Fi adapter driver needs manual OEM review." }
        };
        var networkIds = DriverHubRecommendationEngine.Recommend(DriverHubCatalog.All, networkProfile)
            .Select(item => item.Entry.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("intel-dsa", networkIds);
        Assert.Contains("intel-download-center", networkIds);
        Assert.Contains("intel-killer-networking", networkIds);
    }

    [Fact]
    public void RecommendationEngine_SurfacesLinuxGuidanceForLinuxPlatformOrFilter()
    {
        var linuxProfile = new SystemProfile { OperatingSystem = "Ubuntu Linux 24.04" };
        var linuxIds = DriverHubRecommendationEngine.Recommend(DriverHubCatalog.All, linuxProfile)
            .Select(item => item.Entry.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fwupd-lvfs", linuxIds);
        Assert.Contains("nvidia-linux-drivers", linuxIds);

        var windowsProfile = new SystemProfile { OperatingSystem = "Microsoft Windows 11" };
        var filterIds = DriverHubRecommendationEngine.Recommend(DriverHubCatalog.All, windowsProfile, linuxFilterRequested: true)
            .Select(item => item.Entry.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("amd-linux-drivers", filterIds);
        Assert.Contains("ubuntu-additional-drivers", filterIds);
    }

    [Fact]
    public void FirmwareCards_CarrySafetyWarning()
    {
        var firmwareCards = DriverHubCatalog.All
            .Where(entry => entry.IsFirmwareRelated || entry.Category == DriverHubCategory.BiosFirmware || entry.Platforms.Contains(DriverHubPlatform.BiosFirmware))
            .ToArray();

        Assert.NotEmpty(firmwareCards);
        foreach (var card in firmwareCards)
        {
            Assert.Contains(DriverHubConstants.FirmwareSafetyWarning, card.SafetyNote, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void LinuxCards_AreGuidanceManualLinksNotAutoInstallers()
    {
        var linuxCards = DriverHubCatalog.All.Where(entry => entry.Category == DriverHubCategory.LinuxDrivers).ToArray();
        Assert.NotEmpty(linuxCards);

        foreach (var card in linuxCards)
        {
            Assert.True(card.IsLinuxGuidance, $"{card.Name} should be marked Linux guidance.");
            Assert.True(card.IsManualVendorPortal, $"{card.Name} should be manual guidance.");
            Assert.DoesNotContain(card.Tags, tag => tag.Equals("auto-install", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(card.Description, "auto-install", StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void UsbShortcut_CreatesExpectedUrlPathInsideRoot()
    {
        var root = Directory.CreateTempSubdirectory("forgerems-driverhub-");
        try
        {
            var entry = DriverHubCatalog.All.Single(item => item.Id == "nvidia-app");
            var result = DriverHubUsbShortcutService.CreateShortcut(root.FullName, entry);

            Assert.True(result.Succeeded, result.Message);
            Assert.Equal(@"Drivers\Graphics\DOWNLOAD - NVIDIA App.url", result.RelativePath);
            Assert.True(File.Exists(result.FullPath));
            Assert.StartsWith(root.FullName, Path.GetFullPath(result.FullPath), StringComparison.OrdinalIgnoreCase);
            var content = File.ReadAllText(result.FullPath);
            Assert.Contains("[InternetShortcut]", content, StringComparison.Ordinal);
            Assert.Contains("URL=https://www.nvidia.com/en-us/software/nvidia-app/", content, StringComparison.Ordinal);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void UsbShortcut_FailsSafelyWithoutUsbTargetOrEscapingPath()
    {
        var entry = DriverHubCatalog.All.Single(item => item.Id == "dell-drivers");
        var missingTarget = DriverHubUsbShortcutService.CreateShortcut(null, entry);
        Assert.False(missingTarget.Succeeded);
        Assert.Contains("Select a USB target first.", missingTarget.Message, StringComparison.Ordinal);

        var root = Directory.CreateTempSubdirectory("forgerems-driverhub-escape-");
        try
        {
            var escapingEntry = new DriverHubEntry
            {
                Id = "escape",
                Name = "Escape",
                Vendor = "Vendor",
                Category = DriverHubCategory.ManualVendorPortals,
                Description = "Invalid test entry.",
                OfficialUrl = "https://www.dell.com/support/home",
                Platforms = new[] { DriverHubPlatform.Windows },
                Tags = new[] { "test" },
                UsbShortcutRelativePath = @"..\escape.url"
            };

            var result = DriverHubUsbShortcutService.CreateShortcut(root.FullName, escapingEntry);
            Assert.False(result.Succeeded);
            Assert.Contains("escaped the USB root", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void FilterEngine_ReturnsExpectedCategoriesAndSearchResults()
    {
        var views = DriverHubCatalog.All.Select(entry => new DriverHubEntryView(entry)).ToArray();
        var recommended = views.Single(entry => entry.Id == "nvidia-app");
        recommended.IsRecommended = true;

        Assert.Contains(DriverHubFilterEngine.Filter(views, "Recommended", string.Empty), entry => entry.Id == "nvidia-app");
        Assert.Contains(DriverHubFilterEngine.Filter(views, "GPU", string.Empty), entry => entry.Id == "amd-adrenalin");
        Assert.Contains(DriverHubFilterEngine.Filter(views, "OEM", string.Empty), entry => entry.Id == "dell-drivers");
        Assert.Contains(DriverHubFilterEngine.Filter(views, "Network", string.Empty), entry => entry.Id == "realtek-downloads");
        Assert.Contains(DriverHubFilterEngine.Filter(views, "Chipset", string.Empty), entry => entry.Id == "intel-download-center");
        Assert.Contains(DriverHubFilterEngine.Filter(views, "BIOS/Firmware", string.Empty), entry => entry.Id == "fwupd-lvfs");
        Assert.Contains(DriverHubFilterEngine.Filter(views, "Linux", string.Empty), entry => entry.Id == "ubuntu-additional-drivers");
        Assert.Contains(DriverHubFilterEngine.Filter(views, "Windows", string.Empty), entry => entry.Id == "hp-support-assistant");
        Assert.Contains(DriverHubFilterEngine.Filter(views, "All", "Surface"), entry => entry.Id == "surface-drivers-firmware");
        Assert.Empty(DriverHubFilterEngine.Filter(views, "All", "no-such-driver-card"));
    }
}
