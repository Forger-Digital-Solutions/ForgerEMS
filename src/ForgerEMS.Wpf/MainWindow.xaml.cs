using System.ComponentModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VentoyToolkitSetup.Wpf.ViewModels;

namespace VentoyToolkitSetup.Wpf;

public partial class MainWindow : Window
{
    private bool _initialized;
    private bool _logScrollPending;
    private MainViewModel? _currentViewModel;
    private readonly DispatcherTimer _logScrollTimer;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        CenterWindowForLaunch();
        DataContext = viewModel;

        _logScrollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(140)
        };
        _logScrollTimer.Tick += OnLogScrollTimerTick;

        Loaded += OnLoaded;
        Closed += OnClosed;
        DataContextChanged += OnDataContextChanged;
        AttachToViewModel(viewModel);
    }

    private void CenterWindowForLaunch()
    {
        if (WindowState != WindowState.Normal)
        {
            return;
        }

        const double margin = 40;

        var workArea = SystemParameters.WorkArea;
        var maxWidth = Math.Max(720, workArea.Width - margin);
        var maxHeight = Math.Max(520, workArea.Height - margin);

        MinWidth = Math.Min(MinWidth, maxWidth);
        MinHeight = Math.Min(MinHeight, maxHeight);

        Width = Math.Min(Width, maxWidth);
        Height = Math.Min(Height, maxHeight);

        Left = workArea.Left + ((workArea.Width - Width) / 2);
        Top = workArea.Top + ((workArea.Height - Height) / 2);

        if (Left + Width > workArea.Right)
        {
            Left = Math.Max(workArea.Left, workArea.Right - Width);
        }

        if (Top + Height > workArea.Bottom)
        {
            Top = Math.Max(workArea.Top, workArea.Bottom - Height);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        if (DataContext is MainViewModel viewModel)
        {
            // Install the WM_DEVICECHANGE hook before initialization so the
            // debounced refresh path is ready as soon as the first device
            // arrival/removal arrives. This is required for the "no USB at
            // launch + later plug-in" flow: the hook will refresh the target
            // list automatically once Windows broadcasts the volume mount.
            viewModel.AttachUsbDeviceChangeNotifier(this);
            await viewModel.InitializeAsync();
        }

        UpdateSidebarSelection();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachFromViewModel(_currentViewModel);
        AttachToViewModel(e.NewValue as MainViewModel);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        DetachFromViewModel(_currentViewModel);
        _currentViewModel?.Dispose();
    }

    private void AttachToViewModel(MainViewModel? viewModel)
    {
        _currentViewModel = viewModel;
        if (_currentViewModel is not null)
        {
            _currentViewModel.PropertyChanged += OnViewModelPropertyChanged;
            _currentViewModel.CopilotMessages.CollectionChanged += OnCopilotMessagesChanged;
            _currentViewModel.OpenKyraAdvancedSettingsAction = OpenKyraAdvancedSettingsWindow;
            _currentViewModel.MainTabNavigationAction = NavigateMainTab;
        }
    }

    private void DetachFromViewModel(MainViewModel? viewModel)
    {
        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            viewModel.CopilotMessages.CollectionChanged -= OnCopilotMessagesChanged;
            viewModel.OpenKyraAdvancedSettingsAction = null;
            viewModel.MainTabNavigationAction = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if ((e.PropertyName == nameof(MainViewModel.AutoScrollLogs) ||
             e.PropertyName == nameof(MainViewModel.LogsText)) &&
            _currentViewModel?.AutoScrollLogs == true)
        {
            QueueLogScroll();
        }

    }

    private void OnCopilotMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScrollKyraChatToLatest();
    }

    private void ScrollKyraChatToLatest()
    {
        Dispatcher.BeginInvoke(() => KyraChatScrollViewer?.ScrollToEnd(), DispatcherPriority.Background);
    }

    private void NavigateMainTab(string key)
    {
        if (MainTabControl == null || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        foreach (var item in MainTabControl.Items)
        {
            if (item is TabItem { Header: string h } && h.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                MainTabControl.SelectedItem = item;
                UpdateSidebarSelection();
                return;
            }
        }
    }

    private void OnCopilotInputPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            viewModel.KyraSlashPopupOpen = false;
            viewModel.KyraSlashSuggestions.Clear();
            viewModel.KyraSlashSelectedIndex = -1;
            return;
        }

        if (viewModel.KyraSlashPopupOpen && viewModel.KyraSlashSuggestions.Count > 0)
        {
            if (e.Key == Key.Down)
            {
                e.Handled = true;
                viewModel.KyraSlashSelectedIndex = viewModel.KyraSlashSelectedIndex + 1;
                return;
            }

            if (e.Key == Key.Up)
            {
                e.Handled = true;
                viewModel.KyraSlashSelectedIndex = viewModel.KyraSlashSelectedIndex - 1;
                return;
            }
        }

        if (e.Key == Key.Tab &&
            !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) &&
            viewModel.KyraSlashPopupOpen &&
            viewModel.KyraSlashSuggestions.Count > 0)
        {
            e.Handled = true;
            var i = viewModel.KyraSlashSelectedIndex >= 0 ? viewModel.KyraSlashSelectedIndex : 0;
            viewModel.InsertKyraSlashSuggestion(viewModel.KyraSlashSuggestions[i]);
            KyraChatInputBox?.Focus();
            return;
        }

        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (viewModel.KyraSlashPopupOpen && viewModel.KyraSlashSuggestions.Count > 0)
            {
                if (viewModel.KyraSlashSelectedIndex < 0)
                {
                    viewModel.KyraSlashSelectedIndex = 0;
                }

                e.Handled = true;
                viewModel.ApplyKyraSlashSelection();
                KyraChatInputBox?.Focus();
                return;
            }

            if (viewModel.SendCopilotMessageCommand.CanExecute(null))
            {
                e.Handled = true;
                viewModel.SendCopilotMessageCommand.Execute(null);
            }

            return;
        }
    }

    private void OnKyraSlashListMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not ListBox box || DataContext is not MainViewModel vm)
        {
            return;
        }

        var hit = box.InputHitTest(e.GetPosition(box)) as DependencyObject;
        while (hit is not null && hit is not ListBoxItem)
        {
            hit = VisualTreeHelper.GetParent(hit);
        }

        if (hit is ListBoxItem { Content: string line })
        {
            var i = vm.KyraSlashSuggestions.IndexOf(line);
            if (i >= 0)
            {
                vm.KyraSlashSelectedIndex = i;
            }
        }
    }

    private void OnKyraSlashSuggestionClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox box || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var hit = box.InputHitTest(e.GetPosition(box)) as DependencyObject;
        while (hit is not null && hit is not ListBoxItem)
        {
            hit = VisualTreeHelper.GetParent(hit);
        }

        if (hit is ListBoxItem { Content: string line })
        {
            e.Handled = true;
            viewModel.InsertKyraSlashSuggestion(line);
            KyraChatInputBox.Focus();
        }
    }

    private void OnCopyCopilotResponseClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: VentoyToolkitSetup.Wpf.Models.CopilotChatMessage message } &&
            !string.IsNullOrWhiteSpace(message.Text))
        {
            Clipboard.SetText(message.Text);
        }
    }

    private void QueueLogScroll()
    {
        _logScrollPending = true;
        if (!_logScrollTimer.IsEnabled)
        {
            _logScrollTimer.Start();
        }
    }

    private void OnLogScrollTimerTick(object? sender, EventArgs e)
    {
        if (_currentViewModel?.AutoScrollLogs != true || string.IsNullOrEmpty(LogTextBox.Text))
        {
            _logScrollPending = false;
            _logScrollTimer.Stop();
            return;
        }

        var lastVisibleLine = LogTextBox.GetLastVisibleLineIndex();
        var lastLine = LogTextBox.LineCount - 1;
        if (lastLine <= 0 || lastVisibleLine >= lastLine - 1)
        {
            if (_logScrollPending)
            {
                LogTextBox.ScrollToEnd();
                _logScrollPending = false;
                return;
            }

            _logScrollTimer.Stop();
            return;
        }

        var nextLine = Math.Min(lastLine, lastVisibleLine + 4);
        LogTextBox.ScrollToLine(nextLine);
        _logScrollPending = lastVisibleLine < lastLine - 1;
    }

    private void OnMainContentPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (IsFromTextBox(e.OriginalSource as DependencyObject))
        {
            return;
        }

        e.Handled = true;
        var offsetDelta = -e.Delta * 0.32;
        MainContentScrollViewer.ScrollToVerticalOffset(MainContentScrollViewer.VerticalOffset + offsetDelta);
    }

    private void OpenKyraAdvancedSettingsWindow()
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        vm.RefreshKyraAssistantPanel();
        var dialog = new KyraAdvancedSettingsWindow
        {
            Owner = this,
            DataContext = vm
        };
        dialog.ShowDialog();
    }

    private void OnSidebarNavigateClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button &&
            button.Tag is string tag &&
            int.TryParse(tag, out var selectedIndex) &&
            selectedIndex >= 0 &&
            selectedIndex < MainTabControl.Items.Count)
        {
            MainTabControl.SelectedIndex = selectedIndex;
            UpdateSidebarSelection();
            Dispatcher.BeginInvoke(UpdateSidebarSelection, DispatcherPriority.Background);
        }
    }

    private void OnMainTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(sender, MainTabControl))
        {
            UpdateSidebarSelection();
            Dispatcher.BeginInvoke(UpdateSidebarSelection, DispatcherPriority.Background);
        }
    }

    private void UpdateSidebarSelection()
    {
        if (NavUsbButton is null)
        {
            return;
        }

        var navButtons = new[]
        {
            NavUsbButton,
            NavSystemButton,
            NavToolkitButton,
            NavDriverHubButton,
            NavCopilotButton,
            NavDiagnosticsButton,
            NavSettingsButton
        };

        for (var index = 0; index < navButtons.Length; index++)
        {
            var selected = index == MainTabControl.SelectedIndex;
            navButtons[index].Background = new SolidColorBrush(selected ? Color.FromRgb(255, 224, 178) : Color.FromArgb(204, 10, 16, 26));
            navButtons[index].BorderBrush = new SolidColorBrush(selected ? Color.FromRgb(255, 159, 67) : Color.FromArgb(102, 87, 199, 232));
            navButtons[index].Foreground = new SolidColorBrush(selected ? Color.FromRgb(8, 8, 12) : Color.FromRgb(232, 240, 255));
            navButtons[index].FontWeight = selected ? FontWeights.Bold : FontWeights.SemiBold;
        }
    }

    private void OnShowFullLogsClick(object sender, RoutedEventArgs e)
    {
        FullLogsOverlay.Visibility = Visibility.Visible;
        LogTextBox.ScrollToEnd();
    }

    private void OnCloseFullLogsClick(object sender, RoutedEventArgs e)
    {
        FullLogsOverlay.Visibility = Visibility.Collapsed;
    }

    private void SupportMailto_OnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private void OnStartUsbBuilderFromBetaWelcomeClick(object sender, RoutedEventArgs e)
    {
        MainTabControl.SelectedIndex = 0;
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.DismissBetaWelcome();
        }
    }

    private void OnRunSystemScanFromWelcomeClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        viewModel.DismissBetaWelcome();
        MainTabControl.SelectedIndex = 1;
        if (viewModel.RunSystemScanCommand.CanExecute(null))
        {
            viewModel.RunSystemScanCommand.Execute(null);
        }
    }

    private void OnRunUsbBenchmarkFromWelcomeClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        viewModel.DismissBetaWelcome();
        MainTabControl.SelectedIndex = 0;
        if (viewModel.RunUsbIntelligenceBenchmarkCommand.CanExecute(null))
        {
            viewModel.RunUsbIntelligenceBenchmarkCommand.Execute(null);
        }
    }

    private void OnOpenLogsFromBetaWelcomeClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && viewModel.OpenLogsFolderCommand.CanExecute(null))
        {
            viewModel.OpenLogsFolderCommand.Execute(null);
        }
    }

    private void OnDismissBetaWelcomeClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.DismissBetaWelcome();
        }
    }

    private static bool IsFromTextBox(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is TextBox)
            {
                return true;
            }

            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return false;
    }
}
