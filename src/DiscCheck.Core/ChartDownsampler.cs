namespace DiscCheck.Core;

/// <summary>
/// Riduce una serie di campioni a non più di <c>widthPx</c> punti conservando il massimo di ogni
/// intervallo (nessun picco di C1, C2, CU o velocità scompare). Funzione pura (Req. 18.6).
/// </summary>
public static class ChartDownsampler
{
    /// <summary>
    /// Se i campioni non superano <paramref name="widthPx"/> restituisce la serie invariata.
    /// Altrimenti raggruppa i campioni consecutivi in intervalli di uguale numero (l'ultimo può essere
    /// più corto) e produce un punto per intervallo: <c>Lba</c> e <c>TimeSeconds</c> del primo campione,
    /// massimo per ciascuna metrica (C1, CU e velocità null solo se tutti i valori dell'intervallo sono null).
    /// </summary>
    public static IReadOnlyList<ScanSample> Downsample(IReadOnlyList<ScanSample> samples, int widthPx)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfLessThan(widthPx, 1);

        if (samples.Count <= widthPx)
            return samples;

        int bucketSize = (samples.Count + widthPx - 1) / widthPx;
        var result = new List<ScanSample>((samples.Count + bucketSize - 1) / bucketSize);

        for (int start = 0; start < samples.Count; start += bucketSize)
        {
            int end = Math.Min(start + bucketSize, samples.Count);
            var first = samples[start];
            int? c1 = first.C1;
            int c2 = first.C2;
            int? cu = first.CU;
            double? speed = first.SpeedX;

            for (int i = start + 1; i < end; i++)
            {
                var s = samples[i];
                if (s.C1 is int v1 && (c1 is null || v1 > c1)) c1 = v1;
                if (s.C2 > c2) c2 = s.C2;
                if (s.CU is int vcu && (cu is null || vcu > cu)) cu = vcu;
                if (s.SpeedX is double sp && (speed is null || sp > speed)) speed = sp;
            }

            result.Add(new ScanSample(first.Lba, first.TimeSeconds, c1, c2, cu, speed));
        }

        return result;
    }
}
