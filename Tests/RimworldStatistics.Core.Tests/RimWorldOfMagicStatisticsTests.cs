using RimworldStatistics.RimWorldOfMagic;

namespace RimworldStatistics.Core.Tests;

public sealed class RimWorldOfMagicStatisticsTests
{
    [Theory]
    [InlineData(true, 5f, 3f, 2, 2, true)]
    [InlineData(true, 5f, 5f, 2, 1, true)]
    [InlineData(true, 5f, 5f, 2, 2, false)]
    [InlineData(true, 5f, 6f, 2, 2, false)]
    [InlineData(false, 5f, 3f, 2, 1, false)]
    public void HealingRequiresARealChangeAfterTheSpell(bool result, float before, float after,
        int freshBefore, int freshAfter, bool expected)
        => Assert.Equal(expected, RimWorldOfMagicRules.HealingCommitted(result, before, after, freshBefore, freshAfter));
}
