using RimworldStatistics.VfeInsectoids;

namespace RimworldStatistics.Core.Tests;

public sealed class VfeInsectoidsBirthTests
{
    [Theory]
    [InlineData(3, 4, true, true, true, true, true)]
    [InlineData(3, 4, false, true, true, true, false)]
    [InlineData(3, 4, true, false, true, true, false)]
    [InlineData(3, 4, true, true, false, true, false)]
    [InlineData(3, 4, true, true, true, false, false)]
    [InlineData(3, 3, true, true, true, true, false)]
    [InlineData(3, 5, true, true, true, true, false)]
    public void OnlyFreshSingleSpawnFromNormalHiveTickCounts(
        int before, int after, bool pawnSpawned, bool owner, bool sameMap,
        bool normalTickScope, bool expected)
        => Assert.Equal(expected, VfeInsectoidsRules.NewBirth(
            before, after, pawnSpawned, owner, sameMap, normalTickScope));
}
