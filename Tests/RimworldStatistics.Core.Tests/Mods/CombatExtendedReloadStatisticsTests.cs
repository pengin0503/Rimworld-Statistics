using RimworldStatistics.CombatExtendedReload;

namespace RimworldStatistics.Core.Tests;

public sealed class CombatExtendedReloadStatisticsTests
{
    [Theory]
    [InlineData(15, 10, false, 10, true)]
    [InlineData(15, 0, true, 4, true)]
    [InlineData(15, 15, false, 12, false)]
    [InlineData(15, 15, true, 0, false)]
    [InlineData(0, 0, false, 10, false)]
    public void AmmoConsumptionMustBeConfirmedBeforeReloadCounts(
        int ammoBefore, int ammoAfter, bool ammoDestroyed, int roundsAfter, bool expected)
        => Assert.Equal(expected, CombatExtendedReloadRules.AmmoAdded(
            ammoBefore, ammoAfter, ammoDestroyed, roundsAfter));

    [Fact]
    public void SeveralSuccessfulLoadCallsDuringOneDoReloadCountOnce()
    {
        var record = new CombatExtendedReloadRules.ReloadCounter();
        record.Observe(true);
        record.Observe(true);
        record.Observe(false);
        Assert.True(record.CountOnce());
        Assert.False(record.CountOnce());
    }
}
