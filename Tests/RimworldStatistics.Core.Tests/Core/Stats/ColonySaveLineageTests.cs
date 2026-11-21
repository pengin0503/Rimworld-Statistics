using System.Diagnostics;

namespace RimworldStatistics.Core.Tests;

public sealed class ColonySaveLineageTests
{
    private const string Campaign = "camp-lineage-1";
    private const string HashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string HashB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static readonly DateTime Epoch = new(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);
    private static readonly MetricDefinition SaveMetric = MetricRegistry.Find(MetricKeys.WealthItemsPeak)!;
    private static readonly MetricDefinition RunningTotal = MetricRegistry.Find(ResearchFamily.Completed)!;

    [Fact]
    public void EmptyOrMissingColony_ReturnsEmptyLineageWithoutThrowing()
    {
        var profile = new Profile();
        var lineage = ColonySaveLineage.Build(profile, [], "id:missing");

        Assert.Equal("id:missing", lineage.ColonyKey);
        Assert.Equal(CheckpointLineage.None, lineage.SummaryLineage);
        Assert.Empty(lineage.Nodes);
        Assert.Empty(lineage.Edges);
        Assert.Equal(-1, lineage.RepresentativeIndex);
    }

    [Fact]
    public void LinearCheckpointChain_ConnectsDirectParentsAndMarksRepresentativeLine()
    {
        var profile = MakeProfile(
            SaveObs("s1.rws", 60_000, 1000, Epoch.AddMinutes(10)),
            CheckpointIn("s1.rws", "cp1", null, 60_000, 3, Epoch.AddMinutes(10)),
            SaveObs("s2.rws", 120_000, 2500, Epoch.AddMinutes(20)),
            CheckpointIn("s2.rws", "cp2", "cp1", 120_000, 7, Epoch.AddMinutes(20)),
            SaveObs("s3.rws", 180_000, 4200, Epoch.AddMinutes(30)),
            CheckpointIn("s3.rws", "cp3", "cp2", 180_000, 12, Epoch.AddMinutes(30)));

        var lineage = Build(profile);

        Assert.Equal(CheckpointLineage.Linear, lineage.SummaryLineage);
        Assert.Equal(3, lineage.Nodes.Count);
        Assert.Equal(2, lineage.Edges.Count);
        Assert.Equal(2, lineage.RepresentativeIndex);
        Assert.Equal(0, lineage.MaxLane);
        Assert.All(lineage.Nodes, n =>
        {
            Assert.Equal(SaveLineageNodeKind.Checkpoint, n.Kind);
            Assert.True(n.OnRepresentativeLine);
            Assert.Equal(0, n.Lane);
        });
        Assert.Equal(SaveLineageParentState.Root, lineage.Nodes[0].ParentState);
        Assert.Equal(SaveLineageParentState.ProvenParent, lineage.Nodes[1].ParentState);
        Assert.Equal(SaveLineageParentState.ProvenParent, lineage.Nodes[2].ParentState);
        Assert.All(lineage.Edges, e => Assert.Equal(SaveLineageEdgeKind.DirectCheckpoint, e.Kind));

        var direct = lineage.CompareNodes(0, 1);
        Assert.Equal(SaveLineageRelation.DirectParentChild, direct.Relation);
        Assert.Equal(1, direct.Steps);

        var chain = lineage.CompareNodes(2, 0);
        Assert.Equal(SaveLineageRelation.ProvenLineage, chain.Relation);
        Assert.Equal(2, chain.Steps);

        var metricChange = lineage.CompareMetric(RunningTotal, 0, 2);
        Assert.Equal(HistoryChangeIssue.None, metricChange.Issue);
        Assert.Equal(9d, metricChange.Delta);
    }

    [Fact]
    public void ByteIdenticalSaveCopies_MergeIntoSingleNodeWithoutDoubleCounting()
    {
        var alias1 = Alias("autosave.rws", Epoch.AddMinutes(5));
        var alias2 = Alias("autosave_copy.rws", Epoch.AddMinutes(6));
        var sf = new MetricObservation
        {
            Source = MetricObservationSource.SaveFile,
            Key = "sha-identical",
            SaveTick = 60_000,
            ObservedUtc = alias1.LastWriteUtc,
            Identity = new ColonyIdentity { ModCampaignId = Campaign },
            FileAliases = { alias1, alias2 },
            Metrics =
            {
                [SaveMetric.StoredKey] = MetricEntry.Of(SaveMetric.Revision,
                    new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = 5000 }),
            },
        };
        var cp = new MetricObservation
        {
            Source = MetricObservationSource.Checkpoint,
            Key = "cp-shared",
            ParentKey = null,
            PayloadHash = "cp-shared",
            SaveTick = 60_000,
            ObservedUtc = alias1.LastWriteUtc,
            Identity = new ColonyIdentity { ModCampaignId = Campaign },
            FileAliases = { alias1, alias2 },
        };

        var colony = MakeColony();
        colony.Observations.Add(V1Save(alias1, 60_000, "sha-identical"));
        colony.Observations.Add(V1Save(alias2, 60_000, "sha-identical"));
        var profile = new Profile { MetricObservations = { sf, cp } };

        var lineage = ColonySaveLineage.Build(profile, [colony], colony.Key);

        Assert.Single(lineage.Nodes);
        Assert.Empty(lineage.Edges);
        Assert.Equal(2, lineage.Nodes[0].Files.Count);
        Assert.Equal(new[] { "autosave.rws", "autosave_copy.rws" }, lineage.Nodes[0].Files.Select(f => f.FileName));
    }

    [Fact]
    public void PausedSameTickResave_OrdersParentBeforeChildWithPausedResaveEdge()
    {
        var profile = MakeProfile(
            SaveObs("pause1.rws", 90_000, 1800, Epoch.AddMinutes(10)),
            CheckpointIn("pause1.rws", "cp-pause-1", null, 90_000, 4, Epoch.AddMinutes(10)),
            SaveObs("pause2.rws", 90_000, 1800, Epoch.AddMinutes(11)),
            CheckpointIn("pause2.rws", "cp-pause-2", "cp-pause-1", 90_000, 4, Epoch.AddMinutes(11)));

        var lineage = Build(profile);

        Assert.Equal(2, lineage.Nodes.Count);
        Assert.Equal("cp-pause-1", lineage.Nodes[0].CheckpointId);
        Assert.Equal("cp-pause-2", lineage.Nodes[1].CheckpointId);
        Assert.True(lineage.Nodes[1].SameTickParent);
        var edge = Assert.Single(lineage.Edges);
        Assert.Equal(SaveLineageEdgeKind.PausedResave, edge.Kind);
        Assert.Equal((0, 1), (edge.FromIndex, edge.ToIndex));
    }

    [Fact]
    public void CarriedCheckpointsInLaterSaves_HangOffOriginCheckpointAndNeverFormFalseChainOrSiblingProof()
    {
        // Matches the 6-save E/F shape (#72 §7.1): d -> cpE (tick 120_000), while saveE (180_000) and saveF (240_000)
        // both carry cpE unchanged without recording new checkpoints of their own.
        var saveE = SaveObs("e.rws", 180_000, 1600, Epoch.AddMinutes(20));
        var saveF = SaveObs("f.rws", 240_000, 2100, Epoch.AddMinutes(30));
        var cpE = CheckpointIn([], "cp-e", "cp-d", 120_000, 9, Epoch.AddMinutes(15));
        cpE.FileAliases.AddRange(saveE.FileAliases);
        cpE.FileAliases.AddRange(saveF.FileAliases);

        var profile = MakeProfile(
            SaveObs("d.rws", 60_000, 1000, Epoch.AddMinutes(10)),
            CheckpointIn("d.rws", "cp-d", null, 60_000, 5, Epoch.AddMinutes(10)),
            saveE, saveF, cpE);

        var lineage = Build(profile);

        Assert.Equal(4, lineage.Nodes.Count);
        Assert.Equal(2, lineage.CarriedSaveCount);
        Assert.Equal(0, lineage.BranchPointCount);

        var nodeD = lineage.Nodes[0];
        var nodeCpE = lineage.Nodes[1];
        var nodeSaveE = lineage.Nodes[2];
        var nodeSaveF = lineage.Nodes[3];

        Assert.Equal(SaveLineageNodeKind.Checkpoint, nodeCpE.Kind);
        Assert.Equal(240_000, nodeCpE.CarriedThroughSaveTick);
        Assert.Equal(SaveLineageNodeKind.CarriedCheckpointSave, nodeSaveE.Kind);
        Assert.Equal(SaveLineageNodeKind.CarriedCheckpointSave, nodeSaveF.Kind);
        Assert.Equal(1, nodeSaveE.ParentIndex);
        Assert.Equal(1, nodeSaveF.ParentIndex);
        Assert.DoesNotContain(lineage.Edges, e => e.FromIndex == 2 && e.ToIndex == 3);

        // Save E and Save F share carried checkpoint cp-e: neither proven continuation nor proven separate branches.
        var betweenEF = lineage.CompareNodes(2, 3);
        Assert.Equal(SaveLineageRelation.SharedCarriedOrigin, betweenEF.Relation);
        Assert.Equal(1, betweenEF.CommonAncestorIndex);
        Assert.Equal(HistoryChangeIssue.NotLinked, lineage.CompareMetric(SaveMetric, 2, 3).Issue);

        // Comparing Save D (node 0) to Save F (node 3) is proven via cp-e!
        var dToF = lineage.CompareMetric(SaveMetric, 0, 3);
        Assert.Equal(HistoryChangeIssue.None, dToF.Issue);
        Assert.Equal(1100d, dToF.Delta);

        // Comparing cp-e (node 1) to Save F (node 3) on a checkpoint counter recognizes the carried checkpoint at tick 120_000.
        var cpToF = lineage.CompareNodes(1, 3);
        Assert.Equal(SaveLineageRelation.SameCheckpointCarried, cpToF.Relation);
        Assert.Equal(HistoryChangeIssue.SamePoint, lineage.CompareMetric(RunningTotal, 1, 3).Issue);
    }

    [Fact]
    public void SiblingBranchesWithReload_SeparateLanesMarkRewindAndRefuseCrossBranchSubtraction()
    {
        // Root at tick 60_000 (10:10) -> Main branch at tick 180_000 (10:30)
        // Reload Root -> Alternate branch at tick 90_000 (10:45, saved later in wall-clock time at an earlier game tick!)
        var profile = MakeProfile(
            SaveObs("root.rws", 60_000, 1000, Epoch.AddMinutes(10)),
            CheckpointIn("root.rws", "cp-root", null, 60_000, 2, Epoch.AddMinutes(10)),
            SaveObs("main.rws", 180_000, 3500, Epoch.AddMinutes(30)),
            CheckpointIn("main.rws", "cp-main", "cp-root", 180_000, 10, Epoch.AddMinutes(30)),
            SaveObs("alt.rws", 90_000, 1400, Epoch.AddMinutes(45)),
            CheckpointIn("alt.rws", "cp-alt", "cp-root", 90_000, 4, Epoch.AddMinutes(45)));

        var lineage = Build(profile);

        Assert.Equal(CheckpointLineage.Branched, lineage.SummaryLineage);
        Assert.Equal(1, lineage.BranchPointCount);
        Assert.Equal(3, lineage.Nodes.Count);

        var root = lineage.Nodes.Single(n => n.CheckpointId == "cp-root");
        var alt = lineage.Nodes.Single(n => n.CheckpointId == "cp-alt");
        var main = lineage.Nodes.Single(n => n.CheckpointId == "cp-main");

        Assert.True(root.IsBranchPoint);
        Assert.True(main.IsRepresentative);
        Assert.True(main.OnRepresentativeLine);
        Assert.Equal(0, main.Lane);

        Assert.True(alt.IsAlternateBranch);
        Assert.True(alt.HasRewindEvidence);
        Assert.False(main.HasRewindEvidence);
        Assert.NotEqual(0, alt.Lane);

        var branchCmp = lineage.CompareNodes(alt.Index, main.Index);
        Assert.Equal(SaveLineageRelation.SiblingBranches, branchCmp.Relation);
        Assert.Equal(root.Index, branchCmp.CommonAncestorIndex);
        Assert.Equal(HistoryChangeIssue.NotLinked, lineage.CompareMetric(RunningTotal, alt.Index, main.Index).Issue);
        Assert.Equal(HistoryChangeIssue.NotLinked, lineage.CompareMetric(SaveMetric, alt.Index, main.Index).Issue);
    }

    [Fact]
    public void UnprovenSavesAndMissingParents_NeverCreateSpeculativeEdges()
    {
        var profile = MakeProfile(
            SaveObs("premod1.rws", 30_000, 800, Epoch.AddMinutes(1)),
            SaveObs("premod2.rws", 50_000, 950, Epoch.AddMinutes(5)),
            SaveObs("orphan.rws", 120_000, 2000, Epoch.AddMinutes(20)),
            CheckpointIn("orphan.rws", "cp-orphan", "cp-deleted", 120_000, 6, Epoch.AddMinutes(20)));

        var lineage = Build(profile);

        Assert.Equal(CheckpointLineage.Unknown, lineage.SummaryLineage);
        Assert.Equal(3, lineage.Nodes.Count);
        Assert.Empty(lineage.Edges);
        Assert.Equal(2, lineage.UnprovenNodeCount);
        Assert.Equal(1, lineage.MissingParentCount);
        Assert.Equal(SaveLineageParentState.MissingParent, lineage.Nodes[2].ParentState);
        Assert.Equal("cp-deleted", lineage.Nodes[2].RawParentCheckpointId);

        Assert.Equal(SaveLineageRelation.Unproven, lineage.CompareNodes(0, 1).Relation);
        Assert.Equal(SaveLineageRelation.Unproven, lineage.CompareNodes(1, 2).Relation);
        Assert.Equal(HistoryChangeIssue.NotLinked, lineage.CompareMetric(SaveMetric, 0, 1).Issue);
    }

    [Fact]
    public void ConflictedCheckpoints_AreIsolatedAndNeverLinkedToChildren()
    {
        var cp1 = CheckpointIn("c1.rws", "cp-dup", null, 60_000, 3, Epoch.AddMinutes(5));
        cp1.Conflicted = true;
        var child = CheckpointIn("c2.rws", "cp-child", "cp-dup", 120_000, 8, Epoch.AddMinutes(15));

        var profile = MakeProfile(
            SaveObs("c1.rws", 60_000, 1000, Epoch.AddMinutes(5)), cp1,
            SaveObs("c2.rws", 120_000, 1500, Epoch.AddMinutes(15)), child);

        var lineage = Build(profile);

        Assert.Equal(1, lineage.ConflictedNodeCount);
        Assert.Equal(1, lineage.MissingParentCount);
        Assert.Empty(lineage.Edges);
        Assert.Equal(SaveLineageNodeKind.ConflictedCheckpoint, lineage.Nodes[0].Kind);
        Assert.Equal(SaveLineageParentState.InvalidOrConflictedParent, lineage.Nodes[1].ParentState);
    }

    [Fact]
    public void ArchivedAncestorCheckpoint_RestoresMetricsWithoutMutatingProfile()
    {
        var fullParent = CheckpointIn("p.rws", "cp-parent", null, 60_000, 4, Epoch.AddMinutes(10));
        var inlineParent = CheckpointIn("p.rws", "cp-parent", null, 60_000, 4, Epoch.AddMinutes(10));
        inlineParent.Metrics.Clear();
        inlineParent.Compressed = true;
        inlineParent.ArchivedMetrics = true;

        var child = CheckpointIn("c.rws", "cp-child", "cp-parent", 120_000, 11, Epoch.AddMinutes(25));
        var profile = MakeProfile(
            SaveObs("p.rws", 60_000, 1000, Epoch.AddMinutes(10)), inlineParent,
            SaveObs("c.rws", 120_000, 2200, Epoch.AddMinutes(25)), child);

        var archive = new FakeArchive([fullParent]);
        var lineage = Build(profile, archive);

        Assert.Equal(SaveLineageArchiveState.RestoredFromArchive, lineage.Nodes[0].ArchiveState);
        Assert.Equal(SaveLineageArchiveState.Inline, lineage.Nodes[1].ArchiveState);
        var change = lineage.CompareMetric(RunningTotal, 0, 1);
        Assert.Equal(HistoryChangeIssue.None, change.Issue);
        Assert.Equal(7d, change.Delta);

        // In-memory profile observation must remain compressed and untouched!
        Assert.Empty(inlineParent.Metrics);
        Assert.True(inlineParent.ArchivedMetrics);

        // Without the archive, the lineage still renders the proven edge and reports ArchiveUnavailable cleanly.
        var withoutArchive = Build(profile, archive: null);
        Assert.Equal(SaveLineageArchiveState.ArchivedUnavailable, withoutArchive.Nodes[0].ArchiveState);
        Assert.Single(withoutArchive.Edges);
        Assert.Equal(HistoryChangeIssue.NoValue, withoutArchive.CompareMetric(RunningTotal, 0, 1).Issue);
    }

    [Fact]
    public void EnvironmentChangesAndVerifiedCounterIntervals_AreSurfacedOnNodesAndComparisons()
    {
        var parentCp = CheckpointIn("p.rws", "cp-1", null, 60_000, 2, Epoch.AddMinutes(10));
        parentCp.GameVersion = "1.7.4980 rev1200";
        parentCp.ModSetHash = HashA;
        parentCp.ActiveDlcIds = ["ludeon.rimworld.royalty"];

        var childCp = CheckpointIn("c.rws", "cp-2", "cp-1", 120_000, 6, Epoch.AddMinutes(20));
        childCp.GameVersion = "1.7.4985 rev1269";
        childCp.ModSetHash = HashB;
        childCp.ActiveDlcIds = ["ludeon.rimworld.royalty", "ludeon.rimworld.biotech"];

        var profile = MakeProfile(
            SaveObs("p.rws", 60_000, 1000, Epoch.AddMinutes(10)), parentCp,
            SaveObs("c.rws", 120_000, 1500, Epoch.AddMinutes(20)), childCp);

        var lineage = Build(profile);
        var changedNode = lineage.Nodes.Single(n => n.CheckpointId == "cp-2");
        Assert.True(changedNode.GameVersionChangedFromParent);
        Assert.True(changedNode.ModSetChangedFromParent);
        Assert.True(changedNode.DlcChangedFromParent);

        var cmpChanged = lineage.CompareNodes(0, changedNode.Index);
        Assert.True(cmpChanged.GameVersionChangedAlongPath);
        Assert.True(cmpChanged.ModSetChangedAlongPath);
        Assert.True(cmpChanged.DlcChangedAlongPath);

        // Verified vanilla counter interval from two real SaveReader-decoded saves:
        var save1Bytes = BuildSaveBytes("cp-iv-1", null, 60_000, raids: 2, deaths: 1);
        var save2Bytes = BuildSaveBytes("cp-iv-2", "cp-iv-1", 120_000, raids: 5, deaths: 3);
        var r1 = new SaveReader().ReadDetailed(new MemoryStream(save1Bytes), "iv1.rws", save1Bytes.Length, Epoch.AddMinutes(10));
        var r2 = new SaveReader().ReadDetailed(new MemoryStream(save2Bytes), "iv2.rws", save2Bytes.Length, Epoch.AddMinutes(20));
        var ivProfile = new Profile();
        ivProfile.SaveObservations.Add(r1.Observation);
        ivProfile.SaveObservations.Add(r2.Observation);
        ProfileMutations.UpsertMetricObservation(ivProfile, r1.Metrics!);
        ProfileMutations.UpsertMetricObservation(ivProfile, r1.Checkpoint!);
        ProfileMutations.UpsertMetricObservation(ivProfile, r2.Metrics!);
        ProfileMutations.UpsertMetricObservation(ivProfile, r2.Checkpoint!);

        var ivColonies = CampaignMatcher.BuildColonies(ivProfile);
        var ivColony = Assert.Single(ivColonies);
        var ivLineage = ColonySaveLineage.Build(ivProfile, ivColonies, ivColony.Key);
        var verifiedNode = ivLineage.Nodes.Single(n => n.CheckpointId == "cp-iv-2");
        Assert.NotNull(verifiedNode.VerifiedParentInterval);
        Assert.Equal(3L, verifiedNode.VerifiedParentInterval!.RaidsIncrease);
        Assert.Equal(2L, verifiedNode.VerifiedParentInterval.ColonistDeathsIncrease);
    }

    [Fact]
    public void ThreeThousandSaveColonyWithBranches_BuildsLineageQuicklyAndDeterministically()
    {
        var observations = new List<MetricObservation>(6000);
        for (var i = 0; i < 3000; i++)
        {
            var tick = (i + 1) * 2000;
            var file = $"save_{i:D4}.rws";
            string? parent = i == 0 ? null
                : i % 250 == 0 ? $"cp-{i - 50}" // periodic branch fork every 250 saves
                : $"cp-{i - 1}";
            var utc = Epoch.AddMinutes(i);
            observations.Add(SaveObs(file, tick, 1000 + i * 10, utc));
            observations.Add(CheckpointIn(file, $"cp-{i}", parent, tick, i, utc));
        }
        var profile = new Profile { MetricObservations = observations };
        var colony = MakeColony();

        var sw = Stopwatch.StartNew();
        var lineage = ColonySaveLineage.Build(profile, [colony], colony.Key);
        sw.Stop();

        Assert.Equal(3000, lineage.Nodes.Count);
        Assert.Equal(2999, lineage.Edges.Count);
        Assert.Equal(CheckpointLineage.Branched, lineage.SummaryLineage);
        Assert.True(lineage.BranchPointCount >= 11);
        Assert.True(sw.ElapsedMilliseconds < 2500, $"Expected < 2500 ms for 3,000 saves, took {sw.ElapsedMilliseconds} ms");
    }

    // ----- helpers -----

    private static ColonySaveLineage Build(Profile profile, ICheckpointArchive? archive = null)
    {
        var colony = MakeColony();
        return ColonySaveLineage.Build(profile, [colony], colony.Key, archive);
    }

    private static Colony MakeColony()
    {
        var colony = new Colony { Key = "id:" + Campaign };
        colony.LiveCampaigns.Add(new LiveCampaign { CampaignId = Campaign });
        return colony;
    }

    private static Profile MakeProfile(params MetricObservation[] observations)
        => new() { MetricObservations = [.. observations] };

    private static MetricFileAlias Alias(string file, DateTime? utc = null) => new()
    {
        FileName = file,
        FileSize = 4096 + file.Length,
        LastWriteUtc = utc ?? Epoch,
    };

    private static SaveObservation V1Save(MetricFileAlias alias, int tick, string contentHash) => new()
    {
        FileName = alias.FileName,
        FileSize = alias.FileSize,
        LastWriteUtc = alias.LastWriteUtc,
        Status = ObservationStatus.Ok,
        ContentHash = contentHash,
        GameVersion = "1.7.4985 rev1269",
        Identity = new ColonyIdentity { ModCampaignId = Campaign },
        Display = new DisplayInfo { FactionName = "New Arrivals", WorldName = "Planet" },
        Values = new ObservedValues { TicksGame = tick },
    };

    private static byte[] BuildSaveBytes(string checkpointId, string? parentId, int tick, int raids, int deaths)
    {
        var cp = new CheckpointPayload
        {
            CheckpointId = checkpointId,
            ParentCheckpointId = parentId,
            SaveTick = tick,
            SavedUtc = Epoch.AddMinutes(tick / 60_000.0),
        };
        return new RimworldStatistics.Core.Tests.Fixtures.SaveXmlBuilder()
            .Minimal()
            .MetaMods("1.7.4985 rev1269", "ludeon.rimworld", "ludeon.rimworld.royalty")
            .Tick(ticksGame: tick, start: 0)
            .Stats(raids: raids, killed: deaths)
            .Component(SaveReader.ComponentClassName,
                new System.Xml.Linq.XElement("campaignId", "iv-camp"),
                new System.Xml.Linq.XElement("numericStatistics", CheckpointJson.Serialize(cp)))
            .Build();
    }

    private static MetricObservation SaveObs(string file, int tick, int wealth, DateTime utc) => new()
    {
        Source = MetricObservationSource.SaveFile,
        Key = "sha-" + file,
        SaveTick = tick,
        ObservedUtc = utc,
        GameVersion = "1.7.4985 rev1269",
        ModSetHash = HashA,
        Identity = new ColonyIdentity { ModCampaignId = Campaign },
        FileAliases = { Alias(file, utc) },
        Metrics =
        {
            [SaveMetric.StoredKey] = MetricEntry.Of(SaveMetric.Revision,
                new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = wealth }),
        },
    };

    private static MetricObservation CheckpointIn(string file, string key, string? parent, int tick, int completed, DateTime utc)
        => CheckpointIn([file], key, parent, tick, completed, utc);

    private static MetricObservation CheckpointIn(string[] files, string key, string? parent, int tick, int completed, DateTime utc)
    {
        var obs = new MetricObservation
        {
            Source = MetricObservationSource.Checkpoint,
            Key = key,
            ParentKey = parent,
            PayloadHash = "hash-" + key,
            SaveTick = tick,
            ObservedUtc = utc,
            GameVersion = "1.7.4985 rev1269",
            ModSetHash = HashA,
            Identity = new ColonyIdentity { ModCampaignId = Campaign },
            Metrics =
            {
                [RunningTotal.StoredKey] = MetricEntry.Of(RunningTotal.Revision,
                    new MetricValue { Coverage = MetricCoverage.Complete, IntegerValue = completed, SinceTick = 0 }),
            },
        };
        foreach (var f in files)
            obs.FileAliases.Add(Alias(f, utc));
        return obs;
    }

    private sealed class FakeArchive(IReadOnlyList<MetricObservation> items) : ICheckpointArchive
    {
        public void Append(string campaignId, IReadOnlyList<string> lines) { }
        public IReadOnlyList<MetricObservation> Read(string campaignId, string key)
            => items.Where(o => o.Identity?.ModCampaignId == campaignId && o.Key == key).ToArray();
        public IReadOnlyList<MetricObservation> ReadMany(string campaignId, IReadOnlySet<string> keys)
            => items.Where(o => o.Identity?.ModCampaignId == campaignId && keys.Contains(o.Key)).ToArray();
    }
}
