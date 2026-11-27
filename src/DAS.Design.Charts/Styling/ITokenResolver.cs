using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace DAS.Design.Charts;

/// <summary>Resolves a Design_Token color for a theme variant.</summary>
public interface ITokenResolver
{
    /// <summary>Returns true and the color when <paramref name="tokenKey"/> resolves for <paramref name="variant"/>.</summary>
    bool TryResolveColor(string tokenKey, ThemeVariant variant, out Color color);
}

/// <summary>Resolver backed by the Avalonia application resources (DasTheme).</summary>
public sealed class AvaloniaTokenResolver : ITokenResolver
{
    public bool TryResolveColor(string tokenKey, ThemeVariant variant, out Color color)
    {
        color = default;
        var app = Application.Current;
        if (app is null || !app.TryGetResource(tokenKey, variant, out var value))
            return false;

        switch (value)
        {
            case Color c:
                color = c;
                return true;
            case ISolidColorBrush b:
                color = b.Color;
                return true;
            default:
                return false;
        }
    }
}
