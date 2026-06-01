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
    public void MainWindow_SystemIntelligenceTab_UsesThreePrimaryActionButtons()
    {
        var xamlPath = FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml");
        var text = File.ReadAllText(xamlPath);
        var tabStart = text.IndexOf("<TabItem Header=\"◎  System Intelligence\">", StringComparison.Ordinal);
        Assert.True(tabStart >= 0);
        var tabEnd = text.IndexOf("<TabItem Header=\"▤  Toolkit Manager\">", tabStart, StringComparison.Ordinal);
        Assert.True(tabEnd > tabStart);
        var systemIntelligence = text[tabStart..tabEnd];

        Assert.Contains("Content=\"Elevated Scan\"", systemIntelligence, StringComparison.Ordinal);
        Assert.Contains("Content=\"Open Files\"", systemIntelligence, StringComparison.Ordinal);
        Assert.Contains("Content=\"Create Support Bundle\"", systemIntelligence, StringComparison.Ordinal);
        Assert.Contains("RunElevatedSystemScanCommand", systemIntelligence, StringComparison.Ordinal);
        Assert.Contains("OpenSystemIntelligenceFilesCommand", systemIntelligence, StringComparison.Ordinal);
        Assert.Contains("ExportSupportBundleCommand", systemIntelligence, StringComparison.Ordinal);
        Assert.Contains("SystemIntelligenceScanModeHintText", systemIntelligence, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Run Standard Scan\"", systemIntelligence, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Run Elevated Scan\"", systemIntelligence, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Restart as Administrator\"", systemIntelligence, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Copy Quick Summary\"", systemIntelligence, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Open JSON Report\"", systemIntelligence, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Open Markdown Report\"", systemIntelligence, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Refresh Results\"", systemIntelligence, StringComparison.Ordinal);
        Assert.DoesNotContain("CopyElevatedScanAdminCommand", systemIntelligence, StringComparison.Ordinal);
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
    public void MainWindow_NetworkPulseInternetWidget_IsInsideRightStatusGrid_NotFullHeaderRow()
    {
        var xamlPath = FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml");
        var text = File.ReadAllText(xamlPath);
        Assert.Contains("HeaderRightStatusGrid", text, StringComparison.Ordinal);
        Assert.Contains("NetworkPulseHeaderCompactIsland", text, StringComparison.Ordinal);
        Assert.Contains("NetworkPulseFlyoutTarget", text, StringComparison.Ordinal);
        Assert.Contains("HeaderLeftSupportCopyGroup", text, StringComparison.Ordinal);

        var rightGrid = text.IndexOf("HeaderRightStatusGrid", StringComparison.Ordinal);
        var compactIsland = text.IndexOf("NetworkPulseHeaderCompactIsland", StringComparison.Ordinal);
        var flyout = text.IndexOf("NetworkPulseFlyoutTarget", StringComparison.Ordinal);
        Assert.True(rightGrid >= 0 && compactIsland > rightGrid && flyout > compactIsland);

        var rightOpenEnd = text.IndexOf('>', rightGrid);
        var rightOpenTag = text[rightGrid..rightOpenEnd];
        Assert.Contains("Grid.Column=\"1\"", rightOpenTag, StringComparison.Ordinal);
        Assert.Contains("InternetWidgetLine1", text, StringComparison.Ordinal);
        Assert.Contains("InternetWidgetLine2", text, StringComparison.Ordinal);
        Assert.Contains("NetworkPulse.InternetWidgetLine3", text, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Right\"", text, StringComparison.Ordinal);
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
    public void MainWindow_NetworkPulseHeaderCompactIsland_HasNoBulkyMinSizeOrPadding()
    {
        var xamlPath = FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml");
        var text = File.ReadAllText(xamlPath);
        var start = text.IndexOf("NetworkPulseHeaderCompactIsland", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = text.IndexOf("NetworkPulseFlyoutTarget", start, StringComparison.Ordinal);
        Assert.True(end > start);
        var header = text[start..end];
        Assert.DoesNotContain("MinHeight", header, StringComparison.Ordinal);
        Assert.DoesNotContain("MinWidth", header, StringComparison.Ordinal);
        Assert.DoesNotContain("Padding=\"10,8\"", header, StringComparison.Ordinal);
        Assert.Contains("Padding=\"5,2\"", header, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_NetworkPulseCompactIsland_DoesNotUseColumnSpanFourUnderHeader()
    {
        var xamlPath = FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml");
        var text = File.ReadAllText(xamlPath);
        var start = text.IndexOf("NetworkPulseHeaderCompactIsland", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var openEnd = text.IndexOf('>', start);
        Assert.True(openEnd > start);
        var openTag = text[start..openEnd];
        Assert.DoesNotContain("ColumnSpan=\"4\"", openTag, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_SettingsTabUsesCompactSafetyAndUpdateWording()
    {
        var xamlPath = FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml");
        var text = File.ReadAllText(xamlPath);
        var settingsStart = text.IndexOf("<TabItem Header=\"☰  Settings\">", StringComparison.Ordinal);
        Assert.True(settingsStart >= 0);
        var settings = text[settingsStart..];

        Assert.Contains("System Intelligence sensors / Deep Sensor Mode", settings, StringComparison.Ordinal);
        Assert.Contains("Mode: Off / Read-only local sensors", settings, StringComparison.Ordinal);
        Assert.Contains("Safety: read-only", settings, StringComparison.Ordinal);
        Assert.Contains("No fan, voltage, clock, BIOS, or firmware control.", settings, StringComparison.Ordinal);
        Assert.Contains("Keep Local Only", settings, StringComparison.Ordinal);
        Assert.Contains("Help Improve Kyra", settings, StringComparison.Ordinal);
        Assert.Contains("Learn More", settings, StringComparison.Ordinal);
        Assert.Contains("View Shared Preview", settings, StringComparison.Ordinal);
        Assert.Contains("Export Memory", settings, StringComparison.Ordinal);
        Assert.Contains("Delete Memory", settings, StringComparison.Ordinal);
        Assert.Contains("Reset Learning", settings, StringComparison.Ordinal);
        Assert.Contains("AppUpdateCheckButtonText", settings, StringComparison.Ordinal);
        Assert.Contains("Network Pulse", settings, StringComparison.Ordinal);
        Assert.Contains("Lightweight", settings, StringComparison.Ordinal);
        Assert.Contains("AppUpdateCheckHelperText", settings, StringComparison.Ordinal);
        Assert.Contains("Copy Update Diagnostics", settings, StringComparison.Ordinal);
        Assert.Contains("Copies a safe summary for support sharing.", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("Check Now", settings, StringComparison.Ordinal);
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

    [Fact]
    public void MainWindow_DriveValidatorProgressBar_IsRemovedFromUsbBuilderTab()
    {
        // The Drive Validator card on the USB Builder tab is now a compact summary only — the
        // inline ProgressBar bound to DriveValidatorProgressValue was moved into the Drive
        // Validator Wizard (where the validation actually runs). This test guards against the
        // inline progress bar being reintroduced into the main tab; the wizard's own
        // RunningProgressValue ProgressBar lives in DriveValidatorWizardWindow.xaml and is
        // unaffected.
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
    public void UsbBuilder_DriveValidatorCard_IsCompactAndDelegatesToWizard()
    {
        // The Drive Validator card on the USB Builder tab is intentionally compact: header
        // + status pill, target / last check / result key-value lines, and a single
        // "Open Drive Validator" button. Validation mode dropdown, Start/Cancel buttons,
        // inline ProgressBar, phase text, and evidence expander were moved into the Drive
        // Validator Wizard. This test fails if anyone reintroduces those heavy controls.
        var xamlPath = FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml");
        var text = File.ReadAllText(xamlPath);

        var cardStart = text.IndexOf("UsbBuilderDriveValidatorCompactCard", StringComparison.Ordinal);
        Assert.True(cardStart >= 0, "Compact Drive Validator card (x:Name) must exist in MainWindow.xaml.");
        var cardEnd = text.IndexOf("</GroupBox>", cardStart, StringComparison.Ordinal);
        Assert.True(cardEnd > cardStart);
        var card = text[cardStart..cardEnd];

        // Compact summary bindings.
        Assert.Contains("DriveValidatorQuickSummary", card, StringComparison.Ordinal);
        Assert.Contains("DriveValidatorLastStatusDisplay", card, StringComparison.Ordinal);
        Assert.Contains("DriveValidatorLastValidationAgeDisplay", card, StringComparison.Ordinal);
        Assert.Contains("DriveValidatorTargetDisplay", card, StringComparison.Ordinal);
        Assert.Contains("DriveValidatorResultSummary", card, StringComparison.Ordinal);

        // Single primary action: opens the wizard.
        Assert.Contains("Open Drive Validator", card, StringComparison.Ordinal);
        Assert.Contains("OpenDriveValidatorWizardCommand", card, StringComparison.Ordinal);

        // Heavy controls must NOT live inline in the USB Builder card any more.
        Assert.DoesNotContain("RunDriveValidatorCommand", card, StringComparison.Ordinal);
        Assert.DoesNotContain("CancelDriveValidatorCommand", card, StringComparison.Ordinal);
        Assert.DoesNotContain("DriveValidatorModeIndex", card, StringComparison.Ordinal);
        Assert.DoesNotContain("DriveValidatorProgressValue", card, StringComparison.Ordinal);
        Assert.DoesNotContain("DriveValidatorEvidenceDisplay", card, StringComparison.Ordinal);
        Assert.DoesNotContain("DriveValidatorPhaseDisplay", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Start validation", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Quick Safe Check", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Sampled Capacity Check", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Full Free-Space Validation", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Evidence / details", card, StringComparison.Ordinal);
    }

    [Fact]
    public void UsbBuilder_PortIntelligenceCard_ComposesUsbAndPowerAndDelegatesHeavyActions()
    {
        // The Port Intelligence card keeps USB mapping and charging summary copy together,
        // while still delegating heavy benchmark/mapping workflows to the wizard.
        var xamlPath = FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml");
        var text = File.ReadAllText(xamlPath);

        var cardStart = text.IndexOf("UsbBuilderUsbIntelligenceCompactCard", StringComparison.Ordinal);
        Assert.True(cardStart >= 0, "Compact USB Intelligence card (x:Name) must exist in MainWindow.xaml.");
        var cardEnd = text.IndexOf("</GroupBox>", cardStart, StringComparison.Ordinal);
        Assert.True(cardEnd > cardStart);
        var card = text[cardStart..cardEnd];

        // Compact summary bindings.
        Assert.Contains("UsbIntelligencePanelTargetDisplay", card, StringComparison.Ordinal);
        Assert.Contains("UsbIntelligenceDetectedClassDisplay", card, StringComparison.Ordinal);
        Assert.Contains("UsbIntelligenceBenchmarkReadWriteDisplay", card, StringComparison.Ordinal);
        Assert.Contains("UsbIntelligenceConfidenceScoreDisplay", card, StringComparison.Ordinal);
        Assert.Contains("UsbIntelligenceRecommendationQualityDisplay", card, StringComparison.Ordinal);
        Assert.Contains("UsbIntelligenceMappingLabelDisplay", card, StringComparison.Ordinal);
        Assert.Contains("PortIntelligenceOverviewText", card, StringComparison.Ordinal);
        Assert.Contains("PortIntelligencePortMapSummaryText", card, StringComparison.Ordinal);
        Assert.Contains("PortIntelligenceChargingSummaryText", card, StringComparison.Ordinal);
        Assert.Contains("PortIntelligencePowerSourceSummaryText", card, StringComparison.Ordinal);
        Assert.Contains("PortIntelligenceBottlenecksText", card, StringComparison.Ordinal);
        Assert.Contains("PortIntelligenceRecommendedFixesText", card, StringComparison.Ordinal);
        Assert.Contains("PortIntelligenceDeepScanSummaryText", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Charging Intelligence\"", text, StringComparison.Ordinal);

        // Single primary action: opens the wizard.
        Assert.Contains("Open USB Mapping Wizard", card, StringComparison.Ordinal);
        Assert.Contains("OpenUsbMappingWizardCommand", card, StringComparison.Ordinal);

        // Heavy controls must NOT live inline in the USB Builder card any more.
        Assert.DoesNotContain("RunUsbIntelligenceBenchmarkCommand", card, StringComparison.Ordinal);
        Assert.DoesNotContain("CancelUsbIntelligenceBenchmarkCommand", card, StringComparison.Ordinal);
        Assert.DoesNotContain("StartUsbPortMappingWorkflowCommand", card, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureUsbMappingBeforeCommand", card, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureUsbMappingAfterCommand", card, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveUsbMappingLabelCommand", card, StringComparison.Ordinal);
        Assert.DoesNotContain("UsbMappingLabelDraft", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Run USB Benchmark", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Cancel Benchmark", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Advanced: inline port mapping", card, StringComparison.Ordinal);
    }

    [Fact]
    public void UsbBuilder_CardsRemainBounded_NoLargeInlineExpanders()
    {
        // The USB Builder cards must stay compact — neither the Drive Validator nor the USB
        // Intelligence card should contain an <Expander> (those belonged to the old heavy
        // inline layout and force users to scroll past large technical blocks to reach the
        // Ventoy and build controls below).
        var xamlPath = FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml");
        var text = File.ReadAllText(xamlPath);

        var dvStart = text.IndexOf("UsbBuilderDriveValidatorCompactCard", StringComparison.Ordinal);
        var dvEnd = text.IndexOf("</GroupBox>", dvStart, StringComparison.Ordinal);
        var dv = text[dvStart..dvEnd];
        Assert.DoesNotContain("<Expander", dv, StringComparison.Ordinal);

        var uiStart = text.IndexOf("UsbBuilderUsbIntelligenceCompactCard", StringComparison.Ordinal);
        var uiEnd = text.IndexOf("</GroupBox>", uiStart, StringComparison.Ordinal);
        var ui = text[uiStart..uiEnd];
        Assert.DoesNotContain("<Expander", ui, StringComparison.Ordinal);
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
        Assert.Contains("Header=\"Kyra Intelligence (Beta)\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Kyra Intelligence\"", text, StringComparison.Ordinal);
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
