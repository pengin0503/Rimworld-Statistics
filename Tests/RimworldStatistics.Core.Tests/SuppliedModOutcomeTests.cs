using RimworldStatistics.NewModOutcomes;

namespace RimworldStatistics.Core.Tests;

public sealed class SuppliedModOutcomeTests
{
    [Theory]
    [InlineData(3, 4, true)]
    [InlineData(3, 3, false)]
    [InlineData(3, 5, false)]
    [InlineData(0, 1, false)]
    [InlineData(int.MaxValue, int.MinValue, false)]
    public void SemesterMustAdvanceExactlyOnce(int before, int after, bool expected)
        => Assert.Equal(expected, SuppliedModOutcomeRules.AdvancedSemester(before, after));

    [Theory]
    [InlineData(true, true, 90, true)]
    [InlineData(false, true, 90, false)]
    [InlineData(true, false, 90, false)]
    [InlineData(true, true, 0, false)]
    public void SchoolEventRequiresFinalizedCompletionAndActualActivity(
        bool finalized, bool completed, int ticks, bool expected)
        => Assert.Equal(expected, SuppliedModOutcomeRules.CompletedEvent(finalized, completed, ticks));
}
