using System.ComponentModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Navigation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using VentoyToolkitSetup.Wpf.ViewModels;

namespace VentoyToolkitSetup.Wpf;

public partial class MainWindow : Window
{
    private bool _initialized;
    private bool _logScrollPending;
    private bool _backgroundFlowBoost;
    private bool _animatedBackgroundEnabled = true;
    private bool _suppressBackgroundSettingsEvents;
    private BackgroundDetailLevel _backgroundDetail = BackgroundDetailLevel.Medium;
    private DateTime _backgroundAnimationStartedAtUtc = DateTime.UtcNow;
    private DateTime _targetPulseUntilUtc = DateTime.MinValue;
    private Border? _activeToolNode;
    private System.Windows.Shapes.Path? _activeGroupPulsePath;
    private readonly Random _traceRandom = new();
    private readonly List<TraceParticle> _traceParticles = [];
    private readonly TraceRoute[] _traceRoutes =
    [
        new([new(0.50, 0.28), new(0.50, 0.42), new(0.50, 0.54)]),
        new([new(0.31, 0.20), new(0.43, 0.22), new(0.50, 0.25)]),
        new([new(0.28, 0.42), new(0.40, 0.44), new(0.49, 0.52)]),
        new([new(0.70, 0.22), new(0.61, 0.23), new(0.52, 0.26)]),
        new([new(0.70, 0.46), new(0.60, 0.48), new(0.52, 0.53)]),
        new([new(0.72, 0.66), new(0.62, 0.58), new(0.54, 0.53)]),
        new([new(0.29, 0.84), new(0.40, 0.73), new(0.48, 0.58)]),
        new([new(0.50, 0.86), new(0.50, 0.70), new(0.50, 0.56)]),
        new([new(0.11, 0.34), new(0.25, 0.34), new(0.37, 0.43)]),
        new([new(0.89, 0.63), new(0.77, 0.63), new(0.62, 0.55)]),
        new([new(0.18, 0.18), new(0.18, 0.36), new(0.32, 0.36), new(0.32, 0.52)]),
        new([new(0.82, 0.18), new(0.82, 0.34), new(0.68, 0.34), new(0.68, 0.50)]),
        new([new(0.42, 0.12), new(0.58, 0.12), new(0.58, 0.28), new(0.42, 0.28)]),
        new([new(0.14, 0.58), new(0.30, 0.58), new(0.30, 0.72), new(0.46, 0.72)]),
        new([new(0.86, 0.40), new(0.86, 0.54), new(0.72, 0.54), new(0.72, 0.68)]),
        new([new(0.36, 0.62), new(0.52, 0.62), new(0.52, 0.76), new(0.66, 0.76)]),
        new([new(0.50, 0.18), new(0.62, 0.24), new(0.62, 0.38), new(0.50, 0.44)]),
        new([new(0.24, 0.26), new(0.36, 0.32), new(0.36, 0.46), new(0.24, 0.52)]),
        new([new(0.64, 0.30), new(0.76, 0.36), new(0.76, 0.50), new(0.64, 0.56)]),
        new([new(0.08, 0.48), new(0.20, 0.48), new(0.20, 0.62), new(0.32, 0.62)]),
        new([new(0.92, 0.28), new(0.92, 0.42), new(0.80, 0.42), new(0.80, 0.56)]),
        new([new(0.46, 0.34), new(0.54, 0.40), new(0.54, 0.50), new(0.46, 0.56)]),
        new([new(0.30, 0.66), new(0.42, 0.60), new(0.54, 0.66), new(0.66, 0.60)])
    ];
    private MainViewModel? _currentViewModel;
    private readonly DispatcherTimer _logScrollTimer;
    private readonly DispatcherTimer _backgroundAnimationTimer;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        CenterWindowForLaunch();
        KeepAnimatedBackgroundParticleOnly();
        FreezeBackgroundFreezables(CircuitBackgroundLayer);

        _backgroundAnimationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        _backgroundAnimationTimer.Tick += OnBackgroundAnimationTimerTick;

        LoadBackgroundSettings();
        ApplyBackgroundSettings(updateControls: true);
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
        ApplyBackgroundSettings(updateControls: false);
        if (DataContext is MainViewModel viewModel)
        {
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
            UpdateCircuitBackgroundActivity();
            UpdateManagedPackageGlow();
        }
    }

    private void DetachFromViewModel(MainViewModel? viewModel)
    {
        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            viewModel.CopilotMessages.CollectionChanged -= OnCopilotMessagesChanged;
            viewModel.OpenKyraAdvancedSettingsAction = null;
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

        if (e.PropertyName == nameof(MainViewModel.SelectedUsbTarget) &&
            _animatedBackgroundEnabled &&
            _currentViewModel?.SelectedUsbTarget is not null)
        {
            TriggerTargetUsbPulse();
        }

        if (e.PropertyName == nameof(MainViewModel.IsBusy) ||
            e.PropertyName == nameof(MainViewModel.CurrentTaskText) ||
            e.PropertyName == nameof(MainViewModel.CurrentTaskState) ||
            e.PropertyName == nameof(MainViewModel.SelectedUsbTarget))
        {
            UpdateCircuitBackgroundActivity();
        }

        if (e.PropertyName == nameof(MainViewModel.ManagedSummaryStatusText) ||
            e.PropertyName == nameof(MainViewModel.ManagedSummaryText))
        {
            UpdateManagedPackageGlow();
            UpdateActiveToolFlow();
        }
    }

    private void OnCopilotMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() => CopilotChatScrollViewer?.ScrollToEnd(), DispatcherPriority.Background);
    }

    private void OnCopilotInputPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            return;
        }

        if (DataContext is MainViewModel viewModel && viewModel.SendCopilotMessageCommand.CanExecute(null))
        {
            e.Handled = true;
            viewModel.SendCopilotMessageCommand.Execute(null);
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
        }
    }

    private void OnMainTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(sender, MainTabControl))
        {
            UpdateSidebarSelection();
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

    private void UpdateCircuitBackgroundActivity()
    {
        if (!_animatedBackgroundEnabled)
        {
            _backgroundFlowBoost = false;
            StopBackgroundAnimationTimer();
            return;
        }

        _backgroundFlowBoost = _currentViewModel?.IsBusy == true &&
                               _currentViewModel.SelectedUsbTarget is not null &&
                               IsUsbFlowTask(_currentViewModel.CurrentTaskText);
        UpdateActiveToolFlow();
        EnsureBackgroundAnimationTimer();
    }

    private void UpdateManagedPackageGlow()
    {
        var state = GetManagedSummaryState();
        var packageGroups = GetPackageGroups().ToList();
        ResetPackageGroupGlow(packageGroups);

        if (!_animatedBackgroundEnabled || state == ManagedSummaryVisualState.Neutral)
        {
            return;
        }

        if (state == ManagedSummaryVisualState.Ready)
        {
            foreach (var packageGroup in packageGroups)
            {
                BeginSoftReadyGlow(packageGroup.Group, packageGroup.BaseOpacity);
            }

            return;
        }

        var summaryText = _currentViewModel?.ManagedSummaryText ?? string.Empty;
        var affectedGroups = packageGroups
            .Where(packageGroup => packageGroup.Tokens.Any(token => summaryText.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (affectedGroups.Count == 0)
        {
            affectedGroups = packageGroups;
        }

        foreach (var packageGroup in affectedGroups)
        {
            BeginWarningGlow(packageGroup.Group, packageGroup.Halo, packageGroup.BaseOpacity, state == ManagedSummaryVisualState.Drift);
        }
    }

    private void OnAnimatedBackgroundSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressBackgroundSettingsEvents)
        {
            return;
        }

        _animatedBackgroundEnabled = AnimatedBackgroundCheckBox.IsChecked == true;
        ApplyBackgroundSettings(updateControls: true);
        SaveBackgroundSettings();
    }

    private void OnBackgroundDetailSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressBackgroundSettingsEvents)
        {
            return;
        }

        _backgroundDetail = BackgroundDetailComboBox.SelectedIndex switch
        {
            0 => BackgroundDetailLevel.Low,
            2 => BackgroundDetailLevel.High,
            _ => BackgroundDetailLevel.Medium
        };
        ApplyBackgroundSettings(updateControls: true);
        SaveBackgroundSettings();
    }

    private void ApplyBackgroundSettings(bool updateControls)
    {
        if (updateControls)
        {
            _suppressBackgroundSettingsEvents = true;
            try
            {
                AnimatedBackgroundCheckBox.IsChecked = _animatedBackgroundEnabled;
                AnimatedBackgroundCheckBox.Content = _animatedBackgroundEnabled ? "On" : "Off";
                BackgroundDetailComboBox.SelectedIndex = _backgroundDetail switch
                {
                    BackgroundDetailLevel.Low => 0,
                    BackgroundDetailLevel.High => 2,
                    _ => 1
                };
            }
            finally
            {
                _suppressBackgroundSettingsEvents = false;
            }
        }

        ApplyBackgroundDetail();
        ApplyAnimationPreference();
    }

    private void ApplyAnimationPreference()
    {
        ConfigureTraceParticles();

        if (_animatedBackgroundEnabled)
        {
            CommandCenterBackgroundImage.Opacity = 1;
            BackgroundReadabilityVeil.Opacity = GetBackgroundVeilOpacity(animated: true);
            CircuitBackgroundLayer.Opacity = GetBackgroundLayerOpacity(animated: true);
            TargetUsbGlow.Opacity = GetTargetGlowOpacity();
            TargetPulseRing.Opacity = GetTargetRingOpacity();
            SetPulsePathVisibility(Visibility.Collapsed);
            foreach (var path in GetPulsePaths())
            {
                path.Visibility = Visibility.Visible;
            }
            UpdateCircuitBackgroundActivity();
            UpdateManagedPackageGlow();
            return;
        }

        _backgroundFlowBoost = false;
        StopBackgroundAnimationTimer();
        HideTraceParticles();
        CommandCenterBackgroundImage.Opacity = 1;
        BackgroundReadabilityVeil.Opacity = GetBackgroundVeilOpacity(animated: false);
        CircuitBackgroundLayer.Opacity = GetBackgroundLayerOpacity(animated: false);
        TargetUsbGlow.Opacity = GetTargetGlowOpacity() * 0.55;
        TargetPulseRing.Opacity = 0;
        SetPulsePathVisibility(Visibility.Collapsed);
        ClearActiveToolFlow();
        ResetToolNodePulse();
        ResetPackageGroupGlow(GetPackageGroups().ToList());
    }

    private void ApplyBackgroundDetail()
    {
        ApplyPackageGroupDetail(OsPackageGroup);
        ApplyPackageGroupDetail(RecoveryPackageGroup);
        ApplyPackageGroupDetail(DiagnosticsPackageGroup);
        ApplyPackageGroupDetail(WindowsToolsPackageGroup);
        ApplyPackageGroupDetail(MedicatPackageGroup);
        ApplyPackageGroupDetail(UsbBuildersPackageGroup);
        ResetToolNodePulse();
        KeepAnimatedBackgroundParticleOnly();
    }

    private void KeepAnimatedBackgroundParticleOnly()
    {
        CircuitBackgroundLayer.IsHitTestVisible = false;
        TraceLightCanvas.IsHitTestVisible = false;
        TraceLightCanvas.Background = Brushes.Transparent;

        foreach (UIElement child in CircuitTraceCanvas.Children)
        {
            child.Visibility = IsAnimatedTraceElement(child)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private bool IsAnimatedTraceElement(UIElement element)
    {
        return ReferenceEquals(element, OsToVentoyPulse) ||
               ReferenceEquals(element, RecoveryToVentoyPulse) ||
               ReferenceEquals(element, DiagnosticsToVentoyPulse) ||
               ReferenceEquals(element, WindowsToolsToVentoyPulse) ||
               ReferenceEquals(element, MedicatToVentoyPulse) ||
               ReferenceEquals(element, VentoyToUsbPulse) ||
               ReferenceEquals(element, UsbBuildersToVentoyPulse) ||
               ReferenceEquals(element, TargetPulseRing) ||
               ReferenceEquals(element, TargetUsbPulseNode);
    }

    private double GetBackgroundLayerOpacity(bool animated)
    {
        return _backgroundDetail switch
        {
            BackgroundDetailLevel.Low => animated ? 0.18 : 0.08,
            BackgroundDetailLevel.High => animated ? 0.34 : 0.16,
            _ => animated ? 0.26 : 0.12
        };
    }

    private double GetBackgroundVeilOpacity(bool animated)
    {
        return _backgroundDetail switch
        {
            BackgroundDetailLevel.Low => animated ? 0.66 : 0.68,
            BackgroundDetailLevel.High => animated ? 0.55 : 0.58,
            _ => animated ? 0.62 : 0.64
        };
    }

    private double GetTargetGlowOpacity()
    {
        return _backgroundDetail switch
        {
            BackgroundDetailLevel.Low => 0.28,
            BackgroundDetailLevel.High => 0.4,
            _ => 0.34
        };
    }

    private double GetTargetRingOpacity()
    {
        return _backgroundDetail switch
        {
            BackgroundDetailLevel.Low => 0.06,
            BackgroundDetailLevel.High => 0.16,
            _ => 0.1
        };
    }

    private int GetTraceParticleCount()
    {
        if (!_animatedBackgroundEnabled)
        {
            return 0;
        }

        return _backgroundDetail switch
        {
            BackgroundDetailLevel.Low => 16,
            BackgroundDetailLevel.High => 48,
            _ => 32
        };
    }

    private void ApplyPackageGroupDetail(Panel group)
    {
        foreach (UIElement child in group.Children)
        {
            var tag = (child as FrameworkElement)?.Tag as string;
            child.Visibility = _backgroundDetail switch
            {
                BackgroundDetailLevel.Low => tag is "CoreNode" or "ControllerNode" ? Visibility.Visible : Visibility.Collapsed,
                BackgroundDetailLevel.High => tag is "CoreNode" or "ControllerNode" or "CategoryNode" or "CategoryLabel" or "Satellite" ? Visibility.Visible : Visibility.Collapsed,
                _ => tag is "CoreNode" or "ControllerNode" or "CategoryNode" or "CategoryLabel" ? Visibility.Visible : Visibility.Collapsed
            };
        }
    }

    private void SetPulsePathVisibility(Visibility visibility)
    {
        foreach (var path in GetPulsePaths())
        {
            path.Visibility = visibility;
        }
    }

    private void ConfigureTraceParticles()
    {
        TraceLightCanvas.Children.Clear();
        _traceParticles.Clear();

        var particleCount = GetTraceParticleCount();
        if (particleCount == 0)
        {
            return;
        }

        var colors = new[]
        {
            Color.FromRgb(56, 189, 248),
            Color.FromRgb(34, 211, 238),
            Color.FromRgb(34, 197, 94),
            Color.FromRgb(245, 158, 11),
            Color.FromRgb(168, 85, 247)
        };

        for (var index = 0; index < particleCount; index++)
        {
            var route = _traceRoutes[index % _traceRoutes.Length];
            var color = colors[index % colors.Length];
            var brush = new SolidColorBrush(color);
            brush.Freeze();

            var size = 2.6 + (_traceRandom.NextDouble() * 3.8);
            var light = new Ellipse
            {
                Width = size * 2.6,
                Height = size,
                Fill = brush,
                Opacity = 0,
                IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };

            var transform = new TranslateTransform();
            light.RenderTransform = transform;
            TraceLightCanvas.Children.Add(light);

            _traceParticles.Add(new TraceParticle(
                light,
                transform,
                route,
                _traceRandom.NextDouble(),
                0.035 + (_traceRandom.NextDouble() * 0.07),
                _traceRandom.Next(0, 2) == 0 ? -1 : 1,
                size));
        }
    }

    private void HideTraceParticles()
    {
        foreach (var particle in _traceParticles)
        {
            particle.Visual.Opacity = 0;
        }
    }

    private void UpdateTraceParticles(double elapsed)
    {
        if (!_animatedBackgroundEnabled || _traceParticles.Count == 0)
        {
            HideTraceParticles();
            return;
        }

        var bounds = GetFittedBackgroundBounds();
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        var detailOpacity = _backgroundDetail switch
        {
            BackgroundDetailLevel.Low => 0.48,
            BackgroundDetailLevel.High => 0.82,
            _ => 0.64
        };

        foreach (var particle in _traceParticles)
        {
            var progress = (particle.StartOffset + (elapsed * particle.Speed)) % 1.0;
            if (particle.Direction < 0)
            {
                progress = 1.0 - progress;
            }

            var routePoint = particle.Route.GetPoint(progress);
            particle.Transform.X = bounds.Left + (routePoint.X * bounds.Width) - particle.Size;
            particle.Transform.Y = bounds.Top + (routePoint.Y * bounds.Height) - (particle.Size / 2);
            particle.Visual.Opacity = detailOpacity * (0.72 + (0.28 * Math.Sin((elapsed * 3.2) + particle.StartOffset)));
        }
    }

    private Rect GetFittedBackgroundBounds()
    {
        var availableWidth = TraceLightCanvas.ActualWidth;
        var availableHeight = TraceLightCanvas.ActualHeight;
        var sourceWidth = CommandCenterBackgroundImage.Source?.Width ?? 1536;
        var sourceHeight = CommandCenterBackgroundImage.Source?.Height ?? 1024;

        if (availableWidth <= 0 || availableHeight <= 0 || sourceWidth <= 0 || sourceHeight <= 0)
        {
            return Rect.Empty;
        }

        var scale = Math.Min(availableWidth / sourceWidth, availableHeight / sourceHeight);
        var fittedWidth = sourceWidth * scale;
        var fittedHeight = sourceHeight * scale;

        return new Rect(
            (availableWidth - fittedWidth) / 2,
            (availableHeight - fittedHeight) / 2,
            fittedWidth,
            fittedHeight);
    }

    private IEnumerable<System.Windows.Shapes.Path> GetPulsePaths()
    {
        yield return OsToVentoyPulse;
        yield return RecoveryToVentoyPulse;
        yield return DiagnosticsToVentoyPulse;
        yield return WindowsToolsToVentoyPulse;
        yield return MedicatToVentoyPulse;
        yield return UsbBuildersToVentoyPulse;
        yield return VentoyToUsbPulse;
    }

    private void OnPackageGroupMouseEnter(object sender, MouseEventArgs e)
    {
        if (!_animatedBackgroundEnabled || sender is not Panel activeGroup)
        {
            return;
        }

        foreach (var packageGroup in GetPackageGroups())
        {
            packageGroup.Group.Opacity = ReferenceEquals(packageGroup.Group, activeGroup)
                ? Math.Min(0.98, packageGroup.BaseOpacity + 0.18)
                : Math.Max(0.3, packageGroup.BaseOpacity * 0.48);
        }
    }

    private void OnPackageGroupMouseLeave(object sender, MouseEventArgs e)
    {
        if (!_animatedBackgroundEnabled)
        {
            return;
        }

        UpdateManagedPackageGlow();
    }

    private void LoadBackgroundSettings()
    {
        var settingsPath = GetBackgroundSettingsPath();
        if (!File.Exists(settingsPath))
        {
            return;
        }

        try
        {
            foreach (var line in File.ReadAllLines(settingsPath))
            {
                var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2)
                {
                    continue;
                }

                if (parts[0].Equals("AnimatedBackground", StringComparison.OrdinalIgnoreCase) &&
                    bool.TryParse(parts[1], out var animatedBackground))
                {
                    _animatedBackgroundEnabled = animatedBackground;
                }
                else if (parts[0].Equals("BackgroundDetail", StringComparison.OrdinalIgnoreCase) &&
                         Enum.TryParse<BackgroundDetailLevel>(parts[1], ignoreCase: true, out var backgroundDetail))
                {
                    _backgroundDetail = backgroundDetail;
                }
            }
        }
        catch
        {
            _animatedBackgroundEnabled = true;
            _backgroundDetail = BackgroundDetailLevel.Medium;
        }
    }

    private void SaveBackgroundSettings()
    {
        try
        {
            var settingsPath = GetBackgroundSettingsPath();
            var settingsDirectory = System.IO.Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrWhiteSpace(settingsDirectory))
            {
                Directory.CreateDirectory(settingsDirectory);
            }

            File.WriteAllLines(
                settingsPath,
                [
                    $"AnimatedBackground={_animatedBackgroundEnabled}",
                    $"BackgroundDetail={_backgroundDetail}"
                ]);
        }
        catch
        {
            // UI preferences are best-effort; app operations should never depend on them.
        }
    }

    private static string GetBackgroundSettingsPath()
    {
        return System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ForgerDigitalSolutions",
            "ForgerEMS",
            "ui-settings.txt");
    }

    private static void ResetPackageGroupGlow(IEnumerable<PackageGroupVisual> packageGroups)
    {
        foreach (var packageGroup in packageGroups)
        {
            packageGroup.Group.Opacity = packageGroup.BaseOpacity;
            packageGroup.Halo.Opacity = 0;
        }
    }

    private void ResetToolNodePulse()
    {
        foreach (var toolNode in GetToolNodes())
        {
            toolNode.Node.Opacity = GetToolNodeBaseOpacity();
        }
    }

    private double GetToolNodeBaseOpacity()
    {
        return _backgroundDetail switch
        {
            BackgroundDetailLevel.High => 0.78,
            BackgroundDetailLevel.Medium => 0.68,
            _ => 0.58
        };
    }

    private void ClearActiveToolFlow()
    {
        _activeToolNode = null;
        _activeGroupPulsePath = null;
    }

    private void UpdateActiveToolFlow()
    {
        ClearActiveToolFlow();

        if (!_animatedBackgroundEnabled || !_backgroundFlowBoost)
        {
            ResetToolNodePulse();
            return;
        }

        var signalText = string.Join(
            " ",
            _currentViewModel?.CurrentTaskText,
            _currentViewModel?.ManagedSummaryStatusText,
            _currentViewModel?.ManagedSummaryText,
            _currentViewModel?.LastCommandText);

        var activeTool = GetToolNodes()
            .FirstOrDefault(tool => tool.Tokens.Any(token => signalText.Contains(token, StringComparison.OrdinalIgnoreCase)));

        if (activeTool is null && signalText.Contains("Ventoy", StringComparison.OrdinalIgnoreCase))
        {
            activeTool = GetToolNodes().FirstOrDefault(tool => ReferenceEquals(tool.Node, VentoyToolNode));
        }

        if (activeTool is not null)
        {
            _activeToolNode = activeTool.Node;
            _activeGroupPulsePath = activeTool.GroupPulsePath;
        }

        ResetToolNodePulse();
    }

    private static void BeginSoftReadyGlow(UIElement group, double baseOpacity)
    {
        group.Opacity = Math.Min(0.94, baseOpacity + 0.08);
    }

    private static void BeginWarningGlow(UIElement group, Shape halo, double baseOpacity, bool isDrift)
    {
        group.Opacity = Math.Min(0.96, baseOpacity + 0.12);
        var stroke = new SolidColorBrush(isDrift ? Color.FromRgb(248, 113, 113) : Color.FromRgb(245, 158, 11));
        stroke.Freeze();
        halo.Stroke = stroke;
        halo.Opacity = isDrift ? 0.42 : 0.32;
    }

    private ManagedSummaryVisualState GetManagedSummaryState()
    {
        var status = _currentViewModel?.ManagedSummaryStatusText ?? string.Empty;
        var summary = _currentViewModel?.ManagedSummaryText ?? string.Empty;
        var combined = status + " " + summary;

        if (combined.Contains("DRIFT", StringComparison.OrdinalIgnoreCase))
        {
            return ManagedSummaryVisualState.Drift;
        }

        if (combined.Contains("WARN", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("OK-LIMITED", StringComparison.OrdinalIgnoreCase))
        {
            return ManagedSummaryVisualState.Warning;
        }

        if (combined.Contains("OK", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("READY", StringComparison.OrdinalIgnoreCase))
        {
            return ManagedSummaryVisualState.Ready;
        }

        return ManagedSummaryVisualState.Neutral;
    }

    private IEnumerable<PackageGroupVisual> GetPackageGroups()
    {
        yield return new PackageGroupVisual(
            OsPackageGroup,
            OsGroupHalo,
            GetDetailGroupOpacity(0.78),
            "Windows",
            "Ubuntu",
            "Kali",
            "Linux Mint",
            "Mint");

        yield return new PackageGroupVisual(
            RecoveryPackageGroup,
            RecoveryGroupHalo,
            GetDetailGroupOpacity(0.74),
            "Rescuezilla",
            "Clonezilla",
            "GParted",
            "SystemRescue");

        yield return new PackageGroupVisual(
            DiagnosticsPackageGroup,
            DiagnosticsGroupHalo,
            GetDetailGroupOpacity(0.66),
            "MemTest86",
            "MemTest86+",
            "HWInfo",
            "HWiNFO",
            "CrystalDiskInfo",
            "Crystal Disk");

        yield return new PackageGroupVisual(
            WindowsToolsPackageGroup,
            WindowsToolsGroupHalo,
            GetDetailGroupOpacity(0.64),
            "DriverStoreExplorer",
            "Driver Store Explorer",
            "RustDesk",
            "Angry IP",
            "Angry IP Scanner");

        yield return new PackageGroupVisual(
            UsbBuildersPackageGroup,
            UsbBuildersGroupHalo,
            GetDetailGroupOpacity(0.82),
            "Ventoy",
            "Rufus",
            "balenaEtcher",
            "Etcher");

        yield return new PackageGroupVisual(
            MedicatPackageGroup,
            MedicatGroupHalo,
            GetDetailGroupOpacity(0.74),
            "Medicat",
            "MediCat",
            "Medicat USB");
    }

    private IEnumerable<ToolNodeVisual> GetToolNodes()
    {
        yield return new ToolNodeVisual(UbuntuToolNode, OsToVentoyPulse, "Ubuntu");
        yield return new ToolNodeVisual(KaliToolNode, OsToVentoyPulse, "Kali", "Kali Linux");
        yield return new ToolNodeVisual(LinuxMintToolNode, OsToVentoyPulse, "Linux Mint", "Mint");

        yield return new ToolNodeVisual(ClonezillaToolNode, RecoveryToVentoyPulse, "Clonezilla");
        yield return new ToolNodeVisual(GPartedToolNode, RecoveryToVentoyPulse, "GParted");
        yield return new ToolNodeVisual(SystemRescueToolNode, RecoveryToVentoyPulse, "SystemRescue", "System Rescue");

        yield return new ToolNodeVisual(MemTestToolNode, DiagnosticsToVentoyPulse, "MemTest86", "MemTest86+", "MemTest");
        yield return new ToolNodeVisual(HwInfoToolNode, DiagnosticsToVentoyPulse, "HWInfo", "HWiNFO");
        yield return new ToolNodeVisual(CrystalDiskInfoToolNode, DiagnosticsToVentoyPulse, "CrystalDiskInfo", "Crystal Disk");

        yield return new ToolNodeVisual(DriverStoreExplorerToolNode, WindowsToolsToVentoyPulse, "DriverStoreExplorer", "Driver Store Explorer");
        yield return new ToolNodeVisual(RustDeskToolNode, WindowsToolsToVentoyPulse, "RustDesk");
        yield return new ToolNodeVisual(AngryIpScannerToolNode, WindowsToolsToVentoyPulse, "Angry IP", "Angry IP Scanner");

        yield return new ToolNodeVisual(VentoyToolNode, UsbBuildersToVentoyPulse, "Ventoy");
        yield return new ToolNodeVisual(RufusToolNode, UsbBuildersToVentoyPulse, "Rufus");
        yield return new ToolNodeVisual(BalenaEtcherToolNode, UsbBuildersToVentoyPulse, "balenaEtcher", "Etcher");
    }

    private double GetDetailGroupOpacity(double mediumOpacity)
    {
        return _backgroundDetail switch
        {
            BackgroundDetailLevel.Low => Math.Max(0.58, mediumOpacity - 0.14),
            BackgroundDetailLevel.High => Math.Min(0.96, mediumOpacity + 0.12),
            _ => mediumOpacity
        };
    }

    private void TriggerTargetUsbPulse()
    {
        _targetPulseUntilUtc = DateTime.UtcNow.AddSeconds(1.1);
        EnsureBackgroundAnimationTimer();
    }

    private void EnsureBackgroundAnimationTimer()
    {
        if (!_animatedBackgroundEnabled)
        {
            return;
        }

        if (!_backgroundAnimationTimer.IsEnabled)
        {
            _backgroundAnimationStartedAtUtc = DateTime.UtcNow;
            _backgroundAnimationTimer.Start();
        }
    }

    private void StopBackgroundAnimationTimer()
    {
        if (_backgroundAnimationTimer.IsEnabled)
        {
            _backgroundAnimationTimer.Stop();
        }

        foreach (var path in GetPulsePaths())
        {
            path.Opacity = 0;
            path.StrokeDashOffset = 0;
        }

        ClearActiveToolFlow();
        ResetToolNodePulse();
        HideTraceParticles();
    }

    private void OnBackgroundAnimationTimerTick(object? sender, EventArgs e)
    {
        if (!_animatedBackgroundEnabled)
        {
            StopBackgroundAnimationTimer();
            return;
        }

        var elapsed = (DateTime.UtcNow - _backgroundAnimationStartedAtUtc).TotalSeconds;
        UpdateTraceParticles(elapsed);

        var pulse = 0.5 + (0.5 * Math.Sin(elapsed * Math.PI));
        var detailBoost = _backgroundDetail switch
        {
            BackgroundDetailLevel.Low => 0.78,
            BackgroundDetailLevel.High => 1.22,
            _ => 1.0
        };
        foreach (var path in GetPulsePaths())
        {
            path.Opacity = 0;
        }

        OsToVentoyPulse.StrokeDashOffset = -elapsed * 6.2;
        VentoyToUsbPulse.StrokeDashOffset = -elapsed * 9.4;

        OsToVentoyPulse.Opacity = ((_backgroundFlowBoost ? 0.08 : 0.16) + (0.12 * pulse)) * detailBoost;
        VentoyToUsbPulse.Opacity = ((_backgroundFlowBoost ? 0.46 : 0.22) + (0.28 * pulse)) * detailBoost;

        if (_activeGroupPulsePath is not null)
        {
            _activeGroupPulsePath.StrokeDashOffset = -elapsed * 8.8;
            _activeGroupPulsePath.Opacity = (0.42 + (0.34 * pulse)) * detailBoost;
        }

        ResetToolNodePulse();
        if (_activeToolNode is not null && _backgroundDetail == BackgroundDetailLevel.High)
        {
            _activeToolNode.Opacity = Math.Min(1.0, GetToolNodeBaseOpacity() + (0.22 * pulse));
        }

        var remainingPulse = (_targetPulseUntilUtc - DateTime.UtcNow).TotalSeconds;
        if (remainingPulse > 0)
        {
            var targetPulse = Math.Clamp(remainingPulse / 1.1, 0, 1);
            TargetUsbGlow.Opacity = GetTargetGlowOpacity() + (0.38 * targetPulse);
            TargetPulseRing.Opacity = GetTargetRingOpacity() + (0.22 * targetPulse);
        }
        else
        {
            TargetUsbGlow.Opacity = _backgroundFlowBoost ? GetTargetGlowOpacity() + 0.12 : GetTargetGlowOpacity();
            TargetPulseRing.Opacity = _backgroundFlowBoost ? GetTargetRingOpacity() + 0.06 : GetTargetRingOpacity();
        }
    }

    private static void FreezeBackgroundFreezables(DependencyObject root)
    {
        TryFreeze(root);

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            FreezeBackgroundFreezables(VisualTreeHelper.GetChild(root, index));
        }

        if (root is Shape shape)
        {
            TryFreeze(shape.Fill);
            TryFreeze(shape.Stroke);
            if (shape is System.Windows.Shapes.Path path)
            {
                TryFreeze(path.Data);
            }
        }
    }

    private static void TryFreeze(object? value)
    {
        if (value is Freezable freezable && freezable.CanFreeze)
        {
            freezable.Freeze();
        }
    }

    private static bool IsUsbFlowTask(string taskText)
    {
        if (string.IsNullOrWhiteSpace(taskText))
        {
            return false;
        }

        return taskText.Contains("Download", StringComparison.OrdinalIgnoreCase) ||
               taskText.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
               taskText.Contains("Setup USB", StringComparison.OrdinalIgnoreCase) ||
               taskText.Contains("Ventoy", StringComparison.OrdinalIgnoreCase) ||
               taskText.Contains("Install", StringComparison.OrdinalIgnoreCase);
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

    private enum ManagedSummaryVisualState
    {
        Neutral,
        Ready,
        Warning,
        Drift
    }

    private enum BackgroundDetailLevel
    {
        Low,
        Medium,
        High
    }

    private sealed record PackageGroupVisual(
        UIElement Group,
        Shape Halo,
        double BaseOpacity,
        params string[] Tokens);

    private sealed record ToolNodeVisual(
        Border Node,
        System.Windows.Shapes.Path GroupPulsePath,
        params string[] Tokens);

    private sealed record TraceParticle(
        Ellipse Visual,
        TranslateTransform Transform,
        TraceRoute Route,
        double StartOffset,
        double Speed,
        int Direction,
        double Size);

    private sealed record TraceRoute(Point[] Points)
    {
        public Point GetPoint(double progress)
        {
            if (Points.Length == 0)
            {
                return new Point(0.5, 0.5);
            }

            if (Points.Length == 1)
            {
                return Points[0];
            }

            var totalLength = 0.0;
            for (var index = 1; index < Points.Length; index++)
            {
                totalLength += GetDistance(Points[index - 1], Points[index]);
            }

            if (totalLength <= 0)
            {
                return Points[0];
            }

            var targetLength = Math.Clamp(progress, 0, 1) * totalLength;
            var walkedLength = 0.0;

            for (var index = 1; index < Points.Length; index++)
            {
                var start = Points[index - 1];
                var end = Points[index];
                var segmentLength = GetDistance(start, end);

                if (walkedLength + segmentLength >= targetLength)
                {
                    var segmentProgress = segmentLength <= 0
                        ? 0
                        : (targetLength - walkedLength) / segmentLength;
                    return new Point(
                        start.X + ((end.X - start.X) * segmentProgress),
                        start.Y + ((end.Y - start.Y) * segmentProgress));
                }

                walkedLength += segmentLength;
            }

            return Points[^1];
        }

        private static double GetDistance(Point first, Point second)
        {
            var deltaX = second.X - first.X;
            var deltaY = second.Y - first.Y;
            return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        }
    }

}
