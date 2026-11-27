namespace DAS.Design.Charts;

public enum ChartXAxisFormat { Time, Date }

/// <summary>
/// Specifica degli assi: etichette con unità e formato dell'asse X. Con <see cref="ChartXAxisFormat.Date"/> il
/// <c>TimeSeconds</c> dei campioni è relativo a <paramref name="XOriginUnixSeconds"/> (secondi dall'epoca Unix,
/// default 0), così l'auto-fit 0..durata resta confinato al periodo dei dati.
/// </summary>
public record ChartAxisSpec(string XLabel, string YLabel, string? SecondaryYLabel = null,
                            ChartXAxisFormat XFormat = ChartXAxisFormat.Time, double XOriginUnixSeconds = 0);
