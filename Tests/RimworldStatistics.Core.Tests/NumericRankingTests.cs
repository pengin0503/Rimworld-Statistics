namespace RimworldStatistics.Core.Tests;

/// <summary>Phase D1 rankings derived from v1 records and Phase A observations (ledger sections ranking, ranking_dependency).</summary>
public sealed class NumericRankingTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ColonyIdentity Id(string seed) => new()
    {
        SeedString = seed, PersistentRandomValue = 1, StartingTile = "1,0", GameStartAbsTick = 0, FirstWealthBits = 7,
        StartingPawnIds = new List<string> { "Thing_Human1" }, ModCampaignId = "cid-" + seed,
    };

    private static SaveObservation V1(string seed, int ticks, string difficulty = "Rough", bool permadeath = false, string scenario = "Crashlanded",
        string storyteller = "Cassandra", int population = 3, double realPlay = 3600)
        => new()
        {
            FileName = seed + ".rws", FileSize = 1, LastWriteUtc = T0, ReaderVersion = SaveReader.Version, Status = ObservationStatus.Ok,
            Identity = Id(seed),
            Values = { TicksGame = ticks, GreatestPopulation = population, RealPlayTime = realPlay },
            Display = { Difficulty = difficulty, Permadeath = permadeath, Scenario = scenario, Storyteller = storyteller, FactionName = seed },
        };

    private static EndingEvent Ending(string seed, EndingType type, int ticks, EndingSource source = EndingSource.Live, int ordinal = 0,
        bool dev = false, int? killed = null)
        => new() { CampaignId = "cid-" + seed, Type = type, Ordinal = ordinal, Source = source, TicksGame = ticks, DevMode = dev, ColonistsKilledAtEnding = killed };

    private static NumericView View(Profile p, bool includeDev = false)
    {
        var colonies = CampaignMatcher.BuildColonies(p);
        return MetricCalculator.BuildView(p, colonies, new StatsOptions { IncludeDevMode = includeDev });
    }

    private static NumericRow R(NumericView v, string key) => v.Overall.Single(r => r.Definition.Key == key);

    private static string KeyOf(Profile p, string seed) => CampaignMatcher.BuildColonies(p).Single(c => c.CampaignIds().Contains("cid-" + seed)).Key;

    [Fact]
    public void FastestEnding_UsesMeasuredLiveTicks_NotTraces()
    {
        var p = new Profile();
        p.SaveObservations.Add(V1("A", 700000));
        p.SaveObservations.Add(V1("B", 700000));
        p.EndingEvents.Add(Ending("A", EndingType.Ship, 600000));
        p.EndingEvents.Add(Ending("B", EndingType.AmbiguousLaunch, 300000, EndingSource.SeededTrace));
        var row = R(View(p), "ranking.ending.fastest");
        Assert.Equal(10d, row.Value.Number);
        Assert.Equal(KeyOf(p, "A"), row.Value.ColonyKey);
    }

    [Fact]
    public void FastestEnding_DevModeOnlyWhenIncluded_AndExcludedColoniesNever()
    {
        var p = new Profile();
        p.SaveObservations.Add(V1("A", 700000));
        p.SaveObservations.Add(V1("B", 700000));
        p.SaveObservations.Add(V1("C", 700000));
        p.EndingEvents.Add(Ending("A", EndingType.Ship, 600000));
        p.EndingEvents.Add(Ending("B", EndingType.Ship, 120000, dev: true));
        p.EndingEvents.Add(Ending("C", EndingType.Ship, 60000));
        p.UserDecisions.ExcludedCampaignKeys.Add(KeyOf(p, "C"));
        Assert.Equal(10d, R(View(p), "ranking.ending.fastest").Value.Number);
        Assert.Equal(2d, R(View(p, includeDev: true), "ranking.ending.fastest").Value.Number);
    }

    [Fact]
    public void FastestEnding_UsesTheCountedRecordOfAGroup()
    {
        // The same (type, ordinal) once in dev mode, earlier, and once normally: only the normal one counts by default.
        var p = new Profile();
        p.SaveObservations.Add(V1("A", 700000));
        p.EndingEvents.Add(Ending("A", EndingType.Ship, 120000, dev: true));
        p.EndingEvents.Add(Ending("A", EndingType.Ship, 600000));
        Assert.Equal(10d, R(View(p), "ranking.ending.fastest").Value.Number);
        Assert.Equal(2d, R(View(p, includeDev: true), "ranking.ending.fastest").Value.Number);
    }

    [Fact]
    public void FastestEnding_TieGoesToTheSmallerColonyKey()
    {
        var p = new Profile();
        p.SaveObservations.Add(V1("A", 700000));
        p.SaveObservations.Add(V1("B", 700000));
        p.EndingEvents.Add(Ending("A", EndingType.Ship, 600000));
        p.EndingEvents.Add(Ending("B", EndingType.Ship, 600000));
        var expected = new[] { KeyOf(p, "A"), KeyOf(p, "B") }.Order(StringComparer.Ordinal).First();
        Assert.Equal(expected, R(View(p), "ranking.ending.fastest").Value.ColonyKey);
    }

    [Fact]
    public void ByDifficultyAndPermadeath_SumDaysAndAdoptedEndings()
    {
        var p = new Profile();
        p.SaveObservations.Add(V1("A", 600000, "Rough"));
        p.SaveObservations.Add(V1("B", 1200000, "Rough", permadeath: true));
        p.SaveObservations.Add(V1("C", 300000, "Peaceful"));
        p.EndingEvents.Add(Ending("A", EndingType.Ship, 500000));
        p.EndingEvents.Add(Ending("B", EndingType.Archonexus, 1000000));
        p.EndingEvents.Add(Ending("B", EndingType.Ship, 1100000, dev: true));   // not adopted without the setting
        var v = View(p);
        var days = R(v, "ranking.by_difficulty").Breakdowns.ToDictionary(b => b.Breakdown, b => b.Value.Number);
        Assert.Equal(30d, days["Rough"]);
        Assert.Equal(5d, days["Peaceful"]);
        var endings = R(v, "ranking.by_difficulty.endings").Breakdowns.ToDictionary(b => b.Breakdown, b => b.Value.Number);
        Assert.Equal(2d, endings["Rough"]);
        Assert.Equal(0d, endings["Peaceful"]);
        var perma = R(v, "ranking.by_permadeath").Breakdowns.ToDictionary(b => b.Breakdown, b => b.Value.Number);
        Assert.Equal((20d, 15d), (perma[MetricRankings.PermadeathOn], perma[MetricRankings.PermadeathOff]));
    }

    [Fact]
    public void TopScenario_AndDaysByStoryteller()
    {
        var p = new Profile();
        p.SaveObservations.Add(V1("A", 600000, scenario: "Crashlanded", storyteller: "Cassandra"));
        p.SaveObservations.Add(V1("B", 600000, scenario: "Tribal", storyteller: "Randy"));
        p.SaveObservations.Add(V1("C", 1200000, scenario: "Tribal", storyteller: "Randy"));
        p.EndingEvents.Add(Ending("A", EndingType.Ship, 500000));
        p.EndingEvents.Add(Ending("B", EndingType.Ship, 500000));
        p.EndingEvents.Add(Ending("C", EndingType.Ship, 500000));
        p.EndingEvents.Add(Ending("C", EndingType.Ship, 900000, ordinal: 1));
        var v = View(p);
        var top = R(v, "ranking.ending.top_scenario");
        Assert.Equal((3d, "Tribal"), (top.Value.Number, top.Value.Label));
        var byTeller = R(v, "ranking.survival.by_storyteller").Breakdowns.ToDictionary(b => b.Breakdown, b => b.Value.Number);
        Assert.Equal((10d, 30d), (byTeller["Cassandra"], byTeller["Randy"]));
    }

    [Fact]
    public void PopulationAndPlaytimePeaks_NameTheColony()
    {
        var p = new Profile();
        p.SaveObservations.Add(V1("A", 600000, population: 12, realPlay: 7200));
        p.SaveObservations.Add(V1("B", 600000, population: 8, realPlay: 36000));
        var v = View(p);
        Assert.Equal((12d, KeyOf(p, "A")), (R(v, "ranking.population.peak").Value.Number, R(v, "ranking.population.peak").Value.ColonyKey));
        Assert.Equal((10d, KeyOf(p, "B")), (R(v, "ranking.playtime.peak").Value.Number, R(v, "ranking.playtime.peak").Value.ColonyKey));
    }

    [Fact]
    public void MinimumDeathsAtEnding_IgnoresEndingsWithoutTheCounter()
    {
        var p = new Profile();
        p.SaveObservations.Add(V1("A", 700000));
        p.SaveObservations.Add(V1("B", 700000));
        p.SaveObservations.Add(V1("C", 700000));
        p.EndingEvents.Add(Ending("A", EndingType.Ship, 600000, killed: 4));
        p.EndingEvents.Add(Ending("B", EndingType.Ship, 600000, killed: 0));
        p.EndingEvents.Add(Ending("C", EndingType.Ship, 600000));                 // recorded before this version: unknown, not 0
        var v = View(p);
        var min = R(v, "ranking.ending.min_deaths");
        Assert.Equal((0d, KeyOf(p, "B"), MetricCoverage.Partial), (min.Value.Number, min.Value.ColonyKey, min.Value.Coverage));
        var zero = R(v, "ranking.ending.zero_deaths");
        Assert.Equal((1d, MetricCoverage.Partial), (zero.Value.Number, zero.Value.Coverage));
    }

    [Fact]
    public void MinimumDeaths_WithNoCounterAnywhere_IsUnknown()
    {
        var p = new Profile();
        p.SaveObservations.Add(V1("A", 700000));
        p.EndingEvents.Add(Ending("A", EndingType.Ship, 600000));
        Assert.Equal(MetricCoverage.Unknown, R(View(p), "ranking.ending.min_deaths").Value.Coverage);
        Assert.Equal(MetricCoverage.Unknown, R(View(p), "ranking.ending.zero_deaths").Value.Coverage);
    }

    private static MetricObservation Numeric(string seed, int tick, params (string Key, MetricValue Value)[] metrics)
    {
        var m = new MetricObservation { Source = MetricObservationSource.SaveFile, Key = "k" + seed, Identity = Id(seed), SaveTick = tick };
        foreach (var (k, v) in metrics)
            m.Metrics[k] = MetricEntry.Of(MetricKeys.Revision(k), v);
        return m;
    }

    [Fact]
    public void NetWealthPerYear_DividesByTheYearsTheSeriesSpan()
    {
        var p = new Profile();
        p.SaveObservations.Add(V1("A", 3600000));
        p.SaveObservations.Add(V1("B", 3600000));
        p.SaveObservations.Add(V1("C", 0));
        // A: +12000 over 121 points (1 year); B: −3000 over 241 points (2 years); C: a single point is left out.
        p.MetricObservations.Add(Numeric("A", 3600000, (MetricKeys.WealthTotalNet, new MetricValue { Coverage = MetricCoverage.SnapshotOnly, RealValue = 12000, SampleCount = 121 })));
        p.MetricObservations.Add(Numeric("B", 7200000, (MetricKeys.WealthTotalNet, new MetricValue { Coverage = MetricCoverage.SnapshotOnly, RealValue = -3000, SampleCount = 241 })));
        p.MetricObservations.Add(Numeric("C", 0, (MetricKeys.WealthTotalNet, new MetricValue { Coverage = MetricCoverage.SnapshotOnly, RealValue = 0, SampleCount = 1 })));
        var row = R(View(p), "ranking.wealth.net_per_year");
        Assert.Equal((9000d, 3d), (row.Value.Numerator, row.Value.Denominator));
        Assert.Equal(3000d, row.Value.Number!.Value, 9);
        Assert.Equal((2, 2, MetricCoverage.SnapshotOnly), (row.Targets, row.Valid, row.Value.Coverage));   // C spans no time: not a target

        p.SaveObservations.Add(V1("D", 3600000));   // no numeric data: missing, so Partial
        var partial = R(View(p), "ranking.wealth.net_per_year");
        Assert.Equal((3, 2, MetricCoverage.Partial), (partial.Targets, partial.Valid, partial.Value.Coverage));
    }

    [Fact]
    public void WealthPerColonist_IsTheLargestColonyValue()
    {
        var p = new Profile();
        p.SaveObservations.Add(V1("A", 60000));
        p.SaveObservations.Add(V1("B", 60000));
        p.MetricObservations.Add(Numeric("A", 60000, (MetricKeys.WealthPerColonistMax, new MetricValue { Coverage = MetricCoverage.SnapshotOnly, RealValue = 5000, Numerator = 15000, Denominator = 3 })));
        p.MetricObservations.Add(Numeric("B", 60000, (MetricKeys.WealthPerColonistMax, new MetricValue { Coverage = MetricCoverage.SnapshotOnly, RealValue = 8000, Numerator = 16000, Denominator = 2 })));
        var row = R(View(p), "ranking.wealth.per_colonist");
        Assert.Equal((8000d, KeyOf(p, "B")), (row.Value.Number, row.Value.ColonyKey));
    }

    [Fact]
    public void Rankings_AreNotListedPerColony_AndNoRowForR011()
    {
        var p = new Profile();
        p.SaveObservations.Add(V1("A", 60000));
        var v = View(p);
        Assert.All(v.Colonies.Single().Rows, r => Assert.NotEqual(MetricCategory.Rankings, r.Definition.Category));
        Assert.DoesNotContain(MetricRegistry.All, d => d.LedgerIds.Contains("R-011"));
        Assert.DoesNotContain(MetricRegistry.All, d => d.Key == "ranking.raid.avg_damage");
    }

    [Fact]
    public void EndingCounter_IsKeptWhenALiveRecordReplacesASeededTrace()
    {
        var p = new Profile();
        ProfileMutations.UpsertEnding(p, Ending("A", EndingType.Ship, 100, EndingSource.SeededTrace));
        ProfileMutations.UpsertEnding(p, Ending("A", EndingType.Ship, 600000, killed: 2));
        ProfileMutations.UpsertEnding(p, Ending("A", EndingType.Ship, 600000));
        Assert.Equal(2, p.EndingEvents.Single().ColonistsKilledAtEnding);
    }

    [Fact]
    public void V1Totals_AreUnchangedByTheRankings()
    {
        var p = new Profile();
        p.SaveObservations.Add(V1("A", 700000));
        p.EndingEvents.Add(Ending("A", EndingType.Ship, 600000, killed: 1));
        p.EndingEvents.Add(Ending("A", EndingType.Ship, 650000, ordinal: 1, dev: true));
        var colonies = CampaignMatcher.BuildColonies(p);
        var s = StatsCalculator.Calculate(colonies, new StatsOptions());
        Assert.Equal((1, 1), (s.TotalEndings, s.EndingsByType[EndingType.Ship]));
        Assert.True(s.Colonies.Single().HasDevOnlyEndings);
    }
}
