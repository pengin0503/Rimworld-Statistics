namespace RimworldStatistics.Core;

/// <summary>Selectable aggregation scope; never changes which numeric observations attach to which colony.</summary>
public sealed class ViewScope
{
    public static ViewScope All { get; } = new("", null);

    private readonly HashSet<string>? _members;
    public string CacheKey { get; }
    public bool IsAll => _members is null;

    private ViewScope(string cacheKey, HashSet<string>? members)
    {
        CacheKey = cacheKey;
        _members = members;
    }

    public static ViewScope ForGroup(ColonyGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        var members = new HashSet<string>(group.ColonyKeys ?? new List<string>(), StringComparer.Ordinal);
        var sorted = members.Order(StringComparer.Ordinal);
        // Length-delimit each part so different sets of keys cannot accidentally share a cache entry.
        var key = string.Concat("group:", group.Id.Length, ":", group.Id, ":",
            string.Concat(sorted.Select(s => s.Length + ":" + s)));
        return new ViewScope(key, members);
    }

    public bool Contains(Colony colony) => _members is null
        || _members.Contains(colony.Key)
        || colony.AliasKeys.Any(_members.Contains);

    /// <summary>Call only on fresh colonies constructed for this specific view.</summary>
    public void Apply(IReadOnlyList<Colony> colonies)
    {
        foreach (var colony in colonies)
            colony.OutOfScope = !Contains(colony);
    }
}
