namespace RimworldStatistics.Core.Tests;

/// <summary>
/// Issue #72 / audit C1: a line in a per-colony history means "this save descends from that one". The same
/// campaign id is not that proof. Only embedded checkpoint parent ids establish it; everything else is a
/// separate point. Missing readings stay gaps and a checkpoint shared by several saves is one reading.
/// </summary>
public sealed class MetricChartLineageTests
{
    private const string Campaign = "one";
    private static readonly MetricDefinition SaveMetric = MetricRegistry.Find(MetricKeys.WealthItemsPeak)!;
    private static readonly MetricDefinition CheckpointMetric = MetricRegistry.Find(ResearchFamily.Completed)!;
    private static readonly DateTime Epoch = new(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void DirectParentSavesAreConnected()
    {
        var profile = Profile(
            Save("a.rws", 10, 100), CheckpointIn("a.rws", "ca", null, 10),
            Save("b.rws", 20, 150), CheckpointIn("b.rws", "cb", "ca", 20));

        var points = History(profile).Points;

        Assert.Equal(new[] { 10, 20 }, points.Select(p => p.Tick));
        Assert.Equal(new[] { false, true }, points.Select(p => p.ConnectToPrevious));
        Assert.All(points, p => Assert.False(p.Unlinked));
        Assert.All(points, p => Assert.Equal(-1, p.ContinuesFrom));
    }

    [Fact]
    public void SiblingBranchesAreNotJoinedAndEachContinuesItsOwnParent()
    {
        // a ─┬─ b2(20) ── c2(35)        a reload of "a" after b1 was played:
        //    └─ b1(30) ── c1(40)        sorted by tick the branches interleave.
        var profile = Profile(
            Save("a.rws", 10, 100), CheckpointIn("a.rws", "a", null, 10),
            Save("b2.rws", 20, 120), CheckpointIn("b2.rws", "b2", "a", 20),
            Save("b1.rws", 30, 300), CheckpointIn("b1.rws", "b1", "a", 30),
            Save("c2.rws", 35, 130), CheckpointIn("c2.rws", "c2", "b2", 35),
            Save("c1.rws", 40, 310), CheckpointIn("c1.rws", "c1", "b1", 40));

        var points = History(profile).Points;

        Assert.Equal(new[] { 10, 20, 30, 35, 40 }, points.Select(p => p.Tick));
        Assert.Equal(new[] { -1, 0, 0, 1, 2 }, points.Select((p, i) => p.Predecessor(i)));
        Assert.Equal(new[] { false, true, false, false, false }, points.Select(p => p.ConnectToPrevious));
        Assert.All(points, p => Assert.False(p.Unlinked));
    }

    [Fact]
    public void SameCampaignWithoutCheckpointsIsNotAssumedContinuous()
    {
        // Saves made before the MOD recorded checkpoints, or with every family turned off.
        var profile = Profile(Save("a.rws", 10, 100), Save("b.rws", 20, 120), Save("c.rws", 30, 140));

        var points = History(profile).Points;

        Assert.Equal(new double?[] { 100, 120, 140 }, points.Select(p => p.Value?.Number));
        Assert.All(points, p => Assert.False(p.ConnectToPrevious));
        Assert.All(points, p => Assert.Equal(-1, p.ContinuesFrom));
        Assert.Equal(new[] { false, true, true }, points.Select(p => p.Unlinked));
    }

    [Fact]
    public void AMissingParentLeavesTheLaterSaveUnlinked()
    {
        var profile = Profile(
            Save("a.rws", 10, 100), CheckpointIn("a.rws", "a", null, 10),
            Save("c.rws", 30, 140), CheckpointIn("c.rws", "c", "b-was-deleted", 30));

        var points = History(profile).Points;

        Assert.False(points[1].ConnectToPrevious);
        Assert.Equal(-1, points[1].ContinuesFrom);
        Assert.True(points[1].Unlinked);
    }

    [Fact]
    public void AnAncestorWithoutItsOwnSaveStillProvesTheLine()
    {
        // b's file was overwritten; its checkpoint, kept in the profile, still links c to a.
        var profile = Profile(
            Save("a.rws", 10, 100), CheckpointIn("a.rws", "a", null, 10),
            Checkpoint("b", "a", 20),
            Save("c.rws", 30, 140), CheckpointIn("c.rws", "c", "b", 30));

        var points = History(profile).Points;

        Assert.Equal(new[] { 10, 30 }, points.Select(p => p.Tick));
        Assert.True(points[1].ConnectToPrevious);
    }

    [Fact]
    public void SiblingSavesAtTheSameTickAreNotOneProvenState()
    {
        // Autosaves after a reload land on the same tick in both branches.
        var profile = Profile(
            Save("a.rws", 10, 100), CheckpointIn("a.rws", "a", null, 10),
            Save("b1.rws", 20, 120), CheckpointIn("b1.rws", "b1", "a", 20),
            Save("b2.rws", 20, 120), CheckpointIn("b2.rws", "b2", "a", 20),
            Save("c.rws", 30, 140), CheckpointIn("c.rws", "c", "b1", 30));

        var points = History(profile).Points;

        Assert.Equal(new[] { 10, 20, 30 }, points.Select(p => p.Tick));
        Assert.Equal(120, points[1].Value?.Number);
        Assert.True(points[1].Unlinked);
        Assert.False(points[1].ConnectToPrevious);
        // c descends from b1, which is mixed with its sibling at that tick: c continues from a, not from the mixed point.
        Assert.False(points[2].ConnectToPrevious);
        Assert.Equal(0, points[2].ContinuesFrom);
    }

    [Fact]
    public void ContradictingSiblingsAtTheSameTickAreAGapNotAnArbitraryValue()
    {
        var profile = Profile(
            Save("a.rws", 10, 100), CheckpointIn("a.rws", "a", null, 10),
            Save("b1.rws", 20, 120), CheckpointIn("b1.rws", "b1", "a", 20),
            Save("b2.rws", 20, 999), CheckpointIn("b2.rws", "b2", "a", 20));

        var points = History(profile).Points;

        Assert.Equal(new double?[] { 100, null }, points.Select(p => p.Value?.Number));
    }

    [Fact]
    public void PausedResavesOfOneStateAtOneTickStayProven()
    {
        // Saved twice while paused: b, then c with parent b, both at tick 20.
        var profile = Profile(
            Save("a.rws", 10, 100), CheckpointIn("a.rws", "a", null, 10),
            Save("b.rws", 20, 120), CheckpointIn("b.rws", "b", "a", 20),
            Save("c.rws", 20, 120), CheckpointIn("c.rws", "c", "b", 20),
            Save("d.rws", 30, 140), CheckpointIn("d.rws", "d", "c", 30));

        var points = History(profile).Points;

        Assert.Equal(new[] { 10, 20, 30 }, points.Select(p => p.Tick));
        Assert.Equal(new[] { false, true, true }, points.Select(p => p.ConnectToPrevious));
        Assert.All(points, p => Assert.False(p.Unlinked));
    }

    [Fact]
    public void SavesCarryingTheSameOlderCheckpointDescendFromItButNotFromEachOther()
    {
        // The supplied 21:07/21:19 shape: two later saves rewrote checkpoint e (tick 40) unchanged.
        var profile = Profile(
            Save("d.rws", 30, 100), CheckpointIn("d.rws", "d", null, 30),
            Save("late1.rws", 50, 140), Save("late2.rws", 60, 150),
            CheckpointIn(["late1.rws", "late2.rws"], "e", "d", 40));

        var history = History(profile);
        var points = history.Points;

        Assert.Equal(new[] { 30, 50, 60 }, points.Select(p => p.Tick));
        Assert.True(points[1].ConnectToPrevious);
        Assert.False(points[2].ConnectToPrevious);
        Assert.Equal(0, points[2].ContinuesFrom);
        Assert.All(points, p => Assert.Null(p.SavedTick));

        // The checkpoint statistic: one reading at its own tick, saved again later without new statistics.
        var checkpoint = History(profile, CheckpointMetric).Points;
        Assert.Equal(new[] { 30, 40 }, checkpoint.Select(p => p.Tick));
        Assert.Equal(new int?[] { null, 60 }, checkpoint.Select(p => p.SavedTick));
        Assert.Equal(new[] { false, true }, checkpoint.Select(p => p.ConnectToPrevious));
    }

    [Fact]
    public void ACheckpointAfterItsSaveIsNoEvidence()
    {
        var profile = Profile(
            Save("a.rws", 10, 100), CheckpointIn("a.rws", "a", null, 10),
            Save("b.rws", 20, 120), CheckpointIn("b.rws", "b", "a", 25));

        Assert.True(History(profile).Points[1].Unlinked);
    }

    [Fact]
    public void AConflictedCheckpointProvesNothing()
    {
        var conflicted = CheckpointIn("b.rws", "b", "a", 20);
        conflicted.Conflicted = true;
        var profile = Profile(
            Save("a.rws", 10, 100), CheckpointIn("a.rws", "a", null, 10),
            Save("b.rws", 20, 120), conflicted);

        Assert.True(History(profile).Points[1].Unlinked);
    }

    [Fact]
    public void ACopiedSaveIsOneReading()
    {
        var copy = Save("b.rws", 20, 120);
        copy.FileAliases.Add(Alias("b - copy.rws"));
        var profile = Profile(
            Save("a.rws", 10, 100), CheckpointIn("a.rws", "a", null, 10),
            copy, CheckpointIn(["b.rws", "b - copy.rws"], "b", "a", 20));

        var points = History(profile).Points;

        Assert.Equal(new[] { 10, 20 }, points.Select(p => p.Tick));
        Assert.True(points[1].ConnectToPrevious);
    }

    [Fact]
    public void ACopyWhoseCheckpointWasNotReadDoesNotHideTheProof()
    {
        // The same bytes under another name: the reading is keyed by the content hash, so any copy is that save.
        var copy = Save("b.rws", 20, 120);
        copy.FileAliases.Add(Alias("b - copy.rws"));
        var profile = Profile(
            Save("a.rws", 10, 100), CheckpointIn("a.rws", "a", null, 10),
            copy, CheckpointIn("b.rws", "b", "a", 20));

        Assert.True(History(profile).Points[1].ConnectToPrevious);
    }

    [Fact]
    public void CopiesNamingDifferentCheckpointsProveNothing()
    {
        var copy = Save("b.rws", 20, 120);
        copy.FileAliases.Add(Alias("b - copy.rws"));
        var profile = Profile(
            Save("a.rws", 10, 100), CheckpointIn("a.rws", "a", null, 10),
            copy, CheckpointIn("b.rws", "b", "a", 20), CheckpointIn("b - copy.rws", "other", "a", 20));

        Assert.True(History(profile).Points[1].Unlinked);
    }

    [Fact]
    public void AChangedModListKeepsTheProvenLineButAnUnmeasuredReadingStaysAGap()
    {
        var parent = Save("a.rws", 10, 100);
        parent.ModSetHash = new string('a', 64);
        var child = Save("b.rws", 20, null);   // the reading is not available with the new MOD list
        child.ModSetHash = new string('b', 64);
        var later = Save("c.rws", 30, 140);
        later.ModSetHash = child.ModSetHash;
        var profile = Profile(
            parent, CheckpointIn("a.rws", "a", null, 10),
            child, CheckpointIn("b.rws", "b", "a", 20),
            later, CheckpointIn("c.rws", "c", "b", 30));

        var points = History(profile).Points;

        Assert.Equal(new double?[] { 100, null, 140 }, points.Select(p => p.Value?.Number));
        Assert.Equal(new[] { false, true, true }, points.Select(p => p.ConnectToPrevious));
    }

    [Fact]
    public void BasicChartsUseTheSameSaveAncestry()
    {
        var colony = Colony();
        foreach (var (file, tick, raids) in new[] { ("a.rws", 10, 1), ("b.rws", 20, 2), ("c.rws", 30, 3) })
            colony.Observations.Add(new SaveObservation
            {
                FileName = file, FileSize = Size(file), LastWriteUtc = Alias(file).LastWriteUtc,
                Identity = new ColonyIdentity { ModCampaignId = Campaign },
                Values = new ObservedValues { TicksGame = tick, NumRaidsEnemy = raids },
            });
        var profile = new Profile
        {
            MetricObservations =
            [
                CheckpointIn("a.rws", "a", null, 10),
                CheckpointIn("b.rws", "b", "a", 20),
                CheckpointIn("c.rws", "c", "missing", 30),
            ],
        };
        var definition = new MetricDefinition
        {
            Key = BasicMetrics.Raids, StoredKey = BasicMetrics.Raids, LedgerIds = BasicMetrics.Raids,
            Category = MetricCategory.Colony, Unit = MetricUnit.Count, Provenance = MetricProvenance.SaveHistory,
            Overall = OverallAggregation.Sum, LabelKey = BasicMetrics.Raids, DescriptionKey = BasicMetrics.Raids,
        };

        var points = MetricCalculator.BuildChartHistory(profile, [colony], colony.Key, definition).Points;

        Assert.Equal(new double?[] { 1, 2, 3 }, points.Select(p => p.Value?.Number));
        Assert.Equal(new[] { false, true, false }, points.Select(p => p.ConnectToPrevious));
        Assert.Equal(new[] { false, false, true }, points.Select(p => p.Unlinked));
    }

    [Fact]
    public void GameSideSnapshotsLinkThroughTheCheckpointOfTheSameSave()
    {
        var definition = MetricRegistry.Find(ResearchStateSnapshot.Finished)!;
        MetricObservation Snapshot(int tick, int finished, string? checkpoint) => new()
        {
            Source = MetricObservationSource.SaveSnapshot, Key = "snapshot-" + tick, SaveTick = tick,
            ObservedUtc = Epoch.AddMinutes(tick), CheckpointKey = checkpoint,
            Identity = new ColonyIdentity { ModCampaignId = Campaign },
            Metrics = { [definition.StoredKey] = MetricEntry.Of(definition.Revision,
                new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = finished }) },
        };
        var profile = Profile(
            Snapshot(10, 1, "a"), Checkpoint("a", null, 10),
            Snapshot(20, 2, "b"), Checkpoint("b", "a", 20),
            Snapshot(30, 3, null), Checkpoint("c", "b", 30));

        var points = History(profile, definition).Points;

        Assert.Equal(new[] { 10, 20, 30 }, points.Select(p => p.Tick));
        Assert.Equal(new[] { false, true, false }, points.Select(p => p.ConnectToPrevious));
        Assert.True(points[2].Unlinked);
    }

    [Fact]
    public void AnImportedCopyOfAGameSideSnapshotSaveLinksByItsExactFileTime()
    {
        // Snapshots recorded before checkpoint keys were stored: the save file holding the same tick and write time
        // was imported, so its checkpoint names the same save.
        var definition = MetricRegistry.Find(ResearchStateSnapshot.Finished)!;
        MetricObservation Snapshot(int tick, string file) => new()
        {
            Source = MetricObservationSource.SaveSnapshot, Key = "snapshot-" + tick, SaveTick = tick,
            ObservedUtc = Alias(file).LastWriteUtc,
            Identity = new ColonyIdentity { ModCampaignId = Campaign },
            Metrics = { [definition.StoredKey] = MetricEntry.Of(definition.Revision,
                new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = tick }) },
        };
        var wrongTime = Snapshot(30, "c.rws");
        wrongTime.ObservedUtc = wrongTime.ObservedUtc.AddSeconds(1);
        var profile = Profile(
            Snapshot(10, "a.rws"), CheckpointIn("a.rws", "a", null, 10),
            Snapshot(20, "b.rws"), CheckpointIn("b.rws", "b", "a", 20),
            wrongTime, CheckpointIn("c.rws", "c", "b", 30));

        var points = History(profile, definition).Points;

        Assert.Equal(new[] { false, true, false }, points.Select(p => p.ConnectToPrevious));
        Assert.True(points[2].Unlinked);
    }

    [Fact]
    public void TheSnapshotCheckpointLinkRoundTripsAndIsOmittedWhenAbsent()
    {
        var profile = new Profile
        {
            MetricObservations =
            [
                new MetricObservation { Source = MetricObservationSource.SaveSnapshot, Key = "linked", CheckpointKey = "cp" },
                new MetricObservation { Source = MetricObservationSource.SaveSnapshot, Key = "older" },
            ],
        };

        var json = ProfileJson.Serialize(profile);
        var restored = ProfileJson.Deserialize(json);

        Assert.Equal(1, json.Split("\"checkpointKey\"").Length - 1);
        Assert.Equal(new string?[] { "cp", null }, restored.MetricObservations.Select(o => o.CheckpointKey));
        Assert.All(restored.MetricObservations, o => Assert.Null(o.ExtensionData));
    }

    [Fact]
    public void ThinningKeepsGapsExtremaBranchesAndUnlinkedPoints()
    {
        var points = new List<MetricHistoryPoint>();
        for (var i = 0; i < 400; i++)
            points.Add(new MetricHistoryPoint(i * 10, Value(i == 123 ? 900 : i == 321 ? -50 : i % 7), i > 0 && i != 200));
        points[200] = points[200] with { Value = null };
        points[250] = points[250] with { ConnectToPrevious = false, Unlinked = true };
        points.Add(new MetricHistoryPoint(4000, Value(5)) { ContinuesFrom = 77 });

        var thin = MetricHistoryPlot.Thin(points, 50);

        Assert.True(thin.Count < points.Count);
        Assert.Contains(thin, p => p.Value?.Number == 900);
        Assert.Contains(thin, p => p.Value?.Number == -50);
        Assert.Contains(thin, p => p.Tick == 2000 && p.Value is null);
        Assert.Contains(thin, p => p.Tick == 2500 && p.Unlinked);
        var branch = thin[^1];
        Assert.Equal(4000, branch.Tick);
        Assert.Equal(770, thin[branch.ContinuesFrom].Tick);
        // Every kept line still joins the same two original readings.
        var original = points.ToDictionary(p => p.Tick);
        for (var i = 1; i < thin.Count; i++)
        {
            var from = thin[i].Predecessor(i);
            if (from < 0)
                continue;
            var expected = points.FindIndex(p => p.Tick == thin[i].Tick);
            var source = original[thin[i].Tick].Predecessor(expected);
            Assert.True(source >= 0);
            Assert.True(thin[from].Tick <= thin[i].Tick);
            if (!original[thin[i].Tick].ConnectToPrevious)
                Assert.Equal(points[source].Tick, thin[from].Tick);
        }
        Assert.Same(points, MetricHistoryPlot.Thin(points, 1000));
    }

    private static NumericValue Value(double number) => new() { Number = number, Coverage = MetricCoverage.SnapshotOnly };

    private static MetricHistory History(Profile profile, MetricDefinition? definition = null)
    {
        var colony = Colony();
        return MetricCalculator.BuildChartHistory(profile, [colony], colony.Key, definition ?? SaveMetric);
    }

    private static Colony Colony()
    {
        var colony = new Colony { Key = "id:" + Campaign };
        colony.LiveCampaigns.Add(new LiveCampaign { CampaignId = Campaign });
        return colony;
    }

    private static Profile Profile(params MetricObservation[] observations) => new() { MetricObservations = [.. observations] };

    private static long Size(string file) => 1000 + file.Length;

    private static MetricFileAlias Alias(string file) => new()
    {
        FileName = file, FileSize = Size(file),
        LastWriteUtc = Epoch.AddSeconds(file.Sum(c => c)),
    };

    private static MetricObservation Save(string file, int tick, int? wealth)
    {
        var observation = new MetricObservation
        {
            Source = MetricObservationSource.SaveFile, Key = "sha-" + file, SaveTick = tick,
            Identity = new ColonyIdentity { ModCampaignId = Campaign },
            FileAliases = { Alias(file) },
        };
        if (wealth is { } number)
            observation.Metrics[SaveMetric.StoredKey] = MetricEntry.Of(SaveMetric.Revision,
                new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = number });
        return observation;
    }

    private static MetricObservation Checkpoint(string key, string? parent, int tick) => new()
    {
        Source = MetricObservationSource.Checkpoint, Key = key, ParentKey = parent, SaveTick = tick,
        PayloadHash = key, Identity = new ColonyIdentity { ModCampaignId = Campaign },
        Metrics = { [CheckpointMetric.StoredKey] = MetricEntry.Of(CheckpointMetric.Revision,
            new MetricValue { Coverage = MetricCoverage.Complete, IntegerValue = tick }) },
    };

    private static MetricObservation CheckpointIn(string file, string key, string? parent, int tick)
        => CheckpointIn([file], key, parent, tick);

    private static MetricObservation CheckpointIn(string[] files, string key, string? parent, int tick)
    {
        var checkpoint = Checkpoint(key, parent, tick);
        checkpoint.FileAliases.AddRange(files.Select(Alias));
        return checkpoint;
    }
}
