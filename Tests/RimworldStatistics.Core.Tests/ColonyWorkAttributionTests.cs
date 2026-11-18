using Xunit;

namespace RimworldStatistics.Core.Tests;

public sealed class ColonyWorkAttributionTests
{
    [Theory]
    [InlineData(true, false, false, true)]   // Free colonist / colony slave / colony mech / colony animal
    [InlineData(true, false, true, true)]    // Player-faction non-prisoner with player host faction
    [InlineData(false, true, true, true)]    // Colony prisoner performing labor (e.g. Prison Labor)
    [InlineData(true, true, true, true)]     // Imprisoned own colonist held in colony custody
    [InlineData(true, true, false, false)]   // Captured colonist held prisoner by a hostile faction
    [InlineData(false, true, false, false)]  // Non-player prisoner held by another faction
    [InlineData(false, false, true, false)]  // Visiting allied guest (HostFaction == player, GuestStatus != Prisoner)
    [InlineData(false, false, false, false)] // Raider / trader / wild pawn
    public void IsColonyWorker_ClassifiesPrisonerGuestAndPlayerLaborWithoutOverlap(
        bool isPlayerFaction,
        bool isPrisoner,
        bool isHostPlayerFaction,
        bool expected)
    {
        Assert.Equal(
            expected,
            ColonyWorkAttribution.IsColonyWorker(isPlayerFaction, isPrisoner, isHostPlayerFaction));
    }
}
