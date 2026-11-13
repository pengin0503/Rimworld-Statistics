using RimworldStatistics.VqeDroneFactory;

namespace RimworldStatistics.Core.Tests;

public sealed class VqeDroneFactoryStatisticsTests
{
    [Theory]
    [InlineData(2701, 2700, true, true, true, true)]
    [InlineData(2700, 2700, true, true, true, false)]
    [InlineData(2701, 2700, false, true, true, false)]
    [InlineData(2701, 2700, true, false, true, false)]
    [InlineData(2701, 2700, true, true, false, false)]
    public void OnlyReadyAssemblerWithNewMatchingPlayerDroneCounts(
        int before, int limit, bool spawned, bool correctKind, bool player, bool expected)
        => Assert.Equal(expected, VqeDroneRules.AssemblerSuccess(
            before, limit, spawned, correctKind, player));

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, true, false, false)]
    public void HatchingRequiresConfirmedNewOwnedDrone(bool spawnSuccess, bool exists,
        bool owned, bool expected)
        => Assert.Equal(expected, VqeDroneRules.HatchSuccess(spawnSuccess, exists, owned));
}
