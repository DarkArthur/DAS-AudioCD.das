using DiscCheck.Core;
namespace DiscCheck.Core.Tests;

public class CacheDefeatPlannerTests
{
    [Theory]
    [InlineData(null, 4096)]
    [InlineData(0, 4096)]
    [InlineData(1024, 4096)]
    [InlineData(2048, 4096)]
    [InlineData(4096, 8192)]
    public void Area_kb_is_max_of_twice_cache_and_4096(int? cacheKb, int expected) =>
        Assert.Equal(expected, CacheDefeatPlanner.AreaKb(cacheKb));

    [Fact]
    public void Area_covers_at_least_required_bytes()
    {
        Assert.True((long)CacheDefeatPlanner.AreaSectors(null) * CacheDefeatPlanner.SectorBytes >= 4096L * 1024);
    }

    [Fact]
    public void Plans_forward_area_28500_sectors_from_block()
    {
        var plan = CacheDefeatPlanner.Plan(1000, 300_000, null);
        Assert.False(plan.IsLimited);
        Assert.Equal(29_500, plan.Area!.StartLba);
        Assert.Equal(0, plan.MissingSectors);
    }

    [Fact]
    public void Falls_back_to_area_before_block()
    {
        var plan = CacheDefeatPlanner.Plan(250_000, 252_000, 512);
        Assert.False(plan.IsLimited);
        Assert.Equal(221_500, plan.Area!.StartLba);
    }

    [Fact]
    public void Reports_missing_sectors_when_disc_too_small()
    {
        int count = CacheDefeatPlanner.AreaSectors(null);
        var plan = CacheDefeatPlanner.Plan(1000, 30_000, null);
        Assert.True(plan.IsLimited);
        Assert.Equal(29_500 + count - 30_000, plan.MissingSectors);
    }
}
