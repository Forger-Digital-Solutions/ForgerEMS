#pragma warning disable CA1822 // Partial VM helpers may call private instance/static members.
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ForgerEMS.Wpf.Services.Resources;
using ForgerEMS.Wpf.Services.WindowsMaintenance;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.ViewModels;

/// <summary>
/// Read-only Windows maintenance + Driver Store diagnostics surface. Nothing here installs,
/// removes, or modifies drivers, services, WUA settings, or host state. All work runs
/// backgrounded with an explicit cancellation token and never fires web calls at startup.
/// </summary>
public sealed partial class MainViewModel
{
    private CancellationTokenSource? _windowsMaintenanceCts;
    private bool _windowsMaintenanceBusy;
    private bool _resourceCheckBusy;
    private IWindowsMaintenanceService? _windowsMaintenanceService;

    private string _windowsMaintenanceStatusText =
        "Windows maintenance scan not run yet. Read-only diagnostics only.";

    private string _windowsMaintenanceDetailText = string.Empty;

    private string _resourceCheckStatusText =
        "Resource check not run yet. Resolves official metadata only; nothing is downloaded.";

    /// <summary>Human-readable findings: services, reboot, policy, update offers.</summary>
    public ObservableCollection<string> WindowsMaintenanceFindings { get; } = [];

    /// <summary>Driver Store package classifications; all diagnostic, removal never permitted.</summary>
    public ObservableCollection<string> WindowsMaintenanceDriverAssessments { get; } = [];

    /// <summary>Per-resource resolution results from the last Check Everything run.</summary>
    public ObservableCollection<string> ResourceCheckResults { get; } = [];

    public string WindowsMaintenanceStatusText
    {
        get => _windowsMaintenanceStatusText;
        private set => SetProperty(ref _windowsMaintenanceStatusText, value);
    }

    public string WindowsMaintenanceDetailText
    {
        get => _windowsMaintenanceDetailText;
        private set => SetProperty(ref _windowsMaintenanceDetailText, value);
    }

    public string ResourceCheckStatusText
    {
        get => _resourceCheckStatusText;
        private set => SetProperty(ref _resourceCheckStatusText, value);
    }

    public bool WindowsMaintenanceBusy
    {
        get => _windowsMaintenanceBusy;
        private set => SetProperty(ref _windowsMaintenanceBusy, value);
    }

    public AsyncRelayCommand ScanWindowsMaintenanceCommand { get; private set; } = null!;

    public RelayCommand CancelWindowsMaintenanceScanCommand { get; private set; } = null!;

    public AsyncRelayCommand CheckEverythingResourcesCommand { get; private set; } = null!;

    /// <summary>Explicit user action: opens the native Windows Update settings page.</summary>
    public RelayCommand OpenWindowsUpdateSettingsCommand { get; private set; } = null!;

    private void InitializeWindowsMaintenance()
    {
        ScanWindowsMaintenanceCommand = new AsyncRelayCommand(
            ScanWindowsMaintenanceAsync,
            () => !WindowsMaintenanceBusy && !IsBusy);
        CancelWindowsMaintenanceScanCommand = new RelayCommand(
            CancelWindowsMaintenanceScan,
            () => WindowsMaintenanceBusy);
        CheckEverythingResourcesCommand = new AsyncRelayCommand(
            CheckEverythingResourcesAsync,
            () => !_resourceCheckBusy && !IsBusy);
        OpenWindowsUpdateSettingsCommand = new RelayCommand(OpenWindowsUpdateSettings);
    }

    private void CancelWindowsMaintenanceScan()
    {
        _windowsMaintenanceCts?.Cancel();
    }

    private void OpenWindowsUpdateSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:windowsupdate")
            {
                UseShellExecute = true
            });
            AppendLog(new LogLine(
                DateTimeOffset.Now,
                "[INFO] Opened native Windows Update settings.",
                LogSeverity.Info));
        }
        catch (Exception exception)
        {
            AppendLog(new LogLine(
                DateTimeOffset.Now,
                $"[WARN] Could not open Windows Update settings: {exception.Message}",
                LogSeverity.Warning));
        }
    }

    private async Task ScanWindowsMaintenanceAsync()
    {
        if (WindowsMaintenanceBusy)
        {
            return;
        }

        WindowsMaintenanceBusy = true;
        _windowsMaintenanceCts?.Dispose();
        _windowsMaintenanceCts = new CancellationTokenSource();
        var token = _windowsMaintenanceCts.Token;
        ScanWindowsMaintenanceCommand.RaiseCanExecuteChanged();
        CancelWindowsMaintenanceScanCommand.RaiseCanExecuteChanged();
        WindowsMaintenanceStatusText = "Scanning Windows maintenance state (read-only)…";
        WindowsMaintenanceDetailText = string.Empty;
        WindowsMaintenanceFindings.Clear();
        WindowsMaintenanceDriverAssessments.Clear();

        try
        {
            _windowsMaintenanceService ??= new WindowsMaintenanceService(_powerShellRunnerService);
            var result = await _windowsMaintenanceService
                .ScanAsync(_backendContext, checkUpdates: true, offline: false, token)
                .ConfigureAwait(false);

            RunOnUi(() => ApplyMaintenanceScanResult(result));
        }
        catch (OperationCanceledException)
        {
            RunOnUi(() =>
            {
                WindowsMaintenanceStatusText = "Scan cancelled.";
                AppendLog(new LogLine(
                    DateTimeOffset.Now,
                    "[INFO] Windows maintenance scan cancelled by user.",
                    LogSeverity.Info));
            });
        }
        catch (Exception exception)
        {
            RunOnUi(() =>
            {
                WindowsMaintenanceStatusText = "Scan failed.";
                WindowsMaintenanceDetailText = exception.Message;
                AppendLog(new LogLine(
                    DateTimeOffset.Now,
                    $"[WARN] Windows maintenance scan failed: {exception.Message}",
                    LogSeverity.Warning));
            });
        }
        finally
        {
            RunOnUi(() =>
            {
                WindowsMaintenanceBusy = false;
                ScanWindowsMaintenanceCommand.RaiseCanExecuteChanged();
                CancelWindowsMaintenanceScanCommand.RaiseCanExecuteChanged();
            });
        }
    }

    private void ApplyMaintenanceScanResult(WindowsMaintenanceScanResult result)
    {
        var failed = false;
        if (!result.Succeeded || result.Snapshot is null)
        {
            AppendLog(new LogLine(
                DateTimeOffset.Now,
                $"[WARN] Windows maintenance scan failed ({result.FailureKind}): {result.FailureReason}",
                LogSeverity.Warning));

            var lastGood = _windowsMaintenanceService?.LastGoodSnapshot;
            if (lastGood is null)
            {
                WindowsMaintenanceStatusText = "Scan failed.";
                WindowsMaintenanceDetailText = result.FailureReason ?? result.FailureKind.ToString();
                return;
            }

            failed = true;
            result = result with { Snapshot = lastGood };
        }

        var snapshot = result.Snapshot!;
        if (result.UsedCachedSnapshot)
        {
            AppendLog(new LogLine(
                DateTimeOffset.Now,
                "[INFO] Windows maintenance results served from cache.",
                LogSeverity.Info));
        }

        WindowsMaintenanceStatusText =
            WindowsMaintenanceDisplayMapper.BuildStatusLine(snapshot, failed, result.UsedCachedSnapshot);
        WindowsMaintenanceDetailText = failed
            ? $"{result.FailureReason ?? result.FailureKind.ToString()} — " +
              WindowsMaintenanceDisplayMapper.BuildDetailLine(snapshot)
            : WindowsMaintenanceDisplayMapper.BuildDetailLine(snapshot);

        var findings = WindowsMaintenanceFindings;
        foreach (var line in WindowsMaintenanceDisplayMapper.BuildFindings(snapshot))
        {
            findings.Add(line);
        }

        // Driver Store classification — pure diagnostics; removal is never permitted.
        if (snapshot.DriverStore.Packages.Count > 0)
        {
            var packages = snapshot.DriverStore.Packages
                .Select(p => new DriverStorePackageClassification(
                    p.PublishedName,
                    p.OriginalName,
                    p.Provider,
                    p.Class,
                    p.Version,
                    p.Signer,
                    p.SignatureState,
                    p.Architecture,
                    p.BootCritical,
                    p.DeviceIds))
                .ToList();
            var assessments = DriverStoreClassifier.Classify(packages, snapshot.Bindings);
            foreach (var assessment in assessments)
            {
                WindowsMaintenanceDriverAssessments.Add(
                    $"{assessment.Classification}: {assessment.Explanation}");
            }
        }
    }

    private async Task CheckEverythingResourcesAsync()
    {
        if (_resourceCheckBusy)
        {
            return;
        }

        _resourceCheckBusy = true;
        CheckEverythingResourcesCommand.RaiseCanExecuteChanged();
        ResourceCheckStatusText = "Resolving official resource metadata…";
        ResourceCheckResults.Clear();

        var token = FreshResolveOverlayToken();
        try
        {
            var results = await _managedDownloadResolverService
                .CheckResourcesAsync(
                    _backendContext,
                    onOutput: line => AppendLog(line),
                    cancellationToken: token)
                .ConfigureAwait(false);

            RunOnUi(() =>
            {
                if (results.Count == 0)
                {
                    ResourceCheckStatusText =
                        "No resolvable resources (policy unavailable or all manual exceptions).";
                    return;
                }

                foreach (var resolution in results)
                {
                    var detail = resolution.IsDownloadEligible
                        ? $"{resolution.Descriptor.DisplayName}: {resolution.Version} — expected SHA-256 bound{(resolution.FromCache ? " (cached)" : string.Empty)}"
                        : $"{resolution.Descriptor.DisplayName}: {resolution.State} — {resolution.Reason}";
                    ResourceCheckResults.Add(detail);
                }

                var eligible = results.Count(r => r.IsDownloadEligible);
                ResourceCheckStatusText =
                    $"{eligible}/{results.Count} resource(s) resolved with verified metadata. Nothing was downloaded.";
            });
        }
        catch (OperationCanceledException)
        {
            RunOnUi(() => ResourceCheckStatusText = "Resource check cancelled.");
        }
        catch (Exception exception)
        {
            RunOnUi(() =>
            {
                ResourceCheckStatusText = "Resource check failed.";
                AppendLog(new LogLine(
                    DateTimeOffset.Now,
                    $"[WARN] Resource check failed: {exception.Message}",
                    LogSeverity.Warning));
            });
        }
        finally
        {
            RunOnUi(() =>
            {
                _resourceCheckBusy = false;
                CheckEverythingResourcesCommand.RaiseCanExecuteChanged();
            });
        }
    }
}
