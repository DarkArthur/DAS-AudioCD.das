using System.Globalization;

namespace DiscCheck.Core;

/// <summary>The repositories of the open Database, valid only inside <see cref="IArchive.Execute{T}"/>.</summary>
public sealed record ArchiveRepositories(IDriveRepository Drives, IMediaRepository Media, IDiscRepository Discs,
                                         IScanRepository Scans, IVerificationRepository Verifications);

/// <summary>Raised by an archive whose Database could not be opened (Requirements 16.9, 16.10, 16.15). The message is user-facing.</summary>
public sealed class ArchiveUnavailableException(string message) : Exception(message);

/// <summary>
/// Serialised access to the Database repositories. ViewModels depend on this interface only; the SQLite
/// implementation lives in DiscCheck.Data. Calls are safe from any thread.
/// </summary>
public interface IArchive
{
    /// <exception cref="ArchiveUnavailableException">The Database could not be opened at startup.</exception>
    T Execute<T>(Func<ArchiveRepositories, T> work);
}

public static class ArchiveExtensions
{
    public static void Execute(this IArchive archive, Action<ArchiveRepositories> work) =>
        archive.Execute<object?>(r => { work(r); return null; });

    /// <summary>
    /// Saves a completed scan: drive and disc rows are reused when already present (drive by vendor/model/firmware,
    /// disc by <c>toc_hash</c>), scan and samples go in one transaction (Requirements 9.8, 16.11, 16.12).
    /// </summary>
    public static long SaveScan(this IArchive archive, DriveRecord drive, DiscRecord disc, ScanRecord scan,
                                IReadOnlyList<ScanSample> samples, int? recheckYears = null) =>
        archive.Execute(r =>
        {
            long driveId = r.Drives.AddIfMissing(drive);
            long discId = r.Discs.FindByTocHash(disc.TocHash)?.Id ?? r.Discs.Add(disc);
            long scanId = r.Scans.Save(scan with { DiscId = discId, DriveId = driveId }, samples);
            // recheck_due = scan date + configured years, for this disc only (Requirements 20.2, 20.3).
            if (recheckYears is int years)
            {
                // The scan is already saved: a failure here must not make the caller retry (and duplicate) the save.
                // recheck_due is recomputed from the latest scan on the next save.
                try { new DegradationMonitor(r.Scans, r.Discs).UpdateRecheckDue(discId, years); }
                catch (Exception) { }
            }
            return scanId;
        });

    /// <summary>
    /// Saves a verification with its tracks in one transaction; drive and disc rows are reused when present
    /// (Requirements 14.12, 14.17, 15.10).
    /// </summary>
    public static long SaveVerification(this IArchive archive, DriveRecord drive, DiscRecord disc,
                                        VerificationRecord verification, IReadOnlyList<VerificationTrackRecord> tracks) =>
        archive.Execute(r =>
        {
            long driveId = r.Drives.AddIfMissing(drive);
            long discId = r.Discs.FindByTocHash(disc.TocHash)?.Id ?? r.Discs.Add(disc);
            return r.Verifications.Save(verification with { DiscId = discId, DriveId = driveId }, tracks);
        });
}

/// <summary>Everything the Storico view needs, read in one pass.</summary>
public sealed record HistorySnapshot(IReadOnlyList<DiscRecord> Discs, IReadOnlyList<MediaRecord> Media,
                                     IReadOnlyList<DriveRecord> Drives, IReadOnlyList<ScanRecord> Scans,
                                     IReadOnlyList<VerificationRecord> Verifications)
{
    public static HistorySnapshot Empty { get; } = new([], [], [], [], []);

    public static HistorySnapshot Load(ArchiveRepositories r)
    {
        var discs = r.Discs.List();
        var media = discs.Where(d => d.MediaId is not null)
            .Select(d => d.MediaId!.Value).Distinct()
            .Select(id => r.Media.Get(id)).OfType<MediaRecord>().ToList();
        return new HistorySnapshot(discs, media, r.Drives.List(), r.Scans.List(), r.Verifications.List());
    }
}

/// <summary>
/// Filters of the Storico view (Requirement 17.2). All set criteria are combined with AND; a criterion that does not
/// apply to a list (for example the Verdetto on verifications) leaves that list unfiltered (Requirement 17.3).
/// Dates are local days with inclusive bounds.
/// </summary>
public sealed record HistoryFilter(string? Medium = null, double? SpeedX = null, DateOnly? From = null, DateOnly? To = null,
                                   IReadOnlySet<Verdict>? Verdicts = null)
{
    public const double SpeedTolerance = 0.05;

    public bool IsActive => !string.IsNullOrWhiteSpace(Medium) || SpeedX is not null || From is not null || To is not null
                            || Verdicts is { Count: > 0 };

    /// <summary>Medium text matches when it is contained (case-insensitive) in <paramref name="mediumText"/>.</summary>
    public bool MatchesMedium(string? mediumText) =>
        string.IsNullOrWhiteSpace(Medium)
        || (mediumText?.Contains(Medium.Trim(), StringComparison.CurrentCultureIgnoreCase) ?? false);

    public bool MatchesDate(DateTimeOffset? when)
    {
        if (From is null && To is null) return true;
        if (when is null) return false;
        var day = DateOnly.FromDateTime(when.Value.ToLocalTime().DateTime);
        return (From is null || day >= From) && (To is null || day <= To);
    }

    public bool MatchesSpeed(double speedX) => SpeedX is not double s || Math.Abs(speedX - s) <= SpeedTolerance;

    public bool MatchesVerdict(Verdict verdict) => Verdicts is not { Count: > 0 } || Verdicts.Contains(verdict);

    public bool Matches(ScanRecord scan, string? mediumText) =>
        MatchesMedium(mediumText) && MatchesDate(scan.ScannedAt) && MatchesSpeed(scan.SpeedX) && MatchesVerdict(scan.Verdict);

    public bool Matches(VerificationRecord v, string? mediumText) =>
        MatchesMedium(mediumText) && MatchesDate(v.VerifiedAt);

    public bool Matches(DiscRecord disc, string? mediumText, DateTimeOffset? lastActivity) =>
        MatchesMedium(mediumText) && MatchesDate(lastActivity);

    public static string FormatMedium(MediaRecord? m) => m is null
        ? string.Empty
        : string.Join(' ', new[] { m.MediaId, m.Manufacturer, m.Type, m.Label }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public static string FormatSpeed(double speedX) => $"{speedX.ToString("0.#", CultureInfo.CurrentCulture)}x";
}
