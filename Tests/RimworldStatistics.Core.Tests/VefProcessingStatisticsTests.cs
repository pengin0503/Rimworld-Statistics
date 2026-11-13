using RimworldStatistics.VefProcessing;

namespace RimworldStatistics.Core.Tests;

public sealed class VefProcessingStatisticsTests
{
    [Theory]
    [InlineData(2, 3, true, true, true)]
    [InlineData(2, 2, true, true, false)]
    [InlineData(2, 3, false, true, false)]
    [InlineData(2, 3, true, false, false)]
    [InlineData(2, 4, true, true, false)]
    public void OnlySingleCompletedBatchWithMatchingMachineAndProcessOwnerCounts(
        int before, int after, bool ownerMatches, bool player, bool expected)
        => Assert.Equal(expected, VefProcessingRules.CompleteBatch(
            before, after, ownerMatches, player));

    [Fact]
    public void CookingAndBrewingHaveIndependentNativeFamilies()
    {
        Assert.NotEqual(VefProcessingRules.CookingUnit, VefProcessingRules.BrewingUnit);
        var cooking = new FamilyState { Revision = 1, SinceTick = 0, ThroughTick = 100, Continuous = true };
        var brewing = new FamilyState { Revision = 1, SinceTick = 0, ThroughTick = 100, Continuous = true };
        OptionalTransitionRecording.RecordCount(cooking, "batches", key: "cooking/ProcessA", withBreakdown: true);
        OptionalTransitionRecording.RecordCount(brewing, "batches", key: "brewing/ProcessB", withBreakdown: true);
        Assert.Equal(1L, cooking.Peek("batches")!.N);
        Assert.Equal(1L, brewing.Peek("batches")!.N);
    }
}
