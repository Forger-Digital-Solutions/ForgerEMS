using System.Diagnostics;
using System.IO;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Infrastructure;

/// <summary>Spawns the isolated Kyra.Sdk dogfood tool (avoids Kyra.Core vs Kyra.Local.Core type conflicts in WPF).</summary>
internal static class KyraSdkDogfoodProcessLauncher
{
    public const string CliArgument = "--kyra-sdk-dogfood";

    public static async Task<int> RunAsync(string[] args)
    {
        var toolDir = Path.Combine(GetExecutableBaseDirectory(), "tools", "kyra-sdk-dogfood");
        var exePath = Path.Combine(toolDir, "ForgerEMS.Kyra.SdkDogfood.exe");
        var dllPath = Path.Combine(toolDir, "ForgerEMS.Kyra.SdkDogfood.dll");
        var launchPath = File.Exists(exePath) ? exePath : dllPath;

        if (!File.Exists(launchPath))
        {
            AppendStartupLog($"Kyra SDK dogfood tool missing: {toolDir}");
            return 1;
        }

        var forwarded = BuildForwardedArguments(args);
        var startInfo = new ProcessStartInfo
        {
            FileName = launchPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? "dotnet" : launchPath,
            Arguments = launchPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? $"\"{launchPath}\" {forwarded}"
                : forwarded,
            WorkingDirectory = toolDir,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo);
        if (process is null)
            return 1;

        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode;
    }

    private static string BuildForwardedArguments(string[] args)
    {
        var parts = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], CliArgument, StringComparison.OrdinalIgnoreCase))
                continue;

            parts.Add(Quote(args[i]));
            if (IsValueSwitch(args[i]) && i + 1 < args.Length)
            {
                i++;
                parts.Add(Quote(args[i]));
            }
        }

        if (!parts.Any(p => p.Contains("--kyra-sdk-version", StringComparison.OrdinalIgnoreCase)))
        {
            parts.Add("--kyra-sdk-version");
            parts.Add(Quote(AppReleaseInfo.DisplayVersion));
        }

        return string.Join(' ', parts);
    }

    private static bool IsValueSwitch(string arg) =>
        string.Equals(arg, "--kyra-sdk-prompt", StringComparison.OrdinalIgnoreCase);

    private static string Quote(string value) =>
        value.Contains(' ') ? $"\"{value}\"" : value;

    private static string GetExecutableBaseDirectory()
    {
        try
        {
            var mainModulePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(mainModulePath) && File.Exists(mainModulePath))
                return Path.GetDirectoryName(mainModulePath) ?? AppContext.BaseDirectory;
        }
        catch
        {
        }

        return AppContext.BaseDirectory;
    }

    private static void AppendStartupLog(string message) => StartupDiagnosticLog.AppendLine(message);
}
