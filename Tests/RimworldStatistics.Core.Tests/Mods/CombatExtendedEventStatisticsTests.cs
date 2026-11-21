using RimworldStatistics.CombatExtendedEvents;

namespace RimworldStatistics.Core.Tests;

public sealed class CombatExtendedEventStatisticsTests
{
    [Theory]
    [InlineData(12, 10, true, true, 2)]
    [InlineData(5, 0, true, true, 5)]
    [InlineData(5, 5, true, true, 0)]
    [InlineData(5, 8, true, true, 0)]
    [InlineData(10, 8, true, false, 0)]
    [InlineData(10, 8, false, true, 0)]
    public void MagazineAmmoConsumptionCountsExactPositiveDebit(
        int before, int after, bool player, bool ammoEnabled, long expected)
        => Assert.Equal(expected, CombatExtendedEventRules.MagazineRoundsUsed(
            before, after, player, ammoEnabled));

    [Theory]
    [InlineData(false, true, true, true, 1)]
    [InlineData(true, true, true, true, 0)]
    [InlineData(false, false, true, true, 0)]
    [InlineData(false, true, false, true, 0)]
    [InlineData(false, true, true, false, 0)]
    public void MaglessDirectConsumptionRequiresDestroyedReservedAmmo(
        bool oldDestroyed, bool nowDestroyed, bool player, bool ammoEnabled, long expected)
        => Assert.Equal(expected, CombatExtendedEventRules.DirectRoundUsed(
            oldDestroyed, nowDestroyed, player, ammoEnabled));

    [Theory]
    [InlineData(false, true, true, false, true)]
    [InlineData(true, true, true, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, true, true, true, false)]
    public void SuppressionOnlyCountsFirstNonDebugPlayerThresholdTransition(
        bool wasSuppressed, bool nowSuppressed, bool player, bool debugScope, bool expected)
        => Assert.Equal(expected, CombatExtendedEventRules.Suppressed(
            wasSuppressed, nowSuppressed, player, debugScope));
}
