using System;
using System.IO;
using System.Linq;
using VentoyToolkitSetup.Wpf.Infrastructure;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

// Guards Parts C–E of the System Intelligence / Dr. Forge direction pass:
//   * Elevated Scan user-visible labels renamed to Admin Inventory Scan
//   * Standard Scan user-visible labels renamed to Windows Inventory Scan
//   * Dr. Forge bridge/roadmap copy exists and is read-only-honest
//   * Companion docs ship under docs/
public sealed class DrForgeRoadmapAndAdminInventoryRenameTests
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

            throw new InvalidOperationException("Could not locate ForgerEMS.sln.");
        }
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    [Fact]
    public void SystemIntelligenceScanButtons_AreRemovedFromShell()
    {
        // The in-app System Intelligence scan actions (Admin/Elevated/Standard
        // Inventory Scan) moved to Dr. Forge with the rest of the diagnostics
        // surface. They must not appear in the main ForgerEMS window.
        var xaml = Read("src", "ForgerEMS.Wpf", "MainWindow.xaml");
        Assert.DoesNotContain("Content=\"Admin Inventory Scan\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Elevated Scan\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Run Standard Scan\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void WelcomeCenter_NoLongerRecommendsAnInventoryScan()
    {
        // Requirement: the Welcome Center must not recommend the removed System
        // Intelligence / Diagnostics actions.
        var xaml = Read("src", "ForgerEMS.Wpf", "MainWindow.xaml");
        Assert.DoesNotContain("Content=\"Run Windows Inventory Scan\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Run Standard Scan\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OnRunSystemScanFromWelcomeClick", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DrForgeRoadmap_ContainsHonestStatusAndScope()
    {
        var copy = InfoDocumentTexts.BuildDrForgeRoadmap();
        Assert.Contains("Dr. Forge Advanced Sensors", copy, StringComparison.Ordinal);
        Assert.Contains("CLI bridge", copy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("packaged drforge.exe", copy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("read-only", copy, StringComparison.OrdinalIgnoreCase);
        // First version must NOT promise fan/voltage/charging control.
        Assert.Contains("No fan control", copy, StringComparison.Ordinal);
        Assert.Contains("voltage control", copy, StringComparison.OrdinalIgnoreCase);
        // Honest truthfulness clause.
        Assert.Contains("not fake", copy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not claim full HWiNFO / CPU-Z / LibreHardwareMonitor parity", copy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("remains unavailable", copy, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DrForgeRoadmap_DoesNotPromiseAFakeDownload()
    {
        var copy = InfoDocumentTexts.BuildDrForgeRoadmap();
        // The bridge accepts an already trusted packaged CLI path. There must be
        // no CTA-style download claim unless a real release channel exists.
        Assert.Contains("intentionally absent", copy, StringComparison.Ordinal);
        Assert.DoesNotContain("Download Dr. Forge now", copy, StringComparison.Ordinal);
        Assert.DoesNotContain("Click Download Dr. Forge", copy, StringComparison.Ordinal);
        Assert.DoesNotContain("fake download", copy, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DrForgeMainWindow_HasNoLiveDownloadButton()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot, "src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        Assert.DoesNotContain("Content=\"Download Dr. Forge\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Install Dr. Forge\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DrForgeRoadmapDoc_ExistsAndCoversFirstVersionScope()
    {
        var doc = Read("docs", "DR-FORGE-ADVANCED-SENSORS.md");
        Assert.Contains("Status:", doc, StringComparison.Ordinal);
        Assert.Contains("CLI bridge", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("packaged CLI", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Honest truthfulness", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not claim full HWiNFO / CPU-Z / LibreHardwareMonitor parity", doc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DrForgeIntegrationDoc_DefinesPackagedCliContract()
    {
        var doc = Read("docs", "FORGEREMS-DR-FORGE-INTEGRATION.md");
        Assert.Contains("drforge.exe", doc, StringComparison.Ordinal);
        Assert.Contains("drforge-cli-release-manifest.json", doc, StringComparison.Ordinal);
        Assert.Contains("forge-hardware-intake-report/1.0", doc, StringComparison.Ordinal);
        Assert.Contains("Unavailable", doc, StringComparison.Ordinal);
        Assert.Contains("does not load Dr. Forge WPF or provider internals", doc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SensorStackCopy_MentionsDrForgePlannedRow()
    {
        var vm = Read("src", "ForgerEMS.Wpf", "ViewModels", "MainViewModel.cs");
        Assert.Contains("Dr. Forge Advanced Sensors", vm, StringComparison.Ordinal);
        Assert.Contains("CLI bridge available when configured", vm, StringComparison.Ordinal);
        Assert.Contains("Admin Inventory Scan", vm, StringComparison.Ordinal);
    }

    [Fact]
    public void LearnAboutDrForge_HandlerOpensInfoWindow()
    {
        var codeBehind = Read("src", "ForgerEMS.Wpf", "MainWindow.xaml.cs");
        Assert.Contains("OnLearnAboutDrForgeClick", codeBehind, StringComparison.Ordinal);
        Assert.Contains("BuildDrForgeRoadmap", codeBehind, StringComparison.Ordinal);
    }
}
