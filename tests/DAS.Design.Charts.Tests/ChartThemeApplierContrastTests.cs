using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using DiscCheck.Core;
using FluentAssertions;
using ScottPlot;

namespace DAS.Design.Charts.Tests;

/// <summary>
/// ChartThemeApplier (Light and Dark): colors equal to the tokens and WCAG contrast of the chart palette.
/// Validates: Requirements 37.13, 38.2, 38.11, 29.8, 29.9.
/// </summary>
public class ChartThemeApplierContrastTests
{
    public static TheoryData<string> Variants => new() { "Light", "Dark" };

    private static ThemeVariant Variant(string name) => name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

    private static Avalonia.Media.Color Token(string key, ThemeVariant variant)
    {
        new AvaloniaTokenResolver().TryResolveColor(key, variant, out var c)
            .Should().BeTrue($"token '{key}' must resolve for {variant}");
        return c;
    }

    private static Color Sp(Avalonia.Media.Color c) => new(c.R, c.G, c.B, c.A);

    private static (byte R, byte G, byte B) Rgb(Avalonia.Media.Color c) => (c.R, c.G, c.B);

    [AvaloniaTheory]
    [MemberData(nameof(Variants))]
    public void Apply_sets_every_color_equal_to_the_tokens(string name)
    {
        var variant = Variant(name);
        var plot = new Plot();

        ChartThemeApplier.Apply(plot, variant, new AvaloniaTokenResolver());

        plot.FigureBackground.Color.Should().Be(Sp(Token(DasChartStyle.FigureBackgroundToken, variant)));
        plot.DataBackground.Color.Should().Be(Sp(Token(DasChartStyle.DataBackgroundToken, variant)));

        var grid = Sp(Token(DasChartStyle.GridToken, variant));
        plot.Grid.XAxisStyle.MajorLineStyle.Color.Should().Be(grid.WithOpacity(1.0));
        plot.Grid.YAxisStyle.MajorLineStyle.Color.Should().Be(grid.WithOpacity(1.0));
        plot.Grid.XAxisStyle.MinorLineStyle.Color.Should().Be(grid.WithOpacity(0.4));
        plot.Grid.YAxisStyle.MinorLineStyle.Color.Should().Be(grid.WithOpacity(0.4));

        var axis = Sp(Token(DasChartStyle.AxisToken, variant));
        var tick = Sp(Token(DasChartStyle.TickTextToken, variant));
        var label = Sp(Token(DasChartStyle.AxisLabelToken, variant));
        foreach (var a in plot.Axes.GetAxes())
        {
            a.FrameLineStyle.Color.Should().Be(axis);
            a.MajorTickStyle.Color.Should().Be(axis);
            a.MinorTickStyle.Color.Should().Be(axis);
            a.TickLabelStyle.ForeColor.Should().Be(tick);
            a.Label.ForeColor.Should().Be(label);
        }

        plot.Axes.Title.Label.ForeColor.Should().Be(Sp(Token(DasChartStyle.TitleToken, variant)));
        plot.Legend.BackgroundColor.Should().Be(Sp(Token(DasChartStyle.LegendBackgroundToken, variant)));
        plot.Legend.OutlineColor.Should().Be(Sp(Token(DasChartStyle.LegendBorderToken, variant)));
        plot.Legend.FontColor.Should().Be(Sp(Token(DasChartStyle.LegendTextToken, variant)));
    }

    private static IEnumerable<string> GraphicTokens()
    {
        foreach (var s in new[] { SeriesStyles.C1, SeriesStyles.C2, SeriesStyles.CU, SeriesStyles.SpeedX })
            yield return s.ColorToken;
        for (int i = 0; i < SeriesStyles.CompareSeriesCount; i++)
            yield return SeriesStyles.SeriesColorToken(i);
        yield return "Das.Color.Status.Warn"; // warning threshold
        yield return "Das.Color.Status.Ko";   // KO threshold and error highlight
        yield return "Das.Color.Primary";     // selection highlight
    }

    [AvaloniaTheory]
    [MemberData(nameof(Variants))]
    public void Graphic_objects_have_at_least_3_to_1_contrast_on_Surface(string name)
    {
        var variant = Variant(name);
        var surface = Rgb(Token(DasChartStyle.DataBackgroundToken, variant));

        var failures = GraphicTokens().Distinct()
            .Select(k => (Key: k, Ratio: ContrastCalculator.ContrastRatio(Rgb(Token(k, variant)), surface)))
            .Where(x => x.Ratio < 3.0)
            .Select(x => $"{x.Key} on Surface ({name}) = {x.Ratio:0.00}:1")
            .ToList();

        failures.Should().BeEmpty();
    }

    [AvaloniaTheory]
    [MemberData(nameof(Variants))]
    public void Texts_have_at_least_4_5_to_1_contrast_on_Background_and_Surface(string name)
    {
        var variant = Variant(name);
        string[] textTokens =
        [
            DasChartStyle.TickTextToken, DasChartStyle.AxisLabelToken,
            DasChartStyle.TitleToken, DasChartStyle.LegendTextToken,
        ];
        string[] backgrounds = [DasChartStyle.FigureBackgroundToken, DasChartStyle.DataBackgroundToken];

        var failures = (from t in textTokens.Distinct()
                        from b in backgrounds
                        let ratio = ContrastCalculator.ContrastRatio(Rgb(Token(t, variant)), Rgb(Token(b, variant)))
                        where ratio < 4.5
                        select $"{t} on {b} ({name}) = {ratio:0.00}:1").ToList();

        failures.Should().BeEmpty();
    }
}
