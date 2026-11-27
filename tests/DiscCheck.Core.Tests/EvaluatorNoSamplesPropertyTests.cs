using DiscCheck.Core;
using FsCheck.Xunit;

namespace DiscCheck.Core.Tests;

public class EvaluatorNoSamplesPropertyTests
{
    // Feature: disccheck, Property 6: Nessun Verdetto senza campioni
    // **Validates: Requirements 11.10**
    [Property(MaxTest = 200)]
    public bool Evaluate_WithEmptySamples_ReturnsNull(
        bool useLiteOn, byte meanSeed, byte warnSeed, byte koSeed)
    {
        // Valid thresholds: 0 <= C1MeanWarn, C1MaxWarn <= C1MaxKo.
        var thresholds = new Thresholds(meanSeed % 20 / 2.0, warnSeed, warnSeed + koSeed);
        var kind = useLiteOn ? ScannerKind.LiteOn : ScannerKind.StandardC2;

        return Evaluator.Evaluate(new List<ScanSample>(), kind, thresholds) is null;
    }
}
