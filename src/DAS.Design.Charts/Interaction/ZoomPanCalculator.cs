namespace DAS.Design.Charts;

/// <summary>Calcoli puri di zoom, pan e auto-fit. Nessuna dipendenza dalla UI.</summary>
public static class ZoomPanCalculator
{
    public const double ZoomFactor = 1.25;
    public const double MinSpanSeconds = 5;
    public const double MinAutoFitYMax = 10;
    public const double AutoFitYMargin = 1.05;

    /// <summary>
    /// Zoom per <paramref name="wheelSteps"/> scatti (positivo = avvicina) centrato su <paramref name="pointerX"/>.
    /// L'intervallo risultante ha ampiezza in [min(5 s, durata), durata] ed è contenuto in [0, durata].
    /// </summary>
    public static AxisRange Zoom(AxisRange current, double pointerX, double wheelSteps, double duration)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (!(duration > 0) || double.IsNaN(duration) || double.IsInfinity(duration))
        {
            return new AxisRange(0, 0);
        }

        double minSpan = Math.Min(MinSpanSeconds, duration);
        double span = current.Max - current.Min;
        if (double.IsNaN(span) || span <= 0)
        {
            span = duration;
        }

        double newSpan = span / Math.Pow(ZoomFactor, wheelSteps);
        newSpan = Math.Clamp(double.IsNaN(newSpan) ? span : newSpan, minSpan, duration);

        double anchor = double.IsNaN(pointerX) ? current.Min + span / 2 : Math.Clamp(pointerX, current.Min, current.Min + span);
        double fraction = (anchor - current.Min) / span;
        double min = anchor - fraction * newSpan;
        return Constrain(new AxisRange(min, min + newSpan), duration);
    }

    /// <summary>Sposta l'intervallo di <paramref name="deltaX"/> secondi, vincolato a [0, durata].</summary>
    public static AxisRange Pan(AxisRange current, double deltaX, double duration)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (!(duration > 0) || double.IsNaN(duration) || double.IsInfinity(duration))
        {
            return new AxisRange(0, 0);
        }

        double d = double.IsNaN(deltaX) ? 0 : deltaX;
        return Constrain(new AxisRange(current.Min + d, current.Max + d), duration);
    }

    /// <summary>Intervallo X dell'auto-fit: da 0 alla durata.</summary>
    public static AxisRange AutoFitX(double duration) => new(0, Math.Max(0, duration));

    /// <summary>Intervallo Y dell'auto-fit: da 0 a 1,05 × massimo, con massimo almeno 10.</summary>
    public static AxisRange AutoFitY(double maxValue)
    {
        double max = double.IsNaN(maxValue) ? 0 : maxValue;
        return new AxisRange(0, Math.Max(MinAutoFitYMax, max * AutoFitYMargin));
    }

    /// <summary>Traslazione dell'intervallo (ampiezza invariata) dentro [0, durata]; l'ampiezza è limitata alla durata.</summary>
    public static AxisRange Constrain(AxisRange range, double duration)
    {
        ArgumentNullException.ThrowIfNull(range);
        double span = Math.Min(range.Max - range.Min, duration);
        double min = Math.Clamp(range.Min, 0, duration - span);
        return new AxisRange(min, min + span);
    }
}
