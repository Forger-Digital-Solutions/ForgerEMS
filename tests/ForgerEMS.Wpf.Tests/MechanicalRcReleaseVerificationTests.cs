using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class MechanicalRcReleaseVerificationTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "ForgerEMS.sln")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException("Could not locate ForgerEMS.sln from test base directory.");
        }
    }

    [Fact]
    public void BuildReleaseScript_StartHereLaunchesBundledInstaller()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, "tools", "build-release.ps1"));
        Assert.Contains(@"start """" ""%~dp0ForgerEMS Installer.exe""", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildReleaseScript_ConvertToWindowsVersion_DocumentsPrereleaseStrip()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, "tools", "build-release.ps1"));
        Assert.Contains("Strip semver prerelease", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MainWindow_Xaml_UserVisibleContent_DoesNotUseCopilotAsLabel()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, "src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        Assert.DoesNotMatch(
            new Regex(@"Content=""[^""]*\bCopilot\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            text);
    }

    [Fact]
    public void BuildReleaseScript_DownloadBeta_emphasizes_zip_not_exe()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, "tools", "build-release.ps1"));
        Assert.Contains("DOWNLOAD THE ZIP", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NOT THE EXE", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ForgerEMS-Beta-v", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildReleaseScript_VerifyTxt_warns_against_partial_downloads_and_lists_installer_hash_command()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, "tools", "build-release.ps1"));
        Assert.Contains(".crdownload", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Get-FileHash", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ForgerEMS Installer.exe", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendHashHelper_NormalAndFallbackPathsMatchKnownSha256()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "fe-hash-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var payloadPath = Path.Combine(tempRoot, "payload.bin");
            File.WriteAllBytes(payloadPath, [0x61, 0x62, 0x63]);

            var normal = RunPowerShell(
                $". '{PsQuote(Path.Combine(RepoRoot, "backend", "ForgerEMS.Runtime.ps1"))}'; " +
                $"$h = Get-ForgerSha256 -LiteralPath '{PsQuote(payloadPath)}'; " +
                "Write-Output ($h + '|' + (Get-ForgerLastHashProvider))");
            var fallback = RunPowerShell(
                $". '{PsQuote(Path.Combine(RepoRoot, "backend", "ForgerEMS.Runtime.ps1"))}'; " +
                $"$h = Get-ForgerSha256 -LiteralPath '{PsQuote(payloadPath)}' -ForceDotNetFallback; " +
                "Write-Output ($h + '|' + (Get-ForgerLastHashProvider))");

            Assert.StartsWith("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad|", normal, StringComparison.Ordinal);
            Assert.Matches(@"^ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad\|(Get-FileHash|DotNetFallback)$", normal.Trim());
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad|DotNetFallback", fallback.Trim());
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void BackendHashHelper_MissingFileFailsClearly()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), "fe-missing-" + Guid.NewGuid().ToString("N") + ".bin");

        var result = RunPowerShellRaw(
            $". '{PsQuote(Path.Combine(RepoRoot, "backend", "ForgerEMS.Runtime.ps1"))}'; " +
            $"Get-ForgerSha256 -LiteralPath '{PsQuote(missingPath)}'",
            expectSuccess: false);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Cannot calculate SHA256 for missing file", result.Error + result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BackendHashHelper_DotNetFallbackStreamsLargeFile()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "fe-hash-large-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var payloadPath = Path.Combine(tempRoot, "large.bin");
            var bytes = new byte[1024 * 1024];
            for (var i = 0; i < bytes.Length; i++)
            {
                bytes[i] = (byte)(i % 251);
            }

            File.WriteAllBytes(payloadPath, bytes);

            var output = RunPowerShell(
                $". '{PsQuote(Path.Combine(RepoRoot, "backend", "ForgerEMS.Runtime.ps1"))}'; " +
                $"$h = Get-ForgerSha256 -LiteralPath '{PsQuote(payloadPath)}' -ForceDotNetFallback; " +
                "Write-Output ($h + '|' + (Get-ForgerLastHashProvider))");

            Assert.Matches("^[a-f0-9]{64}\\|DotNetFallback$", output.Trim());
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void BackendVerificationScripts_UseCentralHashHelperForExecutableHashing()
    {
        var verify = File.ReadAllText(Path.Combine(RepoRoot, "backend", "Verify-VentoyCore.ps1"));
        var update = File.ReadAllText(Path.Combine(RepoRoot, "backend", "Update-ForgerEMS.ps1"));
        var health = File.ReadAllText(Path.Combine(RepoRoot, "backend", "ToolkitManager", "Get-ForgerEMSToolkitHealth.ps1"));
        var buildBackend = File.ReadAllText(Path.Combine(RepoRoot, "tools", "build-backend-release.ps1"));

        Assert.DoesNotContain("(Get-FileHash", verify, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("(Get-FileHash", update, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("(Get-FileHash", health, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("(Get-FileHash", buildBackend, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Get-ForgerSha256", verify, StringComparison.Ordinal);
        Assert.Contains("Get-ForgerSha256", update, StringComparison.Ordinal);
        Assert.Contains("Get-ForgerSha256", health, StringComparison.Ordinal);
        Assert.Contains("Get-ForgerSha256", buildBackend, StringComparison.Ordinal);
    }

    [Fact]
    public void Gitignore_Ignores_release_outputs()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, ".gitignore"));
        Assert.Contains("release/", text, StringComparison.Ordinal);
    }

    private static string RunPowerShell(string command) =>
        RunPowerShellRaw(command, expectSuccess: true).Output.Trim();

    private static (int ExitCode, string Output, string Error) RunPowerShellRaw(string command, bool expectSuccess)
    {
        var exe = ResolvePowerShellExe();
        var startInfo = new ProcessStartInfo
        {
            FileName = exe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("-NoProfile");
        if (Path.GetFileName(exe).Equals("powershell.exe", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
        }

        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(30000);

        if (expectSuccess && process.ExitCode != 0)
        {
            throw new InvalidOperationException($"PowerShell failed with exit {process.ExitCode}: {error}{output}");
        }

        return (process.ExitCode, output, error);
    }

    private static string ResolvePowerShellExe()
    {
        var psHome = Environment.GetEnvironmentVariable("PSHOME");
        if (!string.IsNullOrWhiteSpace(psHome))
        {
            var candidate = Path.Combine(psHome, "powershell.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "powershell.exe";
    }

    private static string PsQuote(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
