namespace DiscCheck.Core;

public enum Verdict { OK, WARN, KO }

public enum ScannerKind { StandardC2, LiteOn }

/// <summary>Verdict thresholds (Requirement 11.5 defaults). C1 values are errors per second.</summary>
public record Thresholds(double C1MeanWarn = 1, double C1MaxWarn = 50, double C1MaxKo = 220);

/// <summary>Statistics used by the Evaluator (Requirement 11.1). C1 values are null when no sample has C1.</summary>
public sealed record ScanStatistics(long C2Total, long CuTotal, double? C1Mean, int? C1Max);

/// <summary>Pure verdict logic (Requirement 11).</summary>
public static class Evaluator
{
    public static ScanStatistics ComputeStatistics(IReadOnlyList<ScanSample> samples)
    {
        long c2 = 0, cu = 0, c1Sum = 0;
        int c1Count = 0;
        int? c1Max = null;
        foreach (var s in samples)
        {
            c2 += s.C2;
            cu += s.CU ?? 0; // CU not measured (null) adds nothing
            if (s.C1 is int c1)
            {
                c1Sum += c1;
                c1Count++;
                if (c1Max is null || c1 > c1Max) c1Max = c1;
            }
        }
        return new ScanStatistics(c2, cu, c1Count > 0 ? (double)c1Sum / c1Count : null, c1Max);
    }

    /// <summary>Returns null when there are no samples (Requirement 11.10).</summary>
    public static Verdict? Evaluate(IReadOnlyList<ScanSample> samples, ScannerKind scanner, Thresholds thresholds)
    {
        if (samples.Count == 0) return null;
        var st = ComputeStatistics(samples);
        bool hasErrors = st.C2Total > 0 || st.CuTotal > 0;

        if (scanner == ScannerKind.StandardC2)
            return hasErrors ? Verdict.KO : Verdict.OK;

        if (hasErrors || (st.C1Max is int max && max >= thresholds.C1MaxKo))
            return Verdict.KO;
        bool warn = (st.C1Mean is double mean && mean >= thresholds.C1MeanWarn)
                    || (st.C1Max is int m && m >= thresholds.C1MaxWarn);
        return warn ? Verdict.WARN : Verdict.OK;
    }
}
