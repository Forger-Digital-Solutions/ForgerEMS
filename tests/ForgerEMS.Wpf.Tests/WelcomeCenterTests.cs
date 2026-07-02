using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using VentoyToolkitSetup.Wpf.ViewModels;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class WelcomeCenterTests
{
    [Fact]
    public void WelcomeCenter_DefaultsVisibleOnStartup()
    {
        using var vm = BuildViewModel(NewRuntime());

        Assert.Equal(System.Windows.Visibility.Visible, vm.BetaWelcomeVisibility);
    }

    [Fact]
    public void WelcomeCenter_IgnoresLegacyUnusedWelcomeDismissedKey()
    {
        var runtime = NewRuntime();
        Directory.CreateDirectory(Path.Combine(runtime.RuntimeRoot, "config"));
        File.WriteAllText(
            Path.Combine(runtime.RuntimeRoot, "config", "beta-settings.json"),
            """
            {
              "welcomeDismissed": true,
              "verboseLiveLogs": true
            }
            """);

        using var vm = BuildViewModel(runtime);

        Assert.Equal(System.Windows.Visibility.Visible, vm.BetaWelcomeVisibility);
        Assert.True(vm.VerboseLiveLogs);
    }

    [Fact]
    public void NeverShowWelcomeCenterAgain_PersistsAndSuppressesNextLaunch()
    {
        var runtime = NewRuntime();

        using (var first = BuildViewModel(runtime))
        {
            Assert.Equal(System.Windows.Visibility.Visible, first.BetaWelcomeVisibility);
            first.NeverShowWelcomeCenterAgainCommand.Execute(null);
            Assert.Equal(System.Windows.Visibility.Collapsed, first.BetaWelcomeVisibility);
        }

        using var second = BuildViewModel(runtime);
        Assert.Equal(System.Windows.Visibility.Collapsed, second.BetaWelcomeVisibility);

        // Settings keeps a reopen path even after the preference is set.
        second.OpenWelcomeCenterCommand.Execute(null);
        Assert.Equal(System.Windows.Visibility.Visible, second.BetaWelcomeVisibility);
    }

    [Fact]
    public void WelcomeCenter_CloseForNowIsSessionOnly()
    {
        var runtime = NewRuntime();

        using (var first = BuildViewModel(runtime))
        {
            first.DismissBetaWelcome();
            Assert.Equal(System.Windows.Visibility.Collapsed, first.BetaWelcomeVisibility);
        }

        using var second = BuildViewModel(runtime);
        Assert.Equal(System.Windows.Visibility.Visible, second.BetaWelcomeVisibility);
    }

    [Fact]
    public void WelcomeCenter_CanBeReopenedFromCommand()
    {
        using var vm = BuildViewModel(NewRuntime());
        vm.DismissBetaWelcome();

        vm.OpenWelcomeCenterCommand.Execute(null);

        Assert.Equal(System.Windows.Visibility.Visible, vm.BetaWelcomeVisibility);
    }

    [Fact]
    public void WelcomeCenter_InfoAndActionButtonsDoNotCloseWelcomeCenter()
    {
        using var vm = BuildViewModel(NewRuntime());
        var capture = new WelcomeInfoCapture();
        vm.WelcomeCenterInfoAction = capture.Capture;

        vm.BetaWelcomeKyraKeepLocalOnlyCommand.Execute(null);
        vm.BetaWelcomeKyraViewSharingPreviewCommand.Execute(null);

        Assert.Equal(System.Windows.Visibility.Visible, vm.BetaWelcomeVisibility);
        Assert.Contains(capture.Messages, item => item.Title == "Keep Local Only");
        Assert.Contains(capture.Messages, item => item.Title == "View What Would Be Shared");
    }

    [Fact]
    public void HelpImproveKyra_DoesNotOpenSeparateWindow_UsesInlineConfirmation()
    {
        using var vm = BuildViewModel(NewRuntime());
        ResetKyraSharingState(vm);
        var capture = new WelcomeInfoCapture();
        vm.WelcomeCenterInfoAction = capture.Capture;

        vm.BetaWelcomeKyraShareResolvedCategories = true;
        vm.BetaWelcomeKyraHelpImproveCommand.Execute(null);

        // No ScrollableInfoWindow / WelcomeCenterInfoAction call for Help Improve Kyra.
        Assert.DoesNotContain(capture.Messages, item => item.Title == "Help Improve Kyra");
        Assert.Equal(System.Windows.Visibility.Visible, vm.BetaWelcomeKyraConfirmVisibility);
        Assert.True(vm.BetaWelcomeKyraConfirmHasSelection);
        Assert.False(vm.KyraCommunitySharingEnabled);
        Assert.Equal(System.Windows.Visibility.Visible, vm.BetaWelcomeVisibility);
    }

    [Fact]
    public void HelpImproveKyra_NoCategorySelected_ShowsInlineHintWithoutEnableButton()
    {
        using var vm = BuildViewModel(NewRuntime());
        ResetKyraSharingState(vm);

        vm.BetaWelcomeKyraHelpImproveCommand.Execute(null);

        Assert.Equal(System.Windows.Visibility.Visible, vm.BetaWelcomeKyraConfirmVisibility);
        Assert.False(vm.BetaWelcomeKyraConfirmHasSelection);
        Assert.False(vm.KyraCommunitySharingEnabled);
    }

    [Fact]
    public void HelpImproveKyra_ConfirmEnable_DoesNotUncheckSharingCheckboxes()
    {
        using var vm = BuildViewModel(NewRuntime());
        ResetKyraSharingState(vm);

        vm.BetaWelcomeKyraShareResolvedCategories = true;
        vm.BetaWelcomeKyraHelpImproveCommand.Execute(null);
        vm.BetaWelcomeKyraConfirmEnableCommand.Execute(null);

        Assert.True(vm.KyraCommunitySharingEnabled);
        Assert.True(vm.KyraShareResolvedIssueFixPatterns);
        // Regression guard: confirming must not auto-uncheck the Welcome Center boxes the user just set.
        Assert.True(vm.BetaWelcomeKyraShareResolvedCategories);
        Assert.Equal(System.Windows.Visibility.Collapsed, vm.BetaWelcomeKyraConfirmVisibility);
        Assert.Equal(System.Windows.Visibility.Visible, vm.BetaWelcomeVisibility);
    }

    [Fact]
    public void HelpImproveKyra_Cancel_DoesNotChangeSharingSettings()
    {
        using var vm = BuildViewModel(NewRuntime());
        ResetKyraSharingState(vm);

        vm.BetaWelcomeKyraShareResolvedCategories = true;
        vm.BetaWelcomeKyraHelpImproveCommand.Execute(null);
        vm.BetaWelcomeKyraCancelConfirmCommand.Execute(null);

        Assert.False(vm.KyraCommunitySharingEnabled);
        Assert.False(vm.KyraShareResolvedIssueFixPatterns);
        Assert.Equal(System.Windows.Visibility.Collapsed, vm.BetaWelcomeKyraConfirmVisibility);
    }

    [Fact]
    public void WelcomeCenter_XamlRemovesDontShowAgainAndAddsReopenCommand()
    {
        var xaml = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        var overlay = ExtractBlock(xaml, "BetaWelcomeOverlay", "FullLogsOverlay");

        Assert.Contains("Welcome Center", overlay, StringComparison.Ordinal);
        Assert.Contains("Close for now", overlay, StringComparison.Ordinal);
        Assert.Contains("OpenWelcomeCenterCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("You can reopen this anytime from Settings / Help", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("Don't show again", overlay, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WelcomeCenter_IntroTextUsesResponsiveWrappingInsteadOfFixedWidth()
    {
        var xaml = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        var overlay = ExtractBlock(xaml, "BetaWelcomeOverlay", "FullLogsOverlay");

        Assert.Contains("<ScrollViewer", overlay, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"760\"", overlay, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain(" Width=\"", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("Width=\"620\"", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("TextWrapping=\"NoWrap\"", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("TextTrimming", overlay, StringComparison.Ordinal);
    }

    [Fact]
    public void WelcomeCenter_DoesNotLeadWithLoudVersionLine()
    {
        var xaml = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        var overlay = ExtractBlock(xaml, "BetaWelcomeOverlay", "FullLogsOverlay");

        // The headline must lead with the product name; the descriptive version/feature
        // line (AppVersionFooterText) must not sit directly under the headline anymore -
        // it now lives in the small muted footer near the bottom of the dialog.
        var headlineIndex = overlay.IndexOf("ForgerEMS Welcome Center", StringComparison.Ordinal);
        var kyraPrivacyIndex = overlay.IndexOf("Kyra privacy and learning", StringComparison.Ordinal);
        var versionBindingIndex = overlay.IndexOf("{Binding AppVersionFooterText}", StringComparison.Ordinal);

        Assert.True(headlineIndex >= 0);
        Assert.True(kyraPrivacyIndex > headlineIndex);
        Assert.True(versionBindingIndex > kyraPrivacyIndex, "Version line should be below the main content, not directly under the headline.");
    }

    [Fact]
    public void WelcomeCenter_HasTermsOfServiceLinkAndFooterCopyright()
    {
        var xaml = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        var overlay = ExtractBlock(xaml, "BetaWelcomeOverlay", "FullLogsOverlay");

        Assert.Contains("ShowTermsOfServiceCommand", overlay, StringComparison.Ordinal);
        Assert.Contains("Terms of Service", overlay, StringComparison.Ordinal);
        Assert.Contains("NeverShowWelcomeCenterAgainCommand", overlay, StringComparison.Ordinal);
        Assert.Contains("Never show this Welcome Center again", overlay, StringComparison.Ordinal);
        Assert.Contains("{Binding WelcomeCenterFooterCopyrightText}", overlay, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsTab_HasTermsOfServiceButton()
    {
        var xaml = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        var settingsStart = xaml.IndexOf("<TabItem Header=\"☰  Settings\">", StringComparison.Ordinal);
        Assert.True(settingsStart >= 0);
        var settings = xaml[settingsStart..];

        Assert.Contains("Content=\"Terms of Use\"", settings, StringComparison.Ordinal);
        Assert.Contains("ShowTermsOfServiceCommand", settings, StringComparison.Ordinal);
        Assert.Contains("ShowThirdPartyNoticesCommand", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void WelcomeCenter_ActionHandlersDoNotDismissExceptExplicitClose()
    {
        var code = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml.cs"));

        Assert.DoesNotContain(
            "DismissBetaWelcome",
            ExtractMethod(code, "OnStartUsbBuilderFromBetaWelcomeClick"),
            StringComparison.Ordinal);
        // OnRunSystemScanFromWelcomeClick was removed with the Welcome Center's
        // inventory-scan quick action.
        Assert.DoesNotContain("OnRunSystemScanFromWelcomeClick", code, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DismissBetaWelcome",
            ExtractMethod(code, "OnRunUsbBenchmarkFromWelcomeClick"),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DismissBetaWelcome",
            ExtractMethod(code, "OnOpenLogsFromBetaWelcomeClick"),
            StringComparison.Ordinal);
        Assert.Contains(
            "DismissBetaWelcome",
            ExtractMethod(code, "OnDismissBetaWelcomeClick"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void WelcomeCenter_HelperWindowsUseModelessShowPath()
    {
        var helperCode = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "ScrollableInfoWindow.xaml.cs"));

        Assert.Contains("ShowModeless", helperCode, StringComparison.Ordinal);
        Assert.Contains(".Show();", helperCode, StringComparison.Ordinal);
    }

    private static MainViewModel BuildViewModel(FakeRuntime runtime)
    {
        runtime.EnsureInitialized();
        var powerShell = new PowerShellRunnerService();
        var registry = new CopilotProviderRegistry();
        return new MainViewModel(
            new BackendDiscoveryService(),
            powerShell,
            new EmptyUsbDetectionService(),
            new ManagedDownloadSummaryService(),
            new ScriptStatusParser(),
            new AcceptingPromptService(),
            new VentoyIntegrationService(powerShell, runtime),
            runtime,
            new UsbBenchmarkService(powerShell),
            new CopilotService(registry),
            registry,
            usbIntelligenceService: new UsbIntelligenceService(),
            autoIntelligenceOrchestrator: new NoOpAutoIntelligenceOrchestrator());
    }

    /// <summary>
    /// Normalizes Kyra sharing state regardless of HKLM\Software\ForgerEMS installer-consent
    /// registry values that may already be set on the host machine from a prior real install
    /// (SeedBetaWelcomeKyraCheckboxesFromSettings reads that registry on a fresh profile).
    /// </summary>
    private static void ResetKyraSharingState(MainViewModel vm)
    {
        vm.BetaWelcomeKyraShareRepairIntelligence = false;
        vm.BetaWelcomeKyraShareHardwarePatterns = false;
        vm.BetaWelcomeKyraShareResolvedCategories = false;
        vm.BetaWelcomeKyraShareCrashDiagnostics = false;
        vm.KyraCommunitySharingEnabled = false;
    }

    private static FakeRuntime NewRuntime() =>
        new(Path.Combine(Path.GetTempPath(), "forgerems-welcome-tests", Guid.NewGuid().ToString("N")));

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

    private static string ExtractBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find start marker {startMarker}.");
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Could not find end marker {endMarker}.");
        return text[start..end];
    }

    private static string ExtractMethod(string code, string methodName)
    {
        var signature = $"private void {methodName}";
        var start = code.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method {methodName}.");
        var next = code.IndexOf("\n    private ", start + signature.Length, StringComparison.Ordinal);
        return next > start ? code[start..next] : code[start..];
    }

    private sealed class WelcomeInfoCapture
    {
        public List<Message> Messages { get; } = [];

        public void Capture(string title, string body, string? actionText, Action? action) =>
            Messages.Add(new Message(title, body, actionText, action));

        public sealed record Message(string Title, string Body, string? ActionText, Action? Action);
    }

    private sealed class FakeRuntime(string runtimeRoot) : IAppRuntimeService
    {
        public string RuntimeRoot { get; } = runtimeRoot;
        public string VentoyRoot => Path.Combine(RuntimeRoot, "Ventoy");
        public string VentoyPackagesRoot => Path.Combine(VentoyRoot, "packages");
        public string VentoyExtractedRoot => Path.Combine(VentoyRoot, "extracted");
        public string LogsRoot => Path.Combine(RuntimeRoot, "logs");
        public string DiagnosticsRoot => Path.Combine(RuntimeRoot, "diagnostics");
        public string SessionLogPath => Path.Combine(LogsRoot, "session.log");

        public void EnsureInitialized()
        {
            Directory.CreateDirectory(RuntimeRoot);
            Directory.CreateDirectory(Path.Combine(RuntimeRoot, "config"));
            Directory.CreateDirectory(Path.Combine(RuntimeRoot, "cache"));
            Directory.CreateDirectory(Path.Combine(RuntimeRoot, "reports"));
            Directory.CreateDirectory(LogsRoot);
            Directory.CreateDirectory(DiagnosticsRoot);
        }

        public void AppendSessionLog(LogLine line)
        {
        }

        public string WriteDiagnosticReport(string fileName, IEnumerable<string> lines)
        {
            var path = Path.Combine(DiagnosticsRoot, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, lines);
            return path;
        }
    }

    private sealed class EmptyUsbDetectionService : IUsbDetectionService
    {
        public Task<UsbDetectionResult> GetUsbTargetsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new UsbDetectionResult());
    }

    private sealed class AcceptingPromptService : IUserPromptService
    {
        public bool Confirm(string title, string message) => true;

        public string? PromptText(string title, string message, string initialValue = "") => initialValue;

        public void ShowMessage(
            string title,
            string message,
            System.Windows.MessageBoxImage image = System.Windows.MessageBoxImage.Information)
        {
        }

        public int? PickOption(string title, string message, IReadOnlyList<string> options) =>
            options.Count > 0 ? 0 : null;
    }
}
