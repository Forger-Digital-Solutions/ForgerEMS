using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using VentoyToolkitSetup.Wpf.ViewModels;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// v1.2.3-preview.1 retired Network Pulse and the Forger Deep Sense / Deep Sensor Mode
/// settings surface. Settings must only reflect currently supported features, and old
/// persisted config files for the retired features must be ignored gracefully.
/// </summary>
public sealed class SettingsRetiredFeatureTests
{
    [Fact]
    public void SettingsTab_DoesNotMentionRetiredFeatures()
    {
        var xaml = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        var settingsStart = xaml.IndexOf("<TabItem Header=\"☰  Settings\">", StringComparison.Ordinal);
        Assert.True(settingsStart >= 0, "Settings tab not found.");
        var settings = xaml[settingsStart..];

        foreach (var retired in new[]
                 {
                     "Network Pulse",
                     "NetworkPulse",
                     "Forger Deep Sense",
                     "Deep Sense",
                     "Deep Sensor Mode",
                     "DeepSensorMode",
                     "deep network monitoring"
                 })
        {
            Assert.DoesNotContain(retired, settings, StringComparison.OrdinalIgnoreCase);
        }

        // Active settings surfaces stay.
        Assert.Contains("Kyra Assistant (Beta)", settings, StringComparison.Ordinal);
        Assert.Contains("App Updates", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void MainViewModel_ExposesNoRetiredSettingsBindings()
    {
        var vmType = typeof(MainViewModel);

        foreach (var retired in new[]
                 {
                     "NetworkPulse",
                     "RunNetworkPulseCheckCommand",
                     "NetworkPulseCheckStatusText",
                     "DeepSensorModeSelectedIndex",
                     "DeepSensorModeSourceSummary",
                     "DeepSensorModeConsentNotice",
                     "DeepSensorModeSettingsSummary"
                 })
        {
            Assert.Null(vmType.GetProperty(retired));
        }
    }

    [Fact]
    public void Startup_IgnoresStaleRetiredSettingsFilesWithoutCrashing()
    {
        using var temp = new TempFolder();
        var runtime = new FakeRuntime(temp.Path);
        runtime.EnsureInitialized();

        // Simulate a machine upgrading from an older build that still has retired
        // Network Pulse settings (including junk content) on disk.
        var configDir = Path.Combine(runtime.RuntimeRoot, "config");
        File.WriteAllText(
            Path.Combine(configDir, "network-pulse-settings.json"),
            """{"networkPulseEnabled":true,"networkPulseMode":"Full","uploadProbesEnabled":true}""");
        File.WriteAllText(
            Path.Combine(configDir, "deep-sense-legacy.json"),
            "{ not-even-valid-json ");

        using var vm = BuildViewModel(runtime);

        // Construction succeeded and nothing surfaced the retired features.
        Assert.NotNull(vm.RefreshUsbTargetsCommand);
        Assert.Null(typeof(MainViewModel).GetProperty("NetworkPulse"));
    }

    private static MainViewModel BuildViewModel(FakeRuntime runtime)
    {
        var powerShell = new PowerShellRunnerService();
        var registry = new CopilotProviderRegistry();
        return new MainViewModel(
            new BackendDiscoveryService(),
            powerShell,
            new EmptyUsbDetectionService(),
            new ManagedDownloadSummaryService(),
            new ScriptStatusParser(),
            new AcceptingPromptService(),
            new VentoyIntegrationService(powerShell, runtime),
            runtime,
            new UsbBenchmarkService(powerShell),
            new CopilotService(registry),
            registry,
            usbIntelligenceService: new UsbIntelligenceService(),
            autoIntelligenceOrchestrator: new NoOpAutoIntelligenceOrchestrator());
    }

    private static string FindRepoFile(params string[] segments)
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var candidate = Path.Combine([current.FullName, .. segments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate repo file.", Path.Combine(segments));
    }

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "forgerems-retired-settings-" + Guid.NewGuid().ToString("N"));
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
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
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
