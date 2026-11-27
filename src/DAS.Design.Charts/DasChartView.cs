using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Threading;
using DAS.Design.Charts.Formatting;
using DAS.Design.Controls;
using ScottPlot.Avalonia;
using SP = ScottPlot;

namespace DAS.Design.Charts;

/// <summary>
/// The only way to show a chart in DAS apps: an embedded <see cref="AvaPlot"/> with the Chart_Style,
/// Chart_Cursor, Chart_Selection, zoom/pan, threshold lines and error highlighting (Req. 18, 38, 39).
/// Colours always come from the Design_Tokens (<see cref="ITokenResolver"/>); no literal colours.
/// </summary>
public sealed class DasChartView : UserControl
{
    // ---- constants -------------------------------------------------------------------------------------------
    /// <summary>Minimum interval between two redraws (≤ 30 redraws per second).</summary>
    public static readonly TimeSpan MinRedrawInterval = TimeSpan.FromMilliseconds(34);
    public const string EmptyStateText = "Nessun dato da visualizzare";
    public const string ResetZoomText = "Reset zoom";
    public const string ResetIconName = "das.reset";
    public const string NotAvailable = "n/d";
    public const string DefaultSpeedLabel = "Velocità (x)";
    public const double EmptyXMax = 60;
    public const double EmptyYMax = 10;
    private const double SelectionOpacity = 0.25;
    private const double ErrorBandOpacity = 0.6;
    private const double DragThresholdPx = 3;
    private const double TooltipMargin = 12;
    private const double LabelWidthEstimatePx = 48;

    // ---- styled / direct properties -------------------------------------------------------------------------
    public static readonly StyledProperty<IReadOnlyList<ChartSeries>?> SeriesProperty =
        AvaloniaProperty.Register<DasChartView, IReadOnlyList<ChartSeries>?>(nameof(Series));

    public static readonly StyledProperty<ChartAxisSpec?> AxisSpecProperty =
        AvaloniaProperty.Register<DasChartView, ChartAxisSpec?>(nameof(AxisSpec));

    public static readonly StyledProperty<ThresholdSettings?> ThresholdsProperty =
        AvaloniaProperty.Register<DasChartView, ThresholdSettings?>(nameof(Thresholds));

    public static readonly DirectProperty<DasChartView, SelectionResult?> SelectedStatisticsProperty =
        AvaloniaProperty.RegisterDirect<DasChartView, SelectionResult?>(nameof(SelectedStatistics), o => o.SelectedStatistics);

    public static readonly DirectProperty<DasChartView, int> ErrorSecondsProperty =
        AvaloniaProperty.RegisterDirect<DasChartView, int>(nameof(ErrorSeconds), o => o.ErrorSeconds);

    private SelectionResult? _selectedStatistics;
    private int _errorSeconds;

    // ---- parts ------------------------------------------------------------------------------------------------
    private readonly AvaPlot _avaPlot = new();
    private readonly ITokenResolver _tokens = new AvaloniaTokenResolver();
    private readonly Border _focusBorder = new() { BorderThickness = new Thickness(2), Focusable = false };
    private readonly Button _resetButton = new();
    private readonly TextBlock _errorLabel = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _emptyText = new()
    {
        Text = EmptyStateText,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false,
    };
    private readonly Canvas _overlay = new() { IsHitTestVisible = false, ClipToBounds = true };
    private readonly Border _tooltip = new() { IsVisible = false, Padding = new Thickness(8, 4), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) };
    private readonly TextBlock _tooltipText = new();
    private readonly Border _selectionPanel = new() { IsVisible = false, Padding = new Thickness(8, 4), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) };
    private readonly TextBlock _selectionText = new();

    // ---- state ------------------------------------------------------------------------------------------------
    private double _viewMin;
    private double _viewMax = EmptyXMax;
    private bool _autoFit = true;
    private double _duration;
    private double? _cursorTime;
    private (double T0, double T1)? _selection;
    private double? _selectionAnchor;
    private bool _selecting;
    private bool _panning;
    private bool _panMoved;
    private Point _panStart;
    private Point _panLast;

    private SP.Plottables.VerticalLine? _cursorLine;
    private SP.Plottables.HorizontalSpan? _selectionSpan;

    private readonly DispatcherTimer _redrawTimer = new();
    private readonly Stopwatch _sinceLastRedraw = Stopwatch.StartNew();
    private bool _redrawPending;
    private bool _styleDirty = true;
    private int _redrawCount;

    static DasChartView()
    {
        FocusableProperty.OverrideDefaultValue<DasChartView>(true);
        SeriesProperty.Changed.AddClassHandler<DasChartView>((c, _) => c.RequestRedraw());
        AxisSpecProperty.Changed.AddClassHandler<DasChartView>((c, _) => c.RequestRedraw());
        ThresholdsProperty.Changed.AddClassHandler<DasChartView>((c, _) => c.RequestRedraw());
    }

    public DasChartView()
    {
        _avaPlot.UserInputProcessor.Disable();
        _redrawTimer.Tick += (_, _) => OnRedrawTimer();

        BuildVisualTree();

        ActualThemeVariantChanged += (_, _) =>
        {
            _styleDirty = true;
            RequestRedraw(immediate: true);
        };
        _avaPlot.SizeChanged += (_, _) => RequestRedraw();
        _avaPlot.PointerMoved += OnPlotPointerMoved;
        _avaPlot.PointerExited += (_, _) => HideCursor();
        _avaPlot.PointerPressed += OnPlotPointerPressed;
        _avaPlot.PointerReleased += OnPlotPointerReleased;
        _avaPlot.PointerCaptureLost += (_, _) => { _panning = false; _selecting = false; };
        _avaPlot.PointerWheelChanged += OnPlotPointerWheel;
        _avaPlot.DoubleTapped += (_, e) => { ResetZoom(); e.Handled = true; };
        GotFocus += (_, _) => UpdateFocusVisual();
        LostFocus += (_, _) => UpdateFocusVisual();

        RequestRedraw(immediate: true);
    }

    // ---- public API ------------------------------------------------------------------------------------------
    /// <summary>Series to show. Re-assign (or call <see cref="Invalidate"/>) after appending samples.</summary>
    public IReadOnlyList<ChartSeries>? Series
    {
        get => GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    /// <summary>Axis labels (with units) and X axis format.</summary>
    public ChartAxisSpec? AxisSpec
    {
        get => GetValue(AxisSpecProperty);
        set => SetValue(AxisSpecProperty, value);
    }

    /// <summary>Threshold settings; lines are drawn only for LiteOn with "Mostra soglie" on.</summary>
    public ThresholdSettings? Thresholds
    {
        get => GetValue(ThresholdsProperty);
        set => SetValue(ThresholdsProperty, value);
    }

    /// <summary>Statistics of the current Chart_Selection, null when there is no selection.</summary>
    public SelectionResult? SelectedStatistics
    {
        get => _selectedStatistics;
        private set => SetAndRaise(SelectedStatisticsProperty, ref _selectedStatistics, value);
    }

    /// <summary>Number of seconds with C2 &gt; 0 or CU &gt; 0 in the scan series.</summary>
    public int ErrorSeconds
    {
        get => _errorSeconds;
        private set => SetAndRaise(ErrorSecondsProperty, ref _errorSeconds, value);
    }

    /// <summary>Visible X interval, in seconds. Use it to downsample the data (Req. 18.6).</summary>
    public AxisRange VisibleXRange => new(_viewMin, _viewMax);

    /// <summary>Width of the data area in physical pixels (0 before the first render).</summary>
    public double PlotPixelWidth => DataRect is { Width: > 0 } r ? r.Width : 0;

    /// <summary>True when the view shows the whole data range (auto-fit).</summary>
    public bool IsAutoFit => _autoFit;

    /// <summary>Raised when the visible X interval or the plot width changes.</summary>
    public event EventHandler? VisibleRangeChanged;

    /// <summary>Raised at the start of every redraw (diagnostics and throttling tests).</summary>
    public event EventHandler? Redrawn;

    /// <summary>Number of redraws performed (diagnostics and throttling tests).</summary>
    public int RedrawCount => _redrawCount;

    /// <summary>The embedded plot, for the export services of this library only.</summary>
    internal AvaPlot InnerPlot => _avaPlot;

    /// <summary>Exports the current chart (data, zoom, visible series, active Chart_Style) as a 2x PNG (Req. 21.2).</summary>
    public void ExportPng(string path) => ChartExporter.ExportPng(this, path);

    /// <summary>Exports the current chart as a vector SVG with text as text (Req. 21.11).</summary>
    public void ExportSvg(string path) => ChartExporter.ExportSvg(this, path);

    /// <summary>
    /// Prepares the plot for export: invariant culture, no cursor/selection overlay, plot rebuilt with the
    /// current state. Disposing restores the interactive state.
    /// </summary>
    internal IDisposable BeginExport()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        _exporting = true;
        _styleDirty = true;
        Redraw();
        return new ExportScope(this, previousCulture);
    }

    private bool _exporting;

    private sealed class ExportScope(DasChartView view, CultureInfo culture) : IDisposable
    {
        public void Dispose()
        {
            CultureInfo.CurrentCulture = culture;
            view._exporting = false;
            view.RequestRedraw(immediate: true);
        }
    }

    /// <summary>Back to auto-fit (Req. 39.5).</summary>
    public void ResetZoom()
    {
        _autoFit = true;
        RequestRedraw(immediate: true);
    }

    /// <summary>Requests a redraw after the series were modified in place.</summary>
    public void Invalidate() => RequestRedraw();

    /// <summary>Clears the Chart_Selection.</summary>
    public void ClearSelection()
    {
        if (_selection is null) return;
        _selection = null;
        SelectedStatistics = null;
        RequestRedraw();
    }

    // ---- visual tree -----------------------------------------------------------------------------------------
    private void BuildVisualTree()
    {
        _resetButton.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new DasIcon { IconName = ResetIconName, Size = 16, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = ResetZoomText, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        _resetButton.IsEnabled = false;
        _resetButton.Click += (_, _) => ResetZoom();
        Avalonia.Automation.AutomationProperties.SetName(_resetButton, ResetZoomText);
        BindResource(_errorLabel, TextBlock.ForegroundProperty, "Das.Brush.Text");

        var top = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 0, 4),
            Children = { _errorLabel, _resetButton },
        };

        BindResource(_emptyText, TextBlock.ForegroundProperty, "Das.Brush.TextSecondary");
        foreach (var (panel, text) in new[] { (_tooltip, _tooltipText), (_selectionPanel, _selectionText) })
        {
            BindResource(panel, Border.BackgroundProperty, "Das.Brush.SurfaceAlt");
            BindResource(panel, Border.BorderBrushProperty, "Das.Brush.Border");
            BindResource(text, TextBlock.ForegroundProperty, "Das.Brush.Text");
            text.FontSize = DasChartStyle.TickFontSize;
            panel.Child = text;
            _overlay.Children.Add(panel);
        }
        Canvas.SetLeft(_selectionPanel, 8);
        Canvas.SetTop(_selectionPanel, 8);

        var plotArea = new Grid { Children = { _avaPlot, _emptyText, _overlay } };
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        Grid.SetRow(top, 0);
        Grid.SetRow(plotArea, 1);
        root.Children.Add(top);
        root.Children.Add(plotArea);

        _focusBorder.BorderBrush = Brushes.Transparent;
        _focusBorder.Child = root;
        Content = _focusBorder;
    }

    private void BindResource(AvaloniaObject target, AvaloniaProperty property, string key) =>
        target.Bind(property, this.GetResourceObservable(key));

    private void UpdateFocusVisual()
    {
        IBrush brush = Brushes.Transparent;
        if (IsKeyboardFocusWithin && this.TryFindResource("Das.Brush.Primary", ActualThemeVariant, out var res) && res is IBrush b)
            brush = b;
        _focusBorder.BorderBrush = brush;
    }

    // ---- redraw throttling (≤ 30/s) -------------------------------------------------------------------------
    private void RequestRedraw(bool immediate = false)
    {
        _redrawPending = true;
        if (_redrawTimer.IsEnabled)
            return;
        var wait = MinRedrawInterval - _sinceLastRedraw.Elapsed;
        if (immediate && wait <= TimeSpan.Zero)
        {
            Redraw();
            return;
        }
        _redrawTimer.Interval = wait > TimeSpan.Zero ? wait : TimeSpan.FromMilliseconds(1);
        _redrawTimer.Start();
    }

    private void OnRedrawTimer()
    {
        _redrawTimer.Stop();
        if (!_redrawPending)
            return;
        // The platform timer may fire slightly early (clock granularity): never redraw before the interval elapsed.
        var wait = MinRedrawInterval - _sinceLastRedraw.Elapsed;
        if (wait > TimeSpan.Zero)
        {
            _redrawTimer.Interval = wait;
            _redrawTimer.Start();
            return;
        }
        Redraw();
    }

    private void Redraw()
    {
        _redrawPending = false;
        _sinceLastRedraw.Restart();
        _redrawCount++;
        Redrawn?.Invoke(this, EventArgs.Empty);
        try
        {
            if (_styleDirty)
            {
                _styleDirty = false;
                SP.Plot plot = _avaPlot.Plot;
                ChartThemeApplier.Apply(plot, ActualThemeVariant, _tokens, LogWarning);
            }
            Rebuild();
        }
        catch (Exception ex)
        {
            Logger.TryGet(LogEventLevel.Error, "DasChart")?.Log(this, "DasChartView redraw failed: {Message}", ex.Message);
        }
    }

    private void LogWarning(string message) =>
        Logger.TryGet(LogEventLevel.Warning, "DasChart")?.Log(this, "{Message}", message);

    // ---- model helpers ---------------------------------------------------------------------------------------
    private SP.PixelRect? DataRect
    {
        get
        {
            try { return _avaPlot.Plot.RenderManager.LastRender.DataRect; }
            catch (Exception) { return null; }
        }
    }

    private double Scale => _avaPlot.DisplayScale > 0 ? _avaPlot.DisplayScale : 1;

    private bool IsDateAxis => AxisSpec?.XFormat == ChartXAxisFormat.Date;

    /// <summary>Seconds → plot X coordinate.</summary>
    private double ToX(double seconds) => IsDateAxis ? DateTime.UnixEpoch.AddSeconds(DateOrigin + seconds).ToOADate() : seconds;

    private double DateOrigin => AxisSpec?.XOriginUnixSeconds ?? 0;

    /// <summary>Width of one second in plot X units.</summary>
    private double Unit => IsDateAxis ? 1d / 86400d : 1d;

    private IReadOnlyList<ChartSeries> AllSeries => Series ?? Array.Empty<ChartSeries>();

    private ChartSeries? PrimarySeries =>
        AllSeries.FirstOrDefault(s => s.Kind == ChartSeriesKind.ScanBars) ?? AllSeries.FirstOrDefault();

    private SP.Color? Token(string key)
    {
        if (TryToken(key, ActualThemeVariant, out var c) || TryToken(key, Avalonia.Styling.ThemeVariant.Light, out c))
            return c;
        LogWarning($"Chart_Style: token '{key}' not resolvable; using the default value.");
        return null;
    }

    private bool TryToken(string key, Avalonia.Styling.ThemeVariant variant, out SP.Color color)
    {
        color = default;
        try
        {
            if (_tokens.TryResolveColor(key, variant, out var c))
            {
                color = new SP.Color(c.R, c.G, c.B, c.A);
                return true;
            }
        }
        catch (Exception)
        {
            // treated as not resolvable
        }
        return false;
    }

    // ---- rebuild ---------------------------------------------------------------------------------------------
    private void Rebuild()
    {
        SP.Plot plot = _avaPlot.Plot;
        var series = AllSeries;
        bool hasData = series.Any(s => s.Samples.Count > 0);

        _duration = hasData ? series.Where(s => s.Samples.Count > 0).Max(s => s.Samples[^1].TimeSeconds) : 0;
        if (hasData)
            _duration = Math.Max(_duration, series.SelectMany(s => s.Samples).Max(s => s.TimeSeconds));

        // X view
        double previousMin = _viewMin, previousMax = _viewMax;
        if (!hasData)
        {
            _viewMin = 0;
            _viewMax = EmptyXMax;
            _autoFit = true;
        }
        else if (_autoFit)
        {
            var fit = ZoomPanCalculator.AutoFitX(_duration);
            _viewMin = fit.Min;
            _viewMax = fit.Max > fit.Min ? fit.Max : fit.Min + EmptyXMax;
        }
        else
        {
            var c = ZoomPanCalculator.Constrain(new AxisRange(_viewMin, _viewMax), _duration);
            _viewMin = c.Min;
            _viewMax = c.Max > c.Min ? c.Max : c.Min + EmptyXMax;
        }

        plot.Clear();
        plot.Axes.Bottom.TickGenerator = new SP.TickGenerators.NumericAutomatic();

        // Y ranges from the visible data (Req. 39.5)
        double yMax = hasData ? VisibleMax(series, speed: false) : 0;
        var yRange = hasData ? ZoomPanCalculator.AutoFitY(yMax) : new AxisRange(0, EmptyYMax);
        double speedMax = hasData ? VisibleMax(series, speed: true) : 0;
        var speedRange = hasData && speedMax > 0 ? ZoomPanCalculator.AutoFitY(speedMax) : new AxisRange(0, EmptyYMax);

        var spec = AxisSpec;
        plot.Axes.Bottom.Label.Text = spec?.XLabel ?? string.Empty;
        plot.Axes.Left.Label.Text = spec?.YLabel ?? string.Empty;
        plot.Axes.Left.TickGenerator = new SP.TickGenerators.NumericAutomatic { IntegerTicksOnly = true };
        bool hasSpeed = series.Any(s => s.Kind == ChartSeriesKind.ScanBars && s.Samples.Any(x => x.SpeedX is not null));
        plot.Axes.Right.Label.Text = hasSpeed ? spec?.SecondaryYLabel ?? DefaultSpeedLabel : string.Empty;

        // highlight of seconds with errors (below the data)
        var scan = series.FirstOrDefault(s => s.Kind == ChartSeriesKind.ScanBars);
        var errors = scan is null ? Array.Empty<ChartSample>() : ErrorHighlighter.Seconds(scan.Samples);
        ErrorSeconds = errors.Count;
        AddErrorHighlight(plot, errors, yRange.Max);

        foreach (var s in series)
            AddSeries(plot, s);

        AddThresholds(plot, yRange);

        _cursorLine = plot.Add.VerticalLine(0);
        _cursorLine.LineWidth = 1;
        _cursorLine.IsVisible = _cursorTime is not null && !_exporting;
        if (Token(DasChartStyle.TickTextToken) is { } cursorColor)
            _cursorLine.Color = cursorColor;
        if (_cursorTime is { } ct)
            _cursorLine.X = ToX(ct);

        _selectionSpan = plot.Add.HorizontalSpan(0, 0);
        _selectionSpan.IsVisible = _selection is not null && !_exporting;
        if (Token("Das.Color.Primary") is { } primary)
            _selectionSpan.FillStyle.Color = primary.WithOpacity(SelectionOpacity);
        if (_selection is { } sel)
        {
            _selectionSpan.X1 = ToX(sel.T0);
            _selectionSpan.X2 = ToX(sel.T1);
        }

        ApplyLimitsAndTicks(plot, yRange, speedRange);

        // UI parts
        _emptyText.IsVisible = !hasData;
        _errorLabel.IsVisible = scan is not null;
        _errorLabel.Text = ErrorHighlighter.Label(errors.Count);
        _resetButton.IsEnabled = !_autoFit;
        UpdateSelectionPanel();

        _avaPlot.Refresh();

        if (previousMin != _viewMin || previousMax != _viewMax)
            VisibleRangeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Largest visible value: C1/C2/CU for scan series, C1 for curves; or SpeedX of scan series.</summary>
    private double VisibleMax(IReadOnlyList<ChartSeries> series, bool speed)
    {
        double max = 0;
        foreach (var s in series)
        {
            bool scan = s.Kind == ChartSeriesKind.ScanBars;
            if (speed && !scan) continue;
            foreach (var x in s.Samples)
            {
                if (x.TimeSeconds < _viewMin - 1 || x.TimeSeconds > _viewMax + 1) continue;
                double? v = speed ? x.SpeedX : scan ? new[] { x.C1, x.C2, x.CU }.Max() : x.C1;
                if (v is double d && !double.IsNaN(d) && d > max) max = d;
            }
        }
        return max;
    }
    private void AddErrorHighlight(SP.Plot plot, IReadOnlyList<ChartSample> errors, double yTop)
    {
        if (errors.Count == 0) return;
        var koSurface = Token("Das.Color.Status.KoSurface");
        var ko = Token("Das.Color.Status.Ko");
        double half = 0.5 * Unit;

        // consecutive seconds are merged into a single band
        double start = errors[0].TimeSeconds, end = start;
        void Flush()
        {
            var band = plot.Add.HorizontalSpan(ToX(start) - half, ToX(end) + half);
            band.LineWidth = 0;
            if (koSurface is { } c) band.FillStyle.Color = c.WithOpacity(ErrorBandOpacity);
        }
        for (int i = 1; i < errors.Count; i++)
        {
            double t = errors[i].TimeSeconds;
            if (t - end <= 1.0 + 1e-9) { end = t; continue; }
            Flush();
            start = end = t;
        }
        Flush();

        var xs = errors.Select(e => ToX(e.TimeSeconds)).ToArray();
        var ys = Enumerable.Repeat(yTop * 0.97, xs.Length).ToArray();
        var markers = plot.Add.Scatter(xs, ys);
        markers.LineWidth = 0;
        markers.MarkerShape = SP.MarkerShape.FilledTriangleDown;
        markers.MarkerSize = 8;
        if (ko is { } k) markers.Color = k;
    }

    private void AddThresholds(SP.Plot plot, AxisRange yRange)
    {
        if (Thresholds is not { } settings) return;
        foreach (var line in ThresholdLineSelector.Select(settings, yRange))
        {
            var h = plot.Add.HorizontalLine(line.Value);
            h.LineWidth = 1.5f;
            h.LinePattern = SP.LinePattern.Dashed;
            h.Text = line.Kind == ThresholdKind.Warn ? $"Soglia warning: {line.Value:0.##}" : $"Soglia KO: {line.Value:0.##}";
            if (Token(line.Kind == ThresholdKind.Warn ? "Das.Color.Status.Warn" : "Das.Color.Status.Ko") is { } color)
            {
                h.Color = color;
            }
        }
    }

    // ---- series rendering ------------------------------------------------------------------------------------
    private void AddSeries(SP.Plot plot, ChartSeries series)
    {
        if (series.Samples.Count == 0) return;
        if (series.Kind == ChartSeriesKind.ScanBars)
        {
            AddBars(plot, series, "C1", SeriesStyles.C1, s => s.C1, -0.3);
            AddBars(plot, series, "C2", SeriesStyles.C2, s => s.C2, 0);
            AddBars(plot, series, "CU", SeriesStyles.CU, s => s.CU, 0.3);
            AddLine(plot, series.Samples, "Velocità", SeriesStyles.SpeedX, s => s.SpeedX, secondary: true);
        }
        else
        {
            AddLine(plot, series.Samples, series.Name, SeriesStyles.ForCompareCurve(series.Index), s => s.C1, secondary: false);
        }
    }

    private void AddBars(SP.Plot plot, ChartSeries series, string name, SeriesStyle style,
        Func<ChartSample, double?> value, double offset)
    {
        var color = Token(style.ColorToken);
        var bars = new List<SP.Bar>();
        foreach (var s in series.Samples)
        {
            if (value(s) is not double v || double.IsNaN(v)) continue; // null values are omitted
            var bar = new SP.Bar { Position = ToX(s.TimeSeconds) + offset * Unit, Value = v, Size = 0.3 * Unit };
            if (color is { } c) bar.FillColor = c;
            ApplyHatch(bar, style.Fill, color);
            bars.Add(bar);
        }
        if (bars.Count == 0) return;
        var plottable = plot.Add.Bars(bars);
        plottable.LegendText = series.Index > 0 ? $"{series.Name} {name}" : name;
    }

    private static void ApplyHatch(SP.Bar bar, SeriesFillPattern fill, SP.Color? color)
    {
        switch (fill)
        {
            case SeriesFillPattern.DiagonalHatch:
                bar.FillHatch = new SP.Hatches.Striped(SP.Hatches.StripeDirection.DiagonalUp);
                break;
            case SeriesFillPattern.Dots:
                bar.FillHatch = new SP.Hatches.Dots();
                break;
        }
        if (fill is SeriesFillPattern.DiagonalHatch or SeriesFillPattern.Dots && color is { } c)
        {
            bar.FillHatchColor = c;
            bar.FillColor = c.WithOpacity(0.35);
            bar.LineColor = c;
            bar.LineWidth = 1;
        }
    }

    private void AddLine(SP.Plot plot, IReadOnlyList<ChartSample> samples, string name, SeriesStyle style,
        Func<ChartSample, double?> value, bool secondary)
    {
        var color = Token(style.ColorToken);
        bool first = true;
        var xs = new List<double>();
        var ys = new List<double>();
        void Flush()
        {
            if (xs.Count == 0) return;
            var sp = plot.Add.Scatter(xs.ToArray(), ys.ToArray());
            sp.LineWidth = 1.5f;
            sp.LinePattern = style.LineStyle switch
            {
                SeriesLineStyle.Dashed => SP.LinePattern.Dashed,
                SeriesLineStyle.Dotted => SP.LinePattern.Dotted,
                SeriesLineStyle.DashDot => SP.LinePattern.DenselyDashed,
                _ => SP.LinePattern.Solid,
            };
            sp.MarkerShape = style.Fill == SeriesFillPattern.None && secondary
                ? SP.MarkerShape.None
                : style.Marker switch
                {
                    SeriesMarker.Square => SP.MarkerShape.FilledSquare,
                    SeriesMarker.Diamond => SP.MarkerShape.FilledDiamond,
                    SeriesMarker.Triangle => SP.MarkerShape.FilledTriangleUp,
                    _ => SP.MarkerShape.FilledCircle,
                };
            sp.MarkerSize = 6;
            if (color is { } c) sp.Color = c;
            if (secondary) sp.Axes.YAxis = plot.Axes.Right;
            sp.LegendText = first ? name : string.Empty;
            first = false;
            xs.Clear();
            ys.Clear();
        }
        foreach (var s in samples)
        {
            if (value(s) is double v && !double.IsNaN(v))
            {
                xs.Add(ToX(s.TimeSeconds));
                ys.Add(v);
            }
            else
            {
                Flush(); // a null value interrupts the curve
            }
        }
        Flush();
    }

    // ---- axes -------------------------------------------------------------------------------------------------
    private void ApplyLimitsAndTicks(SP.Plot plot, AxisRange yRange, AxisRange speedRange)
    {
        plot.Axes.SetLimitsX(ToX(_viewMin), ToX(_viewMax));
        plot.Axes.SetLimitsY(yRange.Min, yRange.Max, plot.Axes.Left);
        plot.Axes.SetLimitsY(speedRange.Min, speedRange.Max, plot.Axes.Right);

        if (IsDateAxis)
        {
            plot.Axes.DateTimeTicksBottom();
            return;
        }

        double widthPx = PlotPixelWidth > 0 ? PlotPixelWidth : Math.Max(1, _avaPlot.Bounds.Width * Scale * 0.8);
        double interval = TimeTickIntervalSelector.Select(_viewMax - _viewMin, widthPx, LabelWidthEstimatePx * Scale);
        var ticks = new SP.TickGenerators.NumericManual();
        double minorStep = interval >= 5 ? interval / 5 : interval / 2;
        for (double t = Math.Ceiling(_viewMin / minorStep - 1e-9) * minorStep; t <= _viewMax + 1e-9; t += minorStep)
            ticks.AddMinor(t);
        for (double t = Math.Ceiling(_viewMin / interval - 1e-9) * interval; t <= _viewMax + 1e-9; t += interval)
            ticks.AddMajor(t, TimeTickFormatter.Format(t));
        plot.Axes.Bottom.TickGenerator = ticks;
    }

    // ---- pointer, cursor ------------------------------------------------------------------------------------
    private double? PointerToSeconds(Point p)
    {
        if (DataRect is not { Width: > 0 } r) return null;
        double fraction = (p.X * Scale - r.Left) / r.Width;
        return _viewMin + Math.Clamp(fraction, 0, 1) * (_viewMax - _viewMin);
    }

    private bool InsideData(Point p)
    {
        if (DataRect is not { Width: > 0 } r) return false;
        double x = p.X * Scale, y = p.Y * Scale;
        return x >= r.Left && x <= r.Right && y >= r.Top && y <= r.Bottom;
    }

    private void OnPlotPointerMoved(object? sender, PointerEventArgs e)
    {
        var p = e.GetPosition(_avaPlot);
        if (_selecting && PointerToSeconds(p) is { } t && _selectionAnchor is { } anchor)
        {
            SetSelection(anchor, t);
            return;
        }
        if (_panning)
        {
            PanBy(p);
            return;
        }
        if (PrimarySeries is null || !InsideData(p))
        {
            HideCursor();
            return;
        }
        if (PointerToSeconds(p) is { } pointerTime)
            ShowCursorAt(pointerTime, p);
    }

    private void OnPlotPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Focus();
        var props = e.GetCurrentPoint(_avaPlot).Properties;
        if (!props.IsLeftButtonPressed) return;
        var p = e.GetPosition(_avaPlot);
        if (!InsideData(p)) return;

        e.Pointer.Capture(_avaPlot);
        _panStart = _panLast = p;
        _panMoved = false;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && PointerToSeconds(p) is { } t)
        {
            _selecting = true;
            _selectionAnchor = t;
            SetSelection(t, t);
        }
        else
        {
            _panning = true;
        }
    }

    private void OnPlotPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        bool wasSelecting = _selecting;
        bool clickWithoutDrag = _panning && !_panMoved;
        _selecting = false;
        _panning = false;
        _selectionAnchor = null;
        e.Pointer.Capture(null);
        // click outside (no Shift, no drag) clears the selection
        if (clickWithoutDrag && !wasSelecting)
            ClearSelection();
    }

    private void OnPlotPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_duration <= 0) return;
        var center = PointerToSeconds(e.GetPosition(_avaPlot)) ?? (_viewMin + _viewMax) / 2;
        ZoomAt(center, e.Delta.Y);
        e.Handled = true;
    }

    private void ZoomAt(double center, double steps)
    {
        if (_duration <= 0 || steps == 0) return;
        var z = ZoomPanCalculator.Zoom(new AxisRange(_viewMin, _viewMax), center, steps, _duration);
        SetView(z);
    }

    private void PanBy(Point current)
    {
        if (DataRect is not { Width: > 0 } r || _duration <= 0) return;
        if (Math.Abs(current.X - _panStart.X) > DragThresholdPx || Math.Abs(current.Y - _panStart.Y) > DragThresholdPx)
            _panMoved = true;
        if (!_panMoved) return;
        double deltaPx = (current.X - _panLast.X) * Scale;
        _panLast = current;
        double deltaSeconds = -deltaPx * (_viewMax - _viewMin) / r.Width;
        SetView(ZoomPanCalculator.Pan(new AxisRange(_viewMin, _viewMax), deltaSeconds, _duration));
    }

    private void SetView(AxisRange range)
    {
        if (range.Max <= range.Min) return;
        _viewMin = range.Min;
        _viewMax = range.Max;
        _autoFit = _viewMin <= 0 && _viewMax >= _duration - 1e-9;
        RequestRedraw();
    }

    // ---- cursor and tooltip ---------------------------------------------------------------------------------
    private void ShowCursorAt(double pointerTime, Point pointer)
    {
        var primary = PrimarySeries;
        if (primary is null || primary.Samples.Count == 0) { HideCursor(); return; }
        int idx = NearestSampleFinder.Find(primary.Samples, pointerTime);
        if (idx < 0) { HideCursor(); return; }
        double snapped = primary.Samples[idx].TimeSeconds;
        _cursorTime = snapped;
        if (_cursorLine is not null)
        {
            _cursorLine.X = ToX(snapped);
            _cursorLine.IsVisible = true;
        }
        UpdateTooltip(snapped, pointer);
        _avaPlot.Refresh();
    }

    private void HideCursor()
    {
        if (_cursorTime is null && !_tooltip.IsVisible) return;
        _cursorTime = null;
        _tooltip.IsVisible = false;
        if (_cursorLine is not null) _cursorLine.IsVisible = false;
        _avaPlot.Refresh();
    }

    private void UpdateTooltip(double time, Point pointer)
    {
        var lines = new List<string>
        {
            IsDateAxis
                ? DateTime.UnixEpoch.AddSeconds(DateOrigin + time).ToString("d", System.Globalization.CultureInfo.CurrentCulture)
                : TimeTickFormatter.Format(time),
        };
        var series = AllSeries;
        if (series.Count(s => s.Kind != ChartSeriesKind.ScanBars) > 0 || series.Count > 1)
        {
            foreach (var s in series)
                lines.Add($"{s.Name}: {FormatValue(Nearest(s, time)?.C1, "0")}");
        }
        else if (series.Count == 1)
        {
            var sample = Nearest(series[0], time);
            lines.Add($"C1: {FormatValue(sample?.C1, "0")}");
            lines.Add($"C2: {FormatValue(sample?.C2, "0")}");
            lines.Add($"CU: {FormatValue(sample?.CU, "0")}");
            lines.Add($"Velocità: {FormatValue(sample?.SpeedX, "0.0", "x")}");
        }
        _tooltipText.Text = string.Join(Environment.NewLine, lines);
        _tooltip.IsVisible = true;
        _tooltip.Measure(Size.Infinity);
        var size = _tooltip.DesiredSize;

        double left = pointer.X + TooltipMargin, top = pointer.Y + TooltipMargin;
        double minX = 0, minY = 0, maxX = _overlay.Bounds.Width, maxY = _overlay.Bounds.Height;
        if (DataRect is { Width: > 0 } r)
        {
            minX = r.Left / Scale; minY = r.Top / Scale; maxX = r.Right / Scale; maxY = r.Bottom / Scale;
        }
        if (left + size.Width > maxX) left = pointer.X - TooltipMargin - size.Width; // flip near the right edge
        if (top + size.Height > maxY) top = pointer.Y - TooltipMargin - size.Height;
        Canvas.SetLeft(_tooltip, Math.Max(minX, left));
        Canvas.SetTop(_tooltip, Math.Max(minY, top));
    }

    private static ChartSample? Nearest(ChartSeries series, double time)
    {
        int i = NearestSampleFinder.Find(series.Samples, time);
        return i < 0 ? null : series.Samples[i];
    }

    private static string FormatValue(double? value, string format, string suffix = "") =>
        value is double v ? v.ToString(format, CultureInfo.CurrentCulture) + suffix : NotAvailable;

    // ---- selection -------------------------------------------------------------------------------------------
    private void SetSelection(double a, double b)
    {
        double t0 = Math.Min(a, b), t1 = Math.Max(a, b);
        _selection = (t0, t1);
        var primary = PrimarySeries;
        SelectedStatistics = primary is null ? null : SelectionStatistics.Compute(primary.Samples, t0, t1);
        if (_selectionSpan is not null)
        {
            _selectionSpan.X1 = ToX(t0);
            _selectionSpan.X2 = ToX(t1);
            _selectionSpan.IsVisible = true;
        }
        UpdateSelectionPanel();
        _avaPlot.Refresh();
    }

    private void UpdateSelectionPanel()
    {
        if (SelectedStatistics is not { } r || _selection is null)
        {
            _selectionPanel.IsVisible = false;
            return;
        }
        string Stat(string name, SelectionStats s, bool withSum, string format, string suffix = "")
        {
            if (s.Count == 0) return $"{name}: {NotAvailable}";
            string F(double? v) => FormatValue(v, format, suffix);
            var text = $"{name}: min {F(s.Min)}, max {F(s.Max)}, media {FormatValue(s.Mean, "0.00", suffix)}";
            return withSum ? text + $", somma {F(s.Sum)}" : text;
        }
        var lines = new[]
        {
            $"Selezione {TimeTickFormatter.Format(r.T0)} - {TimeTickFormatter.Format(r.T1)} (durata {TimeTickFormatter.Format(r.T1 - r.T0)})",
            Stat("C1", r.C1, false, "0"),
            Stat("C2", r.C2, true, "0"),
            Stat("CU", r.CU, true, "0"),
            Stat("Velocità", r.SpeedX, false, "0.0", "x"),
        };
        _selectionText.Text = string.Join(Environment.NewLine, lines);
        _selectionPanel.IsVisible = true;
    }

    // ---- keyboard --------------------------------------------------------------------------------------------
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        switch (e.Key)
        {
            case Key.Left:
            case Key.Right:
                MoveCursor(e.Key == Key.Left ? -1 : 1);
                e.Handled = true;
                break;
            case Key.Add:
            case Key.OemPlus:
                ZoomAt((_viewMin + _viewMax) / 2, 1);
                e.Handled = true;
                break;
            case Key.Subtract:
            case Key.OemMinus:
                ZoomAt((_viewMin + _viewMax) / 2, -1);
                e.Handled = true;
                break;
            case Key.D0:
            case Key.NumPad0:
                ResetZoom();
                e.Handled = true;
                break;
            case Key.Escape:
                ClearSelection();
                e.Handled = true;
                break;
        }
    }

    private void MoveCursor(int direction)
    {
        var primary = PrimarySeries;
        if (primary is null || primary.Samples.Count == 0) return;
        int current = _cursorTime is { } t
            ? NearestSampleFinder.Find(primary.Samples, t)
            : NearestSampleFinder.Find(primary.Samples, (_viewMin + _viewMax) / 2);
        int next = _cursorTime is null ? current : Math.Clamp(current + direction, 0, primary.Samples.Count - 1);
        double time = primary.Samples[next].TimeSeconds;
        var pixelX = DataRect is { Width: > 0 } r
            ? (r.Left + (time - _viewMin) / (_viewMax - _viewMin) * r.Width) / Scale
            : 0;
        var anchor = new Point(pixelX, (DataRect?.Top ?? 0) / Scale + 8);
        ShowCursorAt(time, anchor);
    }
}
