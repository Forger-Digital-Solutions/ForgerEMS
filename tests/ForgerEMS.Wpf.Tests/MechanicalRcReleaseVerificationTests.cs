using System.IO;
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
    public void Gitignore_Ignores_release_outputs()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, ".gitignore"));
        Assert.Contains("release/", text, StringComparison.Ordinal);
    }
}
