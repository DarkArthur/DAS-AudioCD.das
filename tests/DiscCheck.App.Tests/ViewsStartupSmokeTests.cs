using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using DiscCheck.App.Views;
using FluentAssertions;

namespace DiscCheck.App.Tests;

/// <summary>
/// Every view must be created and laid out with the real styles and tokens, in both theme variants. Catches resource
/// and style type errors (e.g. a Double token for a Thickness property) that make the application fail at startup.
/// </summary>
public class ViewsStartupSmokeTests
{
    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();
        foreach (var view in new[]
                 {
                     nameof(DriveView), nameof(DiscView), nameof(ScanView), nameof(VerifyView), nameof(HistoryView),
                     nameof(ComparisonView), nameof(DegradationView), nameof(SettingsView), nameof(C2ReliabilityPanel),
                 })
        {
            data.Add(view, "Light");
            data.Add(view, "Dark");
        }
        return data;
    }

    private static Control Create(string name) => name switch
    {
        nameof(DriveView) => new DriveView(),
        nameof(DiscView) => new DiscView(),
        nameof(ScanView) => new ScanView(),
        nameof(VerifyView) => new VerifyView(),
        nameof(HistoryView) => new HistoryView(),
        nameof(ComparisonView) => new ComparisonView(),
        nameof(DegradationView) => new DegradationView(),
        nameof(SettingsView) => new SettingsView(),
        nameof(C2ReliabilityPanel) => new C2ReliabilityPanel(),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public void ViewIsCreatedAndLaidOut(string view, string variant)
    {
        var window = new Window
        {
            Width = 1100,
            Height = 800,
            RequestedThemeVariant = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light,
            Content = Create(view),
        };

        var act = () => { window.Show(); window.UpdateLayout(); };

        act.Should().NotThrow();
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void MainWindowAndAboutWindowAreCreatedAndLaidOut(string variant)
    {
        var theme = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var main = new MainWindow { RequestedThemeVariant = theme };
        var about = new AboutWindow { RequestedThemeVariant = theme };

        var act = () =>
        {
            main.Show();
            main.UpdateLayout();
            about.Show();
            about.UpdateLayout();
        };

        act.Should().NotThrow();
        main.Close();
        about.Close();
    }
}