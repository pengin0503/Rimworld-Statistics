using RimworldStatistics.VqeAncients;

namespace RimworldStatistics.Core.Tests;

public sealed class VqeAncientsInjectorTests
{
    [Theory]
    [InlineData(true, true, true, true, true)]
    [InlineData(true, false, true, true, false)]
    [InlineData(false, true, true, true, false)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, true, true, false, false)]
    public void RejectedInjectionNeedsRealEjectedComatosePlayer(
        bool player, bool released, bool coma, bool rejectedOutcome, bool expected)
        => Assert.Equal(expected, VqeAncientsRules.Rejected(player, released, coma, rejectedOutcome));

    [Theory]
    [InlineData(true, true, true, true, true)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(false, true, true, true, false)]
    [InlineData(true, true, true, false, false)]
    public void MutationNeedsOldPawnDestroyedAndNewMatchingPawnSpawned(
        bool owned, bool oldGone, bool newSpawn, bool kindMatch, bool expected)
        => Assert.Equal(expected, VqeAncientsRules.Mutated(owned, oldGone, newSpawn, kindMatch));
}
