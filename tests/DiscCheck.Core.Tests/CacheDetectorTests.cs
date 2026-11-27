using FluentAssertions;

namespace DiscCheck.Core.Tests;

public class CacheDetectorTests
{
    private static Toc Disc(int sectors = 40_000) =>
        new([new TocEntry(1, 0, true)], sectors);

    /// <summary>Simulated drive: an LRU cache of the given size (KB) in sectors; hit = 1 ms, miss = 10 ms.</summary>
    private sealed class FakeDrive(int cacheKb, int? failAtLba = null)
    {
        private readonly List<int> _cached = [];
        public double Now;
        public int Capacity = cacheKb == 0 ? 0 : CacheDetector.SectorsForKb(cacheKb) + CacheDetector.BlockSectors;

        public CacheReadResult Read(int lba, int count)
        {
            if (failAtLba is { } f && lba <= f && f < lba + count) return CacheReadResult.Fail("status CHECK CONDITION, Sense Key 0x3");
            bool hit = true;
            for (int i = 0; i < count; i++)
            {
                if (!_cached.Remove(lba + i)) hit = false;
                _cached.Add(lba + i);
            }

            while (_cached.Count > Capacity) _cached.RemoveAt(0);
            Now += hit ? 1 : 10;
            return CacheReadResult.Ok;
        }
    }

    [Theory]
    [InlineData(2048, true)]
    [InlineData(0, false)]
    public void Detect_ClassifiesSimulatedDrive(int cacheKb, bool caches)
    {
        var d = new FakeDrive(cacheKb);
        var r = CacheDetector.Detect(Disc(), d.Read, () => d.Now);
        r.Completed.Should().BeTrue();
        r.CachesAudio.Should().Be(caches);
        r.CacheKb.Should().Be(caches ? 2048 : 0);
    }

    [Fact]
    public void Detect_ReadFailure_AbortsWithLbaAndNoResult()
    {
        var d = new FakeDrive(2048, failAtLba: 20_000);
        var r = CacheDetector.Detect(Disc(), d.Read, () => d.Now);
        r.Completed.Should().BeFalse();
        r.CachesAudio.Should().BeNull();
        r.CacheKb.Should().BeNull();
        r.AbortReason.Should().Contain("LBA").And.Contain("Sense Key");
    }

    [Fact]
    public void Detect_ShortDisc_Aborts()
    {
        var d = new FakeDrive(0);
        CacheDetector.Detect(Disc(28_499), d.Read, () => d.Now).Completed.Should().BeFalse();
    }

    [Theory]
    [InlineData(1024, true)]
    [InlineData(2048, false)]
    [InlineData(1536, false)]
    [InlineData(2560, false)]
    [InlineData(4096, true)]
    public void Ihas124Warning_OnlyOutsideRange(int kb, bool warns) =>
        (CacheDetector.Ihas124Warning(kb) is not null).Should().Be(warns);

    [Theory]
    [InlineData(10, 5, true)]
    [InlineData(10, 5.1, false)]
    public void ClassifyFromMedians_UsesFiftyPercentThreshold(double t1, double t2, bool cached) =>
        CacheDetector.ClassifyFromMedians(t1, t2).Should().Be(cached);
}
