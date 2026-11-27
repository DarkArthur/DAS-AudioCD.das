using DiscCheck.Core;
using Xunit;

namespace DiscCheck.Core.Tests;

public class DegradationMonitorSmokeTests
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class FakeScans(params ScanRecord[] items) : IScanRepository
    {
        public long Save(ScanRecord scan, IReadOnlyList<ScanSample> samples) => throw new NotSupportedException();
        public ScanRecord? Get(long id) => items.FirstOrDefault(s => s.Id == id);
        public IReadOnlyList<ScanSample> GetSamples(long scanId) => [];
        public IReadOnlyList<ScanRecord> List() => items;
        public IReadOnlyList<ScanRecord> ListByDisc(long discId) => items.Where(s => s.DiscId == discId).ToList();
        public bool Delete(long id) => false;
    }

    private sealed class FakeDiscs(params DiscRecord[] items) : IDiscRepository
    {
        public List<DiscRecord> Items { get; } = [.. items];
        public long Add(DiscRecord disc) => throw new NotSupportedException();
        public void Update(DiscRecord disc) => Items[Items.FindIndex(d => d.Id == disc.Id)] = disc;
        public DiscRecord? Get(long id) => Items.FirstOrDefault(d => d.Id == id);
        public DiscRecord? FindByTocHash(string tocHash) => Items.FirstOrDefault(d => d.TocHash == tocHash);
        public IReadOnlyList<DiscRecord> List() => Items;
    }

    private static DiscRecord Disc(long id, string? due = null) => new(id, null, null, null, null, null, null, $"h{id}", 1, 1, null, due);

    private static ScanRecord Scan(long id, long disc, string date, Verdict v) =>
        new(id, disc, 1, DateTimeOffset.Parse(date), 4, ScannerKind.StandardC2, v, 10, 1.5, 7, 3, 2, null);

    [Fact]
    public void Trend_is_ascending_and_flags_worsening()
    {
        var m = new DegradationMonitor(new FakeScans(Scan(2, 1, "2024-01-01T00:00:00Z", Verdict.WARN), Scan(1, 1, "2023-01-01T00:00:00Z", Verdict.OK)),
                                       new FakeDiscs(Disc(1)));
        var t = m.GetTrend(1);
        Assert.Equal([1L, 2L], t.Points.Select(p => p.ScanId));
        Assert.True(t.IsWorsening);
        Assert.False(m.GetTrend(99).HasScans);
    }

    [Fact]
    public void RecheckDue_set_only_for_scanned_disc_and_null_without_scans()
    {
        var discs = new FakeDiscs(Disc(1), Disc(2, "2030-01-01"));
        var m = new DegradationMonitor(new FakeScans(Scan(1, 1, "2024-02-29T10:00:00Z", Verdict.OK)), discs);
        Assert.Equal("2029-02-28", m.UpdateRecheckDue(1, 5));
        Assert.Equal("2029-02-28", discs.Get(1)!.RecheckDue);
        Assert.Equal("2030-01-01", discs.Get(2)!.RecheckDue);
        Assert.Null(m.UpdateRecheckDue(2));
    }

    [Fact]
    public void DueList_includes_today_and_90_days_and_is_sorted()
    {
        var discs = new FakeDiscs(Disc(1, "2025-04-01"), Disc(2, "2025-01-01"), Disc(3, "2025-04-02"), Disc(4), Disc(5, "2024-12-01"));
        var m = new DegradationMonitor(new FakeScans(), discs, new FixedClock(DateTimeOffset.Parse("2025-01-01T12:00:00Z")));
        var due = m.GetDueDiscs();
        Assert.Equal([5L, 2L, 1L], due.Select(e => e.Disc.Id));
        Assert.Equal([RecheckStatus.Overdue, RecheckStatus.Overdue, RecheckStatus.DueSoon], due.Select(e => e.Status));
    }
}
