using RimworldStatistics.NewModOutcomes;

namespace RimworldStatistics.Core.Tests;

public sealed class NewModOutcomeTests
{
    [Theory]
    [InlineData(3, 4, true)]
    [InlineData(3, 3, false)]
    [InlineData(3, 5, false)]
    [InlineData(-1, 0, false)]
    public void ExactlyOneWinnerWasAdded(int before, int after, bool expected)
        => Assert.Equal(expected, NewModOutcomeRules.WinnerAdded(before, after));

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void RoadRequiresBothDirectionalLinks(bool forward, bool reverse, bool expected)
        => Assert.Equal(expected, NewModOutcomeRules.RoadImproved(forward, reverse));
}
