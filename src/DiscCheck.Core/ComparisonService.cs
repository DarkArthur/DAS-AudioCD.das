using System.Globalization;

namespace DiscCheck.Core;

/// <summary>Metric plotted on the Y axis of the comparison chart (Requirement 19.1, default C1).</summary>
public enum CompareMetric { C1, C2, CU }

/// <summary>A saved scan with the texts needed by the comparison. <paramref name="Medium"/> may be empty.</summary>
public sealed record ComparisonInput(ScanRecord Scan, IReadOnlyList<ScanSample> Samples, string DriveName, string Medium);

/// <summary>
/// One row of the comparison table (Requirement 19.4). Null values mean "n/d": no sample had the value.
/// <see cref="Legend"/> carries date, medium and speed (Requirement 19.1).
/// </summary>
public sealed record ComparisonRow(int Index, long ScanId, string Legend, double? C1Mean, int? C1Max, long? C2Total,
                                   long? CuTotal, Verdict Verdict);

/// <summary>Outcome of <see cref="ComparisonService.Compare"/>.</summary>
public sealed record ComparisonResult(IReadOnlyList<ComparisonInput> Accepted, IReadOnlyList<ComparisonRow> Rows,
                                      string? LimitMessage, string? MinimumMessage, string? MismatchWarning)
{
    /// <summary>The overlay is drawn only with at least two scans (Requirement 19.5).</summary>
    public bool CanOverlay => Accepted.Count >= ComparisonService.MinScans;
}

/// <summary>Pure logic of the Confronto view (Requirement 19).</summary>
public static class ComparisonService
{
    public const int MinScans = 2;
    public const int MaxScans = 8;
    public const double SpeedTolerance = 0.05;

    public const string LimitMessage = "È possibile confrontare al massimo 8 scansioni: sono mantenute le prime 8 selezionate.";
    public const string MinimumMessage = "Seleziona almeno due scansioni per il confronto.";
    public const string MismatchWarning = "Confronta solo scansioni fatte con lo stesso drive alla stessa velocità";
    public const string NotAvailable = "n/d";

    public static ComparisonResult Compare(IReadOnlyList<ComparisonInput> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        var accepted = selected.Take(MaxScans).ToList();
        string? limit = selected.Count > MaxScans ? LimitMessage : null;
        string? minimum = accepted.Count == 1 ? MinimumMessage : null;

        if (accepted.Count < MinScans)
            return new ComparisonResult(accepted, accepted.Select((a, i) => Row(i, a)).ToList(), limit, minimum, null);

        bool mismatch = accepted.Any(a => a.Scan.DriveId != accepted[0].Scan.DriveId
                                          || Math.Abs(a.Scan.SpeedX - accepted[0].Scan.SpeedX) > SpeedTolerance);
        return new ComparisonResult(accepted, accepted.Select((a, i) => Row(i, a)).ToList(), limit, null,
                                    mismatch ? MismatchWarning : null);
    }

    /// <summary>Row values over the non-null samples only (Requirement 19.4).</summary>
    public static ComparisonRow Row(int index, ComparisonInput input)
    {
        long c1Sum = 0, c2 = 0, cu = 0;
        int c1Count = 0, cuCount = 0;
        int? c1Max = null;
        foreach (var s in input.Samples)
        {
            c2 += s.C2;
            if (s.C1 is int c1)
            {
                c1Sum += c1;
                c1Count++;
                if (c1Max is null || c1 > c1Max) c1Max = c1;
            }
            if (s.CU is int v)
            {
                cu += v;
                cuCount++;
            }
        }
        return new ComparisonRow(index, input.Scan.Id, Legend(input), c1Count > 0 ? (double)c1Sum / c1Count : null, c1Max,
                                 input.Samples.Count > 0 ? c2 : null, cuCount > 0 ? cu : null, input.Scan.Verdict);
    }

    /// <summary>Legend entry: scan date, medium and speed (Requirement 19.1).</summary>
    public static string Legend(ComparisonInput input)
    {
        var ci = CultureInfo.CurrentCulture;
        string date = input.Scan.ScannedAt.ToLocalTime().ToString("g", ci);
        string medium = string.IsNullOrWhiteSpace(input.Medium) ? NotAvailable : input.Medium.Trim();
        return $"{date} - {medium} - {input.Scan.SpeedX.ToString("0.#", ci)}x";
    }

    /// <summary>The value of <paramref name="metric"/> of a sample; null when not measured.</summary>
    public static int? Value(ScanSample s, CompareMetric metric) => metric switch
    {
        CompareMetric.C1 => s.C1,
        CompareMetric.C2 => s.C2,
        _ => s.CU,
    };
}
