using DiscCheck.Core;
using FsCheck.Xunit;

namespace DiscCheck.Core.Tests;

public class ChartDownsamplerTests
{
    private static ScanSample S(int t, int? c1, int c2, int cu, double? sp = null) => new(t * 10, t, c1, c2, cu, sp);

    [Fact]
    public void Series_not_longer_than_width_is_unchanged()
    {
        var samples = new[] { S(0, 1, 0, 0), S(1, 2, 3, 4) };
        Assert.Same(samples, ChartDownsampler.Downsample(samples, 2));
    }

    [Fact]
    public void Peak_is_preserved_and_nulls_handled()
    {
        var samples = new[] { S(0, null, 0, 0), S(1, null, 9, 0), S(2, 5, 0, 7, 8), S(3, 1, 0, 0, 4) };
        var r = ChartDownsampler.Downsample(samples, 2);
        Assert.Equal(2, r.Count);
        Assert.Equal(new ScanSample(0, 0, null, 9, 0, null), r[0]);
        Assert.Equal(new ScanSample(20, 2, 5, 0, 7, 8), r[1]);
    }

    [Fact]
    public void Non_positive_width_throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => ChartDownsampler.Downsample([], 0));

    // Feature: disccheck, Property 21: Downsampling del grafico preserva i massimi
    // **Validates: Requirements 18.6**
    [Property(MaxTest = 100)]
    public bool Downsampling_bounds_count_and_preserves_maxima(byte[] values, byte w)
    {
        int width = (w % 50) + 1;
        var samples = values.Select((v, i) => S(i, v % 100, v, v % 7)).ToList();
        var r = ChartDownsampler.Downsample(samples, width);

        if (r.Count > Math.Max(width, 0) && samples.Count > width) return false;
        if (samples.Count <= width) return r.Count == samples.Count;

        int size = (samples.Count + width - 1) / width;
        for (int b = 0; b < r.Count; b++)
        {
            var chunk = samples.Skip(b * size).Take(size).ToList();
            if (r[b].C2 != chunk.Max(x => x.C2) || r[b].CU != chunk.Max(x => x.CU) || r[b].C1 != chunk.Max(x => x.C1))
                return false;
        }
        return true;
    }
}
