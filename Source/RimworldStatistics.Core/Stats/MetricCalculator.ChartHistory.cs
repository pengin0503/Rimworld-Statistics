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
    /// The index of an earlier, non-adjacent reading this one continues (readings of another branch lie between them);
    /// -1 when there is none or when <see cref="ConnectToPrevious"/> applies.
    /// </summary>
    public int ContinuesFrom { get; init; } = -1;

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
    internal IReadOnlyList<MetricFileAlias> RecorderFiles { get; init; } = [];
    internal string? RecorderHash { get; init; }
    internal double? RecorderMaximum { get; init; }
    internal IReadOnlyList<ArchivedChartPoint> ArchivedPoints { get; init; } = [];
}

internal sealed record ArchivedChartPoint(int Index, string Campaign, string Key, string? Parent, string? Hash, int Tick);

public static partial class MetricCalculator
{
    public static string? ChartRecorder(string key) => key switch
    {
        MetricKeys.WealthTotalMean or MetricKeys.WealthTotalMaxGain15D => HistoryMetricReader.WealthTotal,
        MetricKeys.FreeColonistsMean => HistoryMetricReader.FreeColonists,
        MetricKeys.MoodMean => HistoryMetricReader.ColonistMood,
        _ => null,
    };

    public static MetricHistory BuildChartHistory(Profile profile, IReadOnlyList<Colony> colonies,
        string colonyKey, MetricDefinition definition)
    {
        var colony = colonies.FirstOrDefault(c => c.Key == colonyKey && c.Counted);
        if (colony is null)
            return new(MetricHistoryKind.SaveObservations, []);

        var observations = Attach(profile, colonies, out _).GetValueOrDefault(colony) ?? [];
        var ancestry = new CheckpointAncestry(observations);
        if (BasicMetrics.Contains(definition.Key))
            return BasicChartHistory(colony, definition.Key, ancestry);
        if (definition.Source == MetricObservationSource.Checkpoint)
            return CheckpointChartHistory(colony, observations, definition);

        var source = observations.Where(o => o.Source == definition.Source && o.SaveTick >= 0).ToArray();
        if (definition.Source == MetricObservationSource.SaveSnapshot
            && definition.Acquisition is { } unit && !source.Any(o => Entry(o, definition) is not null))
            source = observations.Where(o => o.Source == MetricObservationSource.SaveFile && o.SaveTick >= 0
                && o.EvidenceByAcquisition?.GetValueOrDefault(unit) == MetricEvidence.Experimental).ToArray();

        var points = SaveChartPoints(source.Select(o => new ChartReading(o.SaveTick, o.Identity.ModCampaignId,
            ChartValue(o, definition), o.Source == MetricObservationSource.SaveSnapshot
                ? ancestry.OfSnapshot(o) : ancestry.OfFile(o.FileAliases, o.SaveTick))), ancestry);
        var history = new MetricHistory(MetricHistoryKind.SaveObservations, points);
        if (ChartRecorder(definition.Key) is not null && MetricObservationOrder.Latest(source) is { } latest
            && ChartValue(latest, definition)?.Number is { } maximum)
            history = history with { RecorderFiles = latest.FileAliases.ToArray(), RecorderHash = latest.Key, RecorderMaximum = maximum };
        return history;
    }

    private static MetricHistory CheckpointChartHistory(Colony colony, List<MetricObservation> observations, MetricDefinition definition)
    {
        var representative = CheckpointSelector.Select(observations).Representative;
        if (representative is null || representative.SaveTick < 0)
            return new(MetricHistoryKind.CheckpointLineage, []);

        var byKey = observations.Where(o => o.Source == MetricObservationSource.Checkpoint)
            .ToLookup(o => o.Key, Ord);
        // The game tick of each save file version read for this colony: a checkpoint found in a save made later
        // was carried over unchanged, so its statistics stop at the checkpoint's own tick.
        var fileTicks = new Dictionary<(string, long, DateTime), int>();
        void Remember(string name, long size, DateTime utc, int tick)
        {
            var file = (name, size, utc);
            fileTicks[file] = Math.Max(tick, fileTicks.GetValueOrDefault(file, int.MinValue));
        }
        foreach (var save in colony.Observations)
            Remember(save.FileName, save.FileSize, save.LastWriteUtc, save.Values.TicksGame);
        foreach (var save in observations.Where(o => o.Source == MetricObservationSource.SaveFile))
            foreach (var alias in save.FileAliases)
                Remember(alias.FileName, alias.FileSize, alias.LastWriteUtc, save.SaveTick);
        int? SavedLater(MetricObservation checkpoint)
        {
            var latest = checkpoint.FileAliases.Select(a => fileTicks.TryGetValue((a.FileName, a.FileSize, a.LastWriteUtc), out var t)
                ? t : int.MinValue).DefaultIfEmpty(int.MinValue).Max();
            return latest > checkpoint.SaveTick ? latest : null;
        }

        var points = new List<MetricHistoryPoint>();
        var archived = new List<ArchivedChartPoint>();
        var current = representative;
        var visited = new HashSet<string>(Ord);
        while (visited.Add(current.Key))
        {
            // Saves made while paused can share a tick. Keep the last reading at that tick,
            // but follow the intervening parents to retain the earlier history.
            if (points.Count == 0 || points[^1].Tick != current.SaveTick)
            {
                var value = ChartValue(current, definition);
                if (value is null && current.ArchivedMetrics && current.Identity.ModCampaignId is { Length: > 0 } campaign)
                    archived.Add(new(points.Count, campaign, current.Key, current.ParentKey, current.PayloadHash, current.SaveTick));
                points.Add(new(current.SaveTick, value, current.ParentKey is not null) { SavedTick = SavedLater(current) });
            }
            if (current.ParentKey is not { } key || byKey[key].Take(2).ToArray() is not [var parent]
                || parent.Conflicted || parent.SaveTick < 0 || parent.SaveTick > current.SaveTick
                || parent.Identity.ModCampaignId != current.Identity.ModCampaignId)
                break;
            current = parent;
        }
        points.Reverse();
        points[0] = points[0] with { ConnectToPrevious = false };
        return new(MetricHistoryKind.CheckpointLineage, points)
        {
            ArchivedPoints = archived.Select(a => a with { Index = points.Count - 1 - a.Index }).ToArray(),
        };
    }

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

    private static NumericValue? ChartValue(MetricObservation observation, MetricDefinition definition)
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
            .Select(o => new ChartReading(o.Values.TicksGame, o.Identity.ModCampaignId, BasicChartValue(o.Values, key),
                ancestry.OfFile(o.FileName, o.FileSize, o.LastWriteUtc, o.Values.TicksGame)));
        return new(MetricHistoryKind.SaveObservations, SaveChartPoints(points, ancestry));
    }

    private static NumericValue? BasicChartValue(ObservedValues values, string key)
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
                ? measured.OrderBy(v => v!.Coverage == MetricCoverage.Partial).First() : null;
            anchors[i] = campaigns is [{ Length: > 0 } campaign]
                && ancestry.Same(group.Select(o => o.Anchor).ToArray()) is { } anchor
                && ancestry.Node(anchor.Key)?.Identity.ModCampaignId == campaign ? anchor : null;
        }

        var from = ancestry.IsEmpty ? Enumerable.Repeat(-1, groups.Length).ToArray() : ancestry.NearestAncestors(anchors);
        var points = new MetricHistoryPoint[groups.Length];
        for (var i = 0; i < groups.Length; i++)
            points[i] = new MetricHistoryPoint(groups[i].Key, values[i], from[i] >= 0 && from[i] == i - 1)
            {
                ContinuesFrom = from[i] >= 0 && from[i] != i - 1 ? from[i] : -1,
                Unlinked = i > 0 && from[i] < 0,
            };
        return points;
    }
}
