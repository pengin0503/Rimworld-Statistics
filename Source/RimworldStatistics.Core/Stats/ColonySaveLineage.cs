namespace RimworldStatistics.Core;

/// <summary>What kind of saved record a node in a colony's save lineage represents (V2-B).</summary>
public enum SaveLineageNodeKind
{
    /// <summary>A unique, unconflicted embedded checkpoint (multiple byte-identical save copies share one node).</summary>
    Checkpoint,

    /// <summary>A later save that carried an earlier checkpoint unchanged without committing a new checkpoint of its own.</summary>
    CarriedCheckpointSave,

    /// <summary>A save or snapshot without a proven checkpoint anchor (pre-MOD save, recording off, or ambiguous file).</summary>
    UnprovenSave,

    /// <summary>A checkpoint id seen with conflicting hashes or duplicate records; never linked to parents or children.</summary>
    ConflictedCheckpoint,
}

/// <summary>Why a node does or does not have a proven parent edge.</summary>
public enum SaveLineageParentState
{
    /// <summary>A checkpoint at the beginning of a recorded chain (<c>ParentKey == null</c>).</summary>
    Root,

    /// <summary>Connected to a unique, unconflicted parent checkpoint in the same campaign.</summary>
    ProvenParent,

    /// <summary>Records a <c>ParentKey</c>, but no checkpoint with that id is present in the profile.</summary>
    MissingParent,

    /// <summary>Records a <c>ParentKey</c> whose target is conflicted, duplicated, later in ticks, cyclic, or in another campaign.</summary>
    InvalidOrConflictedParent,

    /// <summary>No embedded checkpoint id is associated with this save, so parentage cannot be proven.</summary>
    NoCheckpoint,

    /// <summary>This checkpoint itself is conflicted or duplicated and cannot take part in any proven chain.</summary>
    SelfConflicted,
}

/// <summary>Where a checkpoint node's detailed metric entries currently live.</summary>
public enum SaveLineageArchiveState
{
    None,
    Inline,
    RestoredFromArchive,
    ArchivedUnavailable,
}

/// <summary>The proven link drawn between two nodes of a colony's save lineage.</summary>
public enum SaveLineageEdgeKind
{
    /// <summary>Direct parent-to-child embedded checkpoint edge across game time.</summary>
    DirectCheckpoint,

    /// <summary>Direct parent-to-child embedded checkpoint edge saved at the same game tick (e.g. re-saved while paused).</summary>
    PausedResave,

    /// <summary>A later save carrying an earlier checkpoint unchanged; descends from that checkpoint, never a parent itself.</summary>
    CarriedCheckpoint,
}

/// <summary>How two nodes in the same colony's save lineage are related by proven checkpoint evidence.</summary>
public enum SaveLineageRelation
{
    SameNode,
    DirectParentChild,
    ProvenLineage,
    SameCheckpointCarried,
    SiblingBranches,
    SharedCarriedOrigin,
    Unproven,
}

/// <summary>A save file reference attached to a lineage node.</summary>
public sealed record SaveLineageFileRef(
    string FileName,
    long FileSize,
    DateTime LastWriteUtc,
    bool Superseded = false)
{
    public bool Matches(SaveFileIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        return index.Contains(new SaveObservation
        {
            FileName = FileName,
            FileSize = FileSize,
            LastWriteUtc = LastWriteUtc,
        });
    }
}

/// <summary>A proven directed edge from an earlier/parent node to a child/descendant node.</summary>
public sealed record SaveLineageEdge(
    int FromIndex,
    int ToIndex,
    int FromLane,
    int ToLane,
    SaveLineageEdgeKind Kind);

/// <summary>One node in a colony's read-only save lineage graph.</summary>
public sealed class SaveLineageNode
{
    public required int Index { get; init; }
    public required int Lane { get; init; }
    public required SaveLineageNodeKind Kind { get; init; }
    public required SaveLineageParentState ParentState { get; init; }
    public int ParentIndex { get; init; } = -1;
    public IReadOnlyList<int> ChildIndices { get; init; } = [];
    public required int SaveTick { get; init; }
    public int? CapturedTick { get; init; }
    public int? CarriedThroughSaveTick { get; init; }
    public DateTime? ObservedUtc { get; init; }
    public string? CampaignId { get; init; }
    public string? CheckpointId { get; init; }
    public string? RawParentCheckpointId { get; init; }
    public IReadOnlyList<SaveLineageFileRef> Files { get; init; } = [];
    public IReadOnlyList<SaveLineageFileRef> CarriedByFiles { get; init; } = [];
    public bool HasLiveSnapshotOnly { get; init; }
    public string? GameVersion { get; init; }
    public string? ModSetHash { get; init; }
    public IReadOnlyList<string> ActiveDlcIds { get; init; } = [];
    public bool IsRepresentative { get; init; }
    public bool OnRepresentativeLine { get; init; }
    public bool IsBranchPoint { get; init; }
    public bool IsAlternateBranch { get; init; }
    public bool HasRewindEvidence { get; init; }
    public bool SameTickParent { get; init; }
    public bool GameVersionChangedFromParent { get; init; }
    public bool ModSetChangedFromParent { get; init; }
    public bool DlcChangedFromParent { get; init; }
    public SaveLineageArchiveState ArchiveState { get; init; }
    public HistoricalCounterInterval? VerifiedParentInterval { get; init; }

    internal ObservedValues? BasicValues { get; init; }
    internal MetricObservation? SaveFileObservation { get; init; }
    internal MetricObservation? SnapshotObservation { get; init; }
    internal MetricObservation? CheckpointObservation { get; init; }
    internal IReadOnlyDictionary<string, NumericValue>? CompactedSnapshotMetrics { get; init; }
}

/// <summary>The proven relationship between two selected nodes in a colony's save lineage.</summary>
public sealed record SaveLineageNodeComparison
{
    public required int EarlierIndex { get; init; }
    public required int LaterIndex { get; init; }
    public required SaveLineageNode Earlier { get; init; }
    public required SaveLineageNode Later { get; init; }
    public required SaveLineageRelation Relation { get; init; }
    public int Steps { get; init; }
    public int CommonAncestorIndex { get; init; } = -1;
    public bool ModSetChangedAlongPath { get; init; }
    public bool GameVersionChangedAlongPath { get; init; }
    public bool DlcChangedAlongPath { get; init; }
    public HistoricalCounterInterval? DirectCounterInterval { get; init; }
}

/// <summary>
/// Read-only save lineage graph of one colony (V2-B). Edges exist only where embedded checkpoint ids prove direct parentage;
/// matching campaign ids, game ticks, save timestamps or file names never create edges.
/// </summary>
public sealed class ColonySaveLineage
{
    private static readonly StringComparer Ord = StringComparer.Ordinal;
    private const int MaxVisualLanes = 16;

    public required string ColonyKey { get; init; }
    public required CheckpointLineage SummaryLineage { get; init; }
    public required IReadOnlyList<SaveLineageNode> Nodes { get; init; }
    public required IReadOnlyList<SaveLineageEdge> Edges { get; init; }
    public int RepresentativeIndex { get; init; } = -1;
    public int MaxLane { get; init; }
    public int BranchPointCount { get; init; }
    public int UnprovenNodeCount { get; init; }
    public int MissingParentCount { get; init; }
    public int ConflictedNodeCount { get; init; }
    public int CarriedSaveCount { get; init; }
    public int OmittedSnapshotSaves { get; init; }

    public static ColonySaveLineage Empty(string colonyKey) => new()
    {
        ColonyKey = colonyKey,
        SummaryLineage = CheckpointLineage.None,
        Nodes = [],
        Edges = [],
    };

    /// <summary>Captures an immutable read-only lineage snapshot from <paramref name="profile"/>, then optionally restores archived checkpoints.</summary>
    internal sealed record CapturedInputs(
        string ColonyKey,
        SaveObservation[] SaveObservations,
        MetricObservation[] MetricObservations,
        IReadOnlyList<HistoricalCounterInterval> Intervals);

    internal static CapturedInputs? Capture(Profile profile, IReadOnlyList<Colony> colonies, string colonyKey)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(colonies);
        var colony = colonies.FirstOrDefault(c => c.Key == colonyKey);
        if (colony is null)
            return null;

        var attached = MetricCalculator.Attach(profile, colonies, out _).GetValueOrDefault(colony) ?? [];
        var intervals = HistoricalCounterIntervals.Evaluate(profile);
        var saves = colony.Observations.Where(o => o.Status == ObservationStatus.Ok && o.Values.TicksGame >= 0).ToArray();
        var metrics = attached.Where(o => !o.IsQuarantined).Select(CloneForRead).ToArray();
        return new CapturedInputs(colony.Key, saves, metrics, intervals);
    }

    public static ColonySaveLineage Build(Profile profile, IReadOnlyList<Colony> colonies, string colonyKey, ICheckpointArchive? archive = null)
    {
        var captured = Capture(profile, colonies, colonyKey);
        return captured is null ? Empty(colonyKey) : BuildFromCaptured(captured, archive);
    }

    internal static ColonySaveLineage BuildFromCaptured(CapturedInputs inputs, ICheckpointArchive? archive)
    {
        var observations = inputs.MetricObservations;
        var archiveStates = RestoreFromArchive(observations, archive);
        var selection = CheckpointSelector.Select(observations);
        var ancestry = new CheckpointAncestry(observations);

        var intervalByEdge = inputs.Intervals
            .GroupBy(i => (i.ParentCheckpointId, i.ChildCheckpointId))
            .ToDictionary(g => g.Key, g => g.First());

        // 1. Group physical save files into logical save records so byte-identical copies are never double-counted.
        var logicalSaves = BuildLogicalSaves(inputs.SaveObservations, observations);

        // 2. Partition checkpoints into valid unique nodes vs conflicted/duplicate nodes.
        var checkpoints = observations.Where(o => o.Source == MetricObservationSource.Checkpoint).ToArray();
        var keyCounts = new Dictionary<string, int>(Ord);
        foreach (var cp in checkpoints)
            keyCounts[cp.Key] = keyCounts.GetValueOrDefault(cp.Key) + 1;

        var builders = new List<NodeBuilder>();
        var validCheckpointByKey = new Dictionary<string, NodeBuilder>(Ord);
        var conflictedByAlias = new Dictionary<(string FileName, long FileSize, DateTime LastWriteUtc), NodeBuilder>();

        foreach (var cp in checkpoints)
        {
            var duplicateOrEmpty = cp.Key.Length == 0 || keyCounts[cp.Key] > 1;
            if (cp.Conflicted || duplicateOrEmpty || cp.SaveTick < 0)
            {
                var conflictedNode = new NodeBuilder
                {
                    Kind = SaveLineageNodeKind.ConflictedCheckpoint,
                    ParentState = SaveLineageParentState.SelfConflicted,
                    SaveTick = Math.Max(0, cp.SaveTick),
                    CapturedTick = cp.SaveTick >= 0 ? cp.SaveTick : null,
                    ObservedUtc = ValidUtc(cp.ObservedUtc),
                    CampaignId = cp.Identity?.ModCampaignId,
                    CheckpointId = cp.Key.Length > 0 ? cp.Key : null,
                    RawParentCheckpointId = cp.ParentKey,
                    GameVersion = cp.GameVersion,
                    ModSetHash = cp.ModSetHash,
                    ActiveDlcIds = cp.ActiveDlcIds?.ToArray() ?? [],
                    CheckpointObs = cp,
                    ArchiveState = archiveStates.GetValueOrDefault(cp, SaveLineageArchiveState.None),
                    Files = cp.FileAliases.Select(a => new SaveLineageFileRef(a.FileName, a.FileSize, a.LastWriteUtc)).ToList(),
                };
                foreach (var a in cp.FileAliases)
                    conflictedByAlias.TryAdd((a.FileName, a.FileSize, a.LastWriteUtc), conflictedNode);
                builders.Add(conflictedNode);
                continue;
            }

            var node = new NodeBuilder
            {
                Kind = SaveLineageNodeKind.Checkpoint,
                ParentState = cp.ParentKey is null ? SaveLineageParentState.Root : SaveLineageParentState.MissingParent,
                SaveTick = cp.SaveTick,
                CapturedTick = cp.SaveTick,
                ObservedUtc = ValidUtc(cp.ObservedUtc),
                CampaignId = cp.Identity?.ModCampaignId,
                CheckpointId = cp.Key,
                RawParentCheckpointId = cp.ParentKey,
                GameVersion = cp.GameVersion,
                ModSetHash = cp.ModSetHash,
                ActiveDlcIds = cp.ActiveDlcIds?.ToArray() ?? [],
                CheckpointObs = cp,
                IsRepresentative = ReferenceEquals(cp, selection.Representative),
                ArchiveState = archiveStates.GetValueOrDefault(cp, SaveLineageArchiveState.Inline),
            };
            validCheckpointByKey[cp.Key] = node;
            builders.Add(node);
        }

        // 3. Attach logical saves to their exact checkpoint, or emit carried-checkpoint / unproven save nodes.
        var carriedByKeyAndTick = new Dictionary<(string Key, int Tick, string SaveId), NodeBuilder>();
        var claimedCheckpointAliases = new HashSet<(string FileName, long FileSize, DateTime LastWriteUtc)>();

        foreach (var save in logicalSaves)
        {
            var anchor = ancestry.OfFile(save.Aliases, save.Tick);
            if (anchor is { Exact: true } exact && validCheckpointByKey.TryGetValue(exact.Key, out var cpNode)
                && (save.CampaignId is null || cpNode.CampaignId is null || save.CampaignId == cpNode.CampaignId))
            {
                MergeSaveIntoNode(cpNode, save);
                foreach (var f in save.Files)
                    claimedCheckpointAliases.Add((f.FileName, f.FileSize, f.LastWriteUtc));
                continue;
            }

            if (anchor is { Exact: false } carried && validCheckpointByKey.TryGetValue(carried.Key, out var originCp)
                && (save.CampaignId is null || originCp.CampaignId is null || save.CampaignId == originCp.CampaignId))
            {
                originCp.CarriedThroughSaveTick = Math.Max(originCp.CarriedThroughSaveTick ?? int.MinValue, save.Tick);
                foreach (var f in save.Files)
                {
                    AddUniqueFile(originCp.CarriedByFiles, f);
                    claimedCheckpointAliases.Add((f.FileName, f.FileSize, f.LastWriteUtc));
                }

                var carriedNode = new NodeBuilder
                {
                    Kind = SaveLineageNodeKind.CarriedCheckpointSave,
                    ParentState = SaveLineageParentState.ProvenParent,
                    ParentBuilder = originCp,
                    SaveTick = save.Tick,
                    CapturedTick = originCp.SaveTick,
                    ObservedUtc = save.ObservedUtc,
                    CampaignId = save.CampaignId ?? originCp.CampaignId,
                    CheckpointId = originCp.CheckpointId,
                    GameVersion = save.GameVersion ?? originCp.GameVersion,
                    ModSetHash = save.ModSetHash ?? originCp.ModSetHash,
                    ActiveDlcIds = save.ActiveDlcIds.Count > 0 ? save.ActiveDlcIds : originCp.ActiveDlcIds,
                    BasicValues = save.BasicValues,
                    SaveFileObs = save.SaveFileObs,
                    CheckpointObs = originCp.CheckpointObs,
                };
                foreach (var f in save.Files)
                    AddUniqueFile(carriedNode.Files, f);
                carriedByKeyAndTick[(carried.Key, save.Tick, save.GroupKey)] = carriedNode;
                builders.Add(carriedNode);
                continue;
            }

            var matchedConflict = save.Files
                .Select(f => conflictedByAlias.GetValueOrDefault((f.FileName, f.FileSize, f.LastWriteUtc)))
                .FirstOrDefault(n => n is not null && n.SaveTick == save.Tick);
            if (matchedConflict is not null)
            {
                MergeSaveIntoNode(matchedConflict, save);
                continue;
            }

            var unproven = new NodeBuilder
            {
                Kind = SaveLineageNodeKind.UnprovenSave,
                ParentState = SaveLineageParentState.NoCheckpoint,
                SaveTick = save.Tick,
                ObservedUtc = save.ObservedUtc,
                CampaignId = save.CampaignId,
                GameVersion = save.GameVersion,
                ModSetHash = save.ModSetHash,
                ActiveDlcIds = save.ActiveDlcIds,
                BasicValues = save.BasicValues,
                SaveFileObs = save.SaveFileObs,
            };
            foreach (var f in save.Files)
                AddUniqueFile(unproven.Files, f);
            builders.Add(unproven);
        }

        // Preserve file aliases recorded on a checkpoint whose SaveObservation/SaveFile was never separately present at another tick.
        foreach (var cpNode in validCheckpointByKey.Values)
        {
            foreach (var alias in cpNode.CheckpointObs!.FileAliases)
            {
                if (claimedCheckpointAliases.Contains((alias.FileName, alias.FileSize, alias.LastWriteUtc)))
                    continue;
                AddUniqueFile(cpNode.Files, new SaveLineageFileRef(alias.FileName, alias.FileSize, alias.LastWriteUtc));
            }
            if (cpNode.Files.Count == 0 && cpNode.CarriedByFiles.Count == 0)
                cpNode.HasLiveSnapshotOnly = true;
        }

        // 4. Attach SaveSnapshot observations (and #318 compacted snapshot chart rows) without duplicating existing nodes.
        var snapshots = observations.Where(o => o.Source == MetricObservationSource.SaveSnapshot && o.SaveTick >= 0).ToArray();
        var carriedByParentTick = new Dictionary<(NodeBuilder Origin, int Tick), NodeBuilder>();
        var unprovenByTick = new Dictionary<int, List<NodeBuilder>>();
        foreach (var existing in builders)
        {
            if (existing.Kind == SaveLineageNodeKind.UnprovenSave)
            {
                if (!unprovenByTick.TryGetValue(existing.SaveTick, out var list))
                    unprovenByTick[existing.SaveTick] = list = new List<NodeBuilder>();
                list.Add(existing);
            }
        }
        var compactedCount = 0;
        foreach (var snap in snapshots)
        {
            AttachSnapshot(snap, null, ancestry, validCheckpointByKey, builders, carriedByParentTick, unprovenByTick);
            if (snap.CompactedChart?.Rows is not { } rows)
                continue;
            for (var r = 0; r < rows.Count; r++)
            {
                if (rows[r] is not { Tick: >= 0 } row)
                    continue;
                compactedCount++;
                var ghost = new MetricObservation
                {
                    Source = MetricObservationSource.SaveSnapshot,
                    Identity = snap.Identity,
                    SaveTick = row.Tick,
                    ObservedUtc = row.ObservedUtc,
                    CheckpointKey = row.CheckpointKey,
                };
                var rowMetrics = ExtractCompactedMetrics(snap.CompactedChart, r);
                AttachSnapshot(ghost, rowMetrics, ancestry, validCheckpointByKey, builders, carriedByParentTick, unprovenByTick);
            }
        }

        var totalSnapshotSaves = snapshots.Sum(o => (long)Math.Max(1, o.CompactedObservationCount ?? 1));
        var omittedSnapshots = snapshots.Length == 0
            ? 0 : (int)Math.Clamp(totalSnapshotSaves - snapshots.Length - compactedCount, 0, int.MaxValue);

        // 5. Resolve proven checkpoint parent edges using CheckpointAncestry's exact rules (acyclic, non-conflicted, same campaign).
        foreach (var cpNode in validCheckpointByKey.Values)
        {
            var key = cpNode.CheckpointId!;
            if (cpNode.RawParentCheckpointId is not { Length: > 0 } rawParent)
            {
                cpNode.ParentState = SaveLineageParentState.Root;
                continue;
            }
            if (ancestry.Parent(key) is { } provenParent && validCheckpointByKey.TryGetValue(provenParent, out var parentNode))
            {
                cpNode.ParentBuilder = parentNode;
                cpNode.ParentState = SaveLineageParentState.ProvenParent;
                continue;
            }
            cpNode.ParentState = keyCounts.ContainsKey(rawParent)
                ? SaveLineageParentState.InvalidOrConflictedParent
                : SaveLineageParentState.MissingParent;
        }

        // Guard against cycles in checkpoint chains: break any cycle back to InvalidOrConflictedParent.
        BreakCycles(validCheckpointByKey.Values);

        // 6. Mark representative lineage, branches, rewind evidence, and environment changes.
        if (selection.Representative is { Key: { } repKey } && validCheckpointByKey.TryGetValue(repKey, out var repBuilder))
        {
            var seen = new HashSet<NodeBuilder>();
            for (var cur = repBuilder; cur is not null && seen.Add(cur); cur = cur.ParentBuilder)
                cur.OnRepresentativeLine = true;
        }

        foreach (var b in builders)
            b.ParentBuilder?.Children.Add(b);

        foreach (var b in validCheckpointByKey.Values)
        {
            var cpChildren = b.Children.Where(c => c.Kind == SaveLineageNodeKind.Checkpoint).ToArray();
            b.IsBranchPoint = cpChildren.Length >= 2;
            if (cpChildren.Length >= 2)
                MarkRewindEvidence(cpChildren);
        }

        foreach (var b in builders)
        {
            if (b.Kind == SaveLineageNodeKind.Checkpoint && !b.OnRepresentativeLine && b.ParentBuilder is not null)
                b.IsAlternateBranch = true;
            if (b.ParentBuilder is { } parent)
            {
                b.SameTickParent = b.Kind == SaveLineageNodeKind.Checkpoint && b.SaveTick == parent.SaveTick;
                b.GameVersionChangedFromParent = b.GameVersion is { Length: > 0 } gv && parent.GameVersion is { Length: > 0 } pgv
                    && !Ord.Equals(gv, pgv);
                b.ModSetChangedFromParent = ValidHash(b.ModSetHash) && ValidHash(parent.ModSetHash)
                    && !Ord.Equals(b.ModSetHash, parent.ModSetHash);
                b.DlcChangedFromParent = b.ActiveDlcIds.Count > 0 && parent.ActiveDlcIds.Count > 0
                    && !HistoricalCounterIntervals.SameSavedDlc(b.ActiveDlcIds, parent.ActiveDlcIds);
                if (b.Kind == SaveLineageNodeKind.Checkpoint && parent.CheckpointId is { } pid && b.CheckpointId is { } cid
                    && intervalByEdge.TryGetValue((pid, cid), out var interval))
                    b.VerifiedParentInterval = interval;
            }
        }

        // 7. Compute topological depth at identical ticks so parents always precede children, then sort deterministically.
        ComputeDepthWithinTick(builders);
        var ordered = builders
            .OrderBy(b => b.SaveTick)
            .ThenBy(b => b.SameTickDepth)
            .ThenBy(b => KindOrder(b.Kind))
            .ThenBy(b => b.ObservedUtc is { } utc ? UtcTime.ToUtc(utc) : DateTime.MaxValue)
            .ThenBy(b => b.CheckpointId ?? "", Ord)
            .ThenBy(b => FirstFileName(b), Ord)
            .ThenBy(b => b.SnapshotObs?.Key ?? "", Ord)
            .ToArray();

        for (var i = 0; i < ordered.Length; i++)
            ordered[i].AssignedIndex = i;

        // 8. Assign visual lanes without overlapping unrelated branch verticals.
        var maxLane = AssignLanes(ordered, hasRepresentativeLine: ordered.Any(b => b.OnRepresentativeLine));

        // 9. Materialize immutable nodes and edges.
        var nodes = new SaveLineageNode[ordered.Length];
        var edges = new List<SaveLineageEdge>();
        for (var i = 0; i < ordered.Length; i++)
        {
            var b = ordered[i];
            var parentIndex = b.ParentBuilder?.AssignedIndex ?? -1;
            if (parentIndex >= i)
                parentIndex = -1; // Defensive: topological sort guarantees parentIndex < i.

            var childIndices = b.Children
                .Select(c => c.AssignedIndex)
                .Where(idx => idx > i)
                .Order()
                .ToArray();

            if (parentIndex >= 0)
            {
                var p = ordered[parentIndex];
                var edgeKind = b.Kind == SaveLineageNodeKind.CarriedCheckpointSave
                    ? SaveLineageEdgeKind.CarriedCheckpoint
                    : b.SameTickParent ? SaveLineageEdgeKind.PausedResave : SaveLineageEdgeKind.DirectCheckpoint;
                edges.Add(new SaveLineageEdge(parentIndex, i, p.Lane, b.Lane, edgeKind));
            }

            nodes[i] = new SaveLineageNode
            {
                Index = i,
                Lane = b.Lane,
                Kind = b.Kind,
                ParentState = b.ParentState,
                ParentIndex = parentIndex,
                ChildIndices = childIndices,
                SaveTick = b.SaveTick,
                CapturedTick = b.CapturedTick,
                CarriedThroughSaveTick = b.CarriedThroughSaveTick,
                ObservedUtc = b.ObservedUtc,
                CampaignId = b.CampaignId,
                CheckpointId = b.CheckpointId,
                RawParentCheckpointId = b.RawParentCheckpointId,
                Files = b.Files.OrderBy(f => f.FileName, Ord).ThenBy(f => f.LastWriteUtc).ToArray(),
                CarriedByFiles = b.CarriedByFiles.OrderBy(f => f.FileName, Ord).ThenBy(f => f.LastWriteUtc).ToArray(),
                HasLiveSnapshotOnly = b.HasLiveSnapshotOnly,
                GameVersion = b.GameVersion,
                ModSetHash = b.ModSetHash,
                ActiveDlcIds = b.ActiveDlcIds,
                IsRepresentative = b.IsRepresentative,
                OnRepresentativeLine = b.OnRepresentativeLine,
                IsBranchPoint = b.IsBranchPoint,
                IsAlternateBranch = b.IsAlternateBranch,
                HasRewindEvidence = b.HasRewindEvidence,
                SameTickParent = b.SameTickParent,
                GameVersionChangedFromParent = b.GameVersionChangedFromParent,
                ModSetChangedFromParent = b.ModSetChangedFromParent,
                DlcChangedFromParent = b.DlcChangedFromParent,
                ArchiveState = b.ArchiveState,
                VerifiedParentInterval = b.VerifiedParentInterval,
                BasicValues = b.BasicValues,
                SaveFileObservation = b.SaveFileObs,
                SnapshotObservation = b.SnapshotObs,
                CheckpointObservation = b.CheckpointObs,
                CompactedSnapshotMetrics = b.CompactedMetrics,
            };
        }

        return new ColonySaveLineage
        {
            ColonyKey = inputs.ColonyKey,
            SummaryLineage = selection.Lineage,
            Nodes = nodes,
            Edges = edges,
            RepresentativeIndex = Array.FindIndex(nodes, n => n.IsRepresentative),
            MaxLane = maxLane,
            BranchPointCount = nodes.Count(n => n.IsBranchPoint),
            UnprovenNodeCount = nodes.Count(n => n.Kind == SaveLineageNodeKind.UnprovenSave),
            MissingParentCount = nodes.Count(n => n.ParentState is SaveLineageParentState.MissingParent or SaveLineageParentState.InvalidOrConflictedParent),
            ConflictedNodeCount = nodes.Count(n => n.Kind == SaveLineageNodeKind.ConflictedCheckpoint),
            CarriedSaveCount = nodes.Count(n => n.Kind == SaveLineageNodeKind.CarriedCheckpointSave),
            OmittedSnapshotSaves = omittedSnapshots,
        };
    }

    /// <summary>Evaluates the proven lineage relationship between two nodes of this colony.</summary>
    public SaveLineageNodeComparison CompareNodes(int indexA, int indexB)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(indexA);
        ArgumentOutOfRangeException.ThrowIfNegative(indexB);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(indexA, Nodes.Count);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(indexB, Nodes.Count);

        var (earlierIdx, laterIdx) = indexA <= indexB ? (indexA, indexB) : (indexB, indexA);
        var earlier = Nodes[earlierIdx];
        var later = Nodes[laterIdx];
        if (earlierIdx == laterIdx)
        {
            return new SaveLineageNodeComparison
            {
                EarlierIndex = earlierIdx,
                LaterIndex = laterIdx,
                Earlier = earlier,
                Later = later,
                Relation = SaveLineageRelation.SameNode,
            };
        }

        // Walk up proven parents from Later to see whether Earlier is on its ancestor chain.
        if (earlier.Kind == SaveLineageNodeKind.Checkpoint)
        {
            var steps = 0;
            var modChanged = false;
            var verChanged = false;
            var dlcChanged = false;
            for (var cur = later; cur.ParentIndex >= 0; cur = Nodes[cur.ParentIndex])
            {
                steps++;
                modChanged |= cur.ModSetChangedFromParent;
                verChanged |= cur.GameVersionChangedFromParent;
                dlcChanged |= cur.DlcChangedFromParent;
                if (cur.ParentIndex == earlierIdx)
                {
                    var relation = later.Kind == SaveLineageNodeKind.CarriedCheckpointSave && steps == 1
                        ? SaveLineageRelation.SameCheckpointCarried
                        : steps == 1 ? SaveLineageRelation.DirectParentChild : SaveLineageRelation.ProvenLineage;
                    return new SaveLineageNodeComparison
                    {
                        EarlierIndex = earlierIdx,
                        LaterIndex = laterIdx,
                        Earlier = earlier,
                        Later = later,
                        Relation = relation,
                        Steps = steps,
                        CommonAncestorIndex = earlierIdx,
                        ModSetChangedAlongPath = modChanged,
                        GameVersionChangedAlongPath = verChanged,
                        DlcChangedAlongPath = dlcChanged,
                        DirectCounterInterval = steps == 1 ? later.VerifiedParentInterval : null,
                    };
                }
            }
        }

        // Check whether both nodes trace back to a proven common checkpoint ancestor.
        var earlierAncestors = AncestorChain(earlierIdx);
        var laterAncestors = AncestorChain(laterIdx);
        var common = laterAncestors.FirstOrDefault(idx => earlierAncestors.Contains(idx), -1);
        if (common >= 0)
        {
            // Strict sibling proof (#72 §7.1): both nodes must hold their own distinct checkpoint (not merely carry an
            // older checkpoint unchanged) so neither could silently be an intermediate save along the other's play session.
            var bothOwnCheckpoints = earlier.Kind == SaveLineageNodeKind.Checkpoint
                && later.Kind == SaveLineageNodeKind.Checkpoint;
            return new SaveLineageNodeComparison
            {
                EarlierIndex = earlierIdx,
                LaterIndex = laterIdx,
                Earlier = earlier,
                Later = later,
                Relation = bothOwnCheckpoints ? SaveLineageRelation.SiblingBranches : SaveLineageRelation.SharedCarriedOrigin,
                CommonAncestorIndex = common,
            };
        }

        return new SaveLineageNodeComparison
        {
            EarlierIndex = earlierIdx,
            LaterIndex = laterIdx,
            Earlier = earlier,
            Later = later,
            Relation = SaveLineageRelation.Unproven,
        };
    }

    /// <summary>Reads a single metric's value at <paramref name="nodeIndex"/> using the exact rules of <see cref="MetricCalculator"/>.</summary>
    public NumericValue? MetricAt(int nodeIndex, MetricDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentOutOfRangeException.ThrowIfNegative(nodeIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(nodeIndex, Nodes.Count);
        var node = Nodes[nodeIndex];

        if (BasicMetrics.Contains(definition.Key))
            return node.BasicValues is { } basic ? MetricCalculator.BasicChartValue(basic, definition.Key) : null;

        if (definition.Source == MetricObservationSource.Checkpoint)
            return node.CheckpointObservation is { } cp && !cp.Conflicted ? MetricCalculator.ChartValue(cp, definition) : null;

        if (definition.Source == MetricObservationSource.SaveFile)
            return node.SaveFileObservation is { } sf ? MetricCalculator.ChartValue(sf, definition) : null;

        if (definition.Source == MetricObservationSource.SaveSnapshot)
        {
            if (node.SnapshotObservation is { } snap && snap.Metrics.Count > 0)
                return MetricCalculator.ChartValue(snap, definition);
            if (node.CompactedSnapshotMetrics is { } compacted
                && compacted.TryGetValue(definition.StoredKey, out var value))
                return value;
            if (definition.Acquisition is { } unit && node.SaveFileObservation is { } sf
                && sf.EvidenceByAcquisition?.GetValueOrDefault(unit) == MetricEvidence.Experimental)
                return MetricCalculator.ChartValue(sf, definition);
        }
        return null;
    }

    /// <summary>
    /// Compares <paramref name="definition"/> across two lineage nodes by delegating to <see cref="HistoryChanges.Compare"/>
    /// (#307), preserving every continuity, carried-checkpoint, gap, decrease, zero-base and unit rule without duplication.
    /// </summary>
    public HistoryChange CompareMetric(MetricDefinition definition, int indexA, int indexB)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var relation = CompareNodes(indexA, indexB);
        var earlier = relation.Earlier;
        var later = relation.Later;

        // A carried checkpoint save holds its parent checkpoint's own reading at CapturedTick, not a new checkpoint measurement.
        var earlierTick = definition.Source == MetricObservationSource.Checkpoint && earlier.CapturedTick is { } ect
            ? ect : earlier.SaveTick;
        var laterTick = definition.Source == MetricObservationSource.Checkpoint && later.CapturedTick is { } lct
            ? lct : later.SaveTick;
        int? laterSavedTick = definition.Source == MetricObservationSource.Checkpoint
            ? (later.Kind == SaveLineageNodeKind.CarriedCheckpointSave && later.SaveTick > laterTick
                ? later.SaveTick : later.CarriedThroughSaveTick)
            : null;

        var provenContinuation = relation.Relation is SaveLineageRelation.DirectParentChild
            or SaveLineageRelation.ProvenLineage
            or SaveLineageRelation.SameCheckpointCarried;

        var firstPoint = new MetricHistoryPoint(earlierTick, MetricAt(relation.EarlierIndex, definition));
        var secondPoint = new MetricHistoryPoint(laterTick, MetricAt(relation.LaterIndex, definition),
            ConnectToPrevious: provenContinuation && laterTick > earlierTick)
        {
            Unlinked = !provenContinuation,
            SavedTick = laterSavedTick,
        };
        var kind = definition.Source == MetricObservationSource.Checkpoint
            ? MetricHistoryKind.CheckpointLineage : MetricHistoryKind.SaveObservations;
        var history = new MetricHistory(kind, [firstPoint, secondPoint]);
        return HistoryChanges.Compare(definition, history, 0, 1);
    }

    private HashSet<int> AncestorChain(int index)
    {
        var set = new HashSet<int>();
        for (var cur = Nodes[index]; ; cur = Nodes[cur.ParentIndex])
        {
            if (cur.Kind == SaveLineageNodeKind.Checkpoint)
                set.Add(cur.Index);
            if (cur.ParentIndex < 0)
                break;
        }
        return set;
    }

    // ----- internal construction helpers -----

    private sealed class LogicalSave
    {
        public required string GroupKey { get; init; }
        public required int Tick { get; init; }
        public DateTime? ObservedUtc { get; set; }
        public string? CampaignId { get; set; }
        public string? GameVersion { get; set; }
        public string? ModSetHash { get; set; }
        public IReadOnlyList<string> ActiveDlcIds { get; set; } = [];
        public ObservedValues? BasicValues { get; set; }
        public MetricObservation? SaveFileObs { get; set; }
        public List<SaveLineageFileRef> Files { get; } = [];
        public IEnumerable<MetricFileAlias> Aliases => Files.Select(f => new MetricFileAlias
        {
            FileName = f.FileName,
            FileSize = f.FileSize,
            LastWriteUtc = f.LastWriteUtc,
        });
    }

    private sealed class NodeBuilder
    {
        public required SaveLineageNodeKind Kind { get; init; }
        public required SaveLineageParentState ParentState { get; set; }
        public NodeBuilder? ParentBuilder { get; set; }
        public List<NodeBuilder> Children { get; } = [];
        public required int SaveTick { get; init; }
        public int? CapturedTick { get; init; }
        public int? CarriedThroughSaveTick { get; set; }
        public DateTime? ObservedUtc { get; set; }
        public string? CampaignId { get; set; }
        public string? CheckpointId { get; init; }
        public string? RawParentCheckpointId { get; init; }
        public List<SaveLineageFileRef> Files { get; init; } = [];
        public List<SaveLineageFileRef> CarriedByFiles { get; } = [];
        public bool HasLiveSnapshotOnly { get; set; }
        public string? GameVersion { get; set; }
        public string? ModSetHash { get; set; }
        public IReadOnlyList<string> ActiveDlcIds { get; set; } = [];
        public bool IsRepresentative { get; init; }
        public bool OnRepresentativeLine { get; set; }
        public bool IsBranchPoint { get; set; }
        public bool IsAlternateBranch { get; set; }
        public bool HasRewindEvidence { get; set; }
        public bool SameTickParent { get; set; }
        public bool GameVersionChangedFromParent { get; set; }
        public bool ModSetChangedFromParent { get; set; }
        public bool DlcChangedFromParent { get; set; }
        public SaveLineageArchiveState ArchiveState { get; init; }
        public HistoricalCounterInterval? VerifiedParentInterval { get; set; }
        public ObservedValues? BasicValues { get; set; }
        public MetricObservation? SaveFileObs { get; set; }
        public MetricObservation? SnapshotObs { get; set; }
        public MetricObservation? CheckpointObs { get; init; }
        public IReadOnlyDictionary<string, NumericValue>? CompactedMetrics { get; set; }
        public int SameTickDepth { get; set; }
        public int AssignedIndex { get; set; } = -1;
        public int Lane { get; set; }
    }

    private static List<LogicalSave> BuildLogicalSaves(
        SaveObservation[] v1Saves,
        MetricObservation[] metricObs)
    {
        var byGroup = new Dictionary<string, LogicalSave>(Ord);
        var aliasToGroup = new Dictionary<(string FileName, long FileSize, DateTime LastWriteUtc), string>();

        foreach (var sf in metricObs.Where(o => o.Source == MetricObservationSource.SaveFile && o.SaveTick >= 0))
        {
            var groupKey = "sf:" + (sf.Key.Length > 0 ? sf.Key : Guid.NewGuid().ToString("N")) + "@" + sf.SaveTick;
            var save = new LogicalSave
            {
                GroupKey = groupKey,
                Tick = sf.SaveTick,
                ObservedUtc = ValidUtc(sf.ObservedUtc),
                CampaignId = sf.Identity?.ModCampaignId,
                GameVersion = sf.GameVersion,
                ModSetHash = sf.ModSetHash,
                ActiveDlcIds = sf.ActiveDlcIds?.ToArray() ?? [],
                SaveFileObs = sf,
            };
            foreach (var alias in sf.FileAliases)
            {
                AddUniqueFile(save.Files, new SaveLineageFileRef(alias.FileName, alias.FileSize, alias.LastWriteUtc));
                aliasToGroup[(alias.FileName, alias.FileSize, alias.LastWriteUtc)] = groupKey;
            }
            byGroup[groupKey] = save;
        }

        foreach (var v1 in v1Saves)
        {
            var aliasKey = (v1.FileName, v1.FileSize, v1.LastWriteUtc);
            if (!aliasToGroup.TryGetValue(aliasKey, out var groupKey))
            {
                groupKey = v1.ContentHash is { Length: > 0 } hash
                    ? $"hash:{hash}@{v1.Values.TicksGame}:{v1.Identity.ModCampaignId}"
                    : $"file:{v1.FileName}:{v1.FileSize}:{v1.LastWriteUtc.Ticks}";
            }

            if (!byGroup.TryGetValue(groupKey, out var save))
            {
                save = new LogicalSave
                {
                    GroupKey = groupKey,
                    Tick = v1.Values.TicksGame,
                    ObservedUtc = ValidUtc(v1.LastWriteUtc),
                    CampaignId = v1.Identity.ModCampaignId,
                    GameVersion = v1.GameVersion,
                    BasicValues = v1.Values,
                };
                byGroup[groupKey] = save;
            }
            else
            {
                save.BasicValues ??= v1.Values;
                save.CampaignId ??= v1.Identity.ModCampaignId;
                save.GameVersion ??= v1.GameVersion;
                if (ValidUtc(v1.LastWriteUtc) is { } utc && (save.ObservedUtc is null || utc < save.ObservedUtc))
                    save.ObservedUtc = utc;
            }

            UpsertFile(save.Files, new SaveLineageFileRef(v1.FileName, v1.FileSize, v1.LastWriteUtc, v1.Superseded));
        }

        return byGroup.Values.ToList();
    }

    private static void MergeSaveIntoNode(NodeBuilder node, LogicalSave save)
    {
        node.BasicValues ??= save.BasicValues;
        node.SaveFileObs ??= save.SaveFileObs;
        node.GameVersion ??= save.GameVersion;
        node.ModSetHash ??= save.ModSetHash;
        if (node.ActiveDlcIds.Count == 0 && save.ActiveDlcIds.Count > 0)
            node.ActiveDlcIds = save.ActiveDlcIds;
        if (save.ObservedUtc is { } utc && (node.ObservedUtc is null || utc < node.ObservedUtc))
            node.ObservedUtc = utc;
        foreach (var f in save.Files)
            UpsertFile(node.Files, f);
    }

    private static void AttachSnapshot(
        MetricObservation snap,
        IReadOnlyDictionary<string, NumericValue>? compactedMetrics,
        CheckpointAncestry ancestry,
        Dictionary<string, NodeBuilder> validCheckpoints,
        List<NodeBuilder> builders,
        Dictionary<(NodeBuilder Origin, int Tick), NodeBuilder> carriedByParentTick,
        Dictionary<int, List<NodeBuilder>> unprovenByTick)
    {
        var anchor = ancestry.OfSnapshot(snap);
        if (anchor is { Exact: true } exact && validCheckpoints.TryGetValue(exact.Key, out var cpNode))
        {
            if (snap.Metrics.Count > 0)
                cpNode.SnapshotObs ??= snap;
            else if (compactedMetrics is not null)
                cpNode.CompactedMetrics ??= compactedMetrics;
            return;
        }

        if (anchor is { Exact: false } carried && validCheckpoints.TryGetValue(carried.Key, out var originCp))
        {
            originCp.CarriedThroughSaveTick = Math.Max(originCp.CarriedThroughSaveTick ?? int.MinValue, snap.SaveTick);
            if (carriedByParentTick.TryGetValue((originCp, snap.SaveTick), out var existingCarried))
            {
                if (snap.Metrics.Count > 0)
                    existingCarried.SnapshotObs ??= snap;
                else if (compactedMetrics is not null)
                    existingCarried.CompactedMetrics ??= compactedMetrics;
                return;
            }

            var createdCarried = new NodeBuilder
            {
                Kind = SaveLineageNodeKind.CarriedCheckpointSave,
                ParentState = SaveLineageParentState.ProvenParent,
                ParentBuilder = originCp,
                SaveTick = snap.SaveTick,
                CapturedTick = originCp.SaveTick,
                ObservedUtc = ValidUtc(snap.ObservedUtc),
                CampaignId = snap.Identity?.ModCampaignId ?? originCp.CampaignId,
                CheckpointId = originCp.CheckpointId,
                HasLiveSnapshotOnly = true,
                GameVersion = snap.GameVersion ?? originCp.GameVersion,
                ModSetHash = snap.ModSetHash ?? originCp.ModSetHash,
                ActiveDlcIds = snap.ActiveDlcIds?.ToArray() ?? originCp.ActiveDlcIds,
                SnapshotObs = snap.Metrics.Count > 0 ? snap : null,
                CompactedMetrics = compactedMetrics,
                CheckpointObs = originCp.CheckpointObs,
            };
            builders.Add(createdCarried);
            carriedByParentTick[(originCp, snap.SaveTick)] = createdCarried;
            return;
        }

        var snapUtc = ValidUtc(snap.ObservedUtc);
        var snapUtcNorm = UtcTime.ToUtc(snap.ObservedUtc);
        NodeBuilder? existingUnproven = null;
        if (unprovenByTick.TryGetValue(snap.SaveTick, out var candidates))
        {
            for (var i = 0; i < candidates.Count; i++)
            {
                var b = candidates[i];
                if ((b.CampaignId == snap.Identity?.ModCampaignId || b.CampaignId is null || snap.Identity?.ModCampaignId is null)
                    && (snapUtc is null || b.ObservedUtc == snapUtc
                        || b.Files.Any(f => UtcTime.ToUtc(f.LastWriteUtc) == snapUtcNorm)))
                {
                    existingUnproven = b;
                    break;
                }
            }
        }
        if (existingUnproven is not null)
        {
            if (snap.Metrics.Count > 0)
                existingUnproven.SnapshotObs ??= snap;
            else if (compactedMetrics is not null)
                existingUnproven.CompactedMetrics ??= compactedMetrics;
            return;
        }

        var createdUnproven = new NodeBuilder
        {
            Kind = SaveLineageNodeKind.UnprovenSave,
            ParentState = SaveLineageParentState.NoCheckpoint,
            SaveTick = snap.SaveTick,
            ObservedUtc = snapUtc,
            CampaignId = snap.Identity?.ModCampaignId,
            HasLiveSnapshotOnly = true,
            GameVersion = snap.GameVersion,
            ModSetHash = snap.ModSetHash,
            ActiveDlcIds = snap.ActiveDlcIds?.ToArray() ?? [],
            SnapshotObs = snap.Metrics.Count > 0 ? snap : null,
            CompactedMetrics = compactedMetrics,
        };
        builders.Add(createdUnproven);
        if (!unprovenByTick.TryGetValue(snap.SaveTick, out var list))
            unprovenByTick[snap.SaveTick] = list = new List<NodeBuilder>();
        list.Add(createdUnproven);
    }

    private static Dictionary<string, NumericValue>? ExtractCompactedMetrics(SnapshotChartSeries series, int rowIndex)
    {
        if (series.Metrics is null || series.Rows is null)
            return null;
        var dict = new Dictionary<string, NumericValue>(Ord);
        foreach (var (storedKey, metric) in series.Metrics)
        {
            if (metric is null || !metric.Fits(series.Rows.Count) || MetricKeys.Revision(storedKey) != metric.Revision)
                continue;
            if (metric.Values[rowIndex] is { } n && double.IsFinite(n))
            {
                var cov = metric.Coverage?[rowIndex] ?? MetricCoverage.SnapshotOnly;
                if (cov is not (MetricCoverage.Unknown or MetricCoverage.NotApplicable))
                    dict[storedKey] = new NumericValue { Number = n, Coverage = cov };
            }
        }
        return dict.Count > 0 ? dict : null;
    }

    private static Dictionary<MetricObservation, SaveLineageArchiveState> RestoreFromArchive(
        MetricObservation[] observations,
        ICheckpointArchive? archive)
    {
        var states = new Dictionary<MetricObservation, SaveLineageArchiveState>();
        var archivedByCampaign = observations
            .Where(o => o.Source == MetricObservationSource.Checkpoint && o.ArchivedMetrics && !o.Conflicted)
            .GroupBy(o => o.Identity?.ModCampaignId ?? "", Ord);

        foreach (var group in archivedByCampaign)
        {
            if (archive is null || group.Key.Length == 0)
            {
                foreach (var obs in group)
                    states[obs] = SaveLineageArchiveState.ArchivedUnavailable;
                continue;
            }

            IReadOnlyList<MetricObservation> records;
            try
            {
                records = archive.ReadMany(group.Key, group.Select(o => o.Key).ToHashSet(Ord));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                foreach (var obs in group)
                    states[obs] = SaveLineageArchiveState.ArchivedUnavailable;
                continue;
            }

            var byKey = records.ToLookup(r => r.Key, Ord);
            foreach (var target in group)
            {
                var restored = false;
                foreach (var candidate in byKey[target.Key])
                {
                    if (candidate.Source != MetricObservationSource.Checkpoint || candidate.IsQuarantined
                        || candidate.Metrics is not { Count: > 0 }
                        || candidate.Identity?.ModCampaignId != group.Key
                        || candidate.PayloadHash != target.PayloadHash
                        || candidate.ParentKey != target.ParentKey
                        || candidate.SaveTick != target.SaveTick)
                        continue;
                    ProfileMutations.RestoreArchivedMetrics(target, candidate);
                    restored = true;
                }
                if (restored)
                    target.ArchivedMetrics = false;
                states[target] = restored ? SaveLineageArchiveState.RestoredFromArchive : SaveLineageArchiveState.ArchivedUnavailable;
            }
        }

        foreach (var obs in observations.Where(o => o.Source == MetricObservationSource.Checkpoint && !states.ContainsKey(o)))
            states[obs] = obs.Conflicted ? SaveLineageArchiveState.None : SaveLineageArchiveState.Inline;

        return states;
    }

    private static MetricObservation CloneForRead(MetricObservation src) => new()
    {
        Source = src.Source,
        FormatVersion = src.FormatVersion,
        Key = src.Key,
        Identity = src.Identity.Clone(),
        FileAliases = src.FileAliases.Select(a => new MetricFileAlias
        {
            FileName = a.FileName,
            FileSize = a.FileSize,
            LastWriteUtc = a.LastWriteUtc,
        }).ToList(),
        GameVersion = src.GameVersion,
        SaveTick = src.SaveTick,
        ObservedUtc = src.ObservedUtc,
        ActiveDlcIds = src.ActiveDlcIds?.ToList(),
        ModSetHash = src.ModSetHash,
        CheckpointMetadata = src.CheckpointMetadata,
        EvidenceByAcquisition = src.EvidenceByAcquisition is null ? null : new(src.EvidenceByAcquisition, Ord),
        CompactedEvidenceByMetric = src.CompactedEvidenceByMetric is null ? null : new(src.CompactedEvidenceByMetric, Ord),
        ProviderCaptures = src.ProviderCaptures is null ? null : new(src.ProviderCaptures, Ord),
        Metrics = new Dictionary<string, MetricEntry>(src.Metrics, Ord),
        CompactedSampleCounts = src.CompactedSampleCounts is null ? null : new(src.CompactedSampleCounts, Ord),
        CompactedObservationCount = src.CompactedObservationCount,
        CompactedChart = src.CompactedChart,
        ParentKey = src.ParentKey,
        CheckpointKey = src.CheckpointKey,
        PayloadHash = src.PayloadHash,
        ContinuousFamilies = src.ContinuousFamilies?.ToList(),
        Conflicted = src.Conflicted,
        Compressed = src.Compressed,
        ArchivedMetrics = src.ArchivedMetrics,
    };

    private static void BreakCycles(IEnumerable<NodeBuilder> nodes)
    {
        var visited = new HashSet<NodeBuilder>();
        var onStack = new HashSet<NodeBuilder>();
        foreach (var start in nodes)
        {
            onStack.Clear();
            for (var cur = start; cur is not null && !visited.Contains(cur); cur = cur.ParentBuilder)
            {
                if (!onStack.Add(cur))
                {
                    cur.ParentBuilder = null;
                    cur.ParentState = SaveLineageParentState.InvalidOrConflictedParent;
                    break;
                }
            }
            visited.UnionWith(onStack);
        }
    }

    private static void MarkRewindEvidence(NodeBuilder[] children)
    {
        // A branch child has rewind evidence when another sibling subtree already reached a strictly later game tick at an
        // earlier wall-clock save timestamp, and this child was saved afterwards at an earlier game tick from the shared parent.
        var entries = new List<(int Branch, int Tick, DateTime Utc)>();
        var stack = new Stack<NodeBuilder>();
        for (var branch = 0; branch < children.Length; branch++)
        {
            stack.Clear();
            stack.Push(children[branch]);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                if (cur.ObservedUtc is { } utc)
                    entries.Add((branch, cur.SaveTick, UtcTime.ToUtc(utc)));
                foreach (var sub in cur.Children)
                    stack.Push(sub);
            }
        }
        if (entries.Count == 0)
            return;

        entries.Sort(static (a, b) => b.Tick.CompareTo(a.Tick));
        var bestBranch = -1;
        var bestUtc = DateTime.MaxValue;
        var secondUtc = DateTime.MaxValue;
        var minOtherUtcByBranch = new DateTime[children.Length];
        Array.Fill(minOtherUtcByBranch, DateTime.MaxValue);

        var idx = 0;
        while (idx < entries.Count)
        {
            var tick = entries[idx].Tick;
            var groupEnd = idx;
            while (groupEnd < entries.Count && entries[groupEnd].Tick == tick)
                groupEnd++;

            // Query using strictly greater ticks processed before this group.
            for (var b = 0; b < children.Length; b++)
            {
                if (children[b].SaveTick == tick)
                    minOtherUtcByBranch[b] = b != bestBranch ? bestUtc : secondUtc;
            }

            // Commit this tick group into top-2 earliest timestamps across distinct branches.
            for (var k = idx; k < groupEnd; k++)
            {
                var (branch, _, utc) = entries[k];
                if (branch == bestBranch)
                {
                    if (utc < bestUtc)
                        bestUtc = utc;
                }
                else if (utc < bestUtc)
                {
                    secondUtc = bestUtc;
                    bestBranch = branch;
                    bestUtc = utc;
                }
                else if (utc < secondUtc)
                {
                    secondUtc = utc;
                }
            }
            idx = groupEnd;
        }

        for (var i = 0; i < children.Length; i++)
        {
            if (children[i].ObservedUtc is { } childUtc && minOtherUtcByBranch[i] < UtcTime.ToUtc(childUtc))
                children[i].HasRewindEvidence = true;
        }
    }

    private static void ComputeDepthWithinTick(List<NodeBuilder> builders)
    {
        int Depth(NodeBuilder b, HashSet<NodeBuilder> seen)
        {
            if (b.ParentBuilder is not { } p || p.SaveTick != b.SaveTick || !seen.Add(b))
                return 0;
            return 1 + Depth(p, seen);
        }
        var seen = new HashSet<NodeBuilder>();
        foreach (var b in builders)
        {
            seen.Clear();
            b.SameTickDepth = Depth(b, seen);
        }
    }

    private static int AssignLanes(NodeBuilder[] ordered, bool hasRepresentativeLine)
    {
        var laneEndRow = new int[MaxVisualLanes];
        Array.Fill(laneEndRow, -1);
        var reservedLane = new Dictionary<NodeBuilder, int>();
        var firstGeneralLane = hasRepresentativeLine ? 1 : 0;
        var maxUsedLane = 0;

        for (var i = 0; i < ordered.Length; i++)
        {
            var node = ordered[i];
            int lane;
            if (node.OnRepresentativeLine)
            {
                lane = 0;
            }
            else if (reservedLane.TryGetValue(node, out var inherited))
            {
                lane = inherited;
            }
            else
            {
                var busyFrom = node.ParentBuilder is { AssignedIndex: >= 0 } p ? p.AssignedIndex : i;
                lane = -1;
                for (var candidate = firstGeneralLane; candidate < MaxVisualLanes; candidate++)
                {
                    if (laneEndRow[candidate] < busyFrom)
                    {
                        lane = candidate;
                        break;
                    }
                }
                if (lane < 0)
                    lane = MaxVisualLanes - 1;
            }

            node.Lane = lane;
            if (lane > maxUsedLane)
                maxUsedLane = lane;

            var nonRepChildren = node.Children
                .Where(c => !c.OnRepresentativeLine && c.AssignedIndex > i)
                .OrderBy(c => c.Kind == SaveLineageNodeKind.CarriedCheckpointSave ? 1 : 0)
                .ThenByDescending(c => c.AssignedIndex)
                .ToArray();

            if (!node.OnRepresentativeLine && nonRepChildren.Length > 0)
            {
                // Keep the longest continuing child on this branch's own lane; other children take new branch lanes.
                var primary = nonRepChildren[0];
                reservedLane[primary] = lane;
                laneEndRow[lane] = Math.Max(laneEndRow[lane], primary.AssignedIndex);
            }
            else if (!node.OnRepresentativeLine)
            {
                laneEndRow[lane] = Math.Max(laneEndRow[lane], i);
            }
        }

        return maxUsedLane;
    }

    private static int KindOrder(SaveLineageNodeKind kind) => kind switch
    {
        SaveLineageNodeKind.Checkpoint => 0,
        SaveLineageNodeKind.CarriedCheckpointSave => 1,
        SaveLineageNodeKind.UnprovenSave => 2,
        _ => 3,
    };

    private static string FirstFileName(NodeBuilder b) => b.Files.FirstOrDefault()?.FileName ?? "";

    private static DateTime? ValidUtc(DateTime utc) => UtcTime.ValidUtc(utc);

    private static bool ValidHash(string? s)
        => s is { Length: 64 } && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void AddUniqueFile(List<SaveLineageFileRef> list, SaveLineageFileRef item)
    {
        if (!list.Any(f => f.FileName == item.FileName && f.FileSize == item.FileSize && f.LastWriteUtc == item.LastWriteUtc))
            list.Add(item);
    }

    private static void UpsertFile(List<SaveLineageFileRef> list, SaveLineageFileRef item)
    {
        for (var i = 0; i < list.Count; i++)
        {
            var existing = list[i];
            if (existing.FileName == item.FileName && existing.FileSize == item.FileSize && existing.LastWriteUtc == item.LastWriteUtc)
            {
                if (item.Superseded && !existing.Superseded)
                    list[i] = item;
                return;
            }
        }
        list.Add(item);
    }
}
