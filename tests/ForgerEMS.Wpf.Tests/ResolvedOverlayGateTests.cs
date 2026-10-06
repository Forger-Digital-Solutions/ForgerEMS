using System.Diagnostics;
using System.Linq;
using System.Text;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Executed (not source-inspection) tests for the resolved-overlay trust gate in
/// backend/Update-ForgerEMS.ps1. The harness extracts the real function bodies from the
/// script's AST, dot-sources them into an isolated PowerShell process, and evaluates
/// fixture overlays — no USB actions, no host mutation, no payload downloads.
/// </summary>
public class ResolvedOverlayGateTests
{
    private static string FindRepoFile(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Could not locate {string.Join("/", segments)} from {AppContext.BaseDirectory}.");
    }

    private static string ScriptPath => FindRepoFile("backend", "Update-ForgerEMS.ps1");

    private static string PolicyPath => FindRepoFile("manifests", "resource-policy.json");

    private const string ManifestHash =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static readonly string ValidOverlayEntry = """
        {
          "resourceId": "ventoy",
          "url": "https://github.com/ventoy/Ventoy/releases/download/v1.1.10/ventoy-1.1.10-windows.zip",
          "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
          "resolvedVersion": "1.1.10",
          "checkedAtUtc": "%CHECKED%",
          "expiresAtUtc": "%EXPIRES%"
        }
        """;

    private static string BuildHarness(string policyPath, string overlayDocJson, string[] overlayEntryJsons)
    {
        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference='Stop'");
        sb.AppendLine("function Write-Log { param($Message, $Level = 'INFO') }");
        sb.AppendLine($"$scriptPath = '{ScriptPath.Replace("'", "''")}'");
        sb.AppendLine("$tokens=$null; $errors=$null");
        sb.AppendLine("$ast = [System.Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$errors)");
        sb.AppendLine("$funcs = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true)");
        sb.AppendLine("foreach ($want in @('Get-ResourcePolicyDocument','Get-ResourcePolicyDescriptor','Test-ResolvedOverlayGate','Invoke-TrustedManagedDownload')) {");
        sb.AppendLine("  $f = $funcs | Where-Object { $_.Name -eq $want } | Select-Object -First 1");
        sb.AppendLine("  if (-not $f) { Write-Output \"MISSING:$want\"; exit 2 }");
        sb.AppendLine("  Invoke-Expression $f.Extent.Text");
        sb.AppendLine("}");
        // The policy document is resolved relative to the manifest dir; the real
        // resource-policy.json sits in manifests/ so point the harness there.
        sb.AppendLine($"$policyDir = '{Path.GetDirectoryName(policyPath)!.Replace("'", "''")}'");
        sb.AppendLine("$manifestPath = Join-Path $policyDir 'ForgerEMS.updates.json'");
        sb.AppendLine("$pd = Get-ResourcePolicyDescriptor -ManifestPath $manifestPath -ResourceId 'ventoy'");
        sb.AppendLine("if (-not $pd) { Write-Output 'NODESCRIPTOR'; exit 3 }");
        sb.AppendLine("Write-Output (\"DESCRIPTOR|repo=\" + $pd.Repository + \"|ttl=\" + $pd.CacheTtlMinutes + \"|hosts=\" + ($pd.AllowedHostsForDownload -join ','))");
        sb.AppendLine($"$overlayDoc = @'\n{overlayDocJson}\n'@ | ConvertFrom-Json");
        int i = 0;
        foreach (var entry in overlayEntryJsons)
        {
            sb.AppendLine($"$entry{i} = @'\n{entry}\n'@ | ConvertFrom-Json");
            sb.AppendLine($"$r{i} = Test-ResolvedOverlayGate -OverlayDoc $overlayDoc -OverlayEntry $entry{i} -ItemResourceId 'ventoy' -ManifestHash '{ManifestHash}' -PolicyDescriptor $pd");
            sb.AppendLine($"Write-Output (\"CASE{i}|eligible=\" + $r{i}.Eligible + \"|reason=\" + $r{i}.Reason)");
            i++;
        }
        return sb.ToString();
    }

    private static string[] RunHarness(string script)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(script);

        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        Assert.True(proc.WaitForExit(60000), "overlay gate harness timed out");
        Assert.True(proc.ExitCode == 0,
            $"harness exit {proc.ExitCode}\nstdout:\n{stdout}\nstderr:\n{stderr}");
        return stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string OverlayDoc(string manifestHash) => $$"""
        {
          "schemaVersion": 1,
          "manifestSha256": "{{manifestHash}}",
          "entries": []
        }
        """;

    private static string FreshEntry(
        string url = "https://github.com/ventoy/Ventoy/releases/download/v1.1.10/ventoy-1.1.10-windows.zip",
        string sha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
        int checkedMinutesAgo = 5,
        int expiresMinutesAhead = 30)
    {
        var now = DateTimeOffset.UtcNow;
        return ValidOverlayEntry
            .Replace("%CHECKED%", now.AddMinutes(-checkedMinutesAgo).ToString("O"))
            .Replace("%EXPIRES%", now.AddMinutes(expiresMinutesAhead).ToString("O"))
            .Replace("https://github.com/ventoy/Ventoy/releases/download/v1.1.10/ventoy-1.1.10-windows.zip", url)
            .Replace("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", sha);
    }

    [Fact]
    public void Descriptor_CarriesRepositoryPatternAndPolicyTtl()
    {
        var lines = RunHarness(BuildHarness(PolicyPath, OverlayDoc(ManifestHash), new[] { FreshEntry() }));
        var desc = Assert.Single(lines, l => l.StartsWith("DESCRIPTOR|"));
        Assert.Contains("repo=ventoy/Ventoy", desc);
        Assert.Contains("ttl=60", desc); // resource-policy.json cacheTtlMinutes — not hardcoded
        Assert.Contains("github.com", desc);
        Assert.Contains("release-assets.githubusercontent.com", desc);
        var valid = Assert.Single(lines, l => l.StartsWith("CASE0|"));
        Assert.StartsWith("CASE0|eligible=True", valid);
    }

    [Fact]
    public void Gate_Rejects_WrongManifestHash()
    {
        var doc = OverlayDoc("cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc");
        var lines = RunHarness(BuildHarness(PolicyPath, doc, new[] { FreshEntry() }));
        var l = Assert.Single(lines, l => l.StartsWith("CASE0|"));
        Assert.Contains("eligible=False", l);
        Assert.Contains("manifest SHA-256", l);
    }

    [Fact]
    public void Gate_Rejects_MalformedAndMissingHashAndExpiry()
    {
        var cases = new[]
        {
            FreshEntry(sha: "not-a-hash"),
            FreshEntry().Replace("\"checkedAtUtc\":", "\"checkedAtUtcX\":"),   // missing checkedAtUtc
            FreshEntry().Replace("\"expiresAtUtc\":", "\"expiresAtUtcX\":"),   // missing expiresAtUtc
            FreshEntry().Replace("\"sha256\":", "\"sha256X\":"),               // missing sha256
        };
        var lines = RunHarness(BuildHarness(PolicyPath, OverlayDoc(ManifestHash), cases));
        for (var i = 0; i < cases.Length; i++)
        {
            var l = Assert.Single(lines, x => x.StartsWith($"CASE{i}|"));
            Assert.Contains("eligible=False", l);
        }
    }

    [Fact]
    public void Gate_Rejects_ExpiredAndTtlExceeded()
    {
        var cases = new[]
        {
            FreshEntry(expiresMinutesAhead: -5),          // already expired
            FreshEntry(checkedMinutesAgo: 300, expiresMinutesAhead: 120), // expires > checked + 60min TTL
        };
        var lines = RunHarness(BuildHarness(PolicyPath, OverlayDoc(ManifestHash), cases));
        var l0 = Assert.Single(lines, x => x.StartsWith("CASE0|"));
        Assert.Contains("eligible=False", l0);
        Assert.Contains("expired", l0, StringComparison.OrdinalIgnoreCase);
        var l1 = Assert.Single(lines, x => x.StartsWith("CASE1|"));
        Assert.Contains("eligible=False", l1);
        Assert.Contains("TTL", l1);
    }

    [Fact]
    public void Gate_Rejects_WrongRepositoryAndHostAndAsset()
    {
        var cases = new[]
        {
            FreshEntry(url: "https://github.com/evil-org/Ventoy/releases/download/v1.1.10/ventoy-1.1.10-windows.zip"),
            FreshEntry(url: "https://evil-cdn.example.com/ventoy-1.1.10-windows.zip"),
            FreshEntry(url: "https://github.com/ventoy/Ventoy/releases/download/v1.1.10/ventoy-evil.exe"),
            FreshEntry(url: "http://github.com/ventoy/Ventoy/releases/download/v1.1.10/ventoy-1.1.10-windows.zip"),
            FreshEntry(url: "https://user:pass@github.com/ventoy/Ventoy/releases/download/v1.1.10/ventoy-1.1.10-windows.zip"),
        };
        var lines = RunHarness(BuildHarness(PolicyPath, OverlayDoc(ManifestHash), cases));
        for (var i = 0; i < cases.Length; i++)
        {
            var l = Assert.Single(lines, x => x.StartsWith($"CASE{i}|"));
            Assert.Contains("eligible=False", l);
        }
    }

    [Fact]
    public void TrustedDownload_RejectsOffPolicyHostAndInsecureScheme_BeforeAnyRequest()
    {
        // Host/scheme guards must fire before any network I/O; unreachable hosts are fine.
        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference='Continue'");
        sb.AppendLine("function Write-Log { param($Message, $Level = 'INFO') }");
        sb.AppendLine($"$scriptPath = '{ScriptPath.Replace("'", "''")}'");
        sb.AppendLine("$tokens=$null; $errors=$null");
        sb.AppendLine("$ast = [System.Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$errors)");
        sb.AppendLine("$f = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true) | Where-Object { $_.Name -eq 'Invoke-TrustedManagedDownload' } | Select-Object -First 1");
        sb.AppendLine("Invoke-Expression $f.Extent.Text");
        sb.AppendLine("$out = Join-Path $env:TEMP ('fems-trusted-' + [guid]::NewGuid().ToString('N') + '.bin')");
        sb.AppendLine("try {");
        sb.AppendLine("  try { Invoke-TrustedManagedDownload -Url 'https://evil.example.com/a.zip' -OutFile $out -AllowedHosts @('github.com') -TimeoutSec 3 } catch { Write-Output ('OFFHOST|' + $_.Exception.Message) }");
        sb.AppendLine("  try { Invoke-TrustedManagedDownload -Url 'http://github.com/a.zip' -OutFile $out -AllowedHosts @('github.com') -TimeoutSec 3 } catch { Write-Output ('SCHEME|' + $_.Exception.Message) }");
        sb.AppendLine("  try { Invoke-TrustedManagedDownload -Url 'https://github.com:8443/a.zip' -OutFile $out -AllowedHosts @('github.com') -TimeoutSec 3 } catch { Write-Output ('PORT|' + $_.Exception.Message) }");
        sb.AppendLine("  try { Invoke-TrustedManagedDownload -Url 'https://u:p@github.com/a.zip' -OutFile $out -AllowedHosts @('github.com') -TimeoutSec 3 } catch { Write-Output ('CRED|' + $_.Exception.Message) }");
        sb.AppendLine("} finally { if (Test-Path $out) { Remove-Item $out -Force } }");

        var lines = RunHarness(sb.ToString());
        Assert.Single(lines, l => l.StartsWith("OFFHOST|") && l.Contains("not authorized"));
        Assert.Single(lines, l => l.StartsWith("SCHEME|") && l.Contains("https"));
        Assert.Single(lines, l => l.StartsWith("PORT|") && l.Contains("443"));
        Assert.Single(lines, l => l.StartsWith("CRED|") && l.Contains("credentials"));
    }
}
