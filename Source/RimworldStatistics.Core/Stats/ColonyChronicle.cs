namespace RimworldStatistics.Core;

/// <summary>Top-level category of a dated entry in a colony's evidence-based chronicle (V2-A).</summary>
public enum ChronicleEntryKind
{
    /// <summary>A confirmed game ending at a known game tick.</summary>
    Ending,

    /// <summary>A first-event or threshold milestone at an exact tick or 0.5-day history sample.</summary>
    Milestone,

    /// <summary>A completed research project at its exact recorded completion tick on the selected branch.</summary>
    ResearchCompleted,

    /// <summary>A verified multi-tick interval <c>[StartTick, EndTick]</c> between two proven checkpoints.</summary>
    VerifiedInterval,

    /// <summary>An explicit recording gap, missing parent, mid-campaign recording start, cap overflow or environment shift.</summary>
    RecordingGap,
}

/// <summary>How precisely an entry's game time is proven.</summary>
public enum ChronicleTimingKind
{
    /// <summary>Exact in-game tick recorded live or from a start-of-game live accumulator.</summary>
    ExactTick,

    /// <summary>Sampled in-game day from RimWorld's 0.5-day <c>HistoryAutoRecorder</c> curve in a save file.</summary>
    HistorySampleDay,

    /// <summary>Bounded interval <c>[StartTick, EndTick]</c> across a proven parent-to-child save chain; never a single day.</summary>
    ProvenInterval,
}

/// <summary>Where the evidence for a chronicle entry or undated trace came from.</summary>
public enum ChronicleEvidenceSource
{
    /// <summary>Recorded live at the moment the event happened (<see cref="EndingSource.Live"/>).</summary>
    LiveEvent,

    /// <summary>Exact value or per-project completion tick stored in an embedded checkpoint on the selected branch.</summary>
    CheckpointAggregate,

    /// <summary>Verified vanilla cumulative counter difference across a strict parent-to-child checkpoint save pair.</summary>
    VanillaCounterInterval,

    /// <summary>RimWorld's 0.5-day <c>HistoryAutoRecorder</c> curve inside a save file on the selected branch.</summary>
    SaveHistoryGraph,

    /// <summary>Recovered from a save's <c>lineageEndings</c> list when no live ending record exists (dev mode unknown).</summary>
    RecoveredLineage,

    /// <summary>Checkpoint lineage, archive or save-header metadata.</summary>
    SaveMetadata,

    /// <summary>Pre-install ending trace seeded without an in-game day.</summary>
    SeededTrace,

    /// <summary>Unexplained ending trace detected in a save file without an in-game day.</summary>
    SaveTrace,
}

/// <summary>Milestone subtype for <see cref="ChronicleEntryKind.Milestone"/>.</summary>
public enum ChronicleMilestoneKind
{
    FirstColonistJoined,
    FirstEnemyRaid,
    FirstColonistDeath,
    FirstResearchCompleted,
    WealthFirst100K,
    WealthFirst1M,
    ColonistsFirst10,
    WealthPeakDay,
}

/// <summary>Gap or discontinuity subtype for <see cref="ChronicleEntryKind.RecordingGap"/>.</summary>
public enum ChronicleGapKind
{
    MissingParentCheckpoint,
    UnprovenSaveWithoutCheckpoint,
    CarriedCheckpointUnrecordedSpan,
    LiveRecordingStartedLateOrReset,
    ArchivedMetricsUnavailable,
    ResearchKeyCapReached,
    EnvironmentChanged,
}

/// <summary>One selectable branch or terminal save target in <see cref="ColonyChronicle"/>.</summary>
public sealed record ChronicleBranchOption(
    int LeafNodeIndex,
    int SaveTick,
    bool IsRepresentative,
    bool IsAlternateBranch,
    bool HasRewindEvidence,
    SaveLineageNodeKind Kind,
    string? CheckpointId,
    string? PrimaryFileName);

/// <summary>One proven metric change inside a <see cref="ChronicleEntryKind.VerifiedInterval"/>.</summary>
public sealed record ChronicleIntervalFact(
    string MetricKey,
    double Delta,
    bool IsSignedDifference,
    MetricCoverage Coverage,
    ChronicleEvidenceSource EvidenceSource);

/// <summary>One dated or bounded-interval entry in a colony's chronicle.</summary>
public sealed record ChronicleEntry
{
    public required string StableKey { get; init; }
    public required ChronicleEntryKind Kind { get; init; }
    public required ChronicleTimingKind TimingKind { get; init; }
    public required int StartTick { get; init; }
    public required int EndTick { get; init; }
    public required ChronicleEvidenceSource EvidenceSource { get; init; }
    public MetricCoverage Coverage { get; init; } = MetricCoverage.Complete;
    public string? RelatedMetricKey { get; init; }

    public EndingType? EndingType { get; init; }
    public int? EndingOrdinal { get; init; }
    public int? DepartedCount { get; init; }
    public int? PartyAtEnding { get; init; }
    public int? ColonistsKilledAtEnding { get; init; }
    public bool? DevMode { get; init; }
    public DateTime? RealTimeUtc { get; init; }

    public ChronicleMilestoneKind? MilestoneKind { get; init; }
    public double? MilestoneValue { get; init; }

    public string? ResearchDefKey { get; init; }

    public IReadOnlyList<ChronicleIntervalFact> IntervalFacts { get; init; } = [];

    public ChronicleGapKind? GapKind { get; init; }
    public string? ParentSaveName { get; init; }
    public string? ChildSaveName { get; init; }
    public bool VersionChanged { get; init; }
    public bool ModSetChanged { get; init; }
    public bool DlcChanged { get; init; }
}

/// <summary>An ending trace whose in-game day is unknown; never assigned a fabricated day on the dated timeline.</summary>
public sealed record ChronicleUndatedTrace(
    string StableKey,
    EndingType EndingType,
    ChronicleEvidenceSource EvidenceSource,
    bool? DevMode = null,
    DateTime? RealTimeUtc = null);

/// <summary>
/// Read-only, single-branch evidence-based chronicle of one colony (V2-A).
/// Only events with proven ticks, 0.5-day history-graph thresholds, or verified checkpoint-to-checkpoint intervals on the
/// selected branch are placed on the timeline; sibling branches are never mixed, and undated traces remain separate.
/// </summary>
public sealed class ColonyChronicle
{
    public const int DefaultMaxEntries = 512;

    private static readonly StringComparer Ord = StringComparer.Ordinal;

    private static readonly string[] IntervalCounterMetricKeys =
    [
        MembershipFamily.FirstJoin,
        MembershipFamily.PrisonerRecruit,
        MembershipFamily.HumanBirths,
        HealthDeathFamily.DeathsByRaid,
        RaidFamily.ByArrivalMode,
        HealthDeathFamily.HostileKills,
        MetricKeys.QuestSuccess,
        TradeFamily.Deals,
        CaravanFamily.SitesCleared,
    ];

    public required string ColonyKey { get; init; }
    public required int SelectedLeafNodeIndex { get; init; }
    public required IReadOnlyList<ChronicleBranchOption> Branches { get; init; }
    public required IReadOnlyList<int> BranchNodeChain { get; init; }
    public required IReadOnlyList<ChronicleEntry> Entries { get; init; }
    public required IReadOnlyList<ChronicleUndatedTrace> UndatedTraces { get; init; }
    public int OtherBranchEndingCount { get; init; }
    public int AmbiguousBranchEndingCount { get; init; }
    public int OmittedEntryCount { get; init; }

    public static ColonyChronicle? Build(
        StatsView view,
        string colonyKey,
        ColonySaveLineage lineage,
        int? selectedLeafNodeIndex = null,
        bool includeDevMode = false,
        int maxEntries = DefaultMaxEntries)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(colonyKey);
        ArgumentNullException.ThrowIfNull(lineage);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);

        var colony = view.Colonies.FirstOrDefault(c => c.Key == colonyKey);
        var stats = view.Stats.Colonies.FirstOrDefault(s => s.Key == colonyKey);
        if (colony is null || stats is null)
            return null;

        var branches = BuildBranchOptions(lineage);
        var selectedIdx = ResolveSelectedLeafIndex(lineage, branches, selectedLeafNodeIndex);
        var chain = BuildProvenBranchChain(lineage, selectedIdx);
        var chainIndexSet = chain.Select(n => n.Index).ToHashSet();

        var entriesByKey = new Dictionary<string, ChronicleEntry>(Ord);
        void AddEntry(ChronicleEntry entry) => entriesByKey.TryAdd(entry.StableKey, entry);

        // Map save observations onto lineage nodes so save-embedded lineageEndings identify branch endings precisely.
        var saveObsByNode = MapSaveObservationsToNodes(colony, lineage);

        // 1. Recording gaps, missing parents, unproven saves, late starts, and environment changes along the branch.
        CollectBranchStructureGaps(chain, AddEntry);

        // 2. Endings & undated traces (strictly isolated to the selected branch).
        var undatedTraces = new List<ChronicleUndatedTrace>();
        var (otherBranchEndings, ambiguousEndings) = CollectEndingsAndTraces(
            colony, lineage, chain, chainIndexSet, saveObsByNode, includeDevMode, AddEntry, undatedTraces);

        // 3. Exact-tick milestones and per-project research completions from the selected branch's checkpoint chain.
        CollectCheckpointMilestonesAndResearch(chain, AddEntry);

        // 4. 0.5-day history-graph threshold milestones from the selected branch's latest save-file observation.
        CollectSaveHistoryMilestones(view, lineage, chain, AddEntry);

        // 5. Proven checkpoint-to-checkpoint interval summaries (never single-day events).
        CollectVerifiedIntervals(view, lineage, chain, AddEntry);

        var sorted = entriesByKey.Values
            .OrderBy(e => e.EndTick)
            .ThenBy(e => e.StartTick)
            .ThenBy(e => KindSortOrder(e.Kind))
            .ThenBy(e => e.StableKey, Ord)
            .ToList();

        var (bounded, omitted) = ApplyEntryCap(sorted, maxEntries);

        var sortedUndated = undatedTraces
            .DistinctBy(t => t.StableKey, Ord)
            .OrderBy(t => t.EndingType)
            .ThenBy(t => t.EvidenceSource)
            .ThenBy(t => t.StableKey, Ord)
            .ToList();

        return new ColonyChronicle
        {
            ColonyKey = colonyKey,
            SelectedLeafNodeIndex = selectedIdx,
            Branches = branches,
            BranchNodeChain = chain.Select(n => n.Index).ToArray(),
            Entries = bounded,
            UndatedTraces = sortedUndated,
            OtherBranchEndingCount = otherBranchEndings,
            AmbiguousBranchEndingCount = ambiguousEndings,
            OmittedEntryCount = omitted,
        };
    }

    private static MetricDefinition? ResolveDefinition(StatsView view, string key)
        => BasicMetrics.Contains(key) ? BasicMetrics.Definition(key) : MetricLookup.Definition(view, key);

    private static List<ChronicleBranchOption> BuildBranchOptions(ColonySaveLineage lineage)
    {
        if (lineage.Nodes.Count == 0)
            return [];

        var candidateIndices = new HashSet<int>();
        if (lineage.RepresentativeIndex >= 0 && lineage.RepresentativeIndex < lineage.Nodes.Count)
            candidateIndices.Add(lineage.RepresentativeIndex);

        var hasAnyCheckpoint = lineage.Nodes.Any(n => n.Kind == SaveLineageNodeKind.Checkpoint);
        foreach (var node in lineage.Nodes)
        {
            if (node.Kind == SaveLineageNodeKind.Checkpoint)
            {
                var hasCheckpointChild = node.ChildIndices.Any(ci => lineage.Nodes[ci].Kind == SaveLineageNodeKind.Checkpoint);
                if (!hasCheckpointChild)
                    candidateIndices.Add(node.Index);
            }
            else if (node.Kind == SaveLineageNodeKind.CarriedCheckpointSave && node.ChildIndices.Count == 0)
            {
                // Surface a carried-checkpoint leaf when its parent also forked into another checkpoint or when it extends past its parent.
                if (node.ParentIndex >= 0 && node.SaveTick > lineage.Nodes[node.ParentIndex].SaveTick)
                    candidateIndices.Add(node.Index);
            }
            else if (!hasAnyCheckpoint && node.Kind == SaveLineageNodeKind.UnprovenSave)
            {
                candidateIndices.Add(node.Index);
            }
        }

        if (candidateIndices.Count == 0)
            candidateIndices.Add(lineage.Nodes[^1].Index);

        return candidateIndices
            .Select(i => lineage.Nodes[i])
            .OrderByDescending(n => n.IsRepresentative)
            .ThenByDescending(n => n.Kind == SaveLineageNodeKind.Checkpoint)
            .ThenByDescending(n => n.SaveTick)
            .ThenBy(n => n.Index)
            .Select(n => new ChronicleBranchOption(
                LeafNodeIndex: n.Index,
                SaveTick: n.SaveTick,
                IsRepresentative: n.IsRepresentative,
                IsAlternateBranch: n.IsAlternateBranch,
                HasRewindEvidence: SubchainHasRewind(lineage, n.Index),
                Kind: n.Kind,
                CheckpointId: n.CheckpointId,
                PrimaryFileName: PrimaryFileName(n)))
            .ToList();
    }

    private static bool SubchainHasRewind(ColonySaveLineage lineage, int leafIndex)
    {
        for (var cur = lineage.Nodes[leafIndex]; ; cur = lineage.Nodes[cur.ParentIndex])
        {
            if (cur.HasRewindEvidence)
                return true;
            if (cur.ParentIndex < 0 || cur.ParentState != SaveLineageParentState.ProvenParent)
                return false;
        }
    }

    private static int ResolveSelectedLeafIndex(
        ColonySaveLineage lineage,
        IReadOnlyList<ChronicleBranchOption> branches,
        int? requested)
    {
        if (lineage.Nodes.Count == 0)
            return -1;
        if (requested is { } req && req >= 0 && req < lineage.Nodes.Count)
            return req;
        if (lineage.RepresentativeIndex >= 0 && lineage.RepresentativeIndex < lineage.Nodes.Count)
            return lineage.RepresentativeIndex;
        return branches.Count > 0 ? branches[0].LeafNodeIndex : lineage.Nodes[^1].Index;
    }

    private static List<SaveLineageNode> BuildProvenBranchChain(ColonySaveLineage lineage, int selectedIdx)
    {
        if (selectedIdx < 0 || selectedIdx >= lineage.Nodes.Count)
            return [];

        var reversed = new List<SaveLineageNode>();
        var visited = new HashSet<int>();
        for (var cur = lineage.Nodes[selectedIdx]; visited.Add(cur.Index);)
        {
            reversed.Add(cur);
            if (cur.ParentIndex < 0 || cur.ParentState != SaveLineageParentState.ProvenParent)
                break;
            cur = lineage.Nodes[cur.ParentIndex];
        }
        reversed.Reverse();
        return reversed;
    }

    private static Dictionary<int, List<SaveObservation>> MapSaveObservationsToNodes(Colony colony, ColonySaveLineage lineage)
    {
        var byNode = new Dictionary<int, List<SaveObservation>>();
        if (colony.Observations.Count == 0 || lineage.Nodes.Count == 0)
            return byNode;

        var obsLookup = colony.Observations
            .Where(o => o.Status == ObservationStatus.Ok)
            .ToLookup(o => (o.FileName, o.FileSize, o.LastWriteUtc));

        foreach (var node in lineage.Nodes)
        {
            List<SaveObservation>? list = null;
            foreach (var file in node.Files.Concat(node.CarriedByFiles))
            {
                foreach (var match in obsLookup[(file.FileName, file.FileSize, file.LastWriteUtc)])
                {
                    list ??= [];
                    if (!list.Contains(match))
                        list.Add(match);
                }
            }
            if (list is not null)
                byNode[node.Index] = list;
        }
        return byNode;
    }

    private static void CollectBranchStructureGaps(List<SaveLineageNode> chain, Action<ChronicleEntry> add)
    {
        if (chain.Count == 0)
            return;

        var root = chain[0];
        if (root.ParentState is SaveLineageParentState.MissingParent or SaveLineageParentState.InvalidOrConflictedParent)
        {
            add(new ChronicleEntry
            {
                StableKey = $"gap:missing_parent:{root.Index}",
                Kind = ChronicleEntryKind.RecordingGap,
                TimingKind = ChronicleTimingKind.ProvenInterval,
                StartTick = 0,
                EndTick = root.SaveTick,
                EvidenceSource = ChronicleEvidenceSource.SaveMetadata,
                Coverage = MetricCoverage.Partial,
                GapKind = ChronicleGapKind.MissingParentCheckpoint,
                ChildSaveName = PrimaryFileName(root),
            });
        }
        else if (root.Kind is SaveLineageNodeKind.UnprovenSave or SaveLineageNodeKind.ConflictedCheckpoint)
        {
            add(new ChronicleEntry
            {
                StableKey = $"gap:unproven:{root.Index}",
                Kind = ChronicleEntryKind.RecordingGap,
                TimingKind = ChronicleTimingKind.ProvenInterval,
                StartTick = 0,
                EndTick = root.SaveTick,
                EvidenceSource = ChronicleEvidenceSource.SaveMetadata,
                Coverage = MetricCoverage.SnapshotOnly,
                GapKind = ChronicleGapKind.UnprovenSaveWithoutCheckpoint,
                ChildSaveName = PrimaryFileName(root),
            });
        }

        int? previousMaxSinceTick = null;
        SaveLineageNode? prevCheckpoint = null;

        foreach (var node in chain)
        {
            if (node.ArchiveState == SaveLineageArchiveState.ArchivedUnavailable)
            {
                add(new ChronicleEntry
                {
                    StableKey = $"gap:archive_unavailable:{node.Index}",
                    Kind = ChronicleEntryKind.RecordingGap,
                    TimingKind = ChronicleTimingKind.ExactTick,
                    StartTick = node.SaveTick,
                    EndTick = node.SaveTick,
                    EvidenceSource = ChronicleEvidenceSource.SaveMetadata,
                    Coverage = MetricCoverage.Partial,
                    GapKind = ChronicleGapKind.ArchivedMetricsUnavailable,
                    ChildSaveName = PrimaryFileName(node),
                });
            }

            if (node.Kind == SaveLineageNodeKind.CarriedCheckpointSave
                && node.CapturedTick is { } capTick
                && node.SaveTick > capTick)
            {
                add(new ChronicleEntry
                {
                    StableKey = $"gap:carried:{node.Index}:{capTick}:{node.SaveTick}",
                    Kind = ChronicleEntryKind.RecordingGap,
                    TimingKind = ChronicleTimingKind.ProvenInterval,
                    StartTick = capTick,
                    EndTick = node.SaveTick,
                    EvidenceSource = ChronicleEvidenceSource.SaveMetadata,
                    Coverage = MetricCoverage.Partial,
                    GapKind = ChronicleGapKind.CarriedCheckpointUnrecordedSpan,
                    ParentSaveName = prevCheckpoint is not null ? PrimaryFileName(prevCheckpoint) : null,
                    ChildSaveName = PrimaryFileName(node),
                });
            }

            if (node.Kind == SaveLineageNodeKind.Checkpoint && node.CheckpointObservation is { Conflicted: false } cp)
            {
                var maxSince = MaxLiveSinceTick(cp);
                if (maxSince is > 0 && (previousMaxSinceTick is null || maxSince.Value > previousMaxSinceTick.Value))
                {
                    var spanStart = prevCheckpoint is not null && maxSince.Value > prevCheckpoint.SaveTick
                        ? prevCheckpoint.SaveTick
                        : 0;
                    add(new ChronicleEntry
                    {
                        StableKey = $"gap:live_since:{maxSince.Value}",
                        Kind = ChronicleEntryKind.RecordingGap,
                        TimingKind = ChronicleTimingKind.ProvenInterval,
                        StartTick = spanStart,
                        EndTick = maxSince.Value,
                        EvidenceSource = ChronicleEvidenceSource.CheckpointAggregate,
                        Coverage = MetricCoverage.Partial,
                        GapKind = ChronicleGapKind.LiveRecordingStartedLateOrReset,
                        ChildSaveName = PrimaryFileName(node),
                    });
                }
                if (maxSince is not null)
                    previousMaxSinceTick = Math.Max(previousMaxSinceTick ?? 0, maxSince.Value);

                if (prevCheckpoint is not null
                    && (node.GameVersionChangedFromParent || node.ModSetChangedFromParent || node.DlcChangedFromParent))
                {
                    add(new ChronicleEntry
                    {
                        StableKey = $"gap:env:{prevCheckpoint.Index}:{node.Index}",
                        Kind = ChronicleEntryKind.RecordingGap,
                        TimingKind = node.SaveTick > prevCheckpoint.SaveTick
                            ? ChronicleTimingKind.ProvenInterval
                            : ChronicleTimingKind.ExactTick,
                        StartTick = prevCheckpoint.SaveTick,
                        EndTick = node.SaveTick,
                        EvidenceSource = ChronicleEvidenceSource.SaveMetadata,
                        Coverage = MetricCoverage.Partial,
                        GapKind = ChronicleGapKind.EnvironmentChanged,
                        ParentSaveName = PrimaryFileName(prevCheckpoint),
                        ChildSaveName = PrimaryFileName(node),
                        VersionChanged = node.GameVersionChangedFromParent,
                        ModSetChanged = node.ModSetChangedFromParent,
                        DlcChanged = node.DlcChangedFromParent,
                    });
                }

                prevCheckpoint = node;
            }
        }
    }

    private static int? MaxLiveSinceTick(MetricObservation cp)
    {
        int? maxSince = null;
        foreach (var (_, entry) in cp.Metrics)
        {
            if (entry.IsQuarantined)
                continue;
            if (entry.Values.TryGetValue(MetricEntry.Scalar, out var scalar) && scalar.SinceTick is { } st)
                maxSince = maxSince is null ? st : Math.Max(maxSince.Value, st);
        }
        return maxSince;
    }

    private static (int OtherBranchCount, int AmbiguousCount) CollectEndingsAndTraces(
        Colony colony,
        ColonySaveLineage lineage,
        List<SaveLineageNode> chain,
        HashSet<int> chainIndexSet,
        Dictionary<int, List<SaveObservation>> saveObsByNode,
        bool includeDevMode,
        Action<ChronicleEntry> addEntry,
        List<ChronicleUndatedTrace> undatedTraces)
    {
        var chainLineageEntries = new HashSet<(EndingType Type, int Ordinal)>();
        var chainLineageList = new List<LineageEntry>();
        var otherBranchLineageEntries = new HashSet<(EndingType Type, int Ordinal)>();

        foreach (var (nodeIdx, obsList) in saveObsByNode)
        {
            var onChain = chainIndexSet.Contains(nodeIdx);
            foreach (var obs in obsList)
            {
                foreach (var le in obs.Lineage)
                {
                    if (onChain)
                    {
                        if (chainLineageEntries.Add((le.Type, le.Ordinal)))
                            chainLineageList.Add(le);
                    }
                    else
                    {
                        otherBranchLineageEntries.Add((le.Type, le.Ordinal));
                    }
                }
            }
        }

        // Determine whether checkpoint forks exist and where this chain first diverges from another checkpoint branch.
        var checkpointRoots = lineage.Nodes.Count(n =>
            n.Kind == SaveLineageNodeKind.Checkpoint && n.ParentState != SaveLineageParentState.ProvenParent);
        var checkpointTerminals = lineage.Nodes.Count(n =>
            n.Kind == SaveLineageNodeKind.Checkpoint
            && !n.ChildIndices.Any(ci => lineage.Nodes[ci].Kind == SaveLineageNodeKind.Checkpoint));
        var hasCheckpointForks = lineage.BranchPointCount > 0 || checkpointRoots > 1 || checkpointTerminals > 1;

        int? firstDivergenceTick = null;
        if (checkpointRoots > 1)
        {
            firstDivergenceTick = -1;
        }
        else if (lineage.BranchPointCount > 0)
        {
            foreach (var node in chain)
            {
                if (node.IsBranchPoint)
                {
                    firstDivergenceTick = node.SaveTick;
                    break;
                }
            }
        }

        var branchCampaignId = chain.Select(n => n.CampaignId).FirstOrDefault(id => !string.IsNullOrEmpty(id));
        var recordedPairs = new HashSet<(EndingType Type, int Ordinal)>();
        var explainedTypes = new HashSet<EndingType>();
        int otherBranchCount = 0;
        int ambiguousCount = 0;

        foreach (var ending in colony.Endings)
        {
            if (!includeDevMode && ending.DevMode)
                continue;

            recordedPairs.Add((ending.Type, ending.Ordinal));
            explainedTypes.Add(ending.Type);

            if (ending.Source == EndingSource.SeededTrace || ending.TicksGame <= 0)
            {
                undatedTraces.Add(new ChronicleUndatedTrace(
                    StableKey: $"trace:ending:{ending.CampaignId}:{ending.Type}:{ending.Ordinal}:{ending.DevMode}",
                    EndingType: ending.Type,
                    EvidenceSource: ending.Source == EndingSource.SeededTrace
                        ? ChronicleEvidenceSource.SeededTrace
                        : ChronicleEvidenceSource.LiveEvent,
                    DevMode: ending.DevMode,
                    RealTimeUtc: ValidUtc(ending.RealTimeUtc)));
                continue;
            }

            var keyPair = (ending.Type, ending.Ordinal);
            var provenOnChain = chainLineageEntries.Contains(keyPair);
            var provenOnOtherOnly = !provenOnChain && otherBranchLineageEntries.Contains(keyPair);

            if (provenOnOtherOnly)
            {
                otherBranchCount++;
                continue;
            }

            if (!provenOnChain)
            {
                if (branchCampaignId is not null
                    && ending.CampaignId.Length > 0
                    && !string.Equals(ending.CampaignId, branchCampaignId, StringComparison.Ordinal))
                {
                    otherBranchCount++;
                    continue;
                }

                if (hasCheckpointForks)
                {
                    // Before the earliest fork on a single-root lineage, the trunk is shared by all branches.
                    var onSharedTrunk = firstDivergenceTick is { } divTick && divTick >= 0 && ending.TicksGame <= divTick;
                    if (!onSharedTrunk)
                    {
                        ambiguousCount++;
                        continue;
                    }
                }
            }

            addEntry(new ChronicleEntry
            {
                StableKey = $"ending:live:{ending.CampaignId}:{ending.Type}:{ending.Ordinal}:{ending.DevMode}",
                Kind = ChronicleEntryKind.Ending,
                TimingKind = ChronicleTimingKind.ExactTick,
                StartTick = ending.TicksGame,
                EndTick = ending.TicksGame,
                EvidenceSource = ChronicleEvidenceSource.LiveEvent,
                Coverage = MetricCoverage.Complete,
                RelatedMetricKey = BasicMetrics.Endings,
                EndingType = ending.Type,
                EndingOrdinal = ending.Ordinal,
                DepartedCount = ending.Departed,
                PartyAtEnding = ending.PartyAtEnding,
                ColonistsKilledAtEnding = ending.ColonistsKilledAtEnding,
                DevMode = ending.DevMode,
                RealTimeUtc = ValidUtc(ending.RealTimeUtc),
            });
        }

        // Recovered endings from save lineageEndings: use chain saves when nodes exist, or colony-level fallback when no nodes exist.
        var recoveredSource = chain.Count > 0 ? chainLineageList : colony.RecoveredEndings;
        foreach (var rec in recoveredSource)
        {
            explainedTypes.Add(rec.Type);
            if (colony.Endings.Any(e => e.Type == rec.Type && e.Ordinal == rec.Ordinal))
                continue;

            if (rec.TicksGame <= 0)
            {
                undatedTraces.Add(new ChronicleUndatedTrace(
                    StableKey: $"trace:recovered:{rec.Type}:{rec.Ordinal}",
                    EndingType: rec.Type,
                    EvidenceSource: ChronicleEvidenceSource.RecoveredLineage));
                continue;
            }

            addEntry(new ChronicleEntry
            {
                StableKey = $"ending:recovered:{rec.Type}:{rec.Ordinal}:{rec.TicksGame}",
                Kind = ChronicleEntryKind.Ending,
                TimingKind = ChronicleTimingKind.ExactTick,
                StartTick = rec.TicksGame,
                EndTick = rec.TicksGame,
                EvidenceSource = ChronicleEvidenceSource.RecoveredLineage,
                Coverage = MetricCoverage.Partial,
                RelatedMetricKey = BasicMetrics.Endings,
                EndingType = rec.Type,
                EndingOrdinal = rec.Ordinal,
                DevMode = null,
            });
        }

        // Unexplained save-file traces (no game tick exists in the save).
        var traceSource = CollectBranchTraces(colony, chain, saveObsByNode);
        foreach (var traceType in traceSource.OrderBy(t => t))
        {
            if (explainedTypes.Any(recorded => EndingTraces.Explains(recorded, traceType))
                || colony.Endings.Any(e => EndingTraces.Explains(e.Type, traceType))
                || colony.RecoveredEndings.Any(r => EndingTraces.Explains(r.Type, traceType)))
            {
                continue;
            }

            undatedTraces.Add(new ChronicleUndatedTrace(
                StableKey: $"trace:save:{traceType}",
                EndingType: traceType,
                EvidenceSource: ChronicleEvidenceSource.SaveTrace));
        }

        return (otherBranchCount, ambiguousCount);
    }

    private static HashSet<EndingType> CollectBranchTraces(
        Colony colony,
        List<SaveLineageNode> chain,
        Dictionary<int, List<SaveObservation>> saveObsByNode)
    {
        if (chain.Count == 0)
            return new HashSet<EndingType>(colony.Traces);

        var set = new HashSet<EndingType>();
        var anyMapped = false;
        foreach (var node in chain)
        {
            if (!saveObsByNode.TryGetValue(node.Index, out var obsList))
                continue;
            anyMapped = true;
            foreach (var obs in obsList)
            {
                set.UnionWith(obs.EndingTraces);
                foreach (var t in obs.ExplainedTraces)
                {
                    if (!obs.Lineage.Any(le => EndingTraces.Explains(le.Type, t))
                        && !colony.Endings.Any(e => EndingTraces.Explains(e.Type, t)))
                    {
                        set.Add(t);
                    }
                }
            }
        }
        return anyMapped ? set : new HashSet<EndingType>(colony.Traces);
    }

    private static void CollectCheckpointMilestonesAndResearch(
        List<SaveLineageNode> chain,
        Action<ChronicleEntry> addEntry)
    {
        MetricObservation? latestCp = null;
        SaveLineageNode? latestCpNode = null;
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            if (chain[i].CheckpointObservation is { Conflicted: false } cp && cp.Metrics.Count > 0)
            {
                latestCp = cp;
                latestCpNode = chain[i];
                break;
            }
        }
        if (latestCp is null || latestCpNode is null)
            return;

        TryAddFirstDayMilestone(latestCp, MembershipFamily.FirstJoinDay, ChronicleMilestoneKind.FirstColonistJoined, addEntry);
        TryAddFirstDayMilestone(latestCp, RaidFamily.FirstDay, ChronicleMilestoneKind.FirstEnemyRaid, addEntry);
        TryAddFirstDayMilestone(latestCp, HealthDeathFamily.FirstDeathDay, ChronicleMilestoneKind.FirstColonistDeath, addEntry);
        TryAddFirstDayMilestone(latestCp, ResearchFamily.FirstDay, ChronicleMilestoneKind.FirstResearchCompleted, addEntry);

        if (!latestCp.Metrics.TryGetValue(ResearchFamily.CompletedTicks, out var completedEntry)
            || completedEntry.IsQuarantined
            || completedEntry.Revision != MetricKeys.Revision(ResearchFamily.CompletedTicks))
        {
            return;
        }

        var capOverflowed = completedEntry.Values.TryGetValue(MetricEntry.Scalar, out var scalar)
            && scalar.Weakened == true;

        foreach (var (defKey, mv) in completedEntry.Values)
        {
            if (defKey == MetricEntry.Scalar)
                continue;
            if (mv.Weakened == true)
                capOverflowed = true;
            if (mv.RealValue is not { } day || !double.IsFinite(day) || day < 0
                || mv.Coverage is MetricCoverage.Unknown or MetricCoverage.NotApplicable)
            {
                continue;
            }

            var tick = ClampToIntTick(Math.Round(day * StatFormat.TicksPerDay));
            addEntry(new ChronicleEntry
            {
                StableKey = $"research:{defKey}",
                Kind = ChronicleEntryKind.ResearchCompleted,
                TimingKind = ChronicleTimingKind.ExactTick,
                StartTick = tick,
                EndTick = tick,
                EvidenceSource = ChronicleEvidenceSource.CheckpointAggregate,
                Coverage = mv.Coverage,
                RelatedMetricKey = ResearchFamily.CompletedTicks,
                ResearchDefKey = defKey,
            });
        }

        if (capOverflowed)
        {
            addEntry(new ChronicleEntry
            {
                StableKey = $"gap:research_cap:{latestCpNode.Index}",
                Kind = ChronicleEntryKind.RecordingGap,
                TimingKind = ChronicleTimingKind.ExactTick,
                StartTick = latestCpNode.SaveTick,
                EndTick = latestCpNode.SaveTick,
                EvidenceSource = ChronicleEvidenceSource.CheckpointAggregate,
                Coverage = MetricCoverage.Partial,
                RelatedMetricKey = ResearchFamily.CompletedTicks,
                GapKind = ChronicleGapKind.ResearchKeyCapReached,
                ChildSaveName = PrimaryFileName(latestCpNode),
            });
        }
    }

    private static void TryAddFirstDayMilestone(
        MetricObservation cp,
        string metricKey,
        ChronicleMilestoneKind milestoneKind,
        Action<ChronicleEntry> addEntry)
    {
        var mv = cp.Scalar(metricKey, MetricKeys.Revision(metricKey));
        if (mv is null
            || mv.Coverage is MetricCoverage.Unknown or MetricCoverage.NotApplicable
            || mv.RealValue is not { } day
            || !double.IsFinite(day)
            || day < 0)
        {
            return;
        }

        var tick = ClampToIntTick(Math.Round(day * StatFormat.TicksPerDay));
        addEntry(new ChronicleEntry
        {
            StableKey = $"milestone:{metricKey}",
            Kind = ChronicleEntryKind.Milestone,
            TimingKind = ChronicleTimingKind.ExactTick,
            StartTick = tick,
            EndTick = tick,
            EvidenceSource = ChronicleEvidenceSource.CheckpointAggregate,
            Coverage = mv.Coverage,
            RelatedMetricKey = metricKey,
            MilestoneKind = milestoneKind,
            MilestoneValue = day,
        });
    }

    private static void CollectSaveHistoryMilestones(
        StatsView view,
        ColonySaveLineage lineage,
        List<SaveLineageNode> chain,
        Action<ChronicleEntry> addEntry)
    {
        SaveLineageNode? targetNode = null;
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            if (chain[i].SaveFileObservation is not null)
            {
                targetNode = chain[i];
                break;
            }
        }
        if (targetNode is null)
            return;

        TryAddHistoryDayMilestone(view, lineage, targetNode.Index,
            MetricKeys.WealthTotalFirst100K, ChronicleMilestoneKind.WealthFirst100K, null, addEntry);
        TryAddHistoryDayMilestone(view, lineage, targetNode.Index,
            MetricKeys.WealthTotalFirst1M, ChronicleMilestoneKind.WealthFirst1M, null, addEntry);
        TryAddHistoryDayMilestone(view, lineage, targetNode.Index,
            MetricKeys.FreeColonistsFirst10, ChronicleMilestoneKind.ColonistsFirst10, null, addEntry);

        var peakWealth = lineage.MetricAt(targetNode.Index, BasicMetrics.Definition(BasicMetrics.PeakWealth))?.Number;
        TryAddHistoryDayMilestone(view, lineage, targetNode.Index,
            MetricKeys.WealthTotalPeakDay, ChronicleMilestoneKind.WealthPeakDay, peakWealth, addEntry);
    }

    private static void TryAddHistoryDayMilestone(
        StatsView view,
        ColonySaveLineage lineage,
        int nodeIndex,
        string metricKey,
        ChronicleMilestoneKind kind,
        double? associatedValue,
        Action<ChronicleEntry> addEntry)
    {
        if (ResolveDefinition(view, metricKey) is not { } def)
            return;
        var val = lineage.MetricAt(nodeIndex, def);
        if (val is null || !val.Known || val.Number is not { } day || !double.IsFinite(day) || day < 0)
            return;

        var tick = ClampToIntTick(Math.Round(day * StatFormat.TicksPerDay));
        addEntry(new ChronicleEntry
        {
            StableKey = $"milestone:history:{metricKey}",
            Kind = ChronicleEntryKind.Milestone,
            TimingKind = ChronicleTimingKind.HistorySampleDay,
            StartTick = tick,
            EndTick = tick,
            EvidenceSource = ChronicleEvidenceSource.SaveHistoryGraph,
            Coverage = val.Coverage,
            RelatedMetricKey = metricKey,
            MilestoneKind = kind,
            MilestoneValue = associatedValue,
        });
    }

    private static void CollectVerifiedIntervals(
        StatsView view,
        ColonySaveLineage lineage,
        List<SaveLineageNode> chain,
        Action<ChronicleEntry> addEntry)
    {
        var checkpoints = chain.Where(n => n.Kind == SaveLineageNodeKind.Checkpoint).ToList();
        if (checkpoints.Count < 2)
            return;

        for (var i = 1; i < checkpoints.Count; i++)
        {
            var prev = checkpoints[i - 1];
            var next = checkpoints[i];
            if (next.SaveTick <= prev.SaveTick)
                continue;

            var facts = new List<ChronicleIntervalFact>();
            var hasLiveDeaths = false;
            var hasLiveRaids = false;

            foreach (var key in IntervalCounterMetricKeys)
            {
                if (ResolveDefinition(view, key) is not { } def)
                    continue;

                var change = lineage.CompareMetric(def, prev.Index, next.Index);
                if (change.Issue != HistoryChangeIssue.None || change.Delta is not { } delta || !(delta > 0) || !double.IsFinite(delta))
                    continue;

                var cov = change.Partial || change.RecordedSince is > 0
                    ? MetricCoverage.Partial
                    : MetricCoverage.Complete;
                facts.Add(new ChronicleIntervalFact(
                    MetricKey: key,
                    Delta: delta,
                    IsSignedDifference: false,
                    Coverage: cov,
                    EvidenceSource: ChronicleEvidenceSource.CheckpointAggregate));

                if (key == HealthDeathFamily.DeathsByRaid)
                    hasLiveDeaths = true;
                else if (key == RaidFamily.ByArrivalMode)
                    hasLiveRaids = true;
            }

            // Fall back to strict verified vanilla counter intervals only when direct parent-child proof exists and live was absent.
            if (next.ParentIndex == prev.Index && next.VerifiedParentInterval is { } vpi
                && vpi.StartTick == prev.SaveTick && vpi.EndTick == next.SaveTick)
            {
                if (!hasLiveDeaths && vpi.ColonistDeathsIncrease > 0)
                {
                    facts.Add(new ChronicleIntervalFact(
                        MetricKey: BasicMetrics.Killed,
                        Delta: vpi.ColonistDeathsIncrease,
                        IsSignedDifference: false,
                        Coverage: MetricCoverage.Partial,
                        EvidenceSource: ChronicleEvidenceSource.VanillaCounterInterval));
                }
                if (!hasLiveRaids && vpi.RaidsIncrease > 0)
                {
                    facts.Add(new ChronicleIntervalFact(
                        MetricKey: BasicMetrics.Raids,
                        Delta: vpi.RaidsIncrease,
                        IsSignedDifference: false,
                        Coverage: MetricCoverage.Partial,
                        EvidenceSource: ChronicleEvidenceSource.VanillaCounterInterval));
                }
            }

            if (ResolveDefinition(view, BasicMetrics.LastWealth) is { } wealthDef)
            {
                var wealthChange = lineage.CompareMetric(wealthDef, prev.Index, next.Index);
                if (wealthChange.Issue == HistoryChangeIssue.None
                    && wealthChange.Delta is { } wDelta
                    && double.IsFinite(wDelta)
                    && Math.Abs(wDelta) >= 1.0)
                {
                    facts.Add(new ChronicleIntervalFact(
                        MetricKey: BasicMetrics.LastWealth,
                        Delta: wDelta,
                        IsSignedDifference: true,
                        Coverage: MetricCoverage.SnapshotOnly,
                        EvidenceSource: ChronicleEvidenceSource.SaveMetadata));
                }
            }

            if (facts.Count == 0)
                continue;

            var primarySource = facts.Any(f => f.EvidenceSource == ChronicleEvidenceSource.CheckpointAggregate)
                ? ChronicleEvidenceSource.CheckpointAggregate
                : facts.Any(f => f.EvidenceSource == ChronicleEvidenceSource.VanillaCounterInterval)
                    ? ChronicleEvidenceSource.VanillaCounterInterval
                    : ChronicleEvidenceSource.SaveMetadata;

            var anyPartial = facts.Any(f => f.Coverage == MetricCoverage.Partial);
            addEntry(new ChronicleEntry
            {
                StableKey = $"interval:{prev.Index}:{next.Index}",
                Kind = ChronicleEntryKind.VerifiedInterval,
                TimingKind = ChronicleTimingKind.ProvenInterval,
                StartTick = prev.SaveTick,
                EndTick = next.SaveTick,
                EvidenceSource = primarySource,
                Coverage = anyPartial ? MetricCoverage.Partial : MetricCoverage.Complete,
                RelatedMetricKey = facts[0].MetricKey,
                IntervalFacts = facts,
                ParentSaveName = PrimaryFileName(prev),
                ChildSaveName = PrimaryFileName(next),
            });
        }
    }

    private static (IReadOnlyList<ChronicleEntry> Entries, int Omitted) ApplyEntryCap(
        List<ChronicleEntry> sorted,
        int maxEntries)
    {
        if (sorted.Count <= maxEntries)
            return (sorted, 0);

        // Always prioritize high-signal structural entries (Endings, RecordingGaps, Milestones, VerifiedIntervals)
        // before individual ResearchCompleted items so hundreds of modded techs never hide endings or gaps.
        var priority = new List<ChronicleEntry>(Math.Min(sorted.Count, maxEntries));
        var research = new List<ChronicleEntry>();
        foreach (var entry in sorted)
        {
            if (entry.Kind == ChronicleEntryKind.ResearchCompleted)
                research.Add(entry);
            else
                priority.Add(entry);
        }

        List<ChronicleEntry> kept;
        if (priority.Count >= maxEntries)
        {
            // Keep all Endings, Milestones and RecordingGaps first, then the most recent VerifiedIntervals.
            var critical = priority.Where(e => e.Kind != ChronicleEntryKind.VerifiedInterval).ToList();
            if (critical.Count >= maxEntries)
            {
                kept = critical.Take(maxEntries).ToList();
            }
            else
            {
                var remaining = maxEntries - critical.Count;
                var intervals = priority.Where(e => e.Kind == ChronicleEntryKind.VerifiedInterval).TakeLast(remaining);
                kept = critical.Concat(intervals).ToList();
            }
        }
        else
        {
            var remainingForResearch = maxEntries - priority.Count;
            kept = priority.Concat(research.Take(remainingForResearch)).ToList();
        }

        kept.Sort((a, b) =>
        {
            var c = a.EndTick.CompareTo(b.EndTick);
            if (c != 0) return c;
            c = a.StartTick.CompareTo(b.StartTick);
            if (c != 0) return c;
            c = KindSortOrder(a.Kind).CompareTo(KindSortOrder(b.Kind));
            return c != 0 ? c : Ord.Compare(a.StableKey, b.StableKey);
        });

        return (kept, sorted.Count - kept.Count);
    }

    private static int KindSortOrder(ChronicleEntryKind kind) => kind switch
    {
        ChronicleEntryKind.RecordingGap => 0,
        ChronicleEntryKind.Ending => 1,
        ChronicleEntryKind.Milestone => 2,
        ChronicleEntryKind.ResearchCompleted => 3,
        ChronicleEntryKind.VerifiedInterval => 4,
        _ => 5,
    };

    private static string? PrimaryFileName(SaveLineageNode node)
        => node.Files.FirstOrDefault(f => !f.Superseded)?.FileName
            ?? node.Files.FirstOrDefault()?.FileName
            ?? node.CarriedByFiles.FirstOrDefault(f => !f.Superseded)?.FileName
            ?? node.CarriedByFiles.FirstOrDefault()?.FileName;

    private static int ClampToIntTick(double rounded)
    {
        if (!double.IsFinite(rounded) || rounded <= 0)
            return 0;
        return rounded >= int.MaxValue ? int.MaxValue : (int)rounded;
    }

    private static DateTime? ValidUtc(DateTime utc)
        => utc == default || utc.Year <= 1970 ? null : utc;
}
