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
    public void MainWindow_UsesPackagedStaticCommandCenterBackgroundOnly()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot, "src", "ForgerEMS.Wpf", "MainWindow.xaml"));

        Assert.Contains("x:Name=\"CommandCenterBackgroundImage\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Source=\"Assets/ForgerEMS_CommandCenterBackground.png\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BackgroundReadabilityVeil\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"CircuitBackgroundLayer\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"TraceLightCanvas\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CircuitPulseTraceStyle", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("BackgroundDetailComboBox", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ComboBoxItem Content=\"Animated\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_ShowsEntireStaticBackgroundWithoutCropping()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot, "src", "ForgerEMS.Wpf", "MainWindow.xaml"));

        // The named main background image must display the entire artwork
        // (Uniform, centered, never cropped). An optional blurred filler element
        // may use UniformToFill behind it, so we scope assertions to the named
        // element only.
        const string nameMarker = "x:Name=\"CommandCenterBackgroundImage\"";
        var start = xaml.IndexOf(nameMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, "CommandCenterBackgroundImage was not found in MainWindow.xaml.");
        var end = xaml.IndexOf("/>", start, StringComparison.Ordinal);
        Assert.True(end > start, "Could not find the self-closing end of CommandCenterBackgroundImage.");
        var element = xaml[start..end];

        Assert.Contains("Stretch=\"Uniform\"", element, StringComparison.Ordinal);
        Assert.DoesNotContain("Stretch=\"UniformToFill\"", element, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Center\"", element, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment=\"Center\"", element, StringComparison.Ordinal);
        Assert.Contains("IsHitTestVisible=\"False\"", element, StringComparison.Ordinal);
        Assert.Contains("Source=\"Assets/ForgerEMS_CommandCenterBackground.png\"", element, StringComparison.Ordinal);

        // No animation systems may sneak back in around the background.
        Assert.DoesNotContain("BeginAnimation", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Storyboard", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DoubleAnimation", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticCommandCenterBackgroundAsset_ExistsAsPackagedPng()
    {
        var backgroundPath = Path.Combine(RepoRoot, "src", "ForgerEMS.Wpf", "Assets", "ForgerEMS_CommandCenterBackground.png");
        Assert.True(File.Exists(backgroundPath), "The static command-center background asset is missing.");

        var bytes = File.ReadAllBytes(backgroundPath);
        Assert.True(bytes.Length > 1_000_000, "The upgraded background should be the packaged high-detail PNG, not the old small generated asset.");
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, bytes.Take(8).ToArray());

        var width = ReadBigEndianInt32(bytes, 16);
        var height = ReadBigEndianInt32(bytes, 20);
        Assert.True(width >= 1500, $"Expected a wide command-center background. Actual width: {width}.");
        Assert.True(height >= 900, $"Expected a tall command-center background. Actual height: {height}.");
        Assert.InRange(width / (double)height, 1.60, 1.75);
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
    public void BackgroundGenerator_IsRemovedSoPackagedImageRemainsSourceOfTruth()
    {
        Assert.False(
            File.Exists(Path.Combine(RepoRoot, "tools", "Generate-SafeCommandCenterBackground.ps1")),
            "The command-center background should not be regenerated by a local script.");
    }

    [Fact]
    public void DocsAndInAppLegal_ContainIndependenceDisclaimer()
    {
        foreach (var relativePath in new[]
                 {
                     Path.Combine("docs", "FAQ.md"),
                     Path.Combine("docs", "LEGAL.md"),
                     Path.Combine("docs", "ABOUT_FORGEREMS.md"),
                     Path.Combine("docs", "DEV_BETA_SMOKE_CHECKLIST_v1.2.4.md")
                 })
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot, relativePath));
            Assert.Contains("ForgerEMS is independent", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("not affiliated with, sponsored by, or endorsed by", text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains(IndependenceDisclaimer, InfoDocumentTexts.BuildAbout("1.2.4-preview.3", "ForgerEMS v1.2.4 Public Preview", "frontend", "backend"), StringComparison.Ordinal);
        Assert.Contains(IndependenceDisclaimer, InfoDocumentTexts.BuildLegal(), StringComparison.Ordinal);
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
        (bytes[offset] << 24) |
        (bytes[offset + 1] << 16) |
        (bytes[offset + 2] << 8) |
        bytes[offset + 3];
}
