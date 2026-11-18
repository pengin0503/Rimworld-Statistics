using System.Text;

namespace RimworldStatistics.Core;

public enum ColonyHistoryFavoriteStatus
{
    Openable,
    ColonyMissing,
    ColonyExcluded,
    ColonyOutOfScope,
    MetricUnknown,
    MetricNoHistory,
    NoMeasuredValue,
}

public sealed record ResolvedColonyHistoryFavorite(
    string StoredColonyKey,
    IReadOnlyList<string> MatchedStoredColonyKeys,
    string CanonicalColonyKey,
    string MetricKey,
    ColonyStats? Colony,
    MetricDefinition? Definition,
    MetricDefinition? RequestedDefinition,
    NumericValue? ComparisonValue,
    ColonyHistoryFavoriteStatus Status)
{
    public bool CanOpen => Status == ColonyHistoryFavoriteStatus.Openable
        && Definition is not null && ComparisonValue is not null;
}

/// <summary>
/// Resolves and mutates presentation-only (colonyKey, metricKey) line-chart favorites in <see cref="ViewPreferences"/>.
/// Handles legacy colony aliases after merges, duplicate entries, excluded/out-of-scope colonies, and disabled MOD metrics.
/// </summary>
public static class ColonyHistoryFavorites
{
    public static bool SupportsColonyHistory(StatsView view, string metricKey)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (string.IsNullOrWhiteSpace(metricKey))
            return false;
        if (BasicMetrics.Contains(metricKey))
            return BasicMetrics.HasSaveHistory(metricKey);

        var def = FindDefinition(view, metricKey);
        if (def is null || !SupportsColonyHistoryDefinition(def))
            return false;

        var sourceKey = MetricChartSeries.ColonyComparisonKey(def);
        return !view.Numeric.Colonies.Any(c =>
            c.Rows.FirstOrDefault(r => r.Definition.Key == sourceKey)?.Value.BreakdownOnly == true);
    }

    public static IReadOnlyList<string> CandidateMetricKeys(StatsView view, string? colonyKey = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        var keys = new List<string>(BasicMetrics.SaveHistoryKeys);
        var seen = new HashSet<string>(keys, StringComparer.Ordinal);

        var numericRows = colonyKey is not null
            ? (view.Numeric.Colonies.FirstOrDefault(c => c.Key == colonyKey)?.Rows
                ?? view.Numeric.Colonies.SelectMany(c => c.Rows))
            : view.Numeric.Colonies.SelectMany(c => c.Rows);

        foreach (var row in numericRows.Concat(view.Numeric.Overall))
        {
            var def = row.Definition;
            if (!SupportsColonyHistoryDefinition(def) || CrossColonyCatalog.IsDerived(def))
                continue;
            if (row.Value.BreakdownOnly)
                continue;
            if (view.Numeric.Colonies.Any(c =>
                    c.Rows.FirstOrDefault(r => r.Definition.Key == def.Key)?.Value.BreakdownOnly == true))
                continue;
            if (seen.Add(def.Key))
                keys.Add(def.Key);
        }
        return keys;
    }

    public static IReadOnlyList<ResolvedColonyHistoryFavorite> ResolveAll(StatsView view, ViewPreferences? preferences)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (preferences?.FavoriteColonyMetrics is not { Count: > 0 } items)
            return [];

        // Map both canonical and alias colony keys to their current ColonyStats.
        var colonyByAnyKey = new Dictionary<string, ColonyStats>(StringComparer.Ordinal);
        for (var i = 0; i < view.Stats.Colonies.Count; i++)
        {
            var stats = view.Stats.Colonies[i];
            colonyByAnyKey[stats.Key] = stats;
            foreach (var alias in stats.AliasKeys)
                colonyByAnyKey.TryAdd(alias, stats);
        }
        // Fallback if a test constructs StatsView with Colony.AliasKeys only on view.Colonies.
        for (var i = 0; i < view.Colonies.Count; i++)
        {
            var col = view.Colonies[i];
            if (!colonyByAnyKey.TryGetValue(col.Key, out var stats))
                continue;
            foreach (var alias in col.AliasKeys)
                colonyByAnyKey.TryAdd(alias, stats);
        }

        var groupedIndex = new Dictionary<(string CanonicalColonyKey, string MetricKey), int>();
        var storedKeysPerEntry = new List<List<string>>(items.Count);
        var drafts = new List<(string StoredKey, string CanonicalKey, string MetricKey, ColonyStats? Colony)>(items.Count);

        foreach (var item in items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.ColonyKey) || string.IsNullOrWhiteSpace(item.MetricKey))
                continue;
            colonyByAnyKey.TryGetValue(item.ColonyKey, out var colony);
            var canonicalKey = colony?.Key ?? item.ColonyKey;
            var groupKey = (canonicalKey, item.MetricKey);
            if (groupedIndex.TryGetValue(groupKey, out var existingIndex))
            {
                var keys = storedKeysPerEntry[existingIndex];
                if (!keys.Contains(item.ColonyKey, StringComparer.Ordinal))
                    keys.Add(item.ColonyKey);
                // Prefer showing canonical key as StoredColonyKey if present among merged duplicates.
                if (item.ColonyKey == canonicalKey && drafts[existingIndex].StoredKey != canonicalKey)
                    drafts[existingIndex] = drafts[existingIndex] with { StoredKey = canonicalKey };
                continue;
            }

            groupedIndex[groupKey] = drafts.Count;
            storedKeysPerEntry.Add([item.ColonyKey]);
            drafts.Add((item.ColonyKey, canonicalKey, item.MetricKey, colony));
        }

        var result = new ResolvedColonyHistoryFavorite[drafts.Count];
        for (var i = 0; i < drafts.Count; i++)
        {
            var draft = drafts[i];
            result[i] = ResolveSingle(view, draft.StoredKey, storedKeysPerEntry[i], draft.CanonicalKey, draft.MetricKey, draft.Colony);
        }
        return result;
    }

    public static bool IsFavorite(ViewPreferences? preferences, string canonicalColonyKey,
        IReadOnlyCollection<string>? aliasKeys, string metricKey)
    {
        if (preferences?.FavoriteColonyMetrics is not { Count: > 0 } list
            || string.IsNullOrWhiteSpace(canonicalColonyKey) || string.IsNullOrWhiteSpace(metricKey))
            return false;
        foreach (var f in list)
        {
            if (!string.Equals(f.MetricKey, metricKey, StringComparison.Ordinal))
                continue;
            if (string.Equals(f.ColonyKey, canonicalColonyKey, StringComparison.Ordinal)
                || (aliasKeys is not null && aliasKeys.Contains(f.ColonyKey, StringComparer.Ordinal)))
                return true;
        }
        return false;
    }

    public static bool Toggle(ViewPreferences preferences, string canonicalColonyKey,
        IReadOnlyCollection<string>? aliasKeys, string metricKey)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (string.IsNullOrWhiteSpace(canonicalColonyKey) || string.IsNullOrWhiteSpace(metricKey))
            return false;

        if (Remove(preferences, canonicalColonyKey, aliasKeys, metricKey))
            return true;

        if (preferences.FavoriteColonyMetrics.Count >= ViewPreferences.MaxFavoriteCount)
            return false;

        preferences.FavoriteColonyMetrics.Add(new ColonyMetricFavorite
        {
            ColonyKey = canonicalColonyKey,
            MetricKey = metricKey,
        });
        return true;
    }

    public static bool Remove(ViewPreferences preferences, ResolvedColonyHistoryFavorite favorite)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(favorite);
        return Remove(preferences, favorite.CanonicalColonyKey, favorite.MatchedStoredColonyKeys, favorite.MetricKey);
    }

    public static bool Remove(ViewPreferences preferences, string canonicalColonyKey,
        IReadOnlyCollection<string>? aliasKeys, string metricKey)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (string.IsNullOrWhiteSpace(metricKey))
            return false;
        var removed = preferences.FavoriteColonyMetrics.RemoveAll(f =>
            string.Equals(f.MetricKey, metricKey, StringComparison.Ordinal)
            && (string.Equals(f.ColonyKey, canonicalColonyKey, StringComparison.Ordinal)
                || (aliasKeys is not null && aliasKeys.Contains(f.ColonyKey, StringComparer.Ordinal))));
        return removed > 0;
    }

    public static bool Move(ViewPreferences preferences, IReadOnlyList<ResolvedColonyHistoryFavorite> resolved,
        int index, int delta)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(resolved);
        var target = index + delta;
        if (index < 0 || index >= resolved.Count || target < 0 || target >= resolved.Count || delta == 0)
            return false;

        var reordered = resolved.ToArray();
        (reordered[index], reordered[target]) = (reordered[target], reordered[index]);

        preferences.FavoriteColonyMetrics = reordered
            .Take(ViewPreferences.MaxFavoriteCount)
            .Select(r => new ColonyMetricFavorite
            {
                ColonyKey = r.Colony is not null ? r.CanonicalColonyKey : r.StoredColonyKey,
                MetricKey = r.MetricKey,
            })
            .ToList();
        return true;
    }

    public static string Signature(ViewPreferences? preferences)
    {
        if (preferences?.FavoriteColonyMetrics is not { Count: > 0 } list)
            return "";
        var sb = new StringBuilder();
        foreach (var item in list)
            sb.Append(item.ColonyKey.Length).Append(':').Append(item.ColonyKey)
                .Append('/').Append(item.MetricKey.Length).Append(':').Append(item.MetricKey).Append(';');
        return sb.ToString();
    }

    private static ResolvedColonyHistoryFavorite ResolveSingle(StatsView view, string storedKey,
        IReadOnlyList<string> matchedStoredKeys, string canonicalKey, string metricKey, ColonyStats? colony)
    {
        if (BasicMetrics.Contains(metricKey))
        {
            var basicDef = BasicMetrics.Definition(metricKey);
            if (colony is null)
                return new(storedKey, matchedStoredKeys, canonicalKey, metricKey, null, basicDef, basicDef, null,
                    ColonyHistoryFavoriteStatus.ColonyMissing);
            if (!BasicMetrics.HasSaveHistory(metricKey))
                return new(storedKey, matchedStoredKeys, canonicalKey, metricKey, colony, basicDef, basicDef, null,
                    ColonyHistoryFavoriteStatus.MetricNoHistory);
            if (colony.Excluded)
                return new(storedKey, matchedStoredKeys, canonicalKey, metricKey, colony, basicDef, basicDef, null,
                    ColonyHistoryFavoriteStatus.ColonyExcluded);
            if (colony.OutOfScope)
                return new(storedKey, matchedStoredKeys, canonicalKey, metricKey, colony, basicDef, basicDef, null,
                    ColonyHistoryFavoriteStatus.ColonyOutOfScope);
            if (BasicMetrics.Value(colony, metricKey) is { } number && double.IsFinite(number))
            {
                var comparison = new NumericValue { Number = number, Coverage = MetricCoverage.SnapshotOnly };
                return new(storedKey, matchedStoredKeys, canonicalKey, metricKey, colony, basicDef, basicDef, comparison,
                    ColonyHistoryFavoriteStatus.Openable);
            }
            return new(storedKey, matchedStoredKeys, canonicalKey, metricKey, colony, basicDef, basicDef, null,
                ColonyHistoryFavoriteStatus.NoMeasuredValue);
        }

        var requested = FindDefinition(view, metricKey);
        if (requested is null)
        {
            return new(storedKey, matchedStoredKeys, canonicalKey, metricKey, colony, null, null, null,
                colony is null ? ColonyHistoryFavoriteStatus.ColonyMissing : ColonyHistoryFavoriteStatus.MetricUnknown);
        }

        var sourceKey = SupportsColonyHistoryDefinition(requested)
            ? MetricChartSeries.ColonyComparisonKey(requested)
            : requested.Key;
        var sourceDef = sourceKey == requested.Key
            ? requested
            : FindDefinition(view, sourceKey) ?? requested;

        if (colony is null)
            return new(storedKey, matchedStoredKeys, canonicalKey, metricKey, null, sourceDef, requested, null,
                ColonyHistoryFavoriteStatus.ColonyMissing);

        if (!SupportsColonyHistoryDefinition(requested))
            return new(storedKey, matchedStoredKeys, canonicalKey, metricKey, colony, sourceDef, requested, null,
                ColonyHistoryFavoriteStatus.MetricNoHistory);

        var row = MetricLookup.ForColony(view, colony.Key, sourceDef.Key);
        if (row?.Value.BreakdownOnly == true)
            return new(storedKey, matchedStoredKeys, canonicalKey, metricKey, colony, sourceDef, requested, null,
                ColonyHistoryFavoriteStatus.MetricNoHistory);

        if (colony.Excluded)
            return new(storedKey, matchedStoredKeys, canonicalKey, metricKey, colony, sourceDef, requested, null,
                ColonyHistoryFavoriteStatus.ColonyExcluded);
        if (colony.OutOfScope)
            return new(storedKey, matchedStoredKeys, canonicalKey, metricKey, colony, sourceDef, requested, null,
                ColonyHistoryFavoriteStatus.ColonyOutOfScope);

        if (row is not null
            && row.Value.Coverage is not (MetricCoverage.Unknown or MetricCoverage.NotApplicable)
            && row.Value.Number is { } measured && double.IsFinite(measured))
        {
            return new(storedKey, matchedStoredKeys, canonicalKey, metricKey, colony, sourceDef, requested, row.Value,
                ColonyHistoryFavoriteStatus.Openable);
        }

        return new(storedKey, matchedStoredKeys, canonicalKey, metricKey, colony, sourceDef, requested, null,
            ColonyHistoryFavoriteStatus.NoMeasuredValue);
    }

    private static MetricDefinition? FindDefinition(StatsView view, string key)
        => MetricLookup.Definition(view, key) ?? MetricRegistry.Find(key);

    private static bool SupportsColonyHistoryDefinition(MetricDefinition definition)
        => CrossColonyCatalog.IsDerived(definition)
            ? !definition.Key.StartsWith(CrossColonyCatalog.TopPrefix, StringComparison.Ordinal)
                && !definition.Key.StartsWith(CrossColonyCatalog.SumPrefix, StringComparison.Ordinal)
            : definition.Overall != OverallAggregation.Ranking;
}
