using DAS.Design.Charts;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace DAS.Design.Charts.Tests;

public class ErrorHighlighterProperties
{
    // Valori nulli, zero e positivi.
    private static Gen<double?> Metric() =>
        Gen.OneOf(
            Gen.Constant<double?>(null),
            Gen.Constant<double?>(0.0),
            Gen.Choose(1, 50).Select(v => (double?)v));

    private static Arbitrary<List<ChartSample>> Samples() =>
        Gen.Zip(Metric(), Metric())
            .Select(m => new ChartSample(0, null, m.Item1, m.Item2, null))
            .ListOf()
            .Select(l => l.Select((s, i) => s with { TimeSeconds = i }).ToList())
            .ToArbitrary();

    // Feature: disccheck, Property 28: Conteggio dei secondi con errori
    // **Validates: Requirements 39.10, 39.11**
    [Property(MaxTest = 200)]
    public Property Highlighted_seconds_equal_samples_with_C2_or_CU_above_zero() =>
        Prop.ForAll(Samples(), samples =>
        {
            var result = ErrorHighlighter.Seconds(samples);
            int expected = samples.Count(s => s.C2 > 0 || s.CU > 0);

            return result.Count == expected
                   && result.All(s => s.C2 > 0 || s.CU > 0)
                   && result.All(samples.Contains);
        });
}
