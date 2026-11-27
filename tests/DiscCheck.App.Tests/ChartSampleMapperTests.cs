using DAS.Design.Charts;
using DiscCheck.App.Charts;
using DiscCheck.Core;
using FluentAssertions;
namespace DiscCheck.App.Tests;

public class ChartSampleMapperTests
{
    [Fact]
    public void ToChartSample_PreservesNulls()
    {
        var c = ChartSampleMapper.ToChartSample(new ScanSample(10, 3, null, 2, 1, null));
        c.Should().Be(new ChartSample(3, null, 2, 1, null));
    }

    [Theory]
    [InlineData(ScannerKind.LiteOn, true)]
    [InlineData(ScannerKind.StandardC2, false)]
    public void ToThresholdSettings_MapsScannerAndValues(ScannerKind kind, bool liteOn)
    {
        var t = ChartSampleMapper.ToThresholdSettings(new Thresholds(1, 40, 200), kind, true);
        t.Should().Be(new ThresholdSettings(40, 200, liteOn, true));
    }

    [Fact]
    public void ToVisibleChartSamples_DownsamplesKeepingPeaks()
    {
        var samples = Enumerable.Range(0, 1000).Select(i => new ScanSample(i, i, 1, i == 500 ? 9 : 0, 0, 8.0)).ToList();
        var r = ChartSampleMapper.ToVisibleChartSamples(samples, 100);
        r.Count.Should().BeLessThanOrEqualTo(100);
        r.Max(s => s.C2).Should().Be(9);
    }

    [Fact]
    public void ToVisibleChartSamples_RestrictsToVisibleRangeWithNeighbours()
    {
        var samples = Enumerable.Range(0, 100).Select(i => new ScanSample(i, i, 1, 0, 0, null)).ToList();
        var r = ChartSampleMapper.ToVisibleChartSamples(samples, 1000, 10, 20);
        r.First().TimeSeconds.Should().Be(9);
        r.Last().TimeSeconds.Should().Be(21);
    }

    [Fact]
    public void ToVisibleChartSamples_EmptyInput_ReturnsEmpty() =>
        ChartSampleMapper.ToVisibleChartSamples([], 100).Should().BeEmpty();
}
