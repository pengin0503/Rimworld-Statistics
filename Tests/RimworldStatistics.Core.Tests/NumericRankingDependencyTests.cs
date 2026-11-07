namespace RimworldStatistics.Core.Tests;

/// <summary>Unverified B/C inputs can produce provisional rankings only in the development catalog.</summary>
public sealed class NumericRankingDependencyTests
{
    private static ColonyIdentity Id(string seed) => new()
    {
        SeedString = seed, PersistentRandomValue = 1, StartingTile = "1,0", GameStartAbsTick = 0,
        FirstWealthBits = 7, StartingPawnIds = new List<string> { "Thing_" + seed },
        ModCampaignId = "rank-" + seed,
    };

    private static SaveObservation Save(string seed) => new()
    {
        FileName = seed + ".rws", FileSize = 1, LastWriteUtc = DateTime.UtcNow, ReaderVersion = SaveReader.Version,
        Status = ObservationStatus.Ok, Identity = Id(seed), Values = { TicksGame = 60000 },
    };

    private static MetricObservation Event(string seed, params (string Key, double Value)[] values)
    {
        var o = new MetricObservation
        {
            Source = MetricObservationSource.Checkpoint, Key = "cp-" + seed, Identity = Id(seed),
            SaveTick = 60000, ObservedUtc = DateTime.UtcNow,
        };
        foreach (var (k, v) in values)
            o.Metrics[k] = MetricEntry.Of(MetricKeys.Revision(k),
                new MetricValue { Coverage = MetricCoverage.Complete, RealValue = v });
        return o;
    }

    private static MetricObservation Snapshot(string seed, params (string Key, double Value)[] values)
    {
        var o = new MetricObservation
        {
            Source = MetricObservationSource.SaveSnapshot, Key = "ss-" + seed, Identity = Id(seed),
            SaveTick = 60000, ObservedUtc = DateTime.UtcNow,
        };
        foreach (var (k, v) in values)
            o.Metrics[k] = MetricEntry.Of(MetricKeys.Revision(k),
                new MetricValue { Coverage = MetricCoverage.SnapshotOnly, RealValue = v });
        return o;
    }

    private static NumericView View(Profile profile, bool developmentCatalog)
    {
        var colonies = CampaignMatcher.BuildColonies(profile);
        var stats = StatsCalculator.Calculate(colonies, new StatsOptions());
        return MetricCalculator.BuildView(profile, colonies, stats,
            developmentCatalog ? MetricRegistry.Catalog : MetricRegistry.All);
    }

    [Fact]
    public void BAndCInputRankingsChooseCorrectHolderWithoutMixingDifferentSources()
    {
        var p = new Profile();
        p.SaveObservations.Add(Save("A"));
        p.SaveObservations.Add(Save("B"));
        p.MetricObservations.Add(Event("A",
            (HealthDeathFamily.HostileKills, 12),
            (HealthDeathFamily.LongestDeathGap, 2),
            (WorkFamily.CraftedUnits, 80),
            (TradeFamily.SalesPeak, 240)));
        p.MetricObservations.Add(Event("B",
            (HealthDeathFamily.HostileKills, 6),
            (HealthDeathFamily.LongestDeathGap, 4),
            (WorkFamily.CraftedUnits, 100),
            (TradeFamily.SalesPeak, 90)));
        p.MetricObservations.Add(Snapshot("A", (ResearchStateSnapshot.Finished, 10)));
        p.MetricObservations.Add(Snapshot("B", (ResearchStateSnapshot.Finished, 20)));
        var view = View(p, developmentCatalog: true);
        var keys = new[] {
            MetricRankings.MostKills, MetricRankings.LongestDeathFree,
            MetricRankings.MostCrafting, MetricRankings.LargestSale, MetricRankings.MostResearch,
        };
        var expected = new[] { 12d, 4d, 100d, 240d, 20d };
        var a = view.Colonies.Single(x => x.Key.Contains("A") || x.Rows.Any()).Key; // identify by value instead below
        for (var i = 0; i < keys.Length; i++)
        {
            var row = view.Overall.Single(x => x.Definition.Key == keys[i]);
            Assert.Equal(expected[i], row.Value.Number);
            Assert.Equal(2, row.Valid);
            Assert.Equal(2, row.Targets);
            Assert.NotNull(row.Value.ColonyKey);
        }
        // These depend on unverified B/C acquisition and must NOT be published in normal Details.
        var normal = View(p, developmentCatalog: false);
        Assert.All(keys, key => Assert.DoesNotContain(normal.Overall, x => x.Definition.Key == key));
        Assert.All(keys, key => Assert.False(MetricRegistry.Find(key)!.Published));
        Assert.All(keys, key => Assert.DoesNotContain(view.Colonies.SelectMany(c => c.Rows), x => x.Definition.Key == key));
    }

    [Fact]
    public void MissingColonyInputMakesRankPartial_WithoutReplacingAbsentValueWithZero()
    {
        var p = new Profile();
        p.SaveObservations.Add(Save("A"));
        p.SaveObservations.Add(Save("B"));
        p.MetricObservations.Add(Snapshot("A", (ResearchStateSnapshot.Finished, 10)));
        var row = View(p, developmentCatalog: true).Overall.Single(x => x.Definition.Key == MetricRankings.MostResearch);
        Assert.Equal(10d, row.Value.Number);
        Assert.Equal(2, row.Targets);
        Assert.Equal(1, row.Valid);
        Assert.Equal(MetricCoverage.Partial, row.Value.Coverage);
    }
}
