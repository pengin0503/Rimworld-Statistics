namespace RimworldStatistics.Core.Tests;

/// <summary>Attribution to v1 colonies and the cross-colony rules of numeric expansion design §3 (plan A3).</summary>
public sealed class NumericAggregationTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ColonyIdentity Id(string seed, string pawns = "Thing_Human1", string? campaign = null) => new()
    {
        SeedString = seed, PersistentRandomValue = 1, StartingTile = "1,0", GameStartAbsTick = 0, FirstWealthBits = 7,
        StartingPawnIds = pawns.Split(',').ToList(), ModCampaignId = campaign,
    };

    /// <summary>The v1 record of a save file: what makes the colony known.</summary>
    private static SaveObservation V1(string file, ColonyIdentity id, int ticks, int day = 0) => new()
    {
        FileName = file, FileSize = 100, LastWriteUtc = T0.AddDays(day), ReaderVersion = SaveReader.Version,
        Status = ObservationStatus.Ok, Identity = id.Clone(), Values = { TicksGame = ticks },
    };

    private static MetricObservation Numeric(string key, ColonyIdentity id, int tick, string? file = null, int day = 0,
        params (string Key, MetricValue Value)[] metrics)
    {
        var m = new MetricObservation
        {
            Source = MetricObservationSource.SaveFile, FormatVersion = SaveReader.Version, Key = key, Identity = id.Clone(),
            SaveTick = tick, ObservedUtc = T0.AddDays(day),
        };
        if (file != null)
            m.FileAliases.Add(new MetricFileAlias { FileName = file, FileSize = 100, LastWriteUtc = T0.AddDays(day) });
        foreach (var (k, v) in metrics)
            m.Metrics[k] = MetricEntry.Of(MetricKeys.Revision(k), v);
        return m;
    }

    private static (string, MetricValue) Rate(int success, int decided)
        => (MetricKeys.QuestSuccessRate, new MetricValue
        {
            Coverage = MetricCoverage.SnapshotOnly, RealValue = success / (double)decided, Numerator = success, Denominator = decided,
        });

    private static (string, MetricValue) Int(string key, long n, MetricCoverage c = MetricCoverage.SnapshotOnly)
        => (key, new MetricValue { Coverage = c, IntegerValue = n });

    private static (string, MetricValue) Real(string key, double v, MetricCoverage c = MetricCoverage.SnapshotOnly)
        => (key, new MetricValue { Coverage = c, RealValue = v });

    private static NumericView View(Profile p)
    {
        var colonies = CampaignMatcher.BuildColonies(p);
        return MetricCalculator.BuildView(p, colonies, new StatsOptions());
    }

    private static NumericRow Overall(NumericView v, string key) => v.Overall.Single(r => r.Definition.Key == key);

    private static NumericRow Of(NumericView v, string colonyKey, string key)
        => v.Colonies.Single(c => c.Key == colonyKey).Rows.Single(r => r.Definition.Key == key);

    private static string KeyOf(Profile p, string seed) => CampaignMatcher.BuildColonies(p).Single(c => c.Observations.Any(o => o.Identity.SeedString == seed)).Key;

    private static Profile TwoColonies()
    {
        var p = new Profile();
        p.SaveObservations.Add(V1("a.rws", Id("A"), 600000));
        p.SaveObservations.Add(V1("b.rws", Id("B"), 600000));
        p.MetricObservations.Add(Numeric("ka", Id("A"), 600000, "a.rws", metrics: Rate(1, 2)));
        p.MetricObservations.Add(Numeric("kb", Id("B"), 600000, "b.rws", metrics: Rate(9, 10)));
        return p;
    }

    [Fact]
    public void Rate_IsRecomputedFromSummedNumeratorAndDenominator()
    {
        var row = Overall(View(TwoColonies()), MetricKeys.QuestSuccessRate);
        Assert.Equal((10d, 12d), (row.Value.Numerator, row.Value.Denominator));
        Assert.Equal(10d / 12, row.Value.Number!.Value, 12);
        Assert.Equal((2, 2, MetricCoverage.SnapshotOnly), (row.Targets, row.Valid, row.Value.Coverage));
    }

    [Fact]
    public void ExcludedColony_IsListedButNotCounted()
    {
        var p = TwoColonies();
        p.UserDecisions.ExcludedCampaignKeys.Add(KeyOf(p, "B"));
        var v = View(p);
        var row = Overall(v, MetricKeys.QuestSuccessRate);
        Assert.Equal((1d, 2d, 1), (row.Value.Numerator, row.Value.Denominator, row.Targets));
        Assert.True(v.Colonies.Single(c => c.Key == KeyOf(p, "B")).Excluded);
        Assert.Equal(0.9, Of(v, KeyOf(p, "B"), MetricKeys.QuestSuccessRate).Value.Number!.Value, 12);
    }

    [Fact]
    public void ColonyWithoutNumericData_KeepsTheValue_AndMakesItPartial()
    {
        var p = TwoColonies();
        p.SaveObservations.Add(V1("c.rws", Id("C"), 60000));   // known to v1, read before this version (or its file is gone)
        var row = Overall(View(p), MetricKeys.QuestSuccessRate);
        Assert.Equal((10d, 12d), (row.Value.Numerator, row.Value.Denominator));
        Assert.Equal((3, 2, MetricCoverage.Partial), (row.Targets, row.Valid, row.Value.Coverage));
    }

    [Fact]
    public void NotApplicableColony_IsOutsideTheDenominator()
    {
        var p = TwoColonies();
        p.SaveObservations.Add(V1("c.rws", Id("C"), 60000));
        p.MetricObservations.Add(Numeric("kc", Id("C"), 60000, "c.rws",
            metrics: (MetricKeys.QuestSuccessRate, new MetricValue { Coverage = MetricCoverage.NotApplicable })));
        var row = Overall(View(p), MetricKeys.QuestSuccessRate);
        Assert.Equal((2, 2, MetricCoverage.SnapshotOnly), (row.Targets, row.Valid, row.Value.Coverage));
    }

    [Fact]
    public void Representative_IsTheLatestObservation_NotTheLargestOlderBranch()
    {
        var p = new Profile();
        p.SaveObservations.Add(V1("old.rws", Id("A"), 100));
        p.SaveObservations.Add(V1("new.rws", Id("A"), 200));
        p.MetricObservations.Add(Numeric("k1", Id("A"), 100, "old.rws", metrics: Int(MetricKeys.QuestSuccess, 5)));
        p.MetricObservations.Add(Numeric("k2", Id("A"), 200, "new.rws", metrics: Int(MetricKeys.QuestSuccess, 3)));
        var v = View(p);
        Assert.Equal(3d, Overall(v, MetricKeys.QuestSuccess).Value.Number);
        Assert.Equal(200, Of(v, KeyOf(p, "A"), MetricKeys.QuestSuccess).Value.ThroughTick);
    }

    [Fact]
    public void LatestMissingValue_IsNotFilledFromAnOlderObservation()
    {
        var p = new Profile();
        p.SaveObservations.Add(V1("old.rws", Id("A"), 100));
        p.SaveObservations.Add(V1("new.rws", Id("A"), 200));
        p.MetricObservations.Add(Numeric("k1", Id("A"), 100, "old.rws", metrics: Real(MetricKeys.PrisonersPeak, 4)));
        p.MetricObservations.Add(Numeric("k2", Id("A"), 200, "new.rws"));
        var row = Overall(View(p), MetricKeys.PrisonersPeak);
        Assert.Equal((MetricCoverage.Unknown, (double?)null, 0), (row.Value.Coverage, row.Value.Number, row.Valid));
    }

    [Fact]
    public void SameTick_TieBreaksOnTimeThenKey_AndImportOrderDoesNotMatter()
    {
        Profile Build(bool reverse)
        {
            var p = new Profile();
            p.SaveObservations.Add(V1("a.rws", Id("A"), 100));
            var obs = new[]
            {
                Numeric("b-key", Id("A"), 100, "a.rws", day: 1, metrics: Int(MetricKeys.QuestSuccess, 1)),
                Numeric("a-key", Id("A"), 100, "a.rws", day: 1, metrics: Int(MetricKeys.QuestSuccess, 2)),
                Numeric("0-key", Id("A"), 100, "a.rws", day: 0, metrics: Int(MetricKeys.QuestSuccess, 3)),   // smallest key, older
            };
            p.MetricObservations.AddRange(reverse ? obs.Reverse() : obs);
            return p;
        }
        Assert.Equal(2d, Overall(View(Build(false)), MetricKeys.QuestSuccess).Value.Number);   // newest time, then the smaller key
        Assert.Equal(2d, Overall(View(Build(true)), MetricKeys.QuestSuccess).Value.Number);
    }

    [Fact]
    public void SameNameDifferentColonies_StaySeparate_AndManualMergeJoinsThem()
    {
        var p = new Profile();
        p.SaveObservations.Add(V1("a.rws", Id("A"), 100));
        p.SaveObservations.Add(V1("b.rws", Id("B"), 300));
        p.SaveObservations.ForEach(o => o.Display.FactionName = "同じ名前");
        p.MetricObservations.Add(Numeric("ka", Id("A"), 100, "a.rws", metrics: Int(MetricKeys.QuestSuccess, 1)));
        p.MetricObservations.Add(Numeric("kb", Id("B"), 300, "b.rws", metrics: Int(MetricKeys.QuestSuccess, 4)));
        var separate = Overall(View(p), MetricKeys.QuestSuccess);
        Assert.Equal((5d, 2), (separate.Value.Number, separate.Targets));

        p.UserDecisions.ManualMerges.Add(new List<string> { KeyOf(p, "A"), KeyOf(p, "B") });
        var merged = Overall(View(p), MetricKeys.QuestSuccess);
        Assert.Equal((4d, 1), (merged.Value.Number, merged.Targets));   // one colony: its latest observation only
    }

    [Fact]
    public void Attribution_ByCampaignId_FileAlias_OrUniqueSameColony()
    {
        var p = new Profile();
        p.LiveCampaigns.Add(new LiveCampaign { CampaignId = "cid", Identity = Id("L", campaign: "cid"), MaxObservedUtc = T0, Max = { TicksGame = 10 } });
        p.SaveObservations.Add(V1("f.rws", Id("F"), 100));
        p.SaveObservations.Add(V1("s.rws", Id("S", "Thing_Human1,Thing_Human2"), 100));
        p.MetricObservations.Add(Numeric("by-id", Id("other-seed", "Thing_Human9", campaign: "cid"), 10, metrics: Int(MetricKeys.QuestSuccess, 1)));
        p.MetricObservations.Add(Numeric("by-file", Id("F"), 100, "f.rws", metrics: Int(MetricKeys.QuestSuccess, 2)));
        p.MetricObservations.Add(Numeric("by-identity", Id("S", "Thing_Human1"), 50, "gone.rws", metrics: Int(MetricKeys.QuestSuccess, 4)));
        var v = View(p);
        Assert.Equal((7d, 3, 3), (Overall(v, MetricKeys.QuestSuccess).Value.Number, Overall(v, MetricKeys.QuestSuccess).Targets, Overall(v, MetricKeys.QuestSuccess).Valid));
        Assert.Equal(0, v.Unattributed);
    }

    [Fact]
    public void Attribution_NeverCreatesColonies_AndRejectsAmbiguousOrContradictingEvidence()
    {
        var p = new Profile();
        p.SaveObservations.Add(V1("x.rws", Id("X", "Thing_Human1,Thing_Human2"), 100));
        p.SaveObservations.Add(V1("y.rws", Id("X", "Thing_Human1,Thing_Human3"), 100));   // same fingerprint, another branch
        p.SaveObservations.Add(V1("z.rws", Id("Z"), 100));
        p.MetricObservations.Add(Numeric("unknown-colony", Id("nobody"), 10, metrics: Int(MetricKeys.QuestSuccess, 100)));
        p.MetricObservations.Add(Numeric("ambiguous", Id("X", "Thing_Human1"), 10, metrics: Int(MetricKeys.QuestSuccess, 100)));
        p.MetricObservations.Add(Numeric("contradicting", Id("other"), 100, "z.rws", metrics: Int(MetricKeys.QuestSuccess, 100)));
        var v = View(p);
        Assert.Equal(3, v.Colonies.Count);
        Assert.Equal(3, v.Unattributed);
        Assert.Equal((MetricCoverage.Unknown, 0), (Overall(v, MetricKeys.QuestSuccess).Value.Coverage, Overall(v, MetricKeys.QuestSuccess).Valid));
    }

    [Fact]
    public void ModCount_PeakAndMinimumSpanObservations_AndHaveNoOverall()
    {
        var p = new Profile();
        p.SaveObservations.Add(V1("a.rws", Id("A"), 300));
        p.MetricObservations.Add(Numeric("k1", Id("A"), 100, metrics: Int(MetricKeys.ModsLatest, 40)));
        p.MetricObservations.Add(Numeric("k2", Id("A"), 200, metrics: Int(MetricKeys.ModsLatest, 90)));
        p.MetricObservations.Add(Numeric("k3", Id("A"), 300, "a.rws", metrics: Int(MetricKeys.ModsLatest, 60)));
        var v = View(p);
        var key = KeyOf(p, "A");
        Assert.Equal(60d, Of(v, key, "colony.mods.latest").Value.Number);
        Assert.Equal(90d, Of(v, key, "colony.mods.peak").Value.Number);
        Assert.Equal(40d, Of(v, key, "colony.mods.min").Value.Number);
        Assert.DoesNotContain(v.Overall, r => r.Definition.Key.StartsWith("colony.mods.", StringComparison.Ordinal));
    }

    [Fact]
    public void FirstReach_OverallIsTheEarliest_AndNotReachedIsNotZero()
    {
        var p = new Profile();
        p.SaveObservations.Add(V1("a.rws", Id("A"), 300));
        p.SaveObservations.Add(V1("b.rws", Id("B"), 300));
        p.SaveObservations.Add(V1("c.rws", Id("C"), 300));
        p.MetricObservations.Add(Numeric("ka", Id("A"), 300, "a.rws", metrics: Real(MetricKeys.WealthTotalFirst100K, 12.5)));
        p.MetricObservations.Add(Numeric("kb", Id("B"), 300, "b.rws", metrics: (MetricKeys.WealthTotalFirst100K, new MetricValue { Coverage = MetricCoverage.SnapshotOnly })));
        p.MetricObservations.Add(Numeric("kc", Id("C"), 300, "c.rws", metrics: Real(MetricKeys.WealthTotalFirst100K, 30)));
        var v = View(p);
        var row = Overall(v, MetricKeys.WealthTotalFirst100K);
        Assert.Equal((12.5, 3, 3), (row.Value.Number!.Value, row.Targets, row.Valid));
        Assert.Equal(KeyOf(p, "A"), row.Value.ColonyKey);
        var b = Of(v, KeyOf(p, "B"), MetricKeys.WealthTotalFirst100K).Value;
        Assert.True(b.NotReached);
        Assert.Null(b.Number);

        p.MetricObservations.RemoveAll(o => o.Key != "kb");
        var none = Overall(View(p), MetricKeys.WealthTotalFirst100K).Value;
        Assert.True(none.NotReached);
    }

    [Fact]
    public void Sum_CarriesSignAndPartialness()
    {
        var p = new Profile();
        p.SaveObservations.Add(V1("a.rws", Id("A"), 300));
        p.SaveObservations.Add(V1("b.rws", Id("B"), 300));
        p.MetricObservations.Add(Numeric("ka", Id("A"), 300, "a.rws", metrics: Real(MetricKeys.WealthTotalNet, -500)));
        p.MetricObservations.Add(Numeric("kb", Id("B"), 300, "b.rws", metrics: Real(MetricKeys.WealthTotalNet, 200, MetricCoverage.Partial)));
        var row = Overall(View(p), MetricKeys.WealthTotalNet);
        Assert.Equal((-300d, MetricCoverage.Partial), (row.Value.Number, row.Value.Coverage));
    }

    [Fact]
    public void IndividualOnly_HasNoOverallRow()
    {
        var p = new Profile();
        p.SaveObservations.Add(V1("a.rws", Id("A"), 300));
        p.MetricObservations.Add(Numeric("ka", Id("A"), 300, "a.rws", metrics: Real(MetricKeys.WealthTotalFirst, 1234)));
        var v = View(p);
        Assert.DoesNotContain(v.Overall, r => r.Definition.Key == MetricKeys.WealthTotalFirst);
        Assert.Equal(1234d, Of(v, KeyOf(p, "A"), MetricKeys.WealthTotalFirst).Value.Number);
    }

    [Fact]
    public void Breakdown_IsSummedPerKey()
    {
        var p = new Profile();
        p.SaveObservations.Add(V1("a.rws", Id("A"), 300));
        p.SaveObservations.Add(V1("b.rws", Id("B"), 300));
        MetricObservation WithRoots(string key, string seed, params (string Root, long N)[] roots)
        {
            var m = Numeric(key, Id(seed), 300, seed.ToLowerInvariant() + ".rws");
            var e = new MetricEntry { Revision = MetricKeys.Revision(MetricKeys.QuestSuccessByRoot) };
            foreach (var (root, n) in roots)
                e.Values[root] = new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = n };
            m.Metrics[MetricKeys.QuestSuccessByRoot] = e;
            return m;
        }
        p.MetricObservations.Add(WithRoots("ka", "A", ("TradeRequest", 2), ("BanditCamp", 1)));
        p.MetricObservations.Add(WithRoots("kb", "B", ("TradeRequest", 3)));
        var row = Overall(View(p), MetricKeys.QuestSuccessByRoot);
        Assert.Equal(new[] { ("BanditCamp", 1d), ("TradeRequest", 5d) }, row.Breakdowns.Select(b => (b.Breakdown, b.Value.Number!.Value)));
        // B has no BanditCamp success: a known 0, not a missing value.
        Assert.Equal((2, MetricCoverage.SnapshotOnly), (row.Breakdowns[0].Valid, row.Breakdowns[0].Value.Coverage));
        Assert.Equal(6d, row.Value.Number);
    }

    [Fact]
    public void OtherRevision_IsNotMixedIn()
    {
        var p = TwoColonies();
        p.MetricObservations.Single(o => o.Key == "kb").Metrics[MetricKeys.QuestSuccessRate].Revision = 99;
        var row = Overall(View(p), MetricKeys.QuestSuccessRate);
        Assert.Equal((1d, 2d, 1, MetricCoverage.Partial), (row.Value.Numerator, row.Value.Denominator, row.Valid, row.Value.Coverage));
    }

    [Fact]
    public void ZeroOverZero_IsAKnownColony_ButShownAsNoValue()
    {
        var p = TwoColonies();
        p.SaveObservations.Add(V1("c.rws", Id("C"), 60000));
        p.MetricObservations.Add(Numeric("kc", Id("C"), 60000, "c.rws", metrics: (MetricKeys.QuestSuccessRate,
            new MetricValue { Coverage = MetricCoverage.SnapshotOnly, Numerator = 0, Denominator = 0 })));
        var v = View(p);
        var row = Overall(v, MetricKeys.QuestSuccessRate);
        Assert.Equal((10d, 12d, 3, 3, MetricCoverage.SnapshotOnly), (row.Value.Numerator, row.Value.Denominator, row.Targets, row.Valid, row.Value.Coverage));
        var c = Of(v, KeyOf(p, "C"), MetricKeys.QuestSuccessRate).Value;
        Assert.Null(c.Number);
        Assert.False(c.NotReached);
    }

    [Fact]
    public void AllZeroOverZero_StaysKnown_WithNoValue()
    {
        var p = new Profile();
        p.SaveObservations.Add(V1("a.rws", Id("A"), 300));
        p.SaveObservations.Add(V1("b.rws", Id("B"), 300));
        foreach (var (key, seed) in new[] { ("ka", "A"), ("kb", "B") })
            p.MetricObservations.Add(Numeric(key, Id(seed), 300, seed.ToLowerInvariant() + ".rws", metrics: (MetricKeys.QuestSuccessRate,
                new MetricValue { Coverage = MetricCoverage.SnapshotOnly, Numerator = 0, Denominator = 0 })));
        var row = Overall(View(p), MetricKeys.QuestSuccessRate);
        Assert.Equal(((double?)null, 0d, 0d, 2, 2, MetricCoverage.SnapshotOnly),
            (row.Value.Number, row.Value.Numerator, row.Value.Denominator, row.Targets, row.Valid, row.Value.Coverage));
    }

    [Fact]
    public void NotApplicableWindow_LeavesTheDenominatorWithoutMakingItPartial()
    {
        var p = new Profile();
        p.SaveObservations.Add(V1("a.rws", Id("A"), 300));
        p.SaveObservations.Add(V1("b.rws", Id("B"), 300));
        p.MetricObservations.Add(Numeric("ka", Id("A"), 300, "a.rws", metrics: Real(MetricKeys.WealthTotalMaxGain15D, 500)));
        p.MetricObservations.Add(Numeric("kb", Id("B"), 300, "b.rws",
            metrics: (MetricKeys.WealthTotalMaxGain15D, new MetricValue { Coverage = MetricCoverage.NotApplicable })));
        var row = Overall(View(p), MetricKeys.WealthTotalMaxGain15D);
        Assert.Equal((500d, 1, 1, MetricCoverage.SnapshotOnly), (row.Value.Number, row.Targets, row.Valid, row.Value.Coverage));
    }

    [Fact]
    public void Registry_KeysAreUniqueAndLabelled()
    {
        Assert.Equal(MetricRegistry.All.Count, MetricRegistry.All.Select(d => d.Key).Distinct().Count());
        Assert.All(MetricRegistry.All, d => Assert.StartsWith("RWS_Metric_", d.LabelKey));
        Assert.Equal(MetricRegistry.All.Count, MetricRegistry.All.Select(d => d.LabelKey).Distinct().Count());
    }

    // Regression: Phase B facilities "latest" must not select the historical peak.
    [Fact]
    public void RunningGenerators_LatestSnapshotHasTheCurrentCountAndBreakdown()
    {
        var p = new Profile();
        var identity = Id("A");
        p.SaveObservations.Add(V1("a.rws", identity, 300));

        void AddSnapshot(string key, int tick, long count, long solar, long wood)
        {
            var entry = MetricEntry.Of(MetricKeys.Revision(FacilitiesSnapshot.RunningGenerators),
                new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = count });
            entry.Values["SolarGenerator"] = new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = solar };
            if (wood > 0)
                entry.Values["WoodFiredGenerator"] = new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = wood };
            var obs = new MetricObservation
            {
                Source = MetricObservationSource.SaveSnapshot, Key = key, Identity = identity.Clone(),
                SaveTick = tick, ObservedUtc = T0.AddMinutes(tick),
            };
            obs.Metrics[FacilitiesSnapshot.RunningGenerators] = entry;
            p.MetricObservations.Add(obs);
        }

        AddSnapshot("old", 100, 3, 2, 1);
        AddSnapshot("new", 200, 1, 1, 0);
        var def = SnapshotCatalog.Definitions.Single(d => d.Key == FacilitiesSnapshot.RunningGenerators);
        var colonies = CampaignMatcher.BuildColonies(p);
        var view = MetricCalculator.BuildView(p, colonies, StatsCalculator.Calculate(colonies, new StatsOptions()), new[] { def });
        var latest = Assert.Single(Assert.Single(view.Colonies).Rows);
        Assert.Equal(1d, latest.Value.Number);
        Assert.Equal(200, latest.Value.ThroughTick);
        Assert.Equal(1d, Assert.Single(latest.Breakdowns).Value.Number);
        Assert.Equal("SolarGenerator", latest.Breakdowns[0].Breakdown);
        var overall = Assert.Single(view.Overall);
        Assert.Equal(1d, overall.Value.Number);
        Assert.Equal(1d, Assert.Single(overall.Breakdowns).Value.Number);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PersonaWeaponPeak_WithoutRoyaltyIsNotApplicable_ButWithRoyaltyZeroIsKnown(bool royalty)
    {
        var p = new Profile();
        var identity = Id("A");
        p.SaveObservations.Add(V1("a.rws", identity, 300));
        var obs = new MetricObservation
        {
            Source = MetricObservationSource.SaveSnapshot, Key = "one", Identity = identity.Clone(),
            SaveTick = 100, ObservedUtc = T0, ActiveDlcIds = royalty ? new List<string> { AcquisitionUnits.Royalty } : new List<string>(),
        };
        obs.Metrics[StockSnapshot.PersonaWeaponsPeak] = MetricEntry.Of(
            MetricKeys.Revision(StockSnapshot.PersonaWeaponsPeak),
            new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = 0 });
        p.MetricObservations.Add(obs);
        var def = SnapshotCatalog.Definitions.Single(d => d.Key == StockSnapshot.PersonaWeaponsPeak);
        var colonies = CampaignMatcher.BuildColonies(p);
        var view = MetricCalculator.BuildView(p, colonies, StatsCalculator.Calculate(colonies, new StatsOptions()), new[] { def });
        var row = Assert.Single(Assert.Single(view.Colonies).Rows);
        if (royalty)
        {
            Assert.Equal(1, row.Targets);
            Assert.Equal(0d, row.Value.Number);
        }
        else
        {
            Assert.Equal(0, row.Targets);
            Assert.Equal(MetricCoverage.NotApplicable, row.Value.Coverage);
            Assert.Equal(0, Assert.Single(view.Overall).Targets);
        }
    }

}
