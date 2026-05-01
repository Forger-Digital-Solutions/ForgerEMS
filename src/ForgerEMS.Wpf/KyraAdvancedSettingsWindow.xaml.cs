using System.Windows;

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
}
