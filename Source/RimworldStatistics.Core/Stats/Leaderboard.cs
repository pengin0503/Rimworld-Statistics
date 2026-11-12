namespace RimworldStatistics.Core;

/// <summary>A previously collected colony value and its rank; no new statistics are stored.</summary>
public sealed record LeaderboardEntry(
    int Rank, string ColonyKey, double Value, MetricCoverage Coverage,
    MetricEvidence Evidence, int? ThroughTick);

public sealed record Leaderboard(
    string MetricKey, bool Descending, IReadOnlyList<LeaderboardEntry> Entries,
    int Measured, int Targets, bool HasPartial);

public static class LeaderboardBuilder
{
    private sealed record Candidate(
        string Key, double Value, MetricCoverage Coverage, MetricEvidence Evidence, int? ThroughTick);

    public static IReadOnlyList<string> RankableKeys(StatsView view)
    {
        var keys = new List<string>(BasicMetrics.Keys);
        var existing = keys.ToHashSet(StringComparer.Ordinal);
        foreach (var c in view.Numeric.Colonies)
            foreach (var row in c.Rows)
                if (row.Definition.Overall != OverallAggregation.Ranking
                    && !CrossColonyCatalog.IsDerived(row.Definition)
                    && existing.Add(row.Definition.Key))
                    keys.Add(row.Definition.Key);
        return keys;
    }

    public static Leaderboard Build(StatsView view, string metricKey, bool descending)
    {
        var candidates = new List<Candidate>();
        int targets = 0;
        if (BasicMetrics.Contains(metricKey))
        {
            foreach (var c in view.Stats.Colonies.Where(c => !c.Excluded && !c.OutOfScope))
            {
                targets++;
                if (BasicMetrics.Value(c, metricKey) is not { } value || !double.IsFinite(value))
                    continue;
                candidates.Add(new Candidate(c.Key, value, MetricCoverage.SnapshotOnly,
                    MetricEvidence.UnknownLegacy, c.HasObservedPoint ? c.MaxTicks : null));
            }
        }
        else if (MetricLookup.Definition(view, metricKey) is { } definition
            && definition.Overall != OverallAggregation.Ranking
            && !CrossColonyCatalog.IsDerived(definition))
        {
            foreach (var c in view.Numeric.Colonies.Where(c => !c.Excluded && !c.OutOfScope))
            {
                var row = c.Rows.FirstOrDefault(r => r.Definition.Key == metricKey);
                if (row?.Value.Coverage == MetricCoverage.NotApplicable)
                    continue;
                targets++;
                if (row is null || !row.Value.Known || row.Value.Number is not { } value
                    || !double.IsFinite(value))
                    continue;
                candidates.Add(new Candidate(c.Key, value, row.Value.Coverage,
                    row.Value.Evidence, row.Value.ThroughTick));
            }
        }

        var sorted = descending
            ? candidates.OrderByDescending(c => c.Value).ThenBy(c => c.Key, StringComparer.Ordinal).ToArray()
            : candidates.OrderBy(c => c.Value).ThenBy(c => c.Key, StringComparer.Ordinal).ToArray();
        var results = new List<LeaderboardEntry>(sorted.Length);
        var rank = 0;
        for (var i = 0; i < sorted.Length; i++)
        {
            var c = sorted[i];
            if (i == 0 || c.Value != sorted[i - 1].Value)
                rank = i + 1;
            results.Add(new LeaderboardEntry(rank, c.Key, c.Value, c.Coverage, c.Evidence, c.ThroughTick));
        }

        var partial = candidates.Any(c => c.Coverage == MetricCoverage.Partial) || candidates.Count < targets;
        return new Leaderboard(metricKey, descending, results, results.Count, targets, partial);
    }
}
