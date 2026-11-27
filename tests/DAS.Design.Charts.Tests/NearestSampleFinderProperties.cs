using DAS.Design.Charts;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace DAS.Design.Charts.Tests;

public class NearestSampleFinderProperties
{
    // Tempi multipli di 0,25 s (esatti in binario): ordinati, con duplicati e pareggi di distanza frequenti.
    private static Arbitrary<(double[] Times, double T)> Input() =>
        Gen.Zip(
                Gen.Choose(0, 400).ArrayOf().Where(a => a.Length > 0),
                Gen.Choose(-40, 440))
            .Select(p => (Times: p.Item1.OrderBy(x => x).Select(x => x / 4.0).ToArray(), T: p.Item2 / 8.0))
            .ToArbitrary();

    // Feature: disccheck, Property 23: Il Chart_Cursor sceglie il campione a distanza minima
    // **Validates: Requirements 39.1, 39.13, 37.13**
    [Property(MaxTest = 200)]
    public Property NearestSampleFinder_returns_minimum_distance_with_lower_time_on_tie() =>
        Prop.ForAll(Input(), input =>
        {
            var (times, t) = input;
            int idx = NearestSampleFinder.Find(times, t);

            // Riferimento lineare: minima distanza assoluta, a parità il tempo minore.
            double bestDist = times.Min(x => Math.Abs(x - t));
            double expectedTime = times.Where(x => Math.Abs(x - t) == bestDist).Min();

            return idx >= 0 && idx < times.Length
                   && Math.Abs(times[idx] - t) == bestDist
                   && times[idx] == expectedTime;
        });
}
