using System.Numerics;

namespace DiscCheck.Core;

/// <summary>C2 error pointer counting (Requirement 7.5).</summary>
public static class C2BitCounter
{
    /// <summary>Number of C2 pointer bytes per sector.</summary>
    public const int PointerBytes = 294;

    /// <summary>
    /// Counts the bits set to 1 in the 294 C2 pointer bytes of a sector. For format 0x14 the caller passes only
    /// the 294 pointer bytes: the block error byte and the padding byte are not part of the span.
    /// </summary>
    public static int CountC2(ReadOnlySpan<byte> pointers294)
    {
        if (pointers294.Length != PointerBytes)
            throw new ArgumentException($"Expected {PointerBytes} C2 pointer bytes.", nameof(pointers294));
        return CountBits(pointers294);
    }

    /// <summary>
    /// Counts C2 bits in the C2 area that follows the 2352 user data bytes: 294 bytes (format 0x12) or 296 bytes
    /// (format 0x14: block error byte and padding byte first, then the 294 pointers). The first two bytes of the
    /// 296-byte area are excluded from the count.
    /// </summary>
    public static int CountC2FromC2Area(ReadOnlySpan<byte> c2Area) => c2Area.Length switch
    {
        PointerBytes => CountBits(c2Area),
        PointerBytes + 2 => CountBits(c2Area[2..]),
        _ => throw new ArgumentException("C2 area must be 294 or 296 bytes.", nameof(c2Area)),
    };

    private static int CountBits(ReadOnlySpan<byte> bytes)
    {
        int total = 0;
        foreach (byte b in bytes) total += BitOperations.PopCount(b);
        return total;
    }
}
