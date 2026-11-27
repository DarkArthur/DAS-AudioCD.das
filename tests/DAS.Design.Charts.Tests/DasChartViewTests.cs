using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentAssertions;

namespace DAS.Design.Charts.Tests;

public class DasChartViewTests
{
    private static ChartSeries Scan(int seconds, Func<int, ChartSample>? make = null) =>
        new("Scansione",
            Enumerable.Range(0, seconds).Select(i => make?.Invoke(i) ?? new ChartSample(i, i % 3, i % 5 == 0 ? 1 : 0, 0, 8.0)).ToList(),
            ChartSeriesKind.ScanBars, 0);

    private static async Task Pump(Func<bool> done)
    {
        for (int i = 0; i < 50 && !done(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static DasChartView Show(Window window)
    {
        var view = new DasChartView { Width = 800, Height = 400 };
        window.Content = view;
        window.Show();
        return view;
    }

    [AvaloniaFact]
    public void EmptyState_UsesDefaultRangesAndDoesNotThrow()
    {
        var window = new Window();
        var view = Show(window);
        view.VisibleXRange.Should().Be(new AxisRange(0, DasChartView.EmptyXMax));
        view.IsAutoFit.Should().BeTrue();
        view.ErrorSeconds.Should().Be(0);
        view.SelectedStatistics.Should().BeNull();
    }

    [AvaloniaFact]
    public async Task Series_AutoFitsAndCountsErrorSeconds_WithNullsOmitted()
    {
        var window = new Window();
        var view = Show(window);
        var series = Scan(120, i => i == 10 ? new ChartSample(i, null, null, null, null) : new ChartSample(i, 1, i % 30 == 0 ? 2 : 0, i == 7 ? 1 : 0, 8));
        view.Series = new[] { series };
        view.AxisSpec = new ChartAxisSpec("Tempo (mm:ss)", "Errori (n)", "Velocità (x)");
        await Pump(() => view.VisibleXRange.Max > 100);

        view.VisibleXRange.Should().Be(new AxisRange(0, 119));
        view.ErrorSeconds.Should().Be(5); // t = 0, 30, 60, 90 (C2) and t = 7 (CU)
    }

    [AvaloniaFact]
    public async Task ThemeChange_PreservesDataAndView()
    {
        var window = new Window();
        var view = Show(window);
        view.Series = new[] { Scan(200) };
        await Pump(() => view.VisibleXRange.Max > 100);
        var before = view.VisibleXRange;

        window.RequestedThemeVariant = ThemeVariant.Dark;
        await Pump(() => false);

        view.Series.Should().HaveCount(1);
        view.VisibleXRange.Should().Be(before);
    }

    [AvaloniaFact]
    public async Task Redraws_AreCoalesced()
    {
        var window = new Window();
        var view = Show(window);
        await Pump(() => false);
        int start = view.RedrawCount;
        for (int i = 0; i < 100; i++)
            view.Series = new[] { Scan(10 + i) };
        view.RedrawCount.Should().BeLessThanOrEqualTo(start + 1);
        await Pump(() => view.RedrawCount > start);
        view.RedrawCount.Should().BeLessThanOrEqualTo(start + 3);
    }
}
