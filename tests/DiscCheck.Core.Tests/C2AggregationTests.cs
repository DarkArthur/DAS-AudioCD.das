using DiscCheck.Core;
using FluentAssertions;
using Xunit;

namespace DiscCheck.Core.Tests;

public class C2AggregationTests
{
    [Fact]
    public void CountC2_CountsSetBits()
    {
        var p = new byte[294];
        p[0] = 0xFF; p[293] = 0x05;
        C2BitCounter.CountC2(p).Should().Be(10);
    }

    [Fact]
    public void CountC2_WrongLength_Throws()
    {
        var act = () => C2BitCounter.CountC2(new byte[10]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Format14_ExcludesBlockErrorAndPaddingBytes()
    {
        var area = new byte[296];
        area[0] = 0xFF; area[1] = 0xFF; area[2] = 0x01;
        C2BitCounter.CountC2FromC2Area(area).Should().Be(1);
        C2BitCounter.CountC2FromC2Area(area.AsSpan(2)).Should().Be(1);
    }

    [Fact]
    public void Aggregator_EmitsEvery75SectorsAndFlushesResidual()
    {
        var agg = new SampleAggregator();
        var samples = new List<ScanSample>();
        for (int lba = 0; lba < 160; lba++)
        {
            var s = agg.AddSector(lba, 1, 1.0 / 75);
            if (s is not null) samples.Add(s);
        }
        var residual = agg.Flush();
        samples.Should().HaveCount(2);
        samples[0].Should().Match<ScanSample>(s => s.TimeSeconds == 0 && s.C2 == 75 && s.C1 == null && s.CU == 0 && s.Lba == 0);
        samples[1].TimeSeconds.Should().Be(1);
        residual.Should().NotBeNull();
        residual!.C2.Should().Be(10);
        residual.TimeSeconds.Should().Be(2);
        agg.Flush().Should().BeNull();
    }

    [Fact]
    public void SpeedX_IsSectorsOver75OverSeconds()
    {
        // 75 sectors in 0.125 s => 8x
        var agg = new SampleAggregator();
        var list = agg.AddBlock(0, new int[75], 0.125);
        list.Should().ContainSingle().Which.SpeedX.Should().BeApproximately(8.0, 0.01);
    }
}
