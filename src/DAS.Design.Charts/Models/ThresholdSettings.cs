namespace DAS.Design.Charts;

public enum ThresholdKind { Warn, Ko }

public record ThresholdSettings(double C1MaxWarn, double C1MaxKo, bool IsLiteOn, bool ShowThresholds);

public record ThresholdLine(double Value, ThresholdKind Kind);
