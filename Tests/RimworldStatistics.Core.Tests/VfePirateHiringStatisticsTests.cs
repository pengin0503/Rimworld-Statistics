using RimworldStatistics.VfePirateHiring;

namespace RimworldStatistics.Core.Tests;

public sealed class VfePirateHiringStatisticsTests
{
    [Theory]
    [InlineData(true, true, true, 300, true)]
    [InlineData(true, true, true, 0, true)]
    [InlineData(true, true, false, 300, false)]
    [InlineData(true, false, true, 300, false)]
    [InlineData(false, true, true, 300, false)]
    public void ContractCountsOnlyCommittedNewPirateHire(
        bool pirateFaction, bool newPawnList, bool newPawnCountPositive,
        int paid, bool expected)
        => Assert.Equal(expected, VfePirateHiringRules.ContractCreated(
            pirateFaction, newPawnList, newPawnCountPositive));

    [Theory]
    [InlineData(200, 125, true, 75)]
    [InlineData(150, 150, true, 0)]
    [InlineData(150, 200, true, 0)]
    [InlineData(150, 50, false, 0)]
    public void RecordedSilverIsActualDestroyedStackQuantityNotNominalPrice(
        int before, int after, bool contract, long expected)
        => Assert.Equal(expected, VfePirateHiringRules.SilverPaid(
            before, after, contract));
}
