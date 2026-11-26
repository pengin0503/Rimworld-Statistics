namespace RimworldStatistics.Core;

/// <summary>
/// Pure, game-agnostic helpers for short "recently used" lists of keys.
/// Lists are kept most-recent-first, deduplicated and bounded. Keys are ordinary strings:
/// the caller decides which keys are valid for its current view.
/// </summary>
public static class RecentList
{
    /// <summary>Default cap: five entries, as used by both picker dialogs.</summary>
    public const int DefaultCapacity = 5;

    /// <summary>
    /// Records <paramref name="key"/> as the most recent entry, dropping an older duplicate.
    /// Returns true only when the list actually changed.
    /// </summary>
    public static bool Add(List<string> list, string key, int capacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(list);
        if (string.IsNullOrWhiteSpace(key) || capacity <= 0)
            return false;
        var normalized = key.Trim();
        var before = list.ToArray();

        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (string.Equals(list[i], normalized, StringComparison.Ordinal))
                list.RemoveAt(i);
        }
        list.Insert(0, normalized);
        if (list.Count > capacity)
            list.RemoveRange(capacity, list.Count - capacity);
        return !before.SequenceEqual(list, StringComparer.Ordinal);
    }

    /// <summary>Removes every occurrence of <paramref name="key"/>; returns false when absent.</summary>
    public static bool Remove(List<string> list, string? key)
    {
        ArgumentNullException.ThrowIfNull(list);
        if (string.IsNullOrWhiteSpace(key))
            return false;
        var normalized = key.Trim();
        return list.RemoveAll(item => string.Equals(item, normalized, StringComparison.Ordinal)) > 0;
    }

    /// <summary>
    /// Returns the most-recent-first, deduplicated, non-empty keys, limited to
    /// <paramref name="capacity"/> entries. When <paramref name="existing"/> is supplied, keys
    /// no longer present in the current view are omitted.
    /// </summary>
    public static IReadOnlyList<string> Visible(IEnumerable<string>? keys, IReadOnlySet<string>? existing = null,
        int capacity = DefaultCapacity)
    {
        if (keys is null || capacity <= 0)
            return [];
        var result = new List<string>(Math.Min(capacity, 64));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in keys)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var key = raw.Trim();
            if (existing is not null && !existing.Contains(key))
                continue;
            if (!seen.Add(key))
                continue;
            result.Add(key);
            if (result.Count >= capacity)
                break;
        }
        return result;
    }

    /// <summary>Cleans a persisted list: trims, removes blanks and duplicates, enforces the cap.</summary>
    public static List<string> Normalized(IEnumerable<string>? keys, int capacity = DefaultCapacity)
    {
        return Visible(keys, null, capacity).ToList();
    }
}
