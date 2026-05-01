using System.Windows;

namespace VentoyToolkitSetup.Wpf;

public partial class ScrollableInfoWindow : Window
{
    public ScrollableInfoWindow(string title, string body, Window? owner)
    {
        InitializeComponent();
        Title = title;
        Owner = owner;
        BodyTextBlock.Text = body;
    }

    public static void Show(Window? owner, string title, string body)
    {
        new ScrollableInfoWindow(title, body, owner).ShowDialog();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
