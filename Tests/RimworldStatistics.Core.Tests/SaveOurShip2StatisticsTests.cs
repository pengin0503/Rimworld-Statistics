using RimworldStatistics.SaveOurShip2;

namespace RimworldStatistics.Core.Tests;

public sealed class SaveOurShip2StatisticsTests
{
    [Theory]
    [InlineData(true, true, true, true, true)]
    [InlineData(false, true, true, true, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, true, true, false, false)]
    public void OrbitArrivalRequiresPlayerTakeoffAndNominalDestination(bool takeoff, bool player,
        bool ascending, bool nominal, bool expected)
        => Assert.Equal(expected, SaveOurShip2Rules.ReachedOrbit(takeoff, player, ascending, nominal));
}
