using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DiscCheck.Core;
using FluentAssertions;
using ScottPlot;
using Xunit.Abstractions;

namespace DAS.Design.Charts.Tests;

/// <summary>
/// Task 15.18 (Req. 37.14, 38.14): chart performance tests on synthetic data.
///
/// Thresholds (ms, redraw/s) are the ones of the requirements multiplied by the tolerance factor in the
/// environment variable <c>DAS_PERF_TOLERANCE</c> (default 1.0; use e.g. 3 on a slow CI machine).
/// The redraw rate limit (30/s) is never scaled.
///
/// These tests are timing-sensitive, so the standard suite excludes them:
///   dotnet test --filter "Category!=Hardware&amp;Category!=Performance"
/// Run only these tests:
///   dotnet test tests/DAS.Design.Charts.Tests --filter "Category=Performance"
///   $env:DAS_PERF_TOLERANCE = "3"   # optional, slow machine
///
/// What is NOT measurable here: the Avalonia headless platform does not give the AvaPlot a real render
/// target, so <c>DataRect</c> stays empty. Therefore pointer-driven cursor/zoom/pan/selection through the
/// view and the on-screen paint time cannot be exercised. The tests measure instead the work the view does
/// on every such interaction: <c>DasChartView</c> rebuild/redraw (model + plot construction, up to
/// <c>AvaPlot.Refresh</c>), the pure calculators (<see cref="ZoomPanCalculator"/>,
/// <see cref="NearestSampleFinder"/>, <see cref="SelectionStatistics"/>), the downsampler and an offscreen
/// software Skia render of the same plot size (<c>Plot.GetImage</c>) as a proxy for the paint time; the
/// latter is reported only, not asserted.
/// </summary>
[Trait("Category", "Performance")]
[Collection(nameof(ChartPerformanceCollection))]
public class ChartPerformanceTests(ITestOutputHelper output)
{
    private const int Samples = 6000;
    private const int CompareSeries = 8;
    private const double RedrawBudgetMs = 33;
    private const double CompareBudgetMs = 100;
    private const double InteractionBudgetMs = 100;
    private const double MaxRedrawsPerSecond = 30;

    private static double Tolerance
    {
        get
        {
            var raw = Environment.GetEnvironmentVariable("DAS_PERF_TOLERANCE");
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && f >= 1 ? f : 1.0;
        }
    }

    // ---- helpers -----------------------------------------------------------------------------------------
    private static ChartSeries Scan(int n) =>
        new("Scansione",
            Enumerable.Range(0, n).Select(i => new ChartSample(i,
                C1: (i * 7) % 40, C2: i % 97 == 0 ? 2 : 0, CU: i % 503 == 0 ? 1 : 0, SpeedX: 4 + (i % 100) / 10.0)).ToList(),
            ChartSeriesKind.ScanBars, 0);

    private static ChartSeries Curve(int index, int n) =>
        new($"Disco {index + 1}",
            Enumerable.Range(0, n).Select(i => new ChartSample(i, (i * (index + 3)) % 50, null, null, null)).ToList(),
            ChartSeriesKind.CompareCurve, index);

    private static double Percentile(IReadOnlyList<double> values, double p)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        int idx = (int)Math.Ceiling(p * sorted.Length) - 1;
        return sorted[Math.Clamp(idx, 0, sorted.Length - 1)];
    }

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    /// <summary>Lets pending throttled redraws finish so the next explicit redraw runs synchronously.</summary>
    private static async Task Settle(DasChartView view)
    {
        int last = -1;
        for (int i = 0; i < 50 && view.RedrawCount != last; i++)
        {
            last = view.RedrawCount;
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(45);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task<DasChartView> ShowView(Window window, IReadOnlyList<ChartSeries> series, ChartAxisSpec spec)
    {
        var view = new DasChartView { Width = 800, Height = 400 };
        window.Content = view;
        window.Show();
        view.AxisSpec = spec;
        view.Series = series;
        await Settle(view);
        return view;
    }

    /// <summary>p95 of explicit, synchronous redraws (<c>ResetZoom</c> after the throttle interval).</summary>
    private static async Task<List<double>> MeasureRedraws(DasChartView view, int runs)
    {
        var times = new List<double>();
        for (int i = 0; i < runs; i++)
        {
            await Task.Delay((int)DasChartView.MinRedrawInterval.TotalMilliseconds + 2);
            Dispatcher.UIThread.RunJobs(); // lets a pending throttle timer finish so ResetZoom redraws synchronously
            await Task.Delay((int)DasChartView.MinRedrawInterval.TotalMilliseconds + 2);
            int before = view.RedrawCount;
            long t0 = Stopwatch.GetTimestamp();
            view.ResetZoom();
            long t1 = Stopwatch.GetTimestamp();
            if (view.RedrawCount == before + 1)
                times.Add(Ms(t1 - t0));
            Dispatcher.UIThread.RunJobs();
        }
        times.Count.Should().BeGreaterThanOrEqualTo(runs / 2, "enough synchronous redraws must be measurable");
        return times;
    }

    private static List<double> Time(int runs, Action action)
    {
        for (int i = 0; i < 3; i++) action(); // warm-up
        var times = new List<double>(runs);
        for (int i = 0; i < runs; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            action();
            times.Add(Ms(Stopwatch.GetTimestamp() - t0));
        }
        return times;
    }

    private void Report(string name, IReadOnlyList<double> times, double budget)
    {
        double p95 = Percentile(times, 0.95);
        output.WriteLine($"{name}: n={times.Count} p50={Percentile(times, 0.5):F3} ms p95={p95:F3} ms max={times.Max():F3} ms " +
                         $"budget={budget:F0} ms (x{Tolerance:F1} tolerance = {budget * Tolerance:F0} ms)");
        p95.Should().BeLessThanOrEqualTo(budget * Tolerance, name);
    }

    // ---- redraw: 6,000 samples (Req. 37.14, 38.14) ----------------------------------------------------------
    [AvaloniaFact]
    public async Task Redraw_ScanWith6000Samples_P95WithinBudget()
    {
        var window = new Window();
        var view = await ShowView(window, [Scan(Samples)], new ChartAxisSpec("Tempo (mm:ss)", "Errori (n)", "Velocità (x)"));

        var times = await MeasureRedraws(view, runs: 40);

        Report("Redraw DasChartView, scan 6000 samples", times, RedrawBudgetMs);
    }

    [Fact]
    public void OffscreenSkiaRender_6000Samples_IsReported()
    {
        // The app hands DasChartView data already reduced to the plot width (Req. 18.6), so this is the
        // render the user sees. The raw 6000-point render is reported only (software Skia on the test CPU).
        var raw = Enumerable.Range(0, Samples)
            .Select(i => new ScanSample(i * 75, i, (i * 7) % 40, i % 97 == 0 ? 2 : 0, i % 503 == 0 ? 1 : 0, 4 + (i % 100) / 10.0))
            .ToList();
        var reduced = ChartDownsampler.Downsample(raw, 800);

        // Report only (decision on task 15.18): software Skia on the CPU, borderline with the 33 ms budget
        // under load and not the app's render path. The DasChartView redraw tests carry the assertion.
        var downsampled = Time(30, () => BuildPlot(reduced).GetImage(800, 400));
        output.WriteLine($"Offscreen Skia render 800x400, 6000 -> {reduced.Count} samples (not asserted): " +
                         $"p50={Percentile(downsampled, 0.5):F1} ms p95={Percentile(downsampled, 0.95):F1} ms (reference {RedrawBudgetMs:F0} ms)");

        var full = Time(10, () => BuildPlot(raw).GetImage(800, 400));
        output.WriteLine($"Offscreen Skia render 800x400, 6000 samples NOT downsampled (not asserted): " +
                         $"p50={Percentile(full, 0.5):F1} ms p95={Percentile(full, 0.95):F1} ms");
    }

    private static Plot BuildPlot(IReadOnlyList<ScanSample> samples)
    {
        var plot = new Plot();
        foreach (var (offset, value) in new (double, Func<ScanSample, double>)[]
                 { (-0.3, s => s.C1 ?? 0), (0, s => s.C2), (0.3, s => s.CU ?? 0) })
            plot.Add.Bars(samples.Select(s => new Bar { Position = s.TimeSeconds + offset, Value = value(s), Size = 0.3 }).ToList());
        var speed = plot.Add.Scatter(samples.Select(s => (double)s.TimeSeconds).ToArray(), samples.Select(s => s.SpeedX ?? 0).ToArray());
        speed.Axes.YAxis = plot.Axes.Right;
        return plot;
    }

    // ---- Compare: 8 x 6,000 -------------------------------------------------------------------------------------
    [AvaloniaFact]
    public async Task Redraw_Compare8x6000_P95WithinBudget()
    {
        var series = Enumerable.Range(0, CompareSeries).Select(i => Curve(i, Samples)).ToList();
        var window = new Window();
        var view = await ShowView(window, series, new ChartAxisSpec("Tempo (mm:ss)", "C1 (n)"));

        var times = await MeasureRedraws(view, runs: 30);

        Report("Redraw DasChartView, Compare 8x6000", times, CompareBudgetMs);
    }

    // ---- redraw rate (Req. 38.14): ≤ 30/s ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task RedrawRate_UnderContinuousUpdates_StaysAtMost30PerSecond()
    {
        var window = new Window();
        var view = await ShowView(window, [Scan(Samples)], new ChartAxisSpec("Tempo (mm:ss)", "Errori (n)", "Velocità (x)"));
        var alt = new[] { Scan(Samples - 1) };
        var main = view.Series!;

        // Timestamp every redraw (monotonic clock) instead of dividing by a window length subject to jitter.
        var stamps = new List<long>();
        view.Redrawn += (_, _) => stamps.Add(Stopwatch.GetTimestamp());

        var sw = Stopwatch.StartNew();
        int updates = 0;
        while (sw.Elapsed < TimeSpan.FromSeconds(2))
        {
            view.Series = updates++ % 2 == 0 ? alt : main; // each assignment requests a redraw
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(2);
        }

        // Under load the throttle timer can be delayed: wait (bounded) until at least one redraw was performed.
        var deadline = Stopwatch.StartNew();
        while (stamps.Count == 0 && deadline.Elapsed < TimeSpan.FromSeconds(10))
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Dispatcher.UIThread.RunJobs();

        var gaps = stamps.Zip(stamps.Skip(1), (a, b) => Ms(b - a)).ToList();
        // Most redraws in any 1-second window (sliding, anchored at each redraw).
        int maxPerSecond = stamps.Select((t0, i) => stamps.Skip(i).TakeWhile(t => t - t0 < Stopwatch.Frequency).Count())
            .DefaultIfEmpty(0).Max();
        double minGap = gaps.Count > 0 ? gaps.Min() : double.PositiveInfinity;

        output.WriteLine($"Redraw rate: {updates} updates, {stamps.Count} redraws, min gap {minGap:F1} ms " +
                         $"(limit {1000 / MaxRedrawsPerSecond:F1} ms), max in any 1 s window {maxPerSecond} (limit {MaxRedrawsPerSecond:F0})");
        stamps.Count.Should().BeGreaterThan(0, "the throttled redraw must eventually run");
        // 30/s = at least 33.3 ms between consecutive redraws; 1 ms tolerance for the timestamp taken in the handler.
        minGap.Should().BeGreaterThanOrEqualTo(1000 / MaxRedrawsPerSecond - 1, "consecutive redraws must be ≥ ~33 ms apart");
        maxPerSecond.Should().BeLessThanOrEqualTo((int)MaxRedrawsPerSecond);
    }

    // ---- zoom / pan / cursor / selection (pure calculators) -------------------------------------------------
    [Fact]
    public void ZoomAndPan_6000Samples_WithinBudget()
    {
        var range = new AxisRange(0, Samples);
        var times = Time(500, () =>
        {
            var r = ZoomPanCalculator.Zoom(range, 3000, 3, Samples);
            r = ZoomPanCalculator.Pan(r, 120, Samples);
            ZoomPanCalculator.Constrain(r, Samples);
        });
        Report("Zoom+Pan calculation", times, InteractionBudgetMs);
    }

    [Fact]
    public void Cursor_NearestSample_6000And8x6000_WithinBudget()
    {
        var curves = Enumerable.Range(0, CompareSeries).Select(i => Curve(i, Samples)).ToList();
        int k = 0;
        var times = Time(500, () =>
        {
            double t = (k++ * 37) % Samples + 0.4;
            foreach (var c in curves) NearestSampleFinder.Find(c.Samples, t);
        });
        Report("Cursor nearest sample, 8x6000", times, InteractionBudgetMs);
    }

    [Fact]
    public void Selection_Statistics_6000And8x6000_WithinBudget()
    {
        var curves = Enumerable.Range(0, CompareSeries).Select(i => Curve(i, Samples)).ToList();
        var scan = Scan(Samples);

        var single = Time(100, () => SelectionStatistics.Compute(scan.Samples, 100, 5900));
        Report("Selection statistics, 6000 samples", single, InteractionBudgetMs);

        var multi = Time(50, () =>
        {
            foreach (var c in curves) SelectionStatistics.Compute(c.Samples, 0, Samples);
        });
        Report("Selection statistics, 8x6000", multi, InteractionBudgetMs);
    }

    // ---- downsampling (Req. 18.6) ------------------------------------------------------------------------------
    [Fact]
    public void Downsampling_6000Samples_ToPlotWidth_WithinBudget()
    {
        var samples = Enumerable.Range(0, Samples)
            .Select(i => new ScanSample(i * 75, i, (i * 7) % 40, i % 97 == 0 ? 2 : 0, i % 503 == 0 ? 1 : 0, 4 + (i % 100) / 10.0))
            .ToList();

        var times = Time(200, () => ChartDownsampler.Downsample(samples, 800));

        Report("Downsampling 6000 -> 800", times, RedrawBudgetMs);
    }
}

/// <summary>Timings are taken with no other test class running in parallel.</summary>
[CollectionDefinition(nameof(ChartPerformanceCollection), DisableParallelization = true)]
public class ChartPerformanceCollection;
