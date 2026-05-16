using System;
using System.IO;
using Microsoft.Win32;

namespace VentoyToolkitSetup.Wpf.Configuration;

public static class DeepSensorModeValues
{
    public const string Off = "Off";
    public const string ReadOnly = "ReadOnly";
    public const string AdminReadOnly = "AdminReadOnly";
}

public static class DeepSensorModeSources
{
    public const string Environment = "Environment";
    public const string UserSetting = "UserSetting";
    public const string InstallerDefault = "InstallerDefault";
    public const string BuiltInDefault = "BuiltInDefault";
}

public sealed record DeepSensorModeResolution(
    string Mode,
    string Source,
    bool IsEnabled,
    string TechnicianNote,
    bool IsInvalid = false)
{
    public string DisplaySource => Source switch
    {
        DeepSensorModeSources.Environment => "environment variable",
        DeepSensorModeSources.UserSetting => "user setting",
        DeepSensorModeSources.InstallerDefault => "installer default",
        _ => "built-in default"
    };
}

public sealed class DeepSensorModeResolverOptions
{
    public Func<string, string?>? EnvironmentReader { get; init; }

    public string? LocalAppDataRoot { get; init; }

    public Func<string?>? InstallDefaultReader { get; init; }

    public Action<string>? WarningSink { get; init; }
}

public static class DeepSensorModeResolver
{
    public const string EnvironmentVariableName = "FORGEREMS_DEEP_SENSOR_MODE";
    public const string RegistrySubKey = @"Software\ForgerEMS";
    public const string RegistryValueName = "DeepSensorMode";

    public static DeepSensorModeResolution Resolve(DeepSensorModeResolverOptions? options = null)
    {
        options ??= new DeepSensorModeResolverOptions();
        var environmentReader = options.EnvironmentReader ?? Environment.GetEnvironmentVariable;
        var environmentValue = environmentReader(EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return Normalize(environmentValue, DeepSensorModeSources.Environment, options.WarningSink);
        }

        var userValue = ReadUserMode(options.LocalAppDataRoot);
        if (!string.IsNullOrWhiteSpace(userValue))
        {
            return Normalize(userValue, DeepSensorModeSources.UserSetting, options.WarningSink);
        }

        var installDefault = options.InstallDefaultReader is not null
            ? SafeRead(options.InstallDefaultReader, options.WarningSink)
            : ReadInstallDefault(options.WarningSink);
        if (!string.IsNullOrWhiteSpace(installDefault))
        {
            return Normalize(installDefault, DeepSensorModeSources.InstallerDefault, options.WarningSink);
        }

        return new DeepSensorModeResolution(
            DeepSensorModeValues.Off,
            DeepSensorModeSources.BuiltInDefault,
            IsEnabled: false,
            "Deep Sensor Mode defaults to Off until the installer or user enables local read-only hardware sensors.");
    }

    public static void SaveUserMode(string mode, string? localAppDataRoot = null)
    {
        var normalized = Normalize(mode, DeepSensorModeSources.UserSetting, null);
        if (normalized.IsInvalid)
        {
            throw new ArgumentException($"Unsupported Deep Sensor Mode value: {mode}", nameof(mode));
        }

        var path = GetUserSettingPath(localAppDataRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, normalized.Mode);
    }

    public static string GetUserSettingPath(string? localAppDataRoot = null)
    {
        var root = string.IsNullOrWhiteSpace(localAppDataRoot)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localAppDataRoot;
        return Path.Combine(root, "ForgerEMS", "settings", "deep-sensor-mode.txt");
    }

    private static string? ReadUserMode(string? localAppDataRoot)
    {
        try
        {
            var path = GetUserSettingPath(localAppDataRoot);
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadInstallDefault(Action<string>? warningSink)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegistrySubKey, writable: false);
            return key?.GetValue(RegistryValueName)?.ToString();
        }
        catch (Exception ex)
        {
            warningSink?.Invoke($"Deep Sensor Mode installer default could not be read: {ex.Message}");
            return null;
        }
    }

    private static string? SafeRead(Func<string?> reader, Action<string>? warningSink)
    {
        try
        {
            return reader();
        }
        catch (Exception ex)
        {
            warningSink?.Invoke($"Deep Sensor Mode install default reader failed: {ex.Message}");
            return null;
        }
    }

    private static DeepSensorModeResolution Normalize(string rawValue, string source, Action<string>? warningSink)
    {
        var value = (rawValue ?? string.Empty).Trim();
        if (value.Equals(DeepSensorModeValues.Off, StringComparison.OrdinalIgnoreCase))
        {
            return new DeepSensorModeResolution(
                DeepSensorModeValues.Off,
                source,
                IsEnabled: false,
                $"Deep Sensor Mode is Off via {FormatSource(source)}.");
        }

        if (value.Equals(DeepSensorModeValues.ReadOnly, StringComparison.OrdinalIgnoreCase) ||
            value.Equals("ReadOnlyLocalSensors", StringComparison.OrdinalIgnoreCase))
        {
            return new DeepSensorModeResolution(
                DeepSensorModeValues.ReadOnly,
                source,
                IsEnabled: true,
                $"ForgerEMS Deep Sensor Mode is ReadOnly via {FormatSource(source)}. Sensors are local and read-only.");
        }

        if (value.Equals(DeepSensorModeValues.AdminReadOnly, StringComparison.OrdinalIgnoreCase))
        {
            return new DeepSensorModeResolution(
                DeepSensorModeValues.AdminReadOnly,
                source,
                IsEnabled: false,
                $"AdminReadOnly is reserved for future explicit admin scans; current beta stays local read-only without elevation.");
        }

        warningSink?.Invoke($"Invalid Deep Sensor Mode value '{value}' from {source}; falling back to Off.");
        return new DeepSensorModeResolution(
            DeepSensorModeValues.Off,
            source,
            IsEnabled: false,
            $"Invalid Deep Sensor Mode value from {FormatSource(source)}; using Off.",
            IsInvalid: true);
    }

    private static string FormatSource(string source) => source switch
    {
        DeepSensorModeSources.Environment => "environment variable",
        DeepSensorModeSources.UserSetting => "user setting",
        DeepSensorModeSources.InstallerDefault => "installer default",
        _ => "built-in default"
    };
}
