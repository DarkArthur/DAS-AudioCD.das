namespace DiscCheck.Core;

/// <summary>
/// Totals shown after a successful scan (Requirement 18.3). C1 values are null when no sample has C1.
/// <see cref="C1Mean"/> is the arithmetic mean over the samples with a non-null C1 (not rounded).
/// <see cref="CuTotal"/> is null when no sample has a measured CU ("n/d").
/// </summary>
public sealed record ScanSummary(long? C1Total, double? C1Mean, int? C1Max, long C2Total, int C2Max, long? CuTotal)
{
    public static ScanSummary Compute(IReadOnlyList<ScanSample> samples)
    {
        long c1Sum = 0, c2 = 0, cu = 0;
        int c1Count = 0, c2Max = 0, cuCount = 0;
        int? c1Max = null;
        foreach (var s in samples)
        {
            c2 += s.C2;
            if (s.CU is int cuValue)
            {
                cu += cuValue;
                cuCount++;
            }
            if (s.C2 > c2Max) c2Max = s.C2;
            if (s.C1 is int c1)
            {
                c1Sum += c1;
                c1Count++;
                if (c1Max is null || c1 > c1Max) c1Max = c1;
            }
        }
        return new ScanSummary(c1Count > 0 ? c1Sum : null, c1Count > 0 ? (double)c1Sum / c1Count : null,
                               c1Max, c2, c2Max, cuCount > 0 ? cu : null);
    }
}

/// <summary>
/// Thrown by a scanner whose end-of-scan command (closing the Test_Mode) failed during cancellation or after an
/// error. The App logs <see cref="Cdb"/> and the sense data and offers "Reset drive" (Requirement 9.12).
/// </summary>
public sealed class TestModeEndFailedException(string message, string? cdbHex = null, string? senseText = null)
    : Exception(message)
{
    public string? Cdb { get; } = cdbHex;
    public string? Sense { get; } = senseText;
}
