using VentoyToolkitSetup.Wpf.Services.Intelligence;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraSafeContextBuilderTests
{
    [Fact]
    public void BuildBriefSummary_RedactsLongHardwareishTokens()
    {
        var dir = Path.Combine(Path.GetTempPath(), "forgerems-kyractx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var sys = Path.Combine(dir, "s.json");
        File.WriteAllText(
            sys,
            """{"forgerAutomation":{"summaryLine":"Path USBSTOR_DISK&VEN_USB&PROD_STICK&REV_1.0\\0123456789ABCDEF"}}""");

        var text = KyraSafeContextBuilder.BuildBriefSummary(sys, null, null, null, enableRedaction: true);
        Assert.DoesNotContain("0123456789ABCDEF", text, StringComparison.Ordinal);
        Assert.Contains("[redacted]", text, StringComparison.Ordinal);
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // temp cleanup best-effort
        }
    }
}
