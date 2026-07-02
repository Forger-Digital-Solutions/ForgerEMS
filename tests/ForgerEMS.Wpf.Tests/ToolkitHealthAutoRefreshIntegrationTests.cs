using System;
using System.IO;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

// Read-only-ness guard for the script the Toolkit Manager launch auto-refresh
// invokes. If a future contributor adds Invoke-WebRequest, Format-Volume, etc.
// the auto-refresh would no longer be safe for an unattended launch, so we
// fail the build instead of silently letting it through.
public sealed class ToolkitHealthAutoRefreshIntegrationTests
{
    private static readonly string[] BannedCmdlets =
    [
        "Invoke-WebRequest",
        "Invoke-RestMethod",
        "Start-BitsTransfer",
        "Format-Volume",
        "Clear-Disk",
        "Initialize-Disk",
        "Set-Partition",
        "Remove-Partition",
    ];

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

            throw new InvalidOperationException("Could not locate ForgerEMS.sln.");
        }
    }

    [Fact]
    public void ToolkitHealthScriptExists()
    {
        var path = Path.Combine(RepoRoot, "backend", "ToolkitManager", "Get-ForgerEMSToolkitHealth.ps1");
        Assert.True(File.Exists(path), $"Expected toolkit health script at {path}");
    }

    [Fact]
    public void ToolkitHealthScript_HasNoDownloadOrDestructiveCmdlets()
    {
        var path = Path.Combine(RepoRoot, "backend", "ToolkitManager", "Get-ForgerEMSToolkitHealth.ps1");
        var contents = File.ReadAllText(path);
        foreach (var banned in BannedCmdlets)
        {
            Assert.DoesNotContain(banned, contents, System.StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ToolkitHealthScript_TakesTargetRootAndManifestPathParameters()
    {
        // The auto-refresh wiring invokes the script with -TargetRoot + -ManifestPath.
        // If those parameter names ever drift, the auto refresh would silently fail.
        var path = Path.Combine(RepoRoot, "backend", "ToolkitManager", "Get-ForgerEMSToolkitHealth.ps1");
        var contents = File.ReadAllText(path);
        Assert.Contains("$TargetRoot", contents, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$ManifestPath", contents, System.StringComparison.OrdinalIgnoreCase);
    }
}
