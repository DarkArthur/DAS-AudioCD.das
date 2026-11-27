using Avalonia.Controls;
using Avalonia.Interactivity;
using DiscCheck.App.ViewModels;

namespace DiscCheck.App.Views;

public partial class ScanView : UserControl
{
    public ScanView()
    {
        InitializeComponent();
        Chart.VisibleRangeChanged += (_, _) => ReportViewport();
        Chart.SizeChanged += (_, _) => ReportViewport();
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        ReportViewport();
        if (DataContext is ScanViewModel vm)
        {
            vm.ChartExportSource = new Startup.DasChartViewExportSource(Chart);
            await vm.OnViewOpenedAsync();
        }
    }

    /// <summary>Gives the ViewModel pixel width and visible range so it can downsample (Requirement 18.6).</summary>
    private void ReportViewport()
    {
        if (DataContext is not ScanViewModel vm) return;
        int width = (int)(Chart.PlotPixelWidth > 0 ? Chart.PlotPixelWidth : Chart.Bounds.Width);
        vm.UpdateViewport(width, Chart.IsAutoFit ? null : Chart.VisibleXRange);
    }
}
