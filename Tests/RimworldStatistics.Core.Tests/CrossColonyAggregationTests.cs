namespace RimworldStatistics.Core.Tests;

public sealed class CrossColonyAggregationTests
{
    private static readonly DateTime T0 = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    private static MetricDefinition Definition(string key) => MetricRegistry.Find(key)!;

    private static NumericRow Record(string key, double? total, MetricCoverage coverage = MetricCoverage.SnapshotOnly,
        params (string Key, double Units)[] breakdown)
    {
        var definition = Definition(key);
        return new NumericRow
        {
            Definition = definition,
            Value = new NumericValue { Number = total, Coverage = coverage, ThroughTick = 60000 },
            Targets = coverage == MetricCoverage.NotApplicable ? 0 : 1,
            Valid = total is null ? 0 : 1,
            Breakdowns = breakdown.Select(b => new NumericRow
            {
                Definition = definition, Breakdown = b.Key,
                Value = new NumericValue { Number = b.Units, Coverage = coverage },
                Targets = 1, Valid = 1,
            }).ToArray(),
        };
    }

    private static NumericRow Derived(string key, params (string Colony, NumericRow[] Rows)[] colonies)
    {
        var byColony = colonies.ToDictionary(c => c.Colony,
            c => (IReadOnlyList<NumericRow>)c.Rows, StringComparer.Ordinal);
        return CrossColonyAggregator.Build(
            CrossColonyCatalog.Build(MetricRegistry.BaseCatalog), byColony)
            .Single(r => r.Definition.Key == key);
    }

    [Fact]
    public void TopItem_SumsPerDefAcrossIndependentColonies_NotLocalWinners()
    {
        var row = Derived(CrossColonyCatalog.TopPrefix + WorkFamily.HarvestByCrop,
            ("a", new[] { Record(WorkFamily.HarvestByCrop, 13, MetricCoverage.Complete, ("Rice", 10), ("Potato", 3)) }),
            ("b", new[] { Record(WorkFamily.HarvestByCrop, 22, MetricCoverage.Complete, ("Rice", 2), ("Potato", 20)) }));
        Assert.Equal(23, row.Value.Number);
        Assert.Equal("Potato", row.Value.Label);
        Assert.Equal((2, 2, MetricCoverage.Complete), (row.Targets, row.Valid, row.Value.Coverage));
    }

    [Theory]
    [InlineData(TradeFamily.SalesUnits)]
    [InlineData(TradeFamily.BuysUnits)]
    [InlineData(WorkDetailsFamily.CraftedByDef)]
    [InlineData(WorkDetailsFamily.MinedByDef)]
    [InlineData(WorkFamily.MealsByDef)]
    [InlineData(StockSnapshot.MaterialsLatest)]
    [InlineData(FacilitiesSnapshot.RunningGenerators)]
    public void TopItem_UsesExistingDefBreakdowns(string key)
    {
        var row = Derived(CrossColonyCatalog.TopPrefix + key,
            ("a", new[] { Record(key, 3, MetricCoverage.Complete, ("X", 1), ("Y", 2)) }),
            ("b", new[] { Record(key, 8, MetricCoverage.Complete, ("X", 8)) }));
        Assert.Equal(9, row.Value.Number);
        Assert.Equal("X", row.Value.Label);
    }

    [Fact]
    public void SnapshotExtrema_ExcludeUnknownRatherThanTreatingAsZero()
    {
        var colonies = new (string Colony, NumericRow[] Rows)[]
        {
            ("a", new[] { Record(RosterSnapshot.Free, 10) }),
            ("b", new[] { Record(RosterSnapshot.Free, 5) }),
            ("c", new[] { Record(RosterSnapshot.Free, null, MetricCoverage.Unknown) }),
        };
        var high = Derived(CrossColonyCatalog.MaxPrefix + RosterSnapshot.Free, colonies);
        var low = Derived(CrossColonyCatalog.MinPrefix + RosterSnapshot.Free, colonies);
        Assert.Equal((10d, "a", 3, 2, MetricCoverage.Partial),
            (high.Value.Number, high.Value.ColonyKey, high.Targets, high.Valid, high.Value.Coverage));
        Assert.Equal((5d, "b", 3, 2, MetricCoverage.Partial),
            (low.Value.Number, low.Value.ColonyKey, low.Targets, low.Valid, low.Value.Coverage));
    }

    [Fact]
    public void Extremes_RetainStableTies_AndObservedZero()
    {
        var min = Derived(CrossColonyCatalog.MinPrefix + RosterSnapshot.Prisoners,
            ("z", new[] { Record(RosterSnapshot.Prisoners, 0) }),
            ("a", new[] { Record(RosterSnapshot.Prisoners, 0) }),
            ("x", new[] { Record(RosterSnapshot.Prisoners, 5) }));
        Assert.Equal(0, min.Value.Number);
        Assert.Equal("a", min.Value.ColonyKey);
        Assert.Equal(new[] { "a", "z" }, min.Value.HolderKeys);
    }

    [Fact]
    public void DlcAbsent_IsNotAnObservedZero()
    {
        var known = Derived(CrossColonyCatalog.MinPrefix + BiotechFamily.MechsCreated,
            ("a", new[] { Record(BiotechFamily.MechsCreated, 2, MetricCoverage.Partial) }),
            ("b", new[] { Record(BiotechFamily.MechsCreated, 0, MetricCoverage.NotApplicable) }));
        Assert.Equal(2, known.Value.Number);
        Assert.Equal((1, 1, MetricCoverage.Partial), (known.Targets, known.Valid, known.Value.Coverage));

        var absent = Derived(CrossColonyCatalog.MinPrefix + BiotechFamily.MechsCreated,
            ("b", new[] { Record(BiotechFamily.MechsCreated, 0, MetricCoverage.NotApplicable) }));
        Assert.Equal(MetricCoverage.NotApplicable, absent.Value.Coverage);
        Assert.Null(absent.Value.Number);
    }

    [Fact]
    public void ItemRanking_UnknownAndOverflowRemainPartial()
    {
        var row = Derived(CrossColonyCatalog.TopPrefix + TradeFamily.SalesUnits,
            ("a", new[] { Record(TradeFamily.SalesUnits, 30, MetricCoverage.Complete, ("Rice", 10)) }),
            ("b", new[] { Record(TradeFamily.SalesUnits, null, MetricCoverage.Unknown) }),
            ("c", new[] { Record(TradeFamily.SalesUnits, 4, MetricCoverage.Complete, (MetricKeys.UnknownBucket, 3), ("Rice", 1)) }));
        Assert.Equal(11, row.Value.Number);
        Assert.Equal("Rice", row.Value.Label);
        Assert.Equal(MetricCoverage.Partial, row.Value.Coverage);
        Assert.Equal((3, 2), (row.Targets, row.Valid));
    }

    [Fact]
    public void NoObservedValues_DoNotInventZeroOrTopItem()
    {
        var r = Derived(CrossColonyCatalog.TopPrefix + WorkFamily.HarvestByCrop,
            ("a", new[] { Record(WorkFamily.HarvestByCrop, null, MetricCoverage.Unknown) }));
        Assert.Null(r.Value.Number);
        Assert.Null(r.Value.Label);
        Assert.Equal(MetricCoverage.Unknown, r.Value.Coverage);
    }

    private static ColonyIdentity Identity(string seed) => new()
    {
        SeedString = seed, PersistentRandomValue = 1, StartingTile = "1,0", GameStartAbsTick = 0,
        FirstWealthBits = 7, StartingPawnIds = new List<string> { "Thing_Human1" },
        ModCampaignId = "campaign-" + seed,
    };

    private static SaveObservation Save(string file, string seed, int tick) => new()
    {
        FileName = file, FileSize = 123, LastWriteUtc = T0.AddSeconds(tick),
        ReaderVersion = SaveReader.Version, Status = ObservationStatus.Ok,
        Identity = Identity(seed), Values = { TicksGame = tick },
    };

    private static MetricObservation Checkpoint(string checkpoint, string? parent, string seed, int tick,
        int total, params (string Def, int Units)[] crops)
    {
        var observation = new MetricObservation
        {
            Source = MetricObservationSource.Checkpoint,
            Key = checkpoint, ParentKey = parent, FormatVersion = SaveReader.Version,
            Identity = Identity(seed), SaveTick = tick, ObservedUtc = T0.AddSeconds(tick),
        };
        var metric = MetricEntry.Of(MetricKeys.Revision(WorkFamily.HarvestByCrop),
            new MetricValue { IntegerValue = total, Coverage = MetricCoverage.Complete });
        foreach (var crop in crops)
            metric.Values[crop.Def] = new MetricValue { IntegerValue = crop.Units, Coverage = MetricCoverage.Complete };
        observation.Metrics[WorkFamily.HarvestByCrop] = metric;
        return observation;
    }

    [Fact]
    public void CheckpointParentChainAndBranch_DoNotAddTheSameColonyTwice()
    {
        var p = new Profile();
        p.SaveObservations.Add(Save("a1", "A", 100));
        p.SaveObservations.Add(Save("a2", "A", 200));
        p.SaveObservations.Add(Save("a3", "A", 180));
        p.SaveObservations.Add(Save("b1", "B", 100));
        p.MetricObservations.Add(Checkpoint("a1", null, "A", 100, 10, ("Rice", 10)));
        p.MetricObservations.Add(Checkpoint("a2", "a1", "A", 200, 12, ("Rice", 12)));
        p.MetricObservations.Add(Checkpoint("a3", "a1", "A", 180, 999, ("Rice", 999)));
        p.MetricObservations.Add(Checkpoint("b1", null, "B", 100, 20, ("Potato", 20)));

        var colonies = CampaignMatcher.BuildColonies(p);
        Assert.Equal(2, colonies.Count);
        var view = MetricCalculator.BuildView(p, colonies,
            StatsCalculator.Calculate(colonies, new StatsOptions()), MetricRegistry.Catalog);
        Assert.Contains(view.Colonies, c => c.Lineage == CheckpointLineage.Branched);
        var total = view.Overall.Single(r => r.Definition.Key == WorkFamily.HarvestByCrop);
        Assert.Equal(32, total.Value.Number);
        var top = view.Overall.Single(r => r.Definition.Key == CrossColonyCatalog.TopPrefix + WorkFamily.HarvestByCrop);
        Assert.Equal(20, top.Value.Number);
        Assert.Equal("Potato", top.Value.Label);
        Assert.Equal(2, top.Targets);
    }

    [Fact]
    public void DerivedDefinitions_DoNotReplaceTheExisting331Definitions()
    {
        Assert.Equal(331, MetricRegistry.BaseCatalog.Count);
        Assert.All(MetricRegistry.BaseCatalog, old =>
            Assert.Same(old, MetricRegistry.Find(old.Key)));
        Assert.All(CrossColonyCatalog.Build(MetricRegistry.BaseCatalog), derived =>
        {
            Assert.Equal(OverallAggregation.Ranking, derived.Overall);
            Assert.Equal(MetricRegistry.Find(derived.StoredKey)!.Acquisition, derived.Acquisition);
        });
    }
}
