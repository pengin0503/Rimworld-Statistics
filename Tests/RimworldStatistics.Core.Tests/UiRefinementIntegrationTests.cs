namespace RimworldStatistics.Core.Tests;

public sealed class UiRefinementIntegrationTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ColonyIdentity MakeIdentity(int index) => new()
    {
        SeedString = $"seed-{index + 1:D2}",
        PersistentRandomValue = index + 1,
        StartingTile = $"{index + 1},0",
        GameStartAbsTick = 0,
        FirstWealthBits = (uint)(100 + index),
        StartingPawnIds = [$"Thing_Human{index + 1}"],
        ModCampaignId = $"colony-{index + 1:D2}",
    };

    private static Profile BuildProfile(int colonyCount)
    {
        var profile = new Profile();
        for (var i = 0; i < colonyCount; i++)
        {
            var id = $"colony-{i + 1:D2}";
            var identity = MakeIdentity(i);
            var tick = 60_000 * (i + 1);
            profile.LiveCampaigns.Add(new LiveCampaign
            {
                CampaignId = id,
                Identity = identity.Clone(),
                MaxObservedUtc = T0.AddDays(i),
                Max = { TicksGame = tick },
            });

            var checkpointObs = new MetricObservation
            {
                Source = MetricObservationSource.Checkpoint,
                FormatVersion = SaveReader.Version,
                Key = $"obs-cp-{i + 1:D2}",
                Identity = identity.Clone(),
                SaveTick = tick,
                ObservedUtc = T0.AddDays(i),
            };
            var saveObs = new MetricObservation
            {
                Source = MetricObservationSource.SaveFile,
                FormatVersion = SaveReader.Version,
                Key = $"obs-save-{i + 1:D2}",
                Identity = identity.Clone(),
                SaveTick = tick,
                ObservedUtc = T0.AddDays(i),
            };

            // Mix measured 0, positive, negative (signed metrics), Partial, and Unknown (omitted).
            if (i == 0)
            {
                saveObs.Metrics[MetricKeys.QuestSuccess] = MetricEntry.Of(MetricKeys.Revision(MetricKeys.QuestSuccess), new MetricValue { Coverage = MetricCoverage.Complete, IntegerValue = 0 });
                saveObs.Metrics[MetricKeys.WealthTotalNet] = MetricEntry.Of(MetricKeys.Revision(MetricKeys.WealthTotalNet), new MetricValue { Coverage = MetricCoverage.Complete, RealValue = -4500d });
                checkpointObs.Metrics[TradeFamily.Deals] = MetricEntry.Of(MetricKeys.Revision(TradeFamily.Deals), new MetricValue { Coverage = MetricCoverage.Complete, IntegerValue = 0 });
                checkpointObs.Metrics[TradeFamily.BuysTotal] = MetricEntry.Of(MetricKeys.Revision(TradeFamily.BuysTotal), new MetricValue { Coverage = MetricCoverage.Complete, RealValue = 0d });
            }
            else if (i == 1)
            {
                saveObs.Metrics[MetricKeys.QuestSuccess] = MetricEntry.Of(MetricKeys.Revision(MetricKeys.QuestSuccess), new MetricValue { Coverage = MetricCoverage.Complete, IntegerValue = 2 });
                saveObs.Metrics[MetricKeys.WealthTotalNet] = MetricEntry.Of(MetricKeys.Revision(MetricKeys.WealthTotalNet), new MetricValue { Coverage = MetricCoverage.Complete, RealValue = 12_000d });
                checkpointObs.Metrics[TradeFamily.Deals] = MetricEntry.Of(MetricKeys.Revision(TradeFamily.Deals), new MetricValue { Coverage = MetricCoverage.Partial, IntegerValue = 3 });
                checkpointObs.Metrics[TradeFamily.BuysTotal] = MetricEntry.Of(MetricKeys.Revision(TradeFamily.BuysTotal), new MetricValue { Coverage = MetricCoverage.Partial, RealValue = 1250d });
            }
            else if (i % 5 != 0)
            {
                saveObs.Metrics[MetricKeys.QuestSuccess] = MetricEntry.Of(MetricKeys.Revision(MetricKeys.QuestSuccess), new MetricValue { Coverage = MetricCoverage.Complete, IntegerValue = i });
                saveObs.Metrics[MetricKeys.WealthTotalNet] = MetricEntry.Of(MetricKeys.Revision(MetricKeys.WealthTotalNet), new MetricValue { Coverage = MetricCoverage.Complete, RealValue = (i % 2 == 0 ? 1 : -1) * i * 300d });
                checkpointObs.Metrics[TradeFamily.Deals] = MetricEntry.Of(MetricKeys.Revision(TradeFamily.Deals), new MetricValue { Coverage = MetricCoverage.Complete, IntegerValue = i });
                checkpointObs.Metrics[TradeFamily.BuysTotal] = MetricEntry.Of(MetricKeys.Revision(TradeFamily.BuysTotal), new MetricValue { Coverage = MetricCoverage.Complete, RealValue = i * 500d });
            }
            // Note: i % 5 == 0 (> 0) intentionally omits TradeFamily.Deals & BuysTotal so Unknown stays missing.

            profile.MetricObservations.Add(saveObs);
            profile.MetricObservations.Add(checkpointObs);
        }
        return profile;
    }

    private static StatsView ComputeView(Profile profile)
    {
        var allUnits = MetricDisplayPolicy.AllDetailsVisibleUnits;
        var colonies = CampaignMatcher.BuildColonies(profile);
        var stats = StatsCalculator.Calculate(colonies, new StatsOptions());
        var catalog = MetricRegistry.Compose(profile.ModMetricDescriptors, new HashSet<string>(allUnits, StringComparer.Ordinal));
        return new StatsView
        {
            Colonies = colonies,
            Stats = stats,
            MissingObservations = 0,
            Numeric = MetricCalculator.BuildView(profile, colonies, stats, catalog),
        };
    }

    [Fact]
    public void ZeroOneAndThirtyFiveColoniesPreserveStatesGroupingRankingsAndChartBars()
    {
        var emptyView = ComputeView(new Profile());
        Assert.Empty(emptyView.Stats.Colonies);
        Assert.Empty(LeaderboardBuilder.Build(emptyView, TradeFamily.Deals, descending: true).Entries);
        Assert.Empty(MetricChartSeries.ByColony(emptyView.Numeric, TradeFamily.Deals));

        var singleView = ComputeView(BuildProfile(1));
        var singleGrouped = DerivedMetricGroups.Group(singleView.Numeric.Overall, r => r.Definition, enableGrouping: true);
        var singleDealsGroup = Assert.Single(singleGrouped, n => n.Item.Definition.Key == TradeFamily.Deals);
        Assert.Equal(3, singleDealsGroup.Children.Count); // max / min / top retained even for 1 colony with 0
        Assert.Equal(0d, singleDealsGroup.Item.Value.Number);
        Assert.Equal(MetricVisualKind.Experimental, MetricVisualState.Classify(singleDealsGroup.Item));
        var singleQuestRow = Assert.Single(singleView.Numeric.Overall, r => r.Definition.Key == MetricKeys.QuestSuccess);
        Assert.Equal(0d, singleQuestRow.Value.Number);
        Assert.Equal(MetricVisualKind.Complete, MetricVisualState.Classify(singleQuestRow));
        var singleBoard = LeaderboardBuilder.Build(singleView, TradeFamily.Deals, descending: true);
        Assert.Equal(1, singleBoard.Measured);
        Assert.Single(singleBoard.Entries); // measured 0 enters ranking

        var fullView = ComputeView(BuildProfile(35));
        Assert.Equal(35, fullView.Stats.ColonyCount);

        var statuses = MetricStatusClassifier.Classify(fullView, _ => UnitRecordingState.Recording);
        var coverageGroups = MetricAcquisitionLabels.BuildGroups(
            statuses,
            id => id,
            key => key,
            cat => cat.ToString(),
            cat => $"Ext:{cat}",
            StringComparer.Ordinal);
        Assert.NotEmpty(coverageGroups);
        Assert.All(coverageGroups, g =>
        {
            Assert.False(string.IsNullOrWhiteSpace(g.GroupId));
            Assert.False(string.IsNullOrWhiteSpace(g.Label));
            Assert.InRange(g.MeasuredCount, 0, g.TotalCount);
        });

        var grouped35 = DerivedMetricGroups.Group(fullView.Numeric.Overall, r => r.Definition, enableGrouping: true);
        var deals35 = Assert.Single(grouped35, n => n.Item.Definition.Key == TradeFamily.Deals);
        Assert.Equal(MetricCoverage.Partial, deals35.Item.Value.Coverage);
        Assert.Equal(MetricVisualKind.Partial, MetricVisualState.Classify(deals35.Item));
        Assert.Equal(35, deals35.Item.Targets);
        Assert.Equal(29, deals35.Item.Valid); // indices 0..34 minus {5,10,15,20,25,30}

        // Unmeasured metric stays Missing rather than zero, and produces no bar series.
        var unmeasuredDef = MetricRegistry.Find(ResearchFamily.Completed)!;
        var unmeasuredRow = Assert.Single(fullView.Numeric.Overall, r => r.Definition.Key == unmeasuredDef.Key);
        Assert.Null(unmeasuredRow.Value.Number);
        Assert.Equal(MetricVisualKind.Missing, MetricVisualState.Classify(unmeasuredRow));
        Assert.Empty(MetricChartSeries.ByColony(fullView.Numeric, unmeasuredDef));

        // Bar series excludes missing colonies while keeping measured 0 and signed negatives.
        var wealthBars = MetricChartSeries.ByColony(fullView.Numeric, MetricKeys.WealthTotalNet);
        Assert.Equal(29, wealthBars.Count);
        Assert.Contains(wealthBars, b => b.Value.Number < 0d);
        Assert.Contains(wealthBars, b => b.Value.Number > 0d);
        var wealthTicks = MetricChartTicks.Generate(
            wealthBars.Min(b => b.Value.Number!.Value),
            wealthBars.Max(b => b.Value.Number!.Value));
        Assert.Contains(0d, wealthTicks);
        Assert.True(wealthTicks.All(double.IsFinite));

        // Comparison panel over mixed targets (All scope + 3 colonies, including one missing deals)
        var colonyKeys = fullView.Stats.Colonies.ToDictionary(c => c.Key);
        var colony01Key = Assert.Single(fullView.Colonies, c => c.AliasKeys.Contains("id:colony-01")).Key;
        var colony02Key = Assert.Single(fullView.Colonies, c => c.AliasKeys.Contains("id:colony-02")).Key;
        var colony06Key = Assert.Single(fullView.Colonies, c => c.AliasKeys.Contains("id:colony-06")).Key;
        Assert.True(colonyKeys.ContainsKey(colony01Key));
        Assert.True(colonyKeys.ContainsKey(colony02Key));
        Assert.True(colonyKeys.ContainsKey(colony06Key));

        var comparisonCols = new (ComparisonTarget, StatsView)[]
        {
            (new ScopeTarget(ViewScope.All), fullView),
            (new ColonyTarget(colony01Key), fullView),
            (new ColonyTarget(colony06Key), fullView), // missing Deals (i = 5)
            (new ColonyTarget(colony02Key), fullView), // Partial Deals (i = 1)
        };
        var compared = Comparison.Build(comparisonCols, [TradeFamily.Deals, MetricKeys.WealthTotalNet]);
        var dealsComp = Assert.Single(compared, r => r.MetricKey == TradeFamily.Deals);
        Assert.Equal(0d, dealsComp.Values[1]!.Number); // colony-01 measured 0
        Assert.Null(dealsComp.Values[2]); // colony-06 missing -> null, never 0
        Assert.Equal(MetricCoverage.Partial, dealsComp.Values[3]!.Coverage);
    }

    [Theory]
    [InlineData(720f, 540f)]
    [InlineData(1000f, 720f)]
    [InlineData(1400f, 900f)]
    [InlineData(1600f, 1000f)]
    [InlineData(1920f, 1080f)]
    public void WindowAndWidgetLayoutsRemainNonOverlappingAndFiniteAcrossWindowSizes(float screenW, float screenH)
    {
        foreach (var preset in Enum.GetValues<StatisticsWindowSizePreset>())
        {
            var win = StatisticsWindowLayout.Resolve(preset, screenW, screenH);
            Assert.InRange(win.Width, 1f, screenW * 0.9f + 0.01f);
            Assert.InRange(win.Height, 1f, screenH * 0.9f + 0.01f);

            var header = StatisticsWindowLayout.ResolveHeader(win.Width - 24f);
            Assert.True(header.SelectorX >= header.TitleWidth);
            Assert.True(header.SelectorX + header.SelectorWidth <= win.Width - 24f - 32f + 0.01f);

            var contentW = Math.Max(200f, win.Width - 40f);
            var expanded = StatisticsWindowLayout.ResolveCollapsibleSplit(contentW, leftCollapsed: false);
            var collapsed = StatisticsWindowLayout.ResolveCollapsibleSplit(contentW, leftCollapsed: true);
            Assert.True(expanded.LeftWidth > 0f && expanded.RightWidth > 0f);
            Assert.Equal(contentW, collapsed.RightWidth, precision: 2);

            for (var cols = 1; cols <= Comparison.MaxColumns; cols++)
            {
                var pinned = ColumnLayout.ResolvePinned(contentW, cols);
                Assert.True(pinned.NameWidth > 0f);
                Assert.True(pinned.DataViewportWidth > 0f);
            }

            foreach (var depth in new[] { 0, 1, 2 })
            {
                var hit = DerivedMetricGroups.ComputeHitboxes(0f, contentW, depth, isMetricRow: depth < 2, hasExpand: depth == 0);
                Assert.False(hit.Star.Overlaps(hit.Toggle));
                Assert.False(hit.Star.Overlaps(hit.Chart));
                Assert.False(hit.Toggle.Overlaps(hit.Chart));
            }

            var legendWidths = new[] { 110f, 185f, 220f, 150f };
            var legendPlan = HistoryLegendLayout.Resolve(legendWidths, Math.Max(120f, contentW * 0.45f), 22f, 16f, 2f);
            Assert.Equal(legendWidths.Length, legendPlan.Items.Count);
            Assert.True(legendPlan.TotalHeight >= 22f);
        }
    }

    [Fact]
    public void ExtremeBarSeriesAndOverlayStateTransitionsMaintainSafetyAndLineageRules()
    {
        var zeroTicks = MetricChartTicks.Generate(0d, 0d);
        var tinyTicks = MetricChartTicks.Generate(0d, 1e-9d);
        var hugeTicks = MetricChartTicks.Generate(-1e12d, 2.5e12d);

        Assert.Contains(0d, zeroTicks);
        Assert.Contains(0d, tinyTicks);
        Assert.Contains(0d, hugeTicks);
        Assert.All(zeroTicks.Concat(tinyTicks).Concat(hugeTicks), t => Assert.True(double.IsFinite(t)));
        Assert.True(tinyTicks[^1] >= 1e-9d);
        Assert.True(hugeTicks[0] <= -1e12d && hugeTicks[^1] >= 2.5e12d);

        var primaryPoints = new[]
        {
            new MetricHistoryPoint(100, new NumericValue { Number = 10d, Coverage = MetricCoverage.Complete }, ConnectToPrevious: false),
            new MetricHistoryPoint(200, null, ConnectToPrevious: false) { ContinuesFrom = 0 },
            new MetricHistoryPoint(300, new NumericValue { Number = 20d, Coverage = MetricCoverage.Partial }, ConnectToPrevious: false)
            {
                ContinuesFrom = 0,
                Unlinked = true,
                SkipsOnlyOtherBranches = true,
            },
        };
        var primary = new MetricHistory(MetricHistoryKind.CheckpointLineage, primaryPoints) { LatestSaveTick = 300 };

        var tracker = new HistoryOverlayTracker("primary", maxOverlays: 2);
        Assert.True(tracker.TryAdd("c2", "Colony Two", out var o1));
        Assert.True(tracker.TryAdd("c3", "Colony Three", out var o2));
        Assert.False(tracker.CanAdd);

        // Removing an active + an in-flight overlay leaves primary lineage intact and prevents late resurrection.
        Assert.True(tracker.TryComplete(o1!.RequestId, new MetricHistory(
            MetricHistoryKind.CheckpointLineage,
            [new MetricHistoryPoint(150, new NumericValue { Number = 99d, Coverage = MetricCoverage.Complete })])
        {
            LatestSaveTick = 150,
        }, MetricHistoryKind.CheckpointLineage));
        Assert.True(tracker.Remove("c2"));
        Assert.True(tracker.Remove("c3"));
        Assert.False(tracker.TryComplete(o2!.RequestId, new MetricHistory(
            MetricHistoryKind.CheckpointLineage,
            [new MetricHistoryPoint(250, new NumericValue { Number = 500d, Coverage = MetricCoverage.Complete })])
        {
            LatestSaveTick = 250,
        }, MetricHistoryKind.CheckpointLineage));

        var bounds = tracker.ComputeBounds(primary, [primaryPoints[0], primaryPoints[2]]);
        Assert.Equal((100, 300), (bounds.FirstTick, bounds.LastTick));
        Assert.Equal((10d, 20d), (bounds.MinValue, bounds.MaxValue));
        Assert.True(bounds.HasPartial);
        Assert.True(bounds.HasUnlinked);
        Assert.True(primary.Points[2].Unlinked);
        Assert.True(primary.Points[2].SkipsOnlyOtherBranches);
    }
}
