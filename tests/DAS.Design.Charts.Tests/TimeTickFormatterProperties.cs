using DAS.Design.Charts.Formatting;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace DAS.Design.Charts.Tests;

public class TimeTickFormatterProperties
{
    // Secondi interi non negativi, fino a ~277 ore (minuti ben oltre 59).
    private static Arbitrary<int> Seconds() => Gen.Choose(0, 1_000_000).ToArbitrary();

    // Feature: disccheck, Property 25: Round-trip del formato dei tick tempo
    // **Validates: Requirements 38.6, 37.13**
    [Property(MaxTest = 200)]
    public Property Format_then_parse_returns_original_seconds() =>
        Prop.ForAll(Seconds(), s =>
        {
            string text = TimeTickFormatter.Format(s);
            bool parsed = TimeTickFormatter.TryParse(text, out long back);
            bool shape = text.Length >= 5 && text[^3] == ':';
            return parsed && back == s && shape;
        });

    [Fact]
    public void Minutes_beyond_59_are_not_wrapped()
    {
        Assert.Equal("80:00", TimeTickFormatter.Format(4800));
        Assert.True(TimeTickFormatter.TryParse("80:00", out long s));
        Assert.Equal(4800, s);
    }
}
