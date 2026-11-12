using RimworldStatistics.HospitalityAddon;

namespace RimworldStatistics.Core.Tests;

public sealed class HospitalityDeparturePolicyTests
{
    private static VisitExitEvidence Valid => new(
        VisitFinished: true, SawActualExit: true, SentAway: false,
        Hostile: false, RecoveredVisitor: false, OtherLossOccurred: false,
        ViolentLosses: 0, RemainingPawns: 0, PlayerHome: true);

    [Fact]
    public void PeacefulGroupWhoseMembersActuallyExitedCanComplete()
        => Assert.True(HospitalityDeparturePolicy.ShouldRecord(Valid));

    [Theory]
    [InlineData("no visit completion")]
    [InlineData("no observed exit")]
    [InlineData("sent away")]
    [InlineData("became hostile")]
    [InlineData("recovered visitor")]
    [InlineData("recruited or captured")]
    [InlineData("violently lost")]
    [InlineData("pawns remain")]
    [InlineData("outside player home")]
    public void IncompleteOrAmbiguousVisitDoesNotProduceAFinishedEvent(string condition)
    {
        var evidence = condition switch
        {
            "no visit completion" => Valid with { VisitFinished = false },
            "no observed exit" => Valid with { SawActualExit = false },
            "sent away" => Valid with { SentAway = true },
            "became hostile" => Valid with { Hostile = true },
            "recovered visitor" => Valid with { RecoveredVisitor = true },
            "recruited or captured" => Valid with { OtherLossOccurred = true },
            "violently lost" => Valid with { ViolentLosses = 1 },
            "pawns remain" => Valid with { RemainingPawns = 1 },
            _ => Valid with { PlayerHome = false },
        };
        Assert.False(HospitalityDeparturePolicy.ShouldRecord(evidence));
    }

    [Fact]
    public void AReloadedHalfFinishedVisitWithoutInProcessExitProofCannotBeInferred()
        => Assert.False(HospitalityDeparturePolicy.ShouldRecord(
            Valid with { SawActualExit = false }));
}
