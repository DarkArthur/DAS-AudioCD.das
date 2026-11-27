using DiscCheck.Core;
using FluentAssertions;

namespace DiscCheck.Core.Tests;

public class ScanSpeedAndSummaryTests
{
    [Fact]
    public void Nearest_PicksClosestSupportedSpeed()
    {
        int[] supported = [ScanSpeeds.ToKBps(4), ScanSpeeds.ToKBps(10), ScanSpeeds.ToKBps(24)];
        ScanSpeeds.Nearest(supported, ScanSpeeds.ToKBps(16)).Should().Be(ScanSpeeds.ToKBps(10)); // 6x vs 8x away
        ScanSpeeds.Nearest(supported, ScanSpeeds.ToKBps(8)).Should().Be(ScanSpeeds.ToKBps(10));
    }

    [Fact]
    public void Nearest_OnTie_PicksTheLowerSpeed()
    {
        int[] supported = [ScanSpeeds.ToKBps(4), ScanSpeeds.ToKBps(12)];
        ScanSpeeds.Nearest(supported, ScanSpeeds.ToKBps(8)).Should().Be(ScanSpeeds.ToKBps(4));
        ScanSpeeds.Nearest([], 1408).Should().BeNull();
    }

    [Fact]
    public void IsSupported_EmptyListAcceptsEverything()
    {
        ScanSpeeds.IsSupported([], 1408).Should().BeTrue();
        ScanSpeeds.IsSupported([704], 1408).Should().BeFalse();
    }

    [Fact]
    public void Summary_StandardScanner_HasNoC1()
    {
        var s = ScanSummary.Compute([new(0, 0, null, 3, 0, 8.0), new(75, 1, null, 5, 0, 8.0)]);
        s.C1Total.Should().BeNull();
        s.C1Mean.Should().BeNull();
        s.C1Max.Should().BeNull();
        s.C2Total.Should().Be(8);
        s.C2Max.Should().Be(5);
    }

    [Fact]
    public void Summary_C1MeanIgnoresNullSamples()
    {
        var s = ScanSummary.Compute([new(0, 0, 10, 0, 0, null), new(75, 1, null, 0, 0, null), new(150, 2, 20, 0, 2, null)]);
        s.C1Total.Should().Be(30);
        s.C1Mean.Should().Be(15);
        s.C1Max.Should().Be(20);
        s.CuTotal.Should().Be(2);
    }

    [Fact]
    public void Summary_CuIsNotAvailableWhenNoSampleMeasuresIt()
    {
        var s = ScanSummary.Compute([new(0, 0, 4, 1, null, 8.0), new(75, 1, 6, 0, null, 8.0)]);
        s.CuTotal.Should().BeNull();
        s.C2Total.Should().Be(1);
    }

    [Fact]
    public void Summary_CuSumsOnlyMeasuredSamples()
    {
        var s = ScanSummary.Compute([new(0, 0, 4, 0, null, 8.0), new(75, 1, 6, 0, 3, 8.0)]);
        s.CuTotal.Should().Be(3);
    }

    [Fact]
    public void Evaluator_IgnoresUnmeasuredCu()
    {
        var samples = new ScanSample[] { new(0, 0, 0, 0, null, 8.0), new(75, 1, 0, 0, null, 8.0) };
        Evaluator.ComputeStatistics(samples).CuTotal.Should().Be(0);
        Evaluator.Evaluate(samples, ScannerKind.LiteOn, new Thresholds()).Should().Be(Verdict.OK);
    }
}
