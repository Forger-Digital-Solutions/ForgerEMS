using System.Windows;
using System.Windows.Controls;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf;

public partial class KyraAdvancedSettingsWindow : Window
{
    public KyraAdvancedSettingsWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnProviderPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox { DataContext: CopilotProviderSettingView provider } box)
        {
            provider.SessionApiKey = box.Password;
        }
    }
}
