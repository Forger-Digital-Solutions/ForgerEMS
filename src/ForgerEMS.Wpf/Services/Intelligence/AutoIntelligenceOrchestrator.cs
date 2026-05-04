using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public sealed class AutoIntelligenceOrchestrator : IAutoIntelligenceOrchestrator
{
    private readonly IAppRuntimeService _runtime;
    private readonly IPowerShellRunnerService _powerShell;
    private readonly IUsbIntelligenceService _usbIntelligence;
    private readonly IDiagnosticsService _diagnostics;
    private readonly Func<BackendContext, string> _resolveSystemScanScriptPath;
    private readonly Func<Task> _marshalUiRefreshAsync;
    private int _generation;

    public AutoIntelligenceOrchestrator(
        IAppRuntimeService runtime,
        IPowerShellRunnerService powerShell,
        IUsbIntelligenceService usbIntelligence,
        IDiagnosticsService diagnostics,
        Func<BackendContext, string> resolveSystemScanScriptPath,
        Func<Task> marshalUiRefreshAsync)
    {
        _runtime = runtime;
        _powerShell = powerShell;
        _usbIntelligence = usbIntelligence;
        _diagnostics = diagnostics;
        _resolveSystemScanScriptPath = resolveSystemScanScriptPath;
        _marshalUiRefreshAsync = marshalUiRefreshAsync;
    }

    public void ScheduleAppStartupWork(BackendContext backend)
    {
        var token = Interlocked.Increment(ref _generation);
        _ = RunChainAsync(backend, selectedTarget: null, allowQuietSystemScan: true, token);
    }

    public void ScheduleUsbSelectionRefresh(BackendContext backend, UsbTargetInfo? selectedTarget)
    {
        var token = Interlocked.Increment(ref _generation);
        _ = RunChainAsync(backend, selectedTarget, allowQuietSystemScan: false, token);
    }

    public void ScheduleManualIntelligenceRefresh(BackendContext backend)
    {
        var token = Interlocked.Increment(ref _generation);
        _ = RunChainAsync(backend, selectedTarget: null, allowQuietSystemScan: true, token);
    }

    private async Task RunChainAsync(
        BackendContext backend,
        UsbTargetInfo? selectedTarget,
        bool allowQuietSystemScan,
        int token)
    {
        try
        {
            var reports = Path.Combine(_runtime.RuntimeRoot, "reports");
            Directory.CreateDirectory(reports);
            var siPath = Path.Combine(reports, "system-intelligence-latest.json");
            var usbPath = Path.Combine(reports, "usb-intelligence-latest.json");
            var toolkitPath = Path.Combine(reports, "toolkit-health-latest.json");

            SystemIntelligenceAutomationMerger.TryMerge(siPath);

            var profileStore = new UsbMachineProfileStore(_runtime.RuntimeRoot);
            var machineProfile = profileStore.LoadOrCreate();

            UsbTopologySnapshot? previousUsb = null;
            if (File.Exists(usbPath))
            {
                try
                {
                    previousUsb = JsonSerializer.Deserialize<UsbTopologySnapshot>(
                        File.ReadAllText(usbPath),
                        UsbIntelligenceService.UsbJsonReadOptions);
                }
                catch (Exception ex)
                {
                    IntelligenceLogWriter.Append("usb-intelligence.log", $"Previous USB snapshot load failed: {ex.Message}");
                }
            }

            var usbSnapshot = _usbIntelligence.BuildTopologySnapshot(
                selectedTarget,
                new UsbTopologyBuildOptions
                {
                    PreviousSnapshot = previousUsb,
                    MachineProfile = machineProfile
                });
            profileStore.ApplySnapshot(machineProfile, usbSnapshot);
            profileStore.Save(machineProfile);

            await _usbIntelligence.WriteLatestReportAsync(reports, usbSnapshot).ConfigureAwait(false);

            var wsl = File.Exists(Path.Combine(Environment.SystemDirectory, "wsl.exe"));
            var diag = _diagnostics.BuildReport(siPath, usbPath, toolkitPath, wsl);
            await _diagnostics.WriteLatestReportAsync(reports, diag).ConfigureAwait(false);

            if (allowQuietSystemScan)
            {
                await TryQuietSystemScanAsync(backend, siPath).ConfigureAwait(false);
                SystemIntelligenceAutomationMerger.TryMerge(siPath);
                diag = _diagnostics.BuildReport(siPath, usbPath, toolkitPath, wsl);
                await _diagnostics.WriteLatestReportAsync(reports, diag).ConfigureAwait(false);
            }

            if (token != Volatile.Read(ref _generation))
            {
                return;
            }

            await _marshalUiRefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            IntelligenceLogWriter.Append("diagnostics.log", $"Orchestrator pass failed: {ex.Message}");
        }
    }

    private async Task TryQuietSystemScanAsync(BackendContext backend, string siPath)
    {
        if (!backend.IsAvailable)
        {
            return;
        }

        var stale =
            !File.Exists(siPath) ||
            File.GetLastWriteTimeUtc(siPath) < DateTime.UtcNow.AddHours(-24);

        if (!stale)
        {
            return;
        }

        var scriptPath = _resolveSystemScanScriptPath(backend);
        if (string.IsNullOrWhiteSpace(scriptPath) || !File.Exists(scriptPath))
        {
            IntelligenceLogWriter.Append("system-intelligence.log", "Quiet scan skipped: script not found.");
            return;
        }

        try
        {
            await _powerShell.RunAsync(
                new PowerShellRunRequest
                {
                    DisplayName = "System Intelligence (background)",
                    WorkingDirectory = backend.WorkingDirectory,
                    ScriptPath = scriptPath
                },
                onOutput: null).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            IntelligenceLogWriter.Append("system-intelligence.log", $"Quiet scan failed: {ex.Message}");
        }
    }
}
