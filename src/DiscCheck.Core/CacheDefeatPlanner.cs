namespace DiscCheck.Core;

/// <summary>Planned far-area read used to defeat the drive cache.</summary>
/// <param name="StartLba">First sector of the area to read.</param>
/// <param name="SectorCount">Number of sectors to read.</param>
public sealed record CacheDefeatArea(long StartLba, int SectorCount);

/// <summary>
/// Outcome of <see cref="CacheDefeatPlanner.Plan"/>. When <see cref="Area"/> is null the re-read
/// must run without Cache_Defeat and <see cref="MissingSectors"/> reports the limitation.
/// </summary>
public sealed record CacheDefeatPlan(CacheDefeatArea? Area, long MissingSectors)
{
    public bool IsLimited => Area is null;
}

/// <summary>
/// Pure planner for the far-area Cache_Defeat (Requirements 12.6-12.9).
/// </summary>
public static class CacheDefeatPlanner
{
    public const int SectorBytes = 2352;
    public const int MinAreaKb = 4096;
    public const int MinDistanceSectors = 28500;

    /// <summary>Area size in KB: max(2 × cache_kb, 4096); 4096 when cache_kb is null.</summary>
    public static int AreaKb(int? cacheKb) =>
        cacheKb is null ? MinAreaKb : (int)Math.Max(2L * Math.Max(cacheKb.Value, 0), MinAreaKb);

    /// <summary>Area size in sectors (rounded up so the area is never smaller than required).</summary>
    public static int AreaSectors(int? cacheKb) =>
        (int)(((long)AreaKb(cacheKb) * 1024 + SectorBytes - 1) / SectorBytes);

    /// <summary>
    /// Plans the far area for re-reading the block at <paramref name="blockLba"/>.
    /// The first sector of the area is at least <see cref="MinDistanceSectors"/> from the block,
    /// preferring the area after the block, then the one before it.
    /// </summary>
    /// <param name="blockLba">LBA of the block to be re-read.</param>
    /// <param name="totalSectors">Total sectors of the disc (valid LBAs are 0..totalSectors-1).</param>
    /// <param name="cacheKb">Estimated drive cache in KB, or null when unknown.</param>
    public static CacheDefeatPlan Plan(long blockLba, long totalSectors, int? cacheKb)
    {
        int count = AreaSectors(cacheKb);

        long forwardStart = blockLba + MinDistanceSectors;
        long forwardMissing = Math.Max(0, forwardStart + count - totalSectors);
        if (forwardMissing == 0)
            return new CacheDefeatPlan(new CacheDefeatArea(forwardStart, count), 0);

        long backwardStart = blockLba - MinDistanceSectors;
        long backwardMissing = Math.Max(0, -backwardStart) + Math.Max(0, backwardStart + count - totalSectors);
        if (backwardMissing == 0)
            return new CacheDefeatPlan(new CacheDefeatArea(backwardStart, count), 0);

        return new CacheDefeatPlan(null, Math.Min(forwardMissing, backwardMissing));
    }

    /// <summary>Largest number of missing sectors among the planned blocks (0 when every block has a far area). For the report (Requirement 12.9).</summary>
    public static long MaxMissingSectors(IEnumerable<long> blockLbas, long totalSectors, int? cacheKb) =>
        blockLbas.Select(lba => Plan(lba, totalSectors, cacheKb).MissingSectors).DefaultIfEmpty(0).Max();
}
