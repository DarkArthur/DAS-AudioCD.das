namespace DAS.Design.Charts;

public static class NearestSampleFinder
{
    /// <summary>
    /// Indice del tempo più vicino a <paramref name="t"/> in un array ordinato in modo crescente
    /// (ricerca binaria). A pari distanza vince il tempo minore. -1 se l'elenco è vuoto.
    /// </summary>
    public static int Find(IReadOnlyList<double> times, double t)
    {
        int n = times.Count;
        if (n == 0) return -1;

        int lo = 0, hi = n; // primo indice con times[i] >= t
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (times[mid] < t) lo = mid + 1; else hi = mid;
        }
        if (lo == 0) return 0;
        if (lo == n) return n - 1;

        double dPrev = t - times[lo - 1];
        double dNext = times[lo] - t;
        return dPrev <= dNext ? lo - 1 : lo;
    }

    public static int Find(IReadOnlyList<ChartSample> samples, double t)
    {
        int n = samples.Count;
        if (n == 0) return -1;
        var times = new double[n];
        for (int i = 0; i < n; i++) times[i] = samples[i].TimeSeconds;
        return Find(times, t);
    }
}
