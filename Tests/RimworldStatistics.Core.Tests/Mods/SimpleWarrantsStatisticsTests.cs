using RimworldStatistics.SimpleWarrants;

namespace RimworldStatistics.Core.Tests;

public sealed class SimpleWarrantsStatisticsTests
{
    [Theory]
    [InlineData(true, true, true, 75, 75)]
    [InlineData(true, true, true, 0, 0)]
    [InlineData(true, true, true, -5, 0)]
    [InlineData(false, true, true, 75, null)]
    [InlineData(true, false, true, 75, null)]
    [InlineData(true, true, false, 75, null)]
    public void SettlementRequiresCommitAndCountsActualSilver(bool completed, bool removed,
        bool destroyed, long received, int? expected)
        => Assert.Equal((long?)expected, SimpleWarrantsRules.SettledSilver(completed, removed, destroyed, received));
}
