using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        DataContext = viewModel;

        _logScrollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(140)
        };
        _logScrollTimer.Tick += OnLogScrollTimerTick;

        SourceInitialized += (_, _) => ClampWindowToWorkArea();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
        AttachToViewModel(viewModel);
    }

    private void ClampWindowToWorkArea()
    {
        const double margin = 24;
        var workArea = SystemParameters.WorkArea;
        var maxWidth = Math.Max(MinWidth, workArea.Width - margin);
        var maxHeight = Math.Max(MinHeight, workArea.Height - margin);

        Width = Math.Min(Width, maxWidth);
        Height = Math.Min(Height, maxHeight);
        MaxWidth = maxWidth;
        MaxHeight = maxHeight;

        Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2);
        Top = workArea.Top + Math.Max(0, (workArea.Height - Height) / 2);

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
            await viewModel.InitializeAsync();
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachFromViewModel(_currentViewModel);
        AttachToViewModel(e.NewValue as MainViewModel);
    }

    private void AttachToViewModel(MainViewModel? viewModel)
    {
        _currentViewModel = viewModel;
        if (_currentViewModel is not null)
        {
            _currentViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void DetachFromViewModel(MainViewModel? viewModel)
    {
        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
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
