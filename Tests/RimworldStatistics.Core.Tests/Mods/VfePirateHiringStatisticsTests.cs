using RimworldStatistics.VfePirateHiring;

namespace RimworldStatistics.Core.Tests;

public sealed class VfePirateHiringStatisticsTests
{
    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    public void ContractCountsOnlyCommittedNewPirateHire(
        bool pirateFaction, bool newPawnList, bool newPawnCountPositive,
        bool expected)
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
