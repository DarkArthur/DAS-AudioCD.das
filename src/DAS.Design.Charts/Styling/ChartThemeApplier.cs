using Avalonia.Styling;
using ScottPlot;
using SkiaSharp;

namespace DAS.Design.Charts;

/// <summary>Applies the DAS Chart_Style (tokens of the given theme variant) to a ScottPlot <see cref="Plot"/>.</summary>
public static class ChartThemeApplier
{
    /// <summary>
    /// Applies figure/data backgrounds, grid, axes, texts, legend and fonts.
    /// An unresolvable token falls back to its Light value and logs a warning with the Token_Key;
    /// if that fails too the property keeps its current value. Never throws for missing tokens (Req. 38.17).
    /// </summary>
    /// <param name="log">Warning sink (delegate); no logging dependency is required.</param>
    public static void Apply(Plot plot, ThemeVariant variant, ITokenResolver tokens, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(plot);
        ArgumentNullException.ThrowIfNull(tokens);

        Color? Resolve(string key) => ResolveColor(key, variant, tokens, log);

        if (Resolve(DasChartStyle.FigureBackgroundToken) is { } figure)
            plot.FigureBackground.Color = figure;
        if (Resolve(DasChartStyle.DataBackgroundToken) is { } data)
            plot.DataBackground.Color = data;

        if (Resolve(DasChartStyle.GridToken) is { } grid)
        {
            plot.Grid.MajorLineColor = grid.WithOpacity(DasChartStyle.MajorGridOpacity);
            plot.Grid.MinorLineColor = grid.WithOpacity(DasChartStyle.MinorGridOpacity);
        }

        var axisColor = Resolve(DasChartStyle.AxisToken);
        var tickText = Resolve(DasChartStyle.TickTextToken);
        var axisLabel = Resolve(DasChartStyle.AxisLabelToken);
        var title = Resolve(DasChartStyle.TitleToken);

        var fontName = ResolveFontName();

        foreach (var axis in plot.Axes.GetAxes())
        {
            if (axisColor is { } ac)
            {
                axis.FrameLineStyle.Color = ac;
                axis.MajorTickStyle.Color = ac;
                axis.MinorTickStyle.Color = ac;
            }

            axis.TickLabelStyle.FontName = fontName;
            axis.TickLabelStyle.FontSize = DasChartStyle.TickFontSize;
            if (tickText is { } tt)
                axis.TickLabelStyle.ForeColor = tt;

            axis.Label.FontName = fontName;
            axis.Label.FontSize = DasChartStyle.AxisLabelFontSize;
            axis.Label.Bold = true;
            if (axisLabel is { } al)
                axis.Label.ForeColor = al;
        }

        plot.Axes.Title.Label.FontName = fontName;
        plot.Axes.Title.Label.FontSize = DasChartStyle.TitleFontSize;
        plot.Axes.Title.Label.Bold = true;
        if (title is { } t)
            plot.Axes.Title.Label.ForeColor = t;

        ApplyLegend(plot, fontName, Resolve(DasChartStyle.LegendBackgroundToken),
            Resolve(DasChartStyle.LegendBorderToken), Resolve(DasChartStyle.LegendTextToken));
    }

    private static void ApplyLegend(Plot plot, string fontName, Color? background, Color? border, Color? text)
    {
        var legend = plot.Legend;
        legend.FontName = fontName;
        legend.FontSize = DasChartStyle.TickFontSize;
        if (background is { } bg)
            legend.BackgroundColor = bg;
        if (border is { } bd)
            legend.OutlineColor = bd;
        if (text is { } tx)
            legend.FontColor = tx;

        // Outside the data area, below the chart, centered; horizontal layout wraps on the available width
        // (at most DasChartStyle.MaxLegendRows rows: the chart has at most 8 series).
        legend.Orientation = Orientation.Horizontal;
        plot.ShowLegend(Edge.Bottom);
    }

    private static Color? ResolveColor(string key, ThemeVariant variant, ITokenResolver tokens, Action<string>? log)
    {
        if (TryResolve(key, variant, tokens, out var color))
            return ToScottPlot(color);

        log?.Invoke($"Chart_Style: token '{key}' not resolvable for variant '{variant}'; using the Light value.");

        if (TryResolve(key, ThemeVariant.Light, tokens, out var light))
            return ToScottPlot(light);

        log?.Invoke($"Chart_Style: token '{key}' not resolvable for the Light variant; keeping the default value.");
        return null;
    }

    private static bool TryResolve(string key, ThemeVariant variant, ITokenResolver tokens, out Avalonia.Media.Color color)
    {
        try
        {
            return tokens.TryResolveColor(key, variant, out color);
        }
        catch (Exception)
        {
            color = default;
            return false;
        }
    }

    private static Color ToScottPlot(Avalonia.Media.Color c) => new(c.R, c.G, c.B, c.A);

    /// <summary>Returns Inter when installed/loadable, otherwise the first available fallback family.</summary>
    private static string ResolveFontName()
    {
        foreach (var family in new[] { DasChartStyle.PreferredFontFamily }.Concat(DasChartStyle.FallbackFontFamilies))
        {
            try
            {
                using var typeface = SKFontManager.Default.MatchFamily(family);
                if (typeface is not null && string.Equals(typeface.FamilyName, family, StringComparison.OrdinalIgnoreCase))
                    return family;
            }
            catch (Exception)
            {
                // try next family
            }
        }

        return Fonts.Default;
    }
}
