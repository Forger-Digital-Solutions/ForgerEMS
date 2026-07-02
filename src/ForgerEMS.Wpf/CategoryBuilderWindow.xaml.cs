using System.Windows;
using VentoyToolkitSetup.Wpf.ViewModels;

namespace VentoyToolkitSetup.Wpf;

public partial class CategoryBuilderWindow : Window
{
    public CategoryBuilderWindow(CategoryBuilderViewModel viewModel)
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
