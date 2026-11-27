using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using DAS.Design;
using DiscCheck.App.Tests;

[assembly: AvaloniaTestApplication(typeof(ViewsTestApp))]

namespace DiscCheck.App.Tests;

/// <summary>Headless application with the same theme and resources as <c>App.axaml</c>.</summary>
public class ViewsTestApp : Application
{
    public override void Initialize()
    {
        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://DiscCheck.App/"))
        {
            Source = new Uri("avares://DiscCheck.App/Assets/DiscCheckIcons.axaml"),
        });
        Styles.Add(new DasTheme());
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<ViewsTestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
