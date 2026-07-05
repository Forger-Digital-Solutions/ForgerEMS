using System;
using System.Linq;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using VentoyToolkitSetup.Wpf;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using VentoyToolkitSetup.Wpf.ViewModels;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Ensures MainWindow XAML resolves all StaticResource keys (regression for style declaration order)
/// and constructs without binding errors (e.g. read-only header properties must use OneWay bindings).
/// </summary>
public sealed class MainWindowXamlLoadTests
{
    [Fact]
    public void MainWindow_XamlContainsUpdatedPolishLabels()
    {
        var xamlPath = FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml");
        var text = File.ReadAllText(xamlPath);

        Assert.Contains("Refresh USB Targets", text, StringComparison.Ordinal);
        Assert.Contains("Create Support Bundle", text, StringComparison.Ordinal);
        Assert.Contains("Copy Update Diagnostics", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Run Elevated Scan for more detail", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Copy update-check diagnostics (safe summary)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_SystemIntelligenceAndDiagnosticsTabs_AreRemoved_AndDoNotReturn()
    {
        // Shell simplification pass: the heavy System Intelligence and Diagnostics
        // surfaces moved to Dr. Forge (the dedicated diagnostics companion). Guard
        // against either tab — or its sidebar nav button — quietly returning.
        var xamlPath = FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml");
        var text = File.ReadAllText(xamlPath);

        Assert.DoesNotContain("<TabItem Header=\"◎  System Intelligence\">", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<TabItem Header=\"⚙  Diagnostics\">", text, StringComparison.Ordinal);
        Assert.DoesNotContain("NavSystemButton", text, StringComparison.Ordinal);
        Assert.DoesNotContain("NavDiagnosticsButton", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"◎  System Intelligence\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"⚙  Diagnostics\"", text, StringComparison.Ordinal);

        // The diagnostics-only Mission Control / Safety Lab content must be gone too.
        Assert.DoesNotContain("Text=\"1) Mission Control\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"2) Evidence &amp; Logs\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"3) Safety Lab\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_TabStrip_ContainsExactlyTheSixKeptTabsInOrder()
    {
        var xamlPath = FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml");
        var text = File.ReadAllText(xamlPath);

        var headers = System.Text.RegularExpressions.Regex
            .Matches(text, "^                    <TabItem Header=\"(?<h>[^\"]+)\">", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(m => m.Groups["h"].Value)
            .ToArray();

        var expected = new[]
        {
            "USB Builder",
            "Port / USB Intelligence",
            "▤  Toolkit Manager",
            "▥  Driver Hub",
            "◇  Kyra (Beta)",
            "☰  Settings"
        };

        Assert.Equal(expected, headers);
    }

    [Fact]
    public void MainWindow_LiveLogsTab_IsRemoved_AndDoesNotReturn()
    {
        // Live Logs is a single always-visible side panel (plus the View Full Logs
        // overlay) — it must not also exist as a dedicated tab / sidebar nav button.
        var xamlPath = FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml");
        var text = File.ReadAllText(xamlPath);

        Assert.DoesNotContain("<TabItem Header=\"Live Logs\">", text, StringComparison.Ordinal);
        Assert.DoesNotContain("NavLiveLogsButton", text, StringComparison.Ordinal);
        Assert.DoesNotContain("LiveLogsTabTextBox", text, StringComparison.Ordinal);

        // The persistent side panel and its full-logs overlay entry point must remain.
        Assert.Contains("Text=\"Live Logs\"", text, StringComparison.Ordinal);
        Assert.Contains("View Full Logs", text, StringComparison.Ordinal);
        Assert.Contains("FullLogsOverlay", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_SupportBundleAndDrForge_StayReachableInToolkitManager()
    {
        // Create Support Bundle (still needed by support flows) and the honest,
        // read-only Dr. Forge Intake bridge live in Toolkit Manager after the
        // heavy System Intelligence tab was removed.
        var text = LoadMainWindowXaml();
        var toolkit = ExtractTab(text, "▤  Toolkit Manager");

        Assert.Contains("Content=\"Create Support Bundle\"", toolkit, StringComparison.Ordinal);
        Assert.Contains("ExportSupportBundleCommand", toolkit, StringComparison.Ordinal);
        Assert.Contains("Content=\"Learn about Dr. Forge\"", toolkit, StringComparison.Ordinal);
        Assert.Contains("Text=\"Dr. Forge Intake\"", toolkit, StringComparison.Ordinal);
        Assert.Contains("Dr. Forge runs as a local user-mode hardware intake/report tool.", toolkit, StringComparison.Ordinal);
        Assert.Contains("Reports stay local unless you explicitly export or include them in a support bundle.", toolkit, StringComparison.Ordinal);
        Assert.Contains("No production sensor driver is shipped or loaded.", toolkit, StringComparison.Ordinal);
        Assert.Contains("Driver-required readings are unavailable until a future signed-driver phase.", toolkit, StringComparison.Ordinal);
        Assert.Contains("DrForgeVersionDetailText", toolkit, StringComparison.Ordinal);
        Assert.Contains("DrForgeStatusSummaryText", toolkit, StringComparison.Ordinal);
        Assert.Contains("DrForgeLastSuccessfulScanText", toolkit, StringComparison.Ordinal);
        Assert.Contains("DrForgeReportHistoryText", toolkit, StringComparison.Ordinal);
        Assert.Contains("Local Dr. Forge report preview", toolkit, StringComparison.Ordinal);
        Assert.Contains("Preview is read-only.", toolkit, StringComparison.Ordinal);
        Assert.Contains("DrForgeReportHistoryItems", toolkit, StringComparison.Ordinal);
        Assert.Contains("SelectedDrForgeReportHistoryItem", toolkit, StringComparison.Ordinal);
        Assert.Contains("Header=\"Parsed Sections\"", toolkit, StringComparison.Ordinal);
        Assert.Contains("Header=\"Raw Preview\"", toolkit, StringComparison.Ordinal);
        Assert.Contains("DrForgeReportParsedStatusText", toolkit, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding DrForgeReportSections}\"", toolkit, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding DrForgeReportDetailText, Mode=OneWay}\"", toolkit, StringComparison.Ordinal);
        Assert.Contains("Content=\"Select CLI\"", toolkit, StringComparison.Ordinal);
        Assert.Contains("Content=\"Check Package\"", toolkit, StringComparison.Ordinal);
        Assert.Contains("Content=\"Refresh Status\"", toolkit, StringComparison.Ordinal);
        Assert.Contains("Content=\"Generate Report\"", toolkit, StringComparison.Ordinal);
        Assert.Contains("Content=\"Generate Archive\"", toolkit, StringComparison.Ordinal);
        Assert.Contains("Content=\"Open Report Folder\"", toolkit, StringComparison.Ordinal);
        Assert.Contains("Content=\"Copy Status Summary\"", toolkit, StringComparison.Ordinal);
        Assert.Contains("Content=\"Copy Report Summary\"", toolkit, StringComparison.Ordinal);
        Assert.Contains("Content=\"Open Containing Folder\"", toolkit, StringComparison.Ordinal);
        Assert.Contains("Include latest Dr. Forge report/archive", toolkit, StringComparison.Ordinal);
        Assert.DoesNotContain("Deep system scans", toolkit, StringComparison.Ordinal);
        Assert.DoesNotContain("hardware/sensor intelligence", toolkit, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_UsbBuilderActions_DoNotExposeVerifyBackendButton()
    {
        var xamlPath = FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml");
        var text = File.ReadAllText(xamlPath);

        Assert.DoesNotContain("VerifyCommand", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Run backend checks", text, StringComparison.Ordinal);
        Assert.DoesNotContain("✓ Verify", text, StringComparison.Ordinal);
        Assert.Contains("SetupUsbCommand", text, StringComparison.Ordinal);
        Assert.Contains("UpdateUsbCommand", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_NetworkPulse_IsFullyRetiredFromTheShell()
    {
        // v1.2.3: the always-visible Internet widget was removed from the shell header,
        // and the whole Network Pulse feature was then retired in v1.2.3-preview.1.
        // No Network Pulse widget, settings section, command, or binding may remain
        // anywhere in the main window.
        var xamlPath = FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml");
        var text = File.ReadAllText(xamlPath);
        Assert.Contains("HeaderRightStatusGrid", text, StringComparison.Ordinal);
        Assert.Contains("HeaderLeftSupportCopyGroup", text, StringComparison.Ordinal);

        Assert.DoesNotContain("NetworkPulse", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Network Pulse", text, StringComparison.Ordinal);
        Assert.DoesNotContain("InternetWidgetLine1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Run Network Check", text, StringComparison.Ordinal);
        Assert.DoesNotContain("HeaderWidgetVisibility", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_HeaderSupportCopy_IsInLeftBrandGroup_NotFullWidthFooterRow()
    {
        var xamlPath = FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml");
        var text = File.ReadAllText(xamlPath);
        var supportStart = text.IndexOf("HeaderLeftSupportCopyGroup", StringComparison.Ordinal);
        Assert.True(supportStart >= 0);
        Assert.Contains("HeaderRightStatusGrid", text, StringComparison.Ordinal);

        var supportOpenEnd = text.IndexOf('>', supportStart);
        var supportOpenTag = text[supportStart..supportOpenEnd];
        Assert.Contains("HorizontalAlignment=\"Left\"", supportOpenTag, StringComparison.Ordinal);
        Assert.DoesNotContain("Grid.ColumnSpan=\"7\"", supportOpenTag, StringComparison.Ordinal);
        Assert.DoesNotContain("Grid.Row=\"1\"", supportOpenTag, StringComparison.Ordinal);

        var supportBlockEnd = text.IndexOf("</StackPanel>", supportStart, StringComparison.Ordinal);
        var supportBlock = text[supportStart..supportBlockEnd];
        Assert.Contains("Beta issue? Send logs/screenshots", supportBlock, StringComparison.Ordinal);
        Assert.Contains("SupportEmailDoNotSecretsText", supportBlock, StringComparison.Ordinal);
        Assert.Contains("PublicPreviewBannerText", supportBlock, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"NoWrap\"", supportBlock, StringComparison.Ordinal);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", supportBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_HeaderGroups_AreTopAlignedAndCompact()
    {
        var xamlPath = FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml");
        var text = File.ReadAllText(xamlPath);
        var rightStart = text.IndexOf("HeaderRightStatusGrid", StringComparison.Ordinal);
        Assert.True(rightStart >= 0);
        var rightOpenEnd = text.IndexOf('>', rightStart);
        var rightOpenTag = text[rightStart..rightOpenEnd];
        Assert.Contains("VerticalAlignment=\"Top\"", rightOpenTag, StringComparison.Ordinal);
        Assert.DoesNotContain("Grid.RowSpan=\"2\"", rightOpenTag, StringComparison.Ordinal);
        Assert.DoesNotContain("Grid.RowSpan=\"3\"", rightOpenTag, StringComparison.Ordinal);

        var headerStart = text.IndexOf("<Border Grid.Row=\"0\"", StringComparison.Ordinal);
        Assert.True(headerStart >= 0);
        var headerOpenEnd = text.IndexOf('>', headerStart);
        var headerOpenTag = text[headerStart..headerOpenEnd];
        Assert.Contains("Padding=\"10,6\"", headerOpenTag, StringComparison.Ordinal);
        Assert.Contains("Margin=\"0,0,0,6\"", headerOpenTag, StringComparison.Ordinal);
        Assert.DoesNotContain("MinHeight=\"56\"", headerOpenTag, StringComparison.Ordinal);
        Assert.DoesNotContain("MinHeight=\"64\"", headerOpenTag, StringComparison.Ordinal);
        Assert.DoesNotContain("MinHeight=\"72\"", headerOpenTag, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_SettingsTabUsesCompactSafetyAndUpdateWording()
    {
        var xamlPath = FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml");
        var text = File.ReadAllText(xamlPath);
        var settingsStart = text.IndexOf("<TabItem Header=\"☰  Settings\">", StringComparison.Ordinal);
        Assert.True(settingsStart >= 0);
        var settings = text[settingsStart..];

        Assert.Contains("Keep Local Only", settings, StringComparison.Ordinal);
        Assert.Contains("Help Improve Kyra", settings, StringComparison.Ordinal);
        Assert.Contains("Learn More", settings, StringComparison.Ordinal);
        Assert.Contains("View Shared Preview", settings, StringComparison.Ordinal);
        Assert.Contains("Export Memory", settings, StringComparison.Ordinal);
        Assert.Contains("Delete Memory", settings, StringComparison.Ordinal);
        Assert.Contains("Reset Learning", settings, StringComparison.Ordinal);
        Assert.Contains("AppUpdateCheckButtonText", settings, StringComparison.Ordinal);
        Assert.Contains("AppUpdateCheckHelperText", settings, StringComparison.Ordinal);
        Assert.Contains("Copy Update Diagnostics", settings, StringComparison.Ordinal);
        Assert.Contains("Copies a safe summary for support sharing.", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("Check Now", settings, StringComparison.Ordinal);

        // Retired feature sections must not return to Settings.
        Assert.DoesNotContain("Forger Sensor Stack / Deep Sensor Mode", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("Deep Sensor Mode", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("Deep Sense", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("Network Pulse", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("DeepSensorModeSelectedIndex", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("DeepSensorModeConsentNotice", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowConstructsWithoutStaticResourceErrors()
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new VentoyToolkitSetup.Wpf.App();
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                var runtimeService = new AppRuntimeService();
                runtimeService.EnsureInitialized();
                var backendDiscoveryService = new BackendDiscoveryService();
                var powerShellRunnerService = new PowerShellRunnerService();
                var usbDetectionService = new UsbDetectionService(powerShellRunnerService);
                var managedDownloadSummaryService = new ManagedDownloadSummaryService();
                var scriptStatusParser = new ScriptStatusParser();
                var userPromptService = new UserPromptService();
                var ventoyIntegrationService = new VentoyIntegrationService(powerShellRunnerService, runtimeService);
                var usbBenchmarkService = new UsbBenchmarkService(powerShellRunnerService);
                var copilotProviderRegistry = new CopilotProviderRegistry();
                var copilotService = new CopilotService(copilotProviderRegistry);

                var mainViewModel = new MainViewModel(
                    backendDiscoveryService,
                    powerShellRunnerService,
                    usbDetectionService,
                    managedDownloadSummaryService,
                    scriptStatusParser,
                    userPromptService,
                    ventoyIntegrationService,
                    runtimeService,
                    usbBenchmarkService,
                    copilotService,
                    copilotProviderRegistry,
                    wslExecutor: null,
                    usbIntelligenceService: new UsbIntelligenceService(),
                    autoIntelligenceOrchestrator: new NoOpAutoIntelligenceOrchestrator());

                var mainWindow = new VentoyToolkitSetup.Wpf.MainWindow(mainViewModel);
                var readableCombo = mainWindow.TryFindResource("ReadableComboBoxStyle") as Style;
                Assert.NotNull(readableCombo);
                Assert.Contains(
                    readableCombo.Setters.OfType<Setter>(),
                    setter => setter.Property == Control.TemplateProperty);
                Assert.NotNull(mainWindow.TryFindResource("ReadableComboBoxItemStyle"));
                Assert.NotNull(mainWindow.TryFindResource("SecondaryButtonStyle"));
                Assert.NotNull(mainWindow.TryFindResource("FooterButtonStyle"));
                Assert.NotNull(mainWindow.TryFindResource("CopilotChatScrollViewerStyle"));
                var kyraAdvanced = new KyraAdvancedSettingsWindow
                {
                    DataContext = mainViewModel
                };
                kyraAdvanced.Close();
                mainWindow.Close();
                app.Shutdown();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(120)), "STA thread timed out.");
        Assert.Null(caught);
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

    private static string LoadMainWindowXaml() =>
        File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));

    private static string ExtractTab(string xaml, string header)
    {
        const string mainTabIndent = "                    ";
        var start = xaml.IndexOf(mainTabIndent + $"<TabItem Header=\"{header}\">", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find tab '{header}'.");

        var end = xaml.IndexOf("\n" + mainTabIndent + "<TabItem Header=", start + 1, StringComparison.Ordinal);
        return end > start ? xaml[start..end] : xaml[start..];
    }

    [Fact]
    public void MainWindow_DriveValidatorProgressBar_IsRemovedFromUsbBuilderTab()
    {
        // Drive Validator progress belongs in the Drive Validator Wizard, not the main USB
        // Builder surface. The wizard's own RunningProgressValue ProgressBar lives in
        // DriveValidatorWizardWindow.xaml and is unaffected.
        var xamlPath = FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml");
        var text = File.ReadAllText(xamlPath);

        Assert.DoesNotContain(
            "{Binding DriveValidatorProgressValue",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_DriveValidatorReadOnlyDisplays_DoNotUseTwoWayBindings()
    {
        // Defence-in-depth: walk every Drive Validator binding in MainWindow.xaml and ensure none of the
        // read-only display properties is wired as TwoWay or OneWayToSource. The TwoWay binding on
        // DriveValidatorModeIndex is allowed because that property has a public setter.
        var xamlPath = FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml");
        var text = File.ReadAllText(xamlPath);

        var readOnlyProps = new[]
        {
            "DriveValidatorIntro",
            "DriveValidatorTargetDisplay",
            "DriveValidatorCapacityDisplay",
            "DriveValidatorFileSystemDisplay",
            "DriveValidatorFreeSpaceDisplay",
            "DriveValidatorBusPortDisplay",
            "DriveValidatorPhaseDisplay",
            "DriveValidatorProgressDisplay",
            "DriveValidatorProgressValue",
            "DriveValidatorResultSummary",
            "DriveValidatorEvidenceDisplay",
            "DriveValidatorBuilderWarningText",
            "HasDriveValidatorBuilderWarning"
        };

        foreach (var prop in readOnlyProps)
        {
            Assert.DoesNotContain($"{{Binding {prop}, Mode=TwoWay}}", text, StringComparison.Ordinal);
            Assert.DoesNotContain($"{{Binding {prop}, Mode=OneWayToSource}}", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MainWindow_DriveValidatorReadOnlyProperties_HavePrivateSetters()
    {
        // Belt-and-braces guard: if anyone later promotes a private setter to public to make a TwoWay
        // binding "work", this test surfaces the change so the binding intent stays explicit (these
        // are display-only progress / phase / evidence projections from the validator service —
        // making them publicly settable would let the UI overwrite service state).
        var vmType = typeof(MainViewModel);
        string[] readOnlyNames =
        [
            "DriveValidatorProgressValue",
            "DriveValidatorPhaseDisplay",
            "DriveValidatorProgressDisplay",
            "DriveValidatorResultSummary",
            "DriveValidatorEvidenceDisplay",
            "DriveValidatorBuilderWarningText",
            "DriveValidatorTargetDisplay",
            "DriveValidatorCapacityDisplay",
            "DriveValidatorFileSystemDisplay",
            "DriveValidatorFreeSpaceDisplay",
            "DriveValidatorBusPortDisplay",
            "DriveValidatorModeDisplay"
        ];

        foreach (var name in readOnlyNames)
        {
            var prop = vmType.GetProperty(name);
            Assert.NotNull(prop);
            var setter = prop!.SetMethod;
            Assert.True(
                setter is null || !setter.IsPublic,
                $"{name} should not expose a public setter; UI bindings to it must use Mode=OneWay.");
        }
    }

    [Fact]
    public void MainWindow_UsbProductTabs_UseRequestedSectionShape()
    {
        var text = LoadMainWindowXaml();
        var builder = ExtractTab(text, "USB Builder");
        var intelligence = ExtractTab(text, "Port / USB Intelligence");

        Assert.Contains("NavUsbButton", text, StringComparison.Ordinal);
        Assert.Contains("NavPortUsbIntelligenceButton", text, StringComparison.Ordinal);

        foreach (var header in new[] { "USB Target", "USB Builder Profile", "Ventoy", "Actions" })
        {
            Assert.Contains($"<GroupBox Header=\"{header}\">", builder, StringComparison.Ordinal);
        }

        // v1.2.3-preview.1 dashboard shape: devices at the top, check actions, latest
        // results, then the safe battery/system-specs summary.
        foreach (var header in new[] { "Connected USB Devices", "USB / Drive Checks", "Latest Results", "Battery &amp; System Specs" })
        {
            Assert.Contains($"<GroupBox Header=\"{header}\">", intelligence, StringComparison.Ordinal);
        }

        Assert.Contains("PortUsbDashboardCardStyle", text, StringComparison.Ordinal);
        Assert.Contains("PortUsbResultCardStyle", text, StringComparison.Ordinal);
        Assert.Contains("<WrapPanel>", intelligence, StringComparison.Ordinal);
        Assert.DoesNotContain("<UniformGrid Columns=\"3\">", intelligence, StringComparison.Ordinal);

        foreach (var retiredHeader in new[] { "Port Map", "USB Validation / Health", "Charging / Power", "Diagnostics" })
        {
            Assert.DoesNotContain($"<GroupBox Header=\"{retiredHeader}\">", intelligence, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void UsbBuilder_DoesNotDuplicatePortHealthMapOrChargingControls()
    {
        var builder = ExtractTab(LoadMainWindowXaml(), "USB Builder");

        Assert.DoesNotContain("UsbBuilderUsbIntelligenceCompactCard", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("UsbBuilderPortPowerDetailsCard", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("UsbBuilderDriveValidatorCompactCard", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Port Map\"", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"USB Validation / Health\"", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Charging / Power\"", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("RunUsbIntelligenceBenchmarkCommand", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("CancelUsbIntelligenceBenchmarkCommand", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenUsbMappingWizardCommand", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshPortPowerCommand", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("UsbIntelligenceBenchmarkReadWriteDisplay", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("PortIntelligenceChargingSummaryText", builder, StringComparison.Ordinal);
    }

    [Fact]
    public void PortUsbIntelligence_DoesNotDuplicateBuilderWorkflowControls()
    {
        var intelligence = ExtractTab(LoadMainWindowXaml(), "Port / USB Intelligence");

        Assert.DoesNotContain("SetupUsbCommand", intelligence, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateUsbCommand", intelligence, StringComparison.Ordinal);
        Assert.DoesNotContain("InstallOrUpdateVentoyCommand", intelligence, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshVentoyStatusCommand", intelligence, StringComparison.Ordinal);
        Assert.DoesNotContain("FullManagedDownloadCommand", intelligence, StringComparison.Ordinal);
        Assert.DoesNotContain("RevalidateManagedDownloadsCommand", intelligence, StringComparison.Ordinal);
        Assert.DoesNotContain("UsbBuilderProfileOptions", intelligence, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectRecommendedUsbBuilderProfileCommand", intelligence, StringComparison.Ordinal);
        Assert.DoesNotContain("FullVerifyToolkitHealthCommand", intelligence, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenToolkitFolderCommand", intelligence, StringComparison.Ordinal);
    }

    [Fact]
    public void UsbBuilder_ExistingCommandsRemainBound()
    {
        var builder = ExtractTab(LoadMainWindowXaml(), "USB Builder");

        foreach (var binding in new[]
        {
            "RefreshUsbTargetsCommand",
            "OpenDriveValidatorWizardCommand",
            "SelectRecommendedUsbBuilderProfileCommand",
            "SelectAllUsbBuilderProfileCommand",
            "ResetUsbBuilderProfileCommand",
            "FullManagedDownloadCommand",
            "InstallOrUpdateVentoyCommand",
            "RefreshVentoyStatusCommand",
            "RevalidateManagedDownloadsCommand",
            "SetupUsbCommand",
            "UpdateUsbCommand",
            "FullVerifyToolkitHealthCommand",
            "OpenToolkitFolderCommand",
            "RenameUsbCommand",
            "RefreshAllCommand",
            "RetryFailedManagedDownloadsCommand",
            "OpenLogsFolderCommand"
        })
        {
            Assert.Contains(binding, builder, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void UsbBuilder_ProfileCardsExposeItemLevelSummariesAndPickerAction()
    {
        var builder = ExtractTab(LoadMainWindowXaml(), "USB Builder");

        Assert.Contains("SelectedItemSummaryText", builder, StringComparison.Ordinal);
        Assert.Contains("ManagedDownloadsSummaryText", builder, StringComparison.Ordinal);
        Assert.Contains("SelectedUsbFootprintSummaryText", builder, StringComparison.Ordinal);
        Assert.Contains("ManualUserSuppliedSummaryText", builder, StringComparison.Ordinal);
        Assert.Contains("Presets are editable starting points", builder, StringComparison.Ordinal);
        Assert.Contains("Content=\"Pick items\"", builder, StringComparison.Ordinal);
        Assert.Contains("CustomizeUsbBuilderCategoryCommand", builder, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"{Binding}\"", builder, StringComparison.Ordinal);
        Assert.Contains("OnUsbBuilderProfileCardMouseLeftButtonUp", builder, StringComparison.Ordinal);
        Assert.Contains("Cursor=\"Hand\"", builder, StringComparison.Ordinal);
    }

    [Fact]
    public void UsbBuilder_PickItemsButton_UsesCompactSecondaryStyle_AndOldWordingIsGone()
    {
        // The picker button was relabelled "Choose items" -> "Pick items" and made
        // compact so it reads as a secondary action and stays aligned/readable at
        // 1366x768. Guard both the wording swap and the compact styling.
        var xaml = LoadMainWindowXaml();
        var builder = ExtractTab(xaml, "USB Builder");

        Assert.DoesNotContain("Content=\"Choose items\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Pick items\"", builder, StringComparison.Ordinal);

        // The button uses the dedicated compact style rather than the heavy
        // SecondaryButtonStyle (MinHeight 40 / Padding 14,10).
        Assert.Contains("UsbBuilderCategoryPickerButtonStyle", builder, StringComparison.Ordinal);

        var styleStart = xaml.IndexOf("x:Key=\"UsbBuilderCategoryPickerButtonStyle\"", StringComparison.Ordinal);
        Assert.True(styleStart >= 0, "Compact picker button style must be defined.");
        var styleEnd = xaml.IndexOf("</Style>", styleStart, StringComparison.Ordinal);
        Assert.True(styleEnd > styleStart);
        var style = xaml[styleStart..styleEnd];

        Assert.Contains("BasedOn=\"{StaticResource SecondaryButtonStyle}\"", style, StringComparison.Ordinal);
        Assert.Contains("Property=\"MinHeight\" Value=\"26\"", style, StringComparison.Ordinal);
        Assert.Contains("Property=\"FontSize\" Value=\"11.5\"", style, StringComparison.Ordinal);
        Assert.Contains("Property=\"Padding\" Value=\"10,4\"", style, StringComparison.Ordinal);
        Assert.Contains("Property=\"Margin\" Value=\"0\"", style, StringComparison.Ordinal);
    }

    [Fact]
    public void UsbBuilder_PickItemsButton_IsTuckedBottomCenter_NotSharingStatusRow()
    {
        // The picker button sits on its own row, docked to the bottom-center of each
        // category card, so it never overlaps the status/summary wording (previously
        // it shared a 2-column Grid row with the status text and overlapped it in the
        // longer-labelled OS / Tool categories).
        var builder = ExtractTab(LoadMainWindowXaml(), "USB Builder");

        var buttonStart = builder.IndexOf("Style=\"{StaticResource UsbBuilderCategoryPickerButtonStyle}\"", StringComparison.Ordinal);
        Assert.True(buttonStart >= 0, "Pick items button should be present.");

        // Find the opening tag of the button and assert its layout attributes.
        var openTagStart = builder.LastIndexOf("<Button", buttonStart, StringComparison.Ordinal);
        var openTagEnd = builder.IndexOf("/>", buttonStart, StringComparison.Ordinal);
        Assert.True(openTagStart >= 0 && openTagEnd > openTagStart);
        var buttonTag = builder[openTagStart..openTagEnd];

        Assert.Contains("DockPanel.Dock=\"Bottom\"", buttonTag, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Center\"", buttonTag, StringComparison.Ordinal);
        Assert.Contains("Content=\"Pick items\"", buttonTag, StringComparison.Ordinal);

        // The card is a DockPanel now (button docked bottom) and the status summary no
        // longer lives in a two-column Grid shared with the button.
        Assert.Contains("<DockPanel LastChildFill=\"True\">", builder, StringComparison.Ordinal);
    }

    [Fact]
    public void CategoryBuilderWindow_ExposesRequiredPickerActionsAndRowFields()
    {
        var xamlPath = FindRepoFile("src", "ForgerEMS.Wpf", "CategoryBuilderWindow.xaml");
        var text = File.ReadAllText(xamlPath);

        foreach (var expected in new[]
        {
            "Title=\"{Binding WindowTitle}\"",
            "Select Recommended",
            "Select All",
            "Clear Optional",
            "Selected download:",
            "EstimatedSelectedDownloadSize",
            "USB space:",
            "EstimatedSelectedUsbSpace",
            "Apply",
            "Cancel",
            "TypeBadgeLabel",
            "SourceDisplay",
            "WarningText",
            "SpaceChipText",
            "IsSelected"
        })
        {
            Assert.Contains(expected, text, StringComparison.Ordinal);
        }

        Assert.Contains("Width=\"900\"", text, StringComparison.Ordinal);
        Assert.Contains("Height=\"680\"", text, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("FullManagedDownloadCommand", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SetupUsbCommand", text, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateUsbCommand", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PortUsbIntelligence_SummariesAndDiagnosticsRemainBound()
    {
        var intelligence = ExtractTab(LoadMainWindowXaml(), "Port / USB Intelligence");

        foreach (var binding in new[]
        {
            "UsbTargets",
            "RefreshUsbTargetsCommand",
            "OpenUsbMappingWizardCommand",
            "RunUsbIntelligenceBenchmarkCommand",
            "CancelUsbIntelligenceBenchmarkCommand",
            "OpenDriveValidatorWizardCommand",
            "UsbIntelligencePanelTargetDisplay",
            "UsbIntelligenceDetectedClassDisplay",
            "UsbIntelligenceMappingLabelDisplay",
            "UsbIntelligenceBestKnownPortDisplay",
            "UsbIntelligenceBenchmarkReadWriteDisplay",
            "UsbIntelligenceLastBenchmarkTimeDisplay",
            "UsbIntelligenceConfidenceReasonDisplay",
            "DriveValidatorQuickSummary",
            "DriveValidatorLastStatusDisplay",
            "DriveValidatorLastValidationAgeDisplay",
            "DriveValidatorResultSummary",
            "DriveValidatorBusPortDisplay",
            "PortIntelligenceOverviewText",
            "PortIntelligencePortMapSummaryText",
            "PortIntelligenceChargingSummaryText",
            "PortIntelligencePowerSourceSummaryText",
            "PortIntelligenceElevatedInventorySummaryText",
            "PortIntelligenceTelemetryLimitationsText",
            "PortPowerSummaryText",
            "PortPowerBatteryPercentDisplay",
            "PortPowerVoltageCurrentDisplay",
            "PortPowerTelemetryConfidenceDisplay",
            "SystemIntelligenceLastScanText",
            "CopySystemSummaryCommand",
            "OpenSystemIntelligenceFilesCommand"
        })
        {
            Assert.Contains(binding, intelligence, StringComparison.Ordinal);
        }

        // Retired deep-diagnostics surfaces must not come back to this tab.
        Assert.DoesNotContain("PortIntelligenceDeepScanSummaryText", intelligence, StringComparison.Ordinal);
        Assert.DoesNotContain("SystemIntelligenceSensorStackStatusText", intelligence, StringComparison.Ordinal);
    }

    [Fact]
    public void PortUsbIntelligence_UsesSafeCheckWordingOnly()
    {
        // v1.2.3-preview.1 safe-checks boundary: PC/laptop checks in this tab are limited
        // to battery health, system specs, and the local device/USB map. Broad hardware
        // diagnostics wording must not return.
        var intelligence = ExtractTab(LoadMainWindowXaml(), "Port / USB Intelligence");

        foreach (var forbidden in new[]
        {
            "deep scan",
            "PC diagnostics",
            "hardware stress",
            "stress test",
            "thermal prob",
            "fan prob",
            "sensor deep scan",
            "intensive diagnostic"
        })
        {
            Assert.DoesNotContain(forbidden, intelligence, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("Battery", intelligence, StringComparison.Ordinal);
        Assert.Contains("System specs", intelligence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Connected USB Devices", intelligence, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_KyraSidebarNavButton_DisplaysBeta()
    {
        var xamlPath = FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml");
        var text = File.ReadAllText(xamlPath);
        // The sidebar nav button must say "Kyra (Beta)" — not bare "Kyra".
        Assert.Contains("NavCopilotButton", text, StringComparison.Ordinal);
        Assert.Contains("Content=\"◇  Kyra (Beta)\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"◇  Kyra\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_KyraTabItem_HeaderDisplaysBeta()
    {
        var xamlPath = FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml");
        var text = File.ReadAllText(xamlPath);
        Assert.Contains("Header=\"◇  Kyra (Beta)\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"◇  Kyra\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_KyraPageGroupBoxHeader_DisplaysBeta()
    {
        var xamlPath = FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml");
        var text = File.ReadAllText(xamlPath);
        // The in-page GroupBox title next to the Kyra icon.
        Assert.Contains("Text=\"Kyra (Beta)\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_KyraSettingsGroupBox_HeaderDisplaysBeta()
    {
        var xamlPath = FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml");
        var text = File.ReadAllText(xamlPath);
        Assert.Contains("Header=\"Kyra Assistant (Beta)\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Kyra Assistant\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void KyraAdvancedSettingsWindow_TitleAndHeaderDisplayBeta()
    {
        var xamlPath = FindRepoFile("src", "ForgerEMS.Wpf", "KyraAdvancedSettingsWindow.xaml");
        var text = File.ReadAllText(xamlPath);
        Assert.Contains("Title=\"Kyra AI Settings (Beta)\"", text, StringComparison.Ordinal);
        Assert.Contains("Text=\"Kyra AI Settings (Beta)\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Title=\"Kyra AI Settings\"", text, StringComparison.Ordinal);
    }
}
