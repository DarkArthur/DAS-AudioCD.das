namespace DAS.Design.Charts;

/// <summary>Individua i secondi con errori C2/CU da evidenziare.</summary>
public static class ErrorHighlighter
{
    /// <summary>Campioni con C2 &gt; 0 oppure CU &gt; 0 (valori nulli ignorati).</summary>
    public static IReadOnlyList<ChartSample> Seconds(IEnumerable<ChartSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        return samples.Where(s => s.C2 > 0 || s.CU > 0).ToList();
    }

    public static string Label(int count) => $"Secondi con errori C2/CU: {count}";
}
