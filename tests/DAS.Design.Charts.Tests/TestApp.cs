using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using DAS.Design.Charts.Tests;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace DAS.Design.Charts.Tests;

/// <summary>Headless test application: DasTheme only.</summary>
public class TestApp : Application
{
    public override void Initialize() => Styles.Add(new DasTheme());

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
