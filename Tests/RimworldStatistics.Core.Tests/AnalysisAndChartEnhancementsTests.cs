using System.Diagnostics;
using System.Reflection;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class AnalysisAndChartEnhancementsTests
{
    private const int Day = StatFormat.TicksPerDay;
    private static readonly DateTime Now = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
    private static readonly MetricDefinition AdditiveMetric = MetricRegistry.Find(ResearchFamily.Completed)!;
    private static readonly MetricDefinition SecondAdditiveMetric = MetricRegistry.ImplementedCatalog
        .First(d => d.Key != AdditiveMetric.Key && MetricRates.IsAdditiveTotal(d));
    private static readonly MetricDefinition NonAdditiveMetric = MetricRegistry.Find(MetricKeys.MoodMean)!;

    [Fact]
    public void SmartGroupSpec_RoundTripsIdsIncludingColonsSpacesAndRejectsMalformedStrings()
    {
        var specs = new[]
        {
            new SmartGroupSpec(SmartGroupKind.PermadeathOnly),
            new SmartGroupSpec(SmartGroupKind.ReloadAnytimeOnly),
            new SmartGroupSpec(SmartGroupKind.EndingReached),
            new SmartGroupSpec(SmartGroupKind.MinSurvivalDays, MinDays: 60),
            new SmartGroupSpec(SmartGroupKind.Storyteller, "Cassandra Classic"),
            new SmartGroupSpec(SmartGroupKind.Difficulty, "LosingIsFun"),
            new SmartGroupSpec(SmartGroupKind.Scenario, "Crashlanded: Custom: Part 2"),
            new SmartGroupSpec(SmartGroupKind.StartingBiome, "TemperateForest"),
        };

        foreach (var spec in specs)
        {
            Assert.True(SmartGroupSpec.IsSmartGroupId(spec.Id));
            Assert.True(SmartGroupSpec.TryParse(spec.Id, out var parsed));
            Assert.Equal(spec, parsed);
        }

        foreach (var bad in new string?[]
        {
            null,
            "",
            "group:abc",
            "smart:",
            "smart:unknown",
            "smart:days:0",
            "smart:days:-5",
            "smart:storyteller:5:TooLongValue",
            "smart:storyteller:0:",
            "smart:biome:3:   ",
        })
        {
            Assert.False(SmartGroupSpec.TryParse(bad, out _));
        }
    }

    [Fact]
    public void SmartGroups_DynamicallyFilterProfileViewsAcrossAllKindsAndUpdateAsColoniesProgress()
    {
        using var tmp = new TempDir();
        var session = new ProfileSession(new ProfileStore(tmp.Path), () => Now);

        SaveObservation MakeSave(string file, string campaignId, string seed, int ticks, bool permadeath, string storyteller, string difficulty, string scenario) => new()
        {
            FileName = file,
            FileSize = 100,
            LastWriteUtc = Now,
            ReaderVersion = SaveReader.Version,
            Status = ObservationStatus.Ok,
            Identity = new ColonyIdentity
            {
                SeedString = seed,
                StartingTile = "1,0",
                PersistentRandomValue = 1,
                FirstWealthBits = 1,
                StartingPawnIds = ["Thing_Human1"],
                ModCampaignId = campaignId,
            },
            Values =
            {
                TicksGame = ticks,
            },
            Display = new DisplayInfo
            {
                Permadeath = permadeath,
                Storyteller = storyteller,
                Difficulty = difficulty,
                Scenario = scenario,
            },
        };

        MetricObservation MakeBiomeSnapshot(SaveObservation save, string biome)
        {
            var obs = new MetricObservation
            {
                Source = MetricObservationSource.SaveSnapshot,
                FormatVersion = 1,
                Key = save.FileName + ":snap",
                Identity = save.Identity.Clone(),
                SaveTick = save.Values.TicksGame,
                ObservedUtc = Now,
            };
            foreach (var kv in ColonyStartSnapshot.Build(biome))
                obs.Metrics[kv.Key] = kv.Value;
            return obs;
        }

        var saveA = MakeSave("a.rws", "camp-a", "SeedA", 100 * Day, permadeath: true, "Cassandra", "StriveToSurvive", "Crashlanded");
        var saveB = MakeSave("b.rws", "camp-b", "SeedB", 20 * Day, permadeath: false, "Phoebe", "AdventureStory", "Lost Tribe");

        session.Mutate(p =>
        {
            p.SaveObservations.AddRange([saveA, saveB]);
            p.MetricObservations.AddRange([
                MakeBiomeSnapshot(saveA, "TemperateForest"),
                MakeBiomeSnapshot(saveB, "ExtremeDesert"),
            ]);
            // Colony A reached normal ship launch; Colony C is ending-only (dev mode) with no observed save.
            p.EndingEvents.Add(new EndingEvent
            {
                CampaignId = "camp-a",
                Type = EndingType.Ship,
                Ordinal = 0,
                TicksGame = 100 * Day,
                RealTimeUtc = Now,
                DevMode = false,
            });
            p.LiveCampaigns.Add(new LiveCampaign
            {
                CampaignId = "camp-c-dev",
                Identity = new ColonyIdentity
                {
                    SeedString = "SeedC",
                    StartingTile = "2,0",
                    PersistentRandomValue = 9,
                    FirstWealthBits = 9,
                    StartingPawnIds = ["Thing_Human9"],
                    ModCampaignId = "camp-c-dev",
                },
            });
            p.EndingEvents.Add(new EndingEvent
            {
                CampaignId = "camp-c-dev",
                Type = EndingType.Archonexus,
                Ordinal = 0,
                TicksGame = 200 * Day,
                RealTimeUtc = Now,
                DevMode = true,
            });
        }, SaveMode.None);

        var baseOpts = new StatsOptions { IncludeDevMode = false };
        var allView = session.GetView(baseOpts);
        var keyA = allView.Colonies.Single(c => c.Observations.Any(o => o.Identity.SeedString == "SeedA")).Key;
        var keyB = allView.Colonies.Single(c => c.Observations.Any(o => o.Identity.SeedString == "SeedB")).Key;

        string[] InScopeKeys(SmartGroupSpec spec, StatsOptions? opts = null)
            => session.GetView(opts ?? baseOpts, scope: ViewScope.ForSmartGroup(spec))
                .Stats.Colonies.Where(c => !c.Excluded && !c.OutOfScope)
                .Select(c => c.Key)
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToArray();

        // Permadeath vs ReloadAnytime: ending-only colony C has HasObservedPoint == false and must NEVER be treated as ReloadAnytime
        var devOpts = new StatsOptions { IncludeDevMode = true };
        Assert.Equal([keyA], InScopeKeys(new SmartGroupSpec(SmartGroupKind.PermadeathOnly), devOpts));
        Assert.Equal([keyB], InScopeKeys(new SmartGroupSpec(SmartGroupKind.ReloadAnytimeOnly), devOpts));

        // EndingReached respects IncludeDevMode
        Assert.Equal([keyA], InScopeKeys(new SmartGroupSpec(SmartGroupKind.EndingReached), baseOpts));
        Assert.Equal(2, InScopeKeys(new SmartGroupSpec(SmartGroupKind.EndingReached), devOpts).Length);

        // Storyteller, Difficulty, Scenario, StartingBiome
        Assert.Equal([keyA], InScopeKeys(new SmartGroupSpec(SmartGroupKind.Storyteller, "Cassandra")));
        Assert.Equal([keyB], InScopeKeys(new SmartGroupSpec(SmartGroupKind.Difficulty, "AdventureStory")));
        Assert.Equal([keyB], InScopeKeys(new SmartGroupSpec(SmartGroupKind.Scenario, "Lost Tribe")));
        Assert.Equal([keyA], InScopeKeys(new SmartGroupSpec(SmartGroupKind.StartingBiome, "TemperateForest")));
        Assert.Equal([keyB], InScopeKeys(new SmartGroupSpec(SmartGroupKind.StartingBiome, "ExtremeDesert")));

        // MinSurvivalDays >= 60 initially matches only Colony A (100d); when Colony B advances to 75d, it joins automatically!
        var days60 = new SmartGroupSpec(SmartGroupKind.MinSurvivalDays, MinDays: 60);
        Assert.Equal([keyA], InScopeKeys(days60));

        session.Mutate(p =>
        {
            p.SaveObservations.Add(MakeSave("b2.rws", "camp-b", "SeedB", 75 * Day, permadeath: false, "Phoebe", "AdventureStory", "Lost Tribe"));
        }, SaveMode.None);

        var expectedBoth = new[] { keyA, keyB }.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.Equal(expectedBoth, InScopeKeys(days60));

        // Discover(scopedView) counts across all non-excluded colonies even while a narrow scope is active
        var narrowView = session.GetView(baseOpts, scope: ViewScope.ForSmartGroup(new SmartGroupSpec(SmartGroupKind.Storyteller, "Cassandra")));
        var discovered = SmartGroupEvaluator.Discover(narrowView);
        Assert.Equal(2, discovered.Single(c => c.Spec == days60).ColonyCount);
        Assert.Contains(discovered, c => c.Spec == new SmartGroupSpec(SmartGroupKind.StartingBiome, "TemperateForest") && c.ColonyCount == 1);
        Assert.Contains(discovered, c => c.Spec == new SmartGroupSpec(SmartGroupKind.StartingBiome, "ExtremeDesert") && c.ColonyCount == 1);
    }

    [Fact]
    public void ScatterColorGrouping_AssignsCategoriesCollapsesOverflowDetectsMixedClustersAndHasNoRegressionOrCorrelation()
    {
        ColonyFixture MakeCategorized(string key, double x, double y, bool observed = true, bool? permadeath = null,
            int endings = 0, string? difficulty = null, string? storyteller = null)
        {
            var stats = new ColonyStats
            {
                Key = key,
                HasObservedPoint = observed,
                Permadeath = permadeath,
                Difficulty = difficulty,
                Storyteller = storyteller,
                MaxTicks = 10 * Day,
                WealthMax = 50_000,
                PeakWealthKnown = true,
                LastWealthKnown = true,
                SaveCount = observed ? 1 : 0,
                GameVersions = [],
                EndingCounts = endings > 0
                    ? new Dictionary<EndingType, int> { [EndingType.Ship] = endings }
                    : new Dictionary<EndingType, int>(),
            };
            var numeric = new NumericColony
            {
                Key = key,
                Rows =
                [
                    Row(AdditiveMetric, x, sinceTick: 0, throughTick: 10 * Day),
                    Row(NonAdditiveMetric, y, MetricCoverage.SnapshotOnly, sinceTick: null, throughTick: 10 * Day),
                ],
            };
            return new ColonyFixture(stats, numeric);
        }

        var view = View(
            MakeCategorized("c1", 10, 0.5, permadeath: true, endings: 1, difficulty: "D1", storyteller: "Cassandra"),
            MakeCategorized("c2", 10, 0.5, permadeath: false, endings: 0, difficulty: "D1", storyteller: "Phoebe"),
            MakeCategorized("c3", 20, 0.6, permadeath: true, endings: 0, difficulty: "D2", storyteller: "Cassandra"),
            MakeCategorized("c4", 30, 0.7, observed: false, permadeath: false, endings: 1, difficulty: "D3", storyteller: null),
            MakeCategorized("c5", 40, 0.8, permadeath: false, endings: 0, difficulty: "D4", storyteller: "Randy"),
            MakeCategorized("c6", 50, 0.9, permadeath: false, endings: 0, difficulty: "D5", storyteller: "Randy"));

        var plot = MetricScatterPlot.Build(view, AdditiveMetric.Key, NonAdditiveMetric.Key)!;
        Assert.Equal(6, plot.Points.Count);

        // Permadeath: c4 has observed == false so it becomes Unknown even though raw Permadeath field was false
        var perm = ScatterColorGrouping.Assign(plot, view.Stats, ScatterColorMode.Permadeath);
        Assert.Equal(
            [ScatterColorBucketKind.Permadeath, ScatterColorBucketKind.ReloadAnytime, ScatterColorBucketKind.Unknown],
            perm.Buckets.Select(b => b.Kind));
        Assert.Equal(2, perm.Buckets[0].PointCount);
        Assert.Equal(3, perm.Buckets[1].PointCount);
        Assert.Equal(1, perm.Buckets[2].PointCount);

        // EndingReached: 2 reached, 4 not reached
        var end = ScatterColorGrouping.Assign(plot, view.Stats, ScatterColorMode.EndingReached);
        Assert.Equal([ScatterColorBucketKind.EndingReached, ScatterColorBucketKind.EndingNotReached], end.Buckets.Select(b => b.Kind));
        Assert.Equal([2, 4], end.Buckets.Select(b => b.PointCount));

        // Difficulty with maxDistinctCategories = 3 over 5 distinct values -> top 2 named + Other
        var diff = ScatterColorGrouping.Assign(plot, view.Stats, ScatterColorMode.Difficulty, maxDistinctCategories: 3);
        Assert.Equal(3, diff.Buckets.Count);
        Assert.Equal((ScatterColorBucketKind.CategoryValue, "D1", 2), (diff.Buckets[0].Kind, diff.Buckets[0].RawValue, diff.Buckets[0].PointCount));
        Assert.Equal((ScatterColorBucketKind.CategoryValue, "D2", 1), (diff.Buckets[1].Kind, diff.Buckets[1].RawValue, diff.Buckets[1].PointCount));
        Assert.Equal((ScatterColorBucketKind.Other, (string?)null, 3), (diff.Buckets[2].Kind, diff.Buckets[2].RawValue, diff.Buckets[2].PointCount));

        // Storyteller includes Unknown bucket for c4
        var st = ScatterColorGrouping.Assign(plot, view.Stats, ScatterColorMode.Storyteller);
        Assert.Contains(st.Buckets, b => b.Kind == ScatterColorBucketKind.Unknown && b.PointCount == 1);

        // Cluster at (10, 0.5) has c1 and c2: homogeneous under Difficulty ("D1"), mixed under Permadeath -> -1
        var clusters = plot.Cluster(400, 300, 8);
        var sharedSpot = clusters.Single(c => c.PointIndices.Count == 2);
        Assert.Equal(0, ScatterColorGrouping.ResolveClusterBucket(sharedSpot, diff));
        Assert.Equal(-1, ScatterColorGrouping.ResolveClusterBucket(sharedSpot, perm));

        // Strict design invariant: zero regression/trendline/correlation symbols anywhere in scatter types
        var forbiddenTerms = new[] { "Regression", "Correlation", "Pearson", "Spearman", "TrendLine", "Slope", "Intercept", "RSquared" };
        foreach (var type in new[] { typeof(MetricScatterPlot), typeof(ScatterColorGrouping), typeof(ScatterAxis), typeof(ScatterPoint), typeof(ScatterColorAssignment) })
        {
            var members = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            foreach (var member in members)
            {
                foreach (var forbidden in forbiddenTerms)
                    Assert.DoesNotContain(forbidden, member.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void PerDayRateMode_UsesStrictObservedIntervalsAcrossComparisonLeaderboardAndScatterPlot()
    {
        // Colony "fast": 30 completed over [60_000, 360_000] (5.0 recorded days -> 6.0/day), even though colony age is 20 days!
        // Colony "slow": 100 completed over [0, 1_500_000] (25.0 recorded days -> 4.0/day)
        // Colony "short": 10 completed over [0, 30_000] (0.5 days < 1.0 day -> excluded from /day!)
        // Colony "partial": 50 completed over [0, 600_000] with Partial coverage -> excluded from /day!
        var fast = Make("fast", ticks: 20 * Day, rows:
        [
            Row(AdditiveMetric, 30, MetricCoverage.Complete, sinceTick: 1 * Day, throughTick: 6 * Day),
            Row(SecondAdditiveMetric, 15, MetricCoverage.Complete, sinceTick: 1 * Day, throughTick: 6 * Day),
            Row(NonAdditiveMetric, 0.75, MetricCoverage.SnapshotOnly, sinceTick: null, throughTick: 6 * Day),
        ]);
        var slow = Make("slow", ticks: 25 * Day, rows:
        [
            Row(AdditiveMetric, 100, MetricCoverage.Complete, sinceTick: 0, throughTick: 25 * Day),
            Row(SecondAdditiveMetric, 50, MetricCoverage.Complete, sinceTick: 0, throughTick: 25 * Day),
            Row(NonAdditiveMetric, 0.55, MetricCoverage.SnapshotOnly, sinceTick: null, throughTick: 25 * Day),
        ]);
        var shortInterval = Make("short", ticks: 1 * Day, rows:
        [
            Row(AdditiveMetric, 10, MetricCoverage.Complete, sinceTick: 0, throughTick: Day / 2),
            Row(SecondAdditiveMetric, 5, MetricCoverage.Complete, sinceTick: 0, throughTick: Day / 2),
        ]);
        var partial = Make("partial", ticks: 10 * Day, rows:
        [
            Row(AdditiveMetric, 50, MetricCoverage.Partial, sinceTick: 0, throughTick: 10 * Day),
            Row(SecondAdditiveMetric, 20, MetricCoverage.Complete, sinceTick: 0, throughTick: 10 * Day),
        ]);

        var overallRow = new NumericRow
        {
            Definition = AdditiveMetric,
            Value = new NumericValue { Number = 190, Coverage = MetricCoverage.Complete, SinceTick = 0, ThroughTick = 25 * Day },
            Targets = 4,
            Valid = 4,
        };
        var view = ViewWithOverall([overallRow], fast, slow, shortInterval, partial);

        // 1. Comparison.Build(..., perDay: true)
        var compRows = Comparison.Build(
            [
                (new ScopeTarget(ViewScope.All), view),
                (new ColonyTarget("fast"), view),
                (new ColonyTarget("slow"), view),
                (new ColonyTarget("partial"), view),
            ],
            [AdditiveMetric.Key, NonAdditiveMetric.Key],
            perDay: true);

        var compAdditive = compRows.Single(r => r.MetricKey == AdditiveMetric.Key);
        Assert.Null(compAdditive.Values[0]); // Multi-colony ScopeTarget is NEVER divided into a cross-colony /day!
        Assert.Equal(6.0, compAdditive.Values[1]!.Number!.Value, 6); // 30 / 5.0 recorded days (not 30 / 20 colony days!)
        Assert.Equal(4.0, compAdditive.Values[2]!.Number!.Value, 6); // 100 / 25.0 days
        Assert.Null(compAdditive.Values[3]); // Partial coverage excluded

        var compNonAdditive = compRows.Single(r => r.MetricKey == NonAdditiveMetric.Key);
        Assert.All(compNonAdditive.Values, Assert.Null);

        // 2. LeaderboardBuilder in perDay mode: "fast" (6.0/day) ranks #1 ahead of "slow" (4.0/day) despite smaller raw total (30 < 100)
        Assert.True(LeaderboardBuilder.CanRankPerDay(view, AdditiveMetric.Key));
        Assert.False(LeaderboardBuilder.CanRankPerDay(view, NonAdditiveMetric.Key));
        Assert.False(LeaderboardBuilder.CanRankPerDay(view, BasicMetrics.Days));

        var rateKeys = LeaderboardBuilder.RankableKeys(view, perDayOnly: true);
        Assert.Contains(AdditiveMetric.Key, rateKeys);
        Assert.DoesNotContain(NonAdditiveMetric.Key, rateKeys);
        Assert.DoesNotContain(BasicMetrics.Days, rateKeys);

        var board = LeaderboardBuilder.Build(view, AdditiveMetric.Key, descending: true, perDay: true);
        Assert.Equal(4, board.Targets);
        Assert.Equal(2, board.Measured);
        Assert.True(board.HasPartial);
        Assert.Equal(["fast", "slow"], board.Entries.Select(e => e.ColonyKey));
        Assert.Equal(6.0, board.Entries[0].Value, 6);
        Assert.Equal(5.0, board.Entries[0].RecordedDays!.Value, 6);
        Assert.Equal(4.0, board.Entries[1].Value, 6);
        Assert.Equal(25.0, board.Entries[1].RecordedDays!.Value, 6);

        // 3. MetricScatterPlot in perDay mode (X = AdditiveMetric /day, Y = NonAdditiveMetric raw)
        Assert.True(MetricScatterPlot.CanPlotPerDay(view, AdditiveMetric.Key));
        Assert.False(MetricScatterPlot.CanPlotPerDay(view, NonAdditiveMetric.Key));
        Assert.Null(MetricScatterPlot.Build(view, NonAdditiveMetric.Key, AdditiveMetric.Key, xPerDay: true, yPerDay: false));

        var scatter = MetricScatterPlot.Build(view, AdditiveMetric.Key, SecondAdditiveMetric.Key, xPerDay: true, yPerDay: false)!;
        Assert.True(scatter.X.PerDay);
        Assert.False(scatter.Y.PerDay);
        Assert.Equal(["fast", "slow"], scatter.Points.Select(p => p.ColonyKey));
        Assert.Equal((6.0, 15.0), (scatter.Points[0].X.Number, scatter.Points[0].Y.Number));
        Assert.Equal(5.0, scatter.Points[0].X.RecordedDays!.Value, 6);
        Assert.Null(scatter.Points[0].Y.RecordedDays);

        // Both "short" (<1d) and "partial" are reported under IncompleteRateInterval on X, distinct from Missing
        Assert.Equal(2, scatter.Count(ScatterExclusionReason.IncompleteRateInterval, ScatterAxes.X));
        Assert.Equal(0, scatter.Count(ScatterExclusionReason.Missing));
    }

    [Fact]
    public void MetricHistoryPlotThin_Applies1PxMinMaxEnvelopePreservingPeaksSparsePointsGapsAndBranches()
    {
        // Construct irregular save density across [0d, 200d] mapped onto width = 200px (1 day per 1px column):
        // - Day 10 (all inside pixel column 10): 500 saves oscillating with first=100, true min=7 (at #120), true max=9999 (at #340), last=250
        // - Day 20..110: 10 sparse saves (1 every 10 days), plus a null gap at Day 120, a Partial point at Day 130,
        //   a branch child at Day 150 continuing Day 50, and an Unlinked point at Day 200.
        var list = new List<MetricHistoryPoint>();
        for (var i = 0; i < 500; i++)
        {
            var tick = 10 * Day + i * 50; // 10d + up to 24,950 ticks (< 11d, so all fall in px = 10 for width=200 over 0..200d)
            var val = i switch
            {
                0 => 100.0,
                120 => 7.0,
                340 => 9999.0,
                499 => 250.0,
                _ => 200.0 + (i % 17),
            };
            list.Add(new MetricHistoryPoint(tick, new NumericValue { Number = val, Coverage = MetricCoverage.Complete }, ConnectToPrevious: i > 0));
        }

        var sparseTicks = new List<int>();
        var day50OriginalIndex = -1;
        for (var d = 20; d <= 110; d += 10)
        {
            var tick = d * Day;
            sparseTicks.Add(tick);
            if (d == 50)
                day50OriginalIndex = list.Count;
            list.Add(new MetricHistoryPoint(tick, new NumericValue { Number = 300.0 + d, Coverage = MetricCoverage.Complete }, ConnectToPrevious: true));
        }

        // Null gap at 120d
        list.Add(new MetricHistoryPoint(120 * Day, null, ConnectToPrevious: false));
        // Partial point at 130d after gap
        list.Add(new MetricHistoryPoint(130 * Day, new NumericValue { Number = 510, Coverage = MetricCoverage.Partial }, ConnectToPrevious: false));
        // Branch child at 150d continuing Day 50
        list.Add(new MetricHistoryPoint(150 * Day, new NumericValue { Number = 888, Coverage = MetricCoverage.Complete })
        {
            ContinuesFrom = day50OriginalIndex,
            SkipsOnlyOtherBranches = true,
        });
        // Unlinked point at 200d
        list.Add(new MetricHistoryPoint(200 * Day, new NumericValue { Number = 1234, Coverage = MetricCoverage.Complete })
        {
            Unlinked = true,
        });

        var thinned = MetricHistoryPlot.Thin(list, width: 200, axisFirstTick: 0, axisLastTick: 200 * Day, isZoomed: false);

        // Day 10's 500 clustered saves must collapse to exactly 4 chronological envelope points: (first=100, min=7, max=9999, last=250)
        var day10Kept = thinned.Where(p => p.Tick >= 10 * Day && p.Tick < 11 * Day).ToArray();
        Assert.Equal(4, day10Kept.Length);
        Assert.Equal([100.0, 7.0, 9999.0, 250.0], day10Kept.Select(p => p.Value!.Number!.Value));

        // 100% of sparse saves on Days 20..110 are preserved untouched
        foreach (var sparseTick in sparseTicks)
            Assert.Contains(thinned, p => p.Tick == sparseTick);

        // Gap, Partial, Branch ContinuesFrom, and Unlinked are 100% preserved and validly remapped
        Assert.Contains(thinned, p => p.Tick == 120 * Day && p.Value is null);
        Assert.Contains(thinned, p => p.Tick == 130 * Day && p.Value?.Coverage == MetricCoverage.Partial);
        var unlinked = thinned.Single(p => p.Tick == 200 * Day);
        Assert.True(unlinked.Unlinked);

        var branchChildIndex = thinned.ToList().FindIndex(p => p.Tick == 150 * Day);
        var predIndex = thinned[branchChildIndex].Predecessor(branchChildIndex);
        Assert.True(predIndex >= 0);
        Assert.Equal(50 * Day, thinned[predIndex].Tick);
        Assert.True(thinned[branchChildIndex].SkipsOnlyOtherBranches);

        // When zoomed in and point count <= width * 2, Thin returns the raw slice untouched
        var rawWhenZoomed = MetricHistoryPlot.Thin(list, width: 300, axisFirstTick: 10 * Day, axisLastTick: 200 * Day, isZoomed: true);
        Assert.Same(list, rawWhenZoomed);
    }

    [Fact]
    public void MetricHistoryPlotThin_20000PointsCompletesQuicklyWithin4PointsPerPixelBudget()
    {
        var points = new MetricHistoryPoint[20_000];
        for (var i = 0; i < points.Length; i++)
        {
            var val = Math.Sin(i * 0.05) * 1000.0 + (i % 97 == 0 ? 5000.0 : 0.0);
            points[i] = new MetricHistoryPoint(
                i * 1500,
                new NumericValue { Number = val, Coverage = MetricCoverage.SnapshotOnly },
                ConnectToPrevious: i > 0);
        }

        // Warm-up
        _ = MetricHistoryPlot.Thin(points, 600);

        var sw = Stopwatch.StartNew();
        var thinned = MetricHistoryPlot.Thin(points, 600);
        sw.Stop();

        Assert.InRange(thinned.Count, 600, 600 * 4);
        Assert.Equal(points.Max(p => p.Value!.Number!.Value), thinned.Max(p => p.Value!.Number!.Value), 9);
        Assert.Equal(points.Min(p => p.Value!.Number!.Value), thinned.Min(p => p.Value!.Number!.Value), 9);
        Assert.InRange(sw.Elapsed.TotalMilliseconds, 0, 500);
    }

    private sealed record ColonyFixture(ColonyStats Stats, NumericColony Numeric);

    private static ColonyFixture Make(string key, int ticks = 10 * Day, NumericRow[]? rows = null)
        => new(
            new ColonyStats
            {
                Key = key,
                HasObservedPoint = true,
                MaxTicks = ticks,
                WealthMax = 50_000,
                PeakWealthKnown = true,
                LastWealthKnown = true,
                SaveCount = 2,
                GameVersions = [],
                EndingCounts = new Dictionary<EndingType, int>(),
            },
            new NumericColony { Key = key, Rows = rows ?? [] });

    private static NumericRow Row(MetricDefinition definition, double? number,
        MetricCoverage coverage = MetricCoverage.Complete, int? sinceTick = 0, int? throughTick = 10 * Day) => new()
    {
        Definition = definition,
        Value = new NumericValue
        {
            Number = number,
            Coverage = coverage,
            SinceTick = sinceTick,
            ThroughTick = throughTick,
            Evidence = MetricEvidence.Verified,
        },
        Targets = coverage == MetricCoverage.NotApplicable ? 0 : 1,
        Valid = number is null ? 0 : 1,
    };

    private static StatsView View(params ColonyFixture[] colonies) => ViewWithOverall([], colonies);

    private static StatsView ViewWithOverall(IReadOnlyList<NumericRow> overall, params ColonyFixture[] colonies) => new()
    {
        Colonies = [],
        Stats = new StatsResult
        {
            Colonies = colonies.Select(c => c.Stats).ToArray(),
            ColonyCount = colonies.Count(c => !c.Stats.Excluded && !c.Stats.OutOfScope),
            EndingsByType = new Dictionary<EndingType, int>(),
        },
        Numeric = new NumericView { Colonies = colonies.Select(c => c.Numeric).ToArray(), Overall = overall },
    };
}
