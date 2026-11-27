using DiscCheck.Core;
using FsCheck.Xunit;

namespace DiscCheck.Core.Tests;

public class C2BitCounterPropertyTests
{
    // Feature: disccheck, Property 1: Additività del conteggio dei bit C2
    // **Validates: Requirements 7.11**
    [Property(MaxTest = 100)]
    public bool C2Count_IsAdditiveOverTwoHalves(byte[] seed)
    {
        // Normalize the generated array to exactly 294 bytes (cycled, or zeros when empty).
        var pattern = new byte[C2BitCounter.PointerBytes];
        if (seed is { Length: > 0 })
            for (int i = 0; i < pattern.Length; i++) pattern[i] = seed[i % seed.Length];

        int total = C2BitCounter.CountC2(pattern);
        int sumHalves = BitCountOf(pattern.AsSpan(0, 147)) + BitCountOf(pattern.AsSpan(147, 147));
        return total == sumHalves;
    }

    // Feature: disccheck, Property 2: Il conteggio C2 è invariante rispetto al formato con block error byte
    // **Validates: Requirements 7.4, 7.5**
    [Property(MaxTest = 100)]
    public bool C2Count_IsInvariantToBlockErrorFormat(byte[] seed, byte blockErrorByte, byte paddingByte)
    {
        var pointers = new byte[C2BitCounter.PointerBytes];
        if (seed is { Length: > 0 })
            for (int i = 0; i < pointers.Length; i++) pointers[i] = (byte)(seed[i % seed.Length] ^ (i * 31));

        // Format 0x12: 294 bytes. Format 0x14: block error byte + padding byte + 294 pointers (296 bytes),
        // the layout handled by C2BitCounter.CountC2FromC2Area.
        var area12 = pointers;
        var area14 = new byte[C2BitCounter.PointerBytes + 2];
        area14[0] = blockErrorByte;
        area14[1] = paddingByte;
        pointers.CopyTo(area14, 2);

        int expected = C2BitCounter.CountC2(pointers);
        return C2BitCounter.CountC2FromC2Area(area12) == expected
            && C2BitCounter.CountC2FromC2Area(area14) == expected;
    }

    /// <summary>Counts bits of a half by embedding it in a zero-padded 294-byte pattern.</summary>
    private static int BitCountOf(ReadOnlySpan<byte> half)
    {
        var padded = new byte[C2BitCounter.PointerBytes];
        half.CopyTo(padded);
        return C2BitCounter.CountC2(padded);
    }
}
