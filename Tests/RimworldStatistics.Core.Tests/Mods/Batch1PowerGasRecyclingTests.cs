using RimworldStatistics.VfePower;
using RimworldStatistics.VheHelixienGas;
using RimworldStatistics.VreRecycling;

namespace RimworldStatistics.Core.Tests;

public sealed class Batch1PowerGasRecyclingTests
{
    [Theory]
    [InlineData(false, true, true, true, true, true)]
    [InlineData(true, true, true, true, true, false)]
    [InlineData(false, false, true, true, true, false)]
    [InlineData(false, true, false, true, true, false)]
    [InlineData(false, true, true, false, true, false)]
    [InlineData(false, true, true, true, false, false)]
    public void VfePowerNuclearMeltdownCountsOnlyNewCommittedBreakdown(
        bool wasSignaled,
        bool isSignaled,
        bool isBrokenDown,
        bool playerOwned,
        bool spawned,
        bool expected)
    {
        Assert.Equal(expected, VfePowerRules.MeltdownCommitted(
            wasSignaled, isSignaled, isBrokenDown, playerOwned, spawned));
    }

    [Theory]
    [InlineData(30f, true, true, 30d)]
    [InlineData(12.5f, true, true, 12.5d)]
    [InlineData(0f, true, true, 0d)]
    [InlineData(-5f, true, true, 0d)]
    [InlineData(float.NaN, true, true, 0d)]
    [InlineData(float.PositiveInfinity, true, true, 0d)]
    [InlineData(30f, false, true, 0d)]
    [InlineData(30f, true, false, 0d)]
    public void VheHelixienGasOnlyAcceptsFinitePositiveStoredPipeUnits(
        float stored,
        bool matchingPipe,
        bool playerOwned,
        double expected)
    {
        Assert.Equal(expected, VheHelixienGasRules.AcceptedUnits(stored, matchingPipe, playerOwned), 6);
    }

    [Theory]
    [InlineData(true, true, true, 5L, 0, 0, true, 5L)]
    [InlineData(true, true, true, 0L, 0, 0, true, 0L)]
    [InlineData(true, true, false, 99L, 10, 15, false, 5L)]
    [InlineData(true, true, false, 5L, 10, 10, false, 0L)]
    [InlineData(true, true, false, 5L, 10, 15, true, 0L)]
    [InlineData(false, true, true, 5L, 0, 0, true, 0L)]
    [InlineData(true, false, false, 5L, 10, 15, false, 0L)]
    public void VreRecyclingOutputHandlesGenPlaceAndDirectMergeWithoutDoubleCounting(
        bool spawned,
        bool playerOwned,
        bool tryPlaceCalled,
        long placedActionCount,
        int existingBeforeCount,
        int existingAfterCount,
        bool existingDestroyed,
        long expected)
    {
        Assert.Equal(expected, VreRecyclingRules.ConfirmedOutputCount(
            spawned, playerOwned, tryPlaceCalled, placedActionCount,
            existingBeforeCount, existingAfterCount, existingDestroyed));
    }
}
