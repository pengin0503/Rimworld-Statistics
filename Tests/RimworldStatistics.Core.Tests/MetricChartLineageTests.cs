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
    public void ACheckpointChartKeepsTheReadingsBeforeAMissingParentWithoutJoiningThem()
    {
        // a ── b ┄┄ c (no save holding it was imported) ┄┄ d ── e
        var profile = Profile(
            CheckpointIn("a.rws", "a", null, 10), CheckpointIn("b.rws", "b", "a", 20),
            CheckpointIn("d.rws", "d", "c-not-imported", 40), CheckpointIn("e.rws", "e", "d", 50));

        var history = History(profile, CheckpointMetric);

        Assert.Equal(MetricHistoryKind.CheckpointLineage, history.Kind);
        Assert.Equal(new[] { 10, 20, 40, 50 }, history.Points.Select(p => p.Tick));
        Assert.Equal(new double?[] { 10, 20, 40, 50 }, history.Points.Select(p => p.Value?.Number));
        Assert.Equal(new[] { -1, 0, -1, 2 }, history.Points.Select((p, i) => p.Predecessor(i)));
        // d may descend from b through c or from another branch: nothing says which, so no line and a hollow point.
        Assert.Equal(new[] { false, false, true, false }, history.Points.Select(p => p.Unlinked));
        Assert.All(history.Points, p => Assert.False(p.SkipsOnlyOtherBranches));
    }

    [Fact]
    public void BeforeAMissingParentOnlyEarlierCheckpointsAreShownAndTheirOwnBranchesStaySeparate()
    {
        // a ─┬─ b1(20)
        //    ├─ b2(30)                      earlier than the gap: either may lead to d
        //    └─ x(45)                       later than d: another branch, not on this chart
        // ┄┄ d(40, parent missing) ── e(50)
        var conflicted = CheckpointIn("y.rws", "y", "a", 25);
        conflicted.Conflicted = true;
        var profile = Profile(
            CheckpointIn("a.rws", "a", null, 10), CheckpointIn("b1.rws", "b1", "a", 20),
            CheckpointIn("b2.rws", "b2", "a", 30), CheckpointIn("x.rws", "x", "a", 45), conflicted,
            CheckpointIn("d.rws", "d", "c-not-imported", 40), CheckpointIn("e.rws", "e", "d", 50));

        var points = History(profile, CheckpointMetric).Points;

        Assert.Equal(new[] { 10, 20, 30, 40, 50 }, points.Select(p => p.Tick));
        Assert.Equal(new[] { -1, 0, 0, -1, 3 }, points.Select((p, i) => p.Predecessor(i)));
        Assert.Equal(new[] { false, false, false, true, false }, points.Select(p => p.Unlinked));
        Assert.True(points[2].SkipsOnlyOtherBranches);   // b1 holds its own checkpoint, not on b2's chain
    }

    [Fact]
    public void BeforeAMissingParentAPausedReSaveStandsForItsTickAndKeepsItsLaterSave()
    {
        // a ── b(20) ── b'(20, re-saved while paused, carried unchanged into a save at 35) ┄┄ d(40, parent missing) ── e
        var profile = Profile(
            CheckpointIn("a.rws", "a", null, 10), CheckpointIn("b.rws", "b", "a", 20),
            Save("late.rws", 35, 100), CheckpointIn(["b2.rws", "late.rws"], "b2", "b", 20),
            CheckpointIn("d.rws", "d", "c-not-imported", 40), CheckpointIn("e.rws", "e", "d", 50));

        var points = History(profile, CheckpointMetric).Points;

        Assert.Equal(new[] { 10, 20, 40, 50 }, points.Select(p => p.Tick));
        Assert.Equal(new[] { -1, 0, -1, 2 }, points.Select((p, i) => p.Predecessor(i)));
        // The tick-20 point is b', the descendant of the two, so it shows that b' was saved again at 35.
        Assert.Equal(new int?[] { null, 35, null, null }, points.Select(p => p.SavedTick));
        Assert.Equal(new[] { false, false, true, false }, points.Select(p => p.Unlinked));
    }

    [Fact]
    public void AnUnbrokenCheckpointLineageIsUnchanged()
    {
        var profile = Profile(
            CheckpointIn("a.rws", "a", null, 10), CheckpointIn("b.rws", "b", "a", 20), CheckpointIn("x.rws", "x", "a", 15));

        var points = History(profile, CheckpointMetric).Points;

        Assert.Equal(new[] { 10, 20 }, points.Select(p => p.Tick));   // x is another branch of the same start
        Assert.Equal(new[] { false, true }, points.Select(p => p.ConnectToPrevious));
        Assert.All(points, p => Assert.False(p.Unlinked));
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

    [Theory]
    [InlineData(true)]
    // Only the re-save's file was imported: the earlier save at that tick is known from the re-save's parent id alone.
    [InlineData(false)]
    public void ASaveLoadedFromEitherPausedResaveContinuesTheirOneState(bool earlierFileImported)
    {
        // b, then its paused re-save c (both tick 20) are one state. d was played on from b, and e from c.
        var observations = new List<MetricObservation>
        {
            Save("a.rws", 10, 100), CheckpointIn("a.rws", "a", null, 10),
            Save("c.rws", 20, 120), CheckpointIn("c.rws", "c", "b", 20),
            Save("d.rws", 30, 140), CheckpointIn("d.rws", "d", "b", 30),
            Save("e.rws", 40, 160), CheckpointIn("e.rws", "e", "c", 40),
        };
        observations.AddRange(earlierFileImported
            ? [Save("b.rws", 20, 120), CheckpointIn("b.rws", "b", "a", 20)]
            : [Checkpoint("b", "a", 20)]);

        var points = History(Profile([.. observations])).Points;

        Assert.Equal(new[] { 10, 20, 30, 40 }, points.Select(p => p.Tick));
        Assert.Equal(new[] { -1, 0, 1, 1 }, points.Select((p, i) => p.Predecessor(i)));
        Assert.Equal(new[] { false, true, true, false }, points.Select(p => p.ConnectToPrevious));
        // e skips d, which holds its own checkpoint off e's chain: another branch.
        Assert.True(points[3].SkipsOnlyOtherBranches);
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
    public void ABranchThatSkipsOnlyProvenOtherBranchesSaysSo()
    {
        // b1 and b2 hold their own checkpoints, so b2 is proven not to be c1's ancestor.
        var profile = Profile(
            Save("a.rws", 10, 100), CheckpointIn("a.rws", "a", null, 10),
            Save("b1.rws", 20, 120), CheckpointIn("b1.rws", "b1", "a", 20),
            Save("b2.rws", 30, 300), CheckpointIn("b2.rws", "b2", "a", 30),
            Save("c1.rws", 40, 130), CheckpointIn("c1.rws", "c1", "b1", 40));

        var points = History(profile).Points;

        Assert.Equal(new[] { -1, 0, 0, 1 }, points.Select((p, i) => p.Predecessor(i)));
        Assert.Equal(new[] { -1, -1, 0, 1 }, points.Select(p => p.ContinuesFrom));
        // b2 skips b1 and c1 skips b2: each skipped save holds its own checkpoint, which is not on the later save's chain.
        Assert.Equal(new[] { false, false, true, true }, points.Select(p => p.SkipsOnlyOtherBranches));
    }

    [Theory]
    // The 21:07/21:19 shape: the save in between only carried an older checkpoint, so it may be the ancestor.
    [InlineData("carried", false)]
    // A save in between without any checkpoint (before the MOD recorded them).
    [InlineData("no-checkpoint", false)]
    // Saves in between at one tick, one of them without a checkpoint.
    [InlineData("same-tick-unproven", false)]
    // Saves in between at one tick with checkpoints of their own: the tick is a gap, yet neither is the ancestor.
    [InlineData("same-tick-siblings", true)]
    // The same, but one of the two is the ancestor: mixing them at one tick hides it, it does not disprove it.
    [InlineData("same-tick-ancestor", false)]
    public void ABranchClaimsToSkipOtherBranchesOnlyWhenEveryReadingInBetweenIsProvenNotToBeItsAncestor(
        string between, bool otherBranches)
    {
        var observations = new List<MetricObservation> { Save("d.rws", 30, 100), CheckpointIn("d.rws", "d", null, 30) };
        observations.AddRange(between switch
        {
            "carried" => [Save("late1.rws", 50, 140), Save("late2.rws", 60, 150),
                CheckpointIn(["late1.rws", "late2.rws"], "e", "d", 40)],
            "no-checkpoint" => [Save("x.rws", 50, 140), Save("late2.rws", 60, 150), CheckpointIn("late2.rws", "e", "d", 60)],
            "same-tick-unproven" => [Save("s1.rws", 50, 140), CheckpointIn("s1.rws", "s1", "d", 50), Save("x.rws", 50, 140),
                Save("late2.rws", 60, 150), CheckpointIn("late2.rws", "e", "d", 60)],
            "same-tick-siblings" => [Save("s1.rws", 50, 140), CheckpointIn("s1.rws", "s1", "d", 50),
                Save("s2.rws", 50, 140), CheckpointIn("s2.rws", "s2", "d", 50),
                Save("late2.rws", 60, 150), CheckpointIn("late2.rws", "e", "d", 60)],
            _ => [Save("s1.rws", 50, 140), CheckpointIn("s1.rws", "s1", "d", 50),
                Save("s2.rws", 50, 140), CheckpointIn("s2.rws", "s2", "d", 50),
                Save("late2.rws", 60, 150), CheckpointIn("late2.rws", "e", "s1", 60)],
        });

        var points = History(Profile([.. observations])).Points;

        Assert.Equal(new[] { 30, 50, 60 }, points.Select(p => p.Tick));
        Assert.Equal(0, points[2].ContinuesFrom);
        Assert.Equal(otherBranches, points[2].SkipsOnlyOtherBranches);
    }

    [Fact]
    public void AGameSideSnapshotOfASaveThatCarriedAnOlderCheckpointLinksLikeItsSaveFile()
    {
        // Recorded since checkpoint keys were stored: a save that committed no new checkpoint names the one it carried.
        var definition = MetricRegistry.Find(ResearchStateSnapshot.Finished)!;
        var profile = Profile(
            Save("d.rws", 30, 100), CheckpointIn("d.rws", "d", null, 30), GameSnapshot(definition, 30, "d.rws", "d"),
            Save("late1.rws", 50, 140), GameSnapshot(definition, 50, "late1.rws", "e"),
            Save("late2.rws", 60, 150), GameSnapshot(definition, 60, "late2.rws", "e"),
            CheckpointIn(["late1.rws", "late2.rws"], "e", "d", 40));

        var snapshots = History(profile, definition).Points;
        var files = History(profile).Points;

        Assert.Equal(new[] { 30, 50, 60 }, snapshots.Select(p => p.Tick));
        Assert.Equal(files.Select((p, i) => p.Predecessor(i)), snapshots.Select((p, i) => p.Predecessor(i)));
        Assert.Equal(new[] { -1, 0, 0 }, snapshots.Select((p, i) => p.Predecessor(i)));
        Assert.All(snapshots, p => Assert.False(p.Unlinked));
    }

    [Fact]
    public void AnOlderGameSideSnapshotOfACarriedSaveLinksThroughItsImportedFile()
    {
        // Recorded before checkpoint keys were stored: the imported file with exactly the snapshot's tick and write time
        // is the same save, and it holds checkpoint e from an earlier tick.
        var definition = MetricRegistry.Find(ResearchStateSnapshot.Finished)!;
        var otherTick = GameSnapshot(definition, 61, "late2.rws", null);
        var profile = Profile(
            Save("d.rws", 30, 100), CheckpointIn("d.rws", "d", null, 30), GameSnapshot(definition, 30, "d.rws", null),
            Save("late1.rws", 50, 140), GameSnapshot(definition, 50, "late1.rws", null),
            Save("late2.rws", 60, 150), otherTick,
            CheckpointIn(["late1.rws", "late2.rws"], "e", "d", 40));

        var points = History(profile, definition).Points;

        Assert.Equal(new[] { 30, 50, 61 }, points.Select(p => p.Tick));
        Assert.Equal(new[] { -1, 0, -1 }, points.Select((p, i) => p.Predecessor(i)));
        // The file written at that time holds tick 60, not 61: not the same save, so nothing is proven.
        Assert.Equal(new[] { false, false, true }, points.Select(p => p.Unlinked));
    }

    [Fact]
    public void AnOlderSnapshotProvesNothingWhenTheFileWrittenAtItsTimeWasReadWithTwoTicks()
    {
        // Two different saves share one file version (name, size and write time): which of them the snapshot belongs to,
        // and so which checkpoint it holds, is not proven, even though a checkpoint of its tick lists that file.
        var definition = MetricRegistry.Find(ResearchStateSnapshot.Finished)!;
        var otherSave = Save("b.rws", 25, 130);
        otherSave.Key = "sha-b.rws-at-25";
        var profile = Profile(
            Save("a.rws", 10, 100), CheckpointIn("a.rws", "a", null, 10), GameSnapshot(definition, 10, "a.rws", null),
            Save("b.rws", 20, 120), otherSave, CheckpointIn("b.rws", "b", "a", 20), GameSnapshot(definition, 20, "b.rws", null));

        var points = History(profile, definition).Points;

        Assert.Equal(new[] { 10, 20 }, points.Select(p => p.Tick));
        Assert.Equal(new[] { false, true }, points.Select(p => p.Unlinked));
    }

    [Fact]
    public void ASnapshotNamingACheckpointLaterThanItselfProvesNothing()
    {
        var definition = MetricRegistry.Find(ResearchStateSnapshot.Finished)!;
        var profile = Profile(
            Checkpoint("d", null, 30), GameSnapshot(definition, 30, "d.rws", "d"),
            Checkpoint("e", "d", 55), GameSnapshot(definition, 50, "e.rws", "e"));

        Assert.True(History(profile, definition).Points[1].Unlinked);
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
        points.Add(new MetricHistoryPoint(4000, Value(5)) { ContinuesFrom = 77, SkipsOnlyOtherBranches = true });

        var thin = MetricHistoryPlot.Thin(points, 50);

        Assert.True(thin.Count < points.Count);
        Assert.Contains(thin, p => p.Value?.Number == 900);
        Assert.Contains(thin, p => p.Value?.Number == -50);
        Assert.Contains(thin, p => p.Tick == 2000 && p.Value is null);
        Assert.Contains(thin, p => p.Tick == 2500 && p.Unlinked);
        var branch = thin[^1];
        Assert.Equal(4000, branch.Tick);
        Assert.Equal(770, thin[branch.ContinuesFrom].Tick);
        Assert.True(branch.SkipsOnlyOtherBranches);
        Assert.All(thin.Where(p => p.ContinuesFrom < 0), p => Assert.False(p.SkipsOnlyOtherBranches));
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

    [Fact]
    public void ThinningKeepsPartialCoverageBoundariesAndGapRunEndpoints()
    {
        var points = new List<MetricHistoryPoint>();
        for (var i = 0; i < 200; i++)
            points.Add(new MetricHistoryPoint(i * 10, Value(i % 10), i > 0));

        // Single partial reading in the middle of a chunk where its value (5) is neither local min (0) nor max (9).
        points[55] = points[55] with { Value = new NumericValue { Number = 5, Coverage = MetricCoverage.Partial } };

        // Contiguous partial run inside a chunk.
        for (var i = 82; i <= 86; i++)
            points[i] = points[i] with { Value = new NumericValue { Number = 4, Coverage = MetricCoverage.Partial } };

        // Multi-tick contiguous gap run spanning more than one chunk.
        for (var i = 120; i <= 135; i++)
            points[i] = points[i] with { Value = null };
        points[136] = points[136] with { ConnectToPrevious = false };

        var thin = MetricHistoryPlot.Thin(points, 20);

        Assert.Contains(thin, p => p.Tick == 540 && p.Value?.Coverage == MetricCoverage.SnapshotOnly);
        Assert.Contains(thin, p => p.Tick == 550 && p.Value?.Coverage == MetricCoverage.Partial);
        Assert.Contains(thin, p => p.Tick == 560 && p.Value?.Coverage == MetricCoverage.SnapshotOnly);

        Assert.Contains(thin, p => p.Tick == 810 && p.Value?.Coverage == MetricCoverage.SnapshotOnly);
        Assert.Contains(thin, p => p.Tick == 820 && p.Value?.Coverage == MetricCoverage.Partial);
        Assert.Contains(thin, p => p.Tick == 860 && p.Value?.Coverage == MetricCoverage.Partial);
        Assert.Contains(thin, p => p.Tick == 870 && p.Value?.Coverage == MetricCoverage.SnapshotOnly);

        Assert.Contains(thin, p => p.Tick == 1200 && p.Value is null);
        Assert.Contains(thin, p => p.Tick == 1350 && p.Value is null);
        Assert.True(thin.Count(p => p.Value is null) >= 3);
    }

    [Fact]
    public void AddingAndRemovingComparisonOverlaysPreservesPrimaryLineageConnectionRules()
    {
        var primaryProfile = Profile(
            Save("a.rws", 10, 100), CheckpointIn("a.rws", "a", null, 10),
            Save("b1.rws", 20, 120), CheckpointIn("b1.rws", "b1", "a", 20),
            Save("b2.rws", 30, 300), CheckpointIn("b2.rws", "b2", "a", 30),
            Save("c1.rws", 40, 130), CheckpointIn("c1.rws", "c1", "b1", 40));
        var primaryHistory = History(primaryProfile);
        var primaryMeasured = primaryHistory.Points.Where(p => p.Value is not null).ToArray();
        var expectedPredecessors = primaryHistory.Points.Select((p, i) => p.Predecessor(i)).ToArray();
        var expectedUnlinked = primaryHistory.Points.Select(p => p.Unlinked).ToArray();
        var expectedBranches = primaryHistory.Points.Select(p => p.SkipsOnlyOtherBranches).ToArray();

        var overlayHistory = new MetricHistory(primaryHistory.Kind,
        [
            new MetricHistoryPoint(20, new NumericValue { Number = 500d, Coverage = MetricCoverage.SnapshotOnly }) { Unlinked = true },
            new MetricHistoryPoint(35, null),
        ]);

        var tracker = new HistoryOverlayTracker("id:one");
        Assert.True(tracker.TryAdd("id:two", "Colony Two", out var overlay));
        Assert.True(tracker.TryComplete(overlay!.RequestId, overlayHistory, primaryHistory.Kind));
        var boundsWithOverlay = tracker.ComputeBounds(primaryHistory, primaryMeasured);
        Assert.Equal(500d, boundsWithOverlay.MaxValue);
        Assert.True(boundsWithOverlay.HasUnlinked);

        Assert.True(tracker.Remove("id:two"));
        var boundsAfterRemove = tracker.ComputeBounds(primaryHistory, primaryMeasured);
        Assert.Equal(300d, boundsAfterRemove.MaxValue);
        Assert.False(boundsAfterRemove.HasUnlinked);

        Assert.Equal(expectedPredecessors, primaryHistory.Points.Select((p, i) => p.Predecessor(i)).ToArray());
        Assert.Equal(expectedUnlinked, primaryHistory.Points.Select(p => p.Unlinked).ToArray());
        Assert.Equal(expectedBranches, primaryHistory.Points.Select(p => p.SkipsOnlyOtherBranches).ToArray());
    }

    private static NumericValue Value(double number) => new() { Number = number, Coverage = MetricCoverage.SnapshotOnly };

    /// <summary>A game-side snapshot of the save that wrote <paramref name="file"/> (its write time is the snapshot's).</summary>
    private static MetricObservation GameSnapshot(MetricDefinition definition, int tick, string file, string? checkpoint) => new()
    {
        Source = MetricObservationSource.SaveSnapshot, Key = "snapshot-" + tick, SaveTick = tick,
        ObservedUtc = Alias(file).LastWriteUtc, CheckpointKey = checkpoint,
        Identity = new ColonyIdentity { ModCampaignId = Campaign },
        Metrics = { [definition.StoredKey] = MetricEntry.Of(definition.Revision,
            new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = tick }) },
    };

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
