using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Windows;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using ForgerEMS.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using VentoyToolkitSetup.Wpf.ViewModels;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Functional coverage for the event-driven USB hotplug pathway
/// (WM_DEVICECHANGE → debouncer → <see cref="MainViewModel.HandleDebouncedUsbDeviceChangeAsync"/>):
/// a device-change flush must refresh the shared UsbTargets list that both the
/// USB Builder and the Port / USB Intelligence device list render.
/// </summary>
public sealed class UsbHotplugRefreshTests
{
    [Fact]
    public async Task DeviceArrivalFlush_AddsNewTargetToUsbTargetsList()
    {
        using var temp = new TempFolder();
        var runtime = new FakeRuntime(temp.Path);
        runtime.EnsureInitialized();
        var detection = new ScriptedUsbDetectionService();

        using var vm = BuildViewModel(runtime, detection);
        Assert.Empty(vm.UsbTargets);

        detection.CurrentTargets = [MakeTarget("E:", "FORGER-USB")];
        await vm.HandleDebouncedUsbDeviceChangeAsync(UsbDeviceChangeReason.Arrival);

        Assert.Contains(vm.UsbTargets, target => target.RootPath == "E:\\");
    }

    [Fact]
    public async Task DeviceRemovalFlush_DropsUnpluggedTargetFromUsbTargetsList()
    {
        using var temp = new TempFolder();
        var runtime = new FakeRuntime(temp.Path);
        runtime.EnsureInitialized();
        var detection = new ScriptedUsbDetectionService
        {
            CurrentTargets = [MakeTarget("E:", "FORGER-USB")]
        };

        using var vm = BuildViewModel(runtime, detection);
        await vm.HandleDebouncedUsbDeviceChangeAsync(UsbDeviceChangeReason.Arrival);
        Assert.Single(vm.UsbTargets);

        detection.CurrentTargets = [];
        await vm.HandleDebouncedUsbDeviceChangeAsync(UsbDeviceChangeReason.Removal);

        Assert.Empty(vm.UsbTargets);
    }

    [Fact]
    public async Task UnchangedDeviceSet_SkipsListRebuild()
    {
        using var temp = new TempFolder();
        var runtime = new FakeRuntime(temp.Path);
        runtime.EnsureInitialized();
        var detection = new ScriptedUsbDetectionService
        {
            CurrentTargets = [MakeTarget("E:", "FORGER-USB")]
        };

        using var vm = BuildViewModel(runtime, detection);
        await vm.HandleDebouncedUsbDeviceChangeAsync(UsbDeviceChangeReason.Arrival);
        var callsAfterFirstFlush = detection.CallCount;

        // A non-volume device hop (same signature) must only cost one enumeration
        // and must not clear/rebuild the visible list.
        await vm.HandleDebouncedUsbDeviceChangeAsync(UsbDeviceChangeReason.Arrival);

        Assert.Equal(callsAfterFirstFlush + 1, detection.CallCount);
        Assert.Single(vm.UsbTargets);
    }

    private static UsbTargetInfo MakeTarget(string driveLetter, string label) =>
        new()
        {
            DriveLetter = driveLetter,
            RootPath = driveLetter + "\\",
            Label = label,
            FileSystem = "exFAT",
            TotalBytes = 64L * 1024 * 1024 * 1024,
            FreeBytes = 60L * 1024 * 1024 * 1024,
            DriveType = "Removable",
            BusType = "USB",
            IsLikelyUsb = true,
            IsRemovableMedia = true,
            IsSystemDrive = false,
            IsBootDrive = false,
            IsEfiSystemPartition = false,
            IsUndersizedPartition = false,
            IsSelectable = true
        };

    private static MainViewModel BuildViewModel(FakeRuntime runtime, IUsbDetectionService detection)
    {
        var powerShell = new PowerShellRunnerService();
        var registry = new CopilotProviderRegistry();
        return new MainViewModel(
            new BackendDiscoveryService(),
            powerShell,
            detection,
            new ManagedDownloadSummaryService(),
            new ScriptStatusParser(),
            new AcceptingPromptService(),
            new VentoyIntegrationService(powerShell, runtime),
            new ManagedDownloadResolverService(new HttpClient()),
            runtime,
            new StubBenchmarkService(),
            new CopilotService(registry),
            registry,
            usbIntelligenceService: new UsbIntelligenceService(),
            autoIntelligenceOrchestrator: new NoOpAutoIntelligenceOrchestrator());
    }

    private sealed class ScriptedUsbDetectionService : IUsbDetectionService
    {
        public List<UsbTargetInfo> CurrentTargets { get; set; } = [];

        public int CallCount { get; private set; }

        public Task<UsbDetectionResult> GetUsbTargetsAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new UsbDetectionResult { Targets = [.. CurrentTargets] });
        }
    }

    private sealed class StubBenchmarkService : IUsbBenchmarkService
    {
        public Task<UsbBenchmarkResult> RunSequentialBenchmarkAsync(
            UsbTargetInfo target,
            Action<LogLine>? onOutput = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new UsbBenchmarkResult
            {
                Succeeded = true,
                Status = "Complete",
                Summary = "stub",
                WriteSpeedMBps = 10,
                ReadSpeedMBps = 20,
                ResultKind = UsbBenchmarkResultKind.Completed
            });
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

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "forgerems-hotplug-tests-" + Guid.NewGuid().ToString("N"));
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
}
