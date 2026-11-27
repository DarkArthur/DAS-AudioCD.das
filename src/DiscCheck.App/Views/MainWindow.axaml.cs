using Avalonia.Controls;
using DiscCheck.App.ViewModels;

namespace DiscCheck.App.Views;

public partial class MainWindow : Window
{
    /// <summary>Parameterless constructor for the XAML loader and previewer.</summary>
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
        Opened += async (_, _) => await viewModel.CheckReminderAsync();
    }

    private async void OnAboutClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await new AboutWindow().ShowDialog(this);
    }
}
