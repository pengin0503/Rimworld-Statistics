using System.Text;

namespace RimworldStatistics.Core;

/// <summary>
/// Presentation-only colony display names and search matching.
/// Never alters saved faction names, colony keys, campaign merging, or statistical aggregation.
/// </summary>
public static class ColonyPresentation
{
    public static string? NormalizeDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length > ViewPreferences.MaxNameLength
            ? trimmed[..ViewPreferences.MaxNameLength]
            : trimmed;
    }

    public static string? TryGetCustomName(ViewPreferences? preferences, string colonyKey, IReadOnlyList<string>? aliasKeys = null)
    {
        if (preferences?.ColonyDisplayNames is not { Count: > 0 } map)
            return null;
        if (!string.IsNullOrWhiteSpace(colonyKey) && map.TryGetValue(colonyKey, out var direct)
            && NormalizeDisplayName(direct) is { } validDirect)
            return validDirect;
        if (aliasKeys is not { Count: > 0 })
            return null;
        if (aliasKeys.Count == 1)
            return !string.IsNullOrWhiteSpace(aliasKeys[0]) && map.TryGetValue(aliasKeys[0], out var single)
                ? NormalizeDisplayName(single) : null;

        foreach (var alias in aliasKeys.Order(StringComparer.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(alias) && map.TryGetValue(alias, out var fromAlias)
                && NormalizeDisplayName(fromAlias) is { } validAlias)
                return validAlias;
        }
        return null;
    }

    public static string? TryGetCustomName(ViewPreferences? preferences, ColonyStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        return TryGetCustomName(preferences, stats.Key, stats.AliasKeys);
    }

    public static string? TryGetCustomName(ViewPreferences? preferences, Colony colony)
    {
        ArgumentNullException.ThrowIfNull(colony);
        return TryGetCustomName(preferences, colony.Key, colony.AliasKeys);
    }

    public static bool HasCustomName(ViewPreferences? preferences, ColonyStats stats)
        => TryGetCustomName(preferences, stats) is not null;

    public static bool HasCustomName(ViewPreferences? preferences, Colony colony)
        => TryGetCustomName(preferences, colony) is not null;

    public static string OriginalName(ColonyStats stats, Func<string?, string> unnamedFallback)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(unnamedFallback);
        return stats.FactionName is { Length: > 0 } name ? name : unnamedFallback(stats.WorldName);
    }

    public static string ResolveName(ColonyStats stats, ViewPreferences? preferences, Func<string?, string> unnamedFallback)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(unnamedFallback);
        return TryGetCustomName(preferences, stats) ?? OriginalName(stats, unnamedFallback);
    }

    public static bool SetCustomName(ViewPreferences preferences, string canonicalColonyKey,
        IReadOnlyCollection<string>? aliasKeys, string? displayName)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (string.IsNullOrWhiteSpace(canonicalColonyKey))
            return false;
        if (NormalizeDisplayName(displayName) is not { } normalized)
            return ClearCustomName(preferences, canonicalColonyKey, aliasKeys);

        var map = preferences.ColonyDisplayNames;
        var hasCanonical = map.TryGetValue(canonicalColonyKey, out var existing);
        var aliasPresent = aliasKeys is not null
            && aliasKeys.Any(a => !string.IsNullOrWhiteSpace(a) && a != canonicalColonyKey && map.ContainsKey(a));
        if (!hasCanonical && !aliasPresent && map.Count >= ViewPreferences.MaxColonyKeys)
            return false;

        var changed = false;
        if (aliasKeys is not null)
        {
            foreach (var alias in aliasKeys)
            {
                if (!string.IsNullOrWhiteSpace(alias) && alias != canonicalColonyKey && map.Remove(alias))
                    changed = true;
            }
        }
        if (!hasCanonical || !string.Equals(existing, normalized, StringComparison.Ordinal))
        {
            map[canonicalColonyKey] = normalized;
            changed = true;
        }
        return changed;
    }

    public static bool ClearCustomName(ViewPreferences preferences, string canonicalColonyKey,
        IReadOnlyCollection<string>? aliasKeys = null)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var map = preferences.ColonyDisplayNames;
        var changed = !string.IsNullOrWhiteSpace(canonicalColonyKey) && map.Remove(canonicalColonyKey);
        if (aliasKeys is not null)
        {
            foreach (var alias in aliasKeys)
            {
                if (!string.IsNullOrWhiteSpace(alias) && map.Remove(alias))
                    changed = true;
            }
        }
        return changed;
    }

    public static bool MatchesSearch(ColonyStats stats, ViewPreferences? preferences,
        Func<string?, string> unnamedFallback, string? query)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(unnamedFallback);
        if (string.IsNullOrWhiteSpace(query))
            return true;
        var term = query.Trim();
        var displayName = ResolveName(stats, preferences, unnamedFallback);
        return ContainsTerm(displayName, term)
            || ContainsTerm(stats.FactionName, term)
            || ContainsTerm(stats.WorldName, term);
    }

    public static string Signature(ViewPreferences? preferences)
    {
        if (preferences?.ColonyDisplayNames is not { Count: > 0 } map)
            return "";
        var sb = new StringBuilder();
        foreach (var (key, value) in map.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var safeValue = value ?? "";
            sb.Append(key.Length).Append(':').Append(key).Append('=').Append(safeValue.Length).Append(':').Append(safeValue).Append(';');
        }
        return sb.ToString();
    }

    private static bool ContainsTerm(string? text, string term)
        => !string.IsNullOrEmpty(text)
            && (text.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                || text.Contains(term, StringComparison.OrdinalIgnoreCase));
}
