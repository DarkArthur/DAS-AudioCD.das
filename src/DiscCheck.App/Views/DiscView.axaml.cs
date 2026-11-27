using Avalonia.Controls;
using Avalonia.Interactivity;
using DiscCheck.App.ViewModels;

namespace DiscCheck.App.Views;

public partial class DiscView : UserControl
{
    public DiscView()
    {
        InitializeComponent();
    }

    /// <summary>Reads the disc when the view is opened with a drive selected (Requirement 5.1).</summary>
    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is DiscViewModel vm && !vm.HasDisc && vm.ReadDiscCommand.CanExecute(null))
            await vm.ReadDiscAsync();
    }
}
