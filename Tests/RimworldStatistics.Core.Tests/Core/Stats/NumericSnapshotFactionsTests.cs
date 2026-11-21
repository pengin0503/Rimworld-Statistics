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

    private static readonly DateTime Now = new(2026, 11, 17, 0, 0, 0, DateTimeKind.Utc);

    private static (NumericView View, NumericColony Colony) ViewOf(IEnumerable<FactionStateFact> facts)
    {
        var id = new ColonyIdentity
        {
            SeedString = "s", PersistentRandomValue = 1, StartingTile = "1,0", GameStartAbsTick = 0, FirstWealthBits = 7,
            StartingPawnIds = new List<string> { "Thing_Human1" }, ModCampaignId = "goodwill",
        };
        var p = new Profile();
        p.SaveObservations.Add(new SaveObservation
        {
            FileName = "g.rws", FileSize = 1, LastWriteUtc = Now, ReaderVersion = SaveReader.Version,
            Status = ObservationStatus.Ok, Identity = id, Values = { TicksGame = 60000 },
        });
        var o = new MetricObservation
        {
            Source = MetricObservationSource.SaveSnapshot, Key = "ss", Identity = id, SaveTick = 60000, ObservedUtc = Now,
        };
        foreach (var (key, entry) in FactionStateSnapshot.Build(facts))
            o.Metrics[key] = entry;
        p.MetricObservations.Add(o);
        var colonies = CampaignMatcher.BuildColonies(p);
        var view = MetricCalculator.BuildView(p, colonies, StatsCalculator.Calculate(colonies, new StatsOptions()), MetricRegistry.Catalog);
        return (view, view.Colonies.Single());
    }

    [Fact]
    public void MeasuredGoodwillRange_IsAKnownReadingOfItsBreakdowns()
    {
        // #329: the scalar has no additive meaning, but a measured min/max is not "not applicable" nor "not reached".
        var (view, colony) = ViewOf(new[]
        {
            new FactionStateFact("A", true, true, false, 60),
            new FactionStateFact("B", true, false, true, -50),
        });
        var row = colony.Rows.Single(r => r.Definition.Key == FactionStateSnapshot.GoodwillRange);
        Assert.True(row.Value.BreakdownOnly);
        Assert.True(row.Value.Known);
        Assert.False(row.Value.NotReached);
        Assert.Null(row.Value.Number);
        Assert.Equal(MetricCoverage.SnapshotOnly, row.Value.Coverage);
        Assert.Equal((1, 1), (row.Targets, row.Valid));
        Assert.True(MetricDisplayPolicy.HasMeasuredNumber(row));
        Assert.Equal((-50d, 60d), MetricDisplayPolicy.BreakdownRange(row));
        Assert.NotEqual(MetricVisualKind.NotApplicable, MetricVisualState.Classify(row));
        var status = MetricStatusClassifier.Classify(new StatsView { Colonies = [], Stats = new StatsResult { Colonies = [], EndingsByType = new Dictionary<EndingType, int>() }, Numeric = view }, _ => UnitRecordingState.Recording)
            .Single(s => s.Definition.Key == FactionStateSnapshot.GoodwillRange);
        Assert.Equal((MetricAvailability.AllMeasuredColonies, 1, 1), (status.Availability, status.Measured, status.Targets));
    }

    [Fact]
    public void GoodwillRangeWithoutAnyGoodwill_StaysNotApplicable()
    {
        var (_, colony) = ViewOf(new[] { new FactionStateFact("C", true, false, true, null) });
        var row = colony.Rows.Single(r => r.Definition.Key == FactionStateSnapshot.GoodwillRange);
        Assert.False(row.Value.BreakdownOnly);
        Assert.Equal(MetricCoverage.NotApplicable, row.Value.Coverage);
        Assert.False(MetricDisplayPolicy.HasMeasuredNumber(row));
        Assert.Null(MetricDisplayPolicy.BreakdownRange(row));
    }
}
