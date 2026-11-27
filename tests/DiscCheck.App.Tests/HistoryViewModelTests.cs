using DiscCheck.App.ViewModels;
using DiscCheck.Core;
using DiscCheck.Data;
using FluentAssertions;

namespace DiscCheck.App.Tests;

public sealed class HistoryViewModelTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"history-{Guid.NewGuid():N}.db");
    private readonly ArchiveOpenResult _open;

    public HistoryViewModelTests()
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

    private long Save(string hash, DateTimeOffset at, double speed, Verdict verdict)
    {
        var drive = new DriveRecord(0, "LITE-ON", "iHAS124", "CL9J");
        var disc = new DiscRecord(0, hash, null, null, null, null, null, hash, 3, 1000, null, null);
        var scan = new ScanRecord(0, 0, 0, at, speed, ScannerKind.StandardC2, verdict, null, null, null, 4, 2, null);
        ScanSample[] samples = [new(0, 0, null, 1, 0, speed), new(75, 1, null, 3, 0, speed)];
        return _open.Archive.SaveScan(drive, disc, scan, samples);
    }

    [Fact]
    public async Task Lists_are_newest_first_and_filters_combine_with_AND()
    {
        var now = DateTimeOffset.Now;
        Save("A", now.AddDays(-3), 8, Verdict.OK);
        Save("B", now.AddDays(-1), 8, Verdict.KO);
        Save("C", now.AddDays(-2), 16, Verdict.KO);
        var vm = new HistoryViewModel(_open.Archive);
        await vm.LoadAsync();

        vm.Scans.Select(s => s.Disc).Should().Equal("B", "C", "A");

        vm.FilterKo = true;
        vm.SpeedFilter = "8";
        vm.Scans.Should().ContainSingle().Which.Disc.Should().Be("B");
    }

    [Fact]
    public void Saving_a_full_80_minute_scan_of_4800_samples_takes_under_2_seconds()
    {
        var drive = new DriveRecord(0, "LITE-ON", "iHAS124", "CL9J");
        var disc = new DiscRecord(0, "BIG", null, null, null, null, null, "BIG", 20, 360000, null, null);
        var scan = new ScanRecord(0, 0, 0, DateTimeOffset.Now, 8, ScannerKind.StandardC2, Verdict.OK, null, null, null, 0, 0, null);
        var samples = Enumerable.Range(0, 4800).Select(i => new ScanSample(i * 75, i, null, 1, 0, 8)).ToArray();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var id = _open.Archive.SaveScan(drive, disc, scan, samples);
        sw.Stop();

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        var stored = _open.Archive.Execute(r => r.Scans.List()).Should().ContainSingle().Which;
        stored.Id.Should().Be(id);
    }

    [Fact]
    public async Task Filters_without_matches_show_the_no_match_message()
    {
        Save("A", DateTimeOffset.Now, 8, Verdict.OK);
        var vm = new HistoryViewModel(_open.Archive);
        await vm.LoadAsync();

        vm.FilterKo = true;

        vm.Scans.Should().BeEmpty();
        vm.ScansEmptyMessage.Should().Be(HistoryViewModel.NoMatchMessage);
    }

    [Fact]
    public async Task Selecting_a_scan_shows_summary_and_all_samples()
    {
        Save("A", DateTimeOffset.Now, 8, Verdict.WARN);
        var vm = new HistoryViewModel(_open.Archive);
        await vm.LoadAsync();

        await vm.ShowScanAsync(vm.Scans[0]);

        vm.HasScanDetail.Should().BeTrue();
        vm.C2Total.Should().Be("4");
        vm.ChartSeriesList.Should().ContainSingle().Which.Samples.Should().HaveCount(2);
    }

    [Fact]
    public async Task Delete_needs_confirmation_and_removes_the_scan()
    {
        Save("A", DateTimeOffset.Now, 8, Verdict.OK);
        var vm = new HistoryViewModel(_open.Archive);
        await vm.LoadAsync();
        vm.SelectedScan = vm.Scans[0];

        vm.Delete();
        vm.Scans.Should().HaveCount(1);
        vm.HasPendingDelete.Should().BeTrue();

        await vm.ConfirmDeleteAsync();
        vm.Scans.Should().BeEmpty();
        _open.Archive.Execute(r => r.Scans.List()).Should().BeEmpty();
    }

    [Fact]
    public async Task Read_failure_reports_error_and_keeps_last_lists()
    {
        Save("A", DateTimeOffset.Now, 8, Verdict.OK);
        var vm = new HistoryViewModel(_open.Archive);
        await vm.LoadAsync();

        var broken = new HistoryViewModel(new UnavailableArchive("db rotto"));
        await broken.LoadAsync();

        vm.Scans.Should().HaveCount(1);
        broken.HasLoadError.Should().BeTrue();
        broken.LoadError.Should().Contain("db rotto");
    }
}
