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
        Assert.Contains("Run Standard Scan", text, StringComparison.Ordinal);
        Assert.Contains("Refresh Results", text, StringComparison.Ordinal);
        Assert.Contains("Create Support Bundle", text, StringComparison.Ordinal);
        Assert.Contains("Copy Update Diagnostics", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Run Elevated Scan for more detail", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Copy update-check diagnostics (safe summary)", text, StringComparison.Ordinal);
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

        Assert.Contains("Grid.Column=\"4\"", text, StringComparison.Ordinal);
        Assert.Contains("Grid.ColumnSpan=\"3\"", text, StringComparison.Ordinal);
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
        Assert.Contains("Grid.Row=\"1\"", supportOpenTag, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"0\"", supportOpenTag, StringComparison.Ordinal);
        Assert.Contains("Grid.ColumnSpan=\"3\"", supportOpenTag, StringComparison.Ordinal);
        Assert.DoesNotContain("Grid.ColumnSpan=\"7\"", supportOpenTag, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment=\"Top\"", supportOpenTag, StringComparison.Ordinal);

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
        Assert.Contains("Grid.RowSpan=\"2\"", rightOpenTag, StringComparison.Ordinal);
        Assert.DoesNotContain("Grid.RowSpan=\"3\"", rightOpenTag, StringComparison.Ordinal);

        var headerStart = text.IndexOf("<Border Grid.Row=\"0\"", StringComparison.Ordinal);
        Assert.True(headerStart >= 0);
        var headerOpenEnd = text.IndexOf('>', headerStart);
        var headerOpenTag = text[headerStart..headerOpenEnd];
        Assert.Contains("Padding=\"12,8\"", headerOpenTag, StringComparison.Ordinal);
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
        // MaxHeight="56" was removed — the widget auto-sizes so Line 2 is never clipped.
        Assert.DoesNotContain("MaxHeight=\"56\"", header, StringComparison.Ordinal);
        // Compact padding: enough for the third freshness line without bloating the header.
        Assert.Contains("Padding=\"6,3\"", header, StringComparison.Ordinal);
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
    public void MainWindow_DriveValidatorProgressBar_IsBoundOneWay()
    {
        // RangeBase.Value (ProgressBar.Value, Slider.Value, …) binds TwoWay by default. Binding it to a
        // read-only ViewModel property (e.g. `private set`) causes WPF to throw at app startup with:
        //   "A TwoWay or OneWayToSource binding cannot work on the read-only property
        //    'DriveValidatorProgressValue' of type '…MainViewModel'."
        // The DriveValidatorProgressValue setter is intentionally private (display-only progress state),
        // so the XAML binding MUST set Mode=OneWay. This test fails if anyone reverts that.
        var xamlPath = FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml");
        var text = File.ReadAllText(xamlPath);

        Assert.Contains("DriveValidatorProgressValue", text, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Value=\"{Binding DriveValidatorProgressValue}\"",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "Value=\"{Binding DriveValidatorProgressValue, Mode=OneWay}\"",
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
