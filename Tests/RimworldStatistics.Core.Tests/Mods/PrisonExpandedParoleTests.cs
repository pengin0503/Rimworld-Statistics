using RimworldStatistics.PrisonExpandedParole;

namespace RimworldStatistics.Core.Tests;

public sealed class PrisonExpandedParoleTests
{
    [Theory]
    [InlineData(true, true, false, true, 0, true, true)]
    [InlineData(true, true, false, false, 0, true, false)]
    [InlineData(true, true, false, true, 1, true, false)]
    [InlineData(true, true, false, true, 0, false, false)]
    [InlineData(true, false, false, true, 0, true, false)]
    [InlineData(false, true, false, true, 0, true, false)]
    [InlineData(true, true, true, true, 0, true, false)]
    public void OnlyFirstConfirmedPrisonerParoleApprovalCounts(
        bool dueBefore, bool prisonerAliveInCustody, bool alreadyReleasing,
        bool dueCleared, int finalSentenceDays, bool releaseMode, bool expected)
        => Assert.Equal(expected, PrisonExpandedParoleRules.Approved(
            dueBefore, prisonerAliveInCustody, alreadyReleasing, dueCleared,
            finalSentenceDays, releaseMode));
}
