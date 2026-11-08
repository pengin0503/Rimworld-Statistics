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
        var owners = new[] { "rank-A", "rank-B", "rank-B", "rank-A", "rank-B" };
        var byCampaignId = CampaignMatcher.BuildColonies(p).ToDictionary(
            c => c.CampaignIds().Single(), c => c.Key, StringComparer.Ordinal);
        for (var i = 0; i < keys.Length; i++)
        {
            var row = view.Overall.Single(x => x.Definition.Key == keys[i]);
            Assert.Equal(expected[i], row.Value.Number);
            Assert.Equal(2, row.Valid);
            Assert.Equal(2, row.Targets);
            Assert.Equal(byCampaignId[owners[i]], row.Value.ColonyKey);
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
    [Fact]
    public void BiomeRankingsUseOriginalBiomeAndSkipUnknownColonies()
    {
        var p = new Profile();
        foreach (var seed in new[] { "A", "B", "C" })
            p.SaveObservations.Add(Save(seed));
        static MetricObservation StartBiome(string seed, string biome)
        {
            var o = new MetricObservation
            {
                Source = MetricObservationSource.SaveSnapshot,
                Key = "biome-" + seed, Identity = Id(seed), SaveTick = 60000,
                ObservedUtc = DateTime.UtcNow,
            };
            o.Metrics[ColonyStartSnapshot.Biome] = ColonyStartSnapshot.Build(biome)[ColonyStartSnapshot.Biome];
            return o;
        }
        p.MetricObservations.Add(StartBiome("A", "TemperateForest"));
        p.MetricObservations.Add(StartBiome("B", "ExtremeDesert"));
        // C has no original biome; do not guess its current site's biome.
        p.EndingEvents.Add(new EndingEvent
        {
            CampaignId = "rank-B", Type = EndingType.Ship, TicksGame = 60000,
            Source = EndingSource.Live, Ordinal = 0,
        });
        var v = View(p, developmentCatalog: true);
        var days = v.Overall.Single(x => x.Definition.Key == MetricRankings.DaysByBiome);
        Assert.Equal((2d, 3, 2, MetricCoverage.Partial),
            (days.Value.Number, days.Targets, days.Valid, days.Value.Coverage));
        var split = days.Breakdowns.ToDictionary(x => x.Breakdown, x => x.Value.Number);
        Assert.Equal(1d, split["TemperateForest"]);
        Assert.Equal(1d, split["ExtremeDesert"]);
        var endings = v.Overall.Single(x => x.Definition.Key == MetricRankings.EndingsByBiome);
        Assert.Equal(1d, endings.Value.Number);
        Assert.Equal(1d, endings.Breakdowns.Single(x => x.Breakdown == "ExtremeDesert").Value.Number);
        Assert.Equal(0d, endings.Breakdowns.Single(x => x.Breakdown == "TemperateForest").Value.Number);
        Assert.DoesNotContain(View(p, developmentCatalog: false).Overall,
            x => x.Definition.Key == MetricRankings.DaysByBiome || x.Definition.Key == MetricRankings.EndingsByBiome);
    }

    [Fact]
    public void KillDeathRatioCombinesOnlySameCheckpointPairs()
    {
        var p = new Profile();
        foreach (var seed in new[] { "A", "B", "C" })
            p.SaveObservations.Add(Save(seed));
        p.MetricObservations.Add(Event("A",
            (HealthDeathFamily.HostileKills, 12), (HealthDeathFamily.DeathsByRaid, 3)));
        p.MetricObservations.Add(Event("B",
            (HealthDeathFamily.HostileKills, 6), (HealthDeathFamily.DeathsByRaid, 1)));
        // No checkpoint for C: coverage is partial and C does not dilute the numerator.
        var ratio = View(p, developmentCatalog: true).Overall.Single(x => x.Definition.Key == MetricRankings.KillsToDeaths);
        Assert.Equal((18d, 4d, 4.5d), (ratio.Value.Numerator, ratio.Value.Denominator, ratio.Value.Number));
        Assert.Equal((3, 2, MetricCoverage.Partial), (ratio.Targets, ratio.Valid, ratio.Value.Coverage));
        Assert.False(MetricRegistry.Find(MetricRankings.KillsToDeaths)!.Published);
        Assert.DoesNotContain(View(p, developmentCatalog: false).Overall,
            x => x.Definition.Key == MetricRankings.KillsToDeaths);
    }

    [Fact]
    public void KillDeathRatioDoesNotDivideByZeroOrUseUnpairedInput()
    {
        var p = new Profile();
        p.SaveObservations.Add(Save("A"));
        p.MetricObservations.Add(Event("A", (HealthDeathFamily.HostileKills, 7),
            (HealthDeathFamily.DeathsByRaid, 0)));
        var v = View(p, developmentCatalog: true).Overall.Single(x => x.Definition.Key == MetricRankings.KillsToDeaths);
        Assert.Null(v.Value.Number);
        Assert.Equal(7d, v.Value.Numerator);
        Assert.Equal(0d, v.Value.Denominator);
        Assert.Equal(1, v.Valid);
        var missing = new Profile();
        missing.SaveObservations.Add(Save("A"));
        missing.MetricObservations.Add(Event("A", (HealthDeathFamily.HostileKills, 7)));
        var unknown = View(missing, developmentCatalog: true).Overall.Single(x => x.Definition.Key == MetricRankings.KillsToDeaths);
        Assert.Equal(MetricCoverage.Unknown, unknown.Value.Coverage);
        Assert.Equal(0, unknown.Valid);
    }

}
