namespace RimworldStatistics.Core.Tests;

/// <summary>Optimized per-colony by-key lookup must preserve pre-v1.4 aggregation semantics.</summary>
public sealed class IndexedBreakdownAggregationTests
{
    private const string Key = TradeFamily.SalesUnits;
    private static readonly DateTime Stamp = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);

    private static ColonyIdentity Identity(string seed) => new()
    {
        SeedString = seed, PersistentRandomValue = 1, StartingTile = "1,0", GameStartAbsTick = 0,
        FirstWealthBits = 1, StartingPawnIds = new List<string> { "Thing_Human" + seed },
        ModCampaignId = "fixture-" + seed,
    };

    private static Profile Build(int entries, bool incomplete)
    {
        var p = new Profile();
        foreach (var seed in new[] { "A", "B" })
        {
            var identity = Identity(seed);
            var file = seed + ".rws";
            p.SaveObservations.Add(new SaveObservation
            {
                FileName = file, FileSize = 100, LastWriteUtc = Stamp,
                ReaderVersion = SaveReader.Version, Status = ObservationStatus.Ok,
                Identity = identity.Clone(), Values = { TicksGame = 60000 },
            });
            var observation = new MetricObservation
            {
                Source = MetricRegistry.Find(Key)!.Source,
                FormatVersion = SaveReader.Version, Key = "checkpoint-" + seed,
                Identity = identity.Clone(), SaveTick = 60000, ObservedUtc = Stamp,
                ModSetHash = new string('a', 64),
            };
            observation.FileAliases.Add(new MetricFileAlias { FileName = file, FileSize = 100, LastWriteUtc = Stamp });
            var entry = MetricEntry.Of(MetricKeys.Revision(Key),
                new MetricValue
                {
                    Coverage = MetricCoverage.Complete,
                    IntegerValue = (long)entries * (entries + 1) / 2
                        + (incomplete && seed == "B" ? 1 : 0),
                });
            for (var i = 1; i <= entries; i++)
                entry.Values["SyntheticItem" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture)] =
                    new MetricValue { Coverage = MetricCoverage.Complete, IntegerValue = i };
            observation.Metrics[Key] = entry;
            p.MetricObservations.Add(observation);
        }
        return p;
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(64, false)]
    [InlineData(500, false)]
    [InlineData(64, true)]
    public void IndexedSplit_ExactNumbersAndMissingEvidenceStayConsistent(int entries, bool incomplete)
    {
        var p = Build(entries, incomplete);
        var colonies = CampaignMatcher.BuildColonies(p);
        Assert.Equal(2, colonies.Count);
        var d = MetricRegistry.Find(Key)!;
        var derived = MetricRegistry.Find(CrossColonyCatalog.TopPrefix + Key)!;
        var view = MetricCalculator.BuildView(p, colonies,
            StatsCalculator.Calculate(colonies, new StatsOptions()), new[] { d, derived });
        var total = view.Overall.Single(x => x.Definition.Key == Key);
        var ranking = view.Overall.Single(x => x.Definition.Key == derived.Key);
        Assert.Equal(entries, total.Breakdowns.Count);
        Assert.All(total.Breakdowns, row =>
        {
            Assert.Equal(2, row.Targets);
            Assert.Equal(2, row.Valid);
            Assert.Equal(2 * int.Parse(row.Breakdown["SyntheticItem".Length..], System.Globalization.CultureInfo.InvariantCulture), row.Value.Number);
        });
        Assert.Equal((double)entries, ranking.Value.Number);
        Assert.Equal(2 * entries, ranking.Breakdowns[0].Value.Number);
        Assert.Equal(incomplete ? MetricCoverage.Partial : MetricCoverage.Complete, ranking.Value.Coverage);
        Assert.Equal(2, ranking.Breakdowns[0].Breakdowns.Count);
    }

    [Fact]
    public void MissingItem_OnlyFullReconciledParentCanProveZero()
    {
        var p = Build(2, false);
        var a = p.MetricObservations.Single(o => o.Key == "checkpoint-A");
        var b = p.MetricObservations.Single(o => o.Key == "checkpoint-B");
        var aEntry = a.Metrics[Key];
        var bEntry = b.Metrics[Key];
        aEntry.Values.Remove("SyntheticItem0002");
        aEntry.Values[MetricEntry.Scalar].IntegerValue = 1;
        bEntry.Values.Remove("SyntheticItem0001");
        bEntry.Values[MetricEntry.Scalar].IntegerValue = 2;
        var cols = CampaignMatcher.BuildColonies(p);
        var view = MetricCalculator.BuildView(p, cols, StatsCalculator.Calculate(cols, new StatsOptions()),
            new[] { MetricRegistry.Find(Key)! });
        var row = Assert.Single(view.Overall);
        Assert.Equal(1d, row.Breakdowns.Single(r => r.Breakdown == "SyntheticItem0001").Value.Number);
        Assert.Equal(2d, row.Breakdowns.Single(r => r.Breakdown == "SyntheticItem0002").Value.Number);
        Assert.All(row.Breakdowns, r => Assert.Equal((2, 2, MetricCoverage.Complete),
            (r.Targets, r.Valid, r.Value.Coverage)));

        // A scalar that no longer matches its captured split cannot prove a missing item was 0.
        aEntry.Values[MetricEntry.Scalar].IntegerValue = 10;
        view = MetricCalculator.BuildView(p, cols, StatsCalculator.Calculate(cols, new StatsOptions()),
            new[] { MetricRegistry.Find(Key)! });
        row = Assert.Single(view.Overall);
        var incompleteMissing = row.Breakdowns.Single(r => r.Breakdown == "SyntheticItem0002");
        Assert.Equal((2, 1, MetricCoverage.Partial),
            (incompleteMissing.Targets, incompleteMissing.Valid, incompleteMissing.Value.Coverage));
        Assert.Equal(2d, incompleteMissing.Value.Number);
    }
}
