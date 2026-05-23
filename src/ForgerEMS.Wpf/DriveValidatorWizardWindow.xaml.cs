using System;
using System.Windows;
using System.Windows.Threading;
using VentoyToolkitSetup.Wpf.ViewModels;

namespace VentoyToolkitSetup.Wpf;

public partial class DriveValidatorWizardWindow : Window
{
    private readonly DriveValidatorWizardViewModel _viewModel;
    private readonly DispatcherTimer _heartbeatTimer;

    public DriveValidatorWizardWindow(DriveValidatorWizardViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;

        viewModel.CloseRequested += (_, accepted) =>
        {
            try
            {
                DialogResult = accepted;
            }
            catch (InvalidOperationException)
            {
                // DialogResult can only be set when shown as a dialog; ignore otherwise.
            }
            Close();
        };

        // Heartbeat: fire once a second so the running step can show "Still writing… (12s)"
        // when a long I/O phase has not emitted a progress event. The VM decides whether to
        // surface the heartbeat based on time since the last progress callback.
        _heartbeatTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _heartbeatTimer.Tick += (_, _) => _viewModel.TickHeartbeat();
        _heartbeatTimer.Start();
        Closed += (_, _) =>
        {
            _heartbeatTimer.Stop();
            _viewModel.Dispose();
        };
    }
}
