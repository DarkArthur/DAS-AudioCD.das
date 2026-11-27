using FluentAssertions;

namespace DiscCheck.Core.Tests;

public class OffsetDetectorTests
{
    private static short[] Noise(int frames, int seed)
    {
        var rnd = new Random(seed);
        var s = new short[frames * 2];
        for (int i = 0; i < s.Length; i++) s[i] = (short)rnd.Next(short.MinValue, short.MaxValue);
        return s;
    }

    /// <summary>drive[i] = reference[i + k]; frames outside the reference are zero.</summary>
    private static short[] Shift(short[] reference, int k)
    {
        int n = reference.Length / 2;
        var d = new short[reference.Length];
        for (int i = 0; i < n; i++)
        {
            int j = i + k;
            if (j < 0 || j >= n) continue;
            d[i * 2] = reference[j * 2];
            d[i * 2 + 1] = reference[j * 2 + 1];
        }
        return d;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-30)]
    [InlineData(5000)]
    [InlineData(-5000)]
    public void Detect_ReturnsAppliedOffset(int k)
    {
        var reference = Noise(OffsetDetector.MinTrackFrames, 1);
        var result = OffsetDetector.Detect(reference, Shift(reference, k));
        result.IsFound.Should().BeTrue();
        result.Offset.Should().Be(k);
    }

    [Fact]
    public void Detect_TrackTooShort()
    {
        var reference = Noise(OffsetDetector.MinTrackFrames - 1, 2);
        OffsetDetector.Detect(reference, reference).Status.Should().Be(OffsetDetectionStatus.TrackTooShort);
    }

    [Fact]
    public void Detect_NotFound_WhenUnrelated()
    {
        var r = OffsetDetector.Detect(Noise(60_000, 3), Noise(60_000, 4));
        r.Status.Should().Be(OffsetDetectionStatus.NotFound);
        r.Offset.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(-7)]
    public void Compare_ExcludesAbsOffsetFrames(int k)
    {
        var reference = Noise(1000, 5);
        var c = PcmComparer.Compare(reference, Shift(reference, k), k);
        c.ExcludedFrames.Should().Be(Math.Abs(k));
        c.ComparedFrames.Should().Be(1000 - Math.Abs(k));
        c.IsIdentical.Should().BeTrue();
    }

    [Fact]
    public void Compare_ReportsFirstDifference()
    {
        var reference = Noise(100, 6);
        var drive = (short[])reference.Clone();
        drive[41 * 2 + 1] ^= 1;
        drive[60 * 2] ^= 1;
        var c = PcmComparer.Compare(reference, drive, 0);
        c.DifferentFrames.Should().Be(2);
        c.FirstDifferentFrame.Should().Be(41);
    }
}
