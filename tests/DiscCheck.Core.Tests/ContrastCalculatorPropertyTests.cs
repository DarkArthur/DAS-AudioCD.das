using DiscCheck.Core;
using FsCheck.Xunit;

namespace DiscCheck.Core.Tests;

public class ContrastCalculatorPropertyTests
{
    private const double Epsilon = 1e-9;

    // Feature: disccheck, Property 22: Calcolo del Contrast_Ratio
    // **Validates: Requirements 29.8, 29.9, 37.5**
    [Property(MaxTest = 100)]
    public bool ContrastRatio_IsInRange_Symmetric_AndMatchesWcagFormula(
        byte r1, byte g1, byte b1, byte r2, byte g2, byte b2)
    {
        var c1 = (R: r1, G: g1, B: b1);
        var c2 = (R: r2, G: g2, B: b2);

        double ab = ContrastCalculator.ContrastRatio(c1, c2);
        double ba = ContrastCalculator.ContrastRatio(c2, c1);

        double l1 = ContrastCalculator.RelativeLuminance(r1, g1, b1);
        double l2 = ContrastCalculator.RelativeLuminance(r2, g2, b2);
        double expected = (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);

        return ab >= 1.0 - Epsilon
            && ab <= 21.0 + Epsilon
            && Math.Abs(ab - ba) < Epsilon
            && Math.Abs(ab - expected) < Epsilon
            && l1 >= 0.0 && l1 <= 1.0 + Epsilon;
    }

    // Feature: disccheck, Property 22: Calcolo del Contrast_Ratio
    // **Validates: Requirements 29.8, 29.9, 37.5**
    [Property(MaxTest = 100)]
    public bool ContrastRatio_OfColorWithItself_IsOne(byte r, byte g, byte b) =>
        Math.Abs(ContrastCalculator.ContrastRatio((r, g, b), (r, g, b)) - 1.0) < Epsilon;

    // Feature: disccheck, Property 22: Calcolo del Contrast_Ratio
    // **Validates: Requirements 29.8, 29.9, 37.5**
    [Fact]
    public void ContrastRatio_BlackAndWhite_Is21()
    {
        double ratio = ContrastCalculator.ContrastRatio((0, 0, 0), (255, 255, 255));
        Assert.Equal(21.0, ratio, 9);
    }
}
