using DiscCheck.Core;
using FsCheck.Xunit;

namespace DiscCheck.Core.Tests;

public class OffsetRoundTripPropertyTests
{
    private static short[] Noise(int frames, int seed)
    {
        var rnd = new Random(seed);
        var s = new short[frames * 2];
        for (int i = 0; i < s.Length; i++) s[i] = (short)rnd.Next(short.MinValue, short.MaxValue);
        return s;
    }

    /// <summary>drive[i] = reference[i + k]; frames outside the reference are zero.</summary>
    private static short[] Shift(short[] reference, int k)
    {
        int n = reference.Length / 2;
        var d = new short[reference.Length];
        for (int i = 0; i < n; i++)
        {
            int j = i + k;
            if (j < 0 || j >= n) continue;
            d[i * 2] = reference[j * 2];
            d[i * 2 + 1] = reference[j * 2 + 1];
        }
        return d;
    }

    // Feature: disccheck, Property 10: Round-trip del rilevamento dell'offset
    // **Validates: Requirements 15.12**
    [Property(MaxTest = 100)]
    public bool Detect_RecoversAppliedOffset(int seed, int rawOffset)
    {
        int k = (int)(((long)rawOffset % 10001 + 10001) % 10001) - OffsetDetector.MaxOffset;
        var reference = Noise(OffsetDetector.MinTrackFrames, seed);
        var result = OffsetDetector.Detect(reference, Shift(reference, k));
        return result.IsFound && result.Offset == k;
    }
}
