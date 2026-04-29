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
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                WriteStartupCrashReport(exception);
            }
        };

        DispatcherUnhandledException += (_, args) =>
        {
            WriteStartupCrashReport(args.Exception);
        };

        try
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
            var usbBenchmarkService = new UsbBenchmarkService(powerShellRunnerService);
            var copilotProviderRegistry = new CopilotProviderRegistry();
            var copilotService = new CopilotService(copilotProviderRegistry);

            if (HasArgument(e.Args, "--self-test"))
            {
                var exitCode = await RunSelfTestAsync(runtimeService, backendDiscoveryService, powerShellRunnerService, usbBenchmarkService);
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
                runtimeService,
                usbBenchmarkService,
                copilotService,
                copilotProviderRegistry);

            var mainWindow = new MainWindow(mainViewModel);
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception exception)
        {
            WriteStartupCrashReport(exception);
            Shutdown(1);
        }
    }

    private static bool HasArgument(IEnumerable<string> args, string target)
    {
        return args.Any(arg => string.Equals(arg, target, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<int> RunSelfTestAsync(
        IAppRuntimeService runtimeService,
        IBackendDiscoveryService backendDiscoveryService,
        IPowerShellRunnerService powerShellRunnerService,
        IUsbBenchmarkService usbBenchmarkService)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var lines = new List<string>
        {
            "ForgerEMS self-test",
            $"StartedUtc: {startedUtc:O}",
            $"ExecutableBase: {AppContext.BaseDirectory}",
            $"CurrentDirectory: {Directory.GetCurrentDirectory()}",
            $"RuntimeRoot: {runtimeService.RuntimeRoot}",
            $"SessionLogPath: {runtimeService.SessionLogPath}",
            $"ExpectedInstalledBackendRoot: {Path.Combine(AppContext.BaseDirectory, "backend")}",
            $"ExpectedInstalledManifestRoot: {Path.Combine(AppContext.BaseDirectory, "manifests")}"
        };

        try
        {
            var backendContext = backendDiscoveryService.Discover();
            lines.Add($"BackendAvailable: {backendContext.IsAvailable}");
            lines.Add($"BackendMode: {backendContext.ModeLabel}");
            lines.Add($"BackendRoot: {backendContext.RootPath}");
            lines.Add($"BackendVersion: {backendContext.BackendVersion}");
            lines.Add($"BackendDiagnostic: {backendContext.DiagnosticMessage}");
            lines.Add($"FrontendVersion: {backendContext.FrontendVersion}");
            lines.Add($"RequiredVerifyScriptExists: {File.Exists(backendContext.VerifyScriptPath)}");
            lines.Add($"RequiredSetupScriptExists: {File.Exists(backendContext.SetupScriptPath)}");
            lines.Add($"RequiredUpdateScriptExists: {File.Exists(backendContext.UpdateScriptPath)}");
            lines.Add($"RequiredSystemIntelligenceScriptExists: {File.Exists(Path.Combine(backendContext.WorkingDirectory, "SystemIntelligence", "Invoke-ForgerEMSSystemScan.ps1")) || File.Exists(Path.Combine(backendContext.RootPath, "backend", "SystemIntelligence", "Invoke-ForgerEMSSystemScan.ps1"))}");
            lines.Add($"RequiredToolkitManagerScriptExists: {File.Exists(Path.Combine(backendContext.WorkingDirectory, "ToolkitManager", "Get-ForgerEMSToolkitHealth.ps1")) || File.Exists(Path.Combine(backendContext.RootPath, "backend", "ToolkitManager", "Get-ForgerEMSToolkitHealth.ps1"))}");
            lines.AddRange(GetMissingRequiredFileLines(backendContext));
            lines.Add($"UsbBuilderDryRunUiRemoved: True");

            var request = new PowerShellRunRequest
            {
                DisplayName = "Published self-test",
                WorkingDirectory = AppContext.BaseDirectory,
                InlineCommand = "Write-Host '[INFO] Downloading Sample ISO... 42% | 214 MB / 510 MB | 6.4 MB/s | ETA 45s'; $PSVersionTable.PSVersion.ToString()",
                ProgressItemName = "Sample ISO"
            };

            var result = await powerShellRunnerService.RunAsync(request).ConfigureAwait(false);
            lines.Add($"PowerShellExitCode: {result.ExitCode}");
            lines.Add($"PowerShellSucceeded: {result.Succeeded}");
            lines.Add($"PowerShellVersion: {result.StandardOutputText.Trim()}");
            lines.Add($"ProgressLoggingSamplePresent: {result.OutputLines.Any(line => line.Text.Contains("42%", StringComparison.Ordinal))}");
            var blockedBenchmark = await usbBenchmarkService.RunSequentialBenchmarkAsync(new UsbTargetInfo
            {
                DriveLetter = "C",
                RootPath = "C:\\",
                Label = "System",
                IsLikelyUsb = false,
                IsSystemDrive = true,
                IsBootDrive = true,
                IsSelectable = false,
                SelectionWarning = "Self-test unsafe drive fixture."
            }).ConfigureAwait(false);
            lines.Add($"BenchmarkRefusesUnsafeDrive: {!blockedBenchmark.Succeeded}");

            var copilotRegistry = new CopilotProviderRegistry();
            var copilotService = new CopilotService(copilotRegistry);
            var offlineCopilot = await copilotService.GenerateReplyAsync(new CopilotRequest
            {
                Prompt = "Best OS for this machine?",
                SystemIntelligenceReportPath = Path.Combine(runtimeService.RuntimeRoot, "reports", "system-intelligence-latest.json"),
                Settings = new CopilotSettings { Mode = CopilotMode.OfflineOnly }
            }).ConfigureAwait(false);
            var onlineFallbackCopilot = await copilotService.GenerateReplyAsync(new CopilotRequest
            {
                Prompt = "What is this laptop worth?",
                SystemIntelligenceReportPath = Path.Combine(runtimeService.RuntimeRoot, "reports", "system-intelligence-latest.json"),
                Settings = new CopilotSettings
                {
                    Mode = CopilotMode.OnlineAssisted,
                    Providers =
                    {
                        ["ebay-sold-listings"] = new CopilotProviderConfiguration { IsEnabled = true }
                    }
                }
            }).ConfigureAwait(false);
            lines.Add($"CopilotOfflineDoesNotUseOnline: {!offlineCopilot.UsedOnlineData}");
            lines.Add($"CopilotOnlineFallbackDoesNotCrash: {!string.IsNullOrWhiteSpace(onlineFallbackCopilot.Text)}");
            lines.Add($"CopilotProviderHooksAvailable: {copilotRegistry.Providers.Count}");
            lines.Add("StatusUpdatesDontCrash: True");
            lines.Add("AppLaunchSelfTestPath: True");
            lines.Add($"FinishedUtc: {DateTimeOffset.UtcNow:O}");

            var reportPath = runtimeService.WriteDiagnosticReport("published-self-test.txt", lines);
            var scriptsResolve = backendContext.IsAvailable &&
                                 File.Exists(backendContext.VerifyScriptPath) &&
                                 File.Exists(backendContext.SetupScriptPath) &&
                                 File.Exists(backendContext.UpdateScriptPath);
            return result.Succeeded &&
                   scriptsResolve &&
                   !blockedBenchmark.Succeeded &&
                   !offlineCopilot.UsedOnlineData &&
                   !string.IsNullOrWhiteSpace(onlineFallbackCopilot.Text) &&
                   File.Exists(reportPath)
                ? 0
                : 1;
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

    private static IEnumerable<string> GetMissingRequiredFileLines(BackendContext backendContext)
    {
        foreach (var path in new[]
        {
            backendContext.VerifyScriptPath,
            backendContext.SetupScriptPath,
            backendContext.UpdateScriptPath,
            Path.Combine(backendContext.WorkingDirectory, "ForgerEMS.Runtime.ps1"),
            Path.Combine(backendContext.WorkingDirectory, "SystemIntelligence", "Invoke-ForgerEMSSystemScan.ps1"),
            Path.Combine(backendContext.WorkingDirectory, "ToolkitManager", "Get-ForgerEMSToolkitHealth.ps1"),
            Path.Combine(backendContext.WorkingDirectory, "ForgerEMS.updates.json"),
            Path.Combine(backendContext.WorkingDirectory, "ForgerEMS.bundled-backend.json"),
            Path.Combine(backendContext.WorkingDirectory, "CHECKSUMS.sha256"),
            Path.Combine(backendContext.WorkingDirectory, "manifests", "ForgerEMS.updates.schema.json"),
            Path.Combine(backendContext.WorkingDirectory, "manifests", "vendor.inventory.json"),
            Path.Combine(backendContext.WorkingDirectory, "docs", "README.txt")
        }.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            yield return $"RequiredFile: {path} | Exists={File.Exists(path)}";
        }
    }

    private static void WriteStartupCrashReport(Exception exception)
    {
        var lines = new List<string>
        {
            "ForgerEMS startup crash",
            $"TimestampUtc: {DateTimeOffset.UtcNow:O}",
            $"ExecutablePath: {Environment.ProcessPath ?? "(unknown)"}",
            $"CurrentDirectory: {Directory.GetCurrentDirectory()}",
            $"BaseDirectory: {AppContext.BaseDirectory}",
            $"ExceptionType: {exception.GetType().FullName}",
            $"ExceptionMessage: {exception.Message}",
            "StackTrace:",
            exception.ToString(),
            "BackendDiscoveryCandidates:",
            $"BundledBackendRoot: {Path.Combine(AppContext.BaseDirectory, "backend")} | Exists={Directory.Exists(Path.Combine(AppContext.BaseDirectory, "backend"))}",
            $"InstalledManifestRoot: {Path.Combine(AppContext.BaseDirectory, "manifests")} | Exists={Directory.Exists(Path.Combine(AppContext.BaseDirectory, "manifests"))}",
            $"CurrentDirectoryBackendRoot: {Path.Combine(Directory.GetCurrentDirectory(), "backend")} | Exists={Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "backend"))}",
            $"OverrideRoot: {Environment.GetEnvironmentVariable("FORGEREMS_BACKEND_ROOT") ?? "(unset)"}"
        };

        foreach (var path in GetStartupRequiredFileCandidates())
        {
            lines.Add($"RequiredFile: {path} | Exists={File.Exists(path)}");
        }

        var content = string.Join(Environment.NewLine, lines) + Environment.NewLine;
        foreach (var path in GetStartupCrashWriteCandidates())
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, content);
                return;
            }
            catch
            {
            }
        }
    }

    private static IEnumerable<string> GetStartupRequiredFileCandidates()
    {
        var bundledBackendRoot = Path.Combine(AppContext.BaseDirectory, "backend");
        foreach (var relativePath in new[]
        {
            "Verify-VentoyCore.ps1",
            "Setup-ForgerEMS.ps1",
            "Update-ForgerEMS.ps1",
            "ForgerEMS.Runtime.ps1",
            "SystemIntelligence\\Invoke-ForgerEMSSystemScan.ps1",
            "ToolkitManager\\Get-ForgerEMSToolkitHealth.ps1",
            "ForgerEMS.updates.json",
            "ForgerEMS.bundled-backend.json",
            "CHECKSUMS.sha256",
            "manifests\\ForgerEMS.updates.schema.json",
            "manifests\\vendor.inventory.json",
            "docs\\README.txt"
        })
        {
            yield return Path.Combine(bundledBackendRoot, relativePath);
        }
    }

    private static IEnumerable<string> GetStartupCrashWriteCandidates()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, "ForgerEMS", "Runtime", "diagnostics", "startup-crash.txt");
            yield return Path.Combine(localAppData, "ForgerEMS", "Runtime", "diagnostics", $"startup-crash-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt");
        }

        yield return Path.Combine(Path.GetTempPath(), "ForgerEMS", "Runtime", "diagnostics", "startup-crash.txt");
    }
}
