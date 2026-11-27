using Avalonia.Controls;
using Avalonia.Interactivity;
using DiscCheck.App.ViewModels;

namespace DiscCheck.App.Views;

public partial class DegradationView : UserControl
{
    public DegradationView()
    {
        InitializeComponent();
    }

    /// <summary>Reads the Database each time the view is opened (Requirements 20.1, 20.5).</summary>
    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is DegradationViewModel vm) await vm.LoadAsync();
    }
}
