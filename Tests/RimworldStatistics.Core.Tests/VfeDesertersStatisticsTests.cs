using RimworldStatistics.VfeDeserters;

namespace RimworldStatistics.Core.Tests;

public sealed class VfeDesertersStatisticsTests
{
    [Theory]
    [InlineData(true, 10, 7, 3, 3)]
    [InlineData(true, 10, 8, 3, 2)]
    [InlineData(true, 10, 4, 3, 3)]
    [InlineData(true, 10, 10, 3, 0)]
    [InlineData(true, 10, 11, 3, 0)]
    [InlineData(true, 10, 7, 0, 0)]
    [InlineData(true, 10, 7, -1, 0)]
    [InlineData(false, 10, 7, 3, 0)]
    public void CountsActualDebitUpToTheAuthorizedCost(bool paymentAccepted,
        long before, long after, int cost, long expected)
        => Assert.Equal(expected, VfeDesertersRules.IntelPaid(paymentAccepted, before, after, cost));
}
