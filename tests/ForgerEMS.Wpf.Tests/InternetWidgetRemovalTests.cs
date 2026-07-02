using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using VentoyToolkitSetup.Wpf.ViewModels;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// v1.2.3 idle-performance + retirement pass: the always-visible Internet header widget is
/// gone, and Network Pulse itself was retired from the product in v1.2.3-preview.1 — the
/// shell no longer constructs, exposes, or polls it, and Settings has no Network Pulse
/// section. The legacy implementation was removed so source/package scans cannot confuse it
/// with an active feature. The Full Logs overlay gate stops the per-flush full-log string
/// rebuild while it is hidden.
/// </summary>
public sealed class InternetWidgetRemovalTests
{
    private static string FindRepoFile(params string[] parts)
    {
        var candidate = FindRepoPath(parts);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        throw new InvalidOperationException("Could not locate repo file " + string.Join('/', parts));
    }

    private static string FindRepoPath(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 16; i++)
        {
            var candidate = Combine(dir, parts);
            var rootMarker = Path.Combine(dir, "ForgerEMS.sln");
            if (File.Exists(candidate) || File.Exists(rootMarker))
            {
                return candidate;
            }

            var parent = Directory.GetParent(dir);
            if (parent is null)
            {
                break;
            }

            dir = parent.FullName;
        }

        return Combine(AppContext.BaseDirectory, parts);
    }

    private static string Combine(string dir, string[] parts)
    {
        var path = dir;
        foreach (var part in parts)
        {
            path = Path.Combine(path, part);
        }

        return path;
    }

    [Fact]
    public void MainViewModelSource_HasNoNetworkPulseWiring()
    {
        // Network Pulse is retired: the shell view model must not construct the service,
        // expose commands/status for it, or reference the namespace at all.
        var sourcePath = FindRepoFile("src", "ForgerEMS.Wpf", "ViewModels", "MainViewModel.cs");
        var source = File.ReadAllText(sourcePath);
        Assert.DoesNotContain("_networkPulseService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RunNetworkPulseCheckCommand", source, StringComparison.Ordinal);
        Assert.DoesNotContain("using VentoyToolkitSetup.Wpf.Services.NetworkPulse;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RetiredNetworkPulseImplementation_IsNotCompiledIntoTheApp()
    {
        foreach (var parts in new[]
        {
            new[] { "src", "ForgerEMS.Wpf", "Services", "NetworkPulse", "NetworkPulseService.cs" },
            new[] { "src", "ForgerEMS.Wpf", "Services", "NetworkPulse", "NetworkPulseSettingsStore.cs" },
            new[] { "src", "ForgerEMS.Wpf", "ViewModels", "NetworkPulseViewModel.cs" },
            new[] { "tests", "ForgerEMS.Wpf.Tests", "NetworkPulseTests.cs" }
        })
        {
            Assert.False(File.Exists(FindRepoPath(parts)), string.Join('/', parts) + " should stay removed.");
        }
    }

    [Fact]
    public void MainViewModel_Shell_ExposesNoNetworkPulseMembers()
    {
        // Reflection guard: no public property, method, or field on the shell view model
        // may mention Network Pulse — retired features must not creep back into bindings.
        var members = typeof(MainViewModel)
            .GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Static)
            .Where(member => member.Name.Contains("NetworkPulse", StringComparison.OrdinalIgnoreCase))
            .Select(member => member.Name)
            .ToArray();

        Assert.Empty(members);
    }

    [Fact]
    public void LiveLogs_FullLogsText_IsNotRebuiltWhileOverlayHidden()
    {
        var vm = BuildShellViewModel();
        try
        {
            vm.Logs.Add(new LogLine(DateTimeOffset.Now, "[INFO] first line", LogSeverity.Info));
            vm.RefreshLogsText();

            // Overlay hidden: the expensive full join is skipped (LogsText stays stale/empty),
            // but Copy logs remains available because it rebuilds on demand.
            Assert.Equal(string.Empty, vm.LogsText);
            Assert.True(vm.CopyLogsCommand.CanExecute(null));

            // Opening the overlay hydrates the text once.
            vm.IsFullLogsOverlayVisible = true;
            Assert.Contains("first line", vm.LogsText, StringComparison.Ordinal);

            // While visible, flushes keep it current.
            vm.Logs.Add(new LogLine(DateTimeOffset.Now, "[INFO] second line", LogSeverity.Info));
            vm.RefreshLogsText();
            Assert.Contains("second line", vm.LogsText, StringComparison.Ordinal);

            // Closing the overlay stops the rebuilds again.
            vm.IsFullLogsOverlayVisible = false;
            vm.Logs.Add(new LogLine(DateTimeOffset.Now, "[INFO] third line", LogSeverity.Info));
            vm.RefreshLogsText();
            Assert.DoesNotContain("third line", vm.LogsText, StringComparison.Ordinal);

            // EnsureFullLogsTextCurrent (the Copy logs path) rebuilds the stale text.
            vm.EnsureFullLogsTextCurrent();
            Assert.Contains("third line", vm.LogsText, StringComparison.Ordinal);
        }
        finally
        {
            vm.Dispose();
        }
    }

    private static MainViewModel BuildShellViewModel()
    {
        var powerShell = new PowerShellRunnerService();
        var runtime = new AppRuntimeService();
        var registry = new CopilotProviderRegistry();
        var target = BenchmarkTarget();
        return new MainViewModel(
            new BackendDiscoveryService(),
            powerShell,
            new StaticUsbDetectionService(target),
            new ManagedDownloadSummaryService(),
            new ScriptStatusParser(),
            new AcceptingPromptService(),
            new VentoyIntegrationService(powerShell, runtime),
            runtime,
            new StubBenchmarkService(),
            new CopilotService(registry),
            registry,
            usbIntelligenceService: new UsbIntelligenceService(),
            autoIntelligenceOrchestrator: new NoOpAutoIntelligenceOrchestrator());
    }

    private static UsbTargetInfo BenchmarkTarget() =>
        new()
        {
            DriveLetter = "E:",
            RootPath = "E:\\",
            Label = "Ventoy",
            FileSystem = "exFAT",
            TotalBytes = 128L * 1024 * 1024 * 1024,
            FreeBytes = 100L * 1024 * 1024 * 1024,
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

    private sealed class StaticUsbDetectionService(UsbTargetInfo target) : IUsbDetectionService
    {
        public Task<UsbDetectionResult> GetUsbTargetsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new UsbDetectionResult { Targets = [target] });
    }

    private sealed class AcceptingPromptService : IUserPromptService
    {
        public bool Confirm(string title, string message) => true;

        public string? PromptText(string title, string message, string initialValue = "") => initialValue;

        public void ShowMessage(string title, string message, System.Windows.MessageBoxImage image = System.Windows.MessageBoxImage.Information)
        {
        }

        public int? PickOption(string title, string message, IReadOnlyList<string> options) => options.Count > 0 ? 0 : null;
    }
}
