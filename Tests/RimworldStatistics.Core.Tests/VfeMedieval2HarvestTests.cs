using RimworldStatistics.VfeMedieval2;
using RimworldStatistics.Core;

namespace RimworldStatistics.Core.Tests;

public sealed class VfeMedieval2HarvestTests
{
    [Theory]
    [InlineData(6, true, true, 6)]
    [InlineData(4, true, true, 4)]
    [InlineData(3, true, true, 3)]
    [InlineData(0, true, true, 0)]
    [InlineData(-1, true, true, 0)]
    [InlineData(6, false, true, 0)]
    [InlineData(6, true, false, 0)]
    public void HoneyHarvestCountsOnlyActualPlacedCallbackUnits(
        int placed, bool playerOwned, bool matchingApiary, long expected)
        => Assert.Equal(expected,
            VfeMedieval2Rules.HoneyPlaced(placed, playerOwned, matchingApiary));

    [Fact]
    public void TwoPartialHoneyPlacementsSumToActualOutput()
    {
        var family = new FamilyState {Revision=1, SinceTick=0, ThroughTick=100, Continuous=true};
        OptionalTransitionRecording.RecordCount(family,"honey",4,key:"honey.owner/Honey",withBreakdown:true);
        OptionalTransitionRecording.RecordCount(family,"honey",2,key:"honey.owner/Honey",withBreakdown:true);
        Assert.Equal(6, family.Peek("honey")!.N);
    }
}
