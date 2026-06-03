using System;
using System.IO;
using System.Linq;

namespace ForgerEMS.Wpf.Tests;

public sealed class ForgerSensorStackProductTests
{
    [Fact]
    public void Docs_DefineForgerOwnedSensorStackWithoutThirdPartyRuntimeRequirement()
    {
        var stack = Read("docs", "FORGER-SENSOR-STACK.md");
        var roadmap = Read("docs", "FORGER-DEEP-SENSOR-DRIVER-ROADMAP.md");
        var limitations = Read("docs", "SENSOR-LIMITATIONS.md");
        var faq = Read("docs", "FAQ.md");
        var privacy = Read("docs", "PRIVACY.md");

        var combined = string.Join(Environment.NewLine, stack, roadmap, limitations, faq, privacy);

        Assert.Contains("Forger Sensor Core", combined, StringComparison.Ordinal);
        Assert.Contains("Forger Sensor Service", combined, StringComparison.Ordinal);
        Assert.Contains("Forger Deep Sensor Driver", combined, StringComparison.Ordinal);
        Assert.Contains("No cloud upload", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no paid third-party tool requirement", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no fake sensor values", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no fan, voltage, clock, BIOS, firmware", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("requires HWiNFO", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("requires AIDA64", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("requires CPU-Z", combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BackendReport_IncludesForgerSensorStackStateAndRoadmapBoundaries()
    {
        var script = Read("backend", "SystemIntelligence", "Invoke-ForgerEMSSystemScan.ps1");

        Assert.Contains("\"forgerSensorStack\"", script, StringComparison.Ordinal);
        Assert.Contains("forgerSensorCore = \"Active\"", script, StringComparison.Ordinal);
        Assert.Contains("sensorService = \"Not installed\"", script, StringComparison.Ordinal);
        Assert.Contains("deepSensorDriver = \"Not included\"", script, StringComparison.Ordinal);
        Assert.Contains("externalTools = \"Not required\"", script, StringComparison.Ordinal);
        Assert.Contains("generatesFakeSensorValues = $false", script, StringComparison.Ordinal);
        Assert.Contains("Some board-level sensors require the future Forger Deep Sensor Driver.", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ForgerEMS Admin Sensor Bridge", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ForgerEMS Signed Driver Provider", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Roadmap_DoesNotAddUnsafeDriverImplementationOrControlWrites()
    {
        var roadmap = Read("docs", "FORGER-DEEP-SENSOR-DRIVER-ROADMAP.md");
        var repoRoot = FindRepoRoot();
        var sourceRoots = new[] { Path.Combine(repoRoot, "src"), Path.Combine(repoRoot, "backend") };
        var sourceFiles = sourceRoots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.GetFiles(root, "*.*", SearchOption.AllDirectories))
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText);
        var combined = string.Join(Environment.NewLine, sourceFiles);

        Assert.Contains("It is not included in this build.", roadmap, StringComparison.Ordinal);
        Assert.DoesNotContain("fan-control", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("voltage-control", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("clock-control", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BIOS-write capability", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("firmware-write capability", combined, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine(FindRepoRoot(), Path.Combine(segments)));

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ForgerEMS.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate ForgerEMS.sln.");
    }
}
