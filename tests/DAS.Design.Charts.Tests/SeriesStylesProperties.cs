using DAS.Design.Charts;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace DAS.Design.Charts.Tests;

public class SeriesStylesProperties
{
    private static Arbitrary<(int I, int J)> DistinctPair() =>
        Gen.Zip(Gen.Choose(0, 7), Gen.Choose(0, 7))
            .Where(p => p.Item1 != p.Item2)
            .Select(p => (I: p.Item1, J: p.Item2))
            .ToArbitrary();

    // Feature: disccheck, Property 30: Le serie sono distinguibili anche senza il colore
    // **Validates: Requirements 38.12, 37.13**
    [Property(MaxTest = 100)]
    public Property CompareCurves_are_distinguishable_without_color() =>
        Prop.ForAll(DistinctPair(), p =>
        {
            var a = SeriesStyles.ForCompareCurve(p.I);
            var b = SeriesStyles.ForCompareCurve(p.J);
            return a.ColorToken == $"Das.Color.Series.{p.I + 1}"
                   && b.ColorToken == $"Das.Color.Series.{p.J + 1}"
                   && a.LineStyle == (SeriesLineStyle)(p.I % 4)
                   && a.Marker == (SeriesMarker)((p.I / 2) % 4)
                   && a.ColorToken != b.ColorToken
                   && (a.LineStyle != b.LineStyle || a.Marker != b.Marker);
        });

    // Feature: disccheck, Property 30: Le serie sono distinguibili anche senza il colore
    // **Validates: Requirements 38.12, 37.13**
    [Property(MaxTest = 100)]
    public Property ScanSeries_are_distinguishable_without_color() =>
        Prop.ForAll(
            Gen.Zip(Gen.Choose(0, 3), Gen.Choose(0, 3)).Where(p => p.Item1 != p.Item2).ToArbitrary(),
            p =>
            {
                SeriesStyle[] scan = [SeriesStyles.C1, SeriesStyles.C2, SeriesStyles.CU, SeriesStyles.SpeedX];
                var a = scan[p.Item1];
                var b = scan[p.Item2];
                return a.ColorToken != b.ColorToken
                       && (a.Fill != b.Fill || a.LineStyle != b.LineStyle || a.Marker != b.Marker);
            });
}
