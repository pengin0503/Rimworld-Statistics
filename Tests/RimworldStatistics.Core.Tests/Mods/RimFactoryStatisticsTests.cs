using RimworldStatistics.RimFactory;

namespace RimworldStatistics.Core.Tests;

public sealed class RimFactoryStatisticsTests
{
    [Theory]
    [InlineData(10, 4, 4)]
    [InlineData(10, 12, 10)]
    [InlineData(10, 0, 0)]
    [InlineData(10, -1, 0)]
    [InlineData(0, 5, 0)]
    public void GroundOutputCountsOnlyConfirmedQuantityUpToTheInputStack(int startingStack,
        long confirmed, long expected)
        => Assert.Equal(expected, RimFactoryRules.PlacedQuantity(startingStack, confirmed));
}
