using System.IO;
using System.Text.Json;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services;

// Curated defaults plus manifest-derived optional rows for item-level USB Builder
// selection. Curated rows form the technician-friendly recommended baseline;
// manifest rows expose the full bundled catalog without duplicating its data here.
public static class UsbBuilderProfileItemCatalog
{
    private const long Mb = 1024L * 1024;
    private const long Gb = 1024L * 1024 * 1024;

    private static readonly Dictionary<string, IReadOnlyList<UsbBuilderProfileItem>> Items =
        BuildItems()
            .GroupBy(i => i.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .GroupBy(i => i.CategoryId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<UsbBuilderProfileItem>)g.ToList(), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<UsbBuilderProfileItem> All =>
        Items.Values.SelectMany(list => list).Select(CloneItem).ToList();

    public static IReadOnlyList<UsbBuilderProfileItem> ForCategory(string categoryId) =>
        Items.TryGetValue(categoryId, out var list)
            ? list.Select(CloneItem).ToList()
            : Array.Empty<UsbBuilderProfileItem>();

    public static bool TryFind(string itemId, out UsbBuilderProfileItem item)
    {
        item = Items.Values
            .SelectMany(list => list)
            .Where(i => string.Equals(i.Id, itemId, StringComparison.OrdinalIgnoreCase))
            .Select(CloneItem)
            .FirstOrDefault()!;
        return item is not null;
    }

    private static UsbBuilderProfileItem CloneItem(UsbBuilderProfileItem src)
    {
        var clone = new UsbBuilderProfileItem
        {
            Id = src.Id,
            CategoryId = src.CategoryId,
            DisplayName = src.DisplayName,
            Subcategory = src.Subcategory,
            ShortDescription = src.ShortDescription,
            Source = src.Source,
            Kind = src.Kind,
            Tier = src.Tier,
            SpaceEstimate = src.SpaceEstimate,
            ManifestEntryName = src.ManifestEntryName,
            UsbRelativePath = src.UsbRelativePath,
            Notes = src.Notes,
            RequiresUserSuppliedMedia = src.RequiresUserSuppliedMedia,
            VendorPortalOnly = src.VendorPortalOnly,
            LargePayload = src.LargePayload
        };
        clone.DetectedBytes = src.DetectedBytes;
        clone.ExistsOnUsb = src.ExistsOnUsb;
        clone.IsSelected = src.IsSelected;
        return clone;
    }

    private static IEnumerable<UsbBuilderProfileItem> BuildItems()
    {
        var curated = BuildCuratedItems().ToList();
        foreach (var item in curated) yield return item;
        foreach (var item in BuildManifestItems(curated)) yield return item;
    }

    private static IEnumerable<UsbBuilderProfileItem> BuildCuratedItems()
    {
        foreach (var item in BuildCoreItems()) yield return item;
        foreach (var item in BuildForgerEmsPortableItems()) yield return item;
        foreach (var item in BuildWindowsItems()) yield return item;
        foreach (var item in BuildLegacyWindowsItems()) yield return item;
        foreach (var item in BuildLinuxItems()) yield return item;
        foreach (var item in BuildMacOsItems()) yield return item;
        foreach (var item in BuildAndroidItems()) yield return item;
        foreach (var item in BuildIosIpadOsItems()) yield return item;
        foreach (var item in BuildOemItems()) yield return item;
        foreach (var item in BuildDiagnosticsItems()) yield return item;
    }

    private static IEnumerable<UsbBuilderProfileItem> BuildCoreItems()
    {
        yield return new UsbBuilderProfileItem
        {
            Id = "core.ventoy",
            CategoryId = "core",
            DisplayName = "Ventoy bootloader & safety structure",
            Subcategory = "Boot",
            Kind = UsbBuilderProfileItemKind.Tool,
            Tier = UsbBuilderProfileItemTier.Required,
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Range(40 * Mb, 60 * Mb, 80 * Mb,
                UsbBuilderPackSizeConfidence.Known, "bootloader + Ventoy data"),
            ManifestEntryName = "Ventoy pinned fallback 1.1.12 (Windows package)",
            Notes = "Required for multi-ISO boot. ForgerEMS preserves your existing Ventoy install."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "core.forgerems-docs",
            CategoryId = "core",
            DisplayName = "ForgerEMS docs & logs structure",
            Subcategory = "Docs",
            Kind = UsbBuilderProfileItemKind.HtmlGuide,
            Tier = UsbBuilderProfileItemTier.Required,
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Range(5 * Mb, 20 * Mb, 60 * Mb,
                UsbBuilderPackSizeConfidence.Known, "_docs, _logs, _reports, README.html"),
            UsbRelativePath = @"_docs",
            Notes = "Folder skeleton + README.html generated at build time."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "core.manifest",
            CategoryId = "core",
            DisplayName = "Update manifest & checksum index",
            Subcategory = "Catalog",
            Kind = UsbBuilderProfileItemKind.ManagedDownload,
            Tier = UsbBuilderProfileItemTier.Required,
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(2 * Mb, "manifest JSON"),
            UsbRelativePath = @"_forgerems\metadata\ForgerEMS.updates.json",
            Notes = "Drives Update USB and Full Managed Download decisions."
        };
    }

    private static IEnumerable<UsbBuilderProfileItem> BuildForgerEmsPortableItems()
    {
        yield return new UsbBuilderProfileItem
        {
            Id = "forgerems-portable.app",
            CategoryId = "forgerems-portable",
            DisplayName = "ForgerEMS Portable App",
            Subcategory = "Portable app",
            ShortDescription = "Copies the packaged ForgerEMS app and backend so the USB can run ForgerEMS without installer registration.",
            Source = "Packaged release output",
            Kind = UsbBuilderProfileItemKind.Tool,
            Tier = UsbBuilderProfileItemTier.Recommended,
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Range(85 * Mb, 175 * Mb, 400 * Mb,
                UsbBuilderPackSizeConfidence.Estimated, "_apps\\ForgerEMS"),
            UsbRelativePath = @"_apps\ForgerEMS\ForgerEMS.exe",
            Notes = "Generated from the packaged app next to the running backend; writes under _apps\\ForgerEMS only."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "forgerems-portable.docs",
            CategoryId = "forgerems-portable",
            DisplayName = "Terms, Privacy, Legal, FAQ, and About docs",
            Subcategory = "Legal/help docs",
            ShortDescription = "Keeps ForgerEMS preview, legal, privacy, and support guidance available on technician USBs.",
            Source = "ForgerEMS docs",
            Kind = UsbBuilderProfileItemKind.HtmlGuide,
            Tier = UsbBuilderProfileItemTier.Recommended,
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(2 * Mb, "_docs\\ForgerEMS"),
            UsbRelativePath = @"_docs\ForgerEMS\TERMS_OF_USE.md",
            Notes = "Includes Terms of Use, Privacy/Data Handling, Legal Notices, FAQ, About, and third-party notices where packaged."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "forgerems-portable.support-folders",
            CategoryId = "forgerems-portable",
            DisplayName = "ForgerEMS portable logs/support folders",
            Subcategory = "Support folders",
            ShortDescription = "Creates technician USB folders for local logs and support artifacts without putting app clutter at the USB root.",
            Source = "ForgerEMS USB Builder",
            Kind = UsbBuilderProfileItemKind.DropFolder,
            Tier = UsbBuilderProfileItemTier.Recommended,
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(128 * 1024, "_logs\\ForgerEMS"),
            UsbRelativePath = @"_logs\ForgerEMS",
            Notes = "No automatic upload or sync is added; review files before sharing them."
        };
    }

    private static IEnumerable<UsbBuilderProfileItem> BuildWindowsItems()
    {
        yield return new UsbBuilderProfileItem
        {
            Id = "windows.win11-link",
            CategoryId = "windows",
            DisplayName = "Windows 11 download page",
            Subcategory = "Windows 11",
            Kind = UsbBuilderProfileItemKind.OfficialPage,
            Tier = UsbBuilderProfileItemTier.Recommended,
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(2 * 1024, ".url shortcut"),
            ManifestEntryName = "Windows 11 Download Page",
            UsbRelativePath = @"ISO\Windows\DOWNLOAD - Windows 11.url",
            Source = "microsoft.com",
            Notes = "Official Microsoft download page; ISO is user-initiated."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "windows.win10-link",
            CategoryId = "windows",
            DisplayName = "Windows 10 download page",
            Subcategory = "Windows 10",
            Kind = UsbBuilderProfileItemKind.OfficialPage,
            Tier = UsbBuilderProfileItemTier.Recommended,
            ManifestEntryName = "Windows 10 Download Page",
            UsbRelativePath = @"ISO\Windows\DOWNLOAD - Windows 10.url",
            Source = "microsoft.com",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(2 * 1024, ".url shortcut"),
            Notes = "Official Microsoft download page."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "windows.win11-drop",
            CategoryId = "windows",
            DisplayName = "Windows 11 manual ISO drop folder",
            Subcategory = "Windows 11",
            Kind = UsbBuilderProfileItemKind.DropFolder,
            Tier = UsbBuilderProfileItemTier.Recommended,
            UsbRelativePath = @"ISO\Windows\Windows-Manual-ISO-Drop\Windows 11",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.UserSupplied("5–6 GB per ISO"),
            RequiresUserSuppliedMedia = true,
            Notes = "Drop your licensed Windows 11 ISO here for Ventoy to pick up."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "windows.win10-drop",
            CategoryId = "windows",
            DisplayName = "Windows 10 manual ISO drop folder",
            Subcategory = "Windows 10",
            Kind = UsbBuilderProfileItemKind.DropFolder,
            Tier = UsbBuilderProfileItemTier.Recommended,
            UsbRelativePath = @"ISO\Windows\Windows-Manual-ISO-Drop\Windows 10",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.UserSupplied("4–5 GB per ISO"),
            RequiresUserSuppliedMedia = true,
            Notes = "Drop your licensed Windows 10 ISO here."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "windows.server-link",
            CategoryId = "windows",
            DisplayName = "Windows Server evaluation download page",
            Subcategory = "Server",
            Kind = UsbBuilderProfileItemKind.OfficialPage,
            Tier = UsbBuilderProfileItemTier.Optional,
            ManifestEntryName = "Windows Server Evaluation Center",
            UsbRelativePath = @"ISO\Windows\DOWNLOAD - Windows Server Eval.url",
            Source = "microsoft.com",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(2 * 1024, ".url shortcut"),
            Notes = "Microsoft Evaluation Center link; ISO is user-initiated."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "windows.server-drop",
            CategoryId = "windows",
            DisplayName = "Windows Server manual ISO drop folder",
            Subcategory = "Server",
            Kind = UsbBuilderProfileItemKind.DropFolder,
            Tier = UsbBuilderProfileItemTier.Optional,
            UsbRelativePath = @"ISO\Windows\Windows-Manual-ISO-Drop\Windows Server",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.UserSupplied("5–10 GB per ISO"),
            RequiresUserSuppliedMedia = true,
            LargePayload = true,
            Notes = "Drop licensed Windows Server ISO here."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "windows.winpe-adk-guide",
            CategoryId = "windows",
            DisplayName = "WinPE / ADK guidance",
            Subcategory = "WinPE",
            Kind = UsbBuilderProfileItemKind.HtmlGuide,
            Tier = UsbBuilderProfileItemTier.Recommended,
            ManifestEntryName = "Windows ADK and WinPE Info",
            UsbRelativePath = @"ISO\Windows\GUIDE - Windows ADK + WinPE.url",
            Source = "microsoft.com",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(200 * 1024, "HTML doc"),
            Notes = "Step-by-step guidance for creating WinPE media."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "windows.recovery-docs",
            CategoryId = "windows",
            DisplayName = "Windows recovery docs",
            Subcategory = "Recovery",
            Kind = UsbBuilderProfileItemKind.HtmlGuide,
            Tier = UsbBuilderProfileItemTier.Recommended,
            UsbRelativePath = @"ISO\Windows\README - Windows ISO workflow.txt",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(150 * 1024, "HTML doc"),
            Notes = "Recovery environment cheat sheet, bootrec / sfc / DISM."
        };
    }

    private static IEnumerable<UsbBuilderProfileItem> BuildLegacyWindowsItems()
    {
        var legacy = new (string id, string display, string drop, string manifestName)[]
        {
            ("legacy-windows.win81", "Windows 8.1 manual drop folder", "Windows 8.1", "Windows 8.1 Lifecycle Info"),
            ("legacy-windows.win8", "Windows 8 manual drop folder", "Windows 8", "Windows 8 Lifecycle Info"),
            ("legacy-windows.win7", "Windows 7 manual drop folder", "Windows 7", "Windows 7 Lifecycle Info"),
            ("legacy-windows.vista", "Windows Vista manual drop folder", "Windows Vista", "Windows Vista Lifecycle Info"),
            ("legacy-windows.xp", "Windows XP manual drop folder", "Windows XP", "Windows XP Lifecycle Info")
        };

        foreach (var entry in legacy)
        {
            var tier = entry.drop is "Windows 8.1" or "Windows 7"
                ? UsbBuilderProfileItemTier.Recommended
                : UsbBuilderProfileItemTier.Optional;

            yield return new UsbBuilderProfileItem
            {
                Id = entry.id,
                CategoryId = "legacy-windows",
                DisplayName = entry.display,
                Subcategory = entry.drop,
                Kind = UsbBuilderProfileItemKind.DropFolder,
                Tier = tier,
                ManifestEntryName = entry.manifestName,
                UsbRelativePath = $@"ISO\Windows\Windows-Manual-ISO-Drop\{entry.drop}",
                SpaceEstimate = UsbBuilderProfileSpaceEstimate.UserSupplied("ISO size varies"),
                RequiresUserSuppliedMedia = true,
                Notes = "ForgerEMS cannot ship legacy Windows ISOs. Drop user-licensed media here."
            };
        }

        yield return new UsbBuilderProfileItem
        {
            Id = "legacy-windows.lifecycle-docs",
            CategoryId = "legacy-windows",
            DisplayName = "Microsoft lifecycle reference links",
            Subcategory = "Reference",
            Kind = UsbBuilderProfileItemKind.HtmlGuide,
            Tier = UsbBuilderProfileItemTier.Recommended,
            UsbRelativePath = @"ISO\Windows-Legacy\README - Legacy Windows media.txt",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(80 * 1024, ".url shortcuts"),
            Notes = "Per-version lifecycle and known-issue references."
        };
    }

    private static IEnumerable<UsbBuilderProfileItem> BuildLinuxItems()
    {
        yield return new UsbBuilderProfileItem
        {
            Id = "linux.rescuezilla",
            CategoryId = "linux-rescue",
            DisplayName = "Rescuezilla",
            Subcategory = "Recovery",
            Kind = UsbBuilderProfileItemKind.Iso,
            Tier = UsbBuilderProfileItemTier.Recommended,
            ManifestEntryName = "Rescuezilla 2.6.2 (64-bit oracular)",
            UsbRelativePath = @"ISO\Tools\rescuezilla-2.6.2-64bit.oracular.iso",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(2 * Gb + 500 * Mb, "imaging + recovery"),
            Notes = "Disk imaging and rescue toolkit; highly recommended for field repair USBs."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "linux.ubuntu-desktop",
            CategoryId = "linux-rescue",
            DisplayName = "Ubuntu Desktop LTS",
            Subcategory = "Distro",
            Kind = UsbBuilderProfileItemKind.Iso,
            Tier = UsbBuilderProfileItemTier.Recommended,
            ManifestEntryName = "Ubuntu 24.04.4 LTS Desktop (amd64)",
            UsbRelativePath = @"ISO\Linux\ubuntu-24.04.4-desktop-amd64.iso",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(5L * Gb, "live + installer"),
            LargePayload = true,
            Notes = "Common live-boot environment for diagnostics and recovery."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "linux.debian",
            CategoryId = "linux-rescue",
            DisplayName = "Debian netinst / live",
            Subcategory = "Distro",
            Kind = UsbBuilderProfileItemKind.Iso,
            Tier = UsbBuilderProfileItemTier.Optional,
            ManifestEntryName = "Debian GNU/Linux 13.5.0 netinst (amd64)",
            UsbRelativePath = @"ISO\Linux\debian-13.5.0-amd64-netinst.iso",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(700 * Mb, "netinst"),
            Notes = "Minimal Debian installer; good for low-resource recoveries."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "linux.fedora-workstation",
            CategoryId = "linux-rescue",
            DisplayName = "Fedora Workstation",
            Subcategory = "Distro",
            Kind = UsbBuilderProfileItemKind.Iso,
            Tier = UsbBuilderProfileItemTier.Optional,
            ManifestEntryName = "Fedora Workstation 44-1.7 Live (x86_64)",
            UsbRelativePath = @"ISO\Linux\Fedora-Workstation-Live-44-1.7.x86_64.iso",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(2L * Gb, "live image"),
            LargePayload = true
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "linux.rocky",
            CategoryId = "linux-rescue",
            DisplayName = "Rocky Linux minimal",
            Subcategory = "Distro",
            Kind = UsbBuilderProfileItemKind.Iso,
            Tier = UsbBuilderProfileItemTier.Optional,
            ManifestEntryName = "Rocky Linux 10.1 Minimal (x86_64)",
            UsbRelativePath = @"ISO\Linux\Rocky-10.1-x86_64-minimal.iso",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(2L * Gb, "minimal ISO"),
            LargePayload = true
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "linux.alma",
            CategoryId = "linux-rescue",
            DisplayName = "AlmaLinux minimal",
            Subcategory = "Distro",
            Kind = UsbBuilderProfileItemKind.Iso,
            Tier = UsbBuilderProfileItemTier.Optional,
            ManifestEntryName = "AlmaLinux 10.2 Minimal (x86_64)",
            UsbRelativePath = @"ISO\Linux\AlmaLinux-10.2-x86_64-minimal.iso",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(2L * Gb, "minimal ISO"),
            LargePayload = true
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "linux.proxmox",
            CategoryId = "linux-rescue",
            DisplayName = "Proxmox VE installer",
            Subcategory = "Hypervisor",
            Kind = UsbBuilderProfileItemKind.Iso,
            Tier = UsbBuilderProfileItemTier.Optional,
            ManifestEntryName = "Proxmox VE 9.2-1 ISO Installer",
            UsbRelativePath = @"ISO\Linux\proxmox-ve_9.2-1.iso",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(1L * Gb + 200 * Mb, "VE installer"),
            LargePayload = true,
            Notes = "Hypervisor installer; only useful for server-grade rebuilds."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "linux.alpine",
            CategoryId = "linux-rescue",
            DisplayName = "Alpine Linux",
            Subcategory = "Distro",
            Kind = UsbBuilderProfileItemKind.Iso,
            Tier = UsbBuilderProfileItemTier.Optional,
            ManifestEntryName = "Alpine Linux 3.23.4 Standard (x86_64)",
            UsbRelativePath = @"ISO\Linux\alpine-standard-3.23.4-x86_64.iso",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(200 * Mb, "minimal"),
            Notes = "Compact distro for tiny rescue USBs."
        };
    }

    private static IEnumerable<UsbBuilderProfileItem> BuildMacOsItems()
    {
        yield return new UsbBuilderProfileItem
        {
            Id = "macos.support-links",
            CategoryId = "macos",
            DisplayName = "Apple recovery & createinstallmedia guidance",
            Subcategory = "Reference",
            Kind = UsbBuilderProfileItemKind.HtmlGuide,
            Tier = UsbBuilderProfileItemTier.Recommended,
            ManifestEntryName = "Apple macOS Download and Install Guide",
            UsbRelativePath = @"ISO\macOS\GUIDE - Apple macOS download and install guide.url",
            Source = "apple.com",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(100 * 1024, "HTML + .url"),
            Notes = "Apple workflow references. ForgerEMS does not host macOS installers."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "macos.installer-drop",
            CategoryId = "macos",
            DisplayName = "macOS installer drop folder",
            Subcategory = "Installer",
            Kind = UsbBuilderProfileItemKind.DropFolder,
            Tier = UsbBuilderProfileItemTier.Recommended,
            UsbRelativePath = @"ISO\macOS\macOS-Manual-Installer-Drop",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.UserSupplied("8–14 GB per installer"),
            RequiresUserSuppliedMedia = true,
            LargePayload = true,
            Notes = "Drop user-supplied .dmg / .pkg / Install assistant here."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "macos.recovery-docs",
            CategoryId = "macos",
            DisplayName = "macOS recovery workflow docs",
            Subcategory = "Recovery",
            Kind = UsbBuilderProfileItemKind.HtmlGuide,
            Tier = UsbBuilderProfileItemTier.Optional,
            UsbRelativePath = @"ISO\macOS\README - macOS installer workflow.txt",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(80 * 1024, "HTML doc"),
            Notes = "Internet Recovery / Recovery Mode reference."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "macos.dmg-tools-link",
            CategoryId = "macos",
            DisplayName = "DMG / PKG handling tools",
            Subcategory = "Tools",
            Kind = UsbBuilderProfileItemKind.OfficialPage,
            Tier = UsbBuilderProfileItemTier.Optional,
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(2 * 1024, ".url shortcut"),
            Notes = "Pointer to first-party tooling for handling Apple disk images."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "macos.legal-note",
            CategoryId = "macos",
            DisplayName = "Apple legal / licensing reminder",
            Subcategory = "Reference",
            Kind = UsbBuilderProfileItemKind.HtmlGuide,
            Tier = UsbBuilderProfileItemTier.Recommended,
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(40 * 1024, "HTML note"),
            Notes = "Reminder that ForgerEMS does not bypass Apple licensing restrictions."
        };
    }

    private static IEnumerable<UsbBuilderProfileItem> BuildAndroidItems()
    {
        yield return new UsbBuilderProfileItem
        {
            Id = "android.platform-tools",
            CategoryId = "android",
            DisplayName = "Android platform-tools (adb/fastboot)",
            Subcategory = "Tools",
            Kind = UsbBuilderProfileItemKind.OfficialPage,
            Tier = UsbBuilderProfileItemTier.Recommended,
            ManifestEntryName = "Android Platform Tools adb fastboot",
            UsbRelativePath = @"Tools\Android\DOWNLOAD - Android Platform Tools adb fastboot.url",
            Source = "developer.android.com",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(2 * 1024, ".url shortcut"),
            Notes = "Google-published adb/fastboot platform-tools page; download remains user-initiated."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "android.pixel-firmware-guide",
            CategoryId = "android",
            DisplayName = "Pixel factory image guidance",
            Subcategory = "Pixel",
            Kind = UsbBuilderProfileItemKind.HtmlGuide,
            Tier = UsbBuilderProfileItemTier.Recommended,
            ManifestEntryName = "Google Pixel Factory Images",
            UsbRelativePath = @"ISO\Android\Android-Manual-Firmware-Drop\Google Pixel\DOWNLOAD - Google Pixel Factory Images.url",
            Source = "developers.google.com",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(60 * 1024, "HTML + .url"),
            Notes = "Pixel factory image / OTA flashing steps."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "android.firmware-drop",
            CategoryId = "android",
            DisplayName = "OEM firmware drop folder",
            Subcategory = "OEM",
            Kind = UsbBuilderProfileItemKind.DropFolder,
            Tier = UsbBuilderProfileItemTier.Recommended,
            UsbRelativePath = @"ISO\Android\Android-Manual-Firmware-Drop",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.UserSupplied("1–8 GB per firmware"),
            RequiresUserSuppliedMedia = true,
            LargePayload = true,
            Notes = "Drop OEM firmware packages here; firmware must be verified from the vendor."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "android.samsung-links",
            CategoryId = "android",
            DisplayName = "Samsung Odin / firmware references",
            Subcategory = "Samsung",
            Kind = UsbBuilderProfileItemKind.OfficialPage,
            Tier = UsbBuilderProfileItemTier.Optional,
            ManifestEntryName = "Samsung Manual Firmware",
            UsbRelativePath = @"ISO\Android\Android-Manual-Firmware-Drop\Samsung\MANUAL FIRMWARE REQUIRED - Samsung.url",
            VendorPortalOnly = true,
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(2 * 1024, ".url shortcut"),
            Notes = "Vendor-portal-only firmware references."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "android.oem-portal-links",
            CategoryId = "android",
            DisplayName = "OEM unlock & support portal links",
            Subcategory = "OEM",
            Kind = UsbBuilderProfileItemKind.OfficialPage,
            Tier = UsbBuilderProfileItemTier.Optional,
            UsbRelativePath = @"ISO\Android\Android-Manual-Firmware-Drop",
            VendorPortalOnly = true,
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(8 * 1024, ".url shortcuts"),
            Notes = "Per-OEM support and bootloader-unlock landing pages."
        };
    }

    private static IEnumerable<UsbBuilderProfileItem> BuildIosIpadOsItems()
    {
        yield return new UsbBuilderProfileItem
        {
            Id = "ios.restore-guide",
            CategoryId = "ios-ipados",
            DisplayName = "iOS / iPadOS restore workflow guide",
            Subcategory = "Reference",
            Kind = UsbBuilderProfileItemKind.HtmlGuide,
            Tier = UsbBuilderProfileItemTier.Recommended,
            ManifestEntryName = "iPhone iPad Recovery Mode Restore",
            UsbRelativePath = @"ISO\iOS-iPadOS\GUIDE - iPhone iPad recovery mode restore.url",
            Source = "support.apple.com",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(80 * 1024, "HTML doc"),
            Notes = "Finder / iTunes restore reference."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "ios.ipsw-drop",
            CategoryId = "ios-ipados",
            DisplayName = "Signed IPSW drop folder",
            Subcategory = "IPSW",
            Kind = UsbBuilderProfileItemKind.DropFolder,
            Tier = UsbBuilderProfileItemTier.Recommended,
            UsbRelativePath = @"ISO\iOS-iPadOS\iOS-Manual-IPSW-Drop",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.UserSupplied("4–8 GB per IPSW"),
            RequiresUserSuppliedMedia = true,
            LargePayload = true,
            Notes = "User-supplied signed IPSW from Apple's signing window."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "ios.apple-support-links",
            CategoryId = "ios-ipados",
            DisplayName = "Apple support article links",
            Subcategory = "Reference",
            Kind = UsbBuilderProfileItemKind.OfficialPage,
            Tier = UsbBuilderProfileItemTier.Recommended,
            ManifestEntryName = "Apple Configurator Restore Revive Guide",
            UsbRelativePath = @"ISO\iOS-iPadOS\GUIDE - Apple Configurator restore revive.url",
            Source = "support.apple.com",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(4 * 1024, ".url shortcuts"),
            Notes = "Curated set of Apple support article shortcuts."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "ios.recovery-mode-guide",
            CategoryId = "ios-ipados",
            DisplayName = "Recovery / DFU mode quick reference",
            Subcategory = "Reference",
            Kind = UsbBuilderProfileItemKind.HtmlGuide,
            Tier = UsbBuilderProfileItemTier.Optional,
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(40 * 1024, "HTML doc")
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "ios.signing-status-link",
            CategoryId = "ios-ipados",
            DisplayName = "IPSW signing-status checker link",
            Subcategory = "IPSW",
            Kind = UsbBuilderProfileItemKind.OfficialPage,
            Tier = UsbBuilderProfileItemTier.Optional,
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(2 * 1024, ".url shortcut"),
            Notes = "Third-party signing-status reference."
        };
    }

    private static IEnumerable<UsbBuilderProfileItem> BuildOemItems()
    {
        var vendors = new (string id, string name, UsbBuilderProfileItemTier tier, string manifestName, string dest, string source)[]
        {
            ("oem.dell", "Dell support portal", UsbBuilderProfileItemTier.Recommended, "Dell Support / Drivers", @"Drivers\Vendor\DOWNLOAD - Dell Support.url", "dell.com"),
            ("oem.hp", "HP support portal", UsbBuilderProfileItemTier.Recommended, "HP Support / Drivers", @"Drivers\Vendor\DOWNLOAD - HP Support.url", "hp.com"),
            ("oem.lenovo", "Lenovo support portal", UsbBuilderProfileItemTier.Recommended, "Lenovo Support / Drivers", @"Drivers\Vendor\DOWNLOAD - Lenovo Support.url", "lenovo.com"),
            ("oem.surface", "Microsoft Surface recovery", UsbBuilderProfileItemTier.Recommended, "Microsoft Surface Drivers and Firmware", @"Drivers\Vendor\DOWNLOAD - Microsoft Surface Drivers.url", "microsoft.com"),
            ("oem.msi", "MSI support portal", UsbBuilderProfileItemTier.Optional, "MSI Support / Drivers", @"Drivers\Vendor\DOWNLOAD - MSI Support.url", "msi.com"),
            ("oem.asus", "ASUS support portal", UsbBuilderProfileItemTier.Optional, "ASUS Support / Drivers", @"Drivers\Vendor\DOWNLOAD - ASUS Support.url", "asus.com"),
            ("oem.acer", "Acer support portal", UsbBuilderProfileItemTier.Optional, "Acer Support / Drivers", @"Drivers\Vendor\DOWNLOAD - Acer Support.url", "acer.com")
        };

        foreach (var v in vendors)
        {
            yield return new UsbBuilderProfileItem
            {
                Id = v.id,
                CategoryId = "oem-tools",
                DisplayName = v.name,
                Subcategory = "OEM portal",
                Kind = UsbBuilderProfileItemKind.VendorLink,
                Tier = v.tier,
                VendorPortalOnly = true,
                ManifestEntryName = v.manifestName,
                UsbRelativePath = v.dest,
                Source = v.source,
                SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(2 * 1024, ".url shortcut"),
                Notes = "Vendor support portal shortcut. Drivers/firmware are never auto-downloaded."
            };
        }

        yield return new UsbBuilderProfileItem
        {
            Id = "oem.intel-driver-portal",
            CategoryId = "oem-tools",
            DisplayName = "Intel driver/firmware portal",
            Subcategory = "Component",
            Kind = UsbBuilderProfileItemKind.VendorLink,
            Tier = UsbBuilderProfileItemTier.Recommended,
            VendorPortalOnly = true,
            ManifestEntryName = "Intel Driver Download Center",
            UsbRelativePath = @"Drivers\Vendor\DOWNLOAD - Intel Driver Download Center.url",
            Source = "intel.com",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(2 * 1024, ".url shortcut"),
            Notes = "Intel Driver & Support Assistant landing page."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "oem.nvidia-driver-portal",
            CategoryId = "oem-tools",
            DisplayName = "NVIDIA driver portal",
            Subcategory = "Component",
            Kind = UsbBuilderProfileItemKind.VendorLink,
            Tier = UsbBuilderProfileItemTier.Recommended,
            VendorPortalOnly = true,
            ManifestEntryName = "NVIDIA Drivers",
            UsbRelativePath = @"Drivers\Vendor\DOWNLOAD - NVIDIA Drivers.url",
            Source = "nvidia.com",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(2 * 1024, ".url shortcut"),
            Notes = "NVIDIA driver download landing page."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "oem.amd-driver-portal",
            CategoryId = "oem-tools",
            DisplayName = "AMD driver portal",
            Subcategory = "Component",
            Kind = UsbBuilderProfileItemKind.VendorLink,
            Tier = UsbBuilderProfileItemTier.Recommended,
            VendorPortalOnly = true,
            ManifestEntryName = "AMD Drivers and Support",
            UsbRelativePath = @"Drivers\Vendor\DOWNLOAD - AMD Drivers.url",
            Source = "amd.com",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(2 * 1024, ".url shortcut"),
            Notes = "AMD driver auto-detect landing page."
        };
    }

    private static IEnumerable<UsbBuilderProfileItem> BuildDiagnosticsItems()
    {
        yield return new UsbBuilderProfileItem
        {
            Id = "diag.crystaldiskinfo",
            CategoryId = "diagnostics",
            DisplayName = "CrystalDiskInfo (disk health)",
            Subcategory = "Disk health",
            Kind = UsbBuilderProfileItemKind.ManagedDownload,
            Tier = UsbBuilderProfileItemTier.Recommended,
            ManifestEntryName = "CrystalDiskInfo 9.8.0 (standard zip)",
            UsbRelativePath = @"Tools\Portable\Disk\CrystalDiskInfo9_8_0.zip",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(8 * Mb, "portable build"),
            Notes = "SMART read-only disk health utility."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "diag.rufus",
            CategoryId = "diagnostics",
            DisplayName = "Rufus (USB writer)",
            Subcategory = "Imaging",
            Kind = UsbBuilderProfileItemKind.ManagedDownload,
            Tier = UsbBuilderProfileItemTier.Recommended,
            ManifestEntryName = "Rufus 4.14 Portable (x64)",
            UsbRelativePath = @"Tools\Portable\USB\rufus-4.14p.exe",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(2 * Mb, "portable .exe"),
            Notes = "Bootable USB image writer."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "diag.etcher",
            CategoryId = "diagnostics",
            DisplayName = "balenaEtcher (USB writer)",
            Subcategory = "Imaging",
            Kind = UsbBuilderProfileItemKind.ManagedDownload,
            Tier = UsbBuilderProfileItemTier.Optional,
            ManifestEntryName = "balenaEtcher 2.1.6 Setup (x64)",
            UsbRelativePath = @"Tools\Portable\USB\balenaEtcher-Setup-2.1.6.exe",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(140 * Mb, "portable"),
            Notes = "Alternative image writer."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "diag.memtest86plus",
            CategoryId = "diagnostics",
            DisplayName = "MemTest86+ (memory testing)",
            Subcategory = "Memory",
            Kind = UsbBuilderProfileItemKind.ManagedDownload,
            Tier = UsbBuilderProfileItemTier.Recommended,
            ManifestEntryName = "MemTest86+ 8.10 (x86_64 ISO archive)",
            UsbRelativePath = @"ISO\Tools\mt86plus_8.10_x86_64.iso.zip",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(12 * Mb, "boot image"),
            Notes = "Open-source RAM tester boot image."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "diag.gparted-live",
            CategoryId = "diagnostics",
            DisplayName = "GParted Live (partitioning)",
            Subcategory = "Partitioning",
            Kind = UsbBuilderProfileItemKind.Iso,
            Tier = UsbBuilderProfileItemTier.Recommended,
            ManifestEntryName = "GParted Live 1.8.1-3 (amd64)",
            UsbRelativePath = @"ISO\Tools\gparted-live-1.8.1-3-amd64.iso",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(500 * Mb, "live ISO"),
            Notes = "Partitioning live ISO."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "diag.network-tools",
            CategoryId = "diagnostics",
            DisplayName = "Network diagnostics bundle",
            Subcategory = "Network",
            Kind = UsbBuilderProfileItemKind.ManagedDownload,
            Tier = UsbBuilderProfileItemTier.Optional,
            ManifestEntryName = "Angry IP Scanner 3.9.3 (Windows setup)",
            UsbRelativePath = @"Tools\Portable\Network\ipscan-3.9.3-setup.exe",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(40 * Mb, "portable tools"),
            Notes = "Curated network / port / DNS portable utilities."
        };

        yield return new UsbBuilderProfileItem
        {
            Id = "diag.security-tools",
            CategoryId = "diagnostics",
            DisplayName = "Security & malware-removal bundle",
            Subcategory = "Security",
            Kind = UsbBuilderProfileItemKind.ManagedDownload,
            Tier = UsbBuilderProfileItemTier.Optional,
            ManifestEntryName = "KeePassXC 2.7.12 Win64 Portable (zip)",
            UsbRelativePath = @"Tools\Portable\Security\KeePassXC-2.7.12-Win64.zip",
            SpaceEstimate = UsbBuilderProfileSpaceEstimate.Fixed(200 * Mb, "portable tools"),
            Notes = "On-demand scanners and offline cleanup tools."
        };

    }

    private static IEnumerable<UsbBuilderProfileItem> BuildManifestItems(IReadOnlyList<UsbBuilderProfileItem> curatedItems)
    {
        var manifestPath = ResolveBundledManifestPath();
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            yield break;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        }
        catch
        {
            yield break;
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }

            foreach (var item in items.EnumerateArray())
            {
                var name = GetJsonString(item, "name");
                var type = GetJsonString(item, "type");
                var dest = GetJsonString(item, "dest");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dest))
                {
                    continue;
                }

                if (IsCoveredByCuratedItem(curatedItems, name, dest))
                {
                    continue;
                }

                var categoryId = ClassifyManifestCategory(item);
                if (!UsbBuilderProfileCatalog.TryGet(categoryId, out _))
                {
                    continue;
                }

                var kind = ClassifyManifestKind(item, categoryId);
                var requiresUserSupplied = RequiresUserSuppliedMedia(item, categoryId);
                var url = GetJsonString(item, "url");
                var notes = FirstNonEmpty(
                    GetJsonString(item, "notes"),
                    GetJsonString(item, "recommendedUse"),
                    GetJsonString(item, "actionReason"),
                    "Bundled manifest item.");

                yield return new UsbBuilderProfileItem
                {
                    Id = $"manifest.{categoryId}.{Slug(name)}",
                    CategoryId = categoryId,
                    DisplayName = name,
                    Subcategory = BuildSubcategory(item, kind, categoryId),
                    ShortDescription = notes,
                    Source = BuildSource(item, url),
                    Kind = kind,
                    Tier = UsbBuilderProfileItemTier.Optional,
                    SpaceEstimate = BuildManifestSpaceEstimate(item, kind, requiresUserSupplied, type),
                    ManifestEntryName = name,
                    UsbRelativePath = dest,
                    Notes = notes,
                    RequiresUserSuppliedMedia = requiresUserSupplied,
                    VendorPortalOnly = IsVendorPortal(item, categoryId),
                    LargePayload = IsLargeManifestPayload(item, kind)
                };
            }
        }
    }

    private static string? ResolveBundledManifestPath()
    {
        var candidates = new List<string>();
        var baseDir = AppContext.BaseDirectory;

        if (!string.IsNullOrWhiteSpace(baseDir))
        {
            candidates.Add(Path.Combine(baseDir, "ForgerEMS.updates.json"));
            candidates.Add(Path.Combine(baseDir, "manifests", "ForgerEMS.updates.json"));
            candidates.Add(Path.Combine(baseDir, "backend", "ForgerEMS.updates.json"));
            candidates.Add(Path.Combine(baseDir, "backend", "manifests", "ForgerEMS.updates.json"));
        }

        var dir = new DirectoryInfo(baseDir);
        while (dir is not null)
        {
            candidates.Add(Path.Combine(dir.FullName, "manifests", "ForgerEMS.updates.json"));
            candidates.Add(Path.Combine(dir.FullName, "backend", "ForgerEMS.updates.json"));
            if (File.Exists(Path.Combine(dir.FullName, "ForgerEMS.sln")))
            {
                candidates.Add(Path.Combine(dir.FullName, "manifests", "ForgerEMS.updates.json"));
                break;
            }

            dir = dir.Parent;
        }

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists);
    }

    private static bool IsCoveredByCuratedItem(
        IReadOnlyList<UsbBuilderProfileItem> curatedItems,
        string manifestName,
        string manifestDest)
    {
        foreach (var item in curatedItems)
        {
            if (!string.IsNullOrWhiteSpace(item.ManifestEntryName) &&
                (manifestName.Contains(item.ManifestEntryName, StringComparison.OrdinalIgnoreCase) ||
                 item.ManifestEntryName.Contains(manifestName, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(item.UsbRelativePath) &&
                string.Equals(NormalizePath(item.UsbRelativePath), NormalizePath(manifestDest), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ClassifyManifestCategory(JsonElement item)
    {
        var explicitCategory = GetJsonString(item, "categoryId");
        if (!string.IsNullOrWhiteSpace(explicitCategory))
        {
            return explicitCategory.Trim().ToLowerInvariant();
        }

        return UsbBuilderProfileFullManagedDownloadPlanner.ClassifyCategoryIdForPath(
            GetJsonString(item, "dest"),
            GetJsonString(item, "name"),
            GetJsonString(item, "family"));
    }

    private static UsbBuilderProfileItemKind ClassifyManifestKind(JsonElement item, string categoryId)
    {
        var type = GetJsonString(item, "type");
        var dest = GetJsonString(item, "dest");
        var leaf = Path.GetFileName(dest);
        var manifestKind = GetJsonString(item, "kind");
        var actionLabel = GetJsonString(item, "actionLabel");
        var downloadMode = GetJsonString(item, "downloadMode");

        if (string.Equals(type, "file", StringComparison.OrdinalIgnoreCase))
        {
            if (dest.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) ||
                leaf.Contains(".iso.", StringComparison.OrdinalIgnoreCase))
            {
                return UsbBuilderProfileItemKind.Iso;
            }

            if (manifestKind.Contains("driver", StringComparison.OrdinalIgnoreCase) ||
                NormalizePath(dest).StartsWith("drivers\\", StringComparison.OrdinalIgnoreCase))
            {
                return UsbBuilderProfileItemKind.Driver;
            }

            return UsbBuilderProfileItemKind.ManagedDownload;
        }

        if (leaf.StartsWith("GUIDE - ", StringComparison.OrdinalIgnoreCase) ||
            actionLabel.Contains("Guide", StringComparison.OrdinalIgnoreCase))
        {
            return UsbBuilderProfileItemKind.HtmlGuide;
        }

        if (string.Equals(categoryId, "oem-tools", StringComparison.OrdinalIgnoreCase) ||
            manifestKind.Contains("driver", StringComparison.OrdinalIgnoreCase) ||
            downloadMode.Contains("OEM", StringComparison.OrdinalIgnoreCase) ||
            downloadMode.Contains("Firmware", StringComparison.OrdinalIgnoreCase))
        {
            return UsbBuilderProfileItemKind.VendorLink;
        }

        return UsbBuilderProfileItemKind.OfficialPage;
    }

    private static UsbBuilderProfileSpaceEstimate BuildManifestSpaceEstimate(
        JsonElement item,
        UsbBuilderProfileItemKind kind,
        bool requiresUserSupplied,
        string type)
    {
        if (requiresUserSupplied)
        {
            return UsbBuilderProfileSpaceEstimate.UserSupplied("varies");
        }

        var estimated = GetJsonLong(item, "estimatedSizeBytes");
        if (estimated is > 0)
        {
            return UsbBuilderProfileSpaceEstimate.Fixed(estimated.Value, "manifest estimate");
        }

        if (string.Equals(type, "file", StringComparison.OrdinalIgnoreCase))
        {
            return UsbBuilderProfileSpaceEstimate.UnknownManaged("size unknown");
        }

        return kind == UsbBuilderProfileItemKind.HtmlGuide
            ? UsbBuilderProfileSpaceEstimate.Fixed(80 * 1024, "guide/shortcut")
            : UsbBuilderProfileSpaceEstimate.Fixed(2 * 1024, ".url shortcut");
    }

    private static string BuildSubcategory(JsonElement item, UsbBuilderProfileItemKind kind, string categoryId)
    {
        var family = GetJsonString(item, "family");
        var osCategory = GetJsonString(item, "osCategory");
        var actionLabel = GetJsonString(item, "actionLabel");

        return FirstNonEmpty(
            osCategory,
            family,
            actionLabel,
            kind.KindLabelFallback(),
            UsbBuilderProfileCatalog.GetSummaryLabel(categoryId));
    }

    private static string BuildSource(JsonElement item, string url)
    {
        var sourceTrust = GetJsonString(item, "sourceTrust");
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return string.IsNullOrWhiteSpace(sourceTrust)
                ? uri.Host
                : $"{sourceTrust}: {uri.Host}";
        }

        return FirstNonEmpty(sourceTrust, GetJsonString(item, "sourceType"), "ForgerEMS manifest");
    }

    private static bool RequiresUserSuppliedMedia(JsonElement item, string categoryId)
    {
        var dest = GetJsonString(item, "dest");
        var name = GetJsonString(item, "name");
        var actionLabel = GetJsonString(item, "actionLabel");
        var downloadMode = GetJsonString(item, "downloadMode");
        var combined = $"{dest} {name} {actionLabel} {downloadMode}";

        return string.Equals(categoryId, "legacy-windows", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains("Manual ISO", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains("Manual Installer", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains("Manual IPSW", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains("Manual Firmware", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains("FirmwareBlocked", StringComparison.OrdinalIgnoreCase) ||
               (GetJsonBool(item, "requiresManualMedia") &&
                (string.Equals(categoryId, "macos", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(categoryId, "android", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(categoryId, "ios-ipados", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsVendorPortal(JsonElement item, string categoryId)
    {
        var downloadMode = GetJsonString(item, "downloadMode");
        var dest = NormalizePath(GetJsonString(item, "dest"));
        return string.Equals(categoryId, "oem-tools", StringComparison.OrdinalIgnoreCase) ||
               dest.StartsWith("drivers\\", StringComparison.OrdinalIgnoreCase) ||
               downloadMode.Contains("OEM", StringComparison.OrdinalIgnoreCase) ||
               downloadMode.Contains("Firmware", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLargeManifestPayload(JsonElement item, UsbBuilderProfileItemKind kind)
    {
        var estimated = GetJsonLong(item, "estimatedSizeBytes");
        return estimated >= Gb ||
               kind == UsbBuilderProfileItemKind.Iso ||
               GetJsonString(item, "dest").EndsWith(".iso", StringComparison.OrdinalIgnoreCase);
    }

    private static string Slug(string value)
    {
        var chars = new List<char>(value.Length);
        var lastWasDash = false;
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                chars.Add(ch);
                lastWasDash = false;
                continue;
            }

            if (!lastWasDash)
            {
                chars.Add('-');
                lastWasDash = true;
            }
        }

        var slug = new string(chars.ToArray()).Trim('-');
        if (slug.Length > 80)
        {
            slug = slug[..80].Trim('-');
        }

        return string.IsNullOrWhiteSpace(slug) ? "item" : slug;
    }

    private static string NormalizePath(string? path) =>
        (path ?? string.Empty).Trim().Replace('/', '\\');

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string GetJsonString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
    }

    private static bool GetJsonBool(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.True;

    private static long? GetJsonLong(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return value.TryGetInt64(out var result) ? result : null;
    }
}

file static class UsbBuilderProfileItemKindExtensions
{
    public static string KindLabelFallback(this UsbBuilderProfileItemKind kind) =>
        kind switch
        {
            UsbBuilderProfileItemKind.ManagedDownload => "Managed Download",
            UsbBuilderProfileItemKind.OfficialPage => "Official Link",
            UsbBuilderProfileItemKind.ManualMediaFolder => "Manual Folder",
            UsbBuilderProfileItemKind.Shortcut => "Shortcut",
            UsbBuilderProfileItemKind.HtmlGuide => "Guidance",
            UsbBuilderProfileItemKind.DropFolder => "Manual Folder",
            UsbBuilderProfileItemKind.Iso => "ISO",
            UsbBuilderProfileItemKind.Tool => "Tool",
            UsbBuilderProfileItemKind.VendorLink => "Vendor",
            UsbBuilderProfileItemKind.RecoveryMedia => "Recovery",
            UsbBuilderProfileItemKind.Driver => "Driver",
            _ => "Item"
        };
}
