using RimworldStatistics.VanillaTradingExpandedAddon;

namespace RimworldStatistics.Core.Tests;

public sealed class VteTradeMathTests
{
    [Theory]
    [InlineData(100, 135, true, 35)]
    [InlineData(100, 135, false, 0)]
    [InlineData(135, 100, false, 35)]
    [InlineData(135, 100, true, 0)]
    [InlineData(100, 100, true, 0)]
    public void SettledAccountDeltaCountsOnlySuccessfulDirection(int before, int after, bool credit, int expected) =>
        Assert.Equal(expected, VteTradeMath.SettledAmount(before, after, credit));

    [Theory]
    [InlineData(10, 7, 5, 3)]
    [InlineData(10, 0, 5, 5)]
    [InlineData(10, 11, 5, 0)]
    [InlineData(10, 7, 0, 0)]
    public void ContractDeliveryCountsOnlyActualConsumedItems(int before, int after, int requested, int expected) =>
        Assert.Equal(expected, VteTradeMath.FulfilledItems(before, after, requested));

    [Theory]
    [InlineData(200, 10, 5, 100)]
    [InlineData(200, 10, 10, 200)]
    [InlineData(200, 10, 0, 0)]
    [InlineData(200, 0, 5, 0)]
    public void RewardForCaravanDeliveryIsProportional(int reward, int requested, int delivered, int expected) =>
        Assert.Equal(expected, VteTradeMath.ProportionalReward(reward, requested, delivered));

    [Fact]
    public void EventIdsAreStableAcrossEquivalentCheckpointsAndDistinctAcrossRealTransfers()
    {
        var id = VteTradeMath.EventId("bank_1", "deposit", 500, 100, 140);
        Assert.Equal(id, VteTradeMath.EventId("bank_1", "deposit", 500, 100, 140));
        Assert.NotEqual(id, VteTradeMath.EventId("bank_1", "deposit", 500, 140, 180));
        Assert.NotEqual(id, VteTradeMath.EventId("bank_1", "withdraw", 500, 100, 140));
        Assert.NotEqual(id, VteTradeMath.EventId("bank_2", "deposit", 500, 100, 140));
    }
}
