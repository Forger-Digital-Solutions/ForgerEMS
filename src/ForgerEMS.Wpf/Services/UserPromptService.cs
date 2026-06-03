using System.Windows;
using System.Windows.Controls;

namespace VentoyToolkitSetup.Wpf.Services;

public interface IUserPromptService
{
    bool Confirm(string title, string message);

    string? PromptText(string title, string message, string initialValue = "");

    void ShowMessage(string title, string message, MessageBoxImage image = MessageBoxImage.Information);

    /// <summary>Returns the zero-based index of the chosen option, or null when cancelled.</summary>
    int? PickOption(string title, string message, IReadOnlyList<string> options);
}

public sealed class UserPromptService : IUserPromptService
{
    public bool Confirm(string title, string message)
    {
        var result = MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        return result == MessageBoxResult.Yes;
    }

    public string? PromptText(string title, string message, string initialValue = "")
    {
        var owner = Application.Current?.MainWindow;
        var input = new TextBox
        {
            Text = initialValue,
            MinWidth = 340,
            Margin = new Thickness(0, 8, 0, 14)
        };

        var okButton = new Button
        {
            Content = "OK",
            Width = 88,
            MinHeight = 34,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 88,
            MinHeight = 34,
            IsCancel = true
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420
        });
        panel.Children.Add(input);
        panel.Children.Add(buttonPanel);

        var dialog = new Window
        {
            Title = title,
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = owner
        };

        okButton.Click += (_, _) =>
        {
            dialog.DialogResult = true;
            dialog.Close();
        };

        dialog.Loaded += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };

        return dialog.ShowDialog() == true ? input.Text : null;
    }

    public void ShowMessage(string title, string message, MessageBoxImage image = MessageBoxImage.Information)
    {
        MessageBox.Show(
            message,
            title,
            MessageBoxButton.OK,
            image,
            MessageBoxResult.OK);
    }

    public int? PickOption(string title, string message, IReadOnlyList<string> options)
    {
        if (options is null || options.Count == 0)
        {
            return null;
        }

        var owner = Application.Current?.MainWindow;
        int? picked = null;

        var panel = new StackPanel { Margin = new Thickness(16), MinWidth = 320 };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinHeight = 34,
            HorizontalAlignment = HorizontalAlignment.Right,
            Width = 88,
            IsCancel = true
        };

        var dialog = new Window
        {
            Title = title,
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = owner
        };

        for (var i = 0; i < options.Count; i++)
        {
            var index = i;
            var button = new Button
            {
                Content = options[i],
                MinHeight = 36,
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            button.Click += (_, _) =>
            {
                picked = index;
                dialog.DialogResult = true;
                dialog.Close();
            };
            panel.Children.Add(button);
        }

        panel.Children.Add(cancelButton);

        cancelButton.Click += (_, _) =>
        {
            dialog.DialogResult = false;
            dialog.Close();
        };

        return dialog.ShowDialog() == true ? picked : null;
    }
}
