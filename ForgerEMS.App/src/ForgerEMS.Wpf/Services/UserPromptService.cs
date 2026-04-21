using System.Windows;

namespace VentoyToolkitSetup.Wpf.Services;

public interface IUserPromptService
{
    bool Confirm(string title, string message);

    void ShowMessage(string title, string message, MessageBoxImage image = MessageBoxImage.Information);
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

    public void ShowMessage(string title, string message, MessageBoxImage image = MessageBoxImage.Information)
    {
        MessageBox.Show(
            message,
            title,
            MessageBoxButton.OK,
            image,
            MessageBoxResult.OK);
    }
}
