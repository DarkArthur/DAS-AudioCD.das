using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DiscCheck.App.Startup;
using DiscCheck.App.Views;
using DiscCheck.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DiscCheck.App;

public partial class App : Application
{
    /// <summary>Maximum time the splash stays visible (Requirement 32.7).</summary>
    private static readonly TimeSpan SplashMaxDuration = TimeSpan.FromSeconds(5);

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = Program.Host?.Services
                ?? throw new InvalidOperationException("The application host has not been built.");

            // Saved theme applied before any window is shown (Requirement 22.7).
            services.GetRequiredService<IThemeApplier>().Apply(services.GetRequiredService<SettingsStore>().Current.Theme);

            var splash = new SplashWindow();
            splash.Show();

            var main = services.GetRequiredService<MainWindow>();
            main.Opened += (_, _) => splash.Close();
            desktop.MainWindow = main;

            var timer = new DispatcherTimer { Interval = SplashMaxDuration };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                splash.Close();
            };
            timer.Start();
            main.Opened += (_, _) => timer.Stop();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
