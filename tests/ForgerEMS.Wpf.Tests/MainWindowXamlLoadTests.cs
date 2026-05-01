using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using VentoyToolkitSetup.Wpf;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.ViewModels;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Ensures MainWindow XAML resolves all StaticResource keys (regression for style declaration order)
/// and constructs without binding errors (e.g. read-only header properties must use OneWay bindings).
/// </summary>
public sealed class MainWindowXamlLoadTests
{
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
                    copilotProviderRegistry);

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
}
