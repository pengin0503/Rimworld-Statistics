namespace RimworldStatistics.Core.Tests;

public sealed class OutpostProductionTests
{
    [Fact]
    public void OriginalProductionSequenceRunsOnceAndPositiveStacksAreCounted()
    {
        var enumerations = 0;
        IEnumerable<(string, int)> Products()
        {
            enumerations++;
            yield return ("Wood", 5);
            yield return ("Wood", 3);
            yield return ("Steel", 0);
        }

        var delivery = OutpostProduction.Capture(Products(), item => item);
        Assert.Equal(3, delivery.Items.Count);
        Assert.Equal(1, enumerations);
        var state = new FamilyState();
        delivery.Record(state, succeeded: false);
        delivery.Record(state, succeeded: true);
        delivery.Record(state, succeeded: true);
        var results = new Dictionary<string, MetricEntry>();
        OutpostProduction.Family.Build(new FamilyOutputContext(state, 0, results));
        Assert.Equal(1, results[OutpostProduction.Deliveries].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(8, results[OutpostProduction.ByDef].Values["Wood"].IntegerValue);
        Assert.DoesNotContain("Steel", results[OutpostProduction.ByDef].Values.Keys);
    }

    [Fact]
    public void SnapshotKeepsExistingActiveCountAndOnlyReadsKnownPawnCounts()
    {
        var entries = OutpostSnapshot.Build([new("Mining", 3), new("Mining", 4), new("Farming", null)]);
        Assert.Equal(3, entries[OutpostSnapshot.Active].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(7, entries[OutpostSnapshot.Pawns].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(MetricCoverage.Partial, entries[OutpostSnapshot.Pawns].Values[MetricEntry.Scalar].Coverage);
        Assert.Equal(2, entries[OutpostSnapshot.ByType].Values["Mining"].IntegerValue);
    }
}
