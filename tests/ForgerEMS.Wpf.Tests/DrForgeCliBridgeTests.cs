using System.IO;
using System.Security.Cryptography;
using System.Text;
using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class DrForgeCliBridgeTests
{
    [Fact]
    public void Locator_MissingPackageReturnsFriendlyNotConfigured()
    {
        using var temp = new TempDir();

        var result = new DrForgeCliLocator().Locate(null, temp.Path);

        Assert.False(result.Found);
        Assert.Equal(DrForgeCliBridgeState.NotConfigured, result.State);
        Assert.Contains("Select", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManifestSchemaAccepted_AndChecksumVerificationPasses()
    {
        using var temp = CreatePackage("hello");

        var inspection = new DrForgeCliManifestReader().InspectPackage(temp.ExecutablePath);

        Assert.True(inspection.PackageFound);
        Assert.True(inspection.Manifest.Found);
        Assert.Equal("drforge-cli-release-manifest/1.0", inspection.Manifest.Schema);
        Assert.True(inspection.Checksums.Present);
        Assert.True(inspection.Checksums.Passed);
        Assert.Equal(1, inspection.Checksums.CheckedFileCount);
    }

    [Fact]
    public void ChecksumVerificationReportsFailure()
    {
        using var temp = CreatePackage("hello");
        File.WriteAllText(temp.ExecutablePath, "changed");

        var inspection = new DrForgeCliManifestReader().InspectPackage(temp.ExecutablePath);

        Assert.True(inspection.Checksums.Present);
        Assert.False(inspection.Checksums.Passed);
        Assert.Contains("mismatch", string.Join(" ", inspection.Checksums.Failures), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadinessCommandConstruction_UsesVersionAndSensorCoreHelp()
    {
        using var temp = CreatePackage("exe");
        var fake = new FakeDrForgeProcessRunner(
            Success(temp.ExecutablePath, DrForgeCliRunner.BuildVersionArguments(), "Dr. Forge 0.8.0"),
            Success(temp.ExecutablePath, DrForgeCliRunner.BuildSensorCoreHelpArguments(), "Usage: drforge sensor-core"));
        var runner = new DrForgeCliRunner(fake);

        var result = await runner.CheckReadinessAsync(temp.ExecutablePath);

        Assert.True(result.Succeeded);
        Assert.Equal(new[] { "--version" }, fake.Calls[0].Arguments);
        Assert.Equal(new[] { "sensor-core", "--help" }, fake.Calls[1].Arguments);
    }

    [Fact]
    public async Task ReportAndArchiveCommandConstruction_UsesSnapshotBoundary()
    {
        using var temp = CreatePackage("exe");
        var snapshot = Path.Combine(temp.Path, "snapshot.json");
        var report = Path.Combine(temp.Path, "report.json");
        var archive = Path.Combine(temp.Path, "archive");
        File.WriteAllText(snapshot, "{}");

        var fake = new FakeDrForgeProcessRunner(
            onRun: call =>
            {
                if (call.Arguments.Contains("--out") && call.Arguments.Contains(report))
                    File.WriteAllText(report, "{}");
                if (call.Arguments.Contains("--out") && call.Arguments.Contains(archive))
                    Directory.CreateDirectory(archive);
            },
            Success(temp.ExecutablePath, DrForgeCliRunner.BuildReportArguments(snapshot, report), "report ok"),
            Success(temp.ExecutablePath, DrForgeCliRunner.BuildArchiveArguments(snapshot, archive), "archive ok"));
        var runner = new DrForgeCliRunner(fake);

        var reportResult = await runner.GenerateReportAsync(temp.ExecutablePath, snapshot, report);
        var archiveResult = await runner.GenerateArchiveAsync(temp.ExecutablePath, snapshot, archive);

        Assert.True(reportResult.Succeeded);
        Assert.True(archiveResult.Succeeded);
        Assert.Equal(new[] { "sensor-core", "report", snapshot, "--format", "json", "--out", report }, fake.Calls[0].Arguments);
        Assert.Equal(new[] { "sensor-core", "archive", snapshot, "--out", archive }, fake.Calls[1].Arguments);
        Assert.DoesNotContain("--include-service", fake.Calls.SelectMany(c => c.Arguments));
    }

    [Fact]
    public async Task TimeoutHandling_ReturnsFailedState()
    {
        using var temp = CreatePackage("exe");
        var fake = new FakeDrForgeProcessRunner(
            new DrForgeCliProcessResult(temp.ExecutablePath, DrForgeCliRunner.BuildVersionArguments(), 130, true, "", "timed out"));

        var result = await new DrForgeCliRunner(fake).CheckReadinessAsync(temp.ExecutablePath);

        Assert.False(result.Succeeded);
        Assert.Equal(DrForgeCliBridgeState.Failed, result.State);
        Assert.Contains("timed out", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NonZeroStderr_ReturnsFailedState()
    {
        using var temp = CreatePackage("exe");
        var fake = new FakeDrForgeProcessRunner(
            new DrForgeCliProcessResult(temp.ExecutablePath, DrForgeCliRunner.BuildVersionArguments(), 2, false, "", "bad package"));

        var result = await new DrForgeCliRunner(fake).CheckReadinessAsync(temp.ExecutablePath);

        Assert.False(result.Succeeded);
        Assert.Contains("bad package", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IntakeReportReader_RendersNullReadingsAsUnavailable()
    {
        var json = """
        {
          "reportSchemaVersion": "forge-hardware-intake-report/1.0",
          "sourceSchemaVersion": "forge-sensor-core/1.0",
          "platform": { "osFamily": "Windows", "architecture": "X64" },
          "safety": { "satisfiesSafetyInvariants": true, "kernelDriverLoaded": false },
          "summary": {
            "cpuLoadPercent": null,
            "memoryUsedPercent": 42.5,
            "storageCapacityBytes": null,
            "storageSmartHealth": null
          },
          "service": { "requested": false, "available": false, "contributedReadings": 0 },
          "findings": [{ "severity": "Unavailable", "message": "fan RPM: unavailable - requires ring-0" }],
          "ring0Gaps": [{ "reading": "fan RPM", "reason": "Requires ring-0 sensor access; no kernel driver is loaded." }],
          "notes": ["Windows snapshot detected."]
        }
        """;

        var view = new DrForgeIntakeResultReader().ReadJson(json);

        Assert.Contains(view.KeyReadings, r => r.Name == "CPU load" && r.Value == "Unavailable");
        Assert.Contains(view.KeyReadings, r => r.Name == "Storage capacity" && r.Value == "Unavailable");
        Assert.DoesNotContain("CPU load: 0", view.SummaryText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Storage capacity: 0", view.SummaryText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UiCopyIsHonestAboutParityAndUnavailableReadings()
    {
        var xaml = File.ReadAllText(RepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));

        Assert.Contains("Unavailable readings are shown as unavailable, not zero.", xaml, StringComparison.Ordinal);
        Assert.Contains("does not claim full hardware-monitor parity", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unavailable readings are zero", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BridgeDoesNotHardcodePrivateDeveloperPaths()
    {
        var text = File.ReadAllText(RepoFile("src", "ForgerEMS.Wpf", "Services", "DrForgeCliBridge.cs")) +
                   File.ReadAllText(RepoFile("src", "ForgerEMS.Wpf", "ViewModels", "MainViewModel.cs")) +
                   File.ReadAllText(RepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));

        Assert.DoesNotContain("Daddy_FDS", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Desktop\\ForgerDigitalSolutions\\Dr.Forge", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Dr.Forge\\release\\cli", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BridgeAddsNoNetworkTelemetryAccountActivationOrElevationBehavior()
    {
        var text = File.ReadAllText(RepoFile("src", "ForgerEMS.Wpf", "Services", "DrForgeCliBridge.cs"));

        Assert.DoesNotContain("HttpClient", text, StringComparison.Ordinal);
        Assert.DoesNotContain("WebClient", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Socket", text, StringComparison.Ordinal);
        Assert.DoesNotContain("UseShellExecute = true", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Verb = \"runas\"", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pkexec", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sudo", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("activation", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("account", text, StringComparison.OrdinalIgnoreCase);
    }

    private static DrForgePackageTempDir CreatePackage(string executableContent)
    {
        var temp = new DrForgePackageTempDir();
        var packageDir = Path.Combine(temp.Path, "windows-x64");
        Directory.CreateDirectory(packageDir);
        File.WriteAllText(temp.ExecutablePath, executableContent);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(executableContent))).ToLowerInvariant();
        File.WriteAllText(Path.Combine(packageDir, "SHA256SUMS.txt"), $"{hash}  drforge.exe{Environment.NewLine}");
        File.WriteAllText(Path.Combine(temp.Path, "drforge-cli-release-manifest.json"),
            """
            {
              "schema": "drforge-cli-release-manifest/1.0",
              "product": "Dr. Forge",
              "cliName": "drforge",
              "version": "0.8.0",
              "commit": "test",
              "safetyPolicy": { "mode": "user-mode", "summary": [] },
              "packages": [
                {
                  "platform": "windows-x64",
                  "status": "published",
                  "checksumFile": "windows-x64/SHA256SUMS.txt"
                }
              ]
            }
            """);
        return temp;
    }

    private static DrForgeCliProcessResult Success(string executablePath, IReadOnlyList<string> arguments, string stdout) =>
        new(executablePath, arguments, 0, false, stdout, "");

    private static string RepoFile(params string[] parts) =>
        Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray());

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ForgerEMS.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? AppContext.BaseDirectory;
    }

    private sealed class FakeDrForgeProcessRunner : IDrForgeProcessRunner
    {
        private readonly Queue<DrForgeCliProcessResult> _results;
        private readonly Action<Call>? _onRun;

        public FakeDrForgeProcessRunner(params DrForgeCliProcessResult[] results)
            : this(null, results)
        {
        }

        public FakeDrForgeProcessRunner(Action<Call>? onRun, params DrForgeCliProcessResult[] results)
        {
            _onRun = onRun;
            _results = new Queue<DrForgeCliProcessResult>(results);
        }

        public List<Call> Calls { get; } = [];

        public Task<DrForgeCliProcessResult> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var call = new Call(executablePath, arguments.ToList(), timeout);
            Calls.Add(call);
            _onRun?.Invoke(call);
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed record Call(string ExecutablePath, IReadOnlyList<string> Arguments, TimeSpan Timeout);

    private class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "forgerems-drforge-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }

    private sealed class DrForgePackageTempDir : TempDir
    {
        public string ExecutablePath => System.IO.Path.Combine(Path, "windows-x64", "drforge.exe");
    }
}
