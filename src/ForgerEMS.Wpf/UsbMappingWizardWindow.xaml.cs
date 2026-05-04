using System.Windows;
using VentoyToolkitSetup.Wpf.ViewModels;

namespace VentoyToolkitSetup.Wpf;

public partial class UsbMappingWizardWindow : Window
{
    public UsbMappingWizardWindow(UsbMappingWizardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += (_, accepted) =>
        {
            DialogResult = accepted;
            Close();
        };
    }
}
