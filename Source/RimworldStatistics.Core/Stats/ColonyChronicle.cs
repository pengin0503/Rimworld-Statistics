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

    /// <summary>An auxiliary permanent <c>Tale</c> preserved in a save file on the selected branch (never summed into numeric totals).</summary>
    HistoricalTale,

    /// <summary>A verified multi-tick interval <c>[StartTick, EndTick]</c> between two proven checkpoints.</summary>
    VerifiedInterval,

    /// <summary>An explicit recording gap, missing parent, mid-campaign recording start, cap overflow or environment shift.</summary>
    RecordingGap,
}

/// <summary>How precisely an entry's game time is proven.</summary>
public enum ChronicleTimingKind
{
    /// <summary>Exact in-game tick recorded live or kept in a checkpoint.</summary>
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

    /// <summary>Permanent <c>Tale</c> entry stored in <c>&lt;taleManager&gt;</c> inside a save file on the selected branch.</summary>
    SaveTale,

    /// <summary>
    /// A save's own <c>lineageEndings</c> entry without a matching live record in the profile. Dated only when the profile
    /// holds a live record of the same ending that the ledger kept from another branch (a redo deduplicated by design).
    /// </summary>
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
    BossDiabolusDefeated,
    BossWarqueenDefeated,
    BossApocritonDefeated,
}

/// <summary>Gap or discontinuity subtype for <see cref="ChronicleEntryKind.RecordingGap"/>.</summary>
public enum ChronicleGapKind
{
    /// <summary>The branch's first checkpoint names a parent that is missing or cannot be linked.</summary>
    MissingParentCheckpoint,

    /// <summary>The selected record has no proven checkpoint (a save from before the MOD, or recording off).</summary>
    UnprovenSaveWithoutCheckpoint,

    /// <summary>A later save carried the last checkpoint unchanged: nothing was recorded from its tick to that save.</summary>
    CarriedCheckpointUnrecordedSpan,

    /// <summary>Some live statistics began recording only at this tick (MOD added mid-game, unit enabled, or a revision restart).</summary>
    LiveRecordingStartedLateOrReset,

    /// <summary>
    /// Between two proven checkpoints some running totals were not recorded without a gap (recording interrupted, stopped or a
    /// cap reached), so the chronicle shows no change for them over that interval.
    /// </summary>
    IntervalNotCounted,

    /// <summary>The checkpoint's detailed values were archived and the archive could not be read.</summary>
    ArchivedMetricsUnavailable,

    /// <summary>The research completion list reached its key cap; later completions are missing.</summary>
    ResearchKeyCapReached,

    /// <summary>The game version, MOD list or active DLC changed between two checkpoints of the branch.</summary>
    EnvironmentChanged,
}

/// <summary>
/// Identifies a lineage node across rebuilds of the lineage. Node indexes change whenever the profile gains or loses a
/// record, so a branch is requested by what the node is, never by its index.
/// </summary>
public sealed record ChronicleBranchKey(SaveLineageNodeKind Kind, string? CheckpointId, int SaveTick, string? FileName)
{
    public static ChronicleBranchKey Of(SaveLineageNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new ChronicleBranchKey(node.Kind, node.CheckpointId, node.SaveTick, ColonyChronicle.PrimaryFileName(node));
    }

    internal bool Matches(SaveLineageNode node)
        => node.Kind == Kind && node.SaveTick == SaveTick
            && string.Equals(node.CheckpointId, CheckpointId, StringComparison.Ordinal)
            && string.Equals(ColonyChronicle.PrimaryFileName(node), FileName, StringComparison.Ordinal);
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
    string? PrimaryFileName)
{
    public ChronicleBranchKey Key => new(Kind, CheckpointId, SaveTick, PrimaryFileName);
}

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

    public PermanentTaleKind? TaleKind { get; init; }
    public int? TaleId { get; init; }

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
/// Nothing is persisted: the chronicle is rebuilt from the profile's existing records each time it is read.
/// </summary>
public sealed class ColonyChronicle
{
    /// <summary>
    /// The most entries one chronicle returns. Research completions are bounded by their family's key cap (4,096), and
    /// intervals by the number of checkpoints; past this bound research completions are dropped first and counted.
    /// </summary>
    public const int DefaultMaxEntries = 4096;

    private static readonly StringComparer Ord = StringComparer.Ordinal;

    /// <summary>
    /// Live running totals summarised for each proven checkpoint interval. Each one must be a running total of events
    /// (<see cref="HistoryChanges.IsRunningTotal"/>): its change over an interval is what was counted in it.
    /// </summary>
    internal static readonly string[] IntervalCounterMetricKeys =
    [
        MembershipFamily.FirstJoin,
        MembershipFamily.PrisonerRecruit,
        MembershipFamily.HumanBirths,
        HealthDeathFamily.DeathsByRaid,
        RaidFamily.ByArrivalMode,
        HealthDeathFamily.HostileKills,
        TradeFamily.Deals,
        CaravanFamily.SitesCleared,
    ];

    public required string ColonyKey { get; init; }
    public required int SelectedLeafNodeIndex { get; init; }

    /// <summary>The branch shown; null only when the colony has no lineage node at all.</summary>
    public ChronicleBranchOption? SelectedBranch { get; init; }

    /// <summary>A branch was requested but is not (uniquely) in the current lineage; the default branch is shown instead.</summary>
    public bool RequestedBranchMissing { get; init; }

    public required IReadOnlyList<ChronicleBranchOption> Branches { get; init; }
    public required IReadOnlyList<int> BranchNodeChain { get; init; }
    public required IReadOnlyList<ChronicleEntry> Entries { get; init; }
    public required IReadOnlyList<ChronicleUndatedTrace> UndatedTraces { get; init; }

    /// <summary>Recorded endings proven to belong to another branch (or another campaign), left out of this chronicle.</summary>
    public int OtherBranchEndingCount { get; init; }

    /// <summary>Recorded endings whose branch cannot be proven (the lineage forks and no save settles it), left out.</summary>
    public int AmbiguousBranchEndingCount { get; init; }

    public int OmittedEntryCount { get; init; }

    /// <summary>
    /// Permanent tales at or before this tick may be missing (#393): a save on the branch held more than
    /// <see cref="SaveReader.MaxPermanentTales"/> of them and kept the newest, and no earlier save on the branch holds the ones it
    /// left out. Null when the branch's saves hold every tale they had.
    /// </summary>
    public int? TalesMissingThroughTick { get; init; }

    // ----- captured inputs -----

    /// <summary>One recorded ending, copied so the chronicle never reads profile objects outside the session lock.</summary>
    internal sealed record EndingFact(
        string CampaignId, EndingType Type, int Ordinal, EndingSource Source, int TicksGame, DateTime RealTimeUtc,
        int Departed, bool DevMode, int? PartyAtEnding, int? ColonistsKilledAtEnding);

    /// <summary>One read save of the colony, with the ending and permanent-tale evidence it carries.</summary>
    internal sealed record SaveFact(
        string FileName, long FileSize, DateTime LastWriteUtc, int Tick, string? CampaignId,
        LineageEntry[] Lineage, EndingType[] EndingTraces, EndingType[] ExplainedTraces,
        PermanentTaleFact[]? Tales = null, int? TalesDroppedThroughTick = null)
    {
        /// <summary>A save written with the MOD active carries every ending of its own past in <see cref="Lineage"/>.</summary>
        public bool LineageAuthoritative => CampaignId is { Length: > 0 };
    }

    internal sealed record Evidence(string ColonyKey, EndingFact[] Endings, SaveFact[] Saves, EndingType[] ColonyTraces);

    /// <summary>Copies the colony's ending evidence; call it under the profile session lock when the view is live.</summary>
    internal static Evidence? Capture(StatsView view, string colonyKey)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(colonyKey);
        var colony = view.Colonies.FirstOrDefault(c => c.Key == colonyKey);
        if (colony is null || !view.Stats.Colonies.Any(s => s.Key == colonyKey))
            return null;

        var endings = colony.Endings
            .Select(e => new EndingFact(e.CampaignId ?? "", e.Type, e.Ordinal, e.Source, e.TicksGame, e.RealTimeUtc,
                e.Departed, e.DevMode, e.PartyAtEnding, e.ColonistsKilledAtEnding))
            .ToArray();
        var saves = colony.Observations
            .Where(o => o.Status == ObservationStatus.Ok)
            .Select(o => new SaveFact(o.FileName, o.FileSize, o.LastWriteUtc, o.Values.TicksGame,
                o.Identity.ModCampaignId is { Length: > 0 } id ? id : null,
                o.Lineage.ToArray(), o.EndingTraces.ToArray(), o.ExplainedTraces.ToArray(),
                o.PermanentTales?.ToArray(), o.PermanentTalesDroppedThroughTick))
            .ToArray();
        return new Evidence(colony.Key, endings, saves, colony.Traces.OrderBy(t => t).ToArray());
    }

    public static ColonyChronicle? Build(
        StatsView view,
        string colonyKey,
        ColonySaveLineage lineage,
        ChronicleBranchKey? branch = null,
        bool includeDevMode = false,
        int maxEntries = DefaultMaxEntries)
    {
        ArgumentNullException.ThrowIfNull(lineage);
        var evidence = Capture(view, colonyKey);
        return evidence is null ? null : Build(view, evidence, lineage, branch, includeDevMode, maxEntries);
    }

    internal static ColonyChronicle Build(
        StatsView view,
        Evidence evidence,
        ColonySaveLineage lineage,
        ChronicleBranchKey? branch,
        bool includeDevMode,
        int maxEntries = DefaultMaxEntries)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(lineage);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);

        var (selectedIdx, requestedMissing) = ResolveSelected(lineage, branch);
        var branches = BuildBranchOptions(lineage, selectedIdx);
        var chain = BuildProvenBranchChain(lineage, selectedIdx);

        var entriesByKey = new Dictionary<string, ChronicleEntry>(Ord);
        void AddEntry(ChronicleEntry entry) => entriesByKey.TryAdd(entry.StableKey, entry);

        // 1. Recording gaps, missing parents, unproven saves, late starts, and environment changes along the branch.
        var lateStartNodes = CollectBranchStructureGaps(chain, AddEntry);

        // 2. Endings & undated traces (strictly isolated to the selected branch).
        var undatedTraces = new List<ChronicleUndatedTrace>();
        var (otherBranchEndings, ambiguousEndings, chainSaves) = CollectEndingsAndTraces(
            evidence, lineage, chain, includeDevMode, AddEntry, undatedTraces);

        // 3. Auxiliary permanent save tales strictly from saves mapped to the selected branch (never summed into numeric totals).
        CollectPermanentSaveTales(chainSaves, AddEntry);

        // 4. Exact-tick milestones and per-project research completions from the selected branch's checkpoint chain.
        CollectCheckpointMilestonesAndResearch(chain, AddEntry);

        // 5. Biotech mechanitor boss defeat milestones (exact live tick when recorded, else bounded save interval).
        CollectBiotechBossMilestones(chain, AddEntry);

        // 6. 0.5-day history-graph threshold milestones from the selected branch's latest save-file observation.
        CollectSaveHistoryMilestones(view, lineage, chain, AddEntry);

        // 7. Proven checkpoint-to-checkpoint interval summaries (never single-day events), and intervals not counted.
        CollectVerifiedIntervals(view, lineage, chain, lateStartNodes, AddEntry);

        var sorted = entriesByKey.Values.ToList();
        sorted.Sort(CompareEntries);
        var (bounded, omitted) = ApplyEntryCap(sorted, maxEntries);

        var sortedUndated = undatedTraces
            .DistinctBy(t => t.StableKey, Ord)
            .OrderBy(t => t.EndingType)
            .ThenBy(t => t.EvidenceSource)
            .ThenBy(t => t.StableKey, Ord)
            .ToList();

        return new ColonyChronicle
        {
            ColonyKey = evidence.ColonyKey,
            SelectedLeafNodeIndex = selectedIdx,
            SelectedBranch = branches.FirstOrDefault(b => b.LeafNodeIndex == selectedIdx),
            RequestedBranchMissing = requestedMissing,
            Branches = branches,
            BranchNodeChain = chain.Select(n => n.Index).ToArray(),
            Entries = bounded,
            UndatedTraces = sortedUndated,
            OtherBranchEndingCount = otherBranchEndings,
            AmbiguousBranchEndingCount = ambiguousEndings,
            OmittedEntryCount = omitted,
            TalesMissingThroughTick = MissingTalesThroughTick(chainSaves.Select(cs => cs.Save)),
        };
    }

    private static MetricDefinition? ResolveDefinition(StatsView view, string key)
        => BasicMetrics.Contains(key) ? BasicMetrics.Definition(key) : MetricLookup.Definition(view, key);

    // ----- branch selection -----

    private static (int Index, bool RequestedMissing) ResolveSelected(ColonySaveLineage lineage, ChronicleBranchKey? requested)
    {
        if (lineage.Nodes.Count == 0)
            return (-1, requested is not null);
        if (requested is not null)
        {
            var matches = lineage.Nodes.Where(requested.Matches).Take(2).ToArray();
            if (matches.Length == 1)
                return (matches[0].Index, false);
        }
        return (DefaultSelection(lineage), requested is not null);
    }

    private static int DefaultSelection(ColonySaveLineage lineage)
    {
        if (lineage.RepresentativeIndex >= 0 && lineage.RepresentativeIndex < lineage.Nodes.Count)
        {
            // A single later save that carried the representative checkpoint unchanged is the furthest point of that line.
            var rep = lineage.Nodes[lineage.RepresentativeIndex];
            if (rep.ChildIndices.Count == 1 && lineage.Nodes[rep.ChildIndices[0]] is
                { Kind: SaveLineageNodeKind.CarriedCheckpointSave } carried && carried.SaveTick > rep.SaveTick)
                return carried.Index;
            return rep.Index;
        }
        return BranchCandidates(lineage)
            .Select(i => lineage.Nodes[i])
            .OrderByDescending(n => n.Kind == SaveLineageNodeKind.Checkpoint)
            .ThenByDescending(n => n.SaveTick)
            .ThenBy(n => n.Index)
            .First().Index;
    }

    private static HashSet<int> BranchCandidates(ColonySaveLineage lineage)
    {
        var candidates = new HashSet<int>();
        if (lineage.RepresentativeIndex >= 0 && lineage.RepresentativeIndex < lineage.Nodes.Count)
            candidates.Add(lineage.RepresentativeIndex);

        var hasAnyCheckpoint = lineage.Nodes.Any(n => n.Kind == SaveLineageNodeKind.Checkpoint);
        foreach (var node in lineage.Nodes)
        {
            if (node.Kind == SaveLineageNodeKind.Checkpoint)
            {
                if (!node.ChildIndices.Any(ci => lineage.Nodes[ci].Kind == SaveLineageNodeKind.Checkpoint))
                    candidates.Add(node.Index);
            }
            else if (node.Kind == SaveLineageNodeKind.CarriedCheckpointSave)
            {
                // A later save that carried its checkpoint unchanged reaches further than that checkpoint.
                if (node.ParentIndex >= 0 && node.SaveTick > lineage.Nodes[node.ParentIndex].SaveTick)
                    candidates.Add(node.Index);
            }
            else if (!hasAnyCheckpoint && node.Kind == SaveLineageNodeKind.UnprovenSave)
            {
                candidates.Add(node.Index);
            }
        }
        if (candidates.Count == 0)
            candidates.Add(lineage.Nodes[^1].Index);
        return candidates;
    }

    private static List<ChronicleBranchOption> BuildBranchOptions(ColonySaveLineage lineage, int selectedIdx)
    {
        if (lineage.Nodes.Count == 0)
            return [];

        var candidates = BranchCandidates(lineage);
        if (selectedIdx >= 0)
            candidates.Add(selectedIdx);   // a node picked in the lineage view is offered even when it is not a leaf

        return candidates
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
        var visited = new HashSet<int>();
        for (var cur = lineage.Nodes[leafIndex]; visited.Add(cur.Index); cur = lineage.Nodes[cur.ParentIndex])
        {
            if (cur.HasRewindEvidence)
                return true;
            if (cur.ParentIndex < 0 || cur.ParentState != SaveLineageParentState.ProvenParent)
                return false;
        }
        return false;
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

    /// <summary>
    /// The other lines an unsaved ending could have happened on: every other branch leaf (and conflicted checkpoint) with
    /// the proven chain leading to it. Null when the colony's saves cannot be linked at all (no checkpoint) and there is
    /// more than one record, so no line can be singled out.
    /// </summary>
    private static List<HashSet<int>>? OtherLines(ColonySaveLineage lineage, List<SaveLineageNode> chain)
    {
        var lines = new List<HashSet<int>>();
        if (lineage.Nodes.Count == 0)
            return lines;
        if (!lineage.Nodes.Any(n => n.Kind == SaveLineageNodeKind.Checkpoint))
            return lineage.Nodes.Count == 1 ? lines : null;

        var selected = chain.Count > 0 ? chain[^1].Index : -1;
        var others = BranchCandidates(lineage)
            .Concat(lineage.Nodes.Where(n => n.Kind == SaveLineageNodeKind.ConflictedCheckpoint).Select(n => n.Index))
            .Where(i => i != selected)
            .Distinct();
        foreach (var leaf in others)
        {
            // A shorter line that our chain fully contains is not another line (e.g. the representative under our carried save).
            if (chain.Any(n => n.Index == leaf))
                continue;
            lines.Add(BuildProvenBranchChain(lineage, leaf).Select(n => n.Index).ToHashSet());
        }
        return lines;
    }

    // ----- structural gaps -----

    /// <summary>Adds the branch's structural gaps; returns the checkpoint nodes where a late start was reported.</summary>
    private static HashSet<int> CollectBranchStructureGaps(List<SaveLineageNode> chain, Action<ChronicleEntry> add)
    {
        var lateStartNodes = new HashSet<int>();
        if (chain.Count == 0)
            return lateStartNodes;

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
                        StartTick = Math.Min(spanStart, maxSince.Value),
                        EndTick = maxSince.Value,
                        EvidenceSource = ChronicleEvidenceSource.CheckpointAggregate,
                        Coverage = MetricCoverage.Partial,
                        GapKind = ChronicleGapKind.LiveRecordingStartedLateOrReset,
                        ChildSaveName = PrimaryFileName(node),
                    });
                    lateStartNodes.Add(node.Index);
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
        return lateStartNodes;
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

    // ----- endings -----

    private readonly record struct ChainSave(SaveFact Save, int NodeIndex);

    private static (int OtherBranchCount, int AmbiguousCount, List<ChainSave> ChainSaves) CollectEndingsAndTraces(
        Evidence evidence,
        ColonySaveLineage lineage,
        List<SaveLineageNode> chain,
        bool includeDevMode,
        Action<ChronicleEntry> addEntry,
        List<ChronicleUndatedTrace> undatedTraces)
    {
        var chainIndexSet = chain.Select(n => n.Index).ToHashSet();
        var nodeOfSave = MapSavesToNodes(evidence.Saves, lineage);

        // With no lineage node at all (an ending-only colony) every read save counts as this colony's only record.
        var chainSaves = new List<ChainSave>();
        var offChainProvenSaves = new List<SaveFact>();
        var savesByNode = new Dictionary<int, List<SaveFact>>();
        foreach (var save in evidence.Saves)
        {
            if (lineage.Nodes.Count == 0)
            {
                chainSaves.Add(new ChainSave(save, -1));
                continue;
            }
            if (!nodeOfSave.TryGetValue(save, out var nodeIdx))
                continue;
            if (!savesByNode.TryGetValue(nodeIdx, out var list))
                savesByNode[nodeIdx] = list = [];
            list.Add(save);
            if (chainIndexSet.Contains(nodeIdx))
                chainSaves.Add(new ChainSave(save, nodeIdx));
            else if (lineage.Nodes[nodeIdx].Kind is SaveLineageNodeKind.Checkpoint or SaveLineageNodeKind.CarriedCheckpointSave)
                offChainProvenSaves.Add(save);
        }

        var chainCampaigns = chain.Select(n => n.CampaignId)
            .Concat(chainSaves.Select(s => s.Save.CampaignId))
            .OfType<string>().Where(id => id.Length > 0).ToHashSet(Ord);
        var otherLines = OtherLines(lineage, chain);

        bool SameCampaign(SaveFact save, string campaignId)
            => campaignId.Length == 0 || save.CampaignId is null || string.Equals(save.CampaignId, campaignId, StringComparison.Ordinal);

        bool InLineage(SaveFact save, EndingType type, int ordinal, int tick)
            => save.Lineage.Any(le => le.Type == type && le.Ordinal == ordinal && le.TicksGame == tick);

        // A save of the ending's campaign made after it, without it in its own past: its line did not have that ending.
        bool Excludes(SaveFact save, EndingFact ending)
            => save.LineageAuthoritative && string.Equals(save.CampaignId, ending.CampaignId, StringComparison.Ordinal)
                && save.Tick > ending.TicksGame && !InLineage(save, ending.Type, ending.Ordinal, ending.TicksGame);

        // An ending no save holds can still be placed on this branch when every other line is excluded by its own saves.
        bool OnlyThisLineFits(EndingFact ending)
            => otherLines is not null && otherLines.All(line =>
                line.Any(idx => savesByNode.TryGetValue(idx, out var saves) && saves.Any(s => Excludes(s, ending))));

        int otherBranchCount = 0;
        int ambiguousCount = 0;

        foreach (var ending in evidence.Endings)
        {
            if (!includeDevMode && ending.DevMode)
                continue;

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

            // A save holds its own past endings with their exact ticks: one on this branch proves the ending is here, and
            // a later save of the same campaign on this branch without it proves it happened elsewhere.
            var proven = chainSaves.Any(s => s.Save.LineageAuthoritative && SameCampaign(s.Save, ending.CampaignId)
                && InLineage(s.Save, ending.Type, ending.Ordinal, ending.TicksGame));
            if (!proven)
            {
                var contradicted = chainSaves.Any(s => Excludes(s.Save, ending));
                var elsewhere = offChainProvenSaves.Any(s => s.LineageAuthoritative && SameCampaign(s, ending.CampaignId)
                    && InLineage(s, ending.Type, ending.Ordinal, ending.TicksGame));
                var otherCampaign = chainCampaigns.Count > 0 && ending.CampaignId.Length > 0
                    && !chainCampaigns.Contains(ending.CampaignId);
                if (contradicted || elsewhere || otherCampaign)
                {
                    otherBranchCount++;
                    continue;
                }
                if (!OnlyThisLineFits(ending))
                {
                    ambiguousCount++;
                    continue;
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

        // Endings in this branch's saves that the profile holds no record of at the same tick.
        var explainedTypes = new HashSet<EndingType>(evidence.Endings.Select(e => e.Type));
        var seenRecovered = new HashSet<(EndingType, int, int)>();
        foreach (var cs in chainSaves.OrderBy(s => s.Save.Tick))
        {
            if (!cs.Save.LineageAuthoritative)
                continue;
            foreach (var le in cs.Save.Lineage)
            {
                explainedTypes.Add(le.Type);
                if (!seenRecovered.Add((le.Type, le.Ordinal, le.TicksGame)))
                    continue;
                if (evidence.Endings.Any(e => e.Type == le.Type && e.Ordinal == le.Ordinal && e.TicksGame == le.TicksGame))
                    continue;   // the live (or seeded) record itself, shown or deliberately hidden above

                // The ledger keeps the first of endings sharing (campaign, type, ordinal, dev mode): a redo on this branch
                // after loading a pre-ending save is in this save's lineage at its own tick. Its dev-mode flag is the one
                // those records share. Without such a live record the entry may be a pre-install trace seeded at the load
                // tick, so its tick is not shown as the ending's day.
                var sameGroup = evidence.Endings
                    .Where(e => e.Source == EndingSource.Live && e.Type == le.Type && e.Ordinal == le.Ordinal
                        && string.Equals(e.CampaignId, cs.Save.CampaignId, StringComparison.Ordinal))
                    .ToArray();
                if (sameGroup.Length > 0 && le.TicksGame > 0)
                {
                    bool? dev = sameGroup.All(e => e.DevMode) ? true : sameGroup.All(e => !e.DevMode) ? false : null;
                    if (dev == true && !includeDevMode)
                        continue;
                    addEntry(new ChronicleEntry
                    {
                        StableKey = $"ending:recovered:{cs.Save.CampaignId}:{le.Type}:{le.Ordinal}:{le.TicksGame}",
                        Kind = ChronicleEntryKind.Ending,
                        TimingKind = ChronicleTimingKind.ExactTick,
                        StartTick = le.TicksGame,
                        EndTick = le.TicksGame,
                        EvidenceSource = ChronicleEvidenceSource.RecoveredLineage,
                        Coverage = MetricCoverage.Partial,
                        RelatedMetricKey = BasicMetrics.Endings,
                        EndingType = le.Type,
                        EndingOrdinal = le.Ordinal,
                        DevMode = dev,
                    });
                }
                else
                {
                    undatedTraces.Add(new ChronicleUndatedTrace(
                        StableKey: $"trace:recovered:{le.Type}:{le.Ordinal}",
                        EndingType: le.Type,
                        EvidenceSource: ChronicleEvidenceSource.RecoveredLineage));
                }
            }
        }

        // Unexplained save-file traces (no game tick exists in the save).
        foreach (var traceType in CollectBranchTraces(evidence, chainSaves).OrderBy(t => t))
        {
            if (explainedTypes.Any(recorded => EndingTraces.Explains(recorded, traceType)))
                continue;
            undatedTraces.Add(new ChronicleUndatedTrace(
                StableKey: $"trace:save:{traceType}",
                EndingType: traceType,
                EvidenceSource: ChronicleEvidenceSource.SaveTrace));
        }

        return (otherBranchCount, ambiguousCount, chainSaves);
    }

    /// <summary>
    /// The latest tick through which the branch's saves may lack a permanent tale (#393). A save's tales are its whole past (they
    /// are never culled), so the latest save holds every one, except those at or before the tick through which it left some out.
    /// Those are held by an earlier save that was at least as far on and read tales, down to what that save left out in turn; a
    /// save that was not yet that far on leaves the rest uncovered.
    /// </summary>
    internal static int? MissingTalesThroughTick(IEnumerable<SaveFact> branchSaves)
    {
        int? missing = null;
        var first = true;
        foreach (var save in branchSaves.OrderByDescending(s => s.Tick))
        {
            if (save.Tales is not { Length: > 0 } && save.TalesDroppedThroughTick is null)
                continue;   // no tales read from this save: it neither holds nor proves anything
            if (first)
            {
                first = false;
                missing = save.TalesDroppedThroughTick;
            }
            else if (save.Tick >= missing)
            {
                missing = save.TalesDroppedThroughTick;
            }
            else
            {
                break;
            }
            if (missing is null)
                break;
        }
        return missing;
    }

    private static void CollectPermanentSaveTales(
        List<ChainSave> chainSaves,
        Action<ChronicleEntry> addEntry)
    {
        if (chainSaves.Count == 0)
            return;

        var seen = new HashSet<(PermanentTaleKind Kind, int Id)>();
        foreach (var cs in chainSaves.OrderBy(s => s.Save.Tick))
        {
            if (cs.Save.Tales is not { Length: > 0 } tales)
                continue;
            foreach (var t in tales)
            {
                if (t.TicksGame < 0 || t.TicksGame > cs.Save.Tick)
                    continue;
                if (!Enum.IsDefined(t.Kind) || !seen.Add((t.Kind, t.Id)))
                    continue;

                addEntry(new ChronicleEntry
                {
                    StableKey = $"tale:{t.Kind}:{t.Id}",
                    Kind = ChronicleEntryKind.HistoricalTale,
                    TimingKind = ChronicleTimingKind.ExactTick,
                    StartTick = t.TicksGame,
                    EndTick = t.TicksGame,
                    EvidenceSource = ChronicleEvidenceSource.SaveTale,
                    Coverage = MetricCoverage.SnapshotOnly,
                    ChildSaveName = cs.Save.FileName,
                    TaleKind = t.Kind,
                    TaleId = t.Id,
                });
            }
        }
    }

    /// <summary>
    /// Each read save mapped to the one lineage node that holds its file. Files of later saves that merely carried a
    /// checkpoint belong to their own carried node, never to the checkpoint they carried.
    /// </summary>
    private static Dictionary<SaveFact, int> MapSavesToNodes(SaveFact[] saves, ColonySaveLineage lineage)
    {
        var byFile = new Dictionary<(string FileName, long FileSize, DateTime LastWriteUtc), int>();
        foreach (var node in lineage.Nodes)
            foreach (var file in node.Files)
                byFile.TryAdd((file.FileName, file.FileSize, file.LastWriteUtc), node.Index);

        var result = new Dictionary<SaveFact, int>(ReferenceEqualityComparer.Instance);
        foreach (var save in saves)
            if (byFile.TryGetValue((save.FileName, save.FileSize, save.LastWriteUtc), out var idx))
                result[save] = idx;
        return result;
    }

    private static HashSet<EndingType> CollectBranchTraces(Evidence evidence, List<ChainSave> chainSaves)
    {
        if (chainSaves.Count == 0)
            return new HashSet<EndingType>(evidence.ColonyTraces);

        var set = new HashSet<EndingType>();
        foreach (var cs in chainSaves)
        {
            set.UnionWith(cs.Save.EndingTraces);
            foreach (var t in cs.Save.ExplainedTraces)
                if (!cs.Save.Lineage.Any(le => EndingTraces.Explains(le.Type, t)))
                    set.Add(t);
        }
        return set;
    }

    // ----- checkpoint milestones and research -----

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

        // The family weakens every value of the list when it reached its key cap (later completions were dropped).
        var capOverflowed = completedEntry.Values.Values.Any(v => v.Weakened == true);

        foreach (var (defKey, mv) in completedEntry.Values)
        {
            if (defKey == MetricEntry.Scalar)
                continue;
            if (mv.RealValue is not { } day || !double.IsFinite(day) || day < 0
                || mv.Coverage is MetricCoverage.Unknown or MetricCoverage.NotApplicable)
            {
                continue;
            }

            // Each kept completion is exact, whatever the list as a whole misses (shown by the gap entries).
            var tick = ClampToIntTick(Math.Round(day * StatFormat.TicksPerDay));
            addEntry(new ChronicleEntry
            {
                StableKey = $"research:{defKey}",
                Kind = ChronicleEntryKind.ResearchCompleted,
                TimingKind = ChronicleTimingKind.ExactTick,
                StartTick = tick,
                EndTick = tick,
                EvidenceSource = ChronicleEvidenceSource.CheckpointAggregate,
                Coverage = MetricCoverage.Complete,
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
        // A first day is known only when its family recorded from the start of the game without a gap (otherwise Unknown).
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
            Coverage = MetricCoverage.Complete,
            RelatedMetricKey = metricKey,
            MilestoneKind = milestoneKind,
            MilestoneValue = day,
        });
    }

    private static readonly (string BossKey, ChronicleMilestoneKind Kind)[] BiotechBossMilestones =
    [
        (HistoricalBiotechBossReader.Diabolus, ChronicleMilestoneKind.BossDiabolusDefeated),
        (HistoricalBiotechBossReader.Warqueen, ChronicleMilestoneKind.BossWarqueenDefeated),
        (HistoricalBiotechBossReader.Apocriton, ChronicleMilestoneKind.BossApocritonDefeated),
    ];

    private static void CollectBiotechBossMilestones(
        List<SaveLineageNode> chain,
        Action<ChronicleEntry> addEntry)
    {
        if (chain.Count == 0)
            return;

        MetricObservation? latestCp = null;
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            if (chain[i].CheckpointObservation is { Conflicted: false } cp && cp.Metrics.Count > 0)
            {
                latestCp = cp;
                break;
            }
        }

        MetricEntry? exactTicksEntry = null;
        if (latestCp is not null
            && latestCp.Metrics.TryGetValue(BiotechGeneticsFamily.BossFirstDefeatedTicks, out var candidate)
            && !candidate.IsQuarantined
            && candidate.Revision == MetricKeys.Revision(BiotechGeneticsFamily.BossFirstDefeatedTicks))
        {
            exactTicksEntry = candidate;
        }

        var bossesRev = MetricKeys.Revision(BiotechStateSnapshot.BossesDefeated);

        foreach (var (bossKey, milestoneKind) in BiotechBossMilestones)
        {
            // 1. Live exact tick from BiotechGeneticsFamily on this branch.
            if (exactTicksEntry is not null
                && exactTicksEntry.Values.TryGetValue(bossKey, out var exactMv)
                && exactMv.Coverage is not (MetricCoverage.Unknown or MetricCoverage.NotApplicable)
                && exactMv.RealValue is { } day
                && double.IsFinite(day)
                && day >= 0)
            {
                var tick = ClampToIntTick(Math.Round(day * StatFormat.TicksPerDay));
                addEntry(new ChronicleEntry
                {
                    StableKey = $"milestone:boss:{bossKey}",
                    Kind = ChronicleEntryKind.Milestone,
                    TimingKind = ChronicleTimingKind.ExactTick,
                    StartTick = tick,
                    EndTick = tick,
                    EvidenceSource = ChronicleEvidenceSource.CheckpointAggregate,
                    Coverage = MetricCoverage.Complete,
                    RelatedMetricKey = BiotechGeneticsFamily.BossFirstDefeatedTicks,
                    MilestoneKind = milestoneKind,
                    MilestoneValue = day,
                });
                continue;
            }

            // 2. Saved GameComponent_Bossgroup state along the proven branch: bounded interval [prevUndefeatedTick, firstDefeatedSaveTick].
            var prevUndefeatedTick = 0;
            foreach (var node in chain)
            {
                MetricEntry? defeatedEntry = null;
                if (node.SnapshotObservation is { Conflicted: false } snap
                    && snap.Metrics.TryGetValue(BiotechStateSnapshot.BossesDefeated, out var snapEntry)
                    && !snapEntry.IsQuarantined
                    && snapEntry.Revision == bossesRev)
                {
                    defeatedEntry = snapEntry;
                }
                else if (node.SaveFileObservation is { Conflicted: false } saveFile
                    && saveFile.Metrics.TryGetValue(BiotechStateSnapshot.BossesDefeated, out var fileEntry)
                    && !fileEntry.IsQuarantined
                    && fileEntry.Revision == bossesRev)
                {
                    defeatedEntry = fileEntry;
                }

                if (defeatedEntry is null
                    || !defeatedEntry.Values.TryGetValue(MetricEntry.Scalar, out var scalar)
                    || scalar.Coverage is MetricCoverage.Unknown or MetricCoverage.NotApplicable)
                {
                    continue;
                }

                if (defeatedEntry.Values.TryGetValue(bossKey, out var bossMv)
                    && bossMv.Coverage is not (MetricCoverage.Unknown or MetricCoverage.NotApplicable)
                    && bossMv.IntegerValue is >= 1)
                {
                    var startTick = Math.Clamp(prevUndefeatedTick, 0, Math.Max(0, node.SaveTick));
                    addEntry(new ChronicleEntry
                    {
                        StableKey = $"milestone:boss:{bossKey}",
                        Kind = ChronicleEntryKind.Milestone,
                        TimingKind = ChronicleTimingKind.ProvenInterval,
                        StartTick = startTick,
                        EndTick = Math.Max(0, node.SaveTick),
                        EvidenceSource = ChronicleEvidenceSource.SaveMetadata,
                        Coverage = MetricCoverage.Partial,
                        RelatedMetricKey = BiotechStateSnapshot.BossesDefeated,
                        MilestoneKind = milestoneKind,
                    });
                    break;
                }

                if (scalar.Weakened != true && node.SaveTick > prevUndefeatedTick)
                    prevUndefeatedTick = node.SaveTick;
            }
        }
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

        var peakWealth = lineage.MetricAt(targetNode.Index, BasicMetrics.Definition(BasicMetrics.PeakWealth));
        TryAddHistoryDayMilestone(view, lineage, targetNode.Index,
            MetricKeys.WealthTotalPeakDay, ChronicleMilestoneKind.WealthPeakDay,
            peakWealth is { Known: true, Number: { } peak } && double.IsFinite(peak) ? peak : null, addEntry);
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

    // ----- intervals -----

    private static void CollectVerifiedIntervals(
        StatsView view,
        ColonySaveLineage lineage,
        List<SaveLineageNode> chain,
        HashSet<int> lateStartNodes,
        Action<ChronicleEntry> addEntry)
    {
        var checkpoints = chain.Where(n => n.Kind == SaveLineageNodeKind.Checkpoint).ToList();
        if (checkpoints.Count < 2)
            return;

        var counterDefs = IntervalCounterMetricKeys
            .Select(key => ResolveDefinition(view, key))
            .OfType<MetricDefinition>()
            .Where(HistoryChanges.IsRunningTotal)
            .ToArray();
        var wealthDef = ResolveDefinition(view, BasicMetrics.LastWealth);
        var notCountedSpans = new List<(SaveLineageNode From, SaveLineageNode To)>();

        for (var i = 1; i < checkpoints.Count; i++)
        {
            var prev = checkpoints[i - 1];
            var next = checkpoints[i];
            if (next.SaveTick <= prev.SaveTick || next.ParentIndex != prev.Index)
                continue;

            var facts = new List<ChronicleIntervalFact>();
            var liveDeathsCompared = false;
            var liveRaidsCompared = false;
            var notCounted = false;

            foreach (var def in counterDefs)
            {
                var change = lineage.CompareMetric(def, prev.Index, next.Index);
                if (change.Issue is HistoryChangeIssue.CounterGap or HistoryChangeIssue.CounterDecrease)
                    notCounted = true;
                if (change.Issue != HistoryChangeIssue.None || change.Delta is not { } delta || !double.IsFinite(delta))
                    continue;

                if (def.Key == HealthDeathFamily.DeathsByRaid)
                    liveDeathsCompared = true;
                else if (def.Key == RaidFamily.ByArrivalMode)
                    liveRaidsCompared = true;
                if (!(delta > 0))
                    continue;

                facts.Add(new ChronicleIntervalFact(
                    MetricKey: def.Key,
                    Delta: delta,
                    IsSignedDifference: false,
                    Coverage: change.Partial || change.RecordedSince is > 0 ? MetricCoverage.Partial : MetricCoverage.Complete,
                    EvidenceSource: ChronicleEvidenceSource.CheckpointAggregate));
            }

            // The game's own counters over a strictly verified direct parent-child pair, only where no live comparison exists.
            if (next.VerifiedParentInterval is { } vpi && vpi.StartTick == prev.SaveTick && vpi.EndTick == next.SaveTick)
            {
                if (!liveDeathsCompared && vpi.ColonistDeathsIncrease > 0)
                {
                    facts.Add(new ChronicleIntervalFact(
                        MetricKey: BasicMetrics.Killed,
                        Delta: vpi.ColonistDeathsIncrease,
                        IsSignedDifference: false,
                        Coverage: vpi.Coverage,
                        EvidenceSource: ChronicleEvidenceSource.VanillaCounterInterval));
                }
                if (!liveRaidsCompared && vpi.RaidsIncrease > 0)
                {
                    facts.Add(new ChronicleIntervalFact(
                        MetricKey: BasicMetrics.Raids,
                        Delta: vpi.RaidsIncrease,
                        IsSignedDifference: false,
                        Coverage: vpi.Coverage,
                        EvidenceSource: ChronicleEvidenceSource.VanillaCounterInterval));
                }
            }

            if (wealthDef is not null)
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

            // A family recorded without a gap at the parent but not at the child was interrupted, stopped or restarted.
            if (prev.CheckpointObservation?.ContinuousFamilies is { } before
                && next.CheckpointObservation?.ContinuousFamilies is { } after
                && before.Any(f => !after.Contains(f, Ord))
                && !lateStartNodes.Contains(next.Index))
            {
                notCounted = true;
            }
            if (notCounted && !lateStartNodes.Contains(next.Index))
            {
                // Consecutive intervals that could not be counted are reported as one span.
                if (notCountedSpans.Count > 0 && notCountedSpans[^1].To.Index == prev.Index)
                    notCountedSpans[^1] = (notCountedSpans[^1].From, next);
                else
                    notCountedSpans.Add((prev, next));
            }

            if (facts.Count == 0)
                continue;

            var primarySource = facts.Any(f => f.EvidenceSource == ChronicleEvidenceSource.CheckpointAggregate)
                ? ChronicleEvidenceSource.CheckpointAggregate
                : facts.Any(f => f.EvidenceSource == ChronicleEvidenceSource.VanillaCounterInterval)
                    ? ChronicleEvidenceSource.VanillaCounterInterval
                    : ChronicleEvidenceSource.SaveMetadata;

            addEntry(new ChronicleEntry
            {
                StableKey = $"interval:{prev.Index}:{next.Index}",
                Kind = ChronicleEntryKind.VerifiedInterval,
                TimingKind = ChronicleTimingKind.ProvenInterval,
                StartTick = prev.SaveTick,
                EndTick = next.SaveTick,
                EvidenceSource = primarySource,
                Coverage = facts.Any(f => f.Coverage == MetricCoverage.Partial) ? MetricCoverage.Partial : MetricCoverage.Complete,
                RelatedMetricKey = facts[0].MetricKey,
                IntervalFacts = facts,
                ParentSaveName = PrimaryFileName(prev),
                ChildSaveName = PrimaryFileName(next),
            });
        }

        foreach (var (from, to) in notCountedSpans)
        {
            addEntry(new ChronicleEntry
            {
                StableKey = $"gap:not_counted:{from.Index}:{to.Index}",
                Kind = ChronicleEntryKind.RecordingGap,
                TimingKind = ChronicleTimingKind.ProvenInterval,
                StartTick = from.SaveTick,
                EndTick = to.SaveTick,
                EvidenceSource = ChronicleEvidenceSource.CheckpointAggregate,
                Coverage = MetricCoverage.Partial,
                GapKind = ChronicleGapKind.IntervalNotCounted,
                ParentSaveName = PrimaryFileName(from),
                ChildSaveName = PrimaryFileName(to),
            });
        }
    }

    // ----- ordering and bounds -----

    private static int CompareEntries(ChronicleEntry a, ChronicleEntry b)
    {
        var c = a.EndTick.CompareTo(b.EndTick);
        if (c != 0) return c;
        c = a.StartTick.CompareTo(b.StartTick);
        if (c != 0) return c;
        c = KindSortOrder(a.Kind).CompareTo(KindSortOrder(b.Kind));
        return c != 0 ? c : Ord.Compare(a.StableKey, b.StableKey);
    }

    private static (IReadOnlyList<ChronicleEntry> Entries, int Omitted) ApplyEntryCap(
        List<ChronicleEntry> sorted,
        int maxEntries)
    {
        if (sorted.Count <= maxEntries)
            return (sorted, 0);

        // Endings, gaps and milestones first, then the most recent intervals, then auxiliary permanent tales, then research
        // completions in completion order, so auxiliary tales or hundreds of modded research projects never hide an ending or a gap.
        var critical = sorted.Where(e => e.Kind is not (ChronicleEntryKind.ResearchCompleted or ChronicleEntryKind.VerifiedInterval or ChronicleEntryKind.HistoricalTale)).ToList();
        var intervals = sorted.Where(e => e.Kind == ChronicleEntryKind.VerifiedInterval).ToList();
        var tales = sorted.Where(e => e.Kind == ChronicleEntryKind.HistoricalTale).ToList();
        var research = sorted.Where(e => e.Kind == ChronicleEntryKind.ResearchCompleted).ToList();

        var kept = critical.Take(maxEntries).ToList();
        var room = maxEntries - kept.Count;
        if (room > 0)
        {
            var keptIntervals = intervals.Skip(Math.Max(0, intervals.Count - room)).ToList();
            kept.AddRange(keptIntervals);
            room -= keptIntervals.Count;
        }
        if (room > 0)
        {
            var keptTales = tales.Take(room).ToList();
            kept.AddRange(keptTales);
            room -= keptTales.Count;
        }
        if (room > 0)
            kept.AddRange(research.Take(room));

        kept.Sort(CompareEntries);
        return (kept, sorted.Count - kept.Count);
    }

    private static int KindSortOrder(ChronicleEntryKind kind) => kind switch
    {
        ChronicleEntryKind.RecordingGap => 0,
        ChronicleEntryKind.Ending => 1,
        ChronicleEntryKind.Milestone => 2,
        ChronicleEntryKind.HistoricalTale => 3,
        ChronicleEntryKind.ResearchCompleted => 4,
        ChronicleEntryKind.VerifiedInterval => 5,
        _ => 6,
    };

    internal static string? PrimaryFileName(SaveLineageNode node)
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
