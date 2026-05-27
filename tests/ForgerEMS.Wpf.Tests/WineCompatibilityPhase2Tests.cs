using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Compatibility;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Second-pass Wine compatibility coverage: probe gating, SystemHealthEvaluator
/// honesty, Linux helper service state machine, USB Builder write gating, and
/// startup log redaction guard.
/// </summary>
[Collection(WineCompatibilitySerialFixture.Name)]
public sealed class WineCompatibilityPhase2Tests
{
    // ---- AcpiThermalZoneSensorProvider Wine gating -----------------------

    [Fact]
    public void AcpiThermalZoneProvider_ReturnsUnsupported_UnderWine()
    {
        WineProbeGate.OverrideEnvironment = BuildWineEnv();
        try
        {
            var result = new AcpiThermalZoneSensorProvider().Read(new SystemProfile());

            Assert.False(result.IsEnabled);
            Assert.Empty(result.Readings);
            Assert.Contains("unsupported", result.FailureReason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(result.Notes, n => n.Contains("Windows-only", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Notes, n => n.Contains("failed", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            WineProbeGate.OverrideEnvironment = null;
        }
    }

    [Fact]
    public void NvidiaSmiProvider_ReturnsNotDetected_UnderWine_WithoutSpawningProcess()
    {
        WineProbeGate.OverrideEnvironment = BuildWineEnv();
        try
        {
            var spawnCalls = 0;
            var provider = new NvidiaSmiSensorProvider
            {
                PathResolverOverride = () =>
                {
                    spawnCalls++;
                    return "/never/should/run/nvidia-smi";
                }
            };

            var result = provider.Read(new SystemProfile());

            Assert.False(result.IsEnabled);
            Assert.Empty(result.Readings);
            Assert.Equal(0, spawnCalls);
            Assert.Contains("Wine", result.FailureReason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(result.Notes, n => n.Contains("Windows-only", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            WineProbeGate.OverrideEnvironment = null;
        }
    }

    // ---- SystemHealthEvaluator Wine confidence neutrality ---------------

    [Fact]
    public void SystemHealthEvaluator_DoesNotPenalizeConfidence_ForUnknownTpmUnderWine()
    {
        // Compare Wine vs Native side-by-side on a profile whose ONLY
        // confidence-affecting unknowns are TPM and Secure Boot. Other
        // category statuses are OK so the global "unknown status" penalty
        // does not mask the difference we care about.
        var profile = new SystemProfile
        {
            OverallStatus = "OK",
            DiskStatus = "OK",
            RamStatus = "OK",
            BatteryStatus = "OK",
            TpmPresent = null,
            TpmReady = null,
            TpmStatus = "Unknown",
            SecureBoot = null,
            SecureBootStatus = "Unknown"
        };

        int wineConfidence;
        int nativeConfidence;

        WineProbeGate.OverrideEnvironment = BuildWineEnv();
        try
        {
            var wineEval = SystemHealthEvaluator.Evaluate(profile);
            wineConfidence = wineEval.ConfidenceScore;

            Assert.Contains(wineEval.DetectedIssues, message =>
                message.Contains("not checked in Wine compatibility mode", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(wineEval.DetectedIssues, message =>
                message.Contains("TPM state is unknown; verify in BIOS", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            WineProbeGate.OverrideEnvironment = null;
        }

        WineProbeGate.OverrideEnvironment = BuildNativeEnv();
        try
        {
            var nativeEval = SystemHealthEvaluator.Evaluate(profile);
            nativeConfidence = nativeEval.ConfidenceScore;
        }
        finally
        {
            WineProbeGate.OverrideEnvironment = null;
        }

        // Wine path must NOT lower confidence relative to the native path
        // for unknown firmware fields — that is the entire point of the
        // gate. Inverse: native path applies the -8/-6 penalties.
        Assert.True(wineConfidence >= nativeConfidence,
            $"Wine confidence ({wineConfidence}) must be at least native confidence ({nativeConfidence}) when TPM/SecureBoot are unknown.");
        Assert.True(nativeConfidence < 100,
            "Sanity: native path with unknown TPM/SecureBoot must still reduce confidence.");
    }

    // ---- LinuxHelperService state machine -------------------------------

    [Fact]
    public async Task LinuxHelperService_ReturnsNotApplicable_OutsideCompatibilityMode()
    {
        var service = new LinuxHelperService(
            environmentSelector: () => BuildNativeEnv());

        var result = await service.ProbeAsync();

        Assert.Equal(LinuxHelperAvailability.NotApplicable, result.Availability);
        Assert.Null(result.Snapshot);
        Assert.False(result.IsAvailable);
    }

    [Fact]
    public async Task LinuxHelperService_ReportsScriptMissing_WhenLocatorReturnsNull()
    {
        var service = new LinuxHelperService(
            environmentSelector: () => BuildWineEnv(),
            scriptLocatorOverride: () => null);

        var result = await service.ProbeAsync();

        Assert.Equal(LinuxHelperAvailability.ScriptMissing, result.Availability);
        Assert.NotNull(result.FailureReason);
        Assert.Contains("could not be located", result.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LinuxHelperService_ReportsShellUnavailable_WhenNoShellResolved()
    {
        var temp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(temp, "stub");
            var service = new LinuxHelperService(
                environmentSelector: () => BuildWineEnv(),
                scriptLocatorOverride: () => temp,
                shellResolverOverride: () => null);

            var result = await service.ProbeAsync();

            Assert.Equal(LinuxHelperAvailability.ShellUnavailable, result.Availability);
            Assert.False(result.IsAvailable);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public async Task LinuxHelperService_ReportsTimedOut_WhenRunnerSignalsTimeout()
    {
        var temp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(temp, "stub");
            var service = new LinuxHelperService(
                environmentSelector: () => BuildWineEnv(),
                scriptLocatorOverride: () => temp,
                shellResolverOverride: () => "/fake/bash",
                runnerOverride: (_, _, _) => new LinuxHelperProcessResult("", "", -1, true));

            var result = await service.ProbeAsync();

            Assert.Equal(LinuxHelperAvailability.TimedOut, result.Availability);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public async Task LinuxHelperService_ReportsFailed_OnNonZeroExit()
    {
        var temp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(temp, "stub");
            var service = new LinuxHelperService(
                environmentSelector: () => BuildWineEnv(),
                scriptLocatorOverride: () => temp,
                shellResolverOverride: () => "/fake/bash",
                runnerOverride: (_, _, _) => new LinuxHelperProcessResult("", "oops", 1, false));

            var result = await service.ProbeAsync();

            Assert.Equal(LinuxHelperAvailability.Failed, result.Availability);
            Assert.Contains(result.Diagnostics, d => d.Contains("oops", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public async Task LinuxHelperService_ReportsParseError_OnInvalidJson()
    {
        var temp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(temp, "stub");
            var service = new LinuxHelperService(
                environmentSelector: () => BuildWineEnv(),
                scriptLocatorOverride: () => temp,
                shellResolverOverride: () => "/fake/bash",
                runnerOverride: (_, _, _) => new LinuxHelperProcessResult("not json", "", 0, false));

            var result = await service.ProbeAsync();

            Assert.Equal(LinuxHelperAvailability.ParseError, result.Availability);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public async Task LinuxHelperService_ReportsUnsupportedSchema_OnUnknownSchemaVersion()
    {
        var temp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(temp, "stub");
            const string future = """{"schema":"forgerems-linux-helper/9999"}""";
            var service = new LinuxHelperService(
                environmentSelector: () => BuildWineEnv(),
                scriptLocatorOverride: () => temp,
                shellResolverOverride: () => "/fake/bash",
                runnerOverride: (_, _, _) => new LinuxHelperProcessResult(future, "", 0, false));

            var result = await service.ProbeAsync();

            Assert.Equal(LinuxHelperAvailability.UnsupportedSchema, result.Availability);
            Assert.NotNull(result.Snapshot);
            Assert.False(result.Snapshot!.IsSchemaSupported);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public async Task LinuxHelperService_ReportsAvailable_OnValidSnapshot()
    {
        var temp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(temp, "stub");
            const string ok = """
            {
              "schema": "forgerems-linux-helper/1",
              "distro": { "pretty_name": "Nobara Linux 43" },
              "kernel": "Linux 6.10 x86_64",
              "tools_available": { "lsblk": true, "smartctl": false },
              "removable_devices": [ { "name": "sdb1", "removable": true, "label": "Ventoy" } ],
              "ventoy_partitions": [ { "name": "sdb1", "label": "Ventoy" } ]
            }
            """;

            var service = new LinuxHelperService(
                environmentSelector: () => BuildWineEnv(),
                scriptLocatorOverride: () => temp,
                shellResolverOverride: () => "/fake/bash",
                runnerOverride: (_, _, _) => new LinuxHelperProcessResult(ok, "", 0, false));

            var result = await service.ProbeAsync();

            Assert.Equal(LinuxHelperAvailability.Available, result.Availability);
            Assert.True(result.IsAvailable);
            Assert.NotNull(result.Snapshot);
            Assert.Equal("Nobara Linux 43", result.Snapshot!.DistroPrettyName);
            Assert.Single(result.Snapshot.VentoyPartitions);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public void LinuxHelperService_ScriptLocator_FindsRepoScript()
    {
        // Sanity check: the locator must succeed when run from the test
        // assembly (which sits inside the repo tree).
        var located = LinuxHelperService.LocateHelperScript();
        Assert.False(string.IsNullOrEmpty(located));
        Assert.True(File.Exists(located));
    }

    // ---- LinuxHelperService NEVER executes destructive commands ---------

    [Fact]
    public void LinuxHelperScript_NeverInvokesDestructiveCommands()
    {
        var path = LinuxHelperService.LocateHelperScript();
        Assert.False(string.IsNullOrEmpty(path));
        var text = File.ReadAllText(path!);
        foreach (var dangerous in new[] { "dd if=", "dd of=", "mkfs", "wipefs", "parted ", "fdisk -w", "sgdisk -o", "mount -o rw" })
        {
            Assert.False(
                text.Contains(dangerous, StringComparison.Ordinal),
                $"Linux helper must remain read-only; found dangerous fragment: {dangerous}");
        }
    }

    // ---- Startup log redaction guard ------------------------------------

    [Fact]
    public void AppStartup_LogsCompatibilitySnapshot_AndDoesNotEmitEnvVarValues()
    {
        var appCs = File.ReadAllText(LocateRepoRelativeFile("src/ForgerEMS.Wpf/App.xaml.cs"));

        // We log signal names, not their values — assertion enforces that
        // we never start a log line with "env:WINEPREFIX=" (which would
        // leak the user's home directory).
        Assert.Contains("Compatibility.DetectionSignals", appCs, StringComparison.Ordinal);
        Assert.DoesNotContain("env:WINEPREFIX=", appCs, StringComparison.Ordinal);
        Assert.DoesNotContain("WINEPREFIX={", appCs, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeCompatibilityService_NeverEmitsEnvVarValuesIntoSignals()
    {
        // Force a Wine signal via env var override.
        Environment.SetEnvironmentVariable("WINEPREFIX", "/home/secretuser/.wine-secret");
        try
        {
            var env = RuntimeCompatibilityService.Detect();
            foreach (var signal in env.DetectionSignals)
            {
                Assert.DoesNotContain("/home/secretuser", signal, StringComparison.Ordinal);
                Assert.DoesNotContain(".wine-secret", signal, StringComparison.Ordinal);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINEPREFIX", null);
        }
    }

    // ---- USB Builder write gate contract --------------------------------

    [Fact]
    public void MainViewModel_CanRunTargetedActions_ConsultsCompatibilityMode()
    {
        // Surface-level contract: CanRunTargetedActions must short-circuit
        // when in compatibility mode so Setup USB / Update USB / Rename USB
        // / Ventoy install / Toolkit update / Full Managed Download are all
        // disabled at once. We do not instantiate MainViewModel directly
        // (its dependency graph is large) — this guards against accidental
        // removal of the gate.
        var path = LocateRepoRelativeFile("src/ForgerEMS.Wpf/ViewModels/MainViewModel.cs");
        var text = File.ReadAllText(path);

        var canRunIndex = text.IndexOf("private bool CanRunTargetedActions()", StringComparison.Ordinal);
        Assert.True(canRunIndex > 0, "CanRunTargetedActions must still exist.");

        // Capture the body up to the next method declaration so we only
        // assert against the right scope.
        var bodyEnd = text.IndexOf("private bool CanRunToolkitScan", canRunIndex, StringComparison.Ordinal);
        Assert.True(bodyEnd > canRunIndex, "Could not locate end of CanRunTargetedActions body.");
        var body = text[canRunIndex..bodyEnd];

        Assert.Contains("_compatibilityEnvironment", body, StringComparison.Ordinal);
        Assert.Contains("IsCompatibilityMode", body, StringComparison.Ordinal);
        Assert.Contains("return false;", body, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_BannerXaml_BindsLinuxHelperSummary()
    {
        var path = LocateRepoRelativeFile("src/ForgerEMS.Wpf/MainWindow.xaml");
        var text = File.ReadAllText(path);

        Assert.Contains("CompatibilityBanner", text, StringComparison.Ordinal);
        Assert.Contains("CompatibilityBannerVisibility", text, StringComparison.Ordinal);
        Assert.Contains("LinuxHelperSummary", text, StringComparison.Ordinal);
        var compatVm = File.ReadAllText(LocateRepoRelativeFile("src/ForgerEMS.Wpf/ViewModels/MainViewModel.Compatibility.cs"));
        Assert.Contains("native Windows for USB writing", compatVm, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("disabled in this prerelease", compatVm, StringComparison.OrdinalIgnoreCase);
    }

    // ---- helpers --------------------------------------------------------

    private static CompatibilityEnvironment BuildWineEnv()
    {
        return new CompatibilityEnvironment(
            RuntimePlatformKind.WindowsUnderWine,
            isWine: true,
            wineVersion: "11.8",
            hostKernel: "Linux 6.10",
            linuxDistro: "Nobara 43",
            isCompatibilityMode: true,
            forceSoftwareRendering: true,
            unsupportedFeatures: Array.Empty<string>(),
            limitedFeatures: Array.Empty<string>(),
            detectionSignals: Array.Empty<string>());
    }

    private static CompatibilityEnvironment BuildNativeEnv()
    {
        return new CompatibilityEnvironment(
            RuntimePlatformKind.WindowsNative,
            isWine: false,
            wineVersion: null,
            hostKernel: null,
            linuxDistro: null,
            isCompatibilityMode: false,
            forceSoftwareRendering: false,
            unsupportedFeatures: Array.Empty<string>(),
            limitedFeatures: Array.Empty<string>(),
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

        throw new FileNotFoundException($"Could not locate {relative}.");
    }
}
