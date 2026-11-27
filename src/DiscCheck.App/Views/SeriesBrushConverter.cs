using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DiscCheck.App.Views;

/// <summary>Curve index (0-based) → <c>Das.Brush.Series.(i+1)</c> of the active theme variant. No literal colours.</summary>
public sealed class SeriesBrushConverter : IValueConverter
{
    public static SeriesBrushConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int index || Application.Current is not { } app) return null;
        string key = DAS.Design.Charts.SeriesStyles.SeriesColorToken(index).Replace("Color", "Brush");
        return app.TryGetResource(key, app.ActualThemeVariant, out var res) ? res as IBrush : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
