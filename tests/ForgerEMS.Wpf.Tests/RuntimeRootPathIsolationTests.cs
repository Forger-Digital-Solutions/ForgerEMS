using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using ForgerEMS.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using VentoyToolkitSetup.Wpf.ViewModels;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Regression coverage for the runtime report-path seam: Driver Hub and the
/// system-intelligence report loaders must resolve under the injected
/// IAppRuntimeService.RuntimeRoot, never a process-environment special folder.
/// Before the fix, a static helper escaped the injected root and read the real
/// user's report during isolated QA runs.
/// </summary>
public sealed class RuntimeRootPathIsolationTests
{
    [Fact]
    public void RuntimeReportsDirectory_UsesInjectedRuntimeRoot_NotEnvironment()
    {
        using var temp = new TempFolder();
        var runtime = new FakeRuntime(temp.Path);
        using var vm = BuildViewModel(runtime);

        var method = typeof(MainViewModel).GetMethod(
            "GetRuntimeReportsDirectory",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var resolved = (string)method!.Invoke(vm, null)!;
        Assert.Equal(Path.Combine(temp.Path, "reports"), resolved);
    }

    [Fact]
    public void SystemIntelligenceJsonPath_UsesInjectedRuntimeRoot()
    {
        using var temp = new TempFolder();
        var runtime = new FakeRuntime(temp.Path);
        using var vm = BuildViewModel(runtime);

        var method = typeof(MainViewModel).GetMethod(
            "GetSystemIntelligenceJsonPath",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var resolved = (string)method!.Invoke(vm, null)!;
        Assert.Equal(
            Path.Combine(temp.Path, "reports", "system-intelligence-latest.json"),
            resolved);
    }

    [Fact]
    public void DriverHub_LoadsSnapshot_FromInjectedRuntimeRootOnly()
    {
        using var temp = new TempFolder();
        var runtime = new FakeRuntime(temp.Path);
        var reportsDir = Path.Combine(temp.Path, "reports");
        Directory.CreateDirectory(reportsDir);

        // Synthetic fixture with a deliberately stale marker OS that exists
        // ONLY under the injected root. If the loader escaped the injected
        // root it could not surface this string.
        File.WriteAllText(
            Path.Combine(reportsDir, "system-intelligence-latest.json"),
            """{"summary":{"manufacturer":"FixtureVendor","model":"FixtureModel","os":"FixtureLegacyOS 9876","osBuild":"9876","cpu":"FixtureCpu"},"network":{}}""");

        using var vm = BuildViewModel(runtime);

        Assert.Contains("Snapshot OS: FixtureLegacyOS 9876", vm.DriverHubDetectedHardwareText, StringComparison.Ordinal);
    }

    [Fact]
    public void DriverHub_NoProfileFixture_DoesNotLoadAnyOutsideReport()
    {
        using var temp = new TempFolder();
        var runtime = new FakeRuntime(temp.Path);

        // No reports fixture under the injected root at all. The view model
        // must show the empty-snapshot copy rather than a report resolved from
        // a process-environment path.
        using var vm = BuildViewModel(runtime);

        Assert.Equal(
            "No local device snapshot loaded. Driver Hub still shows official starting points.",
            vm.DriverHubDetectedHardwareText);
    }

    private static MainViewModel BuildViewModel(FakeRuntime runtime)
    {
        var powerShell = new PowerShellRunnerService();
        return new MainViewModel(
            new BackendDiscoveryService(),
            powerShell,
            new EmptyUsbDetectionService(),
            new ManagedDownloadSummaryService(),
            new ScriptStatusParser(),
            new AcceptingPromptService(),
            new VentoyIntegrationService(powerShell, runtime),
            new ManagedDownloadResolverService(new HttpClient()),
            runtime,
            new UsbBenchmarkService(powerShell),
            usbIntelligenceService: new UsbIntelligenceService(),
            autoIntelligenceOrchestrator: new NoOpAutoIntelligenceOrchestrator());
    }

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "forgerems-runtime-root-" + Guid.NewGuid().ToString("N"));
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

    private sealed class FakeRuntime(string runtimeRoot) : IAppRuntimeService
    {
        public string RuntimeRoot { get; } = runtimeRoot;
        public string VentoyRoot => Path.Combine(RuntimeRoot, "Ventoy");
        public string VentoyPackagesRoot => Path.Combine(VentoyRoot, "packages");
        public string VentoyExtractedRoot => Path.Combine(VentoyRoot, "extracted");
        public string LogsRoot => Path.Combine(RuntimeRoot, "logs");
        public string DiagnosticsRoot => Path.Combine(RuntimeRoot, "diagnostics");
        public string SessionLogPath => Path.Combine(LogsRoot, "session.log");

        public void EnsureInitialized()
        {
            Directory.CreateDirectory(RuntimeRoot);
            Directory.CreateDirectory(Path.Combine(RuntimeRoot, "config"));
            Directory.CreateDirectory(Path.Combine(RuntimeRoot, "cache"));
            Directory.CreateDirectory(Path.Combine(RuntimeRoot, "reports"));
            Directory.CreateDirectory(LogsRoot);
            Directory.CreateDirectory(DiagnosticsRoot);
        }

        public void AppendSessionLog(LogLine line)
        {
        }

        public string WriteDiagnosticReport(string fileName, IEnumerable<string> lines)
        {
            var path = Path.Combine(DiagnosticsRoot, fileName);
            File.WriteAllLines(path, lines);
            return path;
        }
    }

    private sealed class EmptyUsbDetectionService : IUsbDetectionService
    {
        public System.Threading.Tasks.Task<UsbDetectionResult> GetUsbTargetsAsync(
            System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(new UsbDetectionResult());
    }

    private sealed class AcceptingPromptService : IUserPromptService
    {
        public bool Confirm(string title, string message) => true;

        public string? PromptText(string title, string message, string initialValue = "") => initialValue;

        public void ShowMessage(string title, string message, MessageBoxImage image = MessageBoxImage.Information)
        {
        }

        public int? PickOption(string title, string message, IReadOnlyList<string> options) =>
            options.Count > 0 ? 0 : null;
    }
}
