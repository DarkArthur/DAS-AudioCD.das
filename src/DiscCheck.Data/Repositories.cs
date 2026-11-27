using System.Globalization;
using Dapper;
using DiscCheck.Core;
using Microsoft.Data.Sqlite;

namespace DiscCheck.Data;

internal static class Db
{
    public static string Date(DateTimeOffset d) => d.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public static DateTimeOffset ParseDate(string s) =>
        DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public static long? Flag(bool? b) => b is null ? null : b.Value ? 1 : 0;
    public static bool? Flag(long? v) => v is null ? null : v != 0;
    public static int? Int(long? v) => v is null ? null : checked((int)v.Value);
}

/// <summary>Dapper repository for <c>drives</c>. The connection must be open with foreign_keys=ON (<see cref="Migrator.OpenConnection"/>).</summary>
public sealed class DriveRepository(SqliteConnection db) : IDriveRepository
{
    private sealed class Row
    {
        public long Id { get; set; }
        public string Vendor { get; set; } = "";
        public string Model { get; set; } = "";
        public string Firmware { get; set; } = "";
        public long? C2Supported { get; set; }
        public long? ReadOffset { get; set; }
        public long? CachesAudio { get; set; }
        public long? CacheKb { get; set; }
        public long? C2Reliable { get; set; }
    }

    private const string Select = @"SELECT id AS Id, vendor AS Vendor, model AS Model, firmware AS Firmware,
        c2_supported AS C2Supported, read_offset AS ReadOffset, caches_audio AS CachesAudio,
        cache_kb AS CacheKb, c2_reliable AS C2Reliable FROM drives";

    private static DriveRecord Map(Row r) => new(r.Id, r.Vendor, r.Model, r.Firmware, Db.Flag(r.C2Supported),
        Db.Int(r.ReadOffset), Db.Flag(r.CachesAudio), Db.Int(r.CacheKb), Db.Flag(r.C2Reliable));

    public long AddIfMissing(DriveRecord d)
    {
        using var tx = db.BeginTransaction();
        db.Execute(@"INSERT INTO drives (vendor, model, firmware, c2_supported, read_offset, caches_audio, cache_kb, c2_reliable)
            VALUES (@Vendor, @Model, @Firmware, @C2, @Off, @Cache, @Kb, @Rel)
            ON CONFLICT (vendor, model, firmware) DO UPDATE SET
              c2_supported = COALESCE(excluded.c2_supported, c2_supported),
              read_offset  = COALESCE(excluded.read_offset, read_offset),
              caches_audio = COALESCE(excluded.caches_audio, caches_audio),
              cache_kb     = COALESCE(excluded.cache_kb, cache_kb),
              c2_reliable  = COALESCE(excluded.c2_reliable, c2_reliable)",
            new
            {
                d.Vendor, d.Model, d.Firmware, C2 = Db.Flag(d.C2Supported), Off = d.ReadOffset,
                Cache = Db.Flag(d.CachesAudio), Kb = d.CacheKb, Rel = Db.Flag(d.C2Reliable),
            }, tx);
        var id = db.ExecuteScalar<long>("SELECT id FROM drives WHERE vendor=@Vendor AND model=@Model AND firmware=@Firmware",
            new { d.Vendor, d.Model, d.Firmware }, tx);
        tx.Commit();
        return id;
    }

    public DriveRecord? Get(long id)
    {
        var r = db.QuerySingleOrDefault<Row>(Select + " WHERE id=@id", new { id });
        return r is null ? null : Map(r);
    }

    public IReadOnlyList<DriveRecord> List() => db.Query<Row>(Select + " ORDER BY id").Select(Map).ToList();
}

public sealed class MediaRepository(SqliteConnection db) : IMediaRepository
{
    private sealed class Row
    {
        public long Id { get; set; }
        public string? MediaId { get; set; }
        public string? Manufacturer { get; set; }
        public string? Type { get; set; }
        public string? Label { get; set; }
    }

    private const string Select = "SELECT id AS Id, media_id AS MediaId, manufacturer AS Manufacturer, type AS Type, label AS Label FROM media";

    private static MediaRecord? Map(Row? r) => r is null ? null : new(r.Id, r.MediaId, r.Manufacturer, r.Type, r.Label);

    public long Add(MediaRecord m) =>
        db.ExecuteScalar<long>(@"INSERT INTO media (media_id, manufacturer, type, label) VALUES (@MediaId, @Manufacturer, @Type, @Label);
            SELECT last_insert_rowid();", m);

    public MediaRecord? Get(long id) => Map(db.QuerySingleOrDefault<Row>(Select + " WHERE id=@id", new { id }));

    public MediaRecord? FindByMediaId(string mediaId) =>
        Map(db.QueryFirstOrDefault<Row>(Select + " WHERE media_id=@mediaId ORDER BY id", new { mediaId }));
}

public sealed class DiscRepository(SqliteConnection db) : IDiscRepository
{
    private sealed class Row
    {
        public long Id { get; set; }
        public string? Label { get; set; }
        public long? MediaId { get; set; }
        public string? BurnDrive { get; set; }
        public double? BurnSpeed { get; set; }
        public string? BurnDate { get; set; }
        public string? BurnSoftware { get; set; }
        public string TocHash { get; set; } = "";
        public long TrackCount { get; set; }
        public long TotalSectors { get; set; }
        public string? StorageNotes { get; set; }
        public string? RecheckDue { get; set; }
    }

    private const string Select = @"SELECT id AS Id, label AS Label, media_id AS MediaId, burn_drive AS BurnDrive,
        burn_speed AS BurnSpeed, burn_date AS BurnDate, burn_software AS BurnSoftware, toc_hash AS TocHash,
        track_count AS TrackCount, total_sectors AS TotalSectors, storage_notes AS StorageNotes, recheck_due AS RecheckDue FROM discs";

    private static DiscRecord? Map(Row? r) => r is null ? null : new(r.Id, r.Label, r.MediaId, r.BurnDrive, r.BurnSpeed,
        r.BurnDate, r.BurnSoftware, r.TocHash, (int)r.TrackCount, r.TotalSectors, r.StorageNotes, r.RecheckDue);

    public long Add(DiscRecord d) =>
        db.ExecuteScalar<long>(@"INSERT INTO discs (label, media_id, burn_drive, burn_speed, burn_date, burn_software,
              toc_hash, track_count, total_sectors, storage_notes, recheck_due)
            VALUES (@Label, @MediaId, @BurnDrive, @BurnSpeed, @BurnDate, @BurnSoftware,
              @TocHash, @TrackCount, @TotalSectors, @StorageNotes, @RecheckDue);
            SELECT last_insert_rowid();", d);

    public void Update(DiscRecord d) =>
        db.Execute(@"UPDATE discs SET label=@Label, media_id=@MediaId, burn_drive=@BurnDrive, burn_speed=@BurnSpeed,
              burn_date=@BurnDate, burn_software=@BurnSoftware, toc_hash=@TocHash, track_count=@TrackCount,
              total_sectors=@TotalSectors, storage_notes=@StorageNotes, recheck_due=@RecheckDue WHERE id=@Id", d);

    public DiscRecord? Get(long id) => Map(db.QuerySingleOrDefault<Row>(Select + " WHERE id=@id", new { id }));

    public DiscRecord? FindByTocHash(string tocHash) =>
        Map(db.QueryFirstOrDefault<Row>(Select + " WHERE toc_hash=@tocHash ORDER BY id", new { tocHash }));

    public IReadOnlyList<DiscRecord> List() => db.Query<Row>(Select + " ORDER BY id DESC").Select(r => Map(r)!).ToList();
}

public sealed class ScanRepository(SqliteConnection db) : IScanRepository
{
    private sealed class Row
    {
        public long Id { get; set; }
        public long DiscId { get; set; }
        public long DriveId { get; set; }
        public string ScannedAt { get; set; } = "";
        public double SpeedX { get; set; }
        public string Scanner { get; set; } = "";
        public string Verdict { get; set; } = "";
        public long? C1Total { get; set; }
        public double? C1Mean { get; set; }
        public long? C1Max { get; set; }
        public long? C2Total { get; set; }
        public long? C2Max { get; set; }
        public long? CuTotal { get; set; }
    }

    private sealed class SampleRow
    {
        public long Lba { get; set; }
        public long TSec { get; set; }
        public long? C1 { get; set; }
        public long C2 { get; set; }
        public long? Cu { get; set; }
        public double? SpeedX { get; set; }
    }

    private const string Select = @"SELECT id AS Id, disc_id AS DiscId, drive_id AS DriveId, scanned_at AS ScannedAt,
        speed_x AS SpeedX, scanner AS Scanner, verdict AS Verdict, c1_total AS C1Total, c1_mean AS C1Mean,
        c1_max AS C1Max, c2_total AS C2Total, c2_max AS C2Max, cu_total AS CuTotal FROM scans";

    private static ScanRecord? Map(Row? r) => r is null ? null : new(r.Id, r.DiscId, r.DriveId, Db.ParseDate(r.ScannedAt), r.SpeedX,
        Enum.Parse<ScannerKind>(r.Scanner), Enum.Parse<Verdict>(r.Verdict),
        r.C1Total, r.C1Mean, Db.Int(r.C1Max), r.C2Total, Db.Int(r.C2Max), r.CuTotal);

    public long Save(ScanRecord s, IReadOnlyList<ScanSample> samples)
    {
        using var tx = db.BeginTransaction();
        try
        {
            var id = db.ExecuteScalar<long>(@"INSERT INTO scans (disc_id, drive_id, scanned_at, speed_x, scanner, verdict,
                  c1_total, c1_mean, c1_max, c2_total, c2_max, cu_total)
                VALUES (@DiscId, @DriveId, @ScannedAt, @SpeedX, @Scanner, @Verdict,
                  @C1Total, @C1Mean, @C1Max, @C2Total, @C2Max, @CuTotal);
                SELECT last_insert_rowid();",
                new
                {
                    s.DiscId, s.DriveId, ScannedAt = Db.Date(s.ScannedAt), s.SpeedX,
                    Scanner = s.Scanner.ToString(), Verdict = s.Verdict.ToString(),
                    s.C1Total, s.C1Mean, s.C1Max, s.C2Total, s.C2Max, s.CuTotal,
                }, tx);
            db.Execute(@"INSERT INTO scan_samples (scan_id, t_sec, lba, c1, c2, cu, speed_x)
                VALUES (@ScanId, @T, @Lba, @C1, @C2, @CU, @SpeedX)",
                samples.Select(x => new { ScanId = id, T = x.TimeSeconds, x.Lba, x.C1, x.C2, x.CU, x.SpeedX }), tx);
            tx.Commit();
            return id;
        }
        catch
        {
            try { tx.Rollback(); } catch { /* già annullata */ }
            throw;
        }
    }

    public ScanRecord? Get(long id) => Map(db.QuerySingleOrDefault<Row>(Select + " WHERE id=@id", new { id }));

    public IReadOnlyList<ScanSample> GetSamples(long scanId) =>
        db.Query<SampleRow>(@"SELECT lba AS Lba, t_sec AS TSec, c1 AS C1, c2 AS C2, cu AS Cu, speed_x AS SpeedX
                FROM scan_samples WHERE scan_id=@scanId ORDER BY t_sec", new { scanId })
          .Select(r => new ScanSample((int)r.Lba, (int)r.TSec, Db.Int(r.C1), (int)r.C2, Db.Int(r.Cu), r.SpeedX)).ToList();

    public IReadOnlyList<ScanRecord> List() =>
        db.Query<Row>(Select + " ORDER BY scanned_at DESC, id DESC").Select(r => Map(r)!).ToList();

    public IReadOnlyList<ScanRecord> ListByDisc(long discId) =>
        db.Query<Row>(Select + " WHERE disc_id=@discId ORDER BY scanned_at DESC, id DESC", new { discId }).Select(r => Map(r)!).ToList();

    public bool Delete(long id)
    {
        using var tx = db.BeginTransaction();
        try
        {
            // Esplicito oltre al CASCADE: l'eliminazione resta corretta anche senza foreign_keys=ON.
            db.Execute("DELETE FROM scan_samples WHERE scan_id=@id", new { id }, tx);
            var n = db.Execute("DELETE FROM scans WHERE id=@id", new { id }, tx);
            tx.Commit();
            return n > 0;
        }
        catch
        {
            try { tx.Rollback(); } catch { /* già annullata */ }
            throw;
        }
    }
}

public sealed class VerificationRepository(SqliteConnection db) : IVerificationRepository
{
    private sealed class Row
    {
        public long Id { get; set; }
        public long DiscId { get; set; }
        public long DriveId { get; set; }
        public string VerifiedAt { get; set; } = "";
        public string? SourcePath { get; set; }
        public long OffsetUsed { get; set; }
        public string Result { get; set; } = "";
    }

    private sealed class TrackRow
    {
        public long TrackNo { get; set; }
        public long Identical { get; set; }
        public long DifferentSamples { get; set; }
        public long? FirstDiffIndex { get; set; }
        public string Crc32 { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public long ExcludedEdgeSamples { get; set; }
        public string? UnreliableLbas { get; set; }
    }

    private const string Select = @"SELECT id AS Id, disc_id AS DiscId, drive_id AS DriveId, verified_at AS VerifiedAt,
        source_path AS SourcePath, offset_used AS OffsetUsed, result AS Result FROM verifications";

    private static string ToDb(VerificationResult r) => r switch
    {
        VerificationResult.Identical => "IDENTICAL",
        VerificationResult.Different => "DIFFERENT",
        VerificationResult.Error => "ERROR",
        _ => throw new ArgumentOutOfRangeException(nameof(r)),
    };

    private static VerificationResult FromDb(string s) => s switch
    {
        "IDENTICAL" => VerificationResult.Identical,
        "DIFFERENT" => VerificationResult.Different,
        "ERROR" => VerificationResult.Error,
        _ => throw new InvalidDataException($"Esito di verifica sconosciuto: {s}"),
    };

    private static VerificationRecord? Map(Row? r) => r is null ? null : new(r.Id, r.DiscId, r.DriveId,
        Db.ParseDate(r.VerifiedAt), r.SourcePath, (int)r.OffsetUsed, FromDb(r.Result));

    public long Save(VerificationRecord v, IReadOnlyList<VerificationTrackRecord> tracks)
    {
        using var tx = db.BeginTransaction();
        try
        {
            var id = db.ExecuteScalar<long>(@"INSERT INTO verifications (disc_id, drive_id, verified_at, source_path, offset_used, result)
                VALUES (@DiscId, @DriveId, @VerifiedAt, @SourcePath, @OffsetUsed, @Result);
                SELECT last_insert_rowid();",
                new { v.DiscId, v.DriveId, VerifiedAt = Db.Date(v.VerifiedAt), v.SourcePath, v.OffsetUsed, Result = ToDb(v.Result) }, tx);
            db.Execute(@"INSERT INTO verification_tracks (verification_id, track_no, identical, different_samples,
                  first_diff_index, crc32, sha256, excluded_edge_samples, unreliable_lbas)
                VALUES (@VerificationId, @TrackNo, @Identical, @DifferentSamples, @FirstDiffIndex, @Crc32, @Sha256,
                  @ExcludedEdgeSamples, @UnreliableLbas)",
                tracks.Select(t => new
                {
                    VerificationId = id, t.TrackNo, Identical = t.Identical ? 1 : 0, t.DifferentSamples, t.FirstDiffIndex,
                    t.Crc32, t.Sha256, t.ExcludedEdgeSamples, t.UnreliableLbas,
                }), tx);
            tx.Commit();
            return id;
        }
        catch
        {
            try { tx.Rollback(); } catch { /* già annullata */ }
            throw;
        }
    }

    public VerificationRecord? Get(long id) => Map(db.QuerySingleOrDefault<Row>(Select + " WHERE id=@id", new { id }));

    public IReadOnlyList<VerificationTrackRecord> GetTracks(long verificationId) =>
        db.Query<TrackRow>(@"SELECT track_no AS TrackNo, identical AS Identical, different_samples AS DifferentSamples,
                first_diff_index AS FirstDiffIndex, crc32 AS Crc32, sha256 AS Sha256,
                excluded_edge_samples AS ExcludedEdgeSamples, unreliable_lbas AS UnreliableLbas
                FROM verification_tracks WHERE verification_id=@verificationId ORDER BY track_no", new { verificationId })
          .Select(r => new VerificationTrackRecord((int)r.TrackNo, r.Identical != 0, r.DifferentSamples, r.FirstDiffIndex,
              r.Crc32, r.Sha256, r.ExcludedEdgeSamples, r.UnreliableLbas)).ToList();

    public IReadOnlyList<VerificationRecord> List() =>
        db.Query<Row>(Select + " ORDER BY verified_at DESC, id DESC").Select(r => Map(r)!).ToList();
}
