namespace DAS.Design.Charts;

public enum SeriesLineStyle { Solid, Dashed, Dotted, DashDot }

public enum SeriesFillPattern { None, Solid, DiagonalHatch, Dots }

public enum SeriesMarker { Circle, Square, Diamond, Triangle }

/// <summary>Stile di una serie. <see cref="ColorToken"/> è la chiave di risorsa del colore (nessun colore letterale).</summary>
public record SeriesStyle(
    string ColorToken,
    SeriesLineStyle LineStyle,
    SeriesFillPattern Fill,
    SeriesMarker Marker);

/// <summary>Stili che rendono distinguibili le serie anche senza colore.</summary>
public static class SeriesStyles
{
    public const int CompareSeriesCount = 8;

    public static SeriesStyle C1 { get; } =
        new("Das.Color.Chart.C1", SeriesLineStyle.Solid, SeriesFillPattern.Solid, SeriesMarker.Circle);

    public static SeriesStyle C2 { get; } =
        new("Das.Color.Chart.C2", SeriesLineStyle.Solid, SeriesFillPattern.DiagonalHatch, SeriesMarker.Square);

    public static SeriesStyle CU { get; } =
        new("Das.Color.Chart.CU", SeriesLineStyle.Solid, SeriesFillPattern.Dots, SeriesMarker.Diamond);

    public static SeriesStyle SpeedX { get; } =
        new("Das.Color.Chart.Speed", SeriesLineStyle.Dashed, SeriesFillPattern.None, SeriesMarker.Circle);

    private static readonly SeriesLineStyle[] LineStyles =
        [SeriesLineStyle.Solid, SeriesLineStyle.Dashed, SeriesLineStyle.Dotted, SeriesLineStyle.DashDot];

    private static readonly SeriesMarker[] Markers =
        [SeriesMarker.Circle, SeriesMarker.Square, SeriesMarker.Diamond, SeriesMarker.Triangle];

    /// <summary>Token colore della curva di confronto <paramref name="index"/> (0-based): <c>Series.(i+1)</c>, ciclico su 8.</summary>
    public static string SeriesColorToken(int index) =>
        $"Das.Color.Series.{Mod(index, CompareSeriesCount) + 1}";

    /// <summary>
    /// Stile della curva di confronto <paramref name="index"/>: colore <c>Series.(i+1)</c>,
    /// tratto <c>i mod 4</c>, marcatore <c>(i / 2) mod 4</c>.
    /// Su 8 curve due qualsiasi differiscono per colore e per tratto o marcatore.
    /// </summary>
    public static SeriesStyle ForCompareCurve(int index)
    {
        int i = Mod(index, CompareSeriesCount);
        return new SeriesStyle(
            SeriesColorToken(i),
            LineStyles[i % 4],
            SeriesFillPattern.None,
            Markers[(i / 2) % 4]);
    }

    private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;
}
