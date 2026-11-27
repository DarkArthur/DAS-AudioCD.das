using DiscCheck.Core;
using FsCheck.Xunit;

namespace DiscCheck.Core.Tests;

public class CacheDefeatPlannerPropertyTests
{
    private static long ExpectedMinKb(int? cacheKb) =>
        cacheKb is null ? 4096 : Math.Max(2L * cacheKb.Value, 4096);

    // Feature: disccheck, Property 20: Pianificazione del Cache_Defeat coerente con cache_kb
    // **Validates: Requirements 12.6, 12.8**
    [Property(MaxTest = 100)]
    public bool PlannedArea_IsConsistentWithCacheKb(int? rawCacheKb, uint rawBlock, uint rawExtra)
    {
        int? cacheKb = rawCacheKb is null ? null : (int)((uint)rawCacheKb.Value % 131072u);
        long blockLba = rawBlock % 400_000u;
        // Disc large enough in most cases, but sometimes too small to fit the area.
        long totalSectors = blockLba + 1 + rawExtra % 600_000u;

        var plan = CacheDefeatPlanner.Plan(blockLba, totalSectors, cacheKb);

        if (plan.Area is null)
            return plan.IsLimited && plan.MissingSectors > 0;

        var area = plan.Area;
        long areaBytes = (long)area.SectorCount * CacheDefeatPlanner.SectorBytes;
        return areaBytes >= ExpectedMinKb(cacheKb) * 1024
            && Math.Abs(area.StartLba - blockLba) >= 28_500
            && area.StartLba >= 0
            && area.StartLba + area.SectorCount <= totalSectors
            && plan.MissingSectors == 0;
    }

    // Feature: disccheck, Property 20: Pianificazione del Cache_Defeat coerente con cache_kb
    // **Validates: Requirements 12.6, 12.8**
    [Property(MaxTest = 100)]
    public bool LargeDisc_AlwaysYieldsArea(int? rawCacheKb, uint rawBlock)
    {
        int? cacheKb = rawCacheKb is null ? null : (int)((uint)rawCacheKb.Value % 131072u);
        var plan = CacheDefeatPlanner.Plan(rawBlock % 100_000u, 2_000_000, cacheKb);
        return plan.Area is not null && plan.Area.SectorCount * (long)CacheDefeatPlanner.SectorBytes >= ExpectedMinKb(cacheKb) * 1024;
    }
}
