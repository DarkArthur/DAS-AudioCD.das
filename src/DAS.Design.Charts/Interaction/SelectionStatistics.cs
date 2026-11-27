namespace DAS.Design.Charts;

public static class SelectionStatistics
{
    /// <summary>Statistiche sui campioni con t0 ≤ tempo ≤ t1 (estremi inclusi); solo valori non nulli.</summary>
    public static SelectionResult Compute(IEnumerable<ChartSample> samples, double t0, double t1)
    {
        if (t0 > t1) (t0, t1) = (t1, t0);
        var inRange = samples.Where(s => s.TimeSeconds >= t0 && s.TimeSeconds <= t1).ToList();
        return new SelectionResult(t0, t1,
            Stats(inRange.Select(s => s.C1)),
            Stats(inRange.Select(s => s.C2)),
            Stats(inRange.Select(s => s.CU)),
            Stats(inRange.Select(s => s.SpeedX)));
    }

    private static SelectionStats Stats(IEnumerable<double?> values)
    {
        double min = double.PositiveInfinity, max = double.NegativeInfinity, sum = 0;
        int count = 0;
        foreach (var v in values)
        {
            if (v is not double d) continue;
            if (d < min) min = d;
            if (d > max) max = d;
            sum += d;
            count++;
        }
        return count == 0
            ? new SelectionStats(null, null, null, null, 0)
            : new SelectionStats(min, max, sum / count, sum, count);
    }
}
