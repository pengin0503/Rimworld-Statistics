using RimworldStatistics.VfePirates;

namespace RimworldStatistics.Core.Tests;

public sealed class VfePiratesWarcasketTests
{
    [Theory]
    [InlineData(true, true, true, true, true, true)]
    [InlineData(false, true, true, true, true, false)]
    [InlineData(true, false, true, true, true, false)]
    [InlineData(true, true, false, true, true, false)]
    [InlineData(true, true, true, false, true, false)]
    [InlineData(true, true, true, true, false, false)]
    public void OnlyCompletedPlayerWeldingWithAllThreeNewWornPiecesCounts(
        bool owned, bool jobCompleted, bool armor, bool helmet, bool shoulders, bool expected)
        => Assert.Equal(expected, VfePiratesRules.Fitted(
            owned, jobCompleted, armor, helmet, shoulders));
}
