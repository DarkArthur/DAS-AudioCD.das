using DAS.Design.Charts;
using DiscCheck.App.Charts;
using DiscCheck.App.ViewModels;
using DiscCheck.Core;
using DiscCheck.Data;
using FluentAssertions;

namespace DiscCheck.App.Tests;

public sealed class DegradationViewModelTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"degr-{Guid.NewGuid():N}.db");
    private readonly ArchiveOpenResult _open;

    public DegradationViewModelTests()
    {
        _open = ArchiveOpener.Open(_path);
        _open.Error.Should().BeNull();
    }

    public void Dispose()
    {
        _open.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch (IOException) { }
    }

    private long Save(string hash, DateTimeOffset at, Verdict verdict, int? years = 5)
    {
        var drive = new DriveRecord(0, "LITE-ON", "iHAS124", "CL9J");
        var disc = new DiscRecord(0, hash, null, null, null, null, null, hash, 3, 1000, null, null);
        var scan = new ScanRecord(0, 0, 0, at, 8, ScannerKind.StandardC2, verdict, 10, 1.5, 7, 4, 2, null);
        ScanSample[] samples = [new(0, 0, null, 1, 0, 8)];
        return _open.Archive.SaveScan(drive, disc, scan, samples, years);
    }

    [Fact]
    public async Task Trend_has_three_trend_charts_with_date_axis_and_worsening_is_flagged()
    {
        var now = DateTimeOffset.Now;
        Save("A", now.AddDays(-20), Verdict.OK);
        Save("A", now.AddDays(-10), Verdict.KO);
        var vm = new DegradationViewModel(_open.Archive);
        await vm.LoadAsync();

        vm.Discs.Should().ContainSingle().Which.IsWorsening.Should().BeTrue();
        await vm.ShowTrendAsync(vm.Discs[0]);

        vm.IsWorsening.Should().BeTrue();
        vm.Charts.Should().HaveCount(3);
        vm.Charts.Should().OnlyContain(c => c.Axis.XFormat == ChartXAxisFormat.Date && c.Axis.XLabel == "Data");
        vm.Charts.Should().OnlyContain(c => c.Series.Single().Kind == ChartSeriesKind.Trend
                                            && c.Series.Single().Samples.Count == 2);
        vm.Charts[0].Axis.YLabel.Should().Contain("C1 medio").And.Contain("errori/s");
    }

    [Fact]
    public async Task Single_scan_shows_one_point_and_no_worsening()
    {
        Save("A", DateTimeOffset.Now.AddDays(-1), Verdict.KO);
        var vm = new DegradationViewModel(_open.Archive);
        await vm.LoadAsync();
        await vm.ShowTrendAsync(vm.Discs[0]);

        vm.IsWorsening.Should().BeFalse();
        vm.Charts.Should().OnlyContain(c => c.Series.Single().Samples.Count == 1);
    }

    [Fact]
    public async Task Overdue_and_due_soon_are_listed_and_reminder_shows_once_per_launch()
    {
        var now = DateTimeOffset.Now;
        Save("OLD", now.AddYears(-6), Verdict.OK);          // due a year ago: overdue
        Save("SOON", now.AddYears(-5).AddDays(30), Verdict.OK); // due in 30 days
        Save("FAR", now, Verdict.OK);                        // not due
        var vm = new DegradationViewModel(_open.Archive);
        await vm.LoadAsync();

        vm.DueDiscs.Select(d => d.StatusText).Should().Equal("scaduto", "in scadenza");

        await vm.CheckReminderAsync();
        vm.HasReminder.Should().BeTrue();
        vm.ReminderText.Should().Contain("1 disco");

        vm.DismissReminder();
        await vm.CheckReminderAsync();
        vm.HasReminder.Should().BeFalse("the reminder is shown once per launch");
    }

    [Fact]
    public void Saving_a_scan_sets_recheck_due_from_the_configured_years_only_for_that_disc()
    {
        var at = new DateTimeOffset(2024, 3, 10, 12, 0, 0, TimeSpan.Zero);
        Save("A", at, Verdict.OK, years: 7);
        Save("B", at, Verdict.OK, years: null);

        var discs = _open.Archive.Execute(r => r.Discs.List());
        discs.Single(d => d.TocHash == "A").RecheckDue.Should().NotBeNull().And.StartWith("203");
        discs.Single(d => d.TocHash == "B").RecheckDue.Should().BeNull();
    }

    [Fact]
    public async Task Load_failure_shows_the_error_and_no_partial_data()
    {
        Save("A", DateTimeOffset.Now, Verdict.OK);
        var vm = new DegradationViewModel(_open.Archive);
        await vm.LoadAsync();
        vm.Discs.Should().NotBeEmpty();

        var vm2 = new DegradationViewModel(new ThrowingArchive());
        await vm2.LoadAsync();

        vm2.HasLoadError.Should().BeTrue();
        vm2.LoadError.Should().Contain("boom");
        vm2.Discs.Should().BeEmpty();
        vm2.DueDiscs.Should().BeEmpty();
        vm2.Charts.Should().BeEmpty();
    }

    private sealed class ThrowingArchive : IArchive
    {
        public T Execute<T>(Func<ArchiveRepositories, T> work) => throw new InvalidOperationException("boom");
    }

    [Fact]
    public void Trend_series_x_values_are_local_dates_relative_to_the_origin()
    {
        var at = new DateTimeOffset(2024, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var points = new[] { new TrendPoint(1, at, 1.0, 2, 3, Verdict.OK) };
        double origin = ChartSampleMapper.TrendOrigin(points);
        var series = ChartSampleMapper.ToTrendSeries("x", 0, points, origin, p => p.C1Mean);

        series.Kind.Should().Be(ChartSeriesKind.Trend);
        series.Samples.Single().TimeSeconds.Should().Be(86400);
        series.Samples.Single().C1.Should().Be(1.0);
        DateTime.UnixEpoch.AddSeconds(origin + 86400).Should().Be(at.ToLocalTime().DateTime);
    }
}
