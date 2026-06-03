using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
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
                  { "providerName":"Forger Sensor Core", "isEnabled":true, "isBundled":true, "runtimeMode":"DefaultSafe" },
                  { "providerName":"LibreHardwareMonitor", "isEnabled":false, "isBundled":false, "runtimeMode":"Disabled", "failureReason":"Not packaged / unavailable — LibreHardwareMonitorLib.dll was not found under providers/sensors." },
                  { "providerName":"Forger Sensor Service", "isEnabled":false, "isBundled":false, "runtimeMode":"Disabled" },
                  { "providerName":"Forger Deep Sensor Driver", "isEnabled":false, "isBundled":false, "runtimeMode":"Disabled" }
                ],
                "deepSensorMode": { "mode":"Off", "source":"BuiltInDefault", "isEnabled":false },
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
        Assert.Contains("Forger Sensor Stack: Core active", text);
        Assert.Contains("Sensor Sources: Forger Sensor Core: Active", text);
        Assert.Contains("Deep Sensor Mode: Off via built-in default", text);
        Assert.Contains("no fan/voltage/clock/firmware control", text);
        Assert.Contains("LibreHardwareMonitor: Not packaged / unavailable", text);
        Assert.Contains("Forger Sensor Service: Not installed", text);
        Assert.Contains("Forger Deep Sensor Driver: Not included", text);
        Assert.Contains("may require deep/vendor sensor support", text);
        Assert.DoesNotContain("RequiresExternalProvider", text, StringComparison.Ordinal);
        Assert.DoesNotContain("RequiresVendorDriver", text, StringComparison.Ordinal);
        Assert.DoesNotContain("NotExposedByFirmware", text, StringComparison.Ordinal);
        Assert.DoesNotContain("NotExposed is not failure", text, StringComparison.Ordinal);
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
                  { "providerName":"Forger Sensor Core", "isEnabled":true, "isBundled":true, "runtimeMode":"DefaultSafe" },
                  { "providerName":"LibreHardwareMonitor", "isEnabled":true, "isBundled":true, "runtimeMode":"DeepSensorReadOnly" },
                  { "providerName":"Forger Sensor Service", "isEnabled":false, "isBundled":false, "runtimeMode":"Disabled" },
                  { "providerName":"Forger Deep Sensor Driver", "isEnabled":false, "isBundled":false, "runtimeMode":"Disabled" }
                ],
                "deepSensorMode": { "mode":"ReadOnly", "source":"InstallerDefault", "isEnabled":true },
                "groups":[]
              }
            }
            """).RootElement);

        Assert.Contains("LibreHardwareMonitor: Active read-only", text);
        Assert.Contains("Forger Sensor Core: Active", text);
        Assert.Contains("Deep Sensor Mode: ReadOnly via installer default", text);
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

    [Fact]
    public void HardwareXray_OptionalProviderStatusGroupsPermissionRequiredAndNotExposed()
    {
        var text = InvokeSummary("BuildHardwareXraySummary", JsonDocument.Parse(
            """
            {
              "machineClass": { "primaryClass":"Mobile Workstation", "confidence":"High", "secondaryClasses":[] },
              "sensorMatrix": {
                "coverageSummary":"Coverage: partial",
                "sensorProviders":[]
              },
              "optionalProviderStatus": [
                { "providerName":"Storage health detail", "category":"Disk inventory", "status":"PermissionRequired" },
                { "providerName":"Secure Boot firmware marker", "category":"Security", "status":"NotExposed" },
                { "providerName":"Battery wear provider", "category":"Battery", "status":"ProviderUnavailable" }
              ]
            }
            """).RootElement);

        Assert.Contains("Sensor limits:", text);
        Assert.Contains("Permission required: 1", text);
        Assert.Contains("Firmware/driver-limited: 1", text);
        Assert.Contains("Provider blocked/errors: 1", text);
        Assert.Contains("Elevated Scan unlocks extra detail when Windows allows it.", text);
    }

    [Fact]
    public void SystemIntelligenceLayout_ExposesThreePrimaryActions()
    {
        var xaml = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        var tabStart = xaml.IndexOf("<TabItem Header=\"◎  System Intelligence\">", StringComparison.Ordinal);
        Assert.True(tabStart >= 0);
        var tabEnd = xaml.IndexOf("<TabItem Header=\"▤  Toolkit Manager\">", tabStart, StringComparison.Ordinal);
        Assert.True(tabEnd > tabStart);
        var systemIntelligence = xaml[tabStart..tabEnd];

        Assert.Contains("HorizontalAlignment=\"Stretch\"", systemIntelligence, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"168\"", systemIntelligence, StringComparison.Ordinal);
        Assert.Contains("Elevated Scan", systemIntelligence, StringComparison.Ordinal);
        Assert.Contains("Open Files", systemIntelligence, StringComparison.Ordinal);
        Assert.Contains("Create Support Bundle", systemIntelligence, StringComparison.Ordinal);
        Assert.Contains("RunElevatedSystemScanCommand", systemIntelligence, StringComparison.Ordinal);
        Assert.Contains("OpenSystemIntelligenceFilesCommand", systemIntelligence, StringComparison.Ordinal);
        Assert.Contains("SystemIntelligenceScanStatusText", systemIntelligence, StringComparison.Ordinal);
        Assert.Contains("SystemIntelligenceHealthStatusText", systemIntelligence, StringComparison.Ordinal);
        Assert.Contains("SystemIntelligenceSensorStackStatusText", systemIntelligence, StringComparison.Ordinal);
        Assert.Contains("Forger Sensor Stack", systemIntelligence, StringComparison.Ordinal);
        Assert.DoesNotContain("Run Standard Scan", systemIntelligence, StringComparison.Ordinal);
        Assert.DoesNotContain("Refresh Results", systemIntelligence, StringComparison.Ordinal);
    }

    [Fact]
    public void ElevatedScanHandoff_ClosesNonElevatedInstanceAfterUacRelaunch()
    {
        var source = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "ViewModels", "MainViewModel.cs"));
        var handoff = source[source.IndexOf("RequestElevatedRelaunchAndRunScan", StringComparison.Ordinal)..];
        Assert.Contains("Application.Current?.Shutdown();", handoff, StringComparison.Ordinal);
        Assert.Contains("Closing this non-elevated window", handoff, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowState.Minimized", handoff, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenSystemIntelligenceFiles_UsesPickOptionPicker()
    {
        var source = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "ViewModels", "MainViewModel.cs"));
        Assert.Contains("OpenSystemIntelligenceFiles()", source, StringComparison.Ordinal);
        Assert.Contains("_userPromptService.PickOption(", source, StringComparison.Ordinal);
        Assert.Contains("Latest JSON report", source, StringComparison.Ordinal);
        Assert.Contains("Latest Markdown report", source, StringComparison.Ordinal);
        Assert.Contains("Reports folder", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ElevatedScanLauncher_DirectAdminPathAndTimeoutAreWired()
    {
        var source = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "ViewModels", "MainViewModel.cs"));

        Assert.Contains("if (!appElevated)", source, StringComparison.Ordinal);
        Assert.Contains("RequestElevatedRelaunchAndRunScan", source, StringComparison.Ordinal);
        Assert.Contains("Verb = \"runas\"", source, StringComparison.Ordinal);
        Assert.Contains("startInfo.ArgumentList.Add(arg)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Start-Process -FilePath $ps -ArgumentList $args -Verb RunAs -PassThru", source, StringComparison.Ordinal);
        Assert.DoesNotContain("$p.WaitForExit($timeoutMs)", source, StringComparison.Ordinal);
        Assert.Contains("skipping UAC relaunch", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Timeout = ElevatedScanDiagnostics.ElevatedScanWaitTimeout", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ElevatedScanStartupResume_ConsumesRequestAndDoesNotLoop()
    {
        var app = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "App.xaml.cs"));
        var source = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "ViewModels", "MainViewModel.cs"));

        Assert.Contains("ElevatedScanStartupRequest.Parse(e.Args)", app, StringComparison.Ordinal);
        Assert.Contains("ElevatedScanStartupRequest = ElevatedScanStartupRequest.None", source, StringComparison.Ordinal);
        Assert.Contains("ForgerEMS is still not running as administrator", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestElevatedRelaunchAndRunScan(reportsDir, correlationId);", source[
            source.IndexOf("private async Task ConsumeElevatedScanStartupRequestAsync", StringComparison.Ordinal)..], StringComparison.Ordinal);
    }

    [Fact]
    public void StartupRefresh_IsScheduledAndGuarded()
    {
        var source = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "ViewModels", "MainViewModel.cs"));
        var initializeBlock = source[
            source.IndexOf("public Task InitializeAsync()", StringComparison.Ordinal)..
            source.IndexOf("private async Task RunStartupInitializationAsync", StringComparison.Ordinal)];
        var startupBlock = source[source.IndexOf("private async Task RunStartupInitializationAsync", StringComparison.Ordinal)..];

        Assert.Contains("_ = RunStartupInitializationAsync();", initializeBlock, StringComparison.Ordinal);
        Assert.Contains("Startup refresh failed without closing ForgerEMS", startupBlock, StringComparison.Ordinal);
        Assert.Contains("HydrateFromCachedReportsEarlyAsync", startupBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void ElevatedScanLifecycle_UsesTruthfulUiCopy()
    {
        var source = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "ViewModels", "MainViewModel.cs"));

        Assert.Contains("Elevated scan complete", source, StringComparison.Ordinal);
        Assert.Contains("Deep hardware and port telemetry is available.", source, StringComparison.Ordinal);
        Assert.Contains("Elevated scan complete — some deep telemetry was unavailable on this device.", source, StringComparison.Ordinal);
        Assert.Contains("Elevated scan recommended", source, StringComparison.Ordinal);
        Assert.Contains("Run elevated scan for deeper port and hardware telemetry.", source, StringComparison.Ordinal);
        Assert.Contains("Elevated scan failed", source, StringComparison.Ordinal);
        Assert.Contains("ForgerEMS stayed open. Check logs or retry as administrator.", source, StringComparison.Ordinal);
    }

    [Fact]
    public void UiCopy_UsesForgerOwnedSensorStackAndDoesNotRequireExternalTools()
    {
        var xaml = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        var source = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "ViewModels", "MainViewModel.cs"));
        var combined = xaml + Environment.NewLine + source;

        Assert.Contains("Forger Sensor Stack", combined, StringComparison.Ordinal);
        Assert.Contains("Core: Active", combined, StringComparison.Ordinal);
        Assert.Contains("Sensor Service: Not installed", combined, StringComparison.Ordinal);
        Assert.Contains("Deep Sensor Driver: Not included", combined, StringComparison.Ordinal);
        Assert.Contains("External tools: Not required", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("install HWiNFO", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("install AIDA64", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("install CPU-Z", combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DispatcherUnhandledException_IsLoggedAndMarkedHandled()
    {
        var app = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "App.xaml.cs"));
        var handlerBlock = app[app.IndexOf("Current.DispatcherUnhandledException", StringComparison.Ordinal)..];

        Assert.Contains("LogStartupException(\"Unhandled dispatcher exception\"", handlerBlock, StringComparison.Ordinal);
        Assert.Contains("args.Handled = true;", handlerBlock, StringComparison.Ordinal);
        Assert.Contains("TaskScheduler.UnobservedTaskException", app, StringComparison.Ordinal);
        Assert.Contains("args.SetObserved();", app, StringComparison.Ordinal);
    }

    [Fact]
    public void UacCancelCopy_IsFriendly()
    {
        var source = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "ViewModels", "MainViewModel.cs"));

        Assert.Contains("Elevated Scan was cancelled before administrator permission was approved. Standard Scan results are still available.", source, StringComparison.Ordinal);
        Assert.Contains("NativeError=1223", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CopyAdminCommand_UsesSelectedPowerShellAndQuotedFileArguments()
    {
        var source = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "ViewModels", "MainViewModel.cs"));

        Assert.Contains("var exe = File.Exists(windowsPs) ? windowsPs : \"powershell.exe\";", source, StringComparison.Ordinal);
        Assert.Contains("BuildPowerShellQuotedFileArgs(scriptPath, reportsDir, writeMarkers: true)", source, StringComparison.Ordinal);
        Assert.Contains("Clipboard.SetText(line)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ElevatedScanExitCodeMinus196608_MapsToLaunchFailureWithAdvancedDiagnostics()
    {
        var run = CreateElevatedRun(
            exitCode: ElevatedScanLaunchClassifier.KnownShellElevatedLaunchPseudoExit,
            "[FAIL] Elevated scan exited with code -196608.");
        var analysis = ElevatedScanLaunchClassifier.Analyze(run, scanOutputLikelyMissing: false);

        Assert.Equal(ElevatedScanFailureKind.ElevatedScanLaunchFailed, analysis.Kind);
        Assert.DoesNotContain("-196608", analysis.PrimaryUserMessage, StringComparison.Ordinal);
        Assert.Contains("UAC", analysis.PrimaryUserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Standard Scan results are still available", analysis.PrimaryUserMessage, StringComparison.Ordinal);
        Assert.Contains("-196608", analysis.AdvancedDiagnosticsLine, StringComparison.Ordinal);
        Assert.Contains("0xFFFD0000", analysis.AdvancedDiagnosticsLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ElevatedScanUnknownExitCode_UsesGenericLaunchGuidance()
    {
        var run = CreateElevatedRun(123, "[FAIL] Elevated scan exited with code 123.");
        var analysis = ElevatedScanLaunchClassifier.Analyze(run, scanOutputLikelyMissing: false);

        Assert.Equal(ElevatedScanFailureKind.UnknownElevatedLaunchFailure, analysis.Kind);
        Assert.Contains("UAC", analysis.PrimaryUserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Standard Scan results are still available", analysis.PrimaryUserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ElevatedScanTimeout_MapsToTimeoutGuidance()
    {
        var run = CreateElevatedRun(
            ElevatedScanDiagnostics.TimeoutExitCode,
            "[FAIL] Elevated scan timed out waiting for completion.");
        var analysis = ElevatedScanLaunchClassifier.Analyze(run, scanOutputLikelyMissing: false);

        Assert.Equal(ElevatedScanFailureKind.ElevatedProcessTimedOut, analysis.Kind);
        Assert.Contains("did not finish in time", analysis.PrimaryUserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Standard Scan results are still available", analysis.PrimaryUserMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(ElevatedScanDiagnostics.TimeoutExitCode.ToString(CultureInfo.InvariantCulture), analysis.PrimaryUserMessage, StringComparison.Ordinal);
        Assert.Contains(ElevatedScanDiagnostics.TimeoutExitCode.ToString(CultureInfo.InvariantCulture), analysis.AdvancedDiagnosticsLine, StringComparison.Ordinal);
    }

    [Fact]
    public void ElevatedScanTimedOutResult_MapsToTimeoutEvenWithDifferentExitCode()
    {
        var run = CreateElevatedRun(
            1,
            timedOut: true,
            "[FAIL] System Intelligence elevated scan timed out after 15 minutes.");
        var analysis = ElevatedScanLaunchClassifier.Analyze(run, scanOutputLikelyMissing: false);

        Assert.Equal(ElevatedScanFailureKind.ElevatedProcessTimedOut, analysis.Kind);
        Assert.Contains("did not finish in time", analysis.PrimaryUserMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WarningReason_IncludesBatteryAndWindowsReadinessSignals()
    {
        var root = JsonDocument.Parse(
            """
            {
              "diskStatus":"READY",
              "batteries":[{ "wearDisplay":"40.2%" }],
              "summary":{
                "tpmInfo":{"status":"UNKNOWN"},
                "secureBootInfo":{"status":"UNKNOWN"}
              },
              "optionalProviderStatus":[]
            }
            """).RootElement;
        var method = typeof(MainViewModel).GetMethod("BuildSystemIntelligenceWarningReason", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var reason = (string)method!.Invoke(null, [root, "Overall diagnostics warning"] )!;
        Assert.Contains("Warning:", reason);
        Assert.Contains("Battery wear", reason);
        Assert.Contains("Windows readiness verification needed", reason);
    }

    [Fact]
    public void NetworkCompactSummary_DoesNotExposeIpsByDefault()
    {
        var root = JsonDocument.Parse(
            """
            {
              "network":{
                "status":"READY",
                "internetCheck":true,
                "adapters":[
                  {
                    "name":"Ethernet",
                    "adapterRole":"ActivePhysicalInternet",
                    "ipAddresses":["192.168.1.19"],
                    "gateways":["192.168.1.1"],
                    "dnsServers":["1.1.1.1"]
                  }
                ]
              }
            }
            """).RootElement;
        var compactMethod = typeof(MainViewModel).GetMethod("BuildNetworkSummaryCompact", BindingFlags.NonPublic | BindingFlags.Static);
        var technicalMethod = typeof(MainViewModel).GetMethod("BuildNetworkTechnicalDetails", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(compactMethod);
        Assert.NotNull(technicalMethod);

        var compact = (string)compactMethod!.Invoke(null, [root])!;
        var technical = (string)technicalMethod!.Invoke(null, [root])!;

        Assert.Contains("DNS: configured", compact);
        Assert.DoesNotContain("192.168.1.19", compact, StringComparison.Ordinal);
        Assert.Contains("192.168.1.19", technical);
    }

    private static string InvokeSummary(string methodName, JsonElement root)
    {
        var method = typeof(MainViewModel).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var result = method!.Invoke(null, [root]);
        Assert.IsType<string>(result);
        return (string)result!;
    }

    [Fact]
    public void ElevatedScanFailure_GuidanceDoesNotSayBackendBroken()
    {
        var run = CreateElevatedRun(
            ElevatedScanLaunchClassifier.KnownShellElevatedLaunchPseudoExit,
            "[FAIL] Elevated scan exited with code -196608.");
        var analysis = ElevatedScanLaunchClassifier.Analyze(run, false);
        var joined = analysis.PrimaryUserMessage + " " + string.Join(' ', analysis.SupplementalActionLines);

        Assert.DoesNotContain("backend", joined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("broken", joined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Standard Scan results are still available", joined, StringComparison.Ordinal);
    }

    [Fact]
    public void ElevatedScanParser_ReplacesRawFailLineWithFriendlySummary()
    {
        var parser = new ScriptStatusParser();
        var run = CreateElevatedRun(
            ElevatedScanLaunchClassifier.KnownShellElevatedLaunchPseudoExit,
            "[FAIL] Elevated scan exited with code -196608.");
        var parsed = parser.Parse(ScriptActionType.SystemIntelligence, "System Intelligence elevated scan", run, false);

        Assert.False(parsed.Succeeded);
        Assert.DoesNotContain("-196608", parsed.Summary, StringComparison.Ordinal);
        Assert.Contains("UAC", parsed.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-196608", parsed.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void ElevatedScanParser_UsesFriendlyTimeoutSummary()
    {
        var parser = new ScriptStatusParser();
        var run = CreateElevatedRun(
            ElevatedScanDiagnostics.TimeoutExitCode,
            "[FAIL] Elevated scan timed out waiting for completion.");
        var parsed = parser.Parse(ScriptActionType.SystemIntelligence, "System Intelligence elevated scan", run, false);

        Assert.False(parsed.Succeeded);
        Assert.Contains("did not finish in time", parsed.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Standard Scan results are still available", parsed.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(ElevatedScanDiagnostics.TimeoutExitCode.ToString(CultureInfo.InvariantCulture), parsed.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Win32Exit1223_MapsToUacCancelled()
    {
        var run = CreateElevatedRun(1223, "[FAIL] Elevation handoff failed: canceled NativeError=1223");
        var analysis = ElevatedScanLaunchClassifier.Analyze(run, false);
        Assert.Equal(ElevatedScanFailureKind.UacCancelled, analysis.Kind);
    }

    [Fact]
    public void ScriptMissingExit2_MapsToBackendScriptMissing()
    {
        var run = CreateElevatedRun(2, "[FAIL] System Intelligence script missing.");
        var analysis = ElevatedScanLaunchClassifier.Analyze(run, false);
        Assert.Equal(ElevatedScanFailureKind.BackendScriptMissing, analysis.Kind);
    }

    private static PowerShellRunResult CreateElevatedRun(int exitCode, params string[] hostLines) =>
        CreateElevatedRun(exitCode, timedOut: false, hostLines);

    private static PowerShellRunResult CreateElevatedRun(int exitCode, bool timedOut, params string[] hostLines)
    {
        var lines = new List<LogLine>();
        foreach (var text in hostLines)
        {
            lines.Add(new LogLine(DateTimeOffset.UtcNow, text, LogSeverity.Error, isErrorStream: false));
        }

        return new PowerShellRunResult
        {
            ExitCode = exitCode,
            OutputLines = lines,
            StandardOutputText = string.Join(Environment.NewLine, hostLines),
            TimedOut = timedOut
        };
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
