namespace RimworldStatistics.Core.Tests;

public sealed class HistoricalCounterIntervalsTests
{
    private static ColonyIdentity Identity() => new()
    {
        ModCampaignId = "campaign-1", SeedString = "seed", PersistentRandomValue = 1,
        StartingTile = "100,0", FirstWealthBits = 123u,
        StartingPawnIds = { "pawn-a" },
    };

    private static MetricFileAlias Alias(string id) => new()
    {
        FileName = id + ".rws", FileSize = 1234,
        LastWriteUtc = new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc),
    };

    private static void Point(Profile profile, string key, string? parent, int tick,
        long raids, long deaths, string modHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", string version = "1.7.5000 rev1275",
        bool includeRaids = true)
    {
        var identity = Identity();
        var save = new MetricObservation
        {
            Source = MetricObservationSource.SaveFile, Key = "sha-" + key,
            GameVersion = version, ModSetHash = modHash, SaveTick = tick,
            Identity = identity.Clone(), FileAliases = { Alias(key) },
        };
        if (includeRaids)
            save.Metrics[MetricKeys.RaidsPerYear] = MetricEntry.Of(
                MetricKeys.Revision(MetricKeys.RaidsPerYear),
                new MetricValue { Coverage = MetricCoverage.Complete, Numerator = raids, Denominator = 1, RealValue = raids });
        save.Metrics[MetricKeys.DeathsPerYear] = MetricEntry.Of(
            MetricKeys.Revision(MetricKeys.DeathsPerYear),
            new MetricValue { Coverage = MetricCoverage.Complete, Numerator = deaths, Denominator = 1, RealValue = deaths });

        profile.MetricObservations.Add(save);
        profile.MetricObservations.Add(new MetricObservation
        {
            Source = MetricObservationSource.Checkpoint, Key = key, ParentKey = parent,
            PayloadHash = new string(key[0], 64), SaveTick = tick,
            Identity = identity.Clone(), GameVersion = version, FileAliases = { Alias(key) },
        });
    }

    [Fact]
    public void DirectConfirmedCheckpointAncestor_ProducesOnlyPartialIntervalDeltas()
    {
        var p = new Profile();
        Point(p, "A", null, 60000, 3, 1);
        Point(p, "B", "A", 120000, 6, 3);
        var intervals = HistoricalCounterIntervals.Evaluate(p);
        var interval = Assert.Single(intervals);
        Assert.Equal(("A", "B", 60000, 120000), 
            (interval.ParentCheckpointId, interval.ChildCheckpointId, interval.StartTick, interval.EndTick));
        Assert.Equal(3, interval.RaidsIncrease);
        Assert.Equal(2, interval.ColonistDeathsIncrease);
        Assert.Equal(MetricCoverage.Partial, interval.Coverage);
    }

    [Fact]
    public void NoConfirmedParent_EvenSameCampaignAndLaterSave_IsNotCompared()
    {
        var p = new Profile();
        Point(p, "A", null, 60000, 3, 1);
        Point(p, "B", null, 120000, 6, 3);
        Assert.Empty(HistoricalCounterIntervals.Evaluate(p));
    }

    [Theory]
    [InlineData("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "1.7.5000 rev1275", 120000, 6, 3, true)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "1.7.4995 rev1269", 120000, 6, 3, true)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "1.7.5000 rev1275", 50000, 6, 3, true)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "1.7.5000 rev1275", 120000, 2, 3, true)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "1.7.5000 rev1275", 120000, 6, 0, true)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "1.7.5000 rev1275", 120000, 6, 3, false)]
    public void ChangedInputsOrUnverifiedCounters_AreNotComparable(
        string modHash, string version, int endTick, long raids, long deaths, bool includeRaids)
    {
        var p = new Profile();
        Point(p, "A", null, 60000, 3, 1);
        Point(p, "B", "A", endTick, raids, deaths, modHash, version, includeRaids);
        Assert.Empty(HistoricalCounterIntervals.Evaluate(p));
    }

    [Fact]
    public void SiblingForks_AreReturnedSeparatelyNeverSummedAsOneLineage()
    {
        var p = new Profile();
        Point(p, "A", null, 60000, 3, 1);
        Point(p, "B", "A", 120000, 6, 2);
        Point(p, "C", "A", 150000, 5, 4);
        var intervals = HistoricalCounterIntervals.Evaluate(p);
        Assert.Equal(2, intervals.Count);
        Assert.Equal(new[] { "B", "C" }, intervals.Select(x => x.ChildCheckpointId).Order(StringComparer.Ordinal));
        Assert.Equal(new[] { 3L, 2L }, intervals.OrderBy(x => x.ChildCheckpointId).Select(x => x.RaidsIncrease));
    }

    [Fact]
    public void ConflictedOrDuplicatedCheckpointId_RejectsIntervals()
    {
        var p = new Profile();
        Point(p, "A", null, 60000, 3, 1);
        Point(p, "B", "A", 120000, 6, 2);
        p.MetricObservations.Add(new MetricObservation
        {
            Source = MetricObservationSource.Checkpoint, Key = "A",
            PayloadHash = new string('z', 64), Identity = Identity(),
        });
        Assert.Empty(HistoricalCounterIntervals.Evaluate(p));
    }
}
