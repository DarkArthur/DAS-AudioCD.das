namespace DAS.Design.Charts;

/// <summary>Seleziona le linee soglia da tracciare. Non estende mai l'asse Y.</summary>
public static class ThresholdLineSelector
{
    public static IReadOnlyList<ThresholdLine> Select(ThresholdSettings settings, AxisRange visibleYRange)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(visibleYRange);

        var lines = new List<ThresholdLine>(2);
        if (!settings.IsLiteOn || !settings.ShowThresholds)
        {
            return lines;
        }

        if (IsVisible(settings.C1MaxWarn, visibleYRange))
        {
            lines.Add(new ThresholdLine(settings.C1MaxWarn, ThresholdKind.Warn));
        }

        if (IsVisible(settings.C1MaxKo, visibleYRange))
        {
            lines.Add(new ThresholdLine(settings.C1MaxKo, ThresholdKind.Ko));
        }

        return lines;
    }

    private static bool IsVisible(double value, AxisRange range) =>
        !double.IsNaN(value) && value >= range.Min && value <= range.Max;
}
