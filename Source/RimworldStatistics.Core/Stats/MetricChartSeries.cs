namespace RimworldStatistics.Core;

/// <summary>
/// A chart projects the already-computed numeric view. It never reinterprets checkpoint history,
/// fills missing readings with zero, or changes which colonies participate in the current scope.
/// </summary>
public sealed record MetricChartBar(string Key, NumericValue Value);

public static class MetricChartSeries
{
    /// <summary>One comparable observation per included colony, ordered from highest value to lowest.</summary>
    public static IReadOnlyList<MetricChartBar> ByColony(NumericView view, string metricKey)
    {
        ArgumentNullException.ThrowIfNull(view);
        return Sort(view.Colonies
            .Where(c => !c.Excluded && !c.OutOfScope)
            .Select(c => new { c.Key, Row = c.Rows.FirstOrDefault(r => r.Definition.Key == metricKey) })
            .Where(c => c.Row is not null && Measured(c.Row.Value))
            .Select(c => new MetricChartBar(c.Key, c.Row!.Value)));
    }

    /// <summary>
    /// The colony comparison for a definition. A derived cross-colony maximum, minimum or sum
    /// is computed only for all colonies; its per-colony inputs are the rows of the source metric.
    /// </summary>
    public static IReadOnlyList<MetricChartBar> ByColony(NumericView view, MetricDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return ByColony(view, ColonyComparisonKey(definition));
    }

    /// <summary>
    /// The per-colony row key that a colony comparison reads. A "most common type" ranking keeps
    /// its own key: its source total is a different quantity from the ranked type.
    /// </summary>
    public static string ColonyComparisonKey(MetricDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return CrossColonyCatalog.IsDerived(definition)
            && !definition.Key.StartsWith(CrossColonyCatalog.TopPrefix, StringComparison.Ordinal)
                ? definition.StoredKey : definition.Key;
    }

    /// <summary>Basic Overview values use the same inclusion and evidence rules as BasicMetrics.</summary>
    public static IReadOnlyList<MetricChartBar> BasicByColony(StatsResult stats, string metricKey)
    {
        ArgumentNullException.ThrowIfNull(stats);
        if (!BasicMetrics.Contains(metricKey))
            return [];
        return Sort(stats.Colonies
            .Where(c => !c.Excluded && !c.OutOfScope)
            .Select(c => new { c.Key, Number = BasicMetrics.Value(c, metricKey) })
            .Where(c => c.Number is { } number && double.IsFinite(number))
            .Select(c => new MetricChartBar(c.Key, new NumericValue
            {
                Number = c.Number,
                Coverage = MetricCoverage.SnapshotOnly,
            })));
    }

    /// <summary>Saved per-category contributions, not a series of successive save-time measurements.</summary>
    public static IReadOnlyList<MetricChartBar> Breakdowns(NumericView view, string metricKey, string? colonyKey = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        var rows = colonyKey is null
            ? view.Overall
            : view.Colonies.FirstOrDefault(c => c.Key == colonyKey)?.Rows;
        var row = rows?.FirstOrDefault(r => r.Definition.Key == metricKey);
        return row is null ? [] : Sort(row.Breakdowns
            .Where(r => Measured(r.Value))
            .Select(r => new MetricChartBar(r.Breakdown, r.Value)));
    }

    /// <summary>Fallback for aggregate-only metrics that have neither comparable colonies nor breakdowns.</summary>
    public static IReadOnlyList<MetricChartBar> Overall(NumericView view, string metricKey)
    {
        ArgumentNullException.ThrowIfNull(view);
        var row = view.Overall.FirstOrDefault(r => r.Definition.Key == metricKey);
        return row is not null && Measured(row.Value) ? [new MetricChartBar(metricKey, row.Value)] : [];
    }

    private static bool Measured(NumericValue value)
        => value.Coverage is not (MetricCoverage.Unknown or MetricCoverage.NotApplicable)
            && value.Number is { } number && double.IsFinite(number);

    private static IReadOnlyList<MetricChartBar> Sort(IEnumerable<MetricChartBar> bars)
        => bars.OrderByDescending(b => b.Value.Number!.Value).ThenBy(b => b.Key, StringComparer.Ordinal).ToArray();
}
