using RimworldStatistics.Gastronomy;

namespace RimworldStatistics.Core.Tests;

public sealed class GastronomyPaymentStatisticsTests
{
    [Theory]
    [InlineData(10, 4, true, 4)]
    [InlineData(10, 6, true, 6)]
    [InlineData(10, 0, true, 0)]
    [InlineData(10, 4, false, 0)]
    [InlineData(0, 4, true, 0)]
    [InlineData(10, -2, true, 0)]
    public void RestaurantSilverCountsActualPositiveDebtTransferOnly(
        int debtBefore, int transfer, bool playerRestaurant, long expected)
        => Assert.Equal(expected, GastronomyPaymentRules.Collected(
            debtBefore, transfer, playerRestaurant));
}
