using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using DiscCheck.Core;

namespace DiscCheck.App.Startup;

/// <summary>Applies the UI theme without restart (Requirement 22.7).</summary>
public interface IThemeApplier
{
    void Apply(SettingsTheme theme);
}

/// <summary>
/// Switches <c>Application.RequestedThemeVariant</c>: the Design_Tokens of DAS_Design change variant, so no view
/// replaces any colour itself.
/// </summary>
public sealed class AvaloniaThemeApplier : IThemeApplier
{
    public static ThemeVariant ToVariant(SettingsTheme theme) =>
        theme == SettingsTheme.Dark ? ThemeVariant.Dark : ThemeVariant.Light;

    public void Apply(SettingsTheme theme)
    {
        if (Application.Current is not { } app) return;
        var variant = ToVariant(theme);
        if (Dispatcher.UIThread.CheckAccess()) app.RequestedThemeVariant = variant;
        else Dispatcher.UIThread.Post(() => app.RequestedThemeVariant = variant);
    }
}
