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
}
