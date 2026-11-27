using DAS.Design.Charts.Formatting;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace DAS.Design.Charts.Tests;

public class TimeTickIntervalSelectorProperties
{
    private static readonly int[] Allowed = { 1, 2, 5, 10, 15, 30, 60, 120, 300, 600 };

    // Durata visibile da 3 s a 6600 s (110 min, oltre un CD da 80 min).
    private static Arbitrary<double> Spans() => Gen.Choose(3_000, 6_600_000).Select(ms => ms / 1000.0).ToArbitrary();

    // Larghezza grafico e etichetta tali da consentire almeno 12 etichette senza sovrapposizione.
    private static Arbitrary<(double Plot, double Label)> Widths() =>
        Gen.Choose(800, 3000).SelectMany(p => Gen.Choose(20, 60).Select(l => ((double)p, (double)l))).ToArbitrary();

    // Feature: disccheck, Property 26: Scelta dell'intervallo dei tick tempo
    // **Validates: Requirements 38.6**
    [Property(MaxTest = 200)]
    public Property Interval_is_allowed_with_4_to_12_ticks_and_no_label_overlap() =>
        Prop.ForAll(Spans(), Widths(), (span, w) =>
        {
            double interval = TimeTickIntervalSelector.Select(span, w.Plot, w.Label);
            int ticks = TimeTickIntervalSelector.TickCount(span, interval);
            bool allowed = Array.IndexOf(Allowed, (int)interval) >= 0 && interval == (int)interval;
            bool count = ticks >= 4 && ticks <= 12;
            bool noOverlap = interval / span * w.Plot >= w.Label;
            return allowed && count && noOverlap;
        });
}
