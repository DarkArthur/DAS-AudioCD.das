using DiscCheck.Core;
using FsCheck.Xunit;

namespace DiscCheck.Core.Tests;

public class PcmComparerBoundaryPropertyTests
{
    private static short[] BuildTrack(int frames, int seed)
    {
        var rng = new Random(seed);
        var pcm = new short[frames * 2];
        for (int i = 0; i < pcm.Length; i++)
            pcm[i] = (short)rng.Next(short.MinValue, short.MaxValue + 1);
        return pcm;
    }

    // Feature: disccheck, Property 11: Esclusione dei campioni di bordo proporzionale all'offset
    // **Validates: Requirements 15.8, 15.9**
    [Property(MaxTest = 100)]
    public bool Compare_ExcludesMinAbsOffsetAndLengthFrames(ushort frames, short offset, int seed)
    {
        int n = frames % 400;
        int k = offset % 600; // includes |k| > n
        var reference = BuildTrack(n, seed);
        var drive = BuildTrack(n, seed + 1);

        var result = PcmComparer.Compare(reference, drive, k);

        long expectedExcluded = Math.Min(Math.Abs((long)k), n);
        return result.ExcludedFrames == expectedExcluded
            && result.ComparedFrames == n - expectedExcluded
            && result.ExcludedFrames + result.ComparedFrames == n;
    }

    // Feature: disccheck, Property 11: Esclusione dei campioni di bordo proporzionale all'offset
    // **Validates: Requirements 15.8, 15.9**
    [Property(MaxTest = 100)]
    public bool Compare_ShiftedCopyIsIdenticalOverComparedFrames(ushort frames, short offset, int seed)
    {
        int n = frames % 400;
        int k = offset % 600;
        var reference = BuildTrack(n, seed);

        // drive[i] = reference[i + k] where both are in range.
        var drive = new short[n * 2];
        for (int i = 0; i < n; i++)
        {
            int j = i + k;
            if (j < 0 || j >= n) continue;
            drive[i * 2] = reference[j * 2];
            drive[i * 2 + 1] = reference[j * 2 + 1];
        }

        var result = PcmComparer.Compare(reference, drive, k);
        return result.IsIdentical && result.ComparedFrames == n - Math.Min(Math.Abs((long)k), n);
    }
}
