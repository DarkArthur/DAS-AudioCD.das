using DiscCheck.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace DiscCheck.Core.Tests;

public class RepositoryTests : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly long _driveId;
    private readonly long _discId;

    public RepositoryTests()
    {
        _db = Migrator.OpenConnection("Data Source=:memory:");
        new Migrator().Migrate(_db);
        _driveId = new DriveRepository(_db).AddIfMissing(new DriveRecord(0, "LITE-ON", "iHAS124", "WL0X"));
        _discId = new DiscRepository(_db).Add(new DiscRecord(0, "Disco", null, null, null, null, null, "abc", 12, 300000, null, null));
    }

    public void Dispose() => _db.Dispose();

    private long Count(string table)
    {
        using var c = _db.CreateCommand();
        c.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt64(c.ExecuteScalar());
    }

    private static ScanRecord NewScan(long disc, long drive, ScannerKind k = ScannerKind.LiteOn) =>
        new(0, disc, drive, new DateTimeOffset(2024, 5, 1, 10, 0, 0, TimeSpan.FromHours(2)), 8, k, Verdict.WARN, 10, 0.5, 7, 3, 2, 1);

    [Fact]
    public void Drive_AddIfMissing_does_not_duplicate_and_returns_same_id()
    {
        var repo = new DriveRepository(_db);
        var again = repo.AddIfMissing(new DriveRecord(0, "LITE-ON", "iHAS124", "WL0X", C2Supported: true));
        again.Should().Be(_driveId);
        repo.List().Should().ContainSingle();
        repo.Get(_driveId)!.C2Supported.Should().BeTrue();
    }

    [Fact]
    public void Media_and_disc_round_trip()
    {
        var media = new MediaRepository(_db);
        var mid = media.Add(new MediaRecord(0, "97:24:01", "Taiyo Yuden", "CD-R", "x"));
        media.Get(mid).Should().Be(new MediaRecord(mid, "97:24:01", "Taiyo Yuden", "CD-R", "x"));

        var discs = new DiscRepository(_db);
        var d = new DiscRecord(0, "L", mid, "Burner", 16.5, "2020-01-02", "sw", "hash2", 5, 1234, "note", "2030-01-02");
        var id = discs.Add(d);
        discs.Get(id).Should().Be(d with { Id = id });
        discs.FindByTocHash("hash2")!.Id.Should().Be(id);
    }

    [Fact]
    public void Scan_round_trip_with_samples_and_null_values()
    {
        var repo = new ScanRepository(_db);
        var samples = new[]
        {
            new ScanSample(0, 0, null, 0, 0, null),
            new ScanSample(75, 1, 4, 2, 1, 8.5),
        };
        var scan = NewScan(_discId, _driveId);
        var id = repo.Save(scan, samples);

        repo.Get(id).Should().Be(scan with { Id = id });
        repo.GetSamples(id).Should().Equal(samples);
    }

    [Fact]
    public void Scan_save_rolls_back_on_failed_samples()
    {
        var repo = new ScanRepository(_db);
        var dup = new[] { new ScanSample(0, 0, 1, 0, 0, 1), new ScanSample(75, 0, 1, 0, 0, 1) };
        var act = () => repo.Save(NewScan(_discId, _driveId), dup);
        act.Should().Throw<SqliteException>();
        Count("scans").Should().Be(0);
        Count("scan_samples").Should().Be(0);
    }

    [Fact]
    public void Scan_delete_removes_samples_and_unknown_id_returns_false()
    {
        var repo = new ScanRepository(_db);
        var id = repo.Save(NewScan(_discId, _driveId), [new ScanSample(0, 0, 1, 0, 0, 1)]);
        repo.Delete(id).Should().BeTrue();
        Count("scans").Should().Be(0);
        Count("scan_samples").Should().Be(0);
        repo.Delete(id).Should().BeFalse();
    }

    [Fact]
    public void Scan_list_is_ordered_by_date_descending()
    {
        var repo = new ScanRepository(_db);
        var old = repo.Save(NewScan(_discId, _driveId) with { ScannedAt = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero) }, []);
        var recent = repo.Save(NewScan(_discId, _driveId) with { ScannedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) }, []);
        repo.List().Select(s => s.Id).Should().Equal(recent, old);
    }

    [Fact]
    public void Verification_round_trip_with_tracks()
    {
        var repo = new VerificationRepository(_db);
        var v = new VerificationRecord(0, _discId, _driveId, new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero), "C:\\rip", -30, VerificationResult.Different);
        var tracks = new[]
        {
            new VerificationTrackRecord(1, true, 0, null, "0A1B2C3D", new string('a', 64), 30, null),
            new VerificationTrackRecord(2, false, 5, 100, "FFFFFFFF", new string('b', 64), 30, "10,20"),
        };
        var id = repo.Save(v, tracks);
        repo.Get(id).Should().Be(v with { Id = id });
        repo.GetTracks(id).Should().Equal(tracks);
        repo.List().Should().ContainSingle();
    }
}
