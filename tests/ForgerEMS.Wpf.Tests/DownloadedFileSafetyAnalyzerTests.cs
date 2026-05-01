using System;
using System.IO;
using System.Linq;
using System.Text;
using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class DownloadedFileSafetyAnalyzerTests
{
    [Fact]
    public void AnalyzeReturnsErrorForInvalidPath()
    {
        var report = DownloadedFileSafetyAnalyzer.Analyze("::not a path::", out var err);
        Assert.Null(report);
        Assert.NotNull(err);
    }

    [Fact]
    public void AnalyzeComputesSha256ForTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "forgerems-file-safety-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes("hello-forgerems"));
            var report = DownloadedFileSafetyAnalyzer.Analyze(path, out var err);
            Assert.Null(err);
            Assert.NotNull(report);
            Assert.Equal(64, report!.Sha256Hex.Length);
            Assert.True(report.Sha256Hex.All(static c => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F')));
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void AnalyzeExeNameAddsExtensionRiskFlags()
    {
        var path = Path.Combine(Path.GetTempPath(), "forgerems-setup.pdf.exe");
        try
        {
            File.WriteAllText(path, "MZFAKE");
            var report = DownloadedFileSafetyAnalyzer.Analyze(path, out var err);
            Assert.Null(err);
            Assert.NotNull(report);
            Assert.Contains(report!.RiskFlags, f => f.Contains(".exe", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(report.RiskFlags, f => f.Contains("double extension", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void FormatReportContainsDisclaimer()
    {
        var path = Path.Combine(Path.GetTempPath(), "forgerems-disclaimer-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllText(path, "x");
            var report = DownloadedFileSafetyAnalyzer.Analyze(path, out _);
            Assert.NotNull(report);
            var text = DownloadedFileSafetyAnalyzer.FormatReport(report!);
            Assert.Contains("cannot guarantee", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("read-only", text, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }
    }
}
