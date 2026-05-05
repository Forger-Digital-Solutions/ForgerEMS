using System.Reflection;
using System.Text.Json;
using VentoyToolkitSetup.Wpf.ViewModels;

namespace ForgerEMS.Wpf.Tests;

public sealed class SystemIntelligenceCardSummaryTests
{
    [Fact]
    public void FlipValue_SkipsGenericStorageReducerAndUses32GbDriver()
    {
        var text = InvokeSummary("BuildFlipValueSummary", JsonDocument.Parse(
            """
            {
              "summary": { "ramTotal": "32 GB" },
              "disks": [{ "name":"Samsung 990 Pro", "interfaceType":"NVMe", "mediaType":"NVMe SSD", "status":"READY" }],
              "flipValue": {
                "estimatedResaleRange":"$340 - $490",
                "confidenceScore":0.68,
                "estimateType":"local estimate only",
                "providerStatus":"offline",
                "valueDrivers":["Clean condition"],
                "valueReducers":["spinning or unknown storage lowers buyer confidence"]
              }
            }
            """).RootElement);

        Assert.Contains("32 GB RAM supports premium workstation/dev resale positioning", text);
        Assert.DoesNotContain("spinning or unknown storage lowers buyer confidence", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeviceFit_WatchOutsDoNotFallBackToNoneObviousWhenEvidenceExists()
    {
        var text = InvokeSummary("BuildDeviceFitSummary", JsonDocument.Parse(
            """
            {
              "summary": { "tpmInfo":{"status":"UNKNOWN"}, "secureBootInfo":{"status":"UNKNOWN"} },
              "batteries": [{ "wearDisplay":"42.2%" }],
              "deviceFit": {
                "primaryFit":"Developer / Creator Workstation + Light Gaming",
                "machineClass":"Mobile Workstation",
                "confidence":"High",
                "strongFits":["Software development"],
                "weakFits":["Modern AAA gaming at high settings"],
                "exampleWorkloads":["Visual Studio + Docker", "1080p eSports"]
              }
            }
            """).RootElement);

        Assert.Contains("Watch-outs:", text);
        Assert.Contains("Battery wear 42.2%", text);
        Assert.Contains("TPM/Secure Boot verification still needed", text);
        Assert.Contains("Heavy gaming/thermals remain confidence-limited unless benchmarked", text);
        Assert.DoesNotContain("none obvious", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HardwareXray_HumanizesEnumReasons()
    {
        var text = InvokeSummary("BuildHardwareXraySummary", JsonDocument.Parse(
            """
            {
              "machineClass": { "primaryClass":"Mobile Workstation", "confidence":"High", "secondaryClasses":[] },
              "sensorMatrix": {
                "coverageSummary":"Coverage: partial",
                "liveSensorsSummary":"Live sensors available for inventory only",
                "statusGuide":"guide",
                "groups":[
                  {
                    "category":"Cooling",
                    "readings":[
                      { "name":"Fan RPM", "category":"Cooling", "isUnavailable":true, "unavailableReason":"RequiresExternalProvider" },
                      { "name":"Package Power", "category":"CPU", "isUnavailable":true, "unavailableReason":"RequiresVendorDriver" },
                      { "name":"Secure Boot", "category":"Security", "isUnavailable":true, "unavailableReason":"NotExposedByFirmware" }
                    ]
                  }
                ]
              }
            }
            """).RootElement);

        Assert.Contains("Requires deep sensor provider", text);
        Assert.Contains("Requires vendor driver/support", text);
        Assert.Contains("Not exposed by firmware", text);
    }

    [Fact]
    public void StorageAndNetworkAndSecurityFormatting_AreHumanFriendly()
    {
        var root = JsonDocument.Parse(
            """
            {
              "diskStatus":"READY",
              "disks":[
                { "name":"Disk0","interfaceType":"RAID","mediaType":"SSD","size":"1 TB","healthDisplay":"Healthy","temperatureDisplay":"Temp: Not exposed","wearDisplay":"Wear: Not exposed","status":"READY" }
              ],
              "network":{
                "status":"READY","internetCheck":true,"defaultRouteSummary":"ok",
                "adapters":[{ "name":"Ethernet","adapterRole":"ActivePhysicalInternet","ipDisplay":"ok","gatewayDisplay":"ok","dnsDisplay":"ok","wifiDisplay":"Not a Wi-Fi adapter" }]
              },
              "summary":{
                "tpmInfo":{"status":"UNKNOWN","friendlyDisplayText":"Unknown"},
                "secureBootInfo":{"status":"UNKNOWN","friendlyDisplayText":"Unknown"}
              },
              "security": { "status":"UNKNOWN", "antivirusEnabled":true, "realTimeProtectionEnabled":true, "firewallEnabled":true, "avProducts":[], "bitLockerSummary":"Unknown" }
            }
            """).RootElement;

        var storage = InvokeSummary("BuildDiskHealthSummary", root);
        var network = InvokeSummary("BuildNetworkSummary", root);
        var security = InvokeSummary("BuildSecuritySummary", root);

        Assert.DoesNotContain("temp Temp", storage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NVMe/SSD via RAID/RST controller", storage);
        Assert.DoesNotContain("Wi-Fi Not a Wi-Fi adapter", network, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Active physical internet adapter", network);
        Assert.Contains("TPM: Not reported by Windows scan. Verify BIOS/UEFI TPM/PTT setting.", security);
        Assert.Contains("Secure Boot: Unknown — requires admin or unavailable.", security);
    }

    private static string InvokeSummary(string methodName, JsonElement root)
    {
        var method = typeof(MainViewModel).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var result = method!.Invoke(null, [root]);
        Assert.IsType<string>(result);
        return (string)result!;
    }
}
