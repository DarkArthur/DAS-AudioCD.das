using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using ScottPlot;
using AvColor = Avalonia.Media.Color;

namespace DAS.Design.Charts.Tests;

/// <summary>Task 15.16: behaviours not covered by DasChartViewTests / ChartExporterTests.</summary>
public class DasChartViewBehaviorTests
{
    // ---- fake token resolver (Req. 38.17) ----------------------------------------------------------------
    private sealed class FakeTokens(Func<string, ThemeVariant, AvColor?> map) : ITokenResolver
    {
        public bool TryResolveColor(string tokenKey, ThemeVariant variant, out AvColor color)
        {
            var c = map(tokenKey, variant);
            color = c ?? default;
            return c is not null;
        }
    }

    [Fact]
    public void MissingTokenInDark_FallsBackToLight_LogsKeyAndDoesNotThrow()
    {
        var plot = new Plot();
        var light = AvColor.FromRgb(10, 20, 30);
        var log = new List<string>();
        var tokens = new FakeTokens((key, v) =>
            key == DasChartStyle.DataBackgroundToken && v == ThemeVariant.Dark ? null : light);

        var act = () => ChartThemeApplier.Apply(plot, ThemeVariant.Dark, tokens, log.Add);

        act.Should().NotThrow();
        plot.DataBackground.Color.Should().Be(new ScottPlot.Color(10, 20, 30, 255)); // Light value
        log.Should().ContainSingle(m => m.Contains(DasChartStyle.DataBackgroundToken));
    }

    [Fact]
    public void TokenMissingEverywhere_KeepsDefaultAndDoesNotThrow()
    {
        var plot = new Plot();
        var before = plot.FigureBackground.Color;
        var log = new List<string>();
        var tokens = new FakeTokens((_, _) => null);

        var act = () => ChartThemeApplier.Apply(plot, ThemeVariant.Dark, tokens, log.Add);

        act.Should().NotThrow();
        plot.FigureBackground.Color.Should().Be(before);
        log.Should().Contain(m => m.Contains(DasChartStyle.FigureBackgroundToken));
    }

    [Fact]
    public void ResolverThatThrows_IsTreatedAsMissing()
    {
        var plot = new Plot();
        var tokens = new ThrowingTokens();
        var act = () => ChartThemeApplier.Apply(plot, ThemeVariant.Light, tokens);
        act.Should().NotThrow();
    }

    private sealed class ThrowingTokens : ITokenResolver
    {
        public bool TryResolveColor(string tokenKey, ThemeVariant variant, out AvColor color) =>
            throw new InvalidOperationException("boom");
    }

    // ---- legend outside the data area (Req. 38.x) ----------------------------------------------------------
    [Fact]
    public void Legend_IsHorizontalAndOutsideDataArea()
    {
        var plot = new Plot();
        var s = plot.Add.Scatter(new double[] { 0, 1, 2 }, new double[] { 0, 1, 0 });
        s.LegendText = "Serie";
        ChartThemeApplier.Apply(plot, ThemeVariant.Light, new FakeTokens((_, _) => AvColor.FromRgb(1, 2, 3)));

        plot.GetImage(800, 400);
        var withLegend = plot.RenderManager.LastRender.DataRect;

        var bare = new Plot();
        var b = bare.Add.Scatter(new double[] { 0, 1, 2 }, new double[] { 0, 1, 0 });
        b.LegendText = "Serie";
        bare.GetImage(800, 400);
        var overlay = bare.RenderManager.LastRender.DataRect;

        plot.Legend.Orientation.Should().Be(Orientation.Horizontal);
        // the legend takes space below the data area instead of being drawn on top of it
        withLegend.Height.Should().BeLessThan(overlay.Height);
    }

    // ---- view helpers ------------------------------------------------------------------------------------------
    private static ChartSeries Scan(int seconds) =>
        new("Scansione",
            Enumerable.Range(0, seconds).Select(i => new ChartSample(i, i % 3, i % 5 == 0 ? 1 : 0, 0, 8.0)).ToList(),
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

    private static DasChartView Show(Window window, bool withData)
    {
        var view = new DasChartView { Width = 800, Height = 400 };
        window.Content = view;
        window.Show();
        if (withData)
        {
            view.AxisSpec = new ChartAxisSpec("Tempo (mm:ss)", "Errori (n)", "Velocità (x)");
            view.Series = new[] { Scan(200) };
        }
        return view;
    }

    private static IEnumerable<T> Parts<T>(DasChartView v) where T : Visual =>
        v.GetVisualDescendants().OfType<T>();

    private static Button ResetButton(DasChartView v) => Parts<Button>(v).Single();

    private static void Key(Window w, DasChartView v, Key key, PhysicalKey phys, string sym)
    {
        v.Focus();
        w.KeyPress(key, RawInputModifiers.None, phys, sym);
    }

    // ---- empty state (Req. 39.x) ------------------------------------------------------------------------------
    [AvaloniaFact]
    public void EmptyState_ShowsMessage_AndResetZoomDisabled()
    {
        var window = new Window();
        var view = Show(window, withData: false);
        Dispatcher.UIThread.RunJobs();

        Parts<TextBlock>(view).Should().Contain(t => t.Text == DasChartView.EmptyStateText && t.IsVisible);
        ResetButton(view).IsEnabled.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task WithData_HidesEmptyMessage()
    {
        var window = new Window();
        var view = Show(window, withData: true);
        await Pump(() => view.VisibleXRange.Max > 100);

        Parts<TextBlock>(view).Where(t => t.Text == DasChartView.EmptyStateText).Should().OnlyContain(t => !t.IsVisible);
    }

    // ---- Reset zoom enablement (Req. 39.5) ----------------------------------------------------------------------
    [AvaloniaFact]
    public async Task ResetZoom_EnabledOnlyWhenZoomed_AndRestoresAutoFit()
    {
        var window = new Window();
        var view = Show(window, withData: true);
        await Pump(() => view.VisibleXRange.Max > 100);
        var full = view.VisibleXRange;
        ResetButton(view).IsEnabled.Should().BeFalse();

        Key(window, view, Avalonia.Input.Key.OemPlus, PhysicalKey.Equal, "+");
        await Pump(() => !view.IsAutoFit);

        view.IsAutoFit.Should().BeFalse();
        (view.VisibleXRange.Max - view.VisibleXRange.Min).Should().BeLessThan(full.Max - full.Min);
        await Pump(() => ResetButton(view).IsEnabled);
        ResetButton(view).IsEnabled.Should().BeTrue();

        view.ResetZoom();
        await Pump(() => view.IsAutoFit && !ResetButton(view).IsEnabled);

        view.IsAutoFit.Should().BeTrue();
        view.VisibleXRange.Should().Be(full);
        ResetButton(view).IsEnabled.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task ZoomIn_ThenThemeChange_KeepsZoomAndData()
    {
        var window = new Window();
        var view = Show(window, withData: true);
        await Pump(() => view.VisibleXRange.Max > 100);
        Key(window, view, Avalonia.Input.Key.OemPlus, PhysicalKey.Equal, "+");
        await Pump(() => !view.IsAutoFit);
        var zoomed = view.VisibleXRange;

        window.RequestedThemeVariant = ThemeVariant.Dark;
        await Pump(() => false);

        view.Series.Should().HaveCount(1);
        view.IsAutoFit.Should().BeFalse();
        view.VisibleXRange.Should().Be(zoomed);
    }

    // NOT headless-testable: cursor/tooltip and Chart_Selection by pointer (Req. 39.1-39.4). They depend on the
    // data rectangle of a ScottPlot render, which the headless platform (no Skia GPU/render target) never produces
    // (PlotPixelWidth stays 0), so pointer coordinates cannot be mapped to seconds.

    // ---- exports (Req. 21.2, 21.11) -------------------------------------------------------------------------------
    private static string Temp(string ext) => Path.Combine(Path.GetTempPath(), $"das-chart-{Guid.NewGuid():N}{ext}");

    [AvaloniaFact]
    public async Task ExportPng_SizeIsExactlyTwiceTheLogicalSize()
    {
        var window = new Window();
        var view = Show(window, withData: true);
        await Pump(() => view.VisibleXRange.Max > 100);
        var (w, h) = ChartExporter.LogicalSize(view);
        var path = Temp(".png");
        try
        {
            view.ExportPng(path);
            var bytes = File.ReadAllBytes(path);
            int width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
            int height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
            width.Should().Be(2 * w);
            height.Should().Be(2 * h);
            width.Should().BeGreaterThanOrEqualTo(2400);
            height.Should().BeGreaterThanOrEqualTo(1200);
        }
        finally { File.Delete(path); }
    }
}


