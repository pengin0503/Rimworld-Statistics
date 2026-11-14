namespace RimworldStatistics.Core;

public enum MetricHistoryKind { SaveObservations, CheckpointLineage, WealthRecorder, GameRecorder }

public sealed record MetricHistoryPoint(int Tick, NumericValue? Value, bool ConnectToPrevious = false);

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
        if (BasicMetrics.Contains(definition.Key))
            return BasicChartHistory(colony, definition.Key);

        var observations = Attach(profile, colonies, out _).GetValueOrDefault(colony) ?? [];
        if (definition.Source == MetricObservationSource.Checkpoint)
            return CheckpointChartHistory(observations, definition);

        var source = observations.Where(o => o.Source == definition.Source && o.SaveTick >= 0).ToArray();
        if (definition.Source == MetricObservationSource.SaveSnapshot
            && definition.Acquisition is { } unit && !source.Any(o => Entry(o, definition) is not null))
            source = observations.Where(o => o.Source == MetricObservationSource.SaveFile && o.SaveTick >= 0
                && o.EvidenceByAcquisition?.GetValueOrDefault(unit) == MetricEvidence.Experimental).ToArray();

        var points = SaveChartPoints(source.Select(o => (o.SaveTick, o.Identity.ModCampaignId, ChartValue(o, definition))));
        var history = new MetricHistory(MetricHistoryKind.SaveObservations, points);
        if (ChartRecorder(definition.Key) is not null && MetricObservationOrder.Latest(source) is { } latest
            && ChartValue(latest, definition)?.Number is { } maximum)
            history = history with { RecorderFiles = latest.FileAliases.ToArray(), RecorderHash = latest.Key, RecorderMaximum = maximum };
        return history;
    }

    private static MetricHistory CheckpointChartHistory(List<MetricObservation> observations, MetricDefinition definition)
    {
        var representative = CheckpointSelector.Select(observations).Representative;
        if (representative is null || representative.SaveTick < 0)
            return new(MetricHistoryKind.CheckpointLineage, []);

        var byKey = observations.Where(o => o.Source == MetricObservationSource.Checkpoint)
            .ToLookup(o => o.Key, Ord);
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
                points.Add(new(current.SaveTick, value, current.ParentKey is not null));
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

    private static MetricHistory BasicChartHistory(Colony colony, string key)
    {
        var points = colony.Observations.Where(o => o.Values.TicksGame >= 0)
            .Select(o => (o.Values.TicksGame, o.Identity.ModCampaignId, BasicChartValue(o.Values, key)));
        return new(MetricHistoryKind.SaveObservations, SaveChartPoints(points));
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

    private static IReadOnlyList<MetricHistoryPoint> SaveChartPoints(
        IEnumerable<(int Tick, string? Campaign, NumericValue? Value)> observations)
    {
        var points = new List<MetricHistoryPoint>();
        string? previousCampaign = null;
        foreach (var group in observations.GroupBy(o => o.Tick).OrderBy(g => g.Key))
        {
            var campaigns = group.Select(o => o.Campaign).Distinct(Ord).Take(2).ToArray();
            var values = group.Select(o => o.Value).Where(v => v is not null).ToArray();
            var value = campaigns.Length == 1 && values.Select(v => v!.Number).Distinct().Take(2).Count() == 1
                ? values.OrderBy(v => v!.Coverage == MetricCoverage.Partial).First() : null;
            var campaign = campaigns.Length == 1 ? campaigns[0] : null;
            points.Add(new(group.Key, value, points.Count > 0 && value is not null
                && points[^1].Value is not null && Ord.Equals(previousCampaign, campaign)));
            previousCampaign = campaign;
        }
        return points;
    }
}
