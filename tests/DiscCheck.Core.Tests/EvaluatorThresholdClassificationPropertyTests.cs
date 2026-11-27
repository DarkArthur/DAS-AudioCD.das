using DiscCheck.Core;
using FsCheck.Xunit;

namespace DiscCheck.Core.Tests;

public class EvaluatorThresholdClassificationPropertyTests
{
    // Feature: disccheck, Property 4: Classificazione del Verdetto secondo le Soglie
    // **Validates: Requirements 11.1, 11.2, 11.3, 11.4**
    [Property(MaxTest = 200)]
    public bool Verdict_MatchesThresholdRulesAndIsDeterministic(
        byte[] c1Seed, byte[] c2Seed, byte[] cuSeed, byte meanSeed, byte warnSeed, byte koSeed)
    {
        int n = new[] { c1Seed.Length, c2Seed.Length, cuSeed.Length }.Max();
        if (n == 0) return true; // empty input has no verdict (Req. 11.10)

        // Valid thresholds: 0 <= C1MeanWarn, C1MaxWarn <= C1MaxKo.
        double meanWarn = meanSeed % 20 / 2.0;
        double maxWarn = warnSeed;
        double maxKo = maxWarn + koSeed;
        var thresholds = new Thresholds(meanWarn, maxWarn, maxKo);

        var samples = new List<ScanSample>();
        for (int i = 0; i < n; i++)
        {
            int? c1 = Pick(c1Seed, i) % 7 == 0 ? null : Pick(c1Seed, i) % 256 * (Pick(c1Seed, i) % 3 == 0 ? 1 : 0) + Pick(c1Seed, i) % 5;
            int c2 = Pick(c2Seed, i) % 8 == 0 ? Pick(c2Seed, i) / 8 % 3 : 0; // mostly zero
            int cu = Pick(cuSeed, i) % 8 == 0 ? Pick(cuSeed, i) / 8 % 3 : 0;
            samples.Add(new ScanSample(i * 75, i, c1, c2, cu, 4.0));
        }

        // Independent reference computation.
        long c2Tot = samples.Sum(s => (long)s.C2);
        long cuTot = samples.Sum(s => (long)(s.CU ?? 0));
        var c1Values = samples.Where(s => s.C1.HasValue).Select(s => s.C1!.Value).ToList();
        // Statistics only cover samples with non-null C1 (design: Evaluator). With no C1 sample
        // there is no mean/max, so no C1 threshold can be crossed (a 0 default would wrongly trip
        // a 0 threshold, which was the cause of the all-zero counterexample).
        bool hasC1 = c1Values.Count > 0;
        double mean = hasC1 ? c1Values.Average() : 0;
        double max = hasC1 ? c1Values.Max() : 0;

        bool noErrors = c2Tot == 0 && cuTot == 0;
        bool meanWarnHit = hasC1 && mean >= thresholds.C1MeanWarn;
        bool maxWarnHit = hasC1 && max >= thresholds.C1MaxWarn;
        bool maxKoHit = hasC1 && max >= thresholds.C1MaxKo;
        bool isOk = noErrors && !meanWarnHit && !maxWarnHit;
        bool isWarn = noErrors && (meanWarnHit || maxWarnHit) && !maxKoHit;
        var expected = isOk ? Verdict.OK : isWarn ? Verdict.WARN : Verdict.KO;

        var first = Evaluator.Evaluate(samples, ScannerKind.LiteOn, thresholds);
        var second = Evaluator.Evaluate(samples, ScannerKind.LiteOn, thresholds);
        return first == expected && second == first;
    }

    private static int Pick(byte[] seed, int i) => seed.Length == 0 ? 0 : seed[i % seed.Length];
}
