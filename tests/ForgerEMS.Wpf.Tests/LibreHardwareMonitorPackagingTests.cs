using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace ForgerEMS.Wpf.Tests;

public sealed class LibreHardwareMonitorPackagingTests
{
    [Fact]
    public void WpfProject_PinsLibreHardwareMonitorLibAndCopiesToProvidersSensors()
    {
        var csproj = FindRepoFile("src", "ForgerEMS.Wpf", "ForgerEMS.Wpf.csproj");
        var xml = XDocument.Load(csproj);
        var ns = xml.Root?.GetDefaultNamespace() ?? XNamespace.None;
        var refs = xml.Descendants(ns + "PackageReference")
            .Select(e => new
            {
                Include = e.Attribute("Include")?.Value,
                Version = e.Attribute("Version")?.Value
            })
            .ToArray();
        var libre = refs.FirstOrDefault(r =>
            string.Equals(r.Include, "LibreHardwareMonitorLib", StringComparison.Ordinal));
        Assert.NotNull(libre);
        Assert.Equal("0.9.6", libre!.Version);

        var contents = xml.Descendants(ns + "Content")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();
        Assert.Contains(contents, c => c.Replace('\\', '/').Contains("providers/sensors", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(contents, c => c.Contains("ForgerLibreHardwareMonitorDll", StringComparison.OrdinalIgnoreCase));

        var copyTargets = xml.Descendants(ns + "Target")
            .Where(t => string.Equals(t.Attribute("Name")?.Value, "CopyLibreHardwareMonitorProviderToOutput", StringComparison.Ordinal) ||
                        string.Equals(t.Attribute("Name")?.Value, "CopyLibreHardwareMonitorProviderToPublish", StringComparison.Ordinal))
            .SelectMany(t => t.Descendants(ns + "Copy"))
            .Select(c => c.Attribute("DestinationFiles")?.Value ?? string.Empty)
            .ToArray();
        Assert.Contains(copyTargets, d => d.Replace('\\', '/').Contains("providers/sensors/LibreHardwareMonitorLib.dll", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RuntimeProbe_UsesPackagedProvidersSensorsPath()
    {
        var source = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "Services", "HardwareIntelligenceEngine.cs"));
        Assert.Contains("AppContext.BaseDirectory", source, StringComparison.Ordinal);
        Assert.Contains("\"providers\", \"sensors\"", source, StringComparison.Ordinal);
        Assert.Contains("\"LibreHardwareMonitorLib.dll\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProvidersSensorsFolder_IncludesMplNotices()
    {
        var sensorsDir = FindRepoFile("providers", "sensors");
        Assert.True(Directory.Exists(sensorsDir));
        var licenses = Path.Combine(sensorsDir, "LICENSES", "LibreHardwareMonitor-MPL-2.0.txt");
        Assert.True(File.Exists(licenses), $"Expected notice at {licenses}");
        var thirdParty = Path.Combine(sensorsDir, "THIRD-PARTY-NOTICES.txt");
        Assert.True(File.Exists(thirdParty));
    }

    private static string FindRepoFile(params string[] segments)
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate repo path.", Path.Combine(segments));
    }
}
