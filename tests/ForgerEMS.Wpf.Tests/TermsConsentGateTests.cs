using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Windows;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using ForgerEMS.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using VentoyToolkitSetup.Wpf.ViewModels;

namespace ForgerEMS.Wpf.Tests;

public sealed class TermsConsentGateTests
{
    [Fact]
    public void TermsConsentStore_NoRecordRequiresAcceptance()
    {
        using var temp = new TempFolder();
        var store = new TermsConsentStore(temp.Path);

        Assert.False(store.HasCurrentAcceptance(AppReleaseInfo.Version, AppReleaseInfo.DisplayVersion, out var record));
        Assert.Null(record);
        Assert.Equal(Path.Combine(temp.Path, "config", "terms-consent.json"), store.ConsentFilePath);
        Assert.Matches("^[a-f0-9]{64}$", TermsConsentStore.CurrentTermsSha256);
    }

    [Fact]
    public void TermsConsentStore_SaveAcceptedPersistsVersionTimestampBuildAndHash()
    {
        using var temp = new TempFolder();
        var store = new TermsConsentStore(temp.Path);
        var accepted = new DateTimeOffset(2026, 7, 2, 13, 45, 0, TimeSpan.Zero);

        var saved = store.SaveAccepted(AppReleaseInfo.Version, AppReleaseInfo.DisplayVersion, accepted);

        Assert.Equal(TermsConsentStore.CurrentTermsVersion, saved.TermsVersion);
        Assert.Equal(accepted, saved.AcceptedUtc);
        Assert.Equal(AppReleaseInfo.Version, saved.AppVersion);
        Assert.Equal(AppReleaseInfo.DisplayVersion, saved.AppBuild);
        Assert.Equal(TermsConsentStore.CurrentTermsSha256, saved.TermsSha256);
        Assert.True(store.HasCurrentAcceptance(AppReleaseInfo.Version, AppReleaseInfo.DisplayVersion, out var loaded));
        Assert.Equal(saved.TermsSha256, loaded?.TermsSha256);
    }

    [Fact]
    public void TermsConsentStore_OldTermsVersionRequiresReacceptance()
    {
        using var temp = new TempFolder();
        var store = new TermsConsentStore(temp.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(store.ConsentFilePath)!);
        File.WriteAllText(
            store.ConsentFilePath,
            JsonSerializer.Serialize(new TermsConsentRecord
            {
                TermsVersion = "2026-01-01.old",
                AcceptedUtc = DateTimeOffset.UtcNow,
                AppVersion = AppReleaseInfo.Version,
                AppBuild = AppReleaseInfo.DisplayVersion,
                TermsSha256 = TermsConsentStore.CurrentTermsSha256
            }));

        Assert.False(store.HasCurrentAcceptance(AppReleaseInfo.Version, AppReleaseInfo.DisplayVersion, out _));
    }

    [Fact]
    public void MainViewModel_FirstRunBlocksMainToolsUntilBothConsentBoxesAreAccepted()
    {
        using var temp = new TempFolder();
        var runtime = new FakeRuntime(temp.Path);
        runtime.EnsureInitialized();

        using var vm = BuildViewModel(runtime);

        Assert.False(vm.MainToolsEnabled);
        Assert.True(vm.IsTermsConsentRequired);
        Assert.Equal(Visibility.Visible, vm.TermsConsentVisibility);
        Assert.False(vm.AcceptTermsCommand.CanExecute(null));

        vm.TermsAgreementChecked = true;
        Assert.False(vm.AcceptTermsCommand.CanExecute(null));

        vm.TermsSharingNoticeChecked = true;
        Assert.True(vm.AcceptTermsCommand.CanExecute(null));

        vm.AcceptTermsCommand.Execute(null);
        WaitFor(() => vm.MainToolsEnabled);

        Assert.True(vm.MainToolsEnabled);
        Assert.False(vm.IsTermsConsentRequired);
        Assert.Equal(Visibility.Collapsed, vm.TermsConsentVisibility);
        Assert.Contains(TermsConsentStore.CurrentTermsVersion, vm.TermsConsentStorageStatusText, StringComparison.Ordinal);

        var store = new TermsConsentStore(runtime.RuntimeRoot);
        Assert.True(store.HasCurrentAcceptance(AppReleaseInfo.Version, AppReleaseInfo.DisplayVersion, out var record));
        Assert.NotNull(record?.AcceptedUtc);
    }

    [Fact]
    public void MainViewModel_ReturningUserWithCurrentTermsSkipsGate()
    {
        using var temp = new TempFolder();
        var runtime = new FakeRuntime(temp.Path);
        runtime.EnsureInitialized();
        new TermsConsentStore(runtime.RuntimeRoot).SaveAccepted(
            AppReleaseInfo.Version,
            AppReleaseInfo.DisplayVersion,
            DateTimeOffset.UtcNow);

        using var vm = BuildViewModel(runtime);

        Assert.True(vm.MainToolsEnabled);
        Assert.False(vm.IsTermsConsentRequired);
        Assert.Equal(Visibility.Collapsed, vm.TermsConsentVisibility);
    }

    [Fact]
    public void MainWindowXaml_HasTermsGateAndKeepsShellDisabledUntilAccepted()
    {
        var xaml = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        var overlay = ExtractBlock(xaml, "TermsConsentOverlay", "BetaWelcomeOverlay");

        Assert.Contains("IsEnabled=\"{Binding MainToolsEnabled}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TermsConsentVisibility", overlay, StringComparison.Ordinal);
        Assert.Contains("TermsAgreementChecked", overlay, StringComparison.Ordinal);
        Assert.Contains("TermsSharingNoticeChecked", overlay, StringComparison.Ordinal);
        Assert.Contains("AcceptTermsCommand", overlay, StringComparison.Ordinal);
        Assert.Contains("DeclineTermsCommand", overlay, StringComparison.Ordinal);
        Assert.Contains("ShowTermsOfServiceCommand", overlay, StringComparison.Ordinal);
        Assert.Contains("ShowPrivacyCommand", overlay, StringComparison.Ordinal);
        Assert.Contains("ShowLegalCommand", overlay, StringComparison.Ordinal);
        Assert.Contains("ShowThirdPartyNoticesCommand", overlay, StringComparison.Ordinal);
        Assert.Contains("ShowAboutCommand", overlay, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowXaml_ConsentCheckboxNoticesWrapInsideCheckboxes()
    {
        var xaml = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        var overlay = ExtractBlock(xaml, "TermsConsentOverlay", "BetaWelcomeOverlay");

        // Long consent notices must render as wrapped TextBlocks nested inside the
        // checkboxes. A plain Content="{Binding ...}" string renders single-line and
        // clips the sharing notice at 1366x768.
        Assert.DoesNotContain("Content=\"{Binding TermsConsentRequiredCheckboxText", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"{Binding TermsSharingConsentCheckboxText", overlay, StringComparison.Ordinal);

        AssertCheckboxWrapsNotice(overlay, "TermsConsentRequiredCheckboxText");
        AssertCheckboxWrapsNotice(overlay, "TermsSharingConsentCheckboxText");

        // 1366x768 guard: the gate must stay within the window (bounded size + its own
        // scroll) so legal notices can never render off-screen on small displays.
        Assert.Contains("MaxWidth=\"{Binding ActualWidth, RelativeSource={RelativeSource AncestorType=Window}}\"", overlay, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"{Binding ActualHeight, RelativeSource={RelativeSource AncestorType=Window}}\"", overlay, StringComparison.Ordinal);
        Assert.Contains("<ScrollViewer", overlay, StringComparison.Ordinal);
    }

    [Fact]
    public void TermsConsentStore_CheckboxNoticesUseCurrentApprovedWording()
    {
        Assert.Equal(
            "I have read and agree to the ForgerEMS Terms of Use and understand the Privacy/Data Handling notes.",
            TermsConsentStore.RequiredAgreementText);
        Assert.Equal(
            "I understand that logs, support bundles, Kyra context, and exported reports may contain local device/context information. I will review exported files before sharing them.",
            TermsConsentStore.RequiredSharingNoticeText);
        Assert.StartsWith(
            TermsConsentStore.CurrentTermsRevisionDate + ".",
            TermsConsentStore.CurrentTermsVersion,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MainViewModel_TermsVersionLineSeparatesDocumentRevisionFromAppVersion()
    {
        using var temp = new TempFolder();
        var runtime = new FakeRuntime(temp.Path);
        runtime.EnsureInitialized();

        using var vm = BuildViewModel(runtime);

        Assert.StartsWith(
            "Document revision: " + TermsConsentStore.CurrentTermsRevisionDate,
            vm.TermsConsentVersionText,
            StringComparison.Ordinal);
        Assert.Contains(
            "Applies to ForgerEMS v" + AppReleaseInfo.Version,
            vm.TermsConsentVersionText,
            StringComparison.Ordinal);
    }

    private static void AssertCheckboxWrapsNotice(string overlay, string bindingName)
    {
        var bindingIndex = overlay.IndexOf(bindingName, StringComparison.Ordinal);
        Assert.True(bindingIndex >= 0, $"Consent overlay must bind {bindingName}.");

        var checkBoxStart = overlay.LastIndexOf("<CheckBox", bindingIndex, StringComparison.Ordinal);
        Assert.True(checkBoxStart >= 0, $"{bindingName} must be rendered inside a CheckBox.");

        var checkBoxEnd = overlay.IndexOf("</CheckBox>", bindingIndex, StringComparison.Ordinal);
        Assert.True(checkBoxEnd > checkBoxStart, $"CheckBox hosting {bindingName} must nest its notice content.");

        var segment = overlay[checkBoxStart..checkBoxEnd];
        Assert.Contains("<TextBlock", segment, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", segment, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowCode_DefersInitializationUntilTermsAreAccepted()
    {
        var code = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml.cs"));

        Assert.Contains("PostConsentInitializeAsync", code, StringComparison.Ordinal);
        Assert.Contains("if (viewModel.MainToolsEnabled)", code, StringComparison.Ordinal);
        Assert.Contains("InitializeViewModelAfterConsentAsync", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainViewModel_ExportAndSupportActionsRequireSeparateSharingConsent()
    {
        var source = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "ViewModels", "MainViewModel.cs"));

        Assert.Contains("TermsConsentStore.RequiredSharingNoticeText", source, StringComparison.Ordinal);
        Assert.Contains("ConfirmExportOrSharingConsent", source, StringComparison.Ordinal);
        Assert.Contains("Export Kyra Memory", source, StringComparison.Ordinal);
        Assert.Contains("Export Kyra Intelligence Memory", source, StringComparison.Ordinal);
        Assert.Contains("Create Support Bundle", source, StringComparison.Ordinal);
        Assert.Contains("MainToolsEnabled = false", source, StringComparison.Ordinal);
        Assert.Contains("ExitForgerEms()", source, StringComparison.Ordinal);
    }

    private static MainViewModel BuildViewModel(FakeRuntime runtime)
    {
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
            new ManagedDownloadResolverService(new HttpClient()),
            runtime,
            new UsbBenchmarkService(powerShell),
            new CopilotService(registry),
            registry,
            usbIntelligenceService: new UsbIntelligenceService(),
            autoIntelligenceOrchestrator: new NoOpAutoIntelligenceOrchestrator());
    }

    private static void WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 100; i++)
        {
            if (condition())
            {
                return;
            }

            Thread.Sleep(10);
        }

        Assert.True(condition(), "Timed out waiting for condition.");
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

    private static string ExtractBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find start marker {startMarker}.");
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Could not find end marker {endMarker}.");
        return text[start..end];
    }

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "forgerems-terms-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
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

        public void ShowMessage(string title, string message, MessageBoxImage image = MessageBoxImage.Information)
        {
        }

        public int? PickOption(string title, string message, IReadOnlyList<string> options) =>
            options.Count > 0 ? 0 : null;
    }
}
