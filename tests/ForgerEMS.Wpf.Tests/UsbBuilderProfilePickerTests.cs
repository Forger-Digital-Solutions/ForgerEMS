using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VentoyToolkitSetup.Wpf;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using ForgerEMS.Wpf.Services;
using VentoyToolkitSetup.Wpf.ViewModels;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Regression coverage for the USB Builder Profile per-category item picker
/// ("Pick items" button -> CategoryBuilderWindow). Proves each category card
/// exposes a working picker command wired to the correct category + item list,
/// that picker changes flow back into the profile / build manifest only on
/// commit, and that the picker button's enabled state tracks busy state.
/// </summary>
public sealed class UsbBuilderProfilePickerTests
{
    private static MainViewModel CreateViewModel() =>
        new(
            new BackendDiscoveryService(),
            new PowerShellRunnerService(),
            new NoUsbDetectionService(),
            new ManagedDownloadSummaryService(),
            new ScriptStatusParser(),
            new SilentPromptService(),
            new VentoyIntegrationService(new PowerShellRunnerService(), new AppRuntimeService()),
            new ManagedDownloadResolverService(new HttpClient()),
            new AppRuntimeService(),
            new UsbBenchmarkService(new PowerShellRunnerService()),
            new CopilotService(new CopilotProviderRegistry()),
            new CopilotProviderRegistry());

    [Fact]
    public void EveryCategoryCard_ExposesPickerCommandThatCanExecute()
    {
        var vm = CreateViewModel();

        Assert.NotEmpty(vm.UsbBuilderProfileOptions);
        foreach (var option in vm.UsbBuilderProfileOptions)
        {
            Assert.True(
                vm.CustomizeUsbBuilderCategoryCommand.CanExecute(option),
                $"Picker command should be executable for category '{option.CategoryId}'.");
        }

        // A null parameter (no category) must not be executable.
        Assert.False(vm.CustomizeUsbBuilderCategoryCommand.CanExecute(null));
    }

    [Fact]
    public void EveryCategoryCard_HasItemsSoPickerOpensAWindow()
    {
        // OpenCategoryBuilderFor only opens the picker window when the option has a
        // seeded item catalog; an empty list falls back to a bare checkbox toggle.
        // Every shipped category must therefore have items so "Pick items" opens.
        var vm = CreateViewModel();

        foreach (var option in vm.UsbBuilderProfileOptions)
        {
            Assert.True(
                option.Items.Count > 0,
                $"Category '{option.CategoryId}' must seed items so the picker opens instead of just toggling.");
        }
    }

    [Fact]
    public void Picker_IsWiredToCorrectCategoryAndItemList()
    {
        var vm = CreateViewModel();

        foreach (var option in vm.UsbBuilderProfileOptions)
        {
            var picker = new CategoryBuilderViewModel(option);

            Assert.Equal(option.DisplayName, picker.CategoryHeader);
            Assert.StartsWith(option.DisplayName, picker.WindowTitle, StringComparison.Ordinal);
            Assert.EndsWith("item picker", picker.WindowTitle, StringComparison.Ordinal);
            Assert.NotEmpty(picker.WorkingItems);

            // Every row in the picker must belong to the category it was opened for —
            // no cross-category leakage / stale list from a previous category.
            Assert.All(picker.WorkingItems, item =>
                Assert.Equal(option.CategoryId, item.CategoryId, ignoreCase: true));
        }
    }

    [Fact]
    public void RepeatedPickerUse_AcrossCategories_HasNoStaleDataFromPreviousCategory()
    {
        var vm = CreateViewModel();
        var windows = vm.UsbBuilderProfileOptions.First(o => o.CategoryId == "windows");
        var linux = vm.UsbBuilderProfileOptions.First(o => o.CategoryId == "linux-rescue");

        var firstPicker = new CategoryBuilderViewModel(windows);
        Assert.All(firstPicker.WorkingItems, i => Assert.Equal("windows", i.CategoryId, ignoreCase: true));

        // Opening a second category must produce that category's rows only.
        var secondPicker = new CategoryBuilderViewModel(linux);
        Assert.All(secondPicker.WorkingItems, i => Assert.Equal("linux-rescue", i.CategoryId, ignoreCase: true));
        Assert.DoesNotContain(secondPicker.WorkingItems, i => string.Equals(i.CategoryId, "windows", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PickerSelectionChange_UpdatesSelectedCount_AndCommitFlowsToCategoryCard()
    {
        var vm = CreateViewModel();
        var option = vm.UsbBuilderProfileOptions.First(o => o.CategoryId == "linux-rescue");
        var picker = new CategoryBuilderViewModel(option);

        picker.SelectAllCommand.Execute(null);
        Assert.Equal(picker.TotalCount, picker.SelectedCount);
        Assert.Equal($"{picker.SelectedCount} of {picker.TotalCount} selected", picker.SelectionSummary);

        picker.CommitTo(option);

        // The category card's selected count now reflects the picker's selection.
        Assert.Equal(option.Items.Count, option.SelectedItemCount);
        Assert.Contains($"{option.SelectedItemCount} / {option.VisibleItemCount}", option.SelectedItemSummaryText, StringComparison.Ordinal);
    }

    [Fact]
    public void PickerCancel_DoesNotMutateProfileOrBuildManifest()
    {
        var vm = CreateViewModel();
        var option = vm.UsbBuilderProfileOptions.First(o => o.CategoryId == "diagnostics");

        var beforeSelectors = UsbBuilderProfileItemSelection.BuildSelectedManifestSelectors(vm.UsbBuilderProfileOptions);
        var beforeSelectedIds = option.GetSelectedItemIds().ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Open a picker, flip every toggle, but Cancel (never CommitTo).
        var picker = new CategoryBuilderViewModel(option);
        picker.SelectAllCommand.Execute(null);
        bool? accepted = null;
        picker.CloseRequested += (_, result) => accepted = result;
        picker.CancelCommand.Execute(null);

        Assert.False(accepted);
        Assert.True(option.GetSelectedItemIds().ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(beforeSelectedIds));

        var afterSelectors = UsbBuilderProfileItemSelection.BuildSelectedManifestSelectors(vm.UsbBuilderProfileOptions);
        Assert.Equal(beforeSelectors, afterSelectors);
    }

    [Fact]
    public void CommittedPickerSelection_FlowsIntoBuildManifestSelectors()
    {
        var vm = CreateViewModel();
        var option = vm.UsbBuilderProfileOptions.First(o => o.CategoryId == "linux-rescue");
        Assert.True(option.IsIncluded, "linux-rescue ships included by default.");

        // Pick a specific optional item that is not selected by the recommended baseline.
        var picker = new CategoryBuilderViewModel(option);
        var target = picker.WorkingItems.First(i => i.Id == "linux.debian");
        target.IsSelected = true;
        picker.CommitTo(option);

        var selectors = UsbBuilderProfileItemSelection.BuildSelectedManifestSelectors(vm.UsbBuilderProfileOptions);
        var committed = option.Items.First(i => i.Id == "linux.debian");

        Assert.Contains($"name:{committed.ManifestEntryName}", selectors);
        Assert.Contains($"dest:{committed.UsbRelativePath}", selectors);
    }

    [Fact]
    public void PickerButtonCommand_RefreshesEnabledStateWhenBusyStateChanges()
    {
        // RelayCommand<T> does not hook CommandManager.RequerySuggested, so the picker
        // button only re-evaluates CanExecute when the command raises CanExecuteChanged.
        // MainViewModel.RaiseCommandStates (fired on IsBusy changes) must include the
        // picker command or its button goes stale relative to IsBusy.
        var vm = CreateViewModel();
        var option = vm.UsbBuilderProfileOptions.First();

        var raisedCount = 0;
        vm.CustomizeUsbBuilderCategoryCommand.CanExecuteChanged += (_, _) => raisedCount++;

        var busySetter = typeof(MainViewModel).GetProperty("IsBusy")!.GetSetMethod(nonPublic: true)!;

        busySetter.Invoke(vm, new object[] { true });
        Assert.True(raisedCount >= 1, "Setting IsBusy=true should refresh the picker command's CanExecute.");
        Assert.False(vm.CustomizeUsbBuilderCategoryCommand.CanExecute(option), "Picker should be disabled while busy.");

        busySetter.Invoke(vm, new object[] { false });
        Assert.True(raisedCount >= 2, "Clearing IsBusy should refresh the picker command's CanExecute again.");
        Assert.True(vm.CustomizeUsbBuilderCategoryCommand.CanExecute(option), "Picker should re-enable once idle.");
    }

    [Fact]
    public void CategoryBuilderWindow_ConstructsAndWiresDataContext_OnStaThread()
    {
        // Proves the picker view itself routes/opens: the window loads its XAML
        // (StaticResource keys resolve) and binds to the category VM without throwing.
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try
            {
                var def = UsbBuilderProfileCatalog.GetRequired("oem-tools");
                var option = UsbBuilderProfileOption.FromDefinition(def, included: true);
                option.LoadItems(UsbBuilderProfileItemCatalog.ForCategory("oem-tools"), null);

                var pickerVm = new CategoryBuilderViewModel(option);
                var window = new CategoryBuilderWindow(pickerVm);

                Assert.Same(pickerVm, window.DataContext);
                Assert.Equal("OEM recovery links and vendor tools item picker", pickerVm.WindowTitle);
                window.Close();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "STA thread timed out.");
        Assert.Null(caught);
    }

    [Fact]
    public void CategoryBuilderWindow_ReadOnlyRunBindings_AreOneWay()
    {
        // Run.Text is TwoWay-by-default in WPF. SourceDisplay (and the header
        // summaries) are read-only projections, so a default (TwoWay) binding throws
        // XamlParseException when the item rows are realized. That exception is
        // swallowed by the app's global DispatcherUnhandledException handler, so the
        // picker silently fails to open. Every Run.Text VM/model binding must be OneWay.
        var xaml = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "CategoryBuilderWindow.xaml"));

        Assert.DoesNotContain("Text=\"{Binding SourceDisplay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding SourceDisplay, Mode=OneWay}\"", xaml, StringComparison.Ordinal);

        foreach (Match match in Regex.Matches(xaml, "<Run Text=\"\\{Binding [^}]*\\}\""))
        {
            Assert.Contains("Mode=OneWay", match.Value, StringComparison.Ordinal);
        }

        // Guard the root cause: the bound projection must stay read-only.
        var prop = typeof(UsbBuilderProfileItem).GetProperty(nameof(UsbBuilderProfileItem.SourceDisplay));
        Assert.NotNull(prop);
        Assert.True(
            prop!.SetMethod is null || !prop.SetMethod.IsPublic,
            "SourceDisplay must stay read-only; if it ever gains a public setter update the binding intent explicitly.");
    }

    [Fact]
    public void CategoryBuilderWindow_RealizesItemRows_WithoutReadOnlyBindingCrash_OnStaThread()
    {
        // Faithful runtime reproduction: force layout of the picker's content so the
        // item DataTemplate (with the SourceDisplay Run binding) is actually
        // instantiated. Before the Mode=OneWay fix this threw XamlParseException here.
        //
        // Uses headless Measure/Arrange rather than Window.Show() so the result does
        // not depend on the process-wide WPF Application state that other STA tests in
        // the suite create/tear down (that made a Show()-based version flaky).
        Exception? caught = null;
        var realizedRows = 0;
        var thread = new Thread(() =>
        {
            try
            {
                var def = UsbBuilderProfileCatalog.GetRequired("linux-rescue");
                var option = UsbBuilderProfileOption.FromDefinition(def, included: true);
                option.LoadItems(UsbBuilderProfileItemCatalog.ForCategory("linux-rescue"), null);

                var pickerVm = new CategoryBuilderViewModel(option);
                var window = new CategoryBuilderWindow(pickerVm);

                // Detach the window content and lay it out headless (no Show()). The
                // DataContext is re-pinned so ItemsSource still resolves and the item
                // rows (and their SourceDisplay Run binding) are realized.
                var content = (FrameworkElement)window.Content;
                window.Content = null;
                content.DataContext = pickerVm;
                content.Measure(new Size(900, 680));
                content.Arrange(new Rect(0, 0, 900, 680));
                content.UpdateLayout();

                realizedRows = CountVisualChildren<CheckBox>(content);
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)), "STA thread timed out.");
        Assert.Null(caught);
        Assert.True(realizedRows > 0, "Picker item rows should realize (one checkbox per item).");
    }

    private static int CountVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = 0;
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T)
            {
                count++;
            }

            count += CountVisualChildren<T>(child);
        }

        return count;
    }

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
}
