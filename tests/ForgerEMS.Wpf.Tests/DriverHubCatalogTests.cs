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
            Assert.False(string.IsNullOrWhiteSpace(entry.EffectiveOfficialPageUrl), $"{entry.Name}: page url must not be empty.");
            Assert.True(DriverHubUrlSafety.IsSafeOfficialHttpUrl(entry.EffectiveOfficialPageUrl), $"{entry.Name}: page url must be safe HTTPS without device identifiers.");
            if (!string.IsNullOrWhiteSpace(entry.OfficialDownloadUrl))
            {
                Assert.True(DriverHubUrlSafety.IsSafeOfficialHttpUrl(entry.OfficialDownloadUrl), $"{entry.Name}: download url must be safe HTTPS without device identifiers.");
            }

            Assert.Equal(DriverHubConstants.OfficialVendorSourceTrust, entry.SourceTrust);
            Assert.NotEmpty(entry.Platforms);
            Assert.NotEmpty(entry.Tags);
            Assert.False(string.IsNullOrWhiteSpace(entry.PrimaryActionLabel), $"{entry.Name}: primary action label must not be empty.");
            Assert.False(string.IsNullOrWhiteSpace(entry.BrandTileText), $"{entry.Name}: brand tile text must not be empty.");
            Assert.StartsWith("#", entry.BrandAccentHex, StringComparison.Ordinal);
            Assert.EndsWith(".url", entry.UsbShortcutRelativePath, StringComparison.OrdinalIgnoreCase);
            Assert.False(Path.IsPathRooted(entry.UsbShortcutRelativePath), $"{entry.Name}: USB shortcut must be relative.");
        }
    }

    [Fact]
    public void CatalogEntries_HaveBrandTilesAndNoBundledVendorLogoImages()
    {
        foreach (var entry in DriverHubCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.BrandTileText), $"{entry.Name}: missing brand monogram.");
            Assert.InRange(entry.BrandTileText.Length, 2, 8);
        }

        var repoRoot = FindRepoRoot();
        var appRoot = Path.Combine(repoRoot.FullName, "src", "ForgerEMS.Wpf");
        var imageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".ico", ".jpg", ".jpeg", ".png", ".svg", ".webp"
        };
        var vendorNeedles = new[]
        {
            "nvidia", "intel", "amd", "dell", "hewlett", "hp", "lenovo",
            "msi", "asus", "acer", "realtek", "gigabyte", "asrock",
            "surface", "lvfs", "fwupd"
        };

        var possibleVendorLogoFiles = Directory.EnumerateFiles(appRoot, "*.*", SearchOption.AllDirectories)
            .Where(file => imageExtensions.Contains(Path.GetExtension(file)))
            .Where(file => vendorNeedles.Any(needle =>
                Path.GetFileNameWithoutExtension(file).Contains(needle, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.Empty(possibleVendorLogoFiles);
    }

    [Fact]
    public void CatalogEntries_ResolveExactlyOneHonestPrimaryAction()
    {
        var allowedLabels = new HashSet<string>(StringComparer.Ordinal)
        {
            "Get",
            "Download Installer",
            "Open Official Download",
            "Open Driver Page",
            "Open Support Page",
            "Open Guidance",
            "Open Firmware Guidance",
            "Open Official Page"
        };

        foreach (var view in DriverHubCatalog.All.Select(entry => new DriverHubEntryView(entry)))
        {
            Assert.Contains(view.PrimaryActionLabel, allowedLabels);
            Assert.Equal(DriverHubDisplay.BuildPrimaryActionLabel(view.Entry), view.PrimaryActionLabel);
            Assert.NotEqual(DriverHubPrimaryActionKind.AddShortcut, view.Entry.PrimaryActionKind);
            Assert.NotEqual(DriverHubPrimaryActionKind.Unavailable, view.Entry.PrimaryActionKind);
        }
    }

    [Fact]
    public void CatalogEntries_DoNotExposeMisleadingDownloadActions()
    {
        foreach (var view in DriverHubCatalog.All.Select(entry => new DriverHubEntryView(entry)))
        {
            Assert.NotEqual("Download", view.PrimaryActionLabel);
            Assert.NotEqual("Install", view.PrimaryActionLabel);
            Assert.NotEqual("Download Official App", view.PrimaryActionLabel);

            Assert.Equal(
                view.Entry.PrimaryActionKind == DriverHubPrimaryActionKind.DownloadInstaller,
                view.PrimaryActionLabel.Equals("Download Installer", StringComparison.Ordinal));
            Assert.False(view.CanShowDownloadInstaller, $"{view.Name}: catalog has no direct installer download entries in this pass.");

            if (view.PrimaryActionLabel.Equals("Get", StringComparison.Ordinal))
            {
                Assert.True(view.CanShowDownloadOfficialApp, $"{view.Name}: Get requires official app/store metadata.");
                Assert.True(
                    view.Entry.CanOpenOfficialDownload || view.Entry.CanOpenMicrosoftStore,
                    $"{view.Name}: Get requires an official app or store URL.");
                Assert.True(view.Entry.DownloadKind is DriverHubDownloadKind.OfficialAppPage or DriverHubDownloadKind.MicrosoftStore);
            }

            if (view.Entry.DownloadKind == DriverHubDownloadKind.DriverSearchPage)
            {
                Assert.Equal("Open Driver Page", view.PrimaryActionLabel);
            }

            if (view.Entry.DownloadKind == DriverHubDownloadKind.OemSupportPage)
            {
                Assert.Equal("Open Support Page", view.PrimaryActionLabel);
                Assert.True(view.Entry.RequiresModelLookup);
            }
        }
    }

    [Fact]
    public void DownloadInstallerLabel_RequiresSafeDirectInstallerMetadata()
    {
        var directInstaller = new DriverHubEntry
        {
            Name = "Safe Vendor Installer",
            Vendor = "NVIDIA",
            Category = DriverHubCategory.Gpu,
            OfficialPageUrl = "https://www.nvidia.com/en-us/software/nvidia-app/",
            OfficialDownloadUrl = "https://www.nvidia.com/downloads/nvidia-app.exe",
            DownloadKind = DriverHubDownloadKind.OfficialAppInstaller,
            PrimaryActionKind = DriverHubPrimaryActionKind.DownloadInstaller,
            CanDirectDownloadInstaller = true,
            IsInstallerDownload = true,
            InstallerFileName = "nvidia-app.exe"
        };

        Assert.True(DriverHubUrlSafety.IsSafeOfficialInstallerDownloadUrl(directInstaller.OfficialDownloadUrl));
        Assert.Equal("Download Installer", DriverHubDisplay.BuildPrimaryActionLabel(directInstaller));

        foreach (var entry in DriverHubCatalog.All)
        {
            Assert.False(entry.CanDirectDownloadInstaller, $"{entry.Name}: catalog should not claim a direct installer without safe direct metadata.");
            Assert.NotEqual("Download Installer", new DriverHubEntryView(entry).PrimaryActionLabel);
        }
    }

    [Fact]
    public void UrlSafety_BlocksUnsafeDirectInstallerUrls()
    {
        Assert.True(DriverHubUrlSafety.IsSafeOfficialInstallerDownloadUrl("https://www.nvidia.com/downloads/nvidia-app.exe"));
        Assert.False(DriverHubUrlSafety.IsSafeOfficialInstallerDownloadUrl("http://www.nvidia.com/downloads/nvidia-app.exe"));
        Assert.False(DriverHubUrlSafety.IsSafeOfficialInstallerDownloadUrl("https://downloads.example.com/nvidia-app.exe"));
        Assert.False(DriverHubUrlSafety.IsSafeOfficialInstallerDownloadUrl("https://www.dell.com/support/package.exe?servicetag=ABC123"));
        Assert.False(DriverHubUrlSafety.IsSafeOfficialInstallerDownloadUrl("https://www.intel.com/download/dsa.exe?token=secret"));
        Assert.False(DriverHubUrlSafety.IsSafeOfficialInstallerDownloadUrl("https://www.amd.com/download/adrenalin.exe?utm_source=test"));
        Assert.False(DriverHubUrlSafety.IsSafeOfficialInstallerDownloadUrl("https://www.lenovo.com/download/readme.txt"));
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
            Assert.Contains("Firmware guidance only", new DriverHubEntryView(card).FirmwareBadgeText, StringComparison.Ordinal);
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
            Assert.Equal(DriverHubDownloadKind.LinuxGuidance, card.DownloadKind);
            var expectedLabel = card.IsFirmwareRelated ? "Open Firmware Guidance" : "Open Guidance";
            Assert.Equal(expectedLabel, new DriverHubEntryView(card).PrimaryActionLabel);
            Assert.DoesNotContain(card.Tags, tag => tag.Equals("auto-install", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(card.Description, "auto-install", StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void RecommendedCards_ExposeSameActionMetadataAsCatalogCards()
    {
        var profile = new SystemProfile
        {
            Manufacturer = "Dell Inc.",
            Model = "XPS 15",
            Cpu = "Intel Core i7",
            Gpus = new[] { new SystemGpuProfile { Name = "NVIDIA GeForce RTX 4070" } }
        };

        var featured = DriverHubRecommendationPresentation.SelectFeaturedRecommendations(
            DriverHubRecommendationEngine.Recommend(DriverHubCatalog.All, profile));

        Assert.InRange(featured.Count, 3, 4);
        foreach (var recommendation in featured)
        {
            var catalogView = new DriverHubEntryView(recommendation.Entry);
            var recommendedView = new DriverHubEntryView(recommendation.Entry)
            {
                IsRecommended = true,
                RecommendationStatusText = recommendation.StatusText
            };

            Assert.True(recommendedView.IsRecommended);
            Assert.Equal(catalogView.PrimaryActionLabel, recommendedView.PrimaryActionLabel);
            Assert.Equal(catalogView.OfficialUrl, recommendedView.OfficialUrl);
            Assert.Equal(catalogView.OfficialDownloadUrl, recommendedView.OfficialDownloadUrl);
            Assert.Equal(catalogView.BrandTileText, recommendedView.BrandTileText);
            Assert.Equal(catalogView.PlatformBadgesText, recommendedView.PlatformBadgesText);
            Assert.True(recommendedView.Entry.CanAddShortcutToUsb);
            Assert.False(string.IsNullOrWhiteSpace(recommendedView.RecommendationStatusText));
        }
    }

    [Fact]
    public void FeaturedRecommendations_PrioritizeDetectedAppAndSupportCards()
    {
        var profile = new SystemProfile
        {
            Manufacturer = "Dell Inc.",
            Model = "XPS 15",
            Cpu = "Intel Core i7",
            Gpus = new[] { new SystemGpuProfile { Name = "NVIDIA GeForce RTX 4070" } }
        };

        var ids = DriverHubRecommendationPresentation.SelectFeaturedRecommendations(
                DriverHubRecommendationEngine.Recommend(DriverHubCatalog.All, profile))
            .Select(recommendation => recommendation.Entry.Id)
            .ToArray();

        Assert.InRange(ids.Length, 3, 4);
        Assert.Equal("nvidia-app", ids[0]);
        Assert.Contains("intel-dsa", ids);
        Assert.Contains("dell-supportassist", ids);
        Assert.Contains("dell-drivers", ids);
        Assert.True(Array.IndexOf(ids, "dell-supportassist") < Array.IndexOf(ids, "dell-drivers"));
    }

    [Fact]
    public void DellNvidiaIntelRecommendations_UseStoreStylePrimaryActions()
    {
        var profile = new SystemProfile
        {
            Manufacturer = "Dell Inc.",
            Model = "Precision 5540",
            Cpu = "Intel Core i7",
            OperatingSystem = "Windows 11",
            Gpus = new[]
            {
                new SystemGpuProfile { Name = "Intel UHD Graphics" },
                new SystemGpuProfile { Name = "NVIDIA Quadro T2000" }
            }
        };

        var actions = DriverHubRecommendationPresentation.SelectFeaturedRecommendations(
                DriverHubRecommendationEngine.Recommend(DriverHubCatalog.All, profile))
            .ToDictionary(
                item => item.Entry.Id,
                item => new DriverHubEntryView(item.Entry).PrimaryActionLabel,
                StringComparer.OrdinalIgnoreCase);

        Assert.Equal("Get", actions["nvidia-app"]);
        Assert.Equal("Get", actions["intel-dsa"]);
        Assert.Equal("Get", actions["dell-supportassist"]);
        Assert.Equal("Open Support Page", actions["dell-drivers"]);
        Assert.DoesNotContain(actions.Keys, id => id.Contains("surface", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SecondaryOpenPage_IsHiddenWhenItDuplicatesPrimaryUrl()
    {
        foreach (var view in DriverHubCatalog.All.Select(entry => new DriverHubEntryView(entry)))
        {
            var sameUrl = string.Equals(view.OfficialUrl, view.EffectivePrimaryUrl, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(!sameUrl, view.HasDistinctSecondaryPageAction);
        }
    }

    [Fact]
    public void RecommendationStatusCopy_DoesNotUseVersionOrNecessityClaims()
    {
        var profiles = new[]
        {
            new SystemProfile { Manufacturer = "Dell Inc.", Cpu = "Intel Core i7", Gpus = new[] { new SystemGpuProfile { Name = "NVIDIA GeForce RTX 4070" } } },
            new SystemProfile { Manufacturer = "HP", Cpu = "AMD Ryzen", Gpus = new[] { new SystemGpuProfile { Name = "AMD Radeon RX 7800 XT" } } },
            new SystemProfile { Manufacturer = "Lenovo", OperatingSystem = "Ubuntu Linux 24.04" },
            new SystemProfile { Manufacturer = "ASUSTeK COMPUTER INC.", Gpus = new[] { new SystemGpuProfile { Name = "Intel Arc A770" } } }
        };
        var blockedWords = new[] { "needed", "outdated", "latest installed", "required" };

        foreach (var profile in profiles)
        {
            foreach (var recommendation in DriverHubRecommendationEngine.Recommend(DriverHubCatalog.All, profile))
            {
                foreach (var blockedWord in blockedWords)
                {
                    Assert.DoesNotContain(blockedWord, recommendation.StatusText, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        foreach (var rule in DriverHubCatalog.All.SelectMany(entry => entry.MatchRules))
        {
            foreach (var blockedWord in blockedWords)
            {
                Assert.DoesNotContain(blockedWord, rule.StatusText, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void SurfaceEntry_UsesVerifiedNon404OfficialPage()
    {
        // surface-models-msi (the old URL slug) returns 404 on learn.microsoft.com. The fixed
        // URL points to the Microsoft Support page that is still live and home-user friendly.
        // This test prevents a silent regression back to the dead URL.
        var surface = DriverHubCatalog.All.Single(entry => entry.Id == "surface-drivers-firmware");
        Assert.DoesNotContain("surface-models-msi", surface.EffectiveOfficialPageUrl, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("https://", surface.EffectiveOfficialPageUrl, StringComparison.OrdinalIgnoreCase);
        Assert.True(DriverHubUrlSafety.IsSafeOfficialHttpUrl(surface.EffectiveOfficialPageUrl));
    }

    [Fact]
    public void Catalog_AllPrimaryUrlsAreReachableSafeAndNotKnownDead()
    {
        // Lightweight, network-free contract check: every catalog entry must produce a primary
        // URL that the URL-safety gate would accept (HTTPS, no identifier-bearing query). This
        // mirrors what ExecuteDriverHubPrimaryAction enforces at runtime so a misconfigured
        // entry can never reach the click handler.
        string[] knownDeadFragments =
        {
            "surface-models-msi" // confirmed 404 on learn.microsoft.com
        };

        foreach (var entry in DriverHubCatalog.All)
        {
            Assert.True(
                DriverHubUrlSafety.IsSafeOfficialHttpUrl(entry.EffectivePrimaryUrl),
                $"{entry.Name}: EffectivePrimaryUrl '{entry.EffectivePrimaryUrl}' must be safe HTTPS.");
            foreach (var dead in knownDeadFragments)
            {
                Assert.DoesNotContain(
                    dead,
                    entry.EffectivePrimaryUrl,
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(
                    dead,
                    entry.EffectiveOfficialPageUrl,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Catalog_RequiresModelLookupCardsUseHonestLabels()
    {
        // Cards that need exact model lookup (Surface firmware, every OEM portal) must not pretend
        // they download something — their primary label has to read like an open/support action.
        string[] allowedLabels =
        {
            "Open Support Page",
            "Open Driver Page",
            "Open Firmware Guidance",
            "Open Guidance",
            "Open Official Page",
            "Open Official Download",
            "Get"
        };

        foreach (var entry in DriverHubCatalog.All.Where(item => item.RequiresModelLookup))
        {
            var view = new DriverHubEntryView(entry);
            Assert.Contains(view.PrimaryActionLabel, allowedLabels);
            Assert.NotEqual("Download Installer", view.PrimaryActionLabel);
        }
    }

    [Fact]
    public void UsbShortcutEntries_RemainAvailableForOverflowMenu()
    {
        // Add Shortcut moved from the primary action row into the overflow popup. The underlying
        // capability must still be available on every catalog entry so the menu item is meaningful.
        foreach (var entry in DriverHubCatalog.All)
        {
            Assert.True(entry.CanAddShortcutToUsb, $"{entry.Name}: USB shortcut capability must be available for the overflow menu.");
            Assert.False(string.IsNullOrWhiteSpace(entry.UsbShortcutRelativePath), $"{entry.Name}: shortcut path required for overflow menu.");
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
        Assert.Contains(DriverHubFilterEngine.Filter(views, "Driver Apps", string.Empty), entry => entry.Id == "intel-dsa");
        Assert.Contains(DriverHubFilterEngine.Filter(views, "Driver Apps", string.Empty), entry => entry.Id == "dell-supportassist");
        Assert.DoesNotContain(DriverHubFilterEngine.Filter(views, "Driver Apps", string.Empty), entry => entry.Id == "dell-drivers");
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

    private static DirectoryInfo FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ForgerEMS.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
