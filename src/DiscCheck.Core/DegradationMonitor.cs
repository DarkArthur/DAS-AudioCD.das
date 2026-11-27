using System.Globalization;

namespace DiscCheck.Core;

/// <summary>One point of the trend of a disc: a saved scan (Requirement 20.1).</summary>
public sealed record TrendPoint(long ScanId, DateTimeOffset ScannedAt, double? C1Mean, int? C1Max, long? C2Total, Verdict Verdict);

/// <summary>Trend of a disc, points ordered by scan date ascending. <see cref="IsWorsening"/> follows Requirement 20.7.</summary>
public sealed record DegradationTrend(IReadOnlyList<TrendPoint> Points, bool IsWorsening)
{
    public bool HasScans => Points.Count > 0;
}

public enum RecheckStatus { Overdue, DueSoon }

/// <summary>A disc whose recheck is overdue (due date ≤ today) or due within 90 days (Requirement 20.5).</summary>
public sealed record RecheckEntry(DiscRecord Disc, DateOnly DueDate, RecheckStatus Status);

/// <summary>
/// Trend series, recheck reminders and worsening detection (Requirement 20). Pure logic over the repositories;
/// the clock is injectable through <see cref="TimeProvider"/> ("today" is the local calendar day).
/// </summary>
public sealed class DegradationMonitor(IScanRepository scans, IDiscRepository discs, TimeProvider? clock = null)
{
    public const int DueSoonDays = 90;
    public const int DefaultRecheckYears = 5;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public DateOnly Today => ToLocalDay(_clock.GetUtcNow());

    private DateOnly ToLocalDay(DateTimeOffset when) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(when, _clock.LocalTimeZone).DateTime);

    /// <summary>Scan day + <paramref name="years"/> (a 29 February moves to 28 February in non-leap years).</summary>
    public DateOnly ComputeRecheckDue(DateTimeOffset scannedAt, int years) =>
        ToLocalDay(scannedAt).AddYears(years);

    /// <summary>Trend of the disc: one point per scan, date ascending (Requirements 20.1, 20.10, 20.11).</summary>
    public DegradationTrend GetTrend(long discId)
    {
        var points = scans.ListByDisc(discId)
            .OrderBy(s => s.ScannedAt).ThenBy(s => s.Id)
            .Select(s => new TrendPoint(s.Id, s.ScannedAt, s.C1Mean, s.C1Max, s.C2Total, s.Verdict))
            .ToList();
        // Worsening: the last verdict is worse than the previous one (OK < WARN < KO); needs at least two scans.
        bool worsening = points.Count >= 2 && points[^1].Verdict > points[^2].Verdict;
        return new DegradationTrend(points, worsening);
    }

    /// <summary>
    /// Sets <c>recheck_due</c> of the disc to the date of its latest scan plus <paramref name="years"/>, or NULL when it has
    /// no scans (Requirements 20.2, 20.3, 20.11). Other discs are not touched. Returns the stored value.
    /// </summary>
    public string? UpdateRecheckDue(long discId, int years = DefaultRecheckYears)
    {
        var disc = discs.Get(discId) ?? throw new InvalidOperationException($"Disco {discId} non trovato.");
        var latest = scans.ListByDisc(discId).OrderByDescending(s => s.ScannedAt).ThenByDescending(s => s.Id)
            .Select(s => (DateTimeOffset?)s.ScannedAt).FirstOrDefault();
        string? due = latest is { } at ? ComputeRecheckDue(at, years).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
        if (disc.RecheckDue != due) discs.Update(disc with { RecheckDue = due });
        return due;
    }

    /// <summary>Discs overdue (due ≤ today) or due within the next 90 days, ordered by due date ascending (Requirement 20.5).</summary>
    public IReadOnlyList<RecheckEntry> GetDueDiscs() => Classify(discs.List(), Today);

    public static IReadOnlyList<RecheckEntry> Classify(IEnumerable<DiscRecord> all, DateOnly today)
    {
        var limit = today.AddDays(DueSoonDays);
        var result = new List<RecheckEntry>();
        foreach (var d in all)
        {
            if (d.RecheckDue is null
                || !DateOnly.TryParseExact(d.RecheckDue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var due))
                continue;
            if (due <= today) result.Add(new RecheckEntry(d, due, RecheckStatus.Overdue));
            else if (due <= limit) result.Add(new RecheckEntry(d, due, RecheckStatus.DueSoon));
        }
        return result.OrderBy(e => e.DueDate).ThenBy(e => e.Disc.Id).ToList();
    }
}
