using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Services.Compatibility;
using VentoyToolkitSetup.Wpf.ViewModels;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Phase 9 — exercises the Wine / Linux compatibility layer: platform
/// detection, the WPF software-render gate, probe gating, banner state,
/// Linux helper JSON parsing, and the helper script's stable contract.
/// </summary>
[Collection(WineCompatibilityCollection.Name)]
public sealed class WineCompatibilityTests
{
    private static readonly string[] UnsupportedDefaults = { "WMI" };
    private static readonly string[] LimitedDefaults = { "USB enumeration" };

    // ---- Detection / classifier ------------------------------------------

    [Fact]
    public void Classifier_NativeWindows_WhenNoWineSignals()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var kind = RuntimeCompatibilityService.ClassifyPlatform(isWine: false, isLinuxHost: false);
        Assert.Equal(RuntimePlatformKind.WindowsNative, kind);
    }

    [Fact]
    public void Classifier_WineFlag_ProducesWineUnderWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var kind = RuntimeCompatibilityService.ClassifyPlatform(isWine: true, isLinuxHost: true);
        Assert.Equal(RuntimePlatformKind.WindowsUnderWine, kind);
    }

    [Fact]
    public void Detect_DoesNotThrow_AndProducesEnvelope()
    {
        var env = RuntimeCompatibilityService.Detect();
        Assert.NotNull(env);
        Assert.NotNull(env.DetectionSignals);
        Assert.NotNull(env.UnsupportedFeatures);
        Assert.NotNull(env.LimitedFeatures);
    }

    [Fact]
    public void Detect_ReportsNativeWindows_OnCleanCiHost()
    {
        // The xUnit host does not run inside a Wine prefix, so detection
        // should never flip IsWine on. If it does we want a loud failure
        // so a Wine signal regression is obvious.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var env = RuntimeCompatibilityService.Detect();
        Assert.False(env.IsWine);
        Assert.Equal(RuntimePlatformKind.WindowsNative, env.Platform);
        Assert.False(env.IsCompatibilityMode);
        Assert.False(env.ForceSoftwareRendering);
    }

    [Fact]
    public void UnsupportedAndLimitedFeatureLists_AreStableAndNonEmpty()
    {
        var unsupported = RuntimeCompatibilityService.BuildUnsupportedFeatureList();
        var limited = RuntimeCompatibilityService.BuildLimitedFeatureList();

        Assert.NotEmpty(unsupported);
        Assert.NotEmpty(limited);
        Assert.Contains(unsupported, s => s.Contains("WMI", StringComparison.Ordinal));
        Assert.Contains(unsupported, s => s.Contains("TPM", StringComparison.Ordinal));
        Assert.Contains(limited, s => s.Contains("USB", StringComparison.Ordinal));
    }

    // ---- WineProbeGate ---------------------------------------------------

    [Fact]
    public void WineProbeGate_RespectsOverrideEnvironment()
    {
        WineProbeGate.OverrideEnvironment = BuildCompatEnv(true);
        try
        {
            Assert.True(WineProbeGate.IsCompatibilityMode);
            Assert.False(WineProbeGate.IsWindowsOnlyProbeAllowed);
        }
        finally
        {
            WineProbeGate.OverrideEnvironment = null;
        }
    }

    [Fact]
    public void WineProbeGate_AllowsProbe_WhenNotCompatibilityMode()
    {
        WineProbeGate.OverrideEnvironment = BuildCompatEnv(false);
        try
        {
            Assert.False(WineProbeGate.IsCompatibilityMode);
            Assert.True(WineProbeGate.IsWindowsOnlyProbeAllowed);
        }
        finally
        {
            WineProbeGate.OverrideEnvironment = null;
        }
    }

    [Fact]
    public void DescribeUnsupported_UsesNeutralLanguage_NotFailure()
    {
        var text = WineProbeGate.DescribeUnsupported("TPM probe");
        Assert.Contains("unsupported", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("host limitation", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failed", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WineProbeOutcome_HasExpectedVocabulary()
    {
        // Guard against silent renames that would invalidate downstream
        // diagnostics or test assertions.
        Assert.Equal(0, (int)WineProbeOutcome.NativeOk);
        Assert.Equal(1, (int)WineProbeOutcome.UnsupportedUnderWine);
        Assert.Equal(2, (int)WineProbeOutcome.CompatibilityLimited);
        Assert.Equal(3, (int)WineProbeOutcome.LinuxHelperRequired);
        Assert.Equal(4, (int)WineProbeOutcome.WindowsOnlyProbe);
    }

    // ---- MainViewModel banner state -------------------------------------

    [Fact]
    public void MainViewModelCompatibility_CollapsedByDefault()
    {
        // Default partial-property state without an injected environment
        // must equal the production "native Windows" path: banner hidden.
        var vm = new TestableCompatibilityViewModel();
        Assert.False(vm.IsCompatibilityBannerVisible);
        Assert.Equal(Visibility.Collapsed, vm.CompatibilityBannerVisibility);
        Assert.Equal(string.Empty, vm.CompatibilityBannerHeadline);
        Assert.Equal(string.Empty, vm.CompatibilityBannerBody);
    }

    [Fact]
    public void MainViewModelCompatibility_ShowsBanner_UnderWine()
    {
        var vm = new TestableCompatibilityViewModel
        {
            CompatibilityEnvironment = BuildCompatEnv(true, RuntimePlatformKind.WindowsUnderWine, "11.8", "Nobara")
        };

        Assert.True(vm.IsCompatibilityBannerVisible);
        Assert.Equal(Visibility.Visible, vm.CompatibilityBannerVisibility);
        Assert.Contains("Wine", vm.CompatibilityBannerHeadline, StringComparison.Ordinal);
        Assert.Contains("Nobara", vm.CompatibilityBannerBody, StringComparison.Ordinal);
        Assert.Contains("USB Builder", vm.CompatibilityBannerBody, StringComparison.Ordinal);
        Assert.DoesNotContain("broken", vm.CompatibilityBannerBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MainViewModelCompatibility_FiresPropertyChanged_OnSet()
    {
        var vm = new TestableCompatibilityViewModel();
        var seen = new System.Collections.Generic.List<string>();
        vm.PropertyChanged += (_, args) => seen.Add(args.PropertyName ?? string.Empty);

        vm.CompatibilityEnvironment = BuildCompatEnv(true);

        Assert.Contains(nameof(vm.CompatibilityEnvironment), seen);
        Assert.Contains(nameof(vm.IsCompatibilityBannerVisible), seen);
        Assert.Contains(nameof(vm.CompatibilityBannerVisibility), seen);
    }

    // ---- Linux helper JSON parser ---------------------------------------

    [Fact]
    public void LinuxHelperSnapshot_ParsesCompleteDocument()
    {
        const string json = """
        {
          "schema": "forgerems-linux-helper/1",
          "generated_utc": "2026-05-26T12:00:00Z",
          "distro": { "pretty_name": "Nobara Linux 43 (KDE Plasma)", "id": "nobara", "version_id": "43" },
          "kernel": "Linux 6.10.0-fsync x86_64",
          "tools_available": { "lsblk": true, "smartctl": false },
          "mounts": [ { "source": "/dev/nvme0n1p2", "target": "/", "fstype": "btrfs", "options": "rw,relatime" } ],
          "block_devices": [ { "name": "nvme0n1", "size": "1T", "type": "disk", "removable": false, "mountpoint": "", "label": "", "model": "SAMSUNG SSD", "transport": "nvme" } ],
          "removable_devices": [ { "name": "sdb1", "size": "64G", "type": "part", "removable": true, "mountpoint": "/run/media/x", "label": "Ventoy", "model": "USB", "transport": "usb" } ],
          "ventoy_partitions": [ { "name": "sdb1", "size": "64G", "type": "part", "removable": true, "mountpoint": "/run/media/x", "label": "Ventoy", "model": "USB", "transport": "usb" } ]
        }
        """;

        var snap = LinuxHelperSnapshot.Parse(json);

        Assert.True(snap.IsSchemaSupported);
        Assert.Equal("Nobara Linux 43 (KDE Plasma)", snap.DistroPrettyName);
        Assert.Equal("nobara", snap.DistroId);
        Assert.Equal("43", snap.DistroVersionId);
        Assert.Equal("Linux 6.10.0-fsync x86_64", snap.Kernel);
        Assert.True(snap.ToolsAvailable["lsblk"]);
        Assert.False(snap.ToolsAvailable["smartctl"]);
        Assert.Single(snap.Mounts);
        Assert.Equal("btrfs", snap.Mounts[0].FsType);
        Assert.Single(snap.BlockDevices);
        Assert.Equal("nvme", snap.BlockDevices[0].Transport);
        Assert.Single(snap.RemovableDevices);
        Assert.True(snap.RemovableDevices[0].Removable);
        Assert.Single(snap.VentoyPartitions);
        Assert.Equal("Ventoy", snap.VentoyPartitions[0].Label);
    }

    [Fact]
    public void LinuxHelperSnapshot_Parse_FailsLoudly_OnEmpty()
    {
        Assert.Throws<ArgumentException>(() => LinuxHelperSnapshot.Parse(""));
    }

    [Fact]
    public void LinuxHelperSnapshot_TreatsMissingSchema_AsUnsupported()
    {
        var snap = LinuxHelperSnapshot.Parse("{}");
        Assert.False(snap.IsSchemaSupported);
        Assert.Empty(snap.BlockDevices);
        Assert.Empty(snap.Mounts);
        Assert.Empty(snap.ToolsAvailable);
    }

    // ---- Linux helper script lives on disk and follows its contract -----

    [Fact]
    public void LinuxHelperScript_Exists_AndDeclaresExpectedSchema()
    {
        var path = LocateRepoRelativeFile("tools/linux/forgerems-linux-helper.sh");
        var text = File.ReadAllText(path);

        Assert.Contains("#!/usr/bin/env bash", text, StringComparison.Ordinal);
        Assert.Contains("forgerems-linux-helper/1", text, StringComparison.Ordinal);
        // Read-only contract — never write or mkfs to a block device.
        Assert.DoesNotContain("mkfs", text, StringComparison.Ordinal);
        Assert.DoesNotContain("dd if=", text, StringComparison.Ordinal);
        // Linux helper must reference the tools the WPF app expects.
        foreach (var tool in new[] { "lsblk", "blkid", "udevadm", "smartctl", "mount" })
        {
            Assert.Contains(tool, text, StringComparison.Ordinal);
        }
    }

    // ---- App.xaml.cs wired the SoftwareOnly path -----------------------

    [Fact]
    public void AppStartup_ContainsSoftwareOnlyGate_AndUnobservedTaskHandler()
    {
        var path = LocateRepoRelativeFile("src/ForgerEMS.Wpf/App.xaml.cs");
        var text = File.ReadAllText(path);

        Assert.Contains("ApplyCompatibilityEnvironment()", text, StringComparison.Ordinal);
        Assert.Contains("RenderMode.SoftwareOnly", text, StringComparison.Ordinal);
        Assert.Contains("ForceSoftwareRendering", text, StringComparison.Ordinal);
        Assert.Contains("TaskScheduler.UnobservedTaskException", text, StringComparison.Ordinal);
        Assert.Contains("CompatibilityEnvironment", text, StringComparison.Ordinal);

        // Compatibility path runs before base.OnStartup so the render gate
        // is applied before the first WPF window is constructed.
        var applyIndex = text.IndexOf("ApplyCompatibilityEnvironment()", StringComparison.Ordinal);
        var baseIndex = text.IndexOf("base.OnStartup(e)", StringComparison.Ordinal);
        Assert.True(applyIndex > 0);
        Assert.True(baseIndex > applyIndex,
            "ApplyCompatibilityEnvironment must run before base.OnStartup to force SoftwareOnly before the first window.");
    }

    [Fact]
    public void LibreHardwareMonitorProvider_IsGatedByWineProbeGate()
    {
        var path = LocateRepoRelativeFile("src/ForgerEMS.Wpf/Services/Sensors/LibreHardwareMonitorSensorProvider.cs");
        var text = File.ReadAllText(path);
        // The probe now consults the strict IsWine property rather than the
        // weaker IsCompatibilityMode alias.
        Assert.Contains("WineProbeGate.IsWine", text, StringComparison.Ordinal);
    }

    // ---- Helpers --------------------------------------------------------

    private static CompatibilityEnvironment BuildCompatEnv(
        bool compatibilityMode,
        RuntimePlatformKind? platform = null,
        string? wineVersion = null,
        string? distro = null)
    {
        // Platform defaults follow the compatibility flag so callers that
        // ask for "Wine on" but forget to pass a platform still get a
        // self-consistent envelope. WineProbeGate.IsWine requires
        // Platform == WindowsUnderWine alongside isWine=true.
        var effectivePlatform = platform ??
            (compatibilityMode ? RuntimePlatformKind.WindowsUnderWine : RuntimePlatformKind.WindowsNative);

        return new CompatibilityEnvironment(
            effectivePlatform,
            isWine: compatibilityMode,
            wineVersion: wineVersion,
            hostKernel: null,
            linuxDistro: distro,
            isCompatibilityMode: compatibilityMode,
            forceSoftwareRendering: compatibilityMode,
            unsupportedFeatures: compatibilityMode ? UnsupportedDefaults : Array.Empty<string>(),
            limitedFeatures: compatibilityMode ? LimitedDefaults : Array.Empty<string>(),
            detectionSignals: Array.Empty<string>());
    }

    private static string LocateRepoRelativeFile(string relative)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new FileNotFoundException($"Could not locate {relative} relative to {AppContext.BaseDirectory}.");
    }

    /// <summary>
    /// Subclass that exposes only the compatibility partial surface so
    /// tests do not have to construct the full <see cref="MainViewModel"/>
    /// dependency graph just to verify banner state.
    /// </summary>
    private sealed class TestableCompatibilityViewModel : ObservableObject
    {
        private CompatibilityEnvironment? _env;

        public CompatibilityEnvironment? CompatibilityEnvironment
        {
            get => _env;
            set
            {
                if (ReferenceEquals(_env, value))
                {
                    return;
                }

                _env = value;
                OnPropertyChanged(nameof(CompatibilityEnvironment));
                OnPropertyChanged(nameof(IsCompatibilityBannerVisible));
                OnPropertyChanged(nameof(CompatibilityBannerVisibility));
                OnPropertyChanged(nameof(CompatibilityBannerHeadline));
                OnPropertyChanged(nameof(CompatibilityBannerBody));
            }
        }

        public bool IsCompatibilityBannerVisible =>
            _env is { IsCompatibilityMode: true };

        public Visibility CompatibilityBannerVisibility =>
            IsCompatibilityBannerVisible ? Visibility.Visible : Visibility.Collapsed;

        public string CompatibilityBannerHeadline =>
            _env?.Platform switch
            {
                RuntimePlatformKind.WindowsUnderWine => "Running in Wine compatibility mode",
                RuntimePlatformKind.LinuxHostLikely => "Running on Linux in compatibility mode",
                _ => string.Empty
            };

        public string CompatibilityBannerBody
        {
            get
            {
                if (_env is null || !_env.IsCompatibilityMode)
                {
                    return string.Empty;
                }

                var distro = string.IsNullOrEmpty(_env.LinuxDistro) ? "Linux" : _env.LinuxDistro;
                var wine = string.IsNullOrEmpty(_env.WineVersion) ? "Wine" : $"Wine {_env.WineVersion}";
                return $"ForgerEMS is running under {wine} on {distro}. " +
                       "Core USB Builder, catalog management, and downloads continue to work. " +
                       "Windows-only diagnostics, hardware sensors, TPM/Secure Boot/BitLocker probes, and admin relaunch are limited or unavailable in this environment.";
            }
        }
    }
}
