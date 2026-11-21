using RimworldStatistics.VaeAspirations;

namespace RimworldStatistics.Core.Tests;

public sealed class VaeAspirationsStatisticsTests
{
    [Theory]
    [InlineData(true, false, true, false, true)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(true, false, true, true, false)]
    public void OnlyAssignedNotPreviouslyCompletedNormalAspirationsAreCandidates(
        bool assigned, bool alreadyCompleted, bool normalCompletionScope,
        bool devMode, bool expected)
        => Assert.Equal(expected, VaeAspirationsRules.CanComplete(
            assigned, alreadyCompleted, normalCompletionScope, devMode));

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void SuccessfulCompletionNeedsStateChange(
        bool wasCompleted, bool isCompleted, bool expected)
        => Assert.Equal(expected,
            VaeAspirationsRules.Completed(wasCompleted, isCompleted));
}
