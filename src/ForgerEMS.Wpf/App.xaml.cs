using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.ViewModels;

namespace VentoyToolkitSetup.Wpf;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var runtimeService = new AppRuntimeService();
        runtimeService.EnsureInitialized();

        var backendDiscoveryService = new BackendDiscoveryService();
        var powerShellRunnerService = new PowerShellRunnerService();
        var usbDetectionService = new UsbDetectionService(powerShellRunnerService);
        var managedDownloadSummaryService = new ManagedDownloadSummaryService();
        var scriptStatusParser = new ScriptStatusParser();
        var userPromptService = new UserPromptService();
        var ventoyIntegrationService = new VentoyIntegrationService(powerShellRunnerService, runtimeService);

        if (HasArgument(e.Args, "--self-test"))
        {
            var exitCode = await RunSelfTestAsync(runtimeService, backendDiscoveryService, powerShellRunnerService);
            Shutdown(exitCode);
            return;
        }

        var mainViewModel = new MainViewModel(
            backendDiscoveryService,
            powerShellRunnerService,
            usbDetectionService,
            managedDownloadSummaryService,
            scriptStatusParser,
            userPromptService,
            ventoyIntegrationService,
            runtimeService);

        var mainWindow = new MainWindow(mainViewModel);
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private static bool HasArgument(IEnumerable<string> args, string target)
    {
        return args.Any(arg => string.Equals(arg, target, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<int> RunSelfTestAsync(
        IAppRuntimeService runtimeService,
        IBackendDiscoveryService backendDiscoveryService,
        IPowerShellRunnerService powerShellRunnerService)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var lines = new List<string>
        {
            "ForgerEMS self-test",
            $"StartedUtc: {startedUtc:O}",
            $"ExecutableBase: {AppContext.BaseDirectory}",
            $"CurrentDirectory: {Directory.GetCurrentDirectory()}",
            $"RuntimeRoot: {runtimeService.RuntimeRoot}",
            $"SessionLogPath: {runtimeService.SessionLogPath}"
        };

        try
        {
            var backendContext = backendDiscoveryService.Discover();
            lines.Add($"BackendAvailable: {backendContext.IsAvailable}");
            lines.Add($"BackendMode: {backendContext.ModeLabel}");
            lines.Add($"BackendRoot: {backendContext.RootPath}");
            lines.Add($"BackendVersion: {backendContext.BackendVersion}");
            lines.Add($"BackendDiagnostic: {backendContext.DiagnosticMessage}");

            var request = new PowerShellRunRequest
            {
                DisplayName = "Published self-test",
                WorkingDirectory = Directory.GetCurrentDirectory(),
                InlineCommand = "$PSVersionTable.PSVersion.ToString()"
            };

            var result = await powerShellRunnerService.RunAsync(request).ConfigureAwait(false);
            lines.Add($"PowerShellExitCode: {result.ExitCode}");
            lines.Add($"PowerShellSucceeded: {result.Succeeded}");
            lines.Add($"PowerShellVersion: {result.StandardOutputText.Trim()}");
            lines.Add($"FinishedUtc: {DateTimeOffset.UtcNow:O}");

            var reportPath = runtimeService.WriteDiagnosticReport("published-self-test.txt", lines);
            return result.Succeeded && File.Exists(reportPath) ? 0 : 1;
        }
        catch (Exception exception)
        {
            lines.Add($"PowerShellSucceeded: False");
            lines.Add($"Error: {exception.Message}");
            lines.Add($"FinishedUtc: {DateTimeOffset.UtcNow:O}");
            runtimeService.WriteDiagnosticReport("published-self-test.txt", lines);
            return 1;
        }
    }
}
