namespace DiscCheck.Core;

public enum VerificationResult { Identical, Different, Error }

/// <summary>Row of <c>drives</c>. <see cref="Id"/> is 0 before the first save.</summary>
public sealed record DriveRecord(long Id, string Vendor, string Model, string Firmware,
                                 bool? C2Supported = null, int? ReadOffset = null,
                                 bool? CachesAudio = null, int? CacheKb = null, bool? C2Reliable = null);

/// <summary>Row of <c>media</c>.</summary>
public sealed record MediaRecord(long Id, string? MediaId, string? Manufacturer, string? Type, string? Label);

/// <summary>Row of <c>discs</c> (Requirement 16.14). Dates BurnDate/RecheckDue are AAAA-MM-GG or null.</summary>
public sealed record DiscRecord(long Id, string? Label, long? MediaId, string? BurnDrive, double? BurnSpeed,
                                string? BurnDate, string? BurnSoftware, string TocHash, int TrackCount,
                                long TotalSectors, string? StorageNotes, string? RecheckDue);

/// <summary>Row of <c>scans</c> (samples are stored separately).</summary>
public sealed record ScanRecord(long Id, long DiscId, long DriveId, DateTimeOffset ScannedAt, double SpeedX,
                                ScannerKind Scanner, Verdict Verdict,
                                long? C1Total, double? C1Mean, int? C1Max, long? C2Total, int? C2Max, long? CuTotal);

/// <summary>Row of <c>verifications</c>.</summary>
public sealed record VerificationRecord(long Id, long DiscId, long DriveId, DateTimeOffset VerifiedAt,
                                        string? SourcePath, int OffsetUsed, VerificationResult Result);

/// <summary>Row of <c>verification_tracks</c>.</summary>
public sealed record VerificationTrackRecord(int TrackNo, bool Identical, long DifferentSamples, long? FirstDiffIndex,
                                             string Crc32, string Sha256, long ExcludedEdgeSamples, string? UnreliableLbas);

public interface IDriveRepository
{
    /// <summary>Inserts the drive unless (vendor, model, firmware) already exists; returns the row id either way (Requirement 4.5).
    /// On an existing row, known (non-null) capability values replace the stored ones.</summary>
    long AddIfMissing(DriveRecord drive);
    DriveRecord? Get(long id);
    IReadOnlyList<DriveRecord> List();
}

public interface IMediaRepository
{
    long Add(MediaRecord media);
    MediaRecord? Get(long id);
    MediaRecord? FindByMediaId(string mediaId);
}

public interface IDiscRepository
{
    long Add(DiscRecord disc);
    void Update(DiscRecord disc);
    DiscRecord? Get(long id);
    DiscRecord? FindByTocHash(string tocHash);
    IReadOnlyList<DiscRecord> List();
}

public interface IScanRepository
{
    /// <summary>Saves the scan and all its samples in one transaction; on failure nothing is left behind (Requirements 16.11, 16.12).</summary>
    long Save(ScanRecord scan, IReadOnlyList<ScanSample> samples);
    ScanRecord? Get(long id);
    IReadOnlyList<ScanSample> GetSamples(long scanId);
    /// <summary>All scans, most recent first.</summary>
    IReadOnlyList<ScanRecord> List();
    IReadOnlyList<ScanRecord> ListByDisc(long discId);
    /// <summary>Deletes scan and samples in one transaction; rollback on failure (Requirement 17.6, 17.7). False if the scan does not exist.</summary>
    bool Delete(long id);
}

public interface IVerificationRepository
{
    /// <summary>Saves verification and tracks in one transaction.</summary>
    long Save(VerificationRecord verification, IReadOnlyList<VerificationTrackRecord> tracks);
    VerificationRecord? Get(long id);
    IReadOnlyList<VerificationTrackRecord> GetTracks(long verificationId);
    /// <summary>All verifications, most recent first.</summary>
    IReadOnlyList<VerificationRecord> List();
}
