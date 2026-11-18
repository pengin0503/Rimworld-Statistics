namespace RimworldStatistics.Core;

/// <summary>Selectable aggregation scope; never changes which numeric observations attach to which colony.</summary>
public sealed class ViewScope
{
    public static ViewScope All { get; } = new("", null, null);

    private readonly HashSet<string>? _members;
    public SmartGroupSpec? SmartGroup { get; }
    public string CacheKey { get; }
    public bool IsAll => _members is null && SmartGroup is null;

    private ViewScope(string cacheKey, HashSet<string>? members, SmartGroupSpec? smartGroup)
    {
        CacheKey = cacheKey;
        _members = members;
        SmartGroup = smartGroup;
    }

    public static ViewScope ForGroup(ColonyGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        var members = new HashSet<string>(group.ColonyKeys ?? new List<string>(), StringComparer.Ordinal);
        var sorted = members.Order(StringComparer.Ordinal);
        // Length-delimit each part so different sets of keys cannot accidentally share a cache entry.
        var key = string.Concat("group:", group.Id.Length, ":", group.Id, ":",
            string.Concat(sorted.Select(s => s.Length + ":" + s)));
        return new ViewScope(key, members, null);
    }

    public static ViewScope ForSmartGroup(SmartGroupSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return new ViewScope(spec.Id, null, spec);
    }

    public bool Contains(Colony colony)
    {
        ArgumentNullException.ThrowIfNull(colony);
        if (SmartGroup is not null)
            return SmartGroupEvaluator.Matches(SmartGroup, StatsCalculator.CalculateColony(colony, new StatsOptions()));
        return _members is null
            || _members.Contains(colony.Key)
            || colony.AliasKeys.Any(_members.Contains);
    }

    /// <summary>Call only on fresh colonies constructed for this specific view.</summary>
    public void Apply(IReadOnlyList<Colony> colonies)
    {
        if (SmartGroup is not null)
        {
            SmartGroupEvaluator.Apply(SmartGroup, null, colonies, new StatsOptions());
            return;
        }
        foreach (var colony in colonies)
            colony.OutOfScope = !Contains(colony);
    }

    /// <summary>Applies either a static manual group or a dynamic smart group with full profile/options context.</summary>
    public void Apply(
        Profile profile,
        IReadOnlyList<Colony> colonies,
        StatsOptions options,
        IReadOnlyDictionary<string, string>? startingBiomes = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(colonies);
        ArgumentNullException.ThrowIfNull(options);
        if (SmartGroup is not null)
        {
            SmartGroupEvaluator.Apply(SmartGroup, profile, colonies, options, startingBiomes);
            return;
        }
        foreach (var colony in colonies)
            colony.OutOfScope = !Contains(colony);
    }
}
