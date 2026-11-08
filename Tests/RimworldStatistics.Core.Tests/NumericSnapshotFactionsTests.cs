namespace RimworldStatistics.Core.Tests;

/// <summary>Phase B: relation direction, neutral, permanent hostility and identity boundaries.</summary>
public sealed class NumericSnapshotFactionsTests
{
    [Fact]
    public void CountsOnlyActiveFactionsFromPlayerPerspective()
    {
        var entries = FactionStateSnapshot.Build(new[]
        {
            new FactionStateFact("A", true, true, false, 60),
            new FactionStateFact("B", true, false, true, -50),
            new FactionStateFact("C", true, false, true, null), // permanent enemy
            new FactionStateFact("D", true, false, false, 10), // neutral still affects goodwill range
            new FactionStateFact("E", false, true, false, 100), // hidden
        });
        var r = entries[FactionStateSnapshot.Relations].Values;
        Assert.Equal(3, r[""].IntegerValue);
        Assert.Equal(1, r["ally"].IntegerValue);
        Assert.Equal(2, r["hostile"].IntegerValue);
        var goodwill = entries[FactionStateSnapshot.GoodwillRange].Values;
        Assert.Equal(-50, goodwill["min"].IntegerValue);
        Assert.Equal(60, goodwill["max"].IntegerValue);
        Assert.Equal(MetricCoverage.NotApplicable, goodwill[""].Coverage);
    }

    [Fact]
    public void DuplicateIdentityMarksOnlyTheSnapshotPartial()
    {
        var e = FactionStateSnapshot.Build(new[]
        {
            new FactionStateFact("Faction_1", true, true, false, 20),
            new FactionStateFact("Faction_1", true, false, true, -100),
        });
        Assert.Equal(1, e[FactionStateSnapshot.Relations].Values[""].IntegerValue);
        Assert.Equal(MetricCoverage.Partial, e[FactionStateSnapshot.Relations].Values[""].Coverage);
    }

    [Fact]
    public void NoActiveFactionsHasKnownZeroButNoGoodwillRange()
    {
        var e = FactionStateSnapshot.Build(Array.Empty<FactionStateFact>());
        Assert.Equal(0, e[FactionStateSnapshot.Relations].Values[""].IntegerValue);
        Assert.Single(e[FactionStateSnapshot.GoodwillRange].Values);
        Assert.False(MetricRegistry.Find(FactionStateSnapshot.Relations)!.Published);
    }
}
