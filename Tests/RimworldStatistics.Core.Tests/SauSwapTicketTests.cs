using RimworldStatistics.SmartApparelAddon;

namespace RimworldStatistics.Core.Tests;

public sealed class SauSwapTicketTests
{
    [Fact]
    public void PendingOrderSurvivesCheckpointAndRecordsOnlyOnceAfterSuccessfulWear()
    {
        var state = new FamilyState { Revision = 1, ThroughTick = 205 };
        var ticket = SauSwapTicket.Create("Pawn_15", "Thing_20", 51, 200, playerForced: false)!;
        Assert.True(ticket.Remember(state, 200));
        var checkpoint = new CheckpointPayload { CheckpointId = "p", SaveTick = 205, Families = { ["swaps"] = state } };
        var json = CheckpointJson.Serialize(checkpoint);
        Assert.DoesNotContain("Pawn_15", json);
        Assert.DoesNotContain("Thing_20", json);
        var resumed = LiveMetricAccumulator.Resume(json, [new FamilySpec("swaps", 1)], 205);
        var loaded = resumed.Family("swaps")!;
        Assert.Null(ticket.Complete(loaded, newlyWorn: false));
        Assert.Equal(SauOrderEventId.Create("Pawn_15", "Thing_20", 200), ticket.Complete(loaded, newlyWorn: true));
        Assert.Null(ticket.Complete(loaded, newlyWorn: true));
    }

    [Fact]
    public void RejectsManualOrdersAndOtherJobInstancesForTheSameApparel()
    {
        var state = new FamilyState();
        var ticket = SauSwapTicket.Create("Pawn_15", "Thing_20", 51, 200, playerForced: false)!;
        Assert.True(ticket.Remember(state, 200));
        Assert.Null(SauSwapTicket.Create("Pawn_15", "Thing_20", 51, 200, playerForced: true));
        var otherJob = SauSwapTicket.Create("Pawn_15", "Thing_20", 52, 200, playerForced: false)!;
        Assert.False(otherJob.Pending(state));
        Assert.True(ticket.Pending(state));
    }

    [Fact]
    public void AReplacementRemovesTheCanceledPendingOrder()
    {
        var state = new FamilyState();
        var oldTicket = SauSwapTicket.Create("Pawn_15", "Thing_20", 51, 200, playerForced: false)!;
        var newTicket = SauSwapTicket.Create("Pawn_15", "Thing_25", 52, 205, playerForced: false)!;
        Assert.True(oldTicket.Remember(state, 200));
        Assert.True(newTicket.Remember(state, 205));
        Assert.False(oldTicket.Pending(state));
        Assert.True(newTicket.Pending(state));
    }
}
