using DAS.Design.Charts;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace DAS.Design.Charts.Tests;

public class ThresholdLineSelectorProperties
{
    // Valori in quarti, in un intervallo che include punti dentro e fuori dalla vista.
    private static Gen<double> Val() => Gen.Choose(-20, 120).Select(v => v / 4.0);

    private static Arbitrary<(ThresholdSettings Settings, AxisRange Range)> Input() =>
        Gen.Zip(Gen.Zip(Val(), Val()), Gen.Zip(Gen.Elements(true, false), Gen.Elements(true, false)))
            .SelectMany(a => Gen.Zip(Val(), Val()).Select(r =>
                (Settings: new ThresholdSettings(a.Item1.Item1, a.Item1.Item2, a.Item2.Item1, a.Item2.Item2),
                 Range: new AxisRange(Math.Min(r.Item1, r.Item2), Math.Max(r.Item1, r.Item2)))))
            .ToArbitrary();

    // Feature: disccheck, Property 27: Le linee soglia rispettano scanner, opzione e intervallo visibile
    // **Validates: Requirements 39.8, 39.9**
    [Property(MaxTest = 200)]
    public Property ThresholdLines_respect_scanner_option_and_visible_range() =>
        Prop.ForAll(Input(), input =>
        {
            var (s, range) = input;
            var lines = ThresholdLineSelector.Select(s, range);
            bool enabled = s.IsLiteOn && s.ShowThresholds;

            bool inRange(double v) => v >= range.Min && v <= range.Max;
            var warn = lines.Where(l => l.Kind == ThresholdKind.Warn).ToList();
            var ko = lines.Where(l => l.Kind == ThresholdKind.Ko).ToList();

            bool warnOk = warn.Count == (enabled && inRange(s.C1MaxWarn) ? 1 : 0)
                          && warn.All(l => l.Value == s.C1MaxWarn);
            bool koOk = ko.Count == (enabled && inRange(s.C1MaxKo) ? 1 : 0)
                        && ko.All(l => l.Value == s.C1MaxKo);

            return warnOk && koOk && lines.Count == warn.Count + ko.Count
                   && (enabled || lines.Count == 0)
                   && lines.All(l => inRange(l.Value));
        });
}
