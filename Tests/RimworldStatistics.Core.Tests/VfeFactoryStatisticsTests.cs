using RimworldStatistics.VfeFactory;
using RimworldStatistics.Core;

namespace RimworldStatistics.Core.Tests;

public sealed class VfeFactoryStatisticsTests
{
    [Theory]
    [InlineData(0, 6, 6)]
    [InlineData(2, 8, 6)]
    [InlineData(4, 4, 0)]
    [InlineData(4, 2, 0)]
    public void BufferedHarvestCountsOnlyNewItemsCreatedInRow(
        int before, int after, long expected)
        => Assert.Equal(expected, VfeFactoryRules.BufferYield(before, after));

    [Theory]
    [InlineData(true, true, true, true, true)]
    [InlineData(false, true, true, true, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, true, true, false, false)]
    public void NewlySownMustBeSpawnedAndOwnedInRow(
        bool newPlant, bool sown, bool kindMatch, bool owned, bool expected)
        => Assert.Equal(expected, VfeFactoryRules.Sown(
            newPlant, sown, kindMatch, owned));
}
