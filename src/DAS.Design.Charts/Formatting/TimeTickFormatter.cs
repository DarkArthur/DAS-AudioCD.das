using System.Globalization;

namespace DAS.Design.Charts.Formatting;

/// <summary>
/// Formats time-axis values as <c>mm:ss</c>. Minutes are not wrapped at 59 (e.g. 4800 s is <c>80:00</c>).
/// Output is culture-invariant.
/// </summary>
public static class TimeTickFormatter
{
    /// <summary>Formats seconds (rounded to the nearest whole second, negatives clamped to 0) as mm:ss.</summary>
    public static string Format(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds))
            throw new ArgumentOutOfRangeException(nameof(seconds), "Seconds must be a finite number.");

        long total = (long)Math.Round(Math.Max(0d, seconds), MidpointRounding.AwayFromZero);
        long minutes = total / 60;
        long secs = total % 60;
        return string.Create(CultureInfo.InvariantCulture, $"{minutes:00}:{secs:00}");
    }

    /// <summary>Parses a string produced by <see cref="Format"/> back to whole seconds.</summary>
    public static bool TryParse(string? text, out long seconds)
    {
        seconds = 0;
        if (string.IsNullOrEmpty(text))
            return false;

        int sep = text.IndexOf(':');
        if (sep <= 0 || sep != text.LastIndexOf(':'))
            return false;

        ReadOnlySpan<char> mm = text.AsSpan(0, sep);
        ReadOnlySpan<char> ss = text.AsSpan(sep + 1);
        if (ss.Length != 2 || !IsDigits(mm) || !IsDigits(ss))
            return false;

        if (!long.TryParse(mm, NumberStyles.None, CultureInfo.InvariantCulture, out long m) ||
            !long.TryParse(ss, NumberStyles.None, CultureInfo.InvariantCulture, out long s) ||
            s > 59)
            return false;

        seconds = checked(m * 60 + s);
        return true;
    }

    private static bool IsDigits(ReadOnlySpan<char> span)
    {
        if (span.IsEmpty) return false;
        foreach (char c in span)
            if (c < '0' || c > '9') return false;
        return true;
    }
}
