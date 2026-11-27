using DiscCheck.Core;
using Xunit;

namespace DiscCheck.Core.Tests;

/// <summary>Unit tests of <see cref="DegradationMonitor"/> (Requirements 20.5, 20.7, 20.10, 20.11).</summary>
public class DegradationMonitorTests
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

    private static DegradationMonitor Monitor(ScanRecord[] scans, DiscRecord[] discs, string today = "2025-01-01T12:00:00Z") =>
        new(new FakeScans(scans), new FakeDiscs(discs), new FixedClock(DateTimeOffset.Parse(today)));

    [Fact]
    public void SingleScan_gives_one_point_and_is_not_worsening()
    {
        var m = Monitor([Scan(1, 1, "2024-03-01T00:00:00Z", Verdict.KO)], [Disc(1)]);
        var t = m.GetTrend(1);
        Assert.True(t.HasScans);
        Assert.Single(t.Points);
        Assert.False(t.IsWorsening);
    }

    [Fact]
    public void NoScans_gives_empty_trend_and_null_recheck_due()
    {
        var discs = new FakeDiscs(Disc(1, "2030-01-01"));
        var m = new DegradationMonitor(new FakeScans(), discs, new FixedClock(DateTimeOffset.Parse("2025-01-01T00:00:00Z")));
        var t = m.GetTrend(1);
        Assert.False(t.HasScans);
        Assert.Empty(t.Points);
        Assert.False(t.IsWorsening);
        Assert.Null(m.UpdateRecheckDue(1));
        Assert.Null(discs.Get(1)!.RecheckDue);
    }

    [Fact]
    public void Worsening_OK_to_WARN_to_KO()
    {
        var scans = new[]
        {
            Scan(3, 1, "2024-03-01T00:00:00Z", Verdict.KO),
            Scan(1, 1, "2022-03-01T00:00:00Z", Verdict.OK),
            Scan(2, 1, "2023-03-01T00:00:00Z", Verdict.WARN),
        };
        var m = Monitor(scans, [Disc(1)]);
        var t = m.GetTrend(1);
        Assert.Equal([1L, 2L, 3L], t.Points.Select(p => p.ScanId));
        Assert.Equal([Verdict.OK, Verdict.WARN, Verdict.KO], t.Points.Select(p => p.Verdict));
        Assert.True(t.IsWorsening);

        // Each step considered on its own.
        Assert.True(Monitor(scans.Where(s => s.Id != 3).ToArray(), [Disc(1)]).GetTrend(1).IsWorsening);
        Assert.True(Monitor(scans.Where(s => s.Id != 2).ToArray(), [Disc(1)]).GetTrend(1).IsWorsening);
    }

    [Fact]
    public void Same_or_improving_verdict_is_not_worsening()
    {
        Assert.False(Monitor([Scan(1, 1, "2023-01-01T00:00:00Z", Verdict.WARN), Scan(2, 1, "2024-01-01T00:00:00Z", Verdict.WARN)], [Disc(1)]).GetTrend(1).IsWorsening);
        Assert.False(Monitor([Scan(1, 1, "2023-01-01T00:00:00Z", Verdict.KO), Scan(2, 1, "2024-01-01T00:00:00Z", Verdict.OK)], [Disc(1)]).GetTrend(1).IsWorsening);
    }

    [Fact]
    public void Trend_ignores_scans_of_other_discs()
    {
        var m = Monitor([Scan(1, 1, "2023-01-01T00:00:00Z", Verdict.OK), Scan(2, 2, "2024-01-01T00:00:00Z", Verdict.KO)], [Disc(1), Disc(2)]);
        Assert.Single(m.GetTrend(1).Points);
        Assert.False(m.GetTrend(1).IsWorsening);
    }

    [Fact]
    public void Boundary_today_is_overdue_and_tomorrow_is_due_soon()
    {
        var m = Monitor([], [Disc(1, "2025-01-01"), Disc(2, "2025-01-02"), Disc(3, "2024-12-31")]);
        var due = m.GetDueDiscs();
        Assert.Equal([3L, 1L, 2L], due.Select(e => e.Disc.Id));
        Assert.Equal([RecheckStatus.Overdue, RecheckStatus.Overdue, RecheckStatus.DueSoon], due.Select(e => e.Status));
    }

    [Fact]
    public void Boundary_90_days_included_91_excluded()
    {
        // 2025-01-01 + 90 days = 2025-04-01
        var m = Monitor([], [Disc(1, "2025-04-01"), Disc(2, "2025-04-02")]);
        var due = m.GetDueDiscs();
        Assert.Equal([1L], due.Select(e => e.Disc.Id));
        Assert.Equal(RecheckStatus.DueSoon, due[0].Status);
    }

    [Fact]
    public void Discs_without_or_with_invalid_recheck_due_are_skipped()
    {
        var m = Monitor([], [Disc(1), Disc(2, "not-a-date")]);
        Assert.Empty(m.GetDueDiscs());
    }

    [Fact]
    public void RecheckDue_uses_latest_scan_and_configured_years()
    {
        var discs = new FakeDiscs(Disc(1));
        var m = new DegradationMonitor(
            new FakeScans(Scan(1, 1, "2020-06-15T10:00:00Z", Verdict.OK), Scan(2, 1, "2024-06-15T10:00:00Z", Verdict.OK)),
            discs, new FixedClock(DateTimeOffset.Parse("2025-01-01T00:00:00Z")));
        Assert.Equal("2027-06-15", m.UpdateRecheckDue(1, 3));
        Assert.Equal("2027-06-15", discs.Get(1)!.RecheckDue);
    }
}
