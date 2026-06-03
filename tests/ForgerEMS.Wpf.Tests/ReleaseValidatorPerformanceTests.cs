using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class ReleaseValidatorPerformanceTests
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
    public void UpdateValidator_WhatIfRootResolution_DoesNotSpinAtDriveRoot()
    {
        var scratchRoot = Path.Combine(RepoRoot, ".verify", "tests", "validator-root-resolution");
        Directory.CreateDirectory(scratchRoot);

        var progressLog = Path.Combine(scratchRoot, "progress.log");
        if (File.Exists(progressLog))
        {
            File.Delete(progressLog);
        }

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments =
                "-NoProfile -ExecutionPolicy Bypass -File "
                + Quote(Path.Combine(RepoRoot, "backend", "Update-ForgerEMS.ps1"))
                + " -UsbRoot "
                + Quote(scratchRoot)
                + " -ManifestName "
                + Quote(Path.Combine(RepoRoot, "manifests", "ForgerEMS.updates.json"))
                + " -WhatIf",
            WorkingDirectory = RepoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        process.StartInfo.Environment["FORGEREMS_UPDATE_PROGRESS_LOG"] = progressLog;

        var output = new StringBuilder();
        var error = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null && output.Length < 16_384)
            {
                output.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null && error.Length < 16_384)
            {
                error.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var exited = process.WaitForExit(milliseconds: 15_000);
        if (!exited)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
        }

        Assert.True(
            exited,
            "Update-ForgerEMS.ps1 did not exit within 15 seconds. "
            + $"stdout={output} stderr={error} progress={ReadIfExists(progressLog)}");
        Assert.Contains("Find-ReleaseBundleRoot checking:", ReadIfExists(progressLog), StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseBundleRootSearch_PreservesAbsoluteDriveRoot()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot, "backend", "Update-ForgerEMS.ps1"));
        var functionBody = ExtractFunction(script, "Find-ReleaseBundleRoot");

        Assert.Contains("[IO.Path]::GetFullPath((Resolve-Path -LiteralPath $Path).Path)", functionBody, StringComparison.Ordinal);
        Assert.DoesNotContain(".Path.TrimEnd('\\\\')", functionBody, StringComparison.Ordinal);
        Assert.DoesNotContain(".FullName.TrimEnd('\\\\')", functionBody, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaceholderPlanner_UsesDirectoryIndexInsteadOfNestedManifestScan()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot, "backend", "Update-ForgerEMS.ps1"));
        var functionBody = ExtractFunction(script, "Get-ActiveManagedPlaceholderPlan");

        Assert.Contains("$managedByDirectory", functionBody, StringComparison.Ordinal);
        Assert.Contains("New-ManagedPlaceholderMatchInfo", functionBody, StringComparison.Ordinal);
        Assert.DoesNotContain("$enabledManagedFileItems | Where-Object", functionBody, StringComparison.Ordinal);
    }

    private static string ExtractFunction(string script, string functionName)
    {
        var marker = "function " + functionName;
        var start = script.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find {functionName}.");

        var nextFunction = script.IndexOf("\nfunction ", start + marker.Length, StringComparison.Ordinal);
        return nextFunction >= 0 ? script[start..nextFunction] : script[start..];
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private static string ReadIfExists(string path) => File.Exists(path) ? File.ReadAllText(path) : string.Empty;
}
