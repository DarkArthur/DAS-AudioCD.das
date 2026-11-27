using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using FluentAssertions;

namespace DAS.Design.Tests;

/// <summary>
/// Shared styles must apply without type errors (a Double token assigned to a Thickness property crashed the
/// application at startup in 1.0.0).
/// </summary>
public class SharedStylesTests
{
    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void CardButtonsAndTextStylesApplyWithoutErrors(string variant)
    {
        var theme = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var card = new Border { Classes = { "card" }, Child = new TextBlock { Text = "x", Classes = { "Body" } } };
        var panel = new StackPanel
        {
            Children =
            {
                card,
                new Button { Classes = { "primary" }, Content = "p" },
                new Button { Classes = { "secondary" }, Content = "s" },
                new Button { Classes = { "danger" }, Content = "d" },
            },
        };
        var window = new Window { RequestedThemeVariant = theme, Content = panel };

        var act = () => { window.Show(); window.UpdateLayout(); };

        act.Should().NotThrow();
        card.Padding.Left.Should().Be(16);
        card.Padding.Top.Should().Be(16);
    }
}
