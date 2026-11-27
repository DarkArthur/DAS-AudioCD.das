using DAS.Design.Charts;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace DAS.Design.Charts.Tests;

public class ZoomPanCalculatorProperties
{
    private const double Eps = 1e-9;

    // Durata 1..3600 s, intervallo corrente valido dentro [0, durata], puntatore e scatti arbitrari.
    private static Arbitrary<(double Duration, AxisRange Range, double Pointer, int Steps, double Delta)> Input() =>
        Gen.Zip(
                Gen.Zip(Gen.Choose(1, 3600), Gen.Choose(0, 1000)),
                Gen.Zip(Gen.Zip(Gen.Choose(0, 1000), Gen.Choose(-12, 12)), Gen.Choose(-4000, 4000)),
                (a, b) => (Item1: a, Item2: b.Item1, Item3: b.Item2))
            .Select(p =>
            {
                double duration = p.Item1.Item1;
                double minSpan = Math.Min(5, duration);
                double span = minSpan + (duration - minSpan) * p.Item1.Item2 / 1000.0;
                double min = (duration - span) * p.Item2.Item1 / 1000.0;
                var range = new AxisRange(min, min + span);
                double pointer = min + span * (p.Item2.Item1 / 1000.0);
                return (duration, range, pointer, p.Item2.Item2, (double)p.Item3);
            })
            .ToArbitrary();

    // Feature: disccheck, Property 29: Zoom e pan restano nei limiti dei dati
    // **Validates: Requirements 39.4, 39.5**
    [Property(MaxTest = 200)]
    public Property Zoom_span_stays_within_min5s_and_duration_and_inside_data() =>
        Prop.ForAll(Input(), i =>
        {
            var r = ZoomPanCalculator.Zoom(i.Range, i.Pointer, i.Steps, i.Duration);
            double span = r.Max - r.Min;
            double minSpan = Math.Min(5, i.Duration);
            return span >= minSpan - Eps && span <= i.Duration + Eps
                   && r.Min >= -Eps && r.Max <= i.Duration + Eps;
        });

    // Feature: disccheck, Property 29: Zoom e pan restano nei limiti dei dati
    // **Validates: Requirements 39.4, 39.5**
    [Property(MaxTest = 200)]
    public Property Pan_stays_within_zero_and_duration_and_keeps_span() =>
        Prop.ForAll(Input(), i =>
        {
            var r = ZoomPanCalculator.Pan(i.Range, i.Delta, i.Duration);
            double span = i.Range.Max - i.Range.Min;
            return r.Min >= -Eps && r.Max <= i.Duration + Eps
                   && Math.Abs((r.Max - r.Min) - span) <= Eps;
        });

    // Feature: disccheck, Property 29: Zoom e pan restano nei limiti dei dati
    // **Validates: Requirements 39.4**
    [Property(MaxTest = 200)]
    public Property Zoom_one_step_scales_span_by_1_25_when_not_clamped() =>
        Prop.ForAll(Input(), i =>
        {
            double span = i.Range.Max - i.Range.Min;
            double minSpan = Math.Min(5, i.Duration);
            double inSpan = span / 1.25;
            double outSpan = span * 1.25;
            bool zoomInOk = inSpan < minSpan + 1e-6 ||
                Math.Abs((ZoomPanCalculator.Zoom(i.Range, i.Pointer, 1, i.Duration) is var a ? a.Max - a.Min : 0) - inSpan) <= 1e-7;
            bool zoomOutOk = outSpan > i.Duration - 1e-6 ||
                Math.Abs((ZoomPanCalculator.Zoom(i.Range, i.Pointer, -1, i.Duration) is var b ? b.Max - b.Min : 0) - outSpan) <= 1e-7;
            return zoomInOk && zoomOutOk;
        });

    // Feature: disccheck, Property 29: Zoom e pan restano nei limiti dei dati
    // **Validates: Requirements 39.4**
    [Property(MaxTest = 200)]
    public Property Zoom_keeps_pointer_position_when_not_clamped_at_edges() =>
        Prop.ForAll(Input(), i =>
        {
            double span = i.Range.Max - i.Range.Min;
            double newSpan = span / 1.25;
            if (newSpan < Math.Min(5, i.Duration) + 1e-6) return true;
            var r = ZoomPanCalculator.Zoom(i.Range, i.Pointer, 1, i.Duration);
            double before = (i.Pointer - i.Range.Min) / span;
            double after = (i.Pointer - r.Min) / (r.Max - r.Min);
            return Math.Abs(before - after) <= 1e-7;
        });

    // Feature: disccheck, Property 29: Zoom e pan restano nei limiti dei dati
    // **Validates: Requirements 39.5**
    [Property(MaxTest = 200)]
    public Property AutoFit_x_spans_zero_to_duration_and_y_is_1_05_max_with_minimum_10() =>
        Prop.ForAll(Gen.Choose(0, 100000).Select(x => x / 10.0).ToArbitrary(), max =>
        {
            var x = ZoomPanCalculator.AutoFitX(max);
            var y = ZoomPanCalculator.AutoFitY(max);
            return x.Min == 0 && x.Max == max && y.Min == 0
                   && Math.Abs(y.Max - Math.Max(10, max * 1.05)) <= Eps;
        });
}
