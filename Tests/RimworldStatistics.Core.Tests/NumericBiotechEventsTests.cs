namespace RimworldStatistics.Core.Tests;

/// <summary>Biotech Core never infers completed events from mere job starts, loaded pawns or item loss.</summary>
public sealed class NumericBiotechEventsTests
{
    private static MetricObservation Save(LiveMetricAccumulator a)
    {
        var d = a.PrepareSave(60000, DateTime.UtcNow, "btech")!;
        return CheckpointMetrics.ToObservation(d.Payload!, d.Json, new ColonyIdentity { ModCampaignId = "btech" }, null, null);
    }

    [Fact]
    public void UniqueBirthMothersProducedMechsAndGrowthMilestones()
    {
        var a = LiveMetricAccumulator.StartNew(new[] { BiotechFamily.Family.Spec }, 0);
        var s = a.Family(BiotechFamily.Name)!;
        BiotechFamily.MechProduced(s, "m1", true, true);
        BiotechFamily.MechProduced(s, "m1", true, true);
        BiotechFamily.MechProduced(s, "m2", false, true);
        BiotechFamily.BirthConfirmed(s, "mom1", true, true);
        BiotechFamily.BirthConfirmed(s, "mom1", true, true);
        BiotechFamily.BirthConfirmed(s, "mom2", false, true);
        BiotechFamily.ChildReachedStage(s, "kid", "Child", true, true);
        BiotechFamily.ChildReachedStage(s, "kid", "Child", true, true);
        BiotechFamily.ChildReachedStage(s, "kid", "Adult", true, true);
        BiotechFamily.ChildReachedStage(s, "npc", "Child", false, true);
        var o = Save(a);
        Assert.Equal(1, o.Scalar(BiotechFamily.MechsCreated, MetricKeys.Revision(BiotechFamily.MechsCreated))!.IntegerValue);
        Assert.Equal(1, o.Scalar(BiotechFamily.BirthMothers, MetricKeys.Revision(BiotechFamily.BirthMothers))!.IntegerValue);
        Assert.Equal(2, o.Scalar(BiotechFamily.ChildStages, MetricKeys.Revision(BiotechFamily.ChildStages))!.IntegerValue);
        // The catalog promises a stage breakdown, not just the scalar count.
        var stages = o.Metrics[BiotechFamily.ChildStages].Values;
        Assert.Equal(1, stages["Child"].IntegerValue);
        Assert.Equal(1, stages["Adult"].IntegerValue);
        Assert.Equal(2, stages[MetricEntry.Scalar].IntegerValue);
    }

    [Fact]
    public void RepairDisposalAndCleanupRequireCompletionEvidence()
    {
        var a = LiveMetricAccumulator.StartNew(new[] { BiotechFamily.Family.Spec }, 0);
        var s = a.Family(BiotechFamily.Name)!;
        BiotechFamily.MechFullyRepaired(s, "op1", true, false);
        BiotechFamily.MechFullyRepaired(s, "op1", true, true);
        BiotechFamily.MechFullyRepaired(s, "op1", true, true);
        BiotechFamily.WastePackProduced(s, 10, true);
        BiotechFamily.WastePackProduced(s, 100, false);
        BiotechFamily.WastePackNeutralized(s, 2, false);
        BiotechFamily.WastePackNeutralized(s, 3, true);
        BiotechFamily.PollutionCellCleaned(s, "clean1", true, true, false, true);
        BiotechFamily.PollutionCellCleaned(s, "clean1", true, true, false, true);
        BiotechFamily.PollutionCellCleaned(s, "clean2", true, false, false, true);
        BiotechFamily.PollutionCellCleaned(s, "clean3", false, true, false, true);
        var o = Save(a);
        Assert.Equal(1, o.Scalar(BiotechFamily.MechRepairs, MetricKeys.Revision(BiotechFamily.MechRepairs))!.IntegerValue);
        Assert.Equal(10, o.Scalar(BiotechFamily.WasteCreated, MetricKeys.Revision(BiotechFamily.WasteCreated))!.IntegerValue);
        Assert.Equal(3, o.Scalar(BiotechFamily.WasteDisposed, MetricKeys.Revision(BiotechFamily.WasteDisposed))!.IntegerValue);
        Assert.Equal(1, o.Scalar(BiotechFamily.PollutionCleaned, MetricKeys.Revision(BiotechFamily.PollutionCleaned))!.IntegerValue);
    }

    /// <summary>
    /// A model of 1.7.5011 CompWasteProducer.ProduceWaste: ceil(amount ÷ 5) new stacks of at most 5, each TryAdd merging into
    /// an existing non-full stack first and stopping at the first refusal. Waste is the first wastepack of the container.
    /// </summary>
    private static void ProduceWaste(List<(bool Wastepack, int Count)> held, int amount, int capacity)
    {
        const int stackLimit = 5;
        var stacks = (int)Math.Ceiling(amount / (double)stackLimit);
        for (var i = 0; i < stacks; i++)
        {
            var count = Math.Min(amount, stackLimit);
            var room = capacity - BiotechFamily.Wastepacks(held);
            if (room < count)
                break;
            amount -= count;
            for (var j = 0; j < held.Count && count > 0; j++)
            {
                if (!held[j].Wastepack || held[j].Count >= stackLimit)
                    continue;
                var moved = Math.Min(count, stackLimit - held[j].Count);
                held[j] = (true, held[j].Count + moved);
                count -= moved;
            }
            if (count > 0)
                held.Add((true, count));
        }
    }

    private static int First(List<(bool Wastepack, int Count)> held) => held.FirstOrDefault(t => t.Wastepack).Count;

    [Theory]
    [InlineData("first stack already full", new[] { 5 }, 5, 20, 5, 0)]
    [InlineData("one call makes several stacks", new int[0], 12, 20, 12, 5)]
    [InlineData("later stack grows", new[] { 5, 2 }, 3, 20, 3, 0)]
    [InlineData("container refuses", new[] { 5, 5 }, 5, 10, 0, 0)]
    public void ProducedWaste_IsTheIncreaseOfEveryWastepackStack(string kind, int[] before, int amount, int capacity,
        int expectedProduced, int firstStackDelta)
    {
        _ = kind;
        var held = before.Select(n => (true, n)).Prepend((false, 1)).ToList();   // a non-wastepack thing is never counted
        var (total, first) = (BiotechFamily.Wastepacks(held), First(held));
        ProduceWaste(held, amount, capacity);
        Assert.Equal(expectedProduced, BiotechFamily.Wastepacks(held) - total);
        Assert.Equal(firstStackDelta, First(held) - first);   // what the first-stack reading saw (#323)
    }

    [Fact]
    public void HeldWastepacks_DoNotDependOnStackOrder()
    {
        var held = new List<(bool, int)> { (true, 5), (true, 2), (false, 9) };
        var reordered = new List<(bool, int)> { (true, 2), (false, 9), (true, 5) };
        Assert.Equal(7, BiotechFamily.Wastepacks(held));
        Assert.Equal(BiotechFamily.Wastepacks(held), BiotechFamily.Wastepacks(reordered));
    }

    [Fact]
    public void BiotechEventOutputsStayDlcGatedAndUnverified()
    {
        var d = MetricRegistry.Find(BiotechFamily.PollutionCleaned)!;
        Assert.Equal(AcquisitionUnits.Biotech, d.RequiredDlc);
        Assert.False(d.Published);
    }
}
