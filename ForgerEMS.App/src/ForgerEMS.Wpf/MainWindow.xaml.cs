using System.Collections.Specialized;
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
            _currentViewModel.Logs.CollectionChanged += OnLogsCollectionChanged;
            _currentViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void DetachFromViewModel(MainViewModel? viewModel)
    {
        if (viewModel is not null)
        {
            viewModel.Logs.CollectionChanged -= OnLogsCollectionChanged;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    private void OnLogsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_currentViewModel?.AutoScrollLogs != true || LogListBox.Items.Count == 0)
        {
            return;
        }

        ScrollLogsToEnd();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.AutoScrollLogs) && _currentViewModel?.AutoScrollLogs == true)
        {
            ScrollLogsToEnd();
        }
    }

    private void ScrollLogsToEnd()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (LogListBox.Items.Count > 0)
            {
                LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
            }
        });
    }
}
