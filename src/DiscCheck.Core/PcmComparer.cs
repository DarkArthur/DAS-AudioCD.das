namespace DiscCheck.Core;

/// <summary>
/// Result of a PCM comparison. <see cref="FirstDifferentFrame"/> is the drive-side frame index of the first
/// differing frame, or null when there is none.
/// </summary>
public sealed record PcmComparison(long ComparedFrames, long ExcludedFrames, long DifferentFrames, long? FirstDifferentFrame)
{
    public bool IsIdentical => DifferentFrames == 0;
}

/// <summary>
/// Frame-by-frame PCM comparison with read offset k (Requirements 15.8, 15.9). Same convention as
/// <see cref="OffsetDetector"/>: <c>drive[i]</c> is compared with <c>reference[i + k]</c>.
/// Of the nominal length n (the shorter of the two tracks) exactly min(|k|, n) frames are excluded
/// and n - excluded are compared.
/// </summary>
public static class PcmComparer
{
    public static PcmComparison Compare(ReadOnlySpan<short> reference, ReadOnlySpan<short> drive, int offset)
    {
        long n = Math.Min(reference.Length, drive.Length) / 2;
        long excluded = Math.Min(Math.Abs((long)offset), n);
        long compared = n - excluded;

        // Valid drive frames i: 0 <= i < n and 0 <= i + k < n.
        long from = offset < 0 ? excluded : 0;
        long to = from + compared;

        long different = 0;
        long? first = null;
        for (long i = from; i < to; i++)
        {
            long r = (i + offset) * 2;
            long d = i * 2;
            if (reference[(int)r] != drive[(int)d] || reference[(int)r + 1] != drive[(int)d + 1])
            {
                different++;
                first ??= i;
            }
        }

        return new PcmComparison(compared, excluded, different, first);
    }
}
