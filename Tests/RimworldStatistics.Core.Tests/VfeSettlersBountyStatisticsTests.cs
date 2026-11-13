using RimworldStatistics.VfeSettlers;

namespace RimworldStatistics.Core.Tests;

public sealed class VfeSettlersBountyStatisticsTests
{
    [Theory]
    [InlineData(true, true, true, 30, 30, 30)]
    [InlineData(true, true, true, 30, 12, 12)]
    [InlineData(true, true, true, 30, 0, 0)]
    [InlineData(false, true, true, 30, 30, 0)]
    [InlineData(true, false, true, 30, 30, 0)]
    [InlineData(true, true, false, 30, 30, 0)]
    [InlineData(true, true, true, -1, 30, 0)]
    [InlineData(true, true, true, 30, -1, 0)]
    public void AwardAmountRequiresCriminalRemovedAndFirstRewardSilverAccepted(
        bool previouslyPresent, bool removed, bool playerCaravan, int requestedReward,
        int observedAddedSilver, long expected)
        => Assert.Equal(expected, VfeSettlersBountyRules.RewardReceived(previouslyPresent,
            removed, playerCaravan, requestedReward, observedAddedSilver));

    [Fact]
    public void TransferOfCriminalsBelongingsIsNotSecondReward()
    {
        var scope = new VfeSettlersBountyRules.FirstTransfer();
        Assert.True(scope.ConsumeFirst());
        Assert.False(scope.ConsumeFirst());
    }
}
