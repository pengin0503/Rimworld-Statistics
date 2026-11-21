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
}
