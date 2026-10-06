using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VentoyToolkitSetup.Wpf;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using ForgerEMS.Wpf.Services;
using VentoyToolkitSetup.Wpf.ViewModels;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Removal-regression coverage for the retired in-app assistant (Kyra/Copilot).
/// The assistant feature was deleted: these tests prove the app still starts,
/// the shell carries no assistant surface, shared redaction/USB safety survives,
/// and legacy assistant config keys are ignored harmlessly.
/// </summary>
public sealed class AssistantRemovalTests
{
    private static readonly Regex AssistantSurface =
        new("kyra|copilot|assistant", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [Fact]
    public void AppStartup_ContainsNoAssistantInitializationOrProjectReferences()
    {
        var appCodeBehind = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "App.xaml.cs"));
        Assert.DoesNotMatch(AssistantSurface, appCodeBehind);

        var solution = File.ReadAllText(FindRepoFile("ForgerEMS.sln"));
        Assert.DoesNotContain("kyra", solution, StringComparison.OrdinalIgnoreCase);

        var project = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "ForgerEMS.Wpf.csproj"));
        Assert.DoesNotContain("kyra", project, StringComparison.OrdinalIgnoreCase);

        var testProject = File.ReadAllText(FindRepoFile("tests", "ForgerEMS.Wpf.Tests", "ForgerEMS.Wpf.Tests.csproj"));
        Assert.DoesNotContain("kyra", testProject, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MainWindow_ContainsNoAssistantTabNavigationOrSettings()
    {
        var xaml = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        Assert.DoesNotMatch(AssistantSurface, xaml);

        var codeBehind = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml.cs"));
        Assert.DoesNotMatch(AssistantSurface, codeBehind);
    }

    [Fact]
    public void MainViewModel_Constructor_RequiresNoAssistantService()
    {
        var constructor = typeof(MainViewModel).GetConstructors().Single();
        foreach (var parameter in constructor.GetParameters())
        {
            var signature = $"{parameter.ParameterType.Name} {parameter.Name}";
            Assert.DoesNotMatch(AssistantSurface, signature);
        }
    }

    [Fact]
    public void LegacyBetaSettings_WithAssistantKeys_LoadsSafely()
    {
        var runtimeRoot = Path.Combine(Path.GetTempPath(), "forgerems-removal-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var configDir = Path.Combine(runtimeRoot, "config");
            Directory.CreateDirectory(configDir);
            File.WriteAllText(
                Path.Combine(configDir, "beta-settings.json"),
                """
                {
                    "betaTesterEntitlement": true,
                    "verboseLiveLogs": false,
                    "experimentalEmbeddedWslRunner": false,
                    "kyraEnabled": true,
                    "kyraProvider": "OpenAI",
                    "kyraApiKey": "legacy-secret",
                    "copilotProviderMode": "Cloud",
                    "assistantMemoryEnabled": true,
                    "licenseVerification": "placeholder"
                }
                """);

            var vm = CreateViewModel(runtimeRoot);

            Assert.True(vm.BetaTesterEntitlement);
        }
        finally
        {
            try { Directory.Delete(runtimeRoot, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void AppUpdateSettings_WithUnknownLegacyAssistantKeys_LoadDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "forgerems-update-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(
                path,
                """
                {
                    "CheckAutomatically": false,
                    "kyraEnabled": true,
                    "copilotProviderMode": "Cloud"
                }
                """);

            var settings = new AppUpdateSettingsStore(path).Load();

            Assert.False(settings.CheckAutomatically);
            Assert.False(settings.IncludeBetaRcChannels);
        }
        finally
        {
            try { File.Delete(path); }
            catch (IOException) { }
        }
    }

    [Fact]
    public void SharedRedaction_RemainsIntact()
    {
        var redacted = DiagnosticRedactor.Redact("token=ABCDEF1234567890SECRET", enabled: true);
        Assert.DoesNotContain("ABCDEF1234567890SECRET", redacted);

        var support = SupportContextSanitizer.SanitizeForSupport(@"Run from C:\Users\alice\secret\tool.exe");
        Assert.DoesNotContain("alice", support, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SafeUsbLogPaths_RemainIntact()
    {
        var safeRoots = new[] { @"D:\", @"D:\ISO\" };
        var raw = @"Final destination write result: D:\ISO\Linux\kali-linux-2026.1-installer-amd64.iso";

        var sanitized = UserFacingLogSanitizer.Sanitize(raw, safeRoots);

        Assert.Contains(@"D:\ISO\Linux\kali-linux-2026.1-installer-amd64.iso", sanitized);
        Assert.DoesNotContain("REDACTED_PRIVATE_PATH", sanitized);
    }

    private static MainViewModel CreateViewModel(string runtimeRoot) =>
        new(
            new BackendDiscoveryService(),
            new PowerShellRunnerService(),
            new NoUsbDetectionService(),
            new ManagedDownloadSummaryService(),
            new ScriptStatusParser(),
            new SilentPromptService(),
            new VentoyIntegrationService(new PowerShellRunnerService(), new AppRuntimeService()),
            new ManagedDownloadResolverService(new System.Net.Http.HttpClient()),
            new TempRuntimeService(runtimeRoot),
            new UsbBenchmarkService(new PowerShellRunnerService()));

    private static string FindRepoFile(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate repo file: {Path.Combine(segments)}");
    }

    private sealed class NoUsbDetectionService : IUsbDetectionService
    {
        public Task<UsbDetectionResult> GetUsbTargetsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new UsbDetectionResult { Targets = [] });
    }

    private sealed class SilentPromptService : IUserPromptService
    {
        public bool Confirm(string title, string message) => true;

        public string? PromptText(string title, string message, string initialValue = "") => initialValue;

        public void ShowMessage(string title, string message, MessageBoxImage image = MessageBoxImage.Information)
        {
        }

        public int? PickOption(string title, string message, IReadOnlyList<string> options) => 0;
    }

    private sealed class TempRuntimeService : IAppRuntimeService
    {
        public TempRuntimeService(string runtimeRoot)
        {
            RuntimeRoot = runtimeRoot;
            VentoyRoot = Path.Combine(runtimeRoot, "Ventoy");
            VentoyPackagesRoot = Path.Combine(VentoyRoot, "packages");
            VentoyExtractedRoot = Path.Combine(VentoyRoot, "extracted");
            LogsRoot = Path.Combine(runtimeRoot, "logs");
            DiagnosticsRoot = Path.Combine(runtimeRoot, "diagnostics");
            SessionLogPath = Path.Combine(LogsRoot, "session.log");
        }

        public string RuntimeRoot { get; }

        public string VentoyRoot { get; }

        public string VentoyPackagesRoot { get; }

        public string VentoyExtractedRoot { get; }

        public string LogsRoot { get; }

        public string DiagnosticsRoot { get; }

        public string SessionLogPath { get; }

        public void EnsureInitialized()
        {
            Directory.CreateDirectory(RuntimeRoot);
            Directory.CreateDirectory(LogsRoot);
            Directory.CreateDirectory(DiagnosticsRoot);
        }

        public void AppendSessionLog(LogLine line)
        {
            EnsureInitialized();
            File.AppendAllText(SessionLogPath, line.Text + Environment.NewLine);
        }

        public string WriteDiagnosticReport(string fileName, IEnumerable<string> lines)
        {
            EnsureInitialized();
            var path = Path.Combine(DiagnosticsRoot, fileName);
            File.WriteAllText(path, string.Join(Environment.NewLine, lines));
            return path;
        }
    }
}
