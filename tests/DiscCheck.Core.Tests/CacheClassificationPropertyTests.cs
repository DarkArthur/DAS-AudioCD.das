using FsCheck;
using FsCheck.Xunit;

namespace DiscCheck.Core.Tests;

public class CacheClassificationPropertyTests
{
    // Positive integral millisecond values keep all arithmetic exact.
    private static double[] ToTimes(PositiveInt[] xs) => xs.Select(x => (double)(x.Get % 100_000 + 1)).ToArray();

    // Feature: disccheck, Property 19: Classificazione della cache dalle mediane dei tempi
    // **Validates: Requirements 12.2**
    [Property(MaxTest = 100)]
    public bool Classify_IsCachedIffMedianT2AtMostHalfOfMedianT1(NonEmptyArray<PositiveInt> t1, NonEmptyArray<PositiveInt> t2)
    {
        var a = ToTimes(t1.Get);
        var b = ToTimes(t2.Get);
        double m1 = CacheDetector.Median(a);
        double m2 = CacheDetector.Median(b);
        bool expected = m2 <= 0.5 * m1;

        return CacheDetector.Classify(a, b) == expected && CacheDetector.ClassifyFromMedians(m1, m2) == expected;
    }

    // Feature: disccheck, Property 19: Classificazione della cache dalle mediane dei tempi
    // **Validates: Requirements 12.2**
    [Property(MaxTest = 100)]
    public bool Classify_ConstantSamples_BoundaryAtExactlyFifthPercent(PositiveInt t1Raw, PositiveInt t2Raw, PositiveInt n)
    {
        double t1 = t1Raw.Get % 100_000 + 1;
        double t2 = t2Raw.Get % 100_000 + 1;
        int count = n.Get % 9 + 1;
        var a = Enumerable.Repeat(t1, count).ToArray();
        var b = Enumerable.Repeat(t2, count).ToArray();

        bool expected = 2 * t2 <= t1;
        bool boundary = CacheDetector.Classify(a, [t1 / 2.0]) // exactly 50% => cached
                        && !CacheDetector.Classify(a, [t1 / 2.0 + 1]);
        return CacheDetector.Classify(a, b) == expected && boundary;
    }

    // Feature: disccheck, Property 19: Classificazione della cache dalle mediane dei tempi
    // **Validates: Requirements 12.2**
    [Property(MaxTest = 100)]
    public bool Median_IsIndependentOfOrderAndLiesWithinRange(NonEmptyArray<PositiveInt> xs)
    {
        var v = ToTimes(xs.Get);
        double m = CacheDetector.Median(v);
        double mReversed = CacheDetector.Median(v.Reverse().ToArray());
        var sorted = v.OrderBy(x => x).ToArray();
        int n = sorted.Length;
        double expected = n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
        return m == mReversed && m == expected && m >= sorted[0] && m <= sorted[^1];
    }
}
