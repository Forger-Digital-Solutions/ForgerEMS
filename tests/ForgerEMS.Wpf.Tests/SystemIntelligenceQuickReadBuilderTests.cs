using System.Text.Json;
using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class SystemIntelligenceQuickReadBuilderTests
{
    [Fact]
    public void QuickReadIncludesRequiredOperatorLines()
    {
        var summary = BuildQuickRead(PrecisionReportJson());

        Assert.Contains("Machine:", summary);
        Assert.Contains("Health:", summary);
        Assert.Contains("Scan Confidence:", summary);
        Assert.Contains("Best Use:", summary);
        Assert.Contains("Flip Value:", summary);
        Assert.Contains("Key Strengths:", summary);
        Assert.Contains("Watch-outs:", summary);
        Assert.Contains("Workflow suggestion:", summary);
        Assert.Contains("Next Action:", summary);
    }

    [Fact]
    public void PrecisionQuickReadNamesMachineClassAndWorkstationFit()
    {
        var summary = BuildQuickRead(PrecisionReportJson());

        Assert.Contains("Dell Precision 5540", summary);
        Assert.Contains("Mobile Workstation", summary);
        Assert.Contains("Developer / Creator Workstation + Light Gaming", summary);
    }

    [Fact]
    public void UnknownSecurityAndBatteryExposureAreNotReportedAsFailures()
    {
        var summary = BuildQuickRead(PrecisionReportJson());

        Assert.Contains("battery wear not exposed", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TPM/Secure Boot need verification", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("battery failed", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TPM failed", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secure Boot failed", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fan failed", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HealthyEthernetSuppressesSeriousVirtualAdapterWarning()
    {
        var summary = BuildQuickRead(PrecisionReportJson(includeVirtualAdapterNoise: true));

        Assert.DoesNotContain("VirtualBox", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("host-only", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("APIPA", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QuickReadStaysWithinLineAndLengthLimit()
    {
        var summary = BuildQuickRead(PrecisionReportJson());
        var lines = summary.Split(Environment.NewLine);

        Assert.InRange(lines.Length, 8, 11);
        Assert.All(lines, line => Assert.True(line.Length <= 260, $"Line too long: {line}"));
        Assert.True(summary.Length <= 1800);
    }

    [Fact]
    public void FlipValueBasisIsIncluded()
    {
        var summary = BuildQuickRead(PrecisionReportJson());

        Assert.Contains("Flip Value: $340-$490", summary);
        Assert.Contains("offline/local heuristic", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("live comps not configured", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QuickRead_IgnoresStaleNetworkPulseReports()
    {
        // Network Pulse was retired in v1.2.3-preview.1. Machines upgrading from an
        // older preview may still have network-pulse-latest.json on disk; the quick
        // read must ignore it instead of surfacing retired-feature lines.
        var dir = Path.Combine(Path.GetTempPath(), "si-np-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(
                Path.Combine(dir, "network-pulse-latest.json"),
                """{"summaryLine":"Network Pulse: Wi-Fi · Fair · ping 40 ms · measured 120↓ / 8↑"}""");

            using var doc = JsonDocument.Parse(PrecisionReportJson());
            var summary = SystemIntelligenceQuickReadBuilder.Build(doc.RootElement, dir);
            Assert.DoesNotContain("Network Pulse", summary, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ForgerEMS System Intelligence — Quick Read", summary, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static string BuildQuickRead(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return SystemIntelligenceQuickReadBuilder.Build(doc.RootElement);
    }

    private static string PrecisionReportJson(bool includeVirtualAdapterNoise = false)
    {
        var issue = includeVirtualAdapterNoise
            ? @"""VirtualBox Host-Only adapter has APIPA/no gateway but active Ethernet internet is working"","
            : string.Empty;
        return $$"""
        {
          "overallStatus": "READY",
          "diskStatus": "READY",
          "batteryStatus": "UNKNOWN",
          "summary": {
            "manufacturer": "Dell",
            "model": "Precision 5540",
            "os": "Windows 11 Pro",
            "osBuild": "22631",
            "cpu": "Intel Core i7-9850H",
            "cpuCores": 6,
            "cpuLogicalProcessors": 12,
            "ramTotal": "32 GB",
            "ramSpeed": "2667 MT/s",
            "tpmReady": null,
            "secureBoot": null,
            "tpmInfo": { "status": "Unknown", "source": "Get-Tpm", "reason": "not exposed" },
            "secureBootInfo": { "status": "Unknown", "source": "Confirm-SecureBootUEFI", "reason": "not exposed" },
            "gpus": [
              { "name": "Intel UHD 630", "type": "Integrated", "driverVersion": "1" },
              { "name": "NVIDIA Quadro T2000", "type": "Dedicated", "driverVersion": "2" }
            ]
          },
          "machineClass": {
            "primaryClass": "Mobile Workstation",
            "confidence": "High",
            "secondaryClasses": ["Business Laptop"],
            "technicianNote": "Classified as a mobile workstation because workstation model/GPU/RAM signals dominate."
          },
          "deviceFit": {
            "primaryFit": "Developer / Creator Workstation + Light Gaming",
            "machineClass": "Mobile Workstation",
            "confidence": "High",
            "strongFits": ["Software development", "Technician / repair / diagnostics", "CAD / workstation tasks"],
            "weakFits": ["Modern AAA gaming at high settings", "Long battery sessions until battery wear/runtime is verified"],
            "upgradeFirstAdvice": ["Run battery report/vendor diagnostics before advertising runtime."],
            "listingPositioning": "Market as a mobile workstation/dev laptop, not primarily as a gaming laptop."
          },
          "flipValue": {
            "estimateType": "local estimate only",
            "providerStatus": "LocalHeuristicProvider active; eBay active listing provider not configured; sold comps/manual providers unavailable until configured",
            "estimatedResaleRange": "$340 - $490",
            "recommendedListPrice": "$490",
            "quickSalePrice": "$300",
            "partsRepairPrice": "$150",
            "confidenceScore": 0.68,
            "valueDrivers": [],
            "valueReducers": [],
            "suggestedUpgradeRecommendations": []
          },
          "disks": [
            { "name": "Samsung 990 Pro", "mediaType": "NVMe SSD", "size": "1 TB", "health": "Healthy", "status": "READY" }
          ],
          "batteries": [
            { "name": "Internal Battery", "estimatedChargeRemaining": 82, "status": "UNKNOWN" }
          ],
          "network": {
            "status": "READY",
            "internetCheck": true,
            "physicalAdapters": [{ "name": "Intel Ethernet" }],
            "virtualAdapters": [{ "name": "VirtualBox Host-Only" }],
            "adapters": [
              { "name": "Intel Ethernet", "adapterRole": "ActivePhysicalInternet", "gatewayPresent": true, "isDefaultRoute": true },
              { "name": "VirtualBox Host-Only", "adapterRole": "VirtualAdapter", "apipaDetected": true, "gatewayPresent": false, "isVirtual": true }
            ]
          },
          "obviousProblems": [],
          "recommendations": [
            {{issue}}
            "Verify TPM/Secure Boot in BIOS; Windows did not expose enough data to confirm."
          ]
        }
        """;
    }
}
