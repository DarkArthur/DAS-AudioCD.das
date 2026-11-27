namespace DAS.Design.Charts;

/// <summary>Campione di un grafico. I valori nulli indicano metrica assente.</summary>
public record ChartSample(double TimeSeconds, double? C1, double? C2, double? CU, double? SpeedX);
