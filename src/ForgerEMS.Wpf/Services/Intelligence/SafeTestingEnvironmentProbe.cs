using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public sealed class SafeTestingEnvironmentStatus
{
    public string WindowsSandboxBinary { get; init; } = "Unknown";

    public string WslInstalled { get; init; } = "Unknown";

    public string DefaultWslDistroOrStatus { get; init; } = "Not queried";

    public string VirtualizationHint { get; init; } = "Unknown";

    public string HyperVOptionalFeaturesHint { get; init; } =
        "Check Settings → System → Optional features (or run optionalfeatures.exe) for Hyper-V and Virtual Machine Platform.";

    public string FormatSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Windows Sandbox (binary present): " + WindowsSandboxBinary);
        sb.AppendLine("WSL installed: " + WslInstalled);
        sb.AppendLine("Default WSL / status: " + DefaultWslDistroOrStatus);
        sb.AppendLine("Virtualization: " + VirtualizationHint);
        sb.AppendLine("Hyper-V / VM Platform: " + HyperVOptionalFeaturesHint);
        return sb.ToString().TrimEnd();
    }

    public string BuildCopySafeSummary(string? unifiedDiagnosticsHeadline)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ForgerEMS — safe testing summary (no secrets)");
        if (!string.IsNullOrWhiteSpace(unifiedDiagnosticsHeadline))
        {
            sb.AppendLine(CopilotRedactor.Redact(unifiedDiagnosticsHeadline.Trim(), enabled: true));
        }

        sb.AppendLine();
        sb.Append(FormatSummary());
        return CopilotRedactor.Redact(sb.ToString(), enabled: true).TrimEnd();
    }
}

public static class SafeTestingEnvironmentProbe
{
    public static SafeTestingEnvironmentStatus ProbeQuick()
    {
        try
        {
            var sandboxPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32",
                "WindowsSandbox.exe");
            var sandbox = File.Exists(sandboxPath) ? "Yes" : "No";
            var wsl = File.Exists(WslCommandRunner.WslExecutablePath) ? "Yes" : "No";
            return new SafeTestingEnvironmentStatus
            {
                WindowsSandboxBinary = sandbox,
                WslInstalled = wsl,
                DefaultWslDistroOrStatus = "Not queried — use Refresh in Diagnostics",
                VirtualizationHint =
                    "Open Task Manager → Performance → CPU and look for Virtualization: On/Off (BIOS: VT-x/AMD-V).",
                HyperVOptionalFeaturesHint =
                    "Use Settings → Apps → Optional features (or optionalfeatures.exe) to review Hyper-V / Virtual Machine Platform."
            };
        }
        catch
        {
            return new SafeTestingEnvironmentStatus
            {
                WindowsSandboxBinary = "Unknown",
                WslInstalled = "Unknown",
                DefaultWslDistroOrStatus = "Unknown",
                VirtualizationHint = "Unknown",
                HyperVOptionalFeaturesHint = "Unknown"
            };
        }
    }

    public static async Task<SafeTestingEnvironmentStatus> ProbeWithWslStatusAsync(
        IWslCommandExecutor wslExecutor,
        TimeSpan wslTimeout,
        CancellationToken cancellationToken)
    {
        var quick = ProbeQuick();
        if (!wslExecutor.IsWslInstalled())
        {
            return Clone(quick, "Not configured (wsl.exe missing)");
        }

        try
        {
            var (code, combined) = await wslExecutor.RunHostWslArgumentsAsync(
                ["--status"],
                wslTimeout,
                cancellationToken,
                lineProgress: null).ConfigureAwait(false);

            var distro = ExtractDefaultDistro(combined);
            var line = string.IsNullOrWhiteSpace(distro)
                ? (code == 0 ? "OK (no default name parsed)" : $"Warning/Unknown (exit {code})")
                : distro;

            if (line.Length > 200)
            {
                line = line[..200] + "…";
            }

            return Clone(quick, line);
        }
        catch (OperationCanceledException)
        {
            return Clone(quick, "Warning: WSL status query timed out or was cancelled.");
        }
        catch (Exception ex)
        {
            return Clone(quick, "Unknown: " + ex.Message);
        }
    }

    private static SafeTestingEnvironmentStatus Clone(SafeTestingEnvironmentStatus source, string distroLine) =>
        new()
        {
            WindowsSandboxBinary = source.WindowsSandboxBinary,
            WslInstalled = source.WslInstalled,
            DefaultWslDistroOrStatus = distroLine,
            VirtualizationHint = source.VirtualizationHint,
            HyperVOptionalFeaturesHint = source.HyperVOptionalFeaturesHint
        };

    private static string ExtractDefaultDistro(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        foreach (var line in text.Split('\n'))
        {
            var t = line.Trim();
            if (t.Contains("Default Distribution", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Default Version", StringComparison.OrdinalIgnoreCase))
            {
                var m = Regex.Match(t, @":\s*(.+)$");
                if (m.Success)
                {
                    return m.Groups[1].Value.Trim();
                }
            }
        }

        return string.Empty;
    }
}
