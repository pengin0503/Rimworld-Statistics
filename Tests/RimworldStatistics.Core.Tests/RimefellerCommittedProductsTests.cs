using RimworldStatistics.Core;
using RimworldStatistics.Rimefeller;

namespace RimworldStatistics.Core.Tests;

public sealed class RimefellerCommittedProductsTests
{
    [Theory]
    [InlineData(5, false, 0, false, false, 0, 5)]
    [InlineData(0, false, 0, false, false, 0, 0)]
    [InlineData(5, true, 0, false, false, 0, 0)]
    [InlineData(5, true, 3, true, false, 0, 3)]
    [InlineData(5, true, 2, false, false, 0, 2)]
    [InlineData(5, true, 0, true, true, 5, 5)]
    [InlineData(5, true, 0, true, false, 5, 0)]
    [InlineData(-2, false, 0, false, false, 0, 0)]
    public void CountOnlyPhysicallyCommittedRefineryOutput(
        int creditedToStack, bool attemptedPlacement, int placedCallbackCount,
        bool placementSucceeded, bool itemSpawned, int spawnedStack,
        int expected)
        => Assert.Equal(expected, RimefellerRefiningRules.DeliveredItems(
            creditedToStack, attemptedPlacement, placedCallbackCount,
            placementSucceeded, itemSpawned, spawnedStack));

    [Theory]
    [InlineData(10d, 8.25d, -2d, true, true, 1.75d)]
    [InlineData(10d, 10d, -2d, true, true, 0d)]
    [InlineData(10d, 12d, 2d, true, true, 0d)]
    [InlineData(10d, 8d, 2d, true, true, 0d)]
    [InlineData(10d, 8d, -2d, false, true, 0d)]
    [InlineData(10d, 8d, -2d, true, false, 0d)]
    [InlineData(10d, -1d, -2d, true, true, 0d)]
    [InlineData(double.NaN, 8d, -2d, true, true, 0d)]
    [InlineData(10d, double.PositiveInfinity, -2d, true, true, 0d)]
    public void CountOnlyObservedNetCrudeDepletion(
        double before, double after, double requested, bool playerOwned,
        bool oilStorage, double expected)
        => Assert.Equal(expected, RimefellerRefiningRules.NetCrudeDepleted(
            before, after, requested, playerOwned, oilStorage), 8);

    [Fact]
    public void RepeatedOrFailedPlacementDoesNotIncreaseStoredTotals()
    {
        var state = new FamilyState { Revision=1, SinceTick=0, ThroughTick=600, Continuous=true };
        var first = RimefellerRefiningRules.DeliveredItems(0, true, 4, true, false, 0);
        var failed = RimefellerRefiningRules.DeliveredItems(7, true, 0, false, false, 0);
        Assert.True(OptionalTransitionRecording.RecordCount(state, "products", first,
            "dubwise.rimefeller/Plasteel", withBreakdown:true));
        Assert.False(OptionalTransitionRecording.RecordCount(state, "products", failed,
            "dubwise.rimefeller/Plasteel", withBreakdown:true));
        Assert.Equal(4L, state.Peek("products")!.N!.Value);
        Assert.Equal(4d, state.Peek("products")!.By!["dubwise.rimefeller/Plasteel"]);
    }
}
