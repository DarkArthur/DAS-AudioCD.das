namespace DiscCheck.Core;

public enum C2ReliabilityKind
{
    /// <summary>Every sector with differing data was flagged by C2 in at least one read (Requirement 13.3).</summary>
    Reliable,
    /// <summary>Some sectors with differing data were never flagged by C2 (Requirement 13.4).</summary>
    Unreliable,
    /// <summary>No differing sector: a disc with a known defect is needed (Requirement 13.6).</summary>
    Inconclusive,
}

/// <param name="DifferingSectors">Sectors whose user data differs between the two reads.</param>
/// <param name="UncoveredSectors">Differing sectors not flagged by C2 in either read (the n of Unreliable(n)).</param>
public sealed record C2ReliabilityOutcome(C2ReliabilityKind Kind, int DifferingSectors, int UncoveredSectors)
{
    /// <summary>Value to save in <c>drives.c2_reliable</c>; null (leave unchanged) when inconclusive.</summary>
    public bool? ReliableValue => Kind switch
    {
        C2ReliabilityKind.Reliable => true,
        C2ReliabilityKind.Unreliable => false,
        _ => null,
    };
}

/// <summary>Pure comparison of two READ CD readings (user data + 294 C2 bytes per sector) of the same area (Requirement 13).</summary>
public static class C2ReliabilityAnalyzer
{
    public const int SectorsToRead = 7500;
    public const int UserBytes = CacheDefeatPlanner.SectorBytes;
    public const int SectorStride = UserBytes + C2BitCounter.PointerBytes;

    public static C2ReliabilityOutcome Analyze(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, int sectors)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sectors);
        long needed = (long)sectors * SectorStride;
        if (first.Length < needed || second.Length < needed)
            throw new ArgumentException($"Each reading must hold {sectors} sectors of {SectorStride} bytes.");

        int differing = 0, uncovered = 0;
        for (int s = 0; s < sectors; s++)
        {
            var a = first.Slice(s * SectorStride, SectorStride);
            var b = second.Slice(s * SectorStride, SectorStride);
            if (a[..UserBytes].SequenceEqual(b[..UserBytes]))
                continue;

            differing++;
            bool flagged = C2BitCounter.CountC2(a[UserBytes..]) > 0 || C2BitCounter.CountC2(b[UserBytes..]) > 0;
            if (!flagged)
                uncovered++;
        }

        var kind = differing == 0 ? C2ReliabilityKind.Inconclusive
            : uncovered == 0 ? C2ReliabilityKind.Reliable
            : C2ReliabilityKind.Unreliable;
        return new C2ReliabilityOutcome(kind, differing, uncovered);
    }
}
