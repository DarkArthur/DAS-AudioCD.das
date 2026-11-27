namespace DAS.Design.Charts;

/// <summary>
/// Chart_Style shared by every DAS chart: Token_Keys read from DAS.Design and typography.
/// Contains no literal colors; all colors come from the design tokens (Req. 38.2, 38.17, 35.4).
/// </summary>
public static class DasChartStyle
{
    public const string FigureBackgroundToken = "Das.Color.Background";
    public const string DataBackgroundToken = "Das.Color.Surface";
    public const string GridToken = "Das.Color.Border";
    public const string AxisToken = "Das.Color.Border";
    public const string TickTextToken = "Das.Color.Text";
    public const string AxisLabelToken = "Das.Color.TextSecondary";
    public const string TitleToken = "Das.Color.Text";
    public const string LegendBackgroundToken = "Das.Color.SurfaceAlt";
    public const string LegendBorderToken = "Das.Color.Border";
    public const string LegendTextToken = "Das.Color.Text";

    /// <summary>Opacity of the major grid lines.</summary>
    public const double MajorGridOpacity = 1.0;

    /// <summary>Opacity of the minor grid lines.</summary>
    public const double MinorGridOpacity = 0.4;

    /// <summary>Preferred font family; the embedded Inter font of DAS.Design.</summary>
    public const string PreferredFontFamily = "Inter";

    /// <summary>Fallback font families, in order, when Inter is not available.</summary>
    public static readonly IReadOnlyList<string> FallbackFontFamilies =
        new[] { "Segoe UI", "Helvetica Neue", "Arial", "DejaVu Sans" };

    public const float TickFontSize = 12f;
    public const float AxisLabelFontSize = 14f;
    public const float TitleFontSize = 20f;

    /// <summary>Semi-bold weight (600) used for axis labels and title.</summary>
    public const int SemiBoldWeight = 600;

    /// <summary>Maximum number of rows of the legend placed below the chart.</summary>
    public const int MaxLegendRows = 4;
}
