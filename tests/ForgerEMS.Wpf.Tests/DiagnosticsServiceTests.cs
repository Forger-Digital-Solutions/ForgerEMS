using System.IO;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class DiagnosticsServiceTests
{
    [Fact]
    public void BuildReport_MalformedSystemIntelligenceJson_AddsParseErrorItem()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fems-diagtest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "system-intel.json");
        File.WriteAllText(path, "{ this is not valid json");
        try
        {
            var svc = new DiagnosticsService();
            var report = svc.BuildReport(path, null, null, wslLikelyAvailable: true);
            Assert.Contains(
                report.Items,
                static i => i.Source == "SystemIntelligence" && i.Code == "parse_error");
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
                // best-effort cleanup on Windows locked files
            }
        }
    }
}
