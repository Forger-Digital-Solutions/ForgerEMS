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
        Assert.Contains("AppUpdateCheckHelperText", settings, StringComparison.Ordinal);
        Assert.Contains("Copy Update Diagnostics", settings, StringComparison.Ordinal);
        Assert.Contains("Copies a safe summary for support sharing.", settings, StringComparison.Ordinal);
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
}
