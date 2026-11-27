using DiscCheck.Core;
using FluentAssertions;

namespace DiscCheck.Core.Tests;

/// <summary>C2ReliabilityAnalyzer: Reliable, Unreliable(n), Inconclusive (Requirements 13.3, 13.4, 13.6).</summary>
public class C2ReliabilityAnalyzerTests
{
    private const int Stride = C2ReliabilityAnalyzer.SectorStride;

    private static byte[] Reading(int sectors) => new byte[sectors * Stride];

    private static void Differ(byte[] second, int sector) => second[sector * Stride + 10] = 0x7F;

    private static void FlagC2(byte[] reading, int sector, int bit = 0) =>
        reading[sector * Stride + C2ReliabilityAnalyzer.UserBytes + bit / 8] |= (byte)(1 << (bit % 8));

    [Fact]
    public void Analyze_IdenticalReadings_IsInconclusiveAndSavesNothing()
    {
        var a = Reading(4);
        var outcome = C2ReliabilityAnalyzer.Analyze(a, (byte[])a.Clone(), 4);

        outcome.Kind.Should().Be(C2ReliabilityKind.Inconclusive);
        outcome.DifferingSectors.Should().Be(0);
        outcome.ReliableValue.Should().BeNull();
    }

    [Fact]
    public void Analyze_C2OnIdenticalSectorsOnly_IsStillInconclusive()
    {
        var a = Reading(4);
        FlagC2(a, 1);

        C2ReliabilityAnalyzer.Analyze(a, Reading(4), 4).Kind.Should().Be(C2ReliabilityKind.Inconclusive);
    }

    [Fact]
    public void Analyze_EveryDifferingSectorFlaggedInEitherRead_IsReliable()
    {
        var a = Reading(5);
        var b = Reading(5);
        Differ(b, 1); FlagC2(a, 1);   // flagged in first read only
        Differ(b, 3); FlagC2(b, 3, 2000); // flagged in second read only

        var outcome = C2ReliabilityAnalyzer.Analyze(a, b, 5);

        outcome.Kind.Should().Be(C2ReliabilityKind.Reliable);
        outcome.DifferingSectors.Should().Be(2);
        outcome.UncoveredSectors.Should().Be(0);
        outcome.ReliableValue.Should().BeTrue();
    }

    [Fact]
    public void Analyze_DifferingSectorsWithoutC2_IsUnreliableWithCount()
    {
        var a = Reading(6);
        var b = Reading(6);
        Differ(b, 0); FlagC2(a, 0);
        Differ(b, 2);
        Differ(b, 4);
        Differ(b, 5);

        var outcome = C2ReliabilityAnalyzer.Analyze(a, b, 6);

        outcome.Kind.Should().Be(C2ReliabilityKind.Unreliable);
        outcome.DifferingSectors.Should().Be(4);
        outcome.UncoveredSectors.Should().Be(3);
        outcome.ReliableValue.Should().BeFalse();
    }

    [Fact]
    public void Analyze_ShortReading_Throws()
    {
        var act = () => C2ReliabilityAnalyzer.Analyze(Reading(2), Reading(3), 3);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Analyze_UncoveredNeverExceedsDiffering_ForAnyPattern()
    {
        var rnd = new Random(42);
        for (int iter = 0; iter < 50; iter++)
        {
            int n = rnd.Next(1, 12);
            var a = Reading(n);
            var b = Reading(n);
            for (int s = 0; s < n; s++)
            {
                if (rnd.Next(2) == 0) Differ(b, s);
                if (rnd.Next(2) == 0) FlagC2(a, s);
                if (rnd.Next(2) == 0) FlagC2(b, s, 5);
            }

            var o = C2ReliabilityAnalyzer.Analyze(a, b, n);
            o.UncoveredSectors.Should().BeLessThanOrEqualTo(o.DifferingSectors);
            (o.Kind == C2ReliabilityKind.Reliable).Should().Be(o.DifferingSectors > 0 && o.UncoveredSectors == 0);
        }
    }
}
