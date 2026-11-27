using Avalonia.Controls;
using Avalonia.Interactivity;
using DiscCheck.App.ViewModels;

namespace DiscCheck.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow() : this(new AboutViewModel())
    {
    }

    public AboutWindow(AboutViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
