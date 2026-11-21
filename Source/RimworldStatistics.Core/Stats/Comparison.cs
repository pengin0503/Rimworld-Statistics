namespace RimworldStatistics.Core;

public abstract record ComparisonTarget;
public sealed record ColonyTarget(string ColonyKey) : ComparisonTarget;
public sealed record ScopeTarget(ViewScope Scope) : ComparisonTarget;

public sealed record ComparisonRow(string MetricKey, IReadOnlyList<NumericValue?> Values);

public static class Comparison
{
    public const int MaxColumns = 4;

    public static IReadOnlyList<ComparisonRow> Build(
        IReadOnlyList<(ComparisonTarget Target, StatsView View)> columns, IEnumerable<string> metricKeys, bool perDay = false)
    {
        if (columns.Count > MaxColumns)
            throw new ArgumentOutOfRangeException(nameof(columns));
        var indexes = columns.Select(column =>
        {
            if (perDay && column.Target is ScopeTarget)
                return new Dictionary<string, NumericValue>(StringComparer.Ordinal);

            var rows = column.Target switch
            {
                ScopeTarget => column.View.Numeric.Overall,
                ColonyTarget target => column.View.Numeric.Colonies.FirstOrDefault(c => c.Key == target.ColonyKey)?.Rows ?? [],
                _ => throw new ArgumentException("Unrecognized comparison target.", nameof(columns)),
            };
            if (!perDay)
            {
                var dict = new Dictionary<string, NumericValue>(rows.Count, StringComparer.Ordinal);
                foreach (var row in rows)
                    dict.TryAdd(row.Definition.Key, row.Value);
                return dict;
            }

            var rateDict = new Dictionary<string, NumericValue>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                if (MetricRates.PerDay(row.Definition, row.Value) is { } rate)
                {
                    rateDict[row.Definition.Key] = new NumericValue
                    {
                        Number = rate.PerDay,
                        Coverage = MetricCoverage.Complete,
                        Evidence = row.Value.Evidence,
                        SinceTick = row.Value.SinceTick,
                        ThroughTick = row.Value.ThroughTick,
                    };
                }
            }
            return rateDict;
        }).ToArray();
        return metricKeys.Distinct(StringComparer.Ordinal).Select(key => new ComparisonRow(key,
            indexes.Select(index => index.GetValueOrDefault(key) is { Coverage: not MetricCoverage.Unknown } value
                ? value : null).ToArray())).ToArray();
    }

    public static IEnumerable<ComparisonRow> Sort(IEnumerable<ComparisonRow> rows, int column, bool descending)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        static double? Number(ComparisonRow row, int column)
        {
            if (column >= row.Values.Count)
                return null;
            return row.Values[column] is { Known: true, Number: { } n } && double.IsFinite(n) ? n : null;
        }
        var byMissing = rows.OrderBy(row => Number(row, column) is null);
        var ordered = descending
            ? byMissing.ThenByDescending(row => Number(row, column))
            : byMissing.ThenBy(row => Number(row, column));
        return ordered.ThenBy(row => row.MetricKey, StringComparer.Ordinal);
    }
}
