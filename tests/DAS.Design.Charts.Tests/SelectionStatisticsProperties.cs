using DAS.Design.Charts;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace DAS.Design.Charts.Tests;

public class SelectionStatisticsProperties
{
    // Valori multipli di 0,25 (somme esatte in binario) oppure nulli.
    private static Gen<double?> Value() =>
        Gen.Choose(0, 40).SelectMany(v => Gen.Choose(0, 3).Select(n => n == 0 ? (double?)null : v / 4.0));

    private static Gen<ChartSample> Sample() =>
        Gen.Zip(Gen.Zip(Gen.Choose(0, 80), Value()), Gen.Zip(Value(), Gen.Zip(Value(), Value())))
            .Select(t => new ChartSample(t.Item1.Item1 / 4.0, t.Item1.Item2, t.Item2.Item1, t.Item2.Item2.Item1, t.Item2.Item2.Item2));

    private static Arbitrary<(ChartSample[] Samples, double T0, double T1)> Input() =>
        Gen.Zip(Sample().ArrayOf(), Gen.Zip(Gen.Choose(-8, 88), Gen.Choose(-8, 88)))
            .Select(p => (Samples: p.Item1, T0: Math.Min(p.Item2.Item1, p.Item2.Item2) / 4.0, T1: Math.Max(p.Item2.Item1, p.Item2.Item2) / 4.0))
            .ToArbitrary();

    private static bool Matches(SelectionStats s, IEnumerable<double?> inRange)
    {
        var vals = inRange.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        if (vals.Count == 0)
            return s.Count == 0 && s.Min is null && s.Max is null && s.Mean is null && s.Sum is null;

        double sum = vals.Sum();
        return s.Count == vals.Count
               && s.Min == vals.Min() && s.Max == vals.Max()
               && s.Sum == sum && s.Mean == sum / vals.Count
               && s.Min <= s.Mean && s.Mean <= s.Max;
    }

    // Feature: disccheck, Property 24: Le statistiche di selezione sono coerenti con il calcolo di riferimento
    // **Validates: Requirements 39.6, 37.13**
    [Property(MaxTest = 200)]
    public Property SelectionStatistics_matches_naive_reference() =>
        Prop.ForAll(Input(), input =>
        {
            var (samples, t0, t1) = input;
            var r = SelectionStatistics.Compute(samples, t0, t1);
            var inRange = samples.Where(s => s.TimeSeconds >= t0 && s.TimeSeconds <= t1).ToList();

            return Matches(r.C1, inRange.Select(s => s.C1))
                   && Matches(r.C2, inRange.Select(s => s.C2))
                   && Matches(r.CU, inRange.Select(s => s.CU))
                   && Matches(r.SpeedX, inRange.Select(s => s.SpeedX));
        });
}
