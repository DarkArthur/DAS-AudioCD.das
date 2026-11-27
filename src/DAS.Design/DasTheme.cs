using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace DAS.Design;

/// <summary>Theme entry point: <c>&lt;das:DasTheme /&gt;</c> in <c>Application.Styles</c> or <c>AppBuilder.UseDasTheme()</c>.</summary>
public class DasTheme : Styles
{
    public DasTheme()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
