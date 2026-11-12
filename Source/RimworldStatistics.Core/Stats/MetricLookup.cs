namespace RimworldStatistics.Core;

/// <summary>Finds existing numeric observations by stable key, without re-reading the profile.</summary>
public static class MetricLookup
{
    public static MetricDefinition? Definition(StatsView view, string key)
        => Overall(view, key)?.Definition
            ?? view.Numeric.Colonies.SelectMany(c => c.Rows).FirstOrDefault(r => r.Definition.Key == key)?.Definition;

    public static NumericRow? Overall(StatsView view, string key)
        => view.Numeric.Overall.FirstOrDefault(row => row.Definition.Key == key);

    public static NumericRow? ForColony(StatsView view, string colonyKey, string key)
        => view.Numeric.Colonies.FirstOrDefault(c => c.Key == colonyKey)?
            .Rows.FirstOrDefault(row => row.Definition.Key == key);

    public static bool IsKnownKey(StatsView view, string key)
        => BasicMetrics.Contains(key) || Definition(view, key) is not null;
}
