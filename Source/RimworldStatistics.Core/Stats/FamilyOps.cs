namespace RimworldStatistics.Core;

/// <summary>
/// The operations families apply to their totals. Every keyed collection (a breakdown, a distinct set, an active tracker)
/// holds at most <see cref="KeyCap"/> keys (numeric expansion design §4): past the cap, existing keys and plain counts go on,
/// a new key is dropped and the collection is marked as overflowed, so outputs that depend on it become partial.
/// </summary>
public static class FamilyOps
{
    public const int KeyCap = 4096;

    /// <summary>Prefix of the marker accumulator of an overflowed active tracker.</summary>
    public const string TrackerOverflowPrefix = "~";

    public static Accumulator Acc(this FamilyState s, string name)
    {
        if (!s.Metrics.TryGetValue(name, out var a))
            s.Metrics[name] = a = new Accumulator();
        return a;
    }

    public static Accumulator? Peek(this FamilyState s, string name) => s.Metrics.GetValueOrDefault(name);

    public static void Count(this FamilyState s, string name, long delta = 1)
    {
        var a = s.Acc(name);
        a.N = (a.N ?? 0) + delta;
    }

    /// <summary>Adds to one key of a breakdown; returns false when the key was new and the breakdown is full.</summary>
    public static bool CountBy(this FamilyState s, string name, string key, double delta = 1)
    {
        var a = s.Acc(name);
        a.By ??= new Dictionary<string, double>(StringComparer.Ordinal);
        if (a.By.TryGetValue(key, out var v))
        {
            a.By[key] = v + delta;
            return true;
        }
        if (a.By.Count >= KeyCap)
        {
            a.Overflow = true;
            return false;
        }
        a.By[key] = delta;
        return true;
    }

    /// <summary>One sample: count, sum and extremes (the first key reaching an extreme keeps it on a tie).</summary>
    public static void Observe(this FamilyState s, string name, double value, string? key = null)
    {
        if (!double.IsFinite(value))
            return;
        var a = s.Acc(name);
        a.N = (a.N ?? 0) + 1;
        a.Sum = (a.Sum ?? 0) + value;
        if (a.Min is null || value < a.Min)
        {
            a.Min = value;
            a.MinKey = key;
        }
        if (a.Max is null || value > a.Max)
        {
            a.Max = value;
            a.MaxKey = key;
        }
    }

    /// <summary>Adds a key to a distinct set; true when it was not there before (false also when the set is full).</summary>
    public static bool Distinct(this FamilyState s, string name, string key)
    {
        var a = s.Acc(name);
        a.By ??= new Dictionary<string, double>(StringComparer.Ordinal);
        if (a.By.ContainsKey(key))
            return false;
        if (a.By.Count >= KeyCap)
        {
            a.Overflow = true;
            return false;
        }
        a.By[key] = 1;
        return true;
    }

    /// <summary>Records an event tick: the first stays, the last moves.</summary>
    public static void Mark(this FamilyState s, string name, int tick)
    {
        var a = s.Acc(name);
        a.First ??= tick;
        a.Last = tick;
    }

    /// <summary>An event at <paramref name="tick"/>: the gap since the previous event is a sample (count, sum, extremes).</summary>
    public static void Gap(this FamilyState s, string name, int tick)
    {
        var a = s.Acc(name);
        if (a.Last is { } last && tick >= last)
        {
            double gap = tick - last;
            a.N = (a.N ?? 0) + 1;
            a.Sum = (a.Sum ?? 0) + gap;
            if (a.Min is null || gap < a.Min)
                a.Min = gap;
            if (a.Max is null || gap > a.Max)
                a.Max = gap;
        }
        a.First ??= tick;
        a.Last = tick;
    }

    public static bool Track(this FamilyState s, string tracker, string id, long value)
    {
        if (!s.Active.TryGetValue(tracker, out var map))
            s.Active[tracker] = map = new Dictionary<string, long>(StringComparer.Ordinal);
        if (!map.ContainsKey(id) && map.Count >= KeyCap)
        {
            s.Acc(TrackerOverflowPrefix + tracker).Overflow = true;
            return false;
        }
        map[id] = value;
        return true;
    }

    public static long? Tracked(this FamilyState s, string tracker, string id)
        => s.Active.TryGetValue(tracker, out var map) && map.TryGetValue(id, out var v) ? v : null;

    public static long? Untrack(this FamilyState s, string tracker, string id)
    {
        if (!s.Active.TryGetValue(tracker, out var map) || !map.Remove(id, out var v))
            return null;
        if (map.Count == 0)
            s.Active.Remove(tracker);
        return v;
    }

    public static bool TrackerOverflowed(this FamilyState s, string tracker)
        => s.Peek(TrackerOverflowPrefix + tracker)?.Overflow == true;
}
