using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class SupportBundleExporterTests
{
    private sealed class FakeRuntime : IAppRuntimeService
    {
        public required string RuntimeRoot { get; init; }
        public string VentoyRoot => Path.Combine(RuntimeRoot, "Ventoy");
        public string VentoyPackagesRoot => Path.Combine(VentoyRoot, "packages");
        public string VentoyExtractedRoot => Path.Combine(VentoyRoot, "extracted");
        public string LogsRoot => Path.Combine(RuntimeRoot, "logs");
        public string DiagnosticsRoot => Path.Combine(RuntimeRoot, "diagnostics");
        public string SessionLogPath => Path.Combine(LogsRoot, "session.log");

        public void EnsureInitialized()
        {
            Directory.CreateDirectory(RuntimeRoot);
            Directory.CreateDirectory(LogsRoot);
            Directory.CreateDirectory(Path.Combine(RuntimeRoot, "reports"));
            Directory.CreateDirectory(Path.Combine(RuntimeRoot, "cache"));
        }

        public void AppendSessionLog(LogLine line)
        {
        }

        public string WriteDiagnosticReport(string fileName, IEnumerable<string> lines)
        {
            var p = Path.Combine(DiagnosticsRoot, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllLines(p, lines);
            return p;
        }
    }

    [Fact]
    public void TryCreateSupportBundle_WritesReadmeAndMetadata()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "forgerems-bundle-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var zip = Path.Combine(tmp, "bundle.zip");
        var rt = Path.Combine(tmp, "runtime");
        var fake = new FakeRuntime { RuntimeRoot = rt };
        fake.EnsureInitialized();
        File.WriteAllText(fake.SessionLogPath, @"C:\Users\SecretUser\Desktop\test");
        File.WriteAllText(Path.Combine(rt, "reports", "system-intelligence-latest.json"), "{\"path\":\"D:\\\\Private\\\\x\"}");
        File.WriteAllText(Path.Combine(rt, "reports", "network-pulse-latest.json"), "{\"wifiContext\":\"C:\\\\Users\\\\Secret\\\\wifi.txt\"}");
        File.WriteAllText(Path.Combine(rt, "logs", "diagnostics.log"), "email: private@example.com ip: 8.8.8.8 mac: AA-BB-CC-DD-EE-FF");

        Assert.True(SupportBundleExporter.TryCreateSupportBundle(
            zip,
            fake,
            usbRootForManagedJson: null,
            "update-diag-line",
            "config-health",
            out var err));
        Assert.Null(err);

        using var archive = ZipFile.OpenRead(zip);
        var names = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToHashSet();
        Assert.Contains("README.txt", names);
        Assert.Contains("bundle-metadata.txt", names);
        var sessionEntry = archive.Entries.FirstOrDefault(e =>
            string.Equals(e.Name, "session-latest.log", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(sessionEntry);
        using var reader = new StreamReader(sessionEntry.Open());
        var body = reader.ReadToEnd();
        Assert.Contains("[REDACTED_PRIVATE_PATH]", body, System.StringComparison.Ordinal);

        var diagnostics = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith("diagnostics.log", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(diagnostics);
        using var diagnosticsReader = new StreamReader(diagnostics!.Open());
        var diagnosticsBody = diagnosticsReader.ReadToEnd();
        Assert.DoesNotContain("private@example.com", diagnosticsBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("8.8.8.8", diagnosticsBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AA-BB-CC-DD-EE-FF", diagnosticsBody, StringComparison.OrdinalIgnoreCase);

        var np = archive.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').EndsWith("reports/network-pulse-latest.json", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(np);
        using var npReader = new StreamReader(np!.Open());
        var npBody = npReader.ReadToEnd();
        Assert.DoesNotContain("Secret", npBody, StringComparison.OrdinalIgnoreCase);
    }
}
