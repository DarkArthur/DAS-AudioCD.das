using Avalonia.Controls;
using Avalonia.Interactivity;
using DiscCheck.App.ViewModels;

namespace DiscCheck.App.Views;

public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
    }

    /// <summary>Reads the Database each time the view is opened (Requirement 17.1).</summary>
    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is HistoryViewModel vm)
        {
            vm.ChartExportSource = new Startup.DasChartViewExportSource(Chart);
            await vm.LoadAsync();
        }
    }
}
