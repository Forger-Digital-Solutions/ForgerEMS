using System.Windows;

namespace VentoyToolkitSetup.Wpf;

public partial class ScrollableInfoWindow : Window
{
    private readonly Action? _action;

    public ScrollableInfoWindow(string title, string body, Window? owner, string? actionText = null, Action? action = null)
    {
        InitializeComponent();
        Title = title;
        if (owner is not null)
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        BodyTextBlock.Text = body;
        _action = action;
        if (!string.IsNullOrWhiteSpace(actionText) && action is not null)
        {
            ActionButton.Content = actionText;
            ActionButton.Visibility = Visibility.Visible;
        }
    }

    public static void Show(Window? owner, string title, string body)
    {
        new ScrollableInfoWindow(title, body, owner).ShowDialog();
    }

    public static void ShowModeless(Window? owner, string title, string body, string? actionText = null, Action? action = null)
    {
        new ScrollableInfoWindow(title, body, owner, actionText, action).Show();
    }

    private void OnActionClick(object sender, RoutedEventArgs e)
    {
        _action?.Invoke();
        Close();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
