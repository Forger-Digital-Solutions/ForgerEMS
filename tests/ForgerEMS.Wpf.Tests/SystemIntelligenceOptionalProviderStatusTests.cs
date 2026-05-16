using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class SystemIntelligenceOptionalProviderStatusTests
{
    [Fact]
    public void BackendScan_UsesClassifiedOptionalProviderStatusesAndSafeFirmwareHandling()
    {
        var script = File.ReadAllText(FindRepoFile("backend", "SystemIntelligence", "Invoke-ForgerEMSSystemScan.ps1"));

        Assert.Contains("Resolve-OptionalProviderStatus", script, StringComparison.Ordinal);
        Assert.Contains("PermissionRequired", script, StringComparison.Ordinal);
        Assert.Contains("NotExposed", script, StringComparison.Ordinal);
        Assert.Contains("ProviderUnavailable", script, StringComparison.Ordinal);
        Assert.Contains("optionalProviderStatus", script, StringComparison.Ordinal);
        Assert.Contains("Test-Path -LiteralPath $controlPath", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Optional provider failed:", script, StringComparison.Ordinal);
        Assert.Contains("$report[\"optionalProviderStatus\"] = if ($null -eq $script:OptionalProviderDiagnostics) { @() } else { [object[]]$script:OptionalProviderDiagnostics.ToArray() }", script, StringComparison.Ordinal);
        Assert.DoesNotContain("cpu = Get-ProcessorName -Processor $processor", script, StringComparison.Ordinal);
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
