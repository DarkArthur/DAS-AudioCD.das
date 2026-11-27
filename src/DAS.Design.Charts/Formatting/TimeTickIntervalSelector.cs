namespace DAS.Design.Charts.Formatting;

/// <summary>
/// Chooses the major-tick interval (seconds) of the time axis: the smallest of
/// 1, 2, 5, 10, 15, 30, 60, 120, 300, 600 s that yields at most <see cref="MaxTicks"/> major ticks
/// and whose labels do not overlap. Aims for at least <see cref="MinTicks"/> ticks when the
/// duration allows it.
/// </summary>
public static class TimeTickIntervalSelector
{
    public const int MinTicks = 4;
    public const int MaxTicks = 12;

    public static readonly IReadOnlyList<int> Intervals = new[] { 1, 2, 5, 10, 15, 30, 60, 120, 300, 600 };

    /// <summary>Number of major ticks (including the one at 0) for a visible span and an interval.</summary>
    public static int TickCount(double spanSeconds, double intervalSeconds)
    {
        if (spanSeconds < 0 || double.IsNaN(spanSeconds)) return 0;
        return (int)Math.Floor(spanSeconds / intervalSeconds) + 1;
    }

    /// <summary>
    /// Selects the interval for a visible time span.
    /// </summary>
    /// <param name="spanSeconds">Visible time range length in seconds.</param>
    /// <param name="plotWidthPx">Plot width in pixels (0 or less disables the overlap check).</param>
    /// <param name="labelWidthPx">Width of one tick label in pixels (0 or less disables the overlap check).</param>
    public static double Select(double spanSeconds, double plotWidthPx = 0, double labelWidthPx = 0)
    {
        if (double.IsNaN(spanSeconds) || double.IsInfinity(spanSeconds) || spanSeconds <= 0)
            return Intervals[0];

        bool checkOverlap = plotWidthPx > 0 && labelWidthPx > 0;

        foreach (double candidate in Candidates(spanSeconds))
        {
            if (TickCount(spanSeconds, candidate) > MaxTicks)
                continue;
            if (checkOverlap && candidate / spanSeconds * plotWidthPx < labelWidthPx)
                continue;
            return candidate;
        }

        // Unreachable: Candidates is unbounded in practice; keep a safe fallback.
        return Intervals[^1];
    }

    // The fixed list, then multiples of 600 s for very long spans.
    private static IEnumerable<double> Candidates(double spanSeconds)
    {
        foreach (int i in Intervals)
            yield return i;

        double next = Intervals[^1];
        // Bounded: beyond this the span would exceed any realistic disc.
        for (int n = 2; n <= 100_000; n++)
        {
            next = 600d * n;
            yield return next;
            if (next >= spanSeconds * 4 && next >= spanSeconds) yield break;
        }
    }
}
