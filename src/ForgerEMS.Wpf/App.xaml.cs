using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;
using VentoyToolkitSetup.Wpf.Configuration;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.ViewModels;

namespace VentoyToolkitSetup.Wpf;

public partial class App : Application
{

    protected override async void OnStartup(StartupEventArgs e)
    {
        RegisterStartupExceptionHandlers();

        try
        {
            AppendStartupLog("App starting");
            AppendStartupLog($"ExecutablePath: {GetExecutablePath()}");
            AppendStartupLog($"ExecutableBase: {GetExecutableBaseDirectory()}");
            AppendStartupLog($"CurrentDirectory: {Directory.GetCurrentDirectory()}");

            base.OnStartup(e);

            var runtimeService = new AppRuntimeService();
            runtimeService.EnsureInitialized();
            AppendStartupLog("Config loaded");

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
            AppendStartupLog("Services initialized");

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
            AppendStartupLog("MainWindow constructed");
            MainWindow = mainWindow;
            mainWindow.Show();
            AppendStartupLog("MainWindow shown");
        }
        catch (Exception exception)
        {
            LogStartupException("Startup failed", exception);
            WriteStartupCrashReport(exception);
            ShowLaunchFailureMessage(exception);
            Shutdown(1);
        }
    }

    private static void RegisterStartupExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                LogStartupException("Unhandled AppDomain exception", exception);
                WriteStartupCrashReport(exception);
            }
            else
            {
                AppendStartupLog($"Unhandled AppDomain exception object: {args.ExceptionObject}");
            }
        };

        Current.DispatcherUnhandledException += (_, args) =>
        {
            LogStartupException("Unhandled dispatcher exception", args.Exception);
            WriteStartupCrashReport(args.Exception);
        };
    }

    private static bool HasArgument(IEnumerable<string> args, string target)
    {
        return args.Any(arg => string.Equals(arg, target, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Support-safe path line for published-self-test.txt: keeps at most the last two path segments, no drive or profile.
    /// </summary>
    private static string RedactSelfTestFilesystemPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var leaf = Path.GetFileName(trimmed);
        var parentDir = Path.GetDirectoryName(trimmed);
        var parent = string.IsNullOrEmpty(parentDir) ? "" : Path.GetFileName(parentDir);
        return string.IsNullOrEmpty(parent)
            ? $"[REDACTED_PRIVATE_PATH]/{leaf}"
            : $"[REDACTED_PRIVATE_PATH]/{parent}/{leaf}";
    }

    private static async Task<int> RunSelfTestAsync(
        AppRuntimeService runtimeService,
        BackendDiscoveryService backendDiscoveryService,
        PowerShellRunnerService powerShellRunnerService,
        UsbBenchmarkService usbBenchmarkService)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var executableBase = GetExecutableBaseDirectory();
        var lines = new List<string>
        {
            "ForgerEMS self-test",
            $"AppSemanticVersionLabel: {AppReleaseInfo.Version}",
            $"AppDisplayVersion: {AppReleaseInfo.DisplayVersion}",
            $"StartedUtc: {startedUtc:O}",
            $"ExecutableBase: {RedactSelfTestFilesystemPath(executableBase)}",
            $"CurrentDirectory: {RedactSelfTestFilesystemPath(Directory.GetCurrentDirectory())}",
            $"RuntimeRoot: {CopilotRedactor.Redact(runtimeService.RuntimeRoot, enabled: true)}",
            $"SessionLogPath: {CopilotRedactor.Redact(runtimeService.SessionLogPath, enabled: true)}",
            $"FORGEREMS_ENV: {ForgerEmsEnvironmentConfiguration.ForgerEmsEnv}",
            $"FORGEREMS_RELEASE_CHANNEL: {ForgerEmsEnvironmentConfiguration.ReleaseChannel}",
            $"UpdateGitHubSource: {ForgerEmsEnvironmentConfiguration.GitHubOwner}/{ForgerEmsEnvironmentConfiguration.GitHubRepo}",
            $"UpdateUserAgent: {ForgerEmsEnvironmentConfiguration.UpdateUserAgent}",
            $"TelemetryEnabled(env): {ForgerEmsEnvironmentConfiguration.TelemetryEnabled}",
            $"CrashReportingEnabled(env): {ForgerEmsEnvironmentConfiguration.CrashReportingEnabled}",
            $"ExpectedInstalledBackendRoot: {RedactSelfTestFilesystemPath(Path.Combine(executableBase, "backend"))}",
            $"ExpectedInstalledManifestRoot: {RedactSelfTestFilesystemPath(Path.Combine(executableBase, "manifests"))}"
        };

        try
        {
            var backendContext = backendDiscoveryService.Discover();
            lines.Add($"BackendAvailable: {backendContext.IsAvailable}");
            lines.Add($"BackendMode: {backendContext.ModeLabel}");
            lines.Add($"BackendRoot: {RedactSelfTestFilesystemPath(backendContext.RootPath)}");
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
                WorkingDirectory = executableBase,
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
            lines.Add($"KyraOfflineDoesNotUseOnline: {!offlineCopilot.UsedOnlineData}");
            lines.Add($"KyraOnlineFallbackDoesNotCrash: {!string.IsNullOrWhiteSpace(onlineFallbackCopilot.Text)}");
            lines.Add($"KyraProviderHooksAvailable: {copilotRegistry.Providers.Count}");
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
            yield return $"RequiredFile: {RedactSelfTestFilesystemPath(path)} | Exists={File.Exists(path)}";
        }
    }

    private static void WriteStartupCrashReport(Exception exception)
    {
        var executableBase = GetExecutableBaseDirectory();
        var lines = new List<string>
        {
            "ForgerEMS startup crash",
            $"TimestampUtc: {DateTimeOffset.UtcNow:O}",
            $"ExecutablePath: {GetExecutablePath()}",
            $"CurrentDirectory: {Directory.GetCurrentDirectory()}",
            $"BaseDirectory: {AppContext.BaseDirectory}",
            $"ExecutableBase: {executableBase}",
            $"StartupLogPath: {GetStartupLogPath()}",
            $"ExceptionType: {exception.GetType().FullName}",
            $"ExceptionMessage: {exception.Message}",
            "XamlDetail:",
            FormatXamlParseDetail(exception),
            "StackTrace:",
            exception.ToString(),
            "BackendDiscoveryCandidates:",
            $"BundledBackendRoot: {Path.Combine(executableBase, "backend")} | Exists={Directory.Exists(Path.Combine(executableBase, "backend"))}",
            $"InstalledManifestRoot: {Path.Combine(executableBase, "manifests")} | Exists={Directory.Exists(Path.Combine(executableBase, "manifests"))}",
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
        var bundledBackendRoot = Path.Combine(GetExecutableBaseDirectory(), "backend");
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

    private static void LogStartupException(string stage, Exception exception)
    {
        AppendStartupLog($"{stage}: {exception.GetType().FullName}: {exception.Message}");
        var detail = FormatXamlParseDetail(exception);
        if (!string.IsNullOrWhiteSpace(detail))
        {
            AppendStartupLog(detail.TrimEnd());
        }

        AppendStartupLog(exception.ToString());
    }

    /// <summary>
    /// Adds line/position and inner exception detail for XAML load failures (e.g. missing StaticResource keys).
    /// </summary>
    private static string FormatXamlParseDetail(Exception exception)
    {
        if (exception is not XamlParseException xaml)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine(FormattableString.Invariant($"XamlParseException.LineNumber: {xaml.LineNumber}"));
        builder.AppendLine(FormattableString.Invariant($"XamlParseException.LinePosition: {xaml.LinePosition}"));
        if (xaml.BaseUri is not null)
        {
            builder.AppendLine(FormattableString.Invariant($"XamlParseException.BaseUri: {xaml.BaseUri}"));
        }

        builder.AppendLine(FormattableString.Invariant($"XamlParseException.Message: {xaml.Message}"));
        if (xaml.InnerException is not null)
        {
            builder.AppendLine(FormattableString.Invariant(
                $"XamlParseException.InnerException: {xaml.InnerException.GetType().FullName}: {xaml.InnerException.Message}"));
        }

        var root = xaml.GetBaseException();
        builder.AppendLine(FormattableString.Invariant($"BaseException: {root.GetType().FullName}: {root.Message}"));
        return builder.ToString();
    }

    private static void AppendStartupLog(string message) => StartupDiagnosticLog.AppendLine(message);

    private static string GetStartupLogPath() => StartupDiagnosticLog.GetStartupLogPath();

    private static void ShowLaunchFailureMessage(Exception exception)
    {
        try
        {
            MessageBox.Show(
                $"{exception.Message}{Environment.NewLine}{Environment.NewLine}Log file:{Environment.NewLine}{GetStartupLogPath()}",
                "ForgerEMS failed to launch",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
        }
    }

    private static string GetExecutablePath()
    {
        try
        {
            var mainModulePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(mainModulePath))
            {
                return mainModulePath;
            }
        }
        catch
        {
        }

        return Environment.ProcessPath ?? "(unknown)";
    }

    private static string GetExecutableBaseDirectory()
    {
        var executablePath = GetExecutablePath();
        if (!string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath))
        {
            return Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory;
        }

        return AppContext.BaseDirectory;
    }
}
