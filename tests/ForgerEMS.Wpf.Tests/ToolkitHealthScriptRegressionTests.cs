using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class ToolkitHealthScriptRegressionTests
{
    [Fact]
    public void ToolkitHealthScript_UsesNormalizedToolkitPathResolverAndTraversalGuard()
    {
        var script = File.ReadAllText(FindRepoFile("backend", "ToolkitManager", "Get-ForgerEMSToolkitHealth.ps1"));

        Assert.Contains("Resolve-ToolkitItemPath", script, StringComparison.Ordinal);
        Assert.Contains("Path traversal is not allowed in toolkit destination", script, StringComparison.Ordinal);
        Assert.Contains("Toolkit destination escaped target root", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolkitHealthScript_ExistingFilesAreNotClassifiedAsMissingByVerificationState()
    {
        var script = File.ReadAllText(FindRepoFile("backend", "ToolkitManager", "Get-ForgerEMSToolkitHealth.ps1"));

        Assert.Contains("$status = \"HASH_FAILED\"", script, StringComparison.Ordinal);
        Assert.Contains("$status = \"VERIFICATION_PENDING\"", script, StringComparison.Ordinal);
        Assert.Contains("$status = \"MISSING_REQUIRED\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolkitHealthScript_ClonezillaManifestDestinationIsTrackedAsIsoPath()
    {
        var manifest = File.ReadAllText(FindRepoFile("manifests", "ForgerEMS.updates.json"));
        Assert.Contains("clonezilla-live-3.3.1-35-amd64.iso", manifest, StringComparison.Ordinal);
        Assert.Contains("ISO\\\\Tools\\\\clonezilla-live-3.3.1-35-amd64.iso", manifest, StringComparison.Ordinal);
    }

    private static string FindRepoFile(params string[] segments)
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate repo file.", Path.Combine(segments));
    }
}
