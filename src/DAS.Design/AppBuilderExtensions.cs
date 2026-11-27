using Avalonia;

namespace DAS.Design;

public static class AppBuilderExtensions
{
    /// <summary>Adds <see cref="DasTheme"/> to the application styles (equivalent to the XAML one-liner).</summary>
    public static AppBuilder UseDasTheme(this AppBuilder builder)
    {
        return builder.AfterSetup(_ => Application.Current?.Styles.Add(new DasTheme()));
    }
}
