using System;
using System.IO;
using System.Linq;
using System.Text;
using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class DownloadedFileSafetyAnalyzerTests
{
    [Fact]
    public void AnalyzeReturnsInvalidInputReportForInvalidPath()
    {
        var report = DownloadedFileSafetyAnalyzer.Analyze("   ", out var err);

        Assert.Null(err);
        Assert.NotNull(report);
        Assert.Equal(LocalFileSafetyOutcome.InvalidInput, report!.Outcome);
        Assert.Contains(SafetyCheckSeverity.InvalidInput, report.States);
    }

    [Fact]
    public void AnalyzeDetectsMissingFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "forgerems-missing-" + Guid.NewGuid().ToString("N") + ".bin");

        var report = DownloadedFileSafetyAnalyzer.Analyze(path, out var err);

        Assert.Null(err);
        Assert.NotNull(report);
        Assert.Equal(LocalFileSafetyOutcome.LocalFileNotFound, report!.Outcome);
        Assert.Contains(SafetyCheckSeverity.LocalFileNotFound, report.States);
    }

    [Fact]
    public void AnalyzeComputesSha256ForHarmlessTempFile()
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
            Assert.Contains(SafetyCheckSeverity.HashComputed, report.States);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void AnalyzeExeNameAddsExtensionRiskFlagsWithoutExecution()
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
            Assert.Contains("No execution", DownloadedFileSafetyAnalyzer.FormatReport(report), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void AnalyzeDetectsPeLikeHeaderAsExecutableMetadataOnly()
    {
        var path = Path.Combine(Path.GetTempPath(), "forgerems-fake-pe-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            var bytes = new byte[128];
            bytes[0] = (byte)'M';
            bytes[1] = (byte)'Z';
            BitConverter.GetBytes(0x40).CopyTo(bytes, 0x3C);
            bytes[0x40] = (byte)'P';
            bytes[0x41] = (byte)'E';
            bytes[0x42] = 0;
            bytes[0x43] = 0;
            File.WriteAllBytes(path, bytes);

            var report = DownloadedFileSafetyAnalyzer.Analyze(path, out var err);

            Assert.Null(err);
            Assert.NotNull(report);
            Assert.Contains("PE executable", report!.FileKind, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(SafetyCheckSeverity.ExecutableMetadataOnly, report.States);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void AnalyzeHandlesReadErrorsHonestly()
    {
        var path = Path.Combine(Path.GetTempPath(), "forgerems-read-blocked-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            File.WriteAllText(path, "blocked-read-test");

            var report = DownloadedFileSafetyAnalyzer.Analyze(
                path,
                out var err,
                _ => throw new UnauthorizedAccessException("simulated read block"));

            Assert.Null(err);
            Assert.NotNull(report);
            Assert.Equal(LocalFileSafetyOutcome.LocalFileReadBlocked, report!.Outcome);
            Assert.Contains(SafetyCheckSeverity.LocalFileReadBlocked, report.States);
            Assert.Contains("blocked", report.Verdict, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void AnalyzeRecognizesKnownFixtureFileNamesWithoutHashDependency()
    {
        var path = Path.Combine(Path.GetTempPath(), "eicar_com.zip");
        try
        {
            File.WriteAllText(path, "FORGEREMS-FAKE-EICAR-LIKE-TEST-NOT-MALWARE");

            var report = DownloadedFileSafetyAnalyzer.Analyze(path, out var err);

            Assert.Null(err);
            Assert.NotNull(report);
            Assert.True(report!.Fixture.IsKnown);
            Assert.Contains(SafetyCheckSeverity.SimulatedMalwareTestFixture, report.States);
        }
        finally
        {
            TryDelete(path);
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

            Assert.Contains("Manual review", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("No execution", text, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(path);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
