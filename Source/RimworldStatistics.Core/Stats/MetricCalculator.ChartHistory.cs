namespace RimworldStatistics.Core;

public enum MetricHistoryKind { SaveObservations, CheckpointLineage, WealthRecorder, GameRecorder }

/// <summary>
/// One reading of a per-colony history. A null value is a gap (not measured), never zero. A line joins two readings
/// only when the later one is proven to continue the earlier: one checkpoint lineage, one in-game recorder series,
/// or a save whose embedded checkpoint descends from the earlier save's. The same campaign id is not that proof.
/// </summary>
/// <param name="ConnectToPrevious">This reading continues the immediately preceding one.</param>
public sealed record MetricHistoryPoint(int Tick, NumericValue? Value, bool ConnectToPrevious = false)
{
    /// <summary>
    /// The index of an earlier, non-adjacent reading this one continues; -1 when there is none or when
    /// <see cref="ConnectToPrevious"/> applies. The readings between them are not proven to be ancestors of this one;
    /// only <see cref="SkipsOnlyOtherBranches"/> says they lie on other branches.
    /// </summary>
    public int ContinuesFrom { get; init; } = -1;

    /// <summary>
    /// With <see cref="ContinuesFrom"/>: every reading between that one and this one is proven not to be this save's
    /// ancestor (each holds its own checkpoint, and none of those is on this save's chain), so they lie on other branches.
    /// False when any of them may still be an ancestor: a save without a checkpoint, a save that only carried an older
    /// checkpoint, or several saves at one tick where one of them is unproven or is in fact the ancestor.
    /// </summary>
    public bool SkipsOnlyOtherBranches { get; init; }

    /// <summary>Earlier readings exist, but none is a proven ancestor of this save: another branch, or an older save.</summary>
    public bool Unlinked { get; init; }

    /// <summary>
    /// The latest save that still held this reading, when it was saved after <see cref="Tick"/>: such saves carried
    /// the checkpoint unchanged and recorded no newer statistics. Null when no later save is known.
    /// </summary>
    public int? SavedTick { get; init; }

    /// <summary>The index of the reading this one continues (at <paramref name="index"/> in its list), or -1.</summary>
    public int Predecessor(int index) => ConnectToPrevious ? index - 1 : ContinuesFrom;
}

public sealed record MetricHistory(MetricHistoryKind Kind, IReadOnlyList<MetricHistoryPoint> Points)
{
    /// <summary>
    /// For checkpoint statistics: the furthest game tick any save or live record of the colony reached, which can be later
    /// than the last checkpoint (recording off, or the checkpoint carried unchanged). It may belong to another branch.
    /// </summary>
    public int? LatestSaveTick { get; init; }

    /// <summary>
    /// For save-time snapshot statistics: saves of this colony that have no point, because compaction thinned their reading
    /// out of the bounded chart series or (before #318) removed it. 0 when every save has its point.
    /// </summary>
    public int OmittedSaves { get; init; }

    internal IReadOnlyList<MetricFileAlias> RecorderFiles { get; init; } = [];
    internal string? RecorderHash { get; init; }
    internal double? RecorderMaximum { get; init; }
    internal IReadOnlyList<ArchivedChartPoint> ArchivedPoints { get; init; } = [];
}

internal sealed record ArchivedChartPoint(int Index, string Campaign, string Key, string? Parent, string? Hash, int Tick);

public sealed record ColonyMetricHistorySeries(
    string ColonyKey,
    MetricDefinition Definition,
    MetricHistory History);

public static partial class MetricCalculator
{
    public static string? ChartRecorder(string key) => key switch
    {
        MetricKeys.WealthTotalMean or MetricKeys.WealthTotalMaxGain15D => HistoryMetricReader.WealthTotal,
        MetricKeys.FreeColonistsMean => HistoryMetricReader.FreeColonists,
        MetricKeys.MoodMean => HistoryMetricReader.ColonistMood,
        MetricKeys.ThreatPointsMean or MetricKeys.ThreatPointsLatest => HistoryMetricReader.ThreatPoints,
        MetricKeys.AdaptationMean or MetricKeys.AdaptationLatest => HistoryMetricReader.Adaptation,
        _ => null,
    };

    public static MetricHistory BuildChartHistory(Profile profile, IReadOnlyList<Colony> colonies,
        string colonyKey, MetricDefinition definition)
    {
        var colony = colonies.FirstOrDefault(c => c.Key == colonyKey);
        if (colony is null)
            return new(MetricHistoryKind.SaveObservations, []);

        var observations = Attach(profile, colonies, out _).GetValueOrDefault(colony) ?? [];
        var ancestry = new CheckpointAncestry(observations);
        return BuildChartHistoryForColony(colony, observations, ancestry, definition);
    }

    internal static IReadOnlyList<ColonyMetricHistorySeries> BuildColonyChartHistoriesCore(Profile profile, StatsView view)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(view);
        if (view.Colonies.Count == 0)
            return [];

        var attached = Attach(profile, view.Colonies, out _);
        var statsByKey = new Dictionary<string, ColonyStats>(view.Stats.Colonies.Count, Ord);
        foreach (var cs in view.Stats.Colonies)
            statsByKey.TryAdd(cs.Key, cs);
        var numericByKey = new Dictionary<string, NumericColony>(view.Numeric.Colonies.Count, Ord);
        foreach (var nc in view.Numeric.Colonies)
            numericByKey.TryAdd(nc.Key, nc);
        var basicDefs = BasicMetrics.SaveHistoryKeys.Select(BasicMetrics.Definition).ToArray();
        var result = new List<ColonyMetricHistorySeries>();

        foreach (var colony in view.Colonies)
        {
            if (colony.Excluded || colony.OutOfScope)
                continue;
            if (statsByKey.TryGetValue(colony.Key, out var cs) && (cs.Excluded || cs.OutOfScope))
                continue;

            var observations = attached.GetValueOrDefault(colony) ?? [];
            var ancestry = new CheckpointAncestry(observations);

            if (colony.Observations.Any(o => o.Values.TicksGame >= 0))
            {
                foreach (var basicDef in basicDefs)
                {
                    var history = BasicChartHistory(colony, basicDef.Key, ancestry);
                    if (history.Points.Any(p => p.Value is { Known: true }))
                        result.Add(new ColonyMetricHistorySeries(colony.Key, basicDef, history));
                }
            }

            if (!numericByKey.TryGetValue(colony.Key, out var numColony) || numColony.Excluded || numColony.OutOfScope)
                continue;

            var storedKeysPresent = new HashSet<string>(Ord);
            var hasArchivedCheckpoint = false;
            foreach (var obs in observations)
            {
                if (obs.Source == MetricObservationSource.Checkpoint && obs.ArchivedMetrics)
                    hasArchivedCheckpoint = true;
                foreach (var mk in obs.Metrics.Keys)
                    storedKeysPresent.Add(mk);
                if (obs.CompactedChart?.Metrics is { } cm)
                {
                    foreach (var mk in cm.Keys)
                        storedKeysPresent.Add(mk);
                }
            }

            var checkpointContext = BuildCheckpointChartContext(colony, observations, ancestry);
            foreach (var row in numColony.Rows)
            {
                var def = row.Definition;
                if (CrossColonyCatalog.IsDerived(def))
                    continue;
                var maybeArchived = def.Source == MetricObservationSource.Checkpoint && hasArchivedCheckpoint;
                if (!row.Value.Known && !storedKeysPresent.Contains(def.StoredKey) && !maybeArchived)
                    continue;

                var history = def.Source == MetricObservationSource.Checkpoint
                    ? BuildCheckpointChartHistory(checkpointContext, def)
                    : BuildChartHistoryForColony(colony, observations, ancestry, def);
                if (history.Points.Any(p => p.Value is { Known: true }) || history.ArchivedPoints.Count > 0)
                    result.Add(new ColonyMetricHistorySeries(colony.Key, def, history));
            }
        }

        return result;
    }

    internal static IReadOnlyList<ColonyMetricHistorySeries> RestoreExportHistories(
        IReadOnlyList<ColonyMetricHistorySeries> series,
        ICheckpointArchive? archive)
    {
        if (series.Count == 0)
            return [];

        if (archive is null || !series.Any(s => s.History.ArchivedPoints.Count > 0))
            return series.Where(s => s.History.Points.Any(p => p.Value is { Known: true })).ToArray();

        var keysByCampaign = new Dictionary<string, HashSet<string>>(Ord);
        foreach (var item in series)
        {
            foreach (var pt in item.History.ArchivedPoints)
            {
                if (!keysByCampaign.TryGetValue(pt.Campaign, out var set))
                    keysByCampaign[pt.Campaign] = set = new HashSet<string>(Ord);
                set.Add(pt.Key);
            }
        }

        var recordsByCampaign = new Dictionary<string, ILookup<string, MetricObservation>>(Ord);
        foreach (var (campaign, keys) in keysByCampaign)
        {
            try
            {
                recordsByCampaign[campaign] = archive.ReadMany(campaign, keys).ToLookup(o => o.Key, Ord);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                // Keep explicit unmeasured gaps if an archive file cannot be read.
            }
        }

        var restored = new List<ColonyMetricHistorySeries>(series.Count);
        foreach (var item in series)
        {
            var history = item.History;
            if (history.ArchivedPoints.Count > 0)
            {
                var points = history.Points.ToArray();
                foreach (var pt in history.ArchivedPoints)
                {
                    if (!recordsByCampaign.TryGetValue(pt.Campaign, out var byKey))
                        continue;
                    var value = byKey[pt.Key].Where(o => !o.IsQuarantined && !o.Conflicted
                            && o.Source == MetricObservationSource.Checkpoint && o.Identity?.ModCampaignId == pt.Campaign
                            && o.PayloadHash == pt.Hash && o.ParentKey == pt.Parent && o.SaveTick == pt.Tick)
                        .Select(o => ChartValue(o, item.Definition)).LastOrDefault(v => v is not null);
                    points[pt.Index] = points[pt.Index] with { Value = value };
                }
                history = history with { Points = points, ArchivedPoints = [] };
            }

            if (history.Points.Any(p => p.Value is { Known: true }))
                restored.Add(item with { History = history });
        }

        return restored;
    }

    private static MetricHistory BuildChartHistoryForColony(Colony colony, List<MetricObservation> observations,
        CheckpointAncestry ancestry, MetricDefinition definition)
    {
        if (BasicMetrics.Contains(definition.Key))
            return BasicChartHistory(colony, definition.Key, ancestry);
        if (definition.Source == MetricObservationSource.Checkpoint)
            return CheckpointChartHistory(colony, observations, definition, ancestry);

        var source = observations.Where(o => o.Source == definition.Source && o.SaveTick >= 0).ToArray();
        // #318: the readings of snapshots compaction removed, carried by the latest snapshot of their campaign.
        var compacted = definition.Source == MetricObservationSource.SaveSnapshot
            ? source.SelectMany(o => CompactedReadings(o, definition)).ToArray() : [];
        if (definition.Source == MetricObservationSource.SaveSnapshot
            && definition.Acquisition is { } unit && !source.Any(o => Entry(o, definition) is not null)
            && !compacted.Any(c => c.Value is not null))
        {
            source = observations.Where(o => o.Source == MetricObservationSource.SaveFile && o.SaveTick >= 0
                && o.EvidenceByAcquisition?.GetValueOrDefault(unit) == MetricEvidence.Experimental).ToArray();
            compacted = [];
        }

        var omitted = 0;
        if (source.All(o => o.Source == MetricObservationSource.SaveSnapshot))
        {
            var saves = source.Sum(o => (long)Math.Max(1, o.CompactedObservationCount ?? 1));
            omitted = (int)Math.Clamp(saves - source.Length - compacted.Length, 0, int.MaxValue);
        }
        var points = SaveChartPoints(source.Select(o => new ChartReading(o.SaveTick, o.Identity?.ModCampaignId,
            ChartValue(o, definition), o.Source == MetricObservationSource.SaveSnapshot
                ? ancestry.OfSnapshot(o) : ancestry.OfFile(o.FileAliases, o.SaveTick)))
            .Concat(compacted.Select(c => new ChartReading(c.Row.SaveTick, c.Row.Identity?.ModCampaignId, c.Value,
                ancestry.OfSnapshot(c.Row)))), ancestry);
        var history = new MetricHistory(MetricHistoryKind.SaveObservations, points) { OmittedSaves = omitted };
        if (ChartRecorder(definition.Key) is not null && MetricObservationOrder.Latest(source) is { } latest
            && ChartValue(latest, definition)?.Number is { } maximum)
            history = history with { RecorderFiles = latest.FileAliases.ToArray(), RecorderHash = latest.Key, RecorderMaximum = maximum };
        return history;
    }

    private sealed record CheckpointChartContext(
        CheckpointAncestry Ancestry,
        string? CampaignId,
        List<(MetricObservation Obs, bool ConnectToPrevious, int? SavedLater)> LineageNodes,
        MetricObservation[] EarlierNodes,
        int?[] EarlierSavedLater,
        int? LatestSaveTick)
    {
        public static readonly CheckpointChartContext Empty = new(new CheckpointAncestry([]), null, [], [], [], null);
    }

    private static CheckpointChartContext BuildCheckpointChartContext(Colony colony, List<MetricObservation> observations, CheckpointAncestry ancestry)
    {
        var representative = CheckpointSelector.Select(observations).Representative;
        if (representative is null || representative.SaveTick < 0)
            return CheckpointChartContext.Empty;

        var byKey = observations.Where(o => o.Source == MetricObservationSource.Checkpoint)
            .ToLookup(o => o.Key, Ord);
        // The game tick of each save file version read for this colony: a checkpoint found in a save made later
        // was carried over unchanged, so its statistics stop at the checkpoint's own tick.
        var fileTicks = new Dictionary<(string, long, DateTime), int>();
        void Remember(string name, long size, DateTime utc, int tick)
        {
            var file = (name, size, UtcTime.ToUtc(utc));
            fileTicks[file] = Math.Max(tick, fileTicks.GetValueOrDefault(file, int.MinValue));
        }
        foreach (var save in colony.Observations)
            Remember(save.FileName, save.FileSize, save.LastWriteUtc, save.Values.TicksGame);
        foreach (var save in observations)
        {
            if (save.Source == MetricObservationSource.SaveFile)
                foreach (var alias in save.FileAliases)
                    Remember(alias.FileName, alias.FileSize, alias.LastWriteUtc, save.SaveTick);
        }
        int? SavedLater(MetricObservation checkpoint)
        {
            var latest = int.MinValue;
            foreach (var a in checkpoint.FileAliases)
            {
                if (fileTicks.TryGetValue((a.FileName, a.FileSize, UtcTime.ToUtc(a.LastWriteUtc)), out var t) && t > latest)
                    latest = t;
            }
            return latest > checkpoint.SaveTick ? latest : null;
        }

        var lineageNodes = new List<(MetricObservation Obs, bool ConnectToPrevious, int? SavedLater)>();
        var current = representative;
        var visited = new HashSet<string>(Ord);
        while (visited.Add(current.Key))
        {
            // Saves made while paused can share a tick. Keep the last reading at that tick,
            // but follow the intervening parents to retain the earlier history.
            if (lineageNodes.Count == 0 || lineageNodes[^1].Obs.SaveTick != current.SaveTick)
                lineageNodes.Add((current, current.ParentKey is not null, SavedLater(current)));
            if (current.ParentKey is not { } key || byKey[key].Take(2).ToArray() is not [var parent]
                || parent.Conflicted || parent.SaveTick < 0 || parent.SaveTick > current.SaveTick
                || parent.Identity?.ModCampaignId != current.Identity?.ModCampaignId)
                break;
            current = parent;
        }
        lineageNodes.Reverse();
        lineageNodes[0] = lineageNodes[0] with { ConnectToPrevious = false };

        var campaign = representative.Identity?.ModCampaignId;
        var beforeTick = lineageNodes[0].Obs.SaveTick;
        MetricObservation[] earlierNodes = [];
        int?[] earlierSavedLater = [];
        if (campaign is { Length: > 0 })
        {
            earlierNodes = observations.Where(o => o.Source == MetricObservationSource.Checkpoint && o.SaveTick >= 0
                    && o.SaveTick < beforeTick && !visited.Contains(o.Key) && o.Identity?.ModCampaignId == campaign
                    && ReferenceEquals(ancestry.Node(o.Key), o))
                .ToArray();
            if (earlierNodes.Length > 0)
            {
                static CheckpointAncestry.Anchor? Own(MetricObservation o) => new CheckpointAncestry.Anchor(o.Key, Exact: true);
                var ticks = earlierNodes.GroupBy(o => o.SaveTick).OrderBy(g => g.Key).ToArray();
                earlierSavedLater = new int?[ticks.Length];
                for (var i = 0; i < ticks.Length; i++)
                {
                    var group = ticks[i].ToArray();
                    var node = group.Length == 1 ? group[0]
                        : ancestry.Same(group.Select(Own).ToArray()) is { } same ? ancestry.Node(same.Key) : null;
                    if (node is not null)
                        earlierSavedLater[i] = SavedLater(node);
                }
            }
        }

        var furthest = -1;
        foreach (var o in colony.Observations)
            if (o.Status == ObservationStatus.Ok && o.Values.TicksGame > furthest)
                furthest = o.Values.TicksGame;
        foreach (var l in colony.LiveCampaigns)
            if (l.HasObservation && l.Max.TicksGame > furthest)
                furthest = l.Max.TicksGame;

        return new CheckpointChartContext(ancestry, campaign, lineageNodes, earlierNodes, earlierSavedLater,
            furthest >= 0 ? furthest : null);
    }

    private static MetricHistory BuildCheckpointChartHistory(CheckpointChartContext ctx, MetricDefinition definition)
    {
        if (ctx.LineageNodes.Count == 0)
            return new(MetricHistoryKind.CheckpointLineage, []);

        var points = new MetricHistoryPoint[ctx.LineageNodes.Count];
        var archived = new List<ArchivedChartPoint>();
        for (var i = 0; i < ctx.LineageNodes.Count; i++)
        {
            var (node, connect, savedLater) = ctx.LineageNodes[i];
            var value = ChartValue(node, definition);
            if (value is null && node.ArchivedMetrics && ctx.CampaignId is { Length: > 0 })
                archived.Add(new(i, ctx.CampaignId, node.Key, node.ParentKey, node.PayloadHash, node.SaveTick));
            points[i] = new(node.SaveTick, value, connect) { SavedTick = savedLater };
        }

        MetricHistoryPoint[] before = [];
        ArchivedChartPoint[] beforeArchived = [];
        if (ctx.EarlierNodes.Length > 0 && ctx.CampaignId is { Length: > 0 } campaign)
        {
            static CheckpointAncestry.Anchor? Own(MetricObservation o) => new CheckpointAncestry.Anchor(o.Key, Exact: true);
            before = SaveChartPoints(ctx.EarlierNodes.Select(o => new ChartReading(o.SaveTick, campaign, ChartValue(o, definition), Own(o))),
                ctx.Ancestry).ToArray();
            var ticks = ctx.EarlierNodes.GroupBy(o => o.SaveTick).OrderBy(g => g.Key).ToArray();
            var archivedBefore = new List<ArchivedChartPoint>();
            for (var i = 0; i < ticks.Length; i++)
            {
                before[i] = before[i] with { SavedTick = ctx.EarlierSavedLater[i] };
                if (before[i].Value is null)
                {
                    var group = ticks[i].ToArray();
                    var node = group.Length == 1 ? group[0]
                        : ctx.Ancestry.Same(group.Select(Own).ToArray()) is { } same ? ctx.Ancestry.Node(same.Key) : null;
                    if (node is { ArchivedMetrics: true })
                        archivedBefore.Add(new(i, campaign, node.Key, node.ParentKey, node.PayloadHash, node.SaveTick));
                }
            }
            beforeArchived = archivedBefore.ToArray();
            if (before.Length > 0)
                points[0] = points[0] with { Unlinked = true };
        }

        return new(MetricHistoryKind.CheckpointLineage, [.. before, .. points])
        {
            ArchivedPoints = [.. beforeArchived, .. archived.Select(a => a with { Index = a.Index + before.Length })],
            LatestSaveTick = ctx.LatestSaveTick,
        };
    }

    private static MetricHistory CheckpointChartHistory(Colony colony, List<MetricObservation> observations, MetricDefinition definition,
        CheckpointAncestry ancestry)
        => BuildCheckpointChartHistory(BuildCheckpointChartContext(colony, observations, ancestry), definition);

    internal static MetricHistory RestoreChartHistory(MetricHistory history, MetricDefinition definition, ICheckpointArchive archive)
    {
        if (history.ArchivedPoints.Count == 0)
            return history;
        var points = history.Points.ToArray();
        foreach (var campaign in history.ArchivedPoints.GroupBy(p => p.Campaign, Ord))
        {
            IReadOnlyList<MetricObservation> records;
            try
            {
                records = archive.ReadMany(campaign.Key, campaign.Select(p => p.Key).ToHashSet(Ord));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                continue;
            }
            var byKey = records.ToLookup(o => o.Key, Ord);
            foreach (var point in campaign)
            {
                var value = byKey[point.Key].Where(o => !o.IsQuarantined && !o.Conflicted
                    && o.Source == MetricObservationSource.Checkpoint && o.Identity?.ModCampaignId == point.Campaign
                    && o.PayloadHash == point.Hash && o.ParentKey == point.Parent && o.SaveTick == point.Tick)
                    .Select(o => ChartValue(o, definition)).LastOrDefault(v => v is not null);
                points[point.Index] = points[point.Index] with { Value = value };
            }
        }
        return history with { Points = points, ArchivedPoints = [] };
    }

    /// <summary>
    /// The removed saves a snapshot's chart series holds, each as the snapshot row the chart would have read (only what
    /// <see cref="CheckpointAncestry.OfSnapshot"/> uses) with its reading of <paramref name="definition"/>; a reading of
    /// another definition revision is a gap.
    /// </summary>
    private static IEnumerable<(MetricObservation Row, NumericValue? Value)> CompactedReadings(MetricObservation carrier,
        MetricDefinition definition)
    {
        if (carrier.CompactedChart is not { Rows: { } rows } series)
            yield break;
        var metric = series.Metrics?.GetValueOrDefault(definition.StoredKey) is { } m && m.Revision == definition.Revision
            && m.Fits(rows.Count) ? m : null;
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i] is not { } row || row.Tick < 0)
                continue;
            var ghost = new MetricObservation
            {
                Source = MetricObservationSource.SaveSnapshot, Identity = carrier.Identity, SaveTick = row.Tick,
                ObservedUtc = row.ObservedUtc, CheckpointKey = row.CheckpointKey,
            };
            var value = metric?.Values[i] is { } n && double.IsFinite(n)
                ? new NumericValue { Number = n, Coverage = metric.Coverage?[i] ?? MetricCoverage.SnapshotOnly }
                : null;
            yield return (ghost, value is { Coverage: not (MetricCoverage.Unknown or MetricCoverage.NotApplicable) } ? value : null);
        }
    }

    internal static NumericValue? ChartValue(MetricObservation observation, MetricDefinition definition)
    {
        // Each point is the measured value in that save. Cross-save min/max selection belongs to the bar chart.
        var actual = definition.Selection == ColonySelection.Representative
            ? definition : definition with { Selection = ColonySelection.Representative };
        var value = ColonyRow(actual, [observation], observation, observation).Value;
        return value.Number is { } number && double.IsFinite(number)
            && value.Coverage is not (MetricCoverage.Unknown or MetricCoverage.NotApplicable) ? value : null;
    }

    private static MetricHistory BasicChartHistory(Colony colony, string key, CheckpointAncestry ancestry)
    {
        var points = colony.Observations.Where(o => o.Values.TicksGame >= 0)
            .Select(o => new ChartReading(o.Values.TicksGame, o.Identity?.ModCampaignId, BasicChartValue(o.Values, key),
                ancestry.OfFile(o.FileName, o.FileSize, o.LastWriteUtc, o.Values.TicksGame)));
        return new(MetricHistoryKind.SaveObservations, SaveChartPoints(points, ancestry));
    }

    internal static NumericValue? BasicChartValue(ObservedValues values, string key)
    {
        double? number = key switch
        {
            BasicMetrics.Days => values.TicksGame / (double)StatFormat.TicksPerDay,
            BasicMetrics.RealPlayHours when StatsCalculator.ValidMetric(values.RealPlayTime) => StatFormat.Hours(values.RealPlayTime),
            BasicMetrics.PeakWealth when StatsCalculator.HasWealth(values.WealthMax, values.WealthPoints) => values.WealthMax,
            BasicMetrics.LastWealth when StatsCalculator.HasWealth(values.WealthLast, values.WealthPoints) => values.WealthLast,
            BasicMetrics.Raids when values.NumRaidsEnemy >= 0 => values.NumRaidsEnemy,
            BasicMetrics.Threats when values.NumThreatsQueued >= 0 => values.NumThreatsQueued,
            BasicMetrics.Killed when values.ColonistsKilled >= 0 => values.ColonistsKilled,
            BasicMetrics.MaxPopulation when values.GreatestPopulation >= 0 => values.GreatestPopulation,
            _ => null,
        };
        return number is { } n ? new NumericValue { Number = n, Coverage = MetricCoverage.SnapshotOnly } : null;
    }

    private readonly record struct ChartReading(int Tick, string? Campaign, NumericValue? Value, CheckpointAncestry.Anchor? Anchor);

    /// <summary>
    /// One point per tick. Readings that disagree at one tick are a gap, not an arbitrary choice. A point continues the
    /// nearest earlier point proven to be its ancestor through embedded checkpoint ids; one campaign id is no such proof
    /// (rewinds, reloads and sibling branches share it), so an unproven point stands alone.
    /// </summary>
    private static IReadOnlyList<MetricHistoryPoint> SaveChartPoints(IEnumerable<ChartReading> readings, CheckpointAncestry ancestry)
    {
        var groups = readings.GroupBy(o => o.Tick).OrderBy(g => g.Key).ToArray();
        var anchors = new CheckpointAncestry.Anchor?[groups.Length];
        var values = new NumericValue?[groups.Length];
        for (var i = 0; i < groups.Length; i++)
        {
            var group = groups[i];
            var campaigns = group.Select(o => o.Campaign).Distinct(Ord).Take(2).ToArray();
            var measured = group.Select(o => o.Value).Where(v => v is not null).ToArray();
            values[i] = campaigns.Length == 1 && measured.Select(v => v!.Number).Distinct().Take(2).Count() == 1
                ? measured.OrderBy(v => v!.Coverage == MetricCoverage.Partial ? 2 : v.Coverage == MetricCoverage.SnapshotOnly ? 1 : 0).First() : null;
            anchors[i] = campaigns is [{ Length: > 0 } campaign]
                && ancestry.Same(group.Select(o => o.Anchor).ToArray()) is { } anchor
                && ancestry.Node(anchor.Key)?.Identity?.ModCampaignId == campaign ? anchor : null;
        }

        var from = ancestry.IsEmpty ? Enumerable.Repeat(-1, groups.Length).ToArray() : ancestry.NearestAncestors(anchors);

        // What each single reading proves, apart from how its tick was merged: a reading that is exactly its own checkpoint's
        // state is an ancestor of a later save only if that checkpoint is on the later save's chain. Any other reading (no
        // checkpoint, a carried one) may be an ancestor of anything after it.
        var exactGroup = new Dictionary<string, int>(Ord);
        var unproven = new int[groups.Length + 1];
        for (var i = 0; i < groups.Length; i++)
        {
            var known = true;
            foreach (var reading in groups[i])
                if (reading.Anchor is { Exact: true } exact && reading.Campaign is { Length: > 0 } campaign
                    && ancestry.Node(exact.Key)?.Identity?.ModCampaignId == campaign)
                    exactGroup.TryAdd(exact.Key, i);
                else
                    known = false;
            unproven[i + 1] = unproven[i] + (known ? 0 : 1);
        }
        bool SkipsOnlyOtherBranches(int i, int source)
            => unproven[i] - unproven[source + 1] == 0 && anchors[i] is { } anchor
                && !ancestry.AncestorsAfter(anchor, groups[source].Key)
                    .Any(key => exactGroup.TryGetValue(key, out var at) && at > source && at < i);

        var points = new MetricHistoryPoint[groups.Length];
        for (var i = 0; i < groups.Length; i++)
        {
            var skips = from[i] >= 0 && from[i] != i - 1;
            points[i] = new MetricHistoryPoint(groups[i].Key, values[i], from[i] >= 0 && from[i] == i - 1)
            {
                ContinuesFrom = skips ? from[i] : -1,
                SkipsOnlyOtherBranches = skips && SkipsOnlyOtherBranches(i, from[i]),
                Unlinked = i > 0 && from[i] < 0,
            };
        }
        return points;
    }
}
