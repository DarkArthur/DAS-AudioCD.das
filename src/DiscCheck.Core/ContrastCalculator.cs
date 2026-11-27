namespace DiscCheck.Core;

/// <summary>
/// WCAG 2.1 relative luminance and contrast ratio for sRGB colors.
/// </summary>
public static class ContrastCalculator
{
    /// <summary>Relative luminance (0..1) of an sRGB color with 8-bit channels.</summary>
    public static double RelativeLuminance(byte r, byte g, byte b) =>
        0.2126 * Linearize(r) + 0.7152 * Linearize(g) + 0.0722 * Linearize(b);

    /// <summary>Contrast ratio (1..21) between two sRGB colors; symmetric.</summary>
    public static double ContrastRatio(
        (byte R, byte G, byte B) first,
        (byte R, byte G, byte B) second)
    {
        double l1 = RelativeLuminance(first.R, first.G, first.B);
        double l2 = RelativeLuminance(second.R, second.G, second.B);
        return ContrastRatio(l1, l2);
    }

    /// <summary>Contrast ratio from two relative luminances, in any order.</summary>
    public static double ContrastRatio(double luminanceA, double luminanceB)
    {
        double lighter = Math.Max(luminanceA, luminanceB);
        double darker = Math.Min(luminanceA, luminanceB);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Linearize(byte channel)
    {
        double c = channel / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
