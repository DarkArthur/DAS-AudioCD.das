using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using DAS.Design.Tests;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace DAS.Design.Tests;

/// <summary>Headless test application that references only DAS.Design.</summary>
public class TestApp : Application
{
    public override void Initialize() => Styles.Add(new DasTheme());

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
