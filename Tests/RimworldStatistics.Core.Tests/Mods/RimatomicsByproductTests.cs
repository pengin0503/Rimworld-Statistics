using RimworldStatistics.RimatomicsByproducts;

namespace RimworldStatistics.Core.Tests;

public sealed class RimatomicsByproductTests
{
    [Theory]
    [InlineData(3, true, true, true, 3)]
    [InlineData(2, true, true, true, 2)]
    [InlineData(1, false, true, true, 0)]
    [InlineData(3, true, false, true, 0)]
    [InlineData(3, true, true, false, 0)]
    [InlineData(-1, true, true, true, 0)]
    [InlineData(0, true, true, true, 0)]
    public void ByproductCountOnlyMeasuredScopedPlacement(
        int placed, bool player, bool processor, bool normalUnload, long expected)
        => Assert.Equal(expected, RimatomicsByproductRules.Placed(
            placed, player, processor, normalUnload));
}
