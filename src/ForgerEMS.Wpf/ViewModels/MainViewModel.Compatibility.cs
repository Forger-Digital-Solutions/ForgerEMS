using System.Linq;
using System.Windows;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Services.Compatibility;

namespace VentoyToolkitSetup.Wpf.ViewModels;

/// <summary>
/// Compatibility banner state. Lives in its own partial file so the giant
/// <see cref="MainViewModel"/> file stays untouched.
/// </summary>
public sealed partial class MainViewModel
{
    private CompatibilityEnvironment? _compatibilityEnvironment;

    /// <summary>
    /// Set once by <c>App.OnStartup</c> after the snapshot has been
    /// captured. Null means "native Windows with no compatibility concerns".
    /// </summary>
    public CompatibilityEnvironment? CompatibilityEnvironment
    {
        get => _compatibilityEnvironment;
        set
        {
            if (ReferenceEquals(_compatibilityEnvironment, value))
            {
                return;
            }

            _compatibilityEnvironment = value;
            OnPropertyChanged(nameof(CompatibilityEnvironment));
            OnPropertyChanged(nameof(IsCompatibilityBannerVisible));
            OnPropertyChanged(nameof(CompatibilityBannerVisibility));
            OnPropertyChanged(nameof(CompatibilityBannerHeadline));
            OnPropertyChanged(nameof(CompatibilityBannerBody));
        }
    }

    public bool IsCompatibilityBannerVisible =>
        _compatibilityEnvironment is { IsCompatibilityMode: true };

    public Visibility CompatibilityBannerVisibility =>
        IsCompatibilityBannerVisible ? Visibility.Visible : Visibility.Collapsed;

    public string CompatibilityBannerHeadline =>
        _compatibilityEnvironment?.Platform switch
        {
            RuntimePlatformKind.WindowsUnderWine => "Running in Wine compatibility mode",
            RuntimePlatformKind.LinuxHostLikely => "Running on Linux in compatibility mode",
            _ => string.Empty
        };

    /// <summary>
    /// Honest, non-alarming explanation. Avoids the word "broken"; states
    /// which Windows-only subsystems are off so a technician knows what to
    /// expect before they run a scan.
    /// </summary>
    public string CompatibilityBannerBody
    {
        get
        {
            if (_compatibilityEnvironment is null || !_compatibilityEnvironment.IsCompatibilityMode)
            {
                return string.Empty;
            }

            var env = _compatibilityEnvironment;
            var distro = string.IsNullOrEmpty(env.LinuxDistro) ? "Linux" : env.LinuxDistro;
            var wine = string.IsNullOrEmpty(env.WineVersion) ? "Wine" : $"Wine {env.WineVersion}";
            return $"ForgerEMS is running under {wine} on {distro}. " +
                   "Catalog browsing, profiles, and downloads stay available. " +
                   "USB drive write actions (Setup USB, Update USB, Rename USB, Install/Update Ventoy, Toolkit Update, Full Managed Download) are disabled in this prerelease — use native Windows for USB writing. " +
                   "Windows-only diagnostics, hardware sensors, TPM/Secure Boot/BitLocker probes, and admin relaunch are limited or unavailable.";
        }
    }

    private LinuxHelperResult? _linuxHelperResult;

    /// <summary>
    /// Read-only Linux helper snapshot, populated asynchronously by
    /// <c>App.OnStartup</c> when compatibility mode is active.
    /// </summary>
    public LinuxHelperResult? LinuxHelperResult
    {
        get => _linuxHelperResult;
        set
        {
            if (ReferenceEquals(_linuxHelperResult, value))
            {
                return;
            }

            _linuxHelperResult = value;
            OnPropertyChanged(nameof(LinuxHelperResult));
            OnPropertyChanged(nameof(LinuxHelperSummary));
            OnPropertyChanged(nameof(IsLinuxHelperAvailable));
        }
    }

    public bool IsLinuxHelperAvailable => _linuxHelperResult is { IsAvailable: true };

    /// <summary>
    /// Plain-text one-paragraph summary shown in the compatibility banner /
    /// diagnostics card. Returns empty string when we have no result yet so
    /// the UI can collapse the field.
    /// </summary>
    public string LinuxHelperSummary
    {
        get
        {
            if (_linuxHelperResult is null)
            {
                return string.Empty;
            }

            var result = _linuxHelperResult;
            if (!result.IsAvailable || result.Snapshot is null)
            {
                return $"Linux helper: {result.Availability} — {result.FailureReason ?? "unavailable"}.";
            }

            var snap = result.Snapshot;
            var missingTools = snap.ToolsAvailable.Where(pair => !pair.Value).Select(pair => pair.Key).ToList();
            var distro = string.IsNullOrEmpty(snap.DistroPrettyName) ? "(unknown distro)" : snap.DistroPrettyName;
            var kernel = string.IsNullOrEmpty(snap.Kernel) ? "(unknown kernel)" : snap.Kernel;
            var missingClause = missingTools.Count == 0
                ? "all expected tools available"
                : $"missing tools: {string.Join(", ", missingTools)}";

            return $"Linux helper: available. {distro} / {kernel}. " +
                   $"Removable devices: {snap.RemovableDevices.Count}. " +
                   $"Ventoy partitions: {snap.VentoyPartitions.Count}. " +
                   $"{missingClause}.";
        }
    }
}
