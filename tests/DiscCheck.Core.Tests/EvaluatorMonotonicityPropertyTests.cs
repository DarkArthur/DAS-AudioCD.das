using DiscCheck.Core;
using FsCheck.Xunit;

namespace DiscCheck.Core.Tests;

public class EvaluatorMonotonicityPropertyTests
{
    private static readonly Thresholds Defaults = new();

    // Feature: disccheck, Property 3: Per ogni coppia di insiemi di Scan_Sample A e B, dove B è ottenuto da A aggiungendo o incrementando errori C2 o CU su uno o più campioni (lasciando invariati gli altri valori), il Verdetto calcolato da B non è migliore del Verdetto calcolato da A, nell'ordine OK < WARN < KO.
    // **Validates: Requirements 11.8**
    [Property(MaxTest = 200)]
    public bool Verdict_IsMonotonicWithRespectToC2AndCuErrors(
        byte[] c1Seed, byte[] c2Seed, byte[] cuSeed, byte[] addC2, byte[] addCu, bool liteOn)
    {
        int n = new[] { c1Seed.Length, c2Seed.Length, cuSeed.Length }.Max();
        if (n == 0) return true; // empty set yields no verdict (Req. 11.10), covered elsewhere

        var kind = liteOn ? ScannerKind.LiteOn : ScannerKind.StandardC2;
        var a = new List<ScanSample>();
        var b = new List<ScanSample>();
        for (int i = 0; i < n; i++)
        {
            int? c1 = liteOn ? Pick(c1Seed, i) * 2 : null;       // 0..510, crosses the 50/220 thresholds
            int c2 = Pick(c2Seed, i) % 4 == 0 ? Pick(c2Seed, i) / 4 : 0; // mostly zero
            int cu = Pick(cuSeed, i) % 4 == 0 ? Pick(cuSeed, i) / 4 : 0;
            a.Add(new ScanSample(i * 75, i, c1, c2, cu, 4.0));
            // Increase C2/CU on some samples; every other field is left unchanged.
            b.Add(new ScanSample(i * 75, i, c1, c2 + Pick(addC2, i) % 3 * (Pick(addC2, i) / 3 % 2), cu + Pick(addCu, i) % 3 * (Pick(addCu, i) / 3 % 2), 4.0));
        }

        var va = Evaluator.Evaluate(a, kind, Defaults)!.Value;
        var vb = Evaluator.Evaluate(b, kind, Defaults)!.Value;
        return vb >= va;
    }

    private static int Pick(byte[] seed, int i) => seed.Length == 0 ? 0 : seed[i % seed.Length];
}
