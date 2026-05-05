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
        Assert.DoesNotContain("16 GB RAM meets a strong resale baseline", text, StringComparison.OrdinalIgnoreCase);
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
        Assert.DoesNotContain("Not ideal for: none listed", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Not ideal for:", text);
        Assert.Contains("Modern AAA gaming", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HardwareXray_IsCompactAndDoesNotExposeRawEnumReasons()
    {
        var text = InvokeSummary("BuildHardwareXraySummary", JsonDocument.Parse(
            """
            {
              "machineClass": { "primaryClass":"Mobile Workstation", "confidence":"High", "secondaryClasses":[] },
              "sensorMatrix": {
                "coverageSummary":"Coverage: partial",
                "liveSensorsSummary":"Live sensors available for inventory only",
                "statusGuide":"guide",
                "sensorProviders":[
                  { "providerName":"Windows Native", "isEnabled":true, "isBundled":true, "runtimeMode":"DefaultSafe" },
                  { "providerName":"LibreHardwareMonitor", "isEnabled":false, "isBundled":false, "runtimeMode":"Disabled", "failureReason":"LibreHardwareMonitor provider assembly is not packaged in this build." },
                  { "providerName":"ForgerEMS Admin Sensor Bridge", "isEnabled":false, "isBundled":false, "runtimeMode":"Disabled" },
                  { "providerName":"ForgerEMS Signed Driver Provider", "isEnabled":false, "isBundled":false, "runtimeMode":"Disabled" }
                ],
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

        Assert.Contains("Limited:", text);
        Assert.Contains("Sensor Providers: Windows Native: Active", text);
        Assert.Contains("LibreHardwareMonitor: Not packaged", text);
        Assert.Contains("Admin Bridge: Off", text);
        Assert.Contains("Driver Provider: Not included", text);
        Assert.Contains("may require deep/vendor sensor support", text);
        Assert.DoesNotContain("RequiresExternalProvider", text, StringComparison.Ordinal);
        Assert.DoesNotContain("RequiresVendorDriver", text, StringComparison.Ordinal);
        Assert.DoesNotContain("NotExposedByFirmware", text, StringComparison.Ordinal);
        Assert.DoesNotContain("UnsupportedHardware", text, StringComparison.Ordinal);
    }

    [Fact]
    public void HardwareXray_ShowsLibreHardwareMonitorActiveReadOnlyWhenEnabled()
    {
        var text = InvokeSummary("BuildHardwareXraySummary", JsonDocument.Parse(
            """
            {
              "machineClass": { "primaryClass":"Mobile Workstation", "confidence":"High", "secondaryClasses":[] },
              "sensorMatrix": {
                "coverageSummary":"Coverage: partial",
                "liveSensorsSummary":"CPU Package temperature",
                "statusGuide":"guide",
                "sensorProviders":[
                  { "providerName":"Windows Native", "isEnabled":true, "isBundled":true, "runtimeMode":"DefaultSafe" },
                  { "providerName":"LibreHardwareMonitor", "isEnabled":true, "isBundled":true, "runtimeMode":"DeepSensorReadOnly" },
                  { "providerName":"ForgerEMS Admin Sensor Bridge", "isEnabled":false, "isBundled":false, "runtimeMode":"Disabled" },
                  { "providerName":"ForgerEMS Signed Driver Provider", "isEnabled":false, "isBundled":false, "runtimeMode":"Disabled" }
                ],
                "groups":[]
              }
            }
            """).RootElement);

        Assert.Contains("LibreHardwareMonitor: Active read-only", text);
        Assert.Contains("Windows Native: Active", text);
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
        Assert.DoesNotContain("wear Wear", storage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NVMe/SSD via RAID/RST controller", storage);
        Assert.Contains("Disk health: Healthy; percentage not exposed", storage);
        Assert.DoesNotContain("Wi-Fi Not a Wi-Fi adapter", network, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Active physical internet adapter", network);
        Assert.Contains("TPM: Not reported by Windows scan. Verify BIOS/UEFI TPM/PTT setting.", security);
        Assert.Contains("Secure Boot: Unknown — requires admin or unavailable.", security);
        Assert.Contains("BitLocker: Unavailable — Windows did not report a reason.", security);
    }

    [Fact]
    public void StorageHealthPercent_DisplaysOnlyWhenProviderReturnsPercent()
    {
        var root = JsonDocument.Parse(
            """
            {
              "diskStatus":"READY",
              "disks":[
                {
                  "name":"NVMe","interfaceType":"NVMe","mediaType":"NVMe SSD","size":"1 TB",
                  "healthDisplay":"Healthy","wearPercent":4,
                  "diskHealthPercent":{"value":96,"confidence":"Medium","source":"MSFT_StorageReliabilityCounter.Wear","isEstimated":true},
                  "temperatureDisplay":"42 C","wearDisplay":"4%","status":"READY"
                }
              ]
            }
            """).RootElement;

        var storage = InvokeSummary("BuildDiskHealthSummary", root);

        Assert.Contains("Disk health: 96% estimated from MSFT_StorageReliabilityCounter.Wear", storage);
    }

    [Fact]
    public void SystemIntelligenceLayout_UsesIndependentColumnsInsteadOfUniformGrid()
    {
        var xaml = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));

        Assert.DoesNotContain("<UniformGrid Columns=\"2\"", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<StackPanel Grid.Column=\"0\"", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<StackPanel Grid.Column=\"1\"", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Focusable\" Value=\"False\"", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AutomationLine_UsesScanConfidenceLabel()
    {
        var method = typeof(MainViewModel).GetMethod("NormalizeSystemIntelligenceAutomationLine", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var normalized = (string)method!.Invoke(null, ["Health 74/100. Confidence 94/100. CPU tier: performance."])!;

        Assert.Contains("Scan Confidence 94/100", normalized);
        Assert.DoesNotContain(". Confidence 94/100", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void HardwareXray_UsbCoverageUsesUsbIntelligenceEvidence()
    {
        var text = InvokeSummary("BuildHardwareXraySummary", JsonDocument.Parse(
            """
            {
              "usbDiagnostics": {
                "usbProfileKnownPortsCount": 4,
                "usbCurrentTargetRiskSummary": "Current target risk: Low.",
                "usbBestKnownPortSummary": "Best measured port: LT USB-C (~60.5 MB/s write).",
                "lastBenchmark": { "succeeded": true, "summaryLine": "USB benchmark complete: Usb3" }
              },
              "machineClass": { "primaryClass":"Mobile Workstation", "confidence":"High", "secondaryClasses":[] },
              "sensorMatrix": {
                "coverageSummary":"CPU: 3/6 fields known; USB: 0/3 fields known; Cooling: 0/2 fields known",
                "groups":[
                  { "category":"USB", "knownFields":0, "totalFields":3, "readings":[] }
                ]
              }
            }
            """).RootElement);

        Assert.DoesNotContain("USB 0/3 known", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("USB: 0/3 fields known", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Best measured port: LT USB-C", text);
    }

    private static string InvokeSummary(string methodName, JsonElement root)
    {
        var method = typeof(MainViewModel).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var result = method!.Invoke(null, [root]);
        Assert.IsType<string>(result);
        return (string)result!;
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
