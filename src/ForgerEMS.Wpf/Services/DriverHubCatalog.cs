#pragma warning disable CA1859, CA1861 // Seed catalog readability is more important than micro-optimizing literal arrays.
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services;

public static class DriverHubCatalog
{
    public static IReadOnlyList<DriverHubEntry> All { get; } = BuildCatalog();

    private static IReadOnlyList<DriverHubEntry> BuildCatalog()
    {
        var entries = new List<DriverHubEntry>
        {
            Entry(
                "nvidia-app",
                "NVIDIA App",
                "NVIDIA",
                DriverHubCategory.Gpu,
                "Official NVIDIA desktop app for GeForce driver notifications, graphics settings, and GPU utilities.",
                "https://www.nvidia.com/en-us/software/nvidia-app/",
                new[] { DriverHubPlatform.Windows, DriverHubPlatform.Gpu, DriverHubPlatform.Utility },
                new[] { "gpu", "graphics", "geforce", "nvidia", "windows" },
                @"Drivers\Graphics\DOWNLOAD - NVIDIA App.url",
                officialDownloadUrl: "https://www.nvidia.com/en-us/software/nvidia-app/",
                downloadKind: DriverHubDownloadKind.OfficialAppPage,
                rules: new[]
                {
                    GpuRule("nvidia", "Recommended based on detected NVIDIA GPU"),
                    GpuRule("geforce", "Recommended based on detected NVIDIA GPU"),
                    GpuRule("quadro", "Recommended based on detected NVIDIA GPU"),
                    GpuRule("rtx", "Recommended based on detected NVIDIA GPU"),
                    GpuRule("gtx", "Recommended based on detected NVIDIA GPU")
                }),
            Entry(
                "nvidia-geforce-drivers",
                "NVIDIA Official Drivers / GeForce Drivers",
                "NVIDIA",
                DriverHubCategory.Gpu,
                "Official NVIDIA driver lookup for GeForce, RTX, Quadro, Studio, and related graphics products.",
                "https://www.nvidia.com/en-us/geforce/drivers/",
                new[] { DriverHubPlatform.Windows, DriverHubPlatform.Gpu, DriverHubPlatform.ManualPortal },
                new[] { "gpu", "graphics", "geforce", "driver", "nvidia" },
                @"Drivers\Graphics\DOWNLOAD - NVIDIA Drivers.url",
                downloadKind: DriverHubDownloadKind.DriverSearchPage,
                isManualVendorPortal: true,
                rules: new[] { GpuRule("nvidia", "Recommended based on detected NVIDIA GPU") }),
            Entry(
                "nvidia-studio-drivers",
                "NVIDIA Studio Driver",
                "NVIDIA",
                DriverHubCategory.Gpu,
                "Official NVIDIA Studio software and driver page for creator-focused driver guidance.",
                "https://www.nvidia.com/en-us/studio/software/",
                new[] { DriverHubPlatform.Windows, DriverHubPlatform.Gpu, DriverHubPlatform.ManualPortal },
                new[] { "gpu", "graphics", "studio", "creator", "nvidia" },
                @"Drivers\Graphics\DOWNLOAD - NVIDIA Studio Driver.url",
                downloadKind: DriverHubDownloadKind.DriverSearchPage,
                isManualVendorPortal: true,
                rules: new[] { GpuRule("nvidia", "Recommended based on detected NVIDIA GPU") }),
            Entry(
                "amd-adrenalin",
                "AMD Software: Adrenalin Edition",
                "AMD",
                DriverHubCategory.Gpu,
                "Official AMD Radeon software page for Windows GPU driver and utility guidance.",
                "https://www.amd.com/en/products/software/adrenalin.html",
                new[] { DriverHubPlatform.Windows, DriverHubPlatform.Gpu, DriverHubPlatform.Utility },
                new[] { "gpu", "graphics", "radeon", "amd", "adrenalin", "windows" },
                @"Drivers\Graphics\DOWNLOAD - AMD Software Adrenalin.url",
                officialDownloadUrl: "https://www.amd.com/en/products/software/adrenalin.html",
                downloadKind: DriverHubDownloadKind.OfficialAppPage,
                rules: new[]
                {
                    GpuRule("amd", "Recommended based on detected AMD/Radeon GPU"),
                    GpuRule("radeon", "Recommended based on detected AMD/Radeon GPU")
                }),
            Entry(
                "amd-drivers-support",
                "AMD Drivers and Support",
                "AMD",
                DriverHubCategory.Gpu,
                "Official AMD product support picker for Radeon graphics, chipsets, processors, and Linux driver pages.",
                "https://www.amd.com/en/support/download/drivers.html",
                new[] { DriverHubPlatform.Windows, DriverHubPlatform.Linux, DriverHubPlatform.Gpu, DriverHubPlatform.Chipset, DriverHubPlatform.ManualPortal },
                new[] { "gpu", "graphics", "radeon", "amd", "chipset", "linux", "windows" },
                @"Drivers\Graphics\DOWNLOAD - AMD Drivers.url",
                downloadKind: DriverHubDownloadKind.DriverSearchPage,
                isManualVendorPortal: true,
                rules: new[]
                {
                    GpuRule("amd", "Recommended based on detected AMD/Radeon GPU"),
                    GpuRule("radeon", "Recommended based on detected AMD/Radeon GPU"),
                    CpuRule("amd", "Recommended based on detected AMD CPU")
                }),
            Entry(
                "intel-dsa",
                "Intel Driver & Support Assistant",
                "Intel",
                DriverHubCategory.ChipsetStorageNetwork,
                "Official Intel assistant for supported Intel hardware on Windows. It may not cover OEM-customized devices.",
                "https://www.intel.com/content/www/us/en/support/detect.html",
                new[] { DriverHubPlatform.Windows, DriverHubPlatform.Chipset, DriverHubPlatform.Network, DriverHubPlatform.Gpu, DriverHubPlatform.Utility },
                new[] { "intel", "dsa", "chipset", "network", "wireless", "graphics", "windows" },
                @"Drivers\Chipset\DOWNLOAD - Intel Driver Support Assistant.url",
                officialDownloadUrl: "https://www.intel.com/content/www/us/en/support/detect.html",
                downloadKind: DriverHubDownloadKind.OfficialAppPage,
                rules: new[]
                {
                    CpuRule("intel", "Recommended based on detected Intel CPU"),
                    GpuRule("intel", "Recommended based on detected Intel GPU"),
                    NetworkRule("intel", "Recommended based on detected Intel network vendor"),
                    NetworkRule("killer", "Recommended based on detected Intel Killer network vendor")
                }),
            Entry(
                "intel-download-center",
                "Intel Download Center",
                "Intel",
                DriverHubCategory.ChipsetStorageNetwork,
                "Official Intel download center for chipsets, graphics, wireless, Ethernet, storage, BIOS, firmware, and tools.",
                "https://www.intel.com/content/www/us/en/download-center/home.html",
                new[] { DriverHubPlatform.Windows, DriverHubPlatform.Linux, DriverHubPlatform.Chipset, DriverHubPlatform.Network, DriverHubPlatform.Storage, DriverHubPlatform.Gpu, DriverHubPlatform.ManualPortal },
                new[] { "intel", "download center", "chipset", "storage", "network", "wireless", "ethernet", "graphics", "linux" },
                @"Drivers\Chipset\DOWNLOAD - Intel Drivers.url",
                downloadKind: DriverHubDownloadKind.DriverSearchPage,
                isManualVendorPortal: true,
                rules: new[]
                {
                    CpuRule("intel", "Recommended based on detected Intel CPU"),
                    GpuRule("intel", "Recommended based on detected Intel GPU"),
                    NetworkRule("intel", "Recommended based on detected Intel network vendor"),
                    NetworkRule("killer", "Recommended based on detected Intel Killer network vendor")
                }),

            OemEntry("dell-drivers", "Dell Drivers & Downloads", "Dell", "Official Dell support home for service-tag or model-based driver, BIOS, and firmware lookup.", "https://www.dell.com/support/home", @"Drivers\Vendor\DOWNLOAD - Dell Support.url", "dell"),
            OemAppEntry("dell-supportassist", "Dell SupportAssist", "Dell", "Official Dell SupportAssist page for Dell PC support, diagnostics, and driver update assistance.", "https://www.dell.com/support/contents/en-us/article/product-support/self-support-knowledgebase/software-and-downloads/support-assist/SupportAssist-for-Home", @"Drivers\Vendor\DOWNLOAD - Dell SupportAssist.url", "dell"),
            OemEntry("hp-drivers", "HP Drivers & Software", "HP", "Official HP Software and Drivers portal for model-based driver, software, and firmware lookup.", "https://support.hp.com/us-en/drivers", @"Drivers\Vendor\DOWNLOAD - HP Support.url", "hp", "hewlett-packard"),
            OemAppEntry("hp-support-assistant", "HP Support Assistant", "HP", "Official HP Support Assistant page for Windows support and HP device update assistance.", "https://support.hp.com/us-en/help/hp-support-assistant", @"Drivers\Vendor\DOWNLOAD - HP Support Assistant.url", "hp", "hewlett-packard"),
            OemAppEntry("lenovo-vantage", "Lenovo Vantage", "Lenovo", "Official Lenovo Vantage guidance for Lenovo PC support, settings, and update assistance.", "https://support.lenovo.com/us/en/solutions/ht505081-lenovo-vantage-using-your-pc-just-got-easier", @"Drivers\Vendor\DOWNLOAD - Lenovo Vantage.url", "lenovo"),
            OemAppEntry("lenovo-system-update", "Lenovo System Update / Drivers & Software", "Lenovo", "Official Lenovo System Update and support path for drivers, BIOS, and applications.", "https://support.lenovo.com/us/en/solutions/ht003029-lenovo-system-update-update-drivers-bios-and-applications", @"Drivers\Vendor\DOWNLOAD - Lenovo System Update.url", "lenovo"),
            OemAppEntry("msi-center", "MSI Center", "MSI", "Official MSI Center landing page for MSI system utilities and support features.", "https://www.msi.com/Landing/MSI-Center", @"Drivers\Vendor\DOWNLOAD - MSI Center.url", "msi", "micro-star"),
            OemEntry("msi-support", "MSI Support / Downloads", "MSI", "Official MSI support portal for product-specific drivers, utilities, manuals, BIOS, and firmware.", "https://www.msi.com/support", @"Drivers\Vendor\DOWNLOAD - MSI Support.url", "msi", "micro-star"),
            OemAppEntry("asus-myasus", "ASUS MyASUS", "ASUS", "Official ASUS support app guidance for ASUS device service, diagnostics, and update assistance.", "https://www.asus.com/support/MyASUS-deeplink/", @"Drivers\Vendor\DOWNLOAD - ASUS MyASUS.url", "asus", "asustek"),
            OemEntry("asus-download-center", "ASUS Download Center", "ASUS", "Official ASUS support portal for model-specific drivers, manuals, BIOS, and utilities.", "https://www.asus.com/support/download-center/", @"Drivers\Vendor\DOWNLOAD - ASUS Support.url", "asus", "asustek"),
            OemEntry("acer-support", "Acer Support / Drivers", "Acer", "Official Acer support portal for drivers, manuals, warranty, and model lookup.", "https://www.acer.com/us-en/support", @"Drivers\Vendor\DOWNLOAD - Acer Support.url", "acer"),
            Entry(
                "surface-drivers-firmware",
                "Microsoft Surface Drivers and Firmware",
                "Microsoft",
                DriverHubCategory.BiosFirmware,
                "Official Microsoft Surface driver and firmware package guidance by Surface model.",
                "https://support.microsoft.com/en-us/surface/download-drivers-and-firmware-for-surface-09bb2e09-2a4b-cb69-0951-078a7739e120",
                new[] { DriverHubPlatform.Windows, DriverHubPlatform.Oem, DriverHubPlatform.BiosFirmware, DriverHubPlatform.ManualPortal },
                new[] { "microsoft", "surface", "firmware", "bios", "drivers", "oem" },
                @"Drivers\Vendor\DOWNLOAD - Microsoft Surface Drivers and Firmware.url",
                downloadKind: DriverHubDownloadKind.FirmwareGuidance,
                isFirmwareRelated: true,
                isManualVendorPortal: true,
                requiresModelLookup: true,
                rules: new[]
                {
                    ManufacturerRule("microsoft", "Recommended based on detected Microsoft/Surface system"),
                    ModelRule("surface", "Recommended based on detected Surface model")
                }),
            OemEntry("gigabyte-support", "Gigabyte Support / Downloads", "Gigabyte", "Official Gigabyte support portal for product downloads, drivers, BIOS, utilities, and manuals.", "https://www.gigabyte.com/Support", @"Drivers\Vendor\DOWNLOAD - Gigabyte Support.url", "gigabyte"),
            OemEntry("asrock-support", "ASRock Support / Downloads", "ASRock", "Official ASRock support portal for motherboard, mini PC, networking, BIOS, and driver lookup.", "https://www.asrock.com/support/", @"Drivers\Vendor\DOWNLOAD - ASRock Support.url", "asrock"),

            Entry(
                "intel-wireless-drivers",
                "Intel Wireless Drivers",
                "Intel",
                DriverHubCategory.ChipsetStorageNetwork,
                "Official Intel Wi-Fi driver page. Use OEM support first on systems with customized wireless packages.",
                "https://www.intel.com/content/www/us/en/support/articles/000046918.html",
                new[] { DriverHubPlatform.Windows, DriverHubPlatform.Network, DriverHubPlatform.ManualPortal },
                new[] { "intel", "wireless", "wifi", "network", "windows" },
                @"Drivers\Network\DOWNLOAD - Intel Wireless Drivers.url",
                downloadKind: DriverHubDownloadKind.DriverSearchPage,
                isManualVendorPortal: true,
                rules: new[] { NetworkRule("intel", "Recommended based on detected Intel network vendor") }),
            Entry(
                "intel-ethernet-pack",
                "Intel Ethernet Adapter Driver Pack",
                "Intel",
                DriverHubCategory.ChipsetStorageNetwork,
                "Official Intel Ethernet adapter driver pack page for supported Intel network adapters.",
                "https://www.intel.com/content/www/us/en/download/15084/intel-ethernet-adapter-complete-driver-pack.html",
                new[] { DriverHubPlatform.Windows, DriverHubPlatform.Network, DriverHubPlatform.ManualPortal },
                new[] { "intel", "ethernet", "network", "lan", "windows" },
                @"Drivers\Network\DOWNLOAD - Intel Ethernet Drivers.url",
                downloadKind: DriverHubDownloadKind.DriverSearchPage,
                isManualVendorPortal: true,
                rules: new[] { NetworkRule("intel", "Recommended based on detected Intel network vendor") }),
            Entry(
                "intel-killer-networking",
                "Intel Killer Networking",
                "Intel",
                DriverHubCategory.ChipsetStorageNetwork,
                "Official Intel Killer software and driver page for supported Killer wireless and Ethernet products.",
                "https://www.intel.com/content/www/us/en/support/articles/000059060/wireless.html",
                new[] { DriverHubPlatform.Windows, DriverHubPlatform.Network, DriverHubPlatform.ManualPortal },
                new[] { "intel", "killer", "network", "wireless", "ethernet", "windows" },
                @"Drivers\Network\DOWNLOAD - Intel Killer Networking.url",
                downloadKind: DriverHubDownloadKind.DriverSearchPage,
                isManualVendorPortal: true,
                rules: new[] { NetworkRule("killer", "Recommended based on detected Intel Killer network vendor") }),
            Entry(
                "amd-chipset-drivers",
                "AMD Chipset Drivers",
                "AMD",
                DriverHubCategory.ChipsetStorageNetwork,
                "Official AMD support path for chipset driver lookup and downloads.",
                "https://www.amd.com/en/support/download/drivers.html",
                new[] { DriverHubPlatform.Windows, DriverHubPlatform.Chipset, DriverHubPlatform.ManualPortal },
                new[] { "amd", "chipset", "storage", "windows" },
                @"Drivers\Chipset\DOWNLOAD - AMD Chipset Drivers.url",
                downloadKind: DriverHubDownloadKind.DriverSearchPage,
                isManualVendorPortal: true,
                rules: new[] { CpuRule("amd", "Recommended based on detected AMD CPU") }),
            Entry(
                "realtek-downloads",
                "Realtek Audio and Network Downloads",
                "Realtek",
                DriverHubCategory.ChipsetStorageNetwork,
                "Official Realtek downloads page for audio and network software. OEM support may be better for laptops.",
                "https://www.realtek.com/en/downloads",
                new[] { DriverHubPlatform.Windows, DriverHubPlatform.Audio, DriverHubPlatform.Network, DriverHubPlatform.ManualPortal },
                new[] { "realtek", "audio", "network", "lan", "windows" },
                @"Drivers\Audio\DOWNLOAD - Realtek Audio Drivers.url",
                downloadKind: DriverHubDownloadKind.DriverSearchPage,
                isManualVendorPortal: true,
                rules: new[] { NetworkRule("realtek", "Recommended based on detected Realtek network/audio vendor") }),

            LinuxEntry(
                "nvidia-linux-drivers",
                "Linux NVIDIA Driver Guidance",
                "NVIDIA",
                "Official NVIDIA Unix/Linux driver page. Prefer distro packages when your distribution recommends them.",
                "https://www.nvidia.com/en-us/drivers/unix/",
                @"Drivers\Linux\INFO - NVIDIA Linux Drivers.url",
                new[] { "nvidia", "linux", "gpu", "graphics" }),
            LinuxEntry(
                "amd-linux-drivers",
                "AMD Linux Driver Support",
                "AMD",
                "Official AMD Linux driver page for Radeon and Radeon PRO graphics guidance.",
                "https://www.amd.com/en/support/download/linux-drivers.html",
                @"Drivers\Linux\INFO - AMD Linux Drivers.url",
                new[] { "amd", "radeon", "linux", "gpu", "graphics" }),
            LinuxEntry(
                "intel-linux-wireless",
                "Intel Linux Wireless Guidance",
                "Intel",
                "Official Intel Linux support guidance for Intel wireless adapters.",
                "https://www.intel.com/content/www/us/en/support/articles/000005511/wireless.html",
                @"Drivers\Linux\INFO - Intel Linux Wireless.url",
                new[] { "intel", "linux", "wireless", "network" }),
            LinuxFirmwareEntry(
                "fwupd-lvfs",
                "fwupd / LVFS Firmware Guidance",
                "fwupd / LVFS",
                "Official LVFS project page for Linux firmware update guidance through fwupd-capable tools.",
                "https://fwupd.org/",
                @"Drivers\Linux\INFO - fwupd LVFS.url",
                new[] { "linux", "firmware", "lvfs", "fwupd" }),
            LinuxEntry(
                "ubuntu-additional-drivers",
                "Ubuntu Additional Drivers Guidance",
                "Ubuntu",
                "Official Ubuntu community guidance for proprietary and additional driver management.",
                "https://help.ubuntu.com/community/BinaryDriverHowto",
                @"Drivers\Linux\INFO - Ubuntu Additional Drivers.url",
                new[] { "ubuntu", "linux", "additional drivers", "gpu" }),
            LinuxFirmwareEntry(
                "fedora-firmware-guidance",
                "Fedora Firmware Guidance",
                "Fedora",
                "Official Fedora docs starting point for firmware-related Linux guidance. Use Fedora-supported tools and vendor instructions.",
                "https://docs.fedoraproject.org/en-US/quick-docs/",
                @"Drivers\Linux\INFO - Fedora Firmware Guidance.url",
                new[] { "fedora", "linux", "firmware" })
        };

        return entries
            .OrderBy(entry => DriverHubDisplay.FormatCategory(entry.Category), StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DriverHubEntry Entry(
        string id,
        string name,
        string vendor,
        DriverHubCategory category,
        string description,
        string officialUrl,
        IReadOnlyList<DriverHubPlatform> platforms,
        IReadOnlyList<string> tags,
        string usbShortcutRelativePath,
        string officialDownloadUrl = "",
        string microsoftStoreUrl = "",
        string installerFileName = "",
        DriverHubDownloadKind downloadKind = DriverHubDownloadKind.None,
        bool canDirectDownloadInstaller = false,
        bool isFirmwareRelated = false,
        bool isLinuxGuidance = false,
        bool isManualVendorPortal = false,
        bool requiresModelLookup = false,
        IReadOnlyList<DriverHubMatchRule>? rules = null)
    {
        var effectiveDownloadKind = isLinuxGuidance
            ? DriverHubDownloadKind.LinuxGuidance
            : isFirmwareRelated && downloadKind == DriverHubDownloadKind.None
                ? DriverHubDownloadKind.FirmwareGuidance
                : downloadKind;
        var safeOfficialDownload =
            !string.IsNullOrWhiteSpace(officialDownloadUrl) &&
            DriverHubUrlSafety.IsSafeOfficialHttpUrl(officialDownloadUrl);
        var safeMicrosoftStore =
            !string.IsNullOrWhiteSpace(microsoftStoreUrl) &&
            DriverHubUrlSafety.IsSafeOfficialHttpUrl(microsoftStoreUrl);
        var canDirectDownload =
            canDirectDownloadInstaller &&
            !string.IsNullOrWhiteSpace(installerFileName) &&
            DriverHubUrlSafety.IsSafeOfficialInstallerDownloadUrl(officialDownloadUrl);
        var canOpenOfficialDownload = safeOfficialDownload && !canDirectDownload;
        var canOpenMicrosoftStore = safeMicrosoftStore;
        var canDownloadOfficialApp =
            canOpenMicrosoftStore ||
            canOpenOfficialDownload &&
            (effectiveDownloadKind is DriverHubDownloadKind.OfficialAppPage
                or DriverHubDownloadKind.OfficialAppInstaller
                or DriverHubDownloadKind.MicrosoftStore);
        var isGuidanceOnly =
            isLinuxGuidance ||
            isFirmwareRelated ||
            effectiveDownloadKind is DriverHubDownloadKind.LinuxGuidance or DriverHubDownloadKind.FirmwareGuidance;
        var resolvedRequiresModelLookup =
            requiresModelLookup ||
            isFirmwareRelated ||
            effectiveDownloadKind is DriverHubDownloadKind.OemSupportPage or DriverHubDownloadKind.DriverSearchPage;
        var primaryActionKind = ResolvePrimaryActionKind(
            effectiveDownloadKind,
            canDirectDownload,
            canOpenOfficialDownload,
            canOpenMicrosoftStore,
            isGuidanceOnly);
        var primaryActionPrototype = new DriverHubEntry
        {
            Name = name,
            Category = category,
            IsLinuxGuidance = isLinuxGuidance,
            IsFirmwareRelated = isFirmwareRelated,
            OfficialDownloadUrl = officialDownloadUrl,
            MicrosoftStoreUrl = microsoftStoreUrl,
            DownloadKind = effectiveDownloadKind,
            CanDownloadOfficialApp = canDownloadOfficialApp,
            CanDirectDownloadInstaller = canDirectDownload,
            CanOpenOfficialDownload = canOpenOfficialDownload,
            CanOpenMicrosoftStore = canOpenMicrosoftStore,
            IsInstallerDownload = canDirectDownload,
            IsGuidanceOnly = isGuidanceOnly,
            RequiresModelLookup = resolvedRequiresModelLookup,
            PrimaryActionKind = primaryActionKind
        };

        return new DriverHubEntry
        {
            Id = id,
            Name = name,
            Vendor = vendor,
            Category = category,
            Description = description,
            OfficialPageUrl = officialUrl,
            OfficialUrl = officialUrl,
            OfficialDownloadUrl = officialDownloadUrl,
            MicrosoftStoreUrl = microsoftStoreUrl,
            InstallerFileName = installerFileName,
            DownloadKind = effectiveDownloadKind,
            CanDownloadOfficialApp = canDownloadOfficialApp,
            CanDirectDownloadInstaller = canDirectDownload,
            CanOpenOfficialDownload = canOpenOfficialDownload,
            CanOpenMicrosoftStore = canOpenMicrosoftStore,
            CanDownloadToUsb = canDirectDownload,
            IsInstallerDownload = canDirectDownload,
            IsGuidanceOnly = isGuidanceOnly,
            RequiresModelLookup = resolvedRequiresModelLookup,
            DoesNotAutoInstall = true,
            PrimaryActionKind = primaryActionKind,
            PrimaryActionLabel = DriverHubDisplay.BuildPrimaryActionLabel(primaryActionPrototype),
            BrandTileText = BuildBrandTileText(vendor),
            BrandAccentHex = BuildBrandAccentHex(vendor),
            Platforms = platforms,
            Tags = tags,
            MatchRules = rules ?? Array.Empty<DriverHubMatchRule>(),
            IsFirmwareRelated = isFirmwareRelated,
            IsLinuxGuidance = isLinuxGuidance,
            IsManualVendorPortal = isManualVendorPortal,
            UsbShortcutRelativePath = usbShortcutRelativePath,
            RecommendationReasons = rules?.Select(rule => rule.Reason).Distinct().ToArray() ?? Array.Empty<DriverHubRecommendationReason>(),
            SafetyNote = isFirmwareRelated ? DriverHubConstants.FirmwareSafetyWarning : string.Empty
        };
    }

    private static DriverHubPrimaryActionKind ResolvePrimaryActionKind(
        DriverHubDownloadKind downloadKind,
        bool canDirectDownload,
        bool canOpenOfficialDownload,
        bool canOpenMicrosoftStore,
        bool isGuidanceOnly)
    {
        if (canDirectDownload)
        {
            return DriverHubPrimaryActionKind.DownloadInstaller;
        }

        if (isGuidanceOnly)
        {
            return DriverHubPrimaryActionKind.OpenGuidance;
        }

        if (canOpenMicrosoftStore ||
            canOpenOfficialDownload &&
            (downloadKind is DriverHubDownloadKind.OfficialAppPage
                or DriverHubDownloadKind.OfficialAppInstaller
                or DriverHubDownloadKind.MicrosoftStore))
        {
            return DriverHubPrimaryActionKind.OpenOfficialDownload;
        }

        return DriverHubPrimaryActionKind.OpenOfficialPage;
    }

    private static DriverHubEntry OemAppEntry(
        string id,
        string name,
        string vendor,
        string description,
        string officialUrl,
        string usbShortcutRelativePath,
        params string[] manufacturerMatches) =>
        OemEntryCore(
            id,
            name,
            vendor,
            description,
            officialUrl,
            usbShortcutRelativePath,
            DriverHubDownloadKind.OfficialAppPage,
            manufacturerMatches);

    private static DriverHubEntry OemEntry(
        string id,
        string name,
        string vendor,
        string description,
        string officialUrl,
        string usbShortcutRelativePath,
        params string[] manufacturerMatches)
    {
        return OemEntryCore(
            id,
            name,
            vendor,
            description,
            officialUrl,
            usbShortcutRelativePath,
            DriverHubDownloadKind.OemSupportPage,
            manufacturerMatches);
    }

    private static DriverHubEntry OemEntryCore(
        string id,
        string name,
        string vendor,
        string description,
        string officialUrl,
        string usbShortcutRelativePath,
        DriverHubDownloadKind downloadKind,
        params string[] manufacturerMatches)
    {
        return Entry(
            id,
            name,
            vendor,
            DriverHubCategory.OemSupport,
            description,
            officialUrl,
            new[] { DriverHubPlatform.Windows, DriverHubPlatform.Oem, DriverHubPlatform.ManualPortal },
            manufacturerMatches.Concat(new[] { "oem", "drivers", "support", "firmware", "bios" }).ToArray(),
            usbShortcutRelativePath,
            officialDownloadUrl: downloadKind == DriverHubDownloadKind.OfficialAppPage ? officialUrl : string.Empty,
            downloadKind: downloadKind,
            isManualVendorPortal: downloadKind != DriverHubDownloadKind.OfficialAppPage,
            requiresModelLookup: downloadKind == DriverHubDownloadKind.OemSupportPage,
            rules: manufacturerMatches
                .Select(match => ManufacturerRule(match, $"Recommended based on detected {vendor} system"))
                .ToArray());
    }

    private static DriverHubEntry LinuxEntry(
        string id,
        string name,
        string vendor,
        string description,
        string officialUrl,
        string usbShortcutRelativePath,
        IReadOnlyList<string> tags,
        params DriverHubMatchRule[] matchRules)
    {
        var linuxRule = new DriverHubMatchRule
        {
            OperatingSystemContains = new[] { "linux" },
            Reason = DriverHubRecommendationReason.DetectedOperatingSystem,
            StatusText = "Linux guidance"
        };

        return Entry(
            id,
            name,
            vendor,
            DriverHubCategory.LinuxDrivers,
            description,
            officialUrl,
            new[] { DriverHubPlatform.Linux, DriverHubPlatform.ManualPortal },
            tags.Concat(new[] { "guidance", "manual" }).ToArray(),
            usbShortcutRelativePath,
            downloadKind: DriverHubDownloadKind.LinuxGuidance,
            isLinuxGuidance: true,
            isManualVendorPortal: true,
            rules: matchRules.Concat(new[] { linuxRule }).ToArray());
    }

    private static DriverHubEntry LinuxFirmwareEntry(
        string id,
        string name,
        string vendor,
        string description,
        string officialUrl,
        string usbShortcutRelativePath,
        IReadOnlyList<string> tags) =>
        Entry(
            id,
            name,
            vendor,
            DriverHubCategory.LinuxDrivers,
            description,
            officialUrl,
            new[] { DriverHubPlatform.Linux, DriverHubPlatform.BiosFirmware, DriverHubPlatform.ManualPortal },
            tags.Concat(new[] { "guidance", "manual" }).ToArray(),
            usbShortcutRelativePath,
            downloadKind: DriverHubDownloadKind.LinuxGuidance,
            isFirmwareRelated: true,
            isLinuxGuidance: true,
            isManualVendorPortal: true,
            rules: new[]
            {
                new DriverHubMatchRule
                {
                    OperatingSystemContains = new[] { "linux" },
                    Reason = DriverHubRecommendationReason.DetectedOperatingSystem,
                    StatusText = "Linux guidance"
                }
            });

    private static DriverHubMatchRule ManufacturerRule(string value, string status) =>
        new()
        {
            ManufacturerContains = new[] { value },
            Reason = DriverHubRecommendationReason.DetectedManufacturer,
            StatusText = status
        };

    private static DriverHubMatchRule ModelRule(string value, string status) =>
        new()
        {
            ModelContains = new[] { value },
            Reason = DriverHubRecommendationReason.DetectedManufacturer,
            StatusText = status
        };

    private static DriverHubMatchRule GpuRule(string value, string status) =>
        new()
        {
            GpuContains = new[] { value },
            Reason = DriverHubRecommendationReason.DetectedGpu,
            StatusText = status
        };

    private static DriverHubMatchRule CpuRule(string value, string status) =>
        new()
        {
            CpuContains = new[] { value },
            Reason = DriverHubRecommendationReason.DetectedCpu,
            StatusText = status
        };

    private static DriverHubMatchRule NetworkRule(string value, string status) =>
        new()
        {
            NetworkContains = new[] { value },
            Reason = DriverHubRecommendationReason.DetectedNetwork,
            StatusText = status
        };

    private static string BuildBrandTileText(string vendor)
    {
        // Brand tiles are text monograms only; do not bundle official vendor logo images
        // unless logo licensing/usage rights are reviewed for the release.
        if (vendor.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
        {
            return "NV";
        }

        if (vendor.Contains("AMD", StringComparison.OrdinalIgnoreCase))
        {
            return "AMD";
        }

        if (vendor.Contains("Intel", StringComparison.OrdinalIgnoreCase))
        {
            return "Intel";
        }

        if (vendor.Contains("Dell", StringComparison.OrdinalIgnoreCase))
        {
            return "Dell";
        }

        if (vendor.Equals("HP", StringComparison.OrdinalIgnoreCase))
        {
            return "HP";
        }

        if (vendor.Contains("Lenovo", StringComparison.OrdinalIgnoreCase))
        {
            return "Lenovo";
        }

        if (vendor.Contains("MSI", StringComparison.OrdinalIgnoreCase))
        {
            return "MSI";
        }

        if (vendor.Contains("ASUS", StringComparison.OrdinalIgnoreCase))
        {
            return "ASUS";
        }

        if (vendor.Contains("Acer", StringComparison.OrdinalIgnoreCase))
        {
            return "Acer";
        }

        if (vendor.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
        {
            return "MS";
        }

        if (vendor.Contains("Realtek", StringComparison.OrdinalIgnoreCase))
        {
            return "RTL";
        }

        if (vendor.Contains("fwupd", StringComparison.OrdinalIgnoreCase) ||
            vendor.Contains("LVFS", StringComparison.OrdinalIgnoreCase))
        {
            return "LVFS";
        }

        if (vendor.Contains("Ubuntu", StringComparison.OrdinalIgnoreCase) ||
            vendor.Contains("Fedora", StringComparison.OrdinalIgnoreCase))
        {
            return "Linux";
        }

        return vendor.Length <= 6 ? vendor : vendor[..Math.Min(4, vendor.Length)].ToUpperInvariant();
    }

    private static string BuildBrandAccentHex(string vendor)
    {
        if (vendor.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
        {
            return "#3346A758";
        }

        if (vendor.Contains("AMD", StringComparison.OrdinalIgnoreCase))
        {
            return "#33EF4444";
        }

        if (vendor.Contains("Intel", StringComparison.OrdinalIgnoreCase))
        {
            return "#330078D4";
        }

        if (vendor.Contains("Dell", StringComparison.OrdinalIgnoreCase))
        {
            return "#33007DB8";
        }

        if (vendor.Equals("HP", StringComparison.OrdinalIgnoreCase))
        {
            return "#3338BDF8";
        }

        if (vendor.Contains("Lenovo", StringComparison.OrdinalIgnoreCase))
        {
            return "#33E11B22";
        }

        if (vendor.Contains("MSI", StringComparison.OrdinalIgnoreCase))
        {
            return "#33DC2626";
        }

        if (vendor.Contains("ASUS", StringComparison.OrdinalIgnoreCase))
        {
            return "#334F46E5";
        }

        if (vendor.Contains("Acer", StringComparison.OrdinalIgnoreCase))
        {
            return "#3384CC16";
        }

        if (vendor.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
        {
            return "#332563EB";
        }

        if (vendor.Contains("Realtek", StringComparison.OrdinalIgnoreCase))
        {
            return "#33F97316";
        }

        if (vendor.Contains("fwupd", StringComparison.OrdinalIgnoreCase) ||
            vendor.Contains("LVFS", StringComparison.OrdinalIgnoreCase) ||
            vendor.Contains("Ubuntu", StringComparison.OrdinalIgnoreCase) ||
            vendor.Contains("Fedora", StringComparison.OrdinalIgnoreCase))
        {
            return "#3316A34A";
        }

        return "#334B5563";
    }
}
