using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using VentoyToolkitSetup.Wpf.Infrastructure;

namespace ForgerEMS.Wpf.Tests;

public sealed class ResourceTrademarkSafetyTests
{
    private static readonly string[] UnsafeDecorativeTerms =
    [
        "windows-logo",
        "microsoft-logo",
        "ubuntu-logo",
        "kali-logo",
        "linux-mint-logo",
        "mint-logo",
        "tux",
        "ventoy-logo",
        "rufus-logo",
        "balenaetcher-logo",
        "etcher-logo",
        "hwinfo-logo",
        "crystaldiskinfo-logo",
        "clonezilla-logo",
        "gparted-logo",
        "systemrescue-logo",
        "rustdesk-logo",
        "angry-ip-scanner-logo",
        "driverstoreexplorer-logo",
        "nvidia-logo",
        "intel-logo",
        "amd-logo",
        "dell-logo",
        "hp-logo",
        "lenovo-logo",
        "msi-logo",
        "asus-logo",
        "acer-logo",
        "realtek-logo",
        "gigabyte-logo",
        "asrock-logo",
        "fedora-logo",
        "fwupd-logo",
        "lvfs-logo"
    ];

    private static readonly string[] UnsafeBackgroundLabels =
    [
        "Text=\"Ubuntu\"",
        "Text=\"Kali Linux\"",
        "Text=\"Linux Mint\"",
        "Text=\"MemTest86+\"",
        "Text=\"HWInfo\"",
        "Text=\"HWiNFO\"",
        "Text=\"CrystalDiskInfo\"",
        "Text=\"Rescuezilla\"",
        "Text=\"Clonezilla\"",
        "Text=\"GParted\"",
        "Text=\"SystemRescue\"",
        "Text=\"DriverStoreExplorer\"",
        "Text=\"RustDesk\"",
        "Text=\"Angry IP Scanner\"",
        "Text=\"Rufus\"",
        "Text=\"Ventoy\"",
        "Text=\"balenaEtcher\"",
        "Text=\"Ventoy Core\""
    ];

    private const string IndependenceDisclaimer =
        "ForgerEMS is independent and is not affiliated with, sponsored by, or endorsed by Microsoft, Linux distributions, hardware vendors, driver vendors, or third-party tools referenced in the app. Names are used only to identify compatibility, official resources, or supported technician workflows.";

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

    [Fact]
    public void WpfPublicAssets_DoNotUseVendorLogoFilenames()
    {
        var assetsRoot = Path.Combine(RepoRoot, "src", "ForgerEMS.Wpf", "Assets");
        var publicAssetNames = Directory.GetFiles(assetsRoot, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToArray();

        foreach (var assetName in publicAssetNames)
        {
            foreach (var term in UnsafeDecorativeTerms)
            {
                Assert.DoesNotContain(term, assetName, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void WpfImageReferences_DoNotPointAtUnsafeLogoAssets()
    {
        var xamlAndProjectFiles = Directory.GetFiles(Path.Combine(RepoRoot, "src", "ForgerEMS.Wpf"), "*.*", SearchOption.TopDirectoryOnly)
            .Where(path => path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var referencePattern = new Regex("(Source|Include)=\"(?<path>[^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (var file in xamlAndProjectFiles)
        {
            var text = File.ReadAllText(file);
            foreach (Match match in referencePattern.Matches(text))
            {
                var referencedPath = match.Groups["path"].Value;
                if (!referencedPath.Contains("Assets", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var term in UnsafeDecorativeTerms)
                {
                    Assert.DoesNotContain(term, referencedPath, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    public void MainCircuitBackground_UsesGenericVisibleLabels()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot, "src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        var start = xaml.IndexOf("x:Name=\"CircuitBackgroundLayer\"", StringComparison.Ordinal);
        var end = xaml.IndexOf("x:Name=\"BackgroundReadabilityVeil\"", StringComparison.Ordinal);

        Assert.True(start >= 0, "Could not find CircuitBackgroundLayer in MainWindow.xaml.");
        Assert.True(end > start, "Could not find the end of the circuit background layer in MainWindow.xaml.");

        var backgroundLayer = xaml[start..end];
        foreach (var label in UnsafeBackgroundLabels)
        {
            Assert.DoesNotContain(label, backgroundLayer, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("Text=\"Multiboot Core\"", backgroundLayer, StringComparison.Ordinal);
        Assert.Contains("Text=\"Network Radar\"", backgroundLayer, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfProject_PackagesOnlySafeBackgroundAssets()
    {
        var projectText = File.ReadAllText(Path.Combine(RepoRoot, "src", "ForgerEMS.Wpf", "ForgerEMS.Wpf.csproj"));

        Assert.Contains("Assets\\ForgerEMS_CommandCenterBackground.png", projectText, StringComparison.Ordinal);
        Assert.Contains("Assets\\KyraAdvancedBackground.png", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("Archived", projectText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Unsafe", projectText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LegacyBackground", projectText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BackgroundGenerator_UsesSafeLabelZonesAndGenericLabels()
    {
        var generatorText = File.ReadAllText(Path.Combine(RepoRoot, "tools", "Generate-SafeCommandCenterBackground.ps1"));

        Assert.Contains("ModuleLabelSafeHeight", generatorText, StringComparison.Ordinal);
        Assert.Contains("ModuleLabelSafeWidth", generatorText, StringComparison.Ordinal);
        Assert.Contains("Draw-LabelPill", generatorText, StringComparison.Ordinal);
        Assert.Contains("Draw-FeShield", generatorText, StringComparison.Ordinal);

        foreach (var label in new[]
                 {
                     "Desktop Image",
                     "Modern Image",
                     "Server Image",
                     "Live Terminal",
                     "Security Live",
                     "Desktop Live",
                     "Image Restore",
                     "Disk Clone",
                     "Recovery Kit",
                     "Multiboot USB",
                     "Boot Writer",
                     "Image Flasher",
                     "Memory Check",
                     "Hardware Info",
                     "Disk Health",
                     "Driver Store",
                     "Remote Screen",
                     "Network Radar",
                     "MULTIBOOT`nCORE",
                     "MEDIC USB"
                 })
        {
            Assert.Contains(label, generatorText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DocsAndInAppLegal_ContainIndependenceDisclaimer()
    {
        foreach (var relativePath in new[]
                 {
                     Path.Combine("docs", "FAQ.md"),
                     Path.Combine("docs", "LEGAL.md"),
                     Path.Combine("docs", "ABOUT_FORGEREMS.md"),
                     Path.Combine("docs", "DEV_BETA_SMOKE_CHECKLIST_v1.2.3.md")
                 })
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot, relativePath));
            Assert.Contains("ForgerEMS is independent", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("not affiliated with, sponsored by, or endorsed by", text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains(IndependenceDisclaimer, InfoDocumentTexts.BuildAbout("1.2.3-preview.1", "ForgerEMS v1.2.3 Public Preview", "frontend", "backend"), StringComparison.Ordinal);
        Assert.Contains(IndependenceDisclaimer, InfoDocumentTexts.BuildLegal(), StringComparison.Ordinal);
    }
}
