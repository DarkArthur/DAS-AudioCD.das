using DiscCheck.Data;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Data.Sqlite;

namespace DiscCheck.Core.Tests;

public class RepositoryRoundTripPropertyTests
{
    private static SqliteConnection NewDb()
    {
        var db = Migrator.OpenConnection("Data Source=:memory:");
        new Migrator().Migrate(db);
        return db;
    }

    private static string? Str(bool isNull, byte seed) => isNull ? null : "s" + seed + "\u00e8'\"; --";

    private static bool? Bool(byte b) => (b % 3) switch { 0 => null, 1 => false, _ => true };

    private static int? OptInt(byte b, int v) => b % 2 == 0 ? null : v;

    // Feature: disccheck, Property 15: Round-trip dei Repository
    // **Validates: Requirements 16.16**
    [Property(MaxTest = 100)]
    public bool Drive_Media_Disc_RoundTrip(
        byte vendorSeed, byte[] flags, short offset, ushort cacheKb,
        bool labelNull, byte labelSeed, bool midNull, byte midSeed, byte[] discSeed,
        short burnSpeedTenths, int trackCount, uint totalSectors)
    {
        using var db = NewDb();
        flags ??= [];
        byte F(int i) => i < flags.Length ? flags[i] : (byte)0;

        var drive = new DriveRecord(0, "V" + vendorSeed, "M", "FW", Bool(F(0)), OptInt(F(1), offset),
                                    Bool(F(2)), OptInt(F(3), cacheKb), Bool(F(4)));
        var drepo = new DriveRepository(db);
        var did = drepo.AddIfMissing(drive);
        if (drepo.Get(did) != drive with { Id = did }) return false;

        var media = new MediaRecord(0, Str(midNull, midSeed), Str(labelNull, labelSeed), Str(midNull, labelSeed), Str(labelNull, midSeed));
        var mrepo = new MediaRepository(db);
        var mid = mrepo.Add(media);
        if (mrepo.Get(mid) != media with { Id = mid }) return false;

        var ds = discSeed ?? [];
        byte D(int i) => i < ds.Length ? ds[i] : (byte)0;
        var disc = new DiscRecord(0, Str(D(0) % 2 == 0, D(1)), D(2) % 2 == 0 ? null : mid,
            Str(D(3) % 2 == 0, D(4)), D(5) % 2 == 0 ? null : burnSpeedTenths / 10.0,
            D(6) % 2 == 0 ? null : "2020-01-02", Str(D(7) % 2 == 0, D(8)),
            "hash" + D(9), Math.Abs(trackCount % 100), totalSectors,
            Str(D(10) % 2 == 0, D(11)), D(12) % 2 == 0 ? null : "2031-12-31");
        var discRepo = new DiscRepository(db);
        var id = discRepo.Add(disc);
        return discRepo.Get(id) == disc with { Id = id }
            && discRepo.FindByTocHash(disc.TocHash)?.Id == id;
    }

    // Feature: disccheck, Property 15: Round-trip dei Repository
    // **Validates: Requirements 16.16**
    [Property(MaxTest = 100)]
    public bool Scan_WithSamples_RoundTrip(
        byte[] c1Seed, byte[] c2Seed, byte[] cuSeed, byte[] speedSeed, byte[] lbaSeed,
        bool liteOn, byte verdictSeed, byte[] totals, byte secondsSeed, short offsetMinutes)
    {
        using var db = NewDb();
        var driveId = new DriveRepository(db).AddIfMissing(new DriveRecord(0, "LITE-ON", "iHAS124", "WL0X"));
        var discId = new DiscRepository(db).Add(new DiscRecord(0, null, null, null, null, null, null, "h", 1, 100, null, null));

        c1Seed ??= []; c2Seed ??= []; cuSeed ??= []; speedSeed ??= []; lbaSeed ??= []; totals ??= [];
        var n = Math.Min(c1Seed.Length, 200);
        var samples = Enumerable.Range(0, n).Select(i => new ScanSample(
            i * 75 + (i < lbaSeed.Length ? lbaSeed[i] % 75 : 0),
            i,
            c1Seed[i] % 4 == 0 ? null : c1Seed[i],
            i < c2Seed.Length ? c2Seed[i] : 0,
            i < cuSeed.Length ? (cuSeed[i] % 5 == 0 ? null : cuSeed[i]) : 0, // null = CU not measured
            i < speedSeed.Length && speedSeed[i] % 4 != 0 ? speedSeed[i] / 4.0 : null)).ToArray();

        byte T(int i) => i < totals.Length ? totals[i] : (byte)0;
        var offset = TimeSpan.FromMinutes(offsetMinutes % 840);
        var scan = new ScanRecord(0, discId, driveId,
            new DateTimeOffset(2024, 5, 1, 10, 0, secondsSeed % 60, offset),
            8, liteOn ? ScannerKind.LiteOn : ScannerKind.StandardC2, (Verdict)(verdictSeed % 3),
            T(0) % 2 == 0 ? null : T(1), T(2) % 2 == 0 ? null : T(3) / 8.0, T(4) % 2 == 0 ? null : T(5),
            T(6) % 2 == 0 ? null : T(7), T(8) % 2 == 0 ? null : T(9), T(10) % 2 == 0 ? null : T(11));

        var repo = new ScanRepository(db);
        var id = repo.Save(scan, samples);
        return repo.Get(id) == scan with { Id = id }
            && repo.GetSamples(id).SequenceEqual(samples);
    }

    // Feature: disccheck, Property 15: Round-trip dei Repository
    // **Validates: Requirements 16.16**
    [Property(MaxTest = 100)]
    public bool Verification_WithTracks_RoundTrip(
        byte[] trackSeed, byte resultSeed, short offsetUsed, bool pathNull, byte pathSeed, byte secondsSeed)
    {
        using var db = NewDb();
        var driveId = new DriveRepository(db).AddIfMissing(new DriveRecord(0, "LITE-ON", "iHAS124", "WL0X"));
        var discId = new DiscRepository(db).Add(new DiscRecord(0, null, null, null, null, null, null, "h", 1, 100, null, null));

        trackSeed ??= [];
        var tracks = trackSeed.Take(99).Select((b, i) => new VerificationTrackRecord(
            i + 1, b % 2 == 0, b, b % 3 == 0 ? null : b * 10L,
            b.ToString("X8"), new string("0123456789abcdef"[b % 16], 64), b / 2,
            b % 5 == 0 ? null : "10,20," + b)).ToArray();

        var v = new VerificationRecord(0, discId, driveId,
            new DateTimeOffset(2024, 6, 1, 0, 0, secondsSeed % 60, TimeSpan.Zero),
            Str(pathNull, pathSeed), offsetUsed, (VerificationResult)(resultSeed % 3));

        var repo = new VerificationRepository(db);
        var id = repo.Save(v, tracks);
        return repo.Get(id) == v with { Id = id }
            && repo.GetTracks(id).SequenceEqual(tracks);
    }
}
