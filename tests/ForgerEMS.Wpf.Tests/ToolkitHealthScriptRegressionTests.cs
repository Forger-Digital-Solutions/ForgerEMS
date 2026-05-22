using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services.Intelligence;

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
    public void ToolkitHealthScript_ResolvesSha256UrlForVerificationWhenPinnedHashMissing()
    {
        var script = File.ReadAllText(FindRepoFile("backend", "ToolkitManager", "Get-ForgerEMSToolkitHealth.ps1"));

        Assert.Contains("Get-Sha256FromSourceUrl", script, StringComparison.Ordinal);
        Assert.Contains("$sha256Url = ([string]$Item.sha256Url).Trim()", script, StringComparison.Ordinal);
        Assert.Contains("Resolved {0} from checksum URL", script, StringComparison.Ordinal);
        Assert.Contains("checksumAlgorithm", script, StringComparison.Ordinal);
        Assert.Contains("offline checksum pending", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolkitHealthScript_ClonezillaManifestDestinationIsTrackedAsIsoPath()
    {
        var manifest = File.ReadAllText(FindRepoFile("manifests", "ForgerEMS.updates.json"));
        Assert.Contains("clonezilla-live-3.3.1-35-amd64.iso", manifest, StringComparison.Ordinal);
        Assert.Contains("ISO\\\\Tools\\\\clonezilla-live-3.3.1-35-amd64.iso", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolkitHealthScript_ActiveManagedIsoCoversMissingInfoShortcut()
    {
        var root = CreateTempRoot();
        try
        {
            var iso = Path.Combine(root, "ISO", "Linux", "systemrescue-13.00-amd64.iso");
            Directory.CreateDirectory(Path.GetDirectoryName(iso)!);
            File.WriteAllText(iso, "verified-systemrescue");
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(iso))).ToLowerInvariant();
            var checksumFile = Path.Combine(root, "systemrescue.sha256");
            File.WriteAllText(checksumFile, $"{hash}  systemrescue-13.00-amd64.iso");
            var manifestPath = Path.Combine(root, "manifest.json");
            File.WriteAllText(manifestPath, $$"""
            {"items":[
              {"name":"SystemRescue 13.00 (amd64)","type":"file","dest":"ISO\\Linux\\systemrescue-13.00-amd64.iso","url":"https://example.test/systemrescue.iso","sha256Url":"{{EscapeJson(checksumFile)}}","enabled":true,"notes":"Active managed autodownload: Linux recovery environment."},
              {"name":"SystemRescue Download Page","type":"page","dest":"ISO\\Linux\\DOWNLOAD - SystemRescue.url","url":"https://www.system-rescue.org/Download/","enabled":true,"notes":"Info shortcut: upstream page for the active managed autodownload SystemRescue ISO."}
            ]}
            """);

            var report = RunToolkitHealthScript(root, manifestPath);
            var summary = report.RootElement.GetProperty("summary");
            Assert.Equal(1, summary.GetProperty("installed").GetInt32());
            Assert.Equal(0, summary.GetProperty("manual").GetInt32());
            Assert.Equal(1, summary.GetProperty("coveredByManaged").GetInt32());
            Assert.Equal("READY", report.RootElement.GetProperty("healthVerdict").GetString());

            var covered = report.RootElement.GetProperty("items").EnumerateArray()
                .Single(i => i.GetProperty("tool").GetString() == "SystemRescue Download Page");
            Assert.Equal("COVERED_BY_MANAGED", covered.GetProperty("status").GetString());
            Assert.Contains("No action needed", covered.GetProperty("recommendation").GetString(), StringComparison.OrdinalIgnoreCase);

            var readiness = ToolkitReadinessScorer.Evaluate(
                [
                    new ToolkitHealthItemView { Status = "INSTALLED", Type = "managedAutoDownload", DownloadStatus = "Downloaded", ChecksumStatus = "Verified" },
                    new ToolkitHealthItemView { Status = "COVERED_BY_MANAGED", Type = "manualDownload", DownloadStatus = "Covered", ChecksumStatus = "Covered" }
                ],
                selectedTarget: null,
                ventoyStatusText: string.Empty,
                toolkitReportAvailable: true,
                toolkitLogAvailable: true,
                missingRequiredCount: 0,
                verificationFailedCount: 0,
                updatesAvailableCount: 0,
                verificationPendingCount: 0,
                omitLiveUsbVentoyContext: true);
            Assert.NotEqual(ToolkitReadinessLabel.UnknownLimitedData, readiness.Label);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ToolkitHealthScript_Sha256UrlUnavailableWithFilePresent_IsPendingNotMissing()
    {
        var root = CreateTempRoot();
        try
        {
            var iso = Path.Combine(root, "ISO", "Linux", "systemrescue-13.00-amd64.iso");
            Directory.CreateDirectory(Path.GetDirectoryName(iso)!);
            File.WriteAllText(iso, "present-but-offline");
            var manifestPath = Path.Combine(root, "manifest.json");
            File.WriteAllText(manifestPath, $$"""
            {"items":[
              {"name":"SystemRescue 13.00 (amd64)","type":"file","dest":"ISO\\Linux\\systemrescue-13.00-amd64.iso","url":"https://example.test/systemrescue.iso","sha256Url":"{{EscapeJson(Path.Combine(root, "missing.sha256"))}}","enabled":true}
            ]}
            """);

            var report = RunToolkitHealthScript(root, manifestPath);
            var item = report.RootElement.GetProperty("items").EnumerateArray().Single();
            Assert.Equal("VERIFICATION_PENDING", item.GetProperty("status").GetString());
            Assert.Contains("offline checksum pending", item.GetProperty("verification").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, report.RootElement.GetProperty("summary").GetProperty("missingRequired").GetInt32());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ToolkitHealthScript_VerifiesSha512ManagedFile()
    {
        var root = CreateTempRoot();
        try
        {
            var iso = Path.Combine(root, "ISO", "BSD", "NetBSD-10.1-amd64.iso");
            Directory.CreateDirectory(Path.GetDirectoryName(iso)!);
            File.WriteAllText(iso, "verified-netbsd");
            var hash = Convert.ToHexString(SHA512.HashData(File.ReadAllBytes(iso))).ToLowerInvariant();
            var checksumFile = Path.Combine(root, "SHA512");
            File.WriteAllText(checksumFile, $"SHA512 (NetBSD-10.1-amd64.iso) = {hash}");
            var manifestPath = Path.Combine(root, "manifest.json");
            File.WriteAllText(manifestPath, $$"""
            {"items":[
              {"name":"NetBSD 10.1 amd64 ISO","type":"file","dest":"ISO\\BSD\\NetBSD-10.1-amd64.iso","url":"https://cdn.netbsd.org/pub/NetBSD/images/10.1/NetBSD-10.1-amd64.iso","sha512Url":"{{EscapeJson(checksumFile)}}","enabled":true,"notes":"Active managed autodownload: NetBSD installer."}
            ]}
            """);

            var report = RunToolkitHealthScript(root, manifestPath);
            var item = report.RootElement.GetProperty("items").EnumerateArray().Single();
            Assert.Equal("INSTALLED", item.GetProperty("status").GetString());
            Assert.Contains("SHA512 verified", item.GetProperty("verification").GetString(), StringComparison.Ordinal);
            Assert.Equal("Match", item.GetProperty("checksumStatus").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static JsonDocument RunToolkitHealthScript(string root, string manifestPath)
    {
        var script = FindRepoFile("backend", "ToolkitManager", "Get-ForgerEMSToolkitHealth.ps1");
        var exe = OperatingSystem.IsWindows() ? "powershell" : "pwsh";
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(script);
        psi.ArgumentList.Add("-TargetRoot");
        psi.ArgumentList.Add(root);
        psi.ArgumentList.Add("-ManifestPath");
        psi.ArgumentList.Add(manifestPath);
        psi.Environment["FORGEREMS_TOOLKIT_HEALTH_REPORT_ROOT"] = Path.Combine(root, "_local-reports");

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("PowerShell did not start.");
        Assert.True(process.WaitForExit(30_000), "Toolkit health script timed out.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.ExitCode == 0, stdout + Environment.NewLine + stderr);
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "_local-reports", "toolkit-health-latest.json")));
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "forgerems-toolkit-health-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string EscapeJson(string path) => path.Replace(@"\", @"\\");

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
