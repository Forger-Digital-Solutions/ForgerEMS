using System;
using System.IO;
using VentoyToolkitSetup.Wpf.Infrastructure;

namespace ForgerEMS.Wpf.Tests;

public sealed class DeepSensorDisclosureCopyTests
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

    [Fact]
    public void Faq_ExplainsDeepSensorModeAndMissingSensors()
    {
        var text = Read("docs", "FAQ.md");

        Assert.Contains("What is Deep Sensor Mode?", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("local read-only sensor mode", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not require separate user downloads", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Unavailable readings are coverage limits, not failures", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Does ForgerEMS control my fans, voltage, clocks, BIOS, or firmware?", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Privacy_SaysSensorDataIsLocalAndUserShared()
    {
        var text = Read("docs", "PRIVACY.md");

        Assert.Contains("Deep Sensor Mode", text, StringComparison.Ordinal);
        Assert.Contains("not automatically uploaded", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not automatically sent", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("You choose when to copy, export, or share reports", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("review reports before sharing", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Legal_IncludesLibreHardwareMonitorMplAndNoProprietarySensorRedistribution()
    {
        var text = Read("docs", "LEGAL.md");

        Assert.Contains("LibreHardwareMonitorLib", text, StringComparison.Ordinal);
        Assert.Contains("MPL-2.0", text, StringComparison.Ordinal);
        Assert.Contains("providers/sensors/LICENSES/LibreHardwareMonitor-MPL-2.0.txt", text, StringComparison.Ordinal);
        Assert.Contains("providers/sensors/THIRD-PARTY-NOTICES.txt", text, StringComparison.Ordinal);
        Assert.Contains("HWiNFO, AIDA64, CPU-Z", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("proprietary sensor tools", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("control fans, voltage, clocks", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BIOS, or firmware", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void About_MentionsHardwareXrayAndBundledReadOnlyProviders()
    {
        var docsText = Read("docs", "ABOUT_FORGEREMS.md");
        var appText = InfoDocumentTexts.BuildAbout("1.2.0-preview.1", "ForgerEMS v1.2.0 Public Preview", "frontend", "backend");

        foreach (var text in new[] { docsText, appText })
        {
            Assert.Contains("Hardware X-Ray", text, StringComparison.Ordinal);
            Assert.Contains("local read-only", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Deep Sensor Mode", text, StringComparison.Ordinal);
            Assert.Contains("LibreHardwareMonitor", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SettingsAndInstallerCopy_DiscloseReadOnlyNoControlBehavior()
    {
        var mainViewModel = Read("src", "ForgerEMS.Wpf", "ViewModels", "MainViewModel.cs");
        var installer = Read("installer", "ForgerEMS.iss");

        Assert.Contains("Read-only local sensors", mainViewModel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ForgerEMS does not control fans, voltages, clocks, BIOS, or firmware", mainViewModel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Enable Deep Sensor Mode (local read-only hardware sensors)", installer, StringComparison.Ordinal);
        Assert.Contains("bundled local read-only sensor provider", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Some readings depend on firmware, drivers, permissions, and hardware support", installer, StringComparison.Ordinal);
        Assert.Contains("MPL-2.0", installer, StringComparison.Ordinal);
    }

    [Fact]
    public void BetaReleaseGeneratedCopy_SaysNoSeparateLibreHardwareMonitorDownloadNeeded()
    {
        var script = Read("tools", "build-release.ps1");

        Assert.Contains("No separate LibreHardwareMonitor download is needed", script, StringComparison.Ordinal);
        Assert.Contains("Extract the ZIP", script, StringComparison.Ordinal);
        Assert.Contains("FORGEREMS_DEEP_SENSOR_MODE", script, StringComparison.Ordinal);
        Assert.Contains("Review before sharing", script, StringComparison.Ordinal);
        Assert.Contains("Do not send product keys, API keys, tokens, passwords", script, StringComparison.Ordinal);
    }

    [Fact]
    public void SupportAndCopySummary_WarnToReviewBeforeSharing()
    {
        var supportBundle = Read("src", "ForgerEMS.Wpf", "Services", "SupportBundleExporter.cs");
        var mainViewModel = Read("src", "ForgerEMS.Wpf", "ViewModels", "MainViewModel.cs");

        Assert.Contains("Review before sharing", supportBundle, StringComparison.Ordinal);
        Assert.Contains("hardware details, network adapter data, USB device details", supportBundle, StringComparison.Ordinal);
        Assert.Contains("Review before sharing", mainViewModel, StringComparison.Ordinal);
        Assert.Contains("hardware, network adapter, USB device, and diagnostic details", mainViewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void UserFacingCopy_DoesNotUseForbiddenHiddenSystemInfoPhrase()
    {
        var relativeFiles = new[]
        {
            "README.md",
            Path.Combine("docs", "FAQ.md"),
            Path.Combine("docs", "LEGAL.md"),
            Path.Combine("docs", "PRIVACY.md"),
            Path.Combine("docs", "ABOUT_FORGEREMS.md"),
            Path.Combine("installer", "ForgerEMS.iss"),
            Path.Combine("installer", "ForgerEMS-Installed-README.txt"),
            Path.Combine("src", "ForgerEMS.Wpf", "Infrastructure", "InfoDocumentTexts.cs")
        };

        foreach (var relative in relativeFiles)
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot, relative));
            Assert.DoesNotContain("hidden system info", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ThirdPartyNotices_DocumentLibreHardwareMonitorProviderPath()
    {
        var general = Read("docs", "THIRD_PARTY_NOTICES.md");
        var sensors = Read("docs", "THIRD-PARTY-SENSOR-NOTICES.md");
        var licensePath = Path.Combine(RepoRoot, "providers", "sensors", "LICENSES", "LibreHardwareMonitor-MPL-2.0.txt");

        Assert.Contains("LibreHardwareMonitorLib", general, StringComparison.Ordinal);
        Assert.Contains("providers/sensors/LibreHardwareMonitorLib.dll", general, StringComparison.Ordinal);
        Assert.Contains("providers/sensors/LICENSES/LibreHardwareMonitor-MPL-2.0.txt", sensors, StringComparison.Ordinal);
        Assert.True(File.Exists(licensePath), "Missing packaged LibreHardwareMonitor MPL-2.0 license file.");
    }

    private static string Read(params string[] parts)
    {
        var all = new string[parts.Length + 1];
        all[0] = RepoRoot;
        Array.Copy(parts, 0, all, 1, parts.Length);
        return File.ReadAllText(Path.Combine(all));
    }
}
