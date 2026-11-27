using DAS.Design.Charts;
using DiscCheck.Core;
namespace DiscCheck.App.Charts;

/// <summary>
/// Converte i dati di dominio nei modelli di <c>DasChartView</c>. Nessun codice ScottPlot né colori:
/// lo stile è responsabilità di DAS.Design.Charts (Req. 35.4, 38.15).
/// </summary>
public static class ChartSampleMapper
{
    /// <summary>Converte un campione di scansione; i valori nulli restano nulli.</summary>
    public static ChartSample ToChartSample(ScanSample s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new ChartSample(s.TimeSeconds, s.C1, s.C2, s.CU, s.SpeedX);
    }

    public static IReadOnlyList<ChartSample> ToChartSamples(IEnumerable<ScanSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        return samples.Select(ToChartSample).ToList();
    }

    /// <summary>
    /// Curva di Confronto: il valore della metrica scelta è posto in <c>C1</c>, il campo che
    /// <c>DasChartView</c> traccia per le serie <see cref="ChartSeriesKind.CompareCurve"/>; i nulli restano nulli
    /// (Req. 19.1, 19.8). Stile e colore dipendono solo da <paramref name="index"/> (<c>SeriesStyles</c>).
    /// </summary>
    public static ChartSeries ToCompareSeries(string name, int index, IEnumerable<ScanSample> samples, CompareMetric metric)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var mapped = samples
            .Select(s => new ChartSample(s.TimeSeconds, ComparisonService.Value(s, metric), null, null, null))
            .ToList();
        return new ChartSeries(name, mapped, ChartSeriesKind.CompareCurve, index);
    }

    /// <summary>Un giorno: margine tra l'origine dell'asse delle date e la prima scansione.</summary>
    private const double TrendOriginMarginSeconds = 86400;

    /// <summary>
    /// Secondi dall'epoca Unix della data/ora <em>locale</em> di <paramref name="at"/> (l'ora locale è letta come
    /// se fosse UTC, perché <c>DasChartView</c> disegna l'asse delle date senza conversioni di fuso).
    /// </summary>
    public static double ToLocalUnixSeconds(DateTimeOffset at) =>
        (DateTime.SpecifyKind(at.ToLocalTime().DateTime, DateTimeKind.Utc) - DateTime.UnixEpoch).TotalSeconds;

    /// <summary>Origine dell'asse delle date: un giorno prima della prima scansione (0 senza punti).</summary>
    public static double TrendOrigin(IReadOnlyList<TrendPoint> points) =>
        points.Count == 0 ? 0 : points.Min(p => ToLocalUnixSeconds(p.ScannedAt)) - TrendOriginMarginSeconds;

    /// <summary>
    /// Serie di trend (<see cref="ChartSeriesKind.Trend"/>): un punto per scansione, X in secondi dall'origine
    /// <paramref name="origin"/> (vedi <see cref="TrendOrigin"/>), valore in <c>C1</c>; i nulli restano nulli (Req. 20.1, 20.14).
    /// </summary>
    public static ChartSeries ToTrendSeries(string name, int index, IReadOnlyList<TrendPoint> points, double origin,
                                            Func<TrendPoint, double?> value)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(value);
        var samples = points
            .OrderBy(p => p.ScannedAt)
            .Select(p => new ChartSample(ToLocalUnixSeconds(p.ScannedAt) - origin, value(p), null, null, null))
            .ToList();
        return new ChartSeries(name, samples, ChartSeriesKind.Trend, index);
    }

    /// <summary>Assi di un grafico di trend: asse X "Data" in date locali, asse Y con nome e unità della serie (Req. 38.5).</summary>
    public static ChartAxisSpec ToTrendAxisSpec(string yLabelWithUnit, double origin) =>
        new("Data", yLabelWithUnit, null, ChartXAxisFormat.Date, origin);

    /// <summary>Soglie e tipo di scanner → impostazioni del grafico (linee solo per LiteOn).</summary>
    public static ThresholdSettings ToThresholdSettings(Thresholds thresholds, ScannerKind scanner, bool showThresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        return new ThresholdSettings(thresholds.C1MaxWarn, thresholds.C1MaxKo, scanner == ScannerKind.LiteOn, showThresholds);
    }

    /// <summary>
    /// Restringe i campioni all'intervallo X visibile (mantenendo un campione adiacente per lato, così
    /// la curva raggiunge i bordi), li riduce con <see cref="ChartDownsampler"/> a non più di
    /// <paramref name="widthPx"/> punti e li converte (Req. 18.6). Intervallo nullo = tutti i campioni.
    /// </summary>
    public static IReadOnlyList<ChartSample> ToVisibleChartSamples(
        IReadOnlyList<ScanSample> samples, int widthPx, double? visibleMinSeconds = null, double? visibleMaxSeconds = null)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var window = samples;
        if (visibleMinSeconds is double min && visibleMaxSeconds is double max && samples.Count > 0)
        {
            int first = 0;
            while (first < samples.Count && samples[first].TimeSeconds < min) first++;
            int last = samples.Count - 1;
            while (last >= 0 && samples[last].TimeSeconds > max) last--;
            first = Math.Max(0, first - 1);
            last = Math.Min(samples.Count - 1, last + 1);
            window = first > last ? [] : samples.Skip(first).Take(last - first + 1).ToList();
        }
        if (window.Count == 0) return [];
        return ToChartSamples(ChartDownsampler.Downsample(window, Math.Max(1, widthPx)));
    }
}
