using Avalonia.Controls;
using Avalonia.Interactivity;
using DiscCheck.App.ViewModels;

namespace DiscCheck.App.Views;

public partial class DriveView : UserControl
{
    public DriveView()
    {
        InitializeComponent();
    }

    /// <summary>Enumerates the drives when the view is opened (Requirement 4.1).</summary>
    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is DriveViewModel vm && vm.Drives.Count == 0 && vm.RefreshCommand.CanExecute(null))
            await vm.RefreshAsync();
    }
}
