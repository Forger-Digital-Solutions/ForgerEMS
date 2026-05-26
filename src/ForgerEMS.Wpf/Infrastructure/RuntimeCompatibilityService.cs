using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VentoyToolkitSetup.Wpf.Infrastructure;

/// <summary>
/// Detects whether the current process is running on native Windows, inside
/// Wine/Proton on Linux, or on Linux directly. All detection paths are
/// best-effort: any probe that throws is silently treated as a negative
/// signal so ForgerEMS still starts on hardened or locked-down hosts.
/// </summary>
/// <remarks>
/// Detection is deliberately additive — a single positive signal flips
/// <see cref="CompatibilityEnvironment.IsWine"/> on. We never block startup
/// because detection was inconclusive.
/// </remarks>
public static class RuntimeCompatibilityService
{
    private const string WineRegistryPath = @"SOFTWARE\Wine";
    private const string WineNtdllExport = "wine_get_version";

    /// <summary>
    /// Runs every probe and returns an immutable snapshot of the environment.
    /// Catches all exceptions internally; callers can rely on a non-null
    /// return.
    /// </summary>
    public static CompatibilityEnvironment Detect()
    {
        var signals = new List<string>();
        string? wineVersion = null;
        string? linuxDistro = null;
        string? hostKernel = null;
        var isLinuxHost = false;

        // 1. Environment variables Wine/Proton set on the prefix.
        foreach (var name in new[] { "WINEPREFIX", "WINESERVER", "WINELOADER", "WINEDLLPATH", "WINEDLLOVERRIDES" })
        {
            var value = SafeGetEnv(name);
            if (!string.IsNullOrEmpty(value))
            {
                signals.Add($"env:{name} set");
            }
        }

        // 2. Wine registry hive (HKLM\Software\Wine exists on every prefix).
        if (TryProbeWineRegistry(out var registrySignal, out var registryVersion))
        {
            signals.Add(registrySignal);
            wineVersion ??= registryVersion;
        }

        // 3. ntdll!wine_get_version is the canonical detection technique.
        if (TryInvokeWineGetVersion(out var exportVersion))
        {
            signals.Add("ntdll:wine_get_version exported");
            wineVersion ??= exportVersion;
        }

        // 4. /proc/version, /etc/os-release, uname — only readable from a
        //    Wine prefix or actual Linux host.
        if (TryReadFirstLine("/proc/version", out var procVersion))
        {
            signals.Add("file:/proc/version readable");
            hostKernel ??= TrimToFirstNewline(procVersion);
            isLinuxHost = true;
        }

        if (TryReadOsRelease(out var distro))
        {
            signals.Add("file:/etc/os-release readable");
            linuxDistro ??= distro;
            isLinuxHost = true;
        }

        if (string.IsNullOrEmpty(hostKernel) && TryReadFirstLine("/proc/sys/kernel/osrelease", out var kernelOnly))
        {
            signals.Add("file:/proc/sys/kernel/osrelease readable");
            hostKernel ??= kernelOnly.Trim();
            isLinuxHost = true;
        }

        // 5. Steam Proton sentinel.
        if (!string.IsNullOrEmpty(SafeGetEnv("STEAM_COMPAT_DATA_PATH")))
        {
            signals.Add("env:STEAM_COMPAT_DATA_PATH set (Proton)");
        }

        // Wine classification requires STRONG, Wine-specific evidence. /proc
        // and /etc/os-release tell us "the host is Linux-ish" but do NOT
        // prove we are inside a Wine prefix — they can be present in
        // sandboxes, containers, or odd CI environments where a tool surfaces
        // a fake /proc. Without this guard a Windows CI runner with an
        // accessible /proc/version (e.g. via a mapped drive) would flip into
        // compatibility mode and start gating Windows-native probes.
        var isWine = signals.Any(s =>
            s.StartsWith("env:WINE", StringComparison.Ordinal) ||
            s.StartsWith("env:STEAM_COMPAT_DATA_PATH", StringComparison.Ordinal) ||
            s.StartsWith("ntdll:wine", StringComparison.Ordinal) ||
            s.StartsWith("registry:HKLM\\Software\\Wine", StringComparison.Ordinal));

        var platform = ClassifyPlatform(isWine, isLinuxHost);

        // Compatibility mode is now strictly Wine-driven. Pure Linux hosts
        // are reported via Platform/LinuxDistro for diagnostics but do not
        // flip gating — ForgerEMS does not run as a native Linux process,
        // so the platform-Linux path is informational only.
        var isCompatibilityMode = isWine && platform == RuntimePlatformKind.WindowsUnderWine;

        var forceSoftwareRendering = isCompatibilityMode;

        var unsupported = isCompatibilityMode ? BuildUnsupportedFeatureList() : Array.Empty<string>();
        var limited = isCompatibilityMode ? BuildLimitedFeatureList() : Array.Empty<string>();

        return new CompatibilityEnvironment(
            platform,
            isWine,
            wineVersion,
            hostKernel,
            linuxDistro,
            isCompatibilityMode,
            forceSoftwareRendering,
            unsupported,
            limited,
            signals);
    }

    /// <summary>
    /// Pure classifier extracted so tests can exercise the decision matrix
    /// without invoking any I/O.
    /// </summary>
    public static RuntimePlatformKind ClassifyPlatform(bool isWine, bool isLinuxHost)
    {
        // Order matters: a process that thinks it is Windows AND has Wine
        // signals is the canonical Wine-on-Linux case.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return isWine ? RuntimePlatformKind.WindowsUnderWine : RuntimePlatformKind.WindowsNative;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || isLinuxHost)
        {
            return RuntimePlatformKind.LinuxHostLikely;
        }

        return RuntimePlatformKind.Unknown;
    }

    /// <summary>
    /// Stable list of Windows-only subsystems ForgerEMS should not attempt
    /// under Wine. Lives here so the diagnostic file and the UI banner stay
    /// in sync without each maintaining its own copy.
    /// </summary>
    public static IReadOnlyList<string> BuildUnsupportedFeatureList()
    {
        return new[]
        {
            "WMI hardware/security probes (TPM, Secure Boot, BitLocker, Defender)",
            "Windows-only driver and service enumeration",
            "Native sensor providers (LibreHardwareMonitor)",
            "Admin/UAC elevated relaunch",
            "Native Windows installer integration (UEFI/NVRAM)"
        };
    }

    /// <summary>
    /// Subsystems that should work in a reduced form under Wine.
    /// </summary>
    public static IReadOnlyList<string> BuildLimitedFeatureList()
    {
        return new[]
        {
            "Animated background visuals (downgraded to static under software rendering)",
            "USB device enumeration (Windows API only; richer enumeration requires the Linux helper)",
            "Live system intelligence scans (limited to features that do not require WMI)"
        };
    }

    private static bool TryProbeWineRegistry(out string signal, out string? version)
    {
        signal = string.Empty;
        version = null;

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(WineRegistryPath);
            if (key is null)
            {
                return false;
            }

            signal = "registry:HKLM\\Software\\Wine present";
            try
            {
                version = key.GetValue("Version") as string;
            }
            catch
            {
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryInvokeWineGetVersion(out string? version)
    {
        version = null;

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            if (!NativeLibrary.TryLoad("ntdll.dll", out var handle))
            {
                return false;
            }

            try
            {
                if (!NativeLibrary.TryGetExport(handle, WineNtdllExport, out var export))
                {
                    return false;
                }

                var thunk = Marshal.GetDelegateForFunctionPointer<WineGetVersionDelegate>(export);
                var ptr = thunk();
                version = Marshal.PtrToStringAnsi(ptr);
                return true;
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadFirstLine(string path, out string content)
    {
        content = string.Empty;
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            content = File.ReadAllText(path);
            return !string.IsNullOrWhiteSpace(content);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadOsRelease(out string? distro)
    {
        distro = null;
        try
        {
            if (!File.Exists("/etc/os-release"))
            {
                return false;
            }

            foreach (var line in File.ReadAllLines("/etc/os-release"))
            {
                if (line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal))
                {
                    distro = line["PRETTY_NAME=".Length..].Trim().Trim('"');
                    return true;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? SafeGetEnv(string name)
    {
        try
        {
            return Environment.GetEnvironmentVariable(name);
        }
        catch
        {
            return null;
        }
    }

    private static string TrimToFirstNewline(string value)
    {
        var newline = value.IndexOfAny(new[] { '\r', '\n' });
        return newline < 0 ? value.Trim() : value[..newline].Trim();
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate IntPtr WineGetVersionDelegate();
}
