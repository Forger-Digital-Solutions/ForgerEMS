using System.ComponentModel;
using System.Windows;
using VentoyToolkitSetup.Wpf.ViewModels;

namespace VentoyToolkitSetup.Wpf;

public partial class MainWindow : Window
{
    private bool _initialized;
    private MainViewModel? _currentViewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
        AttachToViewModel(viewModel);
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
            ScrollLogsToEnd();
        }
    }

    private void ScrollLogsToEnd()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!string.IsNullOrEmpty(LogTextBox.Text))
            {
                LogTextBox.ScrollToEnd();
            }
        });
    }
}
