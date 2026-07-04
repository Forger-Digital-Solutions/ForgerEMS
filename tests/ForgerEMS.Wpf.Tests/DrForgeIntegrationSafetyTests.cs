using System;
using System.IO;
using System.Linq;
using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

// Dr. Forge integration readiness safety net.
//
// Dr. Forge remains a separate local diagnostic companion. Its future driver
// support is dev-foundation / contract-first only, so ForgerEMS must never
// install/start/load a driver, ship driver artifacts in normal packages, offer
// driver-install or run-as-admin UI for Dr. Forge, or present driver-required
// sensors as available. These tests pin those boundaries at the source level so
// they fail fast if a future pass drifts.
public sealed class DrForgeIntegrationSafetyTests
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

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    private static readonly string[][] DriverVerbScanTargets =
    [
        ["src", "ForgerEMS.Wpf", "Services", "DrForgeCliBridge.cs"],
        ["src", "ForgerEMS.Wpf", "Services", "SupportBundleExporter.cs"],
        ["src", "ForgerEMS.Wpf", "Services", "UsbBuilderProfileCatalog.cs"],
        ["src", "ForgerEMS.Wpf", "ViewModels", "MainViewModel.cs"],
        ["src", "ForgerEMS.Wpf", "MainWindow.xaml"],
        ["src", "ForgerEMS.Wpf", "MainWindow.xaml.cs"],
        ["src", "ForgerEMS.Wpf", "Infrastructure", "InfoDocumentTexts.cs"],
        ["tools", "build-release.ps1"],
        ["tools", "Validate-ForgerEMSRelease.ps1"],
        ["installer", "ForgerEMS.iss"]
    ];

    // Windows driver install/start/load verbs that must never appear in the app,
    // its packaging scripts, or the installer. The negated forms in docs/comments
    // are allowed elsewhere; these files must not even reference the verbs.
    private static readonly string[] ForbiddenDriverVerbs =
    [
        "sc create",
        "sc.exe create",
        "sc start",
        "sc.exe start",
        "pnputil",
        "devcon",
        "NtLoadDriver",
        "ZwLoadDriver",
        "SeLoadDriverPrivilege"
    ];

    [Fact]
    public void AppPackagingAndInstallerSources_ContainNoDriverInstallOrLoadVerbs()
    {
        foreach (var target in DriverVerbScanTargets)
        {
            var text = Read(target);
            foreach (var verb in ForbiddenDriverVerbs)
            {
                Assert.False(
                    text.Contains(verb, StringComparison.OrdinalIgnoreCase),
                    $"'{verb}' must not appear in {string.Join('/', target)}.");
            }
        }
    }

    [Fact]
    public void ShellXaml_HasNoDriverInstallOrRunAsAdminButtons()
    {
        var xaml = Read("src", "ForgerEMS.Wpf", "MainWindow.xaml");

        foreach (var forbiddenLabel in new[]
                 {
                     "Install Sensor Driver",
                     "Install Driver",
                     "Enable Deep Sensor Driver",
                     "Unlock all hardware sensors",
                     "Run as Admin",
                     "Run as Administrator"
                 })
        {
            Assert.False(
                xaml.Contains(forbiddenLabel, StringComparison.OrdinalIgnoreCase),
                $"MainWindow.xaml must not offer a '{forbiddenLabel}' action.");
        }
    }

    [Fact]
    public void DrForgeCardCopy_KeepsDriverRequiredSensorsAsFutureUnavailable()
    {
        var xaml = Read("src", "ForgerEMS.Wpf", "MainWindow.xaml");

        // The Dr. Forge card must keep the user-mode-today / driver-future framing.
        Assert.Contains("local user-mode hardware intake/report tool", xaml, StringComparison.Ordinal);
        Assert.Contains("requires future safe providers or signed privileged components", xaml, StringComparison.Ordinal);
        Assert.Contains("does not claim full hardware-monitor parity", xaml, StringComparison.OrdinalIgnoreCase);

        // And must never present the future driver as shipped or active.
        Assert.DoesNotContain("driver installed", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("kernel driver active", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IntakeReportReader_TreatsDriverAbsenceAsNormalNotError()
    {
        const string json = """
            {
              "reportSchemaVersion": "forge-hardware-intake-report/1.0",
              "sourceSchemaVersion": "forge-sensor-core-snapshot/1.0",
              "platform": { "osFamily": "Windows", "architecture": "x64" },
              "safety": { "satisfiesSafetyInvariants": true, "kernelDriverLoaded": false },
              "summary": {
                "cpuLoadPercent": 12.5,
                "memoryUsedPercent": null,
                "storageCapacityBytes": null,
                "storageSmartHealth": null
              },
              "findings": [],
              "notes": ["User-mode scan; no driver action taken."],
              "ring0Gaps": [
                { "reading": "Fan RPM", "reason": "Requires a future driver-backed provider." }
              ]
            }
            """;

        var view = new DrForgeIntakeResultReader().ReadJson(json);

        // Driver absent + user-mode fallback is the expected healthy state.
        Assert.Contains("kernel driver loaded: no", view.SummaryText, StringComparison.Ordinal);
        Assert.Contains("safety invariants: yes", view.SummaryText, StringComparison.Ordinal);
        Assert.Contains("Fan RPM: Requires a future driver-backed provider.", view.SummaryText, StringComparison.Ordinal);

        // Null readings stay Unavailable, never zero or invented.
        Assert.Contains(view.KeyReadings, r => r.Name == "Memory used" && r.Value == "Unavailable");
        Assert.Contains(view.KeyReadings, r => r.Name == "Storage SMART health" && r.Value == "Unavailable");
        Assert.DoesNotContain("0 %", view.KeyReadings.Single(r => r.Name == "Memory used").Value, StringComparison.Ordinal);
    }

    [Fact]
    public void IntakeReportReader_ToleratesUnknownAndMissingFields()
    {
        // Conservative parsing: unknown/new fields are ignored and missing safety
        // data renders as user-mode with unavailable status, never as a crash.
        const string json = """
            {
              "reportSchemaVersion": "forge-hardware-intake-report/1.1",
              "futureUnknownBlock": { "nested": [1, 2, 3] },
              "safety": { "brandNewField": "whatever" }
            }
            """;

        var view = new DrForgeIntakeResultReader().ReadJson(json);

        Assert.Contains("User-mode", view.SummaryText, StringComparison.Ordinal);
        Assert.Contains("kernel driver loaded: Unavailable", view.SummaryText, StringComparison.Ordinal);
        Assert.All(view.KeyReadings, reading => Assert.Equal("Unavailable", reading.Value));
    }

    [Fact]
    public void BuildReleaseScript_GuardsAgainstDriverArtifactsInPackages()
    {
        var script = Read("tools", "build-release.ps1");

        Assert.Contains("Assert-NoDriverArtifacts", script, StringComparison.Ordinal);
        Assert.Contains("\".sys\"", script, StringComparison.Ordinal);
        Assert.Contains("\".inf\"", script, StringComparison.Ordinal);
        Assert.Contains("\".cat\"", script, StringComparison.Ordinal);
        Assert.Contains("portable ZIP package", script, StringComparison.Ordinal);
        Assert.Contains("staged app/backend", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseValidator_FailsOnDriverArtifactsInReleaseOutputAndZip()
    {
        var script = Read("tools", "Validate-ForgerEMSRelease.ps1");

        Assert.Contains("driver-artifacts", script, StringComparison.Ordinal);
        Assert.Contains("zip-driver-artifacts", script, StringComparison.Ordinal);
        Assert.Contains(@"\.(sys|inf|cat)$", script, StringComparison.Ordinal);
    }

    [Fact]
    public void RepoSources_ContainNoDriverArtifactFilesForPackaging()
    {
        // Nothing under src/, backend/, installer/, providers/, or manifests/ may
        // stage a *.sys / *.inf / *.cat file where packaging could pick it up.
        var driverExtensions = new[] { ".sys", ".inf", ".cat" };
        foreach (var relativeRoot in new[] { "src", "backend", "installer", "providers", "manifests" })
        {
            var root = Path.Combine(RepoRoot, relativeRoot);
            if (!Directory.Exists(root))
            {
                continue;
            }

            var offenders = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(f => driverExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Where(f => !f.Contains(Path.Combine("obj", ""), StringComparison.OrdinalIgnoreCase) &&
                            !f.Contains(Path.Combine("bin", ""), StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.True(offenders.Count == 0,
                $"Driver artifacts are not allowed under {relativeRoot}/: {string.Join("; ", offenders.Take(5))}");
        }
    }

    [Fact]
    public void UsbBuilderCatalog_DoesNotBundleDrForgeOrDriverPayloads()
    {
        var catalog = Read("src", "ForgerEMS.Wpf", "Services", "UsbBuilderProfileCatalog.cs");

        // Dr. Forge may be referenced as the separate companion app, but no USB
        // Builder pack may stage a Dr. Forge executable or driver artifacts.
        Assert.Contains("Dr. Forge", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("drforge.exe", catalog, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".sys", catalog, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("autorun", catalog, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadinessDoc_ExistsAndPinsTheSafetyBoundaries()
    {
        var doc = Read("docs", "integrations", "DR-FORGE-INTEGRATION-READINESS.md");

        Assert.Contains("dev-foundation / contract-first only", doc, StringComparison.Ordinal);
        Assert.Contains("not bundled", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("install, start, load, or register any Dr. Forge (or other) kernel driver", doc, StringComparison.Ordinal);
        Assert.Contains("ship `*.sys`, `*.inf`, or `*.cat` driver artifacts", doc, StringComparison.Ordinal);
        Assert.Contains("driver absent / user-mode fallback", doc, StringComparison.Ordinal);
        Assert.Contains("Unavailable", doc, StringComparison.Ordinal);
        Assert.Contains("upload Dr. Forge reports, telemetry, or support bundles anywhere automatically", doc, StringComparison.Ordinal);
        Assert.Contains("Assert-NoDriverArtifacts", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void SupportBundleReadme_KeepsDrForgeInclusionExplicitAndReviewable()
    {
        var exporter = Read("src", "ForgerEMS.Wpf", "Services", "SupportBundleExporter.cs");

        Assert.Contains("only when explicitly included from the app", exporter, StringComparison.Ordinal);
        Assert.Contains("Review before sharing", exporter, StringComparison.Ordinal);
    }
}
