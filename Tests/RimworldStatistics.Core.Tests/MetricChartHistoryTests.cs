using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class MetricChartHistoryTests
{
    [Fact]
    public void RecorderChartsKeepGapsAndRequireAlignedTiming()
    {
        var points = HistoryMetricReader.RecorderHistory(HistoryMetricReader.ColonistMood, [55f, 0f, 70f, 75f], "1.7", 90000);
        Assert.Equal(new double?[] { 55d, null, 70d, 75d }, points.Select(p => p.Value?.Number));
        Assert.Equal(new[] { false, false, false, true }, points.Select(p => p.ConnectToPrevious));
        Assert.Empty(HistoryMetricReader.RecorderHistory(HistoryMetricReader.FreeColonists, [0f, 3f], "1.7", 60000));
        Assert.Equal(0d, HistoryMetricReader.RecorderHistory(HistoryMetricReader.FreeColonists, [0f, 3f], "1.7", 30000)[0].Value?.Number);
    }

    [Fact]
    public void FifteenDayGainShowsTheRunningMaximumWithoutInventingEarlyReadings()
    {
        var wealth = Enumerable.Repeat(100f, 34).ToArray();
        wealth[30] = 140;
        wealth[31] = 90;
        wealth[33] = 200;
        var history = HistoryMetricReader.WealthGain15DayHistory(wealth, "1.7.4985", 990000);

        Assert.Equal(new[] { 900000, 930000, 960000, 990000 }, history.Select(p => p.Tick));
        Assert.Equal(new double?[] { 40, 40, 40, 100 }, history.Select(p => p.Value?.Number));
        Assert.Empty(HistoryMetricReader.WealthGain15DayHistory(wealth, "1.7.4985", 1020000));
    }

    [Fact]
    public void FifteenDayGainBreaksAtInvalidWindowsAndResumesAsPartial()
    {
        var wealth = Enumerable.Repeat(100f, 63).ToArray();
        wealth[31] = float.NaN;
        wealth[62] = 150;
        var history = HistoryMetricReader.WealthGain15DayHistory(wealth, "1.7.4985", 1860000);

        Assert.Equal(0, history[0].Value!.Number);
        Assert.All(history.Skip(1).Take(31), p => Assert.Null(p.Value));
        Assert.Equal(50, history[^1].Value!.Number);
        Assert.Equal(MetricCoverage.Partial, history[^1].Value!.Coverage);
    }

    [Fact]
    public void CheckpointsFollowRepresentativeParentsAndKeepMissingValuesAsGaps()
    {
        var definition = MetricRegistry.Find(ResearchFamily.Completed)!;
        var colony = new Colony { Key = "id:one" };
        colony.LiveCampaigns.Add(new LiveCampaign { CampaignId = "one" });
        var profile = new Profile
        {
            MetricObservations =
            [
                Checkpoint("one", "a", null, 10, 0),
                Checkpoint("one", "b", "a", 20, null),
                Checkpoint("one", "c", "b", 40, 8),
                Checkpoint("one", "other-branch", "a", 30, 90),
                Checkpoint("another", "foreign", null, 50, 100),
            ],
        };

        var result = MetricCalculator.BuildChartHistory(profile, [colony], colony.Key, definition);
        Assert.Equal(MetricHistoryKind.CheckpointLineage, result.Kind);
        Assert.Equal(new[] { 10, 20, 40 }, result.Points.Select(p => p.Tick));
        Assert.Equal(new double?[] { 0, null, 8 }, result.Points.Select(p => p.Value?.Number));
        Assert.Equal(new[] { false, true, true }, result.Points.Select(p => p.ConnectToPrevious));

        MetricObservation Checkpoint(string campaign, string key, string? parent, int tick, int? number)
        {
            var observation = new MetricObservation
            {
                Key = key, ParentKey = parent, SaveTick = tick, Source = MetricObservationSource.Checkpoint,
                Identity = new ColonyIdentity { ModCampaignId = campaign },
            };
            if (number is not null)
                observation.Metrics[definition.StoredKey] = MetricEntry.Of(definition.Revision,
                    new MetricValue { Coverage = MetricCoverage.Complete, IntegerValue = number });
            return observation;
        }
    }

    [Fact]
    public void RepeatedSavesWhilePausedKeepTheLatestReadingAndEarlierHistory()
    {
        var definition = MetricRegistry.Find(ResearchFamily.Completed)!;
        var colony = new Colony { Key = "id:one" };
        colony.LiveCampaigns.Add(new LiveCampaign { CampaignId = "one" });
        var profile = new Profile
        {
            MetricObservations =
            [
                Checkpoint("a", null, 10, 1),
                Checkpoint("b", "a", 20, 2),
                Checkpoint("c", "b", 20, 3),
                Checkpoint("d", "c", 40, 4),
            ],
        };

        var result = MetricCalculator.BuildChartHistory(profile, [colony], colony.Key, definition);

        Assert.Equal(new[] { 10, 20, 40 }, result.Points.Select(p => p.Tick));
        Assert.Equal(new double?[] { 1, 3, 4 }, result.Points.Select(p => p.Value?.Number));
        Assert.Equal(new[] { false, true, true }, result.Points.Select(p => p.ConnectToPrevious));

        MetricObservation Checkpoint(string key, string? parent, int tick, int number) => new()
        {
            Key = key, ParentKey = parent, SaveTick = tick, Source = MetricObservationSource.Checkpoint,
            Identity = new ColonyIdentity { ModCampaignId = "one" },
            Metrics = { [definition.StoredKey] = MetricEntry.Of(definition.Revision,
                new MetricValue { Coverage = MetricCoverage.Complete, IntegerValue = number }) },
        };
    }

    [Fact]
    public void UnverifiedSaveReadingsDoNotPickAnArbitraryValueAtTheSameTick()
    {
        var definition = MetricRegistry.Find(MetricKeys.WealthTotalMaxGain15D)!;
        var colony = new Colony { Key = "id:one" };
        colony.LiveCampaigns.Add(new LiveCampaign { CampaignId = "one" });
        var profile = new Profile
        {
            MetricObservations = [Save(10, 0), Save(20, 4), Save(20, 8), Save(30, 12)],
        };

        var result = MetricCalculator.BuildChartHistory(profile, [colony], colony.Key, definition);
        Assert.Equal(MetricHistoryKind.SaveObservations, result.Kind);
        Assert.Equal(new double?[] { 0, null, 12 }, result.Points.Select(p => p.Value?.Number));

        MetricObservation Save(int tick, int number) => new()
        {
            Key = number.ToString(), SaveTick = tick, Source = MetricObservationSource.SaveFile,
            Identity = new ColonyIdentity { ModCampaignId = "one" },
            Metrics = { [definition.StoredKey] = MetricEntry.Of(definition.Revision,
                new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = number }) },
        };
    }

    [Fact]
    public void ProfileReadsTheExactSaveAndFallsBackIfItHasChanged()
    {
        using var directory = new TempDir();
        var path = directory.File("history.rws");
        var wealth = Enumerable.Repeat(100f, 34).ToArray();
        wealth[33] = 200;
        File.WriteAllBytes(path, new SaveXmlBuilder().Minimal().Tick(ticksGame: 990000, start: 0)
            .WealthTotalDeflate(wealth).Build());
        var save = new SaveReader().ReadDetailed(path);
        var session = new ProfileSession(new ProfileStore(directory.Path), () => DateTime.UtcNow);
        session.Mutate(profile =>
        {
            profile.SaveObservations.Add(save.Observation);
            profile.MetricObservations.Add(save.Metrics!);
        }, SaveMode.None);

        var view = session.GetView(new StatsOptions());
        var colony = Assert.Single(view.Colonies);
        var definition = MetricRegistry.Find(MetricKeys.WealthTotalMaxGain15D)!;
        var bar = Assert.Single(MetricChartSeries.ByColony(view.Numeric, definition));
        var history = session.ReadChartHistory(view, colony.Key, definition, directory.Path);
        Assert.Equal(MetricHistoryKind.WealthRecorder, history.Kind);
        Assert.Equal(bar.Value.Number, history.Points[^1].Value!.Number);
        Assert.Equal(4, history.Points.Count);

        // Even a same-size replacement with the same file timestamp must not be substituted.
        var bytes = File.ReadAllBytes(path);
        bytes[^1] = (byte)' ';
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, save.Observation.LastWriteUtc);
        var fallback = session.ReadChartHistory(view, colony.Key, definition, directory.Path);
        Assert.Equal(MetricHistoryKind.SaveObservations, fallback.Kind);
        Assert.Equal(bar.Value.Number, Assert.Single(fallback.Points).Value!.Number);
    }

    [Fact]
    public void ArchivedParentUsesOnlyTheMatchingCheckpoint()
    {
        using var directory = new TempDir();
        var archive = new FileCheckpointArchive(directory.File("archive"));
        var session = new ProfileSession(new ProfileStore(directory.Path), () => DateTime.UtcNow, archive);
        var definition = MetricRegistry.Find(ResearchFamily.Completed)!;
        var identity = new ColonyIdentity { ModCampaignId = "one" };
        MetricObservation Checkpoint(string key, string? parent, int tick, int number) => new()
        {
            Key = key, ParentKey = parent, SaveTick = tick, Source = MetricObservationSource.Checkpoint,
            Identity = identity, PayloadHash = key,
            Metrics = { [definition.StoredKey] = MetricEntry.Of(definition.Revision,
                new MetricValue { Coverage = MetricCoverage.Complete, IntegerValue = number }) },
        };

        var parent = Checkpoint("a", null, 100, 3);
        var wrong = Checkpoint("a", null, 100, 99);
        wrong.PayloadHash = "different";
        archive.Append("one", [System.Text.Json.JsonSerializer.Serialize(parent, ProfileJson.CompactOptions),
            System.Text.Json.JsonSerializer.Serialize(wrong, ProfileJson.CompactOptions)]);
        parent.Metrics.Clear();
        parent.Compressed = true;
        parent.ArchivedMetrics = true;
        session.Mutate(profile =>
        {
            ProfileMutations.ApplyLiveObservation(profile, "one", identity,
                new ObservedValues { TicksGame = 200 }, new DisplayInfo(), DateTime.UtcNow, "1.7.4985");
            profile.MetricObservations.AddRange([parent, Checkpoint("b", "a", 200, 7)]);
        }, SaveMode.None);

        var view = session.GetView(new StatsOptions());
        var result = session.ReadChartHistory(view, Assert.Single(view.Colonies).Key, definition, directory.Path);
        Assert.Equal(new double?[] { 3, 7 }, result.Points.Select(p => p.Value?.Number));
        Assert.Empty(parent.Metrics);
    }

    [Fact]
    public void AnArchivedReadingBeforeAMissingParentIsRestoredToo()
    {
        using var directory = new TempDir();
        var archive = new FileCheckpointArchive(directory.File("archive"));
        var session = new ProfileSession(new ProfileStore(directory.Path), () => DateTime.UtcNow, archive);
        var definition = MetricRegistry.Find(ResearchFamily.Completed)!;
        var identity = new ColonyIdentity { ModCampaignId = "one" };
        MetricObservation Checkpoint(string key, string? parent, int tick, int number) => new()
        {
            Key = key, ParentKey = parent, SaveTick = tick, Source = MetricObservationSource.Checkpoint,
            Identity = identity, PayloadHash = key,
            Metrics = { [definition.StoredKey] = MetricEntry.Of(definition.Revision,
                new MetricValue { Coverage = MetricCoverage.Complete, IntegerValue = number }) },
        };

        // a ── b ┄┄ (missing) ┄┄ d ── e: a's values were moved to the archive once b confirmed it, d's once e did.
        var a = Checkpoint("a", null, 100, 3);
        var d = Checkpoint("d", "missing", 400, 9);
        archive.Append("one", [System.Text.Json.JsonSerializer.Serialize(a, ProfileJson.CompactOptions),
            System.Text.Json.JsonSerializer.Serialize(d, ProfileJson.CompactOptions)]);
        foreach (var moved in new[] { a, d })
        {
            moved.Metrics.Clear();
            moved.Compressed = true;
            moved.ArchivedMetrics = true;
        }
        session.Mutate(profile =>
        {
            ProfileMutations.ApplyLiveObservation(profile, "one", identity,
                new ObservedValues { TicksGame = 500 }, new DisplayInfo(), DateTime.UtcNow, "1.7.4985");
            profile.MetricObservations.AddRange([a, Checkpoint("b", "a", 200, 5), d, Checkpoint("e", "d", 500, 11)]);
        }, SaveMode.None);

        var view = session.GetView(new StatsOptions());
        var result = session.ReadChartHistory(view, Assert.Single(view.Colonies).Key, definition, directory.Path);
        // Each archived reading goes back to its own point, before and after the gap.
        Assert.Equal(new[] { 100, 200, 400, 500 }, result.Points.Select(p => p.Tick));
        Assert.Equal(new double?[] { 3, 5, 9, 11 }, result.Points.Select(p => p.Value?.Number));
        Assert.Equal(new[] { false, false, true, false }, result.Points.Select(p => p.Unlinked));
        Assert.Equal(new[] { false, true, false, true }, result.Points.Select(p => p.ConnectToPrevious));
    }

    [Fact]
    public void NullObservationIdentityDoesNotThrowWhenBuildingChartHistory()
    {
        var definition = MetricRegistry.Find(ResearchFamily.Completed)!;
        var colony = new Colony { Key = "id:one" };
        colony.LiveCampaigns.Add(new LiveCampaign { CampaignId = "one" });
        var profile = new Profile
        {
            MetricObservations =
            [
                new MetricObservation
                {
                    Key = "root", SaveTick = 100, Source = MetricObservationSource.Checkpoint,
                    Identity = null!,
                    Metrics = { [definition.StoredKey] = MetricEntry.Of(definition.Revision,
                        new MetricValue { Coverage = MetricCoverage.Complete, IntegerValue = 2 }) },
                },
                new MetricObservation
                {
                    Key = "child", ParentKey = "root", SaveTick = 200, Source = MetricObservationSource.Checkpoint,
                    Identity = new ColonyIdentity { ModCampaignId = "one" },
                    Metrics = { [definition.StoredKey] = MetricEntry.Of(definition.Revision,
                        new MetricValue { Coverage = MetricCoverage.Complete, IntegerValue = 5 }) },
                },
                new MetricObservation
                {
                    Key = "unlinked-null", SaveTick = 50, Source = MetricObservationSource.Checkpoint,
                    Identity = null!,
                    Metrics = { [definition.StoredKey] = MetricEntry.Of(definition.Revision,
                        new MetricValue { Coverage = MetricCoverage.Complete, IntegerValue = 1 }) },
                },
            ],
        };

        var result = MetricCalculator.BuildChartHistory(profile, [colony], colony.Key, definition);
        Assert.Equal(MetricHistoryKind.CheckpointLineage, result.Kind);
        Assert.Equal(new[] { 200 }, result.Points.Select(p => p.Tick));
        Assert.Equal(new double?[] { 5 }, result.Points.Select(p => p.Value?.Number));
    }

    [Fact]
    public void LegendLayoutWrapsLongItemsOntoMultipleRowsWithoutDroppingAnyItem()
    {
        var singleRow = HistoryLegendLayout.Resolve([90f, 110f, 120f], 400f);
        Assert.Equal(1, singleRow.RowCount);
        Assert.Equal(22f, singleRow.TotalHeight);
        Assert.Equal(3, singleRow.Items.Count);
        Assert.All(singleRow.Items, item => Assert.Equal(0f, item.Y));

        var wrapped = HistoryLegendLayout.Resolve([120f, 130f, 140f, 95f], 280f, rowHeight: 22f, itemGap: 16f, rowGap: 2f);
        Assert.Equal(4, wrapped.Items.Count);
        Assert.Equal(2, wrapped.RowCount);
        Assert.Equal(0f, wrapped.Items[0].X);
        Assert.Equal(0f, wrapped.Items[0].Y);
        Assert.Equal(136f, wrapped.Items[1].X);
        Assert.Equal(0f, wrapped.Items[1].Y);
        Assert.Equal(0f, wrapped.Items[2].X);
        Assert.Equal(24f, wrapped.Items[2].Y);
        Assert.Equal(156f, wrapped.Items[3].X);
        Assert.Equal(24f, wrapped.Items[3].Y);
        Assert.Equal(46f, wrapped.TotalHeight);

        var threeRows = HistoryLegendLayout.Resolve([120f, 130f, 140f, 140f], 280f, rowHeight: 22f, itemGap: 16f, rowGap: 2f);
        Assert.Equal(3, threeRows.RowCount);
        Assert.Equal(70f, threeRows.TotalHeight);

        Assert.Equal(0, HistoryLegendLayout.Resolve([], 200f).RowCount);
        Assert.Equal(0, HistoryLegendLayout.Resolve([50f], 0f).RowCount);
    }

    [Fact]
    public void OverlayTrackerAssignsDistinctColorsAndReopensSelectionWhenRemovingOneOrAll()
    {
        var tracker = new HistoryOverlayTracker("primary", maxOverlays: 2);
        Assert.True(tracker.IsSelected("primary"));
        Assert.False(tracker.TryAdd("primary", "Primary", out _));

        Assert.True(tracker.TryAdd("c1", "Colony 1", out var e1));
        Assert.Equal(0, e1!.ColorIndex);
        Assert.True(tracker.TryAdd("c2", "Colony 2", out var e2));
        Assert.Equal(1, e2!.ColorIndex);
        Assert.False(tracker.CanAdd);
        Assert.False(tracker.TryAdd("c3", "Colony 3", out _));

        // Removing first overlay frees color slot 0 and c1 selection without touching c2
        Assert.True(tracker.Remove("c1"));
        Assert.False(tracker.IsSelected("c1"));
        Assert.True(tracker.IsSelected("c2"));
        Assert.True(tracker.CanAdd);

        Assert.True(tracker.TryAdd("c3", "Colony 3", out var e3));
        Assert.Equal(0, e3!.ColorIndex);
        Assert.Equal([1, 0], tracker.Entries.Select(e => e.ColorIndex));

        // Removing all returns to primary-only and allows re-adding c1 later
        Assert.True(tracker.Remove("c2"));
        Assert.True(tracker.Remove("c3"));
        Assert.Empty(tracker.Entries);
        Assert.True(tracker.CanAdd);
        Assert.True(tracker.TryAdd("c1", "Colony 1", out var readded));
        Assert.Equal(0, readded!.ColorIndex);
    }

    [Fact]
    public void OverlayTrackerIgnoresLateAsyncCompletionAfterRemovalAndRestoresPrimaryBounds()
    {
        var primary = new MetricHistory(MetricHistoryKind.SaveObservations,
        [
            new MetricHistoryPoint(100, new NumericValue { Number = 10d, Coverage = MetricCoverage.SnapshotOnly }),
            new MetricHistoryPoint(200, new NumericValue { Number = 20d, Coverage = MetricCoverage.SnapshotOnly }),
        ]);
        var primaryMeasured = primary.Points.Where(p => p.Value is not null).ToArray();
        var baseBounds = new HistoryPlotBounds(100, 200, 10d, 20d, HasPartial: false, HasUnlinked: false);

        var tracker = new HistoryOverlayTracker("p", maxOverlays: 2);
        Assert.True(tracker.TryAdd("late", "Late Colony", out var late));
        var staleRequestId = late!.RequestId;
        Assert.True(tracker.Remove("late"));

        // Late completion arrives for removed request -> must be ignored and never alter bounds or entries
        var lateHistory = new MetricHistory(MetricHistoryKind.SaveObservations,
        [
            new MetricHistoryPoint(10, new NumericValue { Number = -500d, Coverage = MetricCoverage.Partial }) { Unlinked = true },
            new MetricHistoryPoint(999, new NumericValue { Number = 5000d, Coverage = MetricCoverage.Partial }),
        ]);
        Assert.False(tracker.TryComplete(staleRequestId, lateHistory, primary.Kind));
        Assert.Empty(tracker.Entries);
        Assert.Equal(baseBounds, tracker.ComputeBounds(primary, primaryMeasured));

        // Re-add gets a fresh RequestId; completing widens bounds, and removing restores base bounds cleanly
        Assert.True(tracker.TryAdd("late", "Late Colony", out var fresh));
        Assert.NotEqual(staleRequestId, fresh!.RequestId);
        Assert.False(tracker.TryComplete(staleRequestId, lateHistory, primary.Kind));
        Assert.True(tracker.TryComplete(fresh.RequestId, lateHistory, primary.Kind));

        var expanded = tracker.ComputeBounds(primary, primaryMeasured);
        Assert.Equal(10, expanded.FirstTick);
        Assert.Equal(999, expanded.LastTick);
        Assert.Equal(-500d, expanded.MinValue);
        Assert.Equal(5000d, expanded.MaxValue);
        Assert.True(expanded.HasPartial);
        Assert.True(expanded.HasUnlinked);

        Assert.True(tracker.Remove("late"));
        Assert.Equal(baseBounds, tracker.ComputeBounds(primary, primaryMeasured));
    }

    [Fact]
    public void OverlayTrackerMarksIncompatibleKindWithoutAffectingPlotBounds()
    {
        var primary = new MetricHistory(MetricHistoryKind.CheckpointLineage,
        [
            new MetricHistoryPoint(50, new NumericValue { Number = 5d, Coverage = MetricCoverage.Complete }),
        ]);
        var primaryMeasured = primary.Points.ToArray();
        var tracker = new HistoryOverlayTracker("p");
        Assert.True(tracker.TryAdd("other", "Other", out var entry));

        var otherHistory = new MetricHistory(MetricHistoryKind.SaveObservations,
        [
            new MetricHistoryPoint(10, new NumericValue { Number = 999d, Coverage = MetricCoverage.SnapshotOnly }),
        ]);
        Assert.True(tracker.TryComplete(entry!.RequestId, otherHistory, primary.Kind));
        Assert.Equal(HistoryOverlayStatus.Incompatible, entry.Status);
        Assert.Empty(entry.Measured);
        Assert.Equal(new HistoryPlotBounds(50, 50, 5d, 5d, false, false), tracker.ComputeBounds(primary, primaryMeasured));
    }
}
