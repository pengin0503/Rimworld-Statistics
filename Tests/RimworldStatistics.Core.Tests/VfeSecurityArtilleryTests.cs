using RimworldStatistics.VfeSecurityArtillery;
using RimworldStatistics.Core;

namespace RimworldStatistics.Core.Tests;

public sealed class VfeSecurityArtilleryTests
{
    [Theory]
    [InlineData(0, 1, true, true, true)]
    [InlineData(3, 4, true, true, true)]
    [InlineData(3, 3, true, true, false)]
    [InlineData(3, 4, false, true, false)]
    [InlineData(3, 4, true, false, false)]
    [InlineData(3, 5, true, true, false)]
    public void OffMapBombardmentsCountConfirmedProjectileLaunchIntoDestination(
        int before, int after, bool playerLauncher, bool differentMap, bool expected)
        => Assert.Equal(expected, VfeSecurityArtilleryRules.LaunchedAtTarget(
            before, after, playerLauncher, differentMap));
}
