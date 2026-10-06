using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Fail-closed invariants of the Update-ForgerEMS.ps1 resolved-overlay gate. Structural
/// asserts pin the check ordering; a real PSParser pass pins script syntax.
/// </summary>
public sealed class OverlayGateRegressionTests
{
    private static string ScriptText()
    {
        var repo = ResourceCatalogTests.RepoRoot();
        return File.ReadAllText(Path.Combine(repo, "backend", "Update-ForgerEMS.ps1"));
    }

    [Fact]
    public void Script_ParsesUnderRealPowerShellParser()
    {
        var repo = ResourceCatalogTests.RepoRoot();
        var script = Path.Combine(repo, "backend", "Update-ForgerEMS.ps1");
        var exe = OperatingSystem.IsWindows() ? "powershell" : "pwsh";
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(
            "$e=$null; [System.Management.Automation.Language.Parser]::ParseFile('" +
            script.Replace("'", "''") +
            "', [ref]$null, [ref]$e) | Out-Null; if ($e -and $e.Count -gt 0) { $e | ForEach-Object { Write-Error $_.Message }; exit 1 }");

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(30_000);
        Assert.True(process.ExitCode == 0,
            $"PowerShell parse errors for Update-ForgerEMS.ps1: {stderr} {stdout}");
    }

    [Fact]
    public void OverlayGate_RequiresSchemaVersion1()
    {
        var text = ScriptText();
        Assert.Contains("$OverlayDoc.SchemaVersion -ne 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayGate_RequiresManifestHashBinding()
    {
        var text = ScriptText();
        Assert.Contains("ManifestSha256", text, StringComparison.Ordinal);
        Assert.Contains("'^[0-9a-fA-F]{64}$'", text, StringComparison.Ordinal);
        Assert.Contains("does not match the current manifest", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayGate_RequiresExactResourceIdMatch()
    {
        var text = ScriptText();
        Assert.Contains("\"rid:\" + $itemResourceId", text, StringComparison.Ordinal);
        Assert.Contains("overlay entry resourceId mismatch", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayGate_RejectsMissingMalformedOrExpiredTimestamps()
    {
        var text = ScriptText();
        Assert.Contains("CheckedAtUtc/ExpiresAtUtc missing or malformed", text, StringComparison.Ordinal);
        Assert.Contains("overlay metadata is expired", text, StringComparison.Ordinal);
        Assert.Contains("CheckedAtUtc is in the future", text, StringComparison.Ordinal);
        Assert.Contains("bounded policy TTL", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayGate_UsesPolicyAllowedHosts_NotOverlayHosts()
    {
        var text = ScriptText();
        Assert.Contains("Get-ResourcePolicyDescriptor", text, StringComparison.Ordinal);
        Assert.Contains("AllowedHostsForDownload", text, StringComparison.Ordinal);
        Assert.Contains("is not in the resource policy allowed hosts", text, StringComparison.Ordinal);
        Assert.Contains("no trusted host list in resource policy", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayGate_RequiresHttpsAndSha256OnOverlayEntry()
    {
        var text = ScriptText();
        Assert.Contains("no HTTPS artifact URL", text, StringComparison.Ordinal);
        Assert.Contains("lacks a 64-hex expected SHA-256", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiresResolution_ItemsFailClosed_NoFallback()
    {
        var text = ScriptText();
        Assert.Contains("RequiresUserAction): managed item", text, StringComparison.Ordinal);
        Assert.Contains("requires a fresh verified overlay", text, StringComparison.Ordinal);
        // The skip path must 'continue' — never download from manifest or overlay defaults.
        var gateIndex = text.IndexOf("requires a fresh verified overlay", StringComparison.Ordinal);
        var afterGate = text.Substring(gateIndex, Math.Min(2000, text.Length - gateIndex));
        Assert.Contains("continue", afterGate, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke-WebRequest", afterGate.Substring(0,
            Math.Min(afterGate.IndexOf("continue", StringComparison.Ordinal) + 8, afterGate.Length)));
    }

    [Fact]
    public void ResourcePolicy_GitHubArtifactHosts_ScopedToGitHubProvider()
    {
        var text = ScriptText();
        // The three GitHub CDN hosts must only be appended for github-stable descriptors.
        Assert.Contains("$descriptor.provider) -eq \"github-stable\"", text, StringComparison.Ordinal);
        Assert.Contains("release-assets.githubusercontent.com", text, StringComparison.Ordinal);
        Assert.Contains("objects.githubusercontent.com", text, StringComparison.Ordinal);
    }
}
