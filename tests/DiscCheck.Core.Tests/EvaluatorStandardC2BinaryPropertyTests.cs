using DiscCheck.Core;
using FsCheck.Xunit;

namespace DiscCheck.Core.Tests;

public class EvaluatorStandardC2BinaryPropertyTests
{
    // Feature: disccheck, Property 5: Il Verdetto di StandardC2Scanner è binario
    // **Validates: Requirements 11.6**
    [Property(MaxTest = 200)]
    public bool StandardC2Verdict_IsOnlyOkOrKo_AndDependsOnlyOnC2AndCu(
        byte[] c1Seed, byte[] c2Seed, byte[] cuSeed, byte meanSeed, byte warnSeed, byte koSeed)
    {
        int n = new[] { c1Seed.Length, c2Seed.Length, cuSeed.Length }.Max();
        if (n == 0) return true; // empty input has no verdict (Req. 11.10)

        var thresholds = new Thresholds(meanSeed % 20 / 2.0, warnSeed, warnSeed + koSeed);

        var samples = new List<ScanSample>();
        for (int i = 0; i < n; i++)
        {
            // C1 may be anything (even above thresholds): it must be ignored.
            int? c1 = Pick(c1Seed, i) % 5 == 0 ? null : Pick(c1Seed, i) * 3;
            int c2 = Pick(c2Seed, i) % 8 == 0 ? Pick(c2Seed, i) / 8 % 3 : 0;
            int cu = Pick(cuSeed, i) % 8 == 0 ? Pick(cuSeed, i) / 8 % 3 : 0;
            samples.Add(new ScanSample(i * 75, i, c1, c2, cu, 4.0));
        }

        bool noErrors = samples.All(s => s.C2 == 0 && s.CU == 0);
        var expected = noErrors ? Verdict.OK : Verdict.KO;

        var verdict = Evaluator.Evaluate(samples, ScannerKind.StandardC2, thresholds);
        return verdict is Verdict.OK or Verdict.KO && verdict == expected;
    }

    private static int Pick(byte[] seed, int i) => seed.Length == 0 ? 0 : seed[i % seed.Length];
}
