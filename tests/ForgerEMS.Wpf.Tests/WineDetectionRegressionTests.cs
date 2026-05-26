using System;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Compatibility;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Regression coverage for the CI failure where Wine gating leaked into
/// Windows-native test runs. The fix has three parts: tighter detection
/// (only strong Wine signals flip Wine), an AsyncLocal-backed override so
/// parallel tests cannot clobber each other, and explicit
/// <see cref="WineProbeGate.IsWine"/> usage in probe gates.
/// </summary>
[Collection(WineCompatibilityCollection.Name)]
public sealed class WineDetectionRegressionTests
{
    [Fact]
    public void Detection_DoesNotFlipWine_OnCleanWindowsCi()
    {
        // CI runners (and dev machines) do not have any of the strong Wine
        // signals: no WINE* env vars, no STEAM_COMPAT_DATA_PATH, no
        // HKLM\Software\Wine, no ntdll!wine_get_version. Detect() must
        // therefore return IsWine=false and IsCompatibilityMode=false.
        var env = RuntimeCompatibilityService.Detect();

        Assert.False(env.IsWine, "Clean Windows environment must not classify as Wine.");
        Assert.False(env.IsCompatibilityMode, "Clean Windows environment must not enter compatibility mode.");
        Assert.NotEqual(RuntimePlatformKind.WindowsUnderWine, env.Platform);
        Assert.False(env.ForceSoftwareRendering);
    }

    [Fact]
    public void Detection_DoesNotFlipWine_FromProcVersionAlone()
    {
        // Old behaviour treated a readable /proc/version as a Wine signal.
        // That was wrong: containers, sandboxes, and odd CI hosts can
        // surface /proc/version without being a Wine prefix. The detector
        // must require Wine-specific evidence.
        Environment.SetEnvironmentVariable("WINEPREFIX", null);
        Environment.SetEnvironmentVariable("WINESERVER", null);
        Environment.SetEnvironmentVariable("WINELOADER", null);
        Environment.SetEnvironmentVariable("STEAM_COMPAT_DATA_PATH", null);

        var env = RuntimeCompatibilityService.Detect();

        // /proc/version doesn't exist on Windows, but even if a future
        // tooling change made it readable, IsWine must still require an
        // env/registry/ntdll signal — guarded by the strict allow-list in
        // RuntimeCompatibilityService.Detect.
        foreach (var signal in env.DetectionSignals)
        {
            // /proc and /etc/os-release signals MAY still appear (they tell
            // us "the host is Linux-ish") but they MUST NOT have caused
            // IsWine to flip on. We assert the strict postcondition below.
            _ = signal;
        }

        Assert.False(env.IsWine, "Detection must not classify Wine from /proc or /etc/os-release alone.");
    }

    [Fact]
    public void SystemHealthEvaluator_PenalizesConfidence_ForUnknownTpm_OnNativeWindows()
    {
        // The native Windows behavior must be preserved: unknown TPM /
        // Secure Boot fields drop confidence and surface the canonical
        // "verify in BIOS/UEFI" message. The original CI failure happened
        // because a Wine override leaked into this code path and replaced
        // the message with "not checked in Wine compatibility mode".
        using var _ = WineProbeGate.PushOverride(BuildNativeEnv());

        var profile = new SystemProfile
        {
            OverallStatus = "READY",
            DiskStatus = "READY",
            RamStatus = "READY",
            BatteryStatus = "READY",
            TpmPresent = null,
            TpmReady = null,
            TpmStatus = "UNKNOWN",
            SecureBoot = null,
            SecureBootStatus = "UNKNOWN"
        };

        var evaluation = SystemHealthEvaluator.Evaluate(profile);

        Assert.True(evaluation.ConfidenceScore < 100,
            "Native Windows path must reduce confidence when TPM/SecureBoot are unknown.");
        Assert.Contains(evaluation.DetectedIssues, issue =>
            issue.Contains("TPM state is unknown", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(evaluation.DetectedIssues, issue =>
            issue.Contains("not checked in Wine compatibility mode", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SystemHealthEvaluator_EmitsWineMessages_OnlyWhenWineExplicitlyForced()
    {
        using var _ = WineProbeGate.PushOverride(BuildWineEnv());

        var profile = new SystemProfile
        {
            OverallStatus = "READY",
            DiskStatus = "READY",
            RamStatus = "READY",
            BatteryStatus = "READY",
            TpmPresent = null,
            TpmReady = null,
            TpmStatus = "UNKNOWN",
            SecureBoot = null,
            SecureBootStatus = "UNKNOWN"
        };

        var evaluation = SystemHealthEvaluator.Evaluate(profile);

        Assert.Contains(evaluation.DetectedIssues, issue =>
            issue.Contains("not checked in Wine compatibility mode", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(evaluation.DetectedIssues, issue =>
            issue.Contains("TPM state is unknown; verify in BIOS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WineProbeGate_Override_DoesNotLeak_AcrossPushOverrideScopes()
    {
        // Scoped IDisposable helper resets to the prior value on dispose.
        Assert.False(WineProbeGate.IsWine, "Pre-condition: gate must be off.");

        using (WineProbeGate.PushOverride(BuildWineEnv()))
        {
            Assert.True(WineProbeGate.IsWine);
        }

        Assert.False(WineProbeGate.IsWine,
            "Override must be restored to prior value when the scope is disposed.");
    }

    [Fact]
    public async Task WineProbeGate_OverrideIsAsyncLocal_AndDoesNotLeakToParallelLogicalContext()
    {
        // AsyncLocal flows down the current ExecutionContext only. A child
        // task that sets the override must NOT mutate the parent's view of
        // the gate, even after the child has set it.
        Assert.False(WineProbeGate.IsWine);

        await Task.Run(() =>
        {
            using var _ = WineProbeGate.PushOverride(BuildWineEnv());
            Assert.True(WineProbeGate.IsWine);
        });

        Assert.False(WineProbeGate.IsWine,
            "Parent context must not see Wine after a child Task set and unset the override.");
    }

    [Fact]
    public async Task WineProbeGate_OverrideInOneTask_DoesNotLeakToAnotherParallelTask()
    {
        // Two concurrently running logical contexts — only one sets Wine.
        // The other must read false consistently.
        var ready = new TaskCompletionSource<object?>();
        var release = new TaskCompletionSource<object?>();

        var wineTask = Task.Run(async () =>
        {
            using var _ = WineProbeGate.PushOverride(BuildWineEnv());
            Assert.True(WineProbeGate.IsWine);
            ready.SetResult(null);
            await release.Task;
            Assert.True(WineProbeGate.IsWine);
        });

        var nativeTask = Task.Run(async () =>
        {
            await ready.Task;
            // While the wineTask is mid-flight with its AsyncLocal override
            // set, this independent task must still see the gate as off.
            Assert.False(WineProbeGate.IsWine,
                "AsyncLocal override must not bleed into a sibling Task's logical context.");
            release.SetResult(null);
        });

        await Task.WhenAll(wineTask, nativeTask);
        Assert.False(WineProbeGate.IsWine);
    }

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
}
