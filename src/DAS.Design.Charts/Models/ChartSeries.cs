namespace DAS.Design.Charts;

public enum ChartSeriesKind { ScanBars, CompareCurve, Trend }

public record ChartSeries(string Name, IReadOnlyList<ChartSample> Samples, ChartSeriesKind Kind, int Index);
