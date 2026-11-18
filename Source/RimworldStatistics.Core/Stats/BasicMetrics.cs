namespace RimworldStatistics.Core;

/// <summary>Stable, presentation-only references to existing basic lifetime statistics.</summary>
public static class BasicMetrics
{
    public const string Prefix = "basic.";
    public const string Days = "basic.days";
    public const string RealPlayHours = "basic.real_play_hours";
    public const string PeakWealth = "basic.peak_wealth";
    public const string LastWealth = "basic.last_wealth";
    public const string Raids = "basic.raids";
    public const string Threats = "basic.threats";
    public const string Killed = "basic.killed";
    public const string Departed = "basic.departed";
    public const string MaxPopulation = "basic.max_population";
    public const string Endings = "basic.endings";
    public const string Saves = "basic.saves";

    public static IReadOnlyList<string> Keys { get; } =
    [
        Days, RealPlayHours, PeakWealth, LastWealth, Raids, Threats, Killed,
        Departed, MaxPopulation, Endings, Saves,
    ];

    public static bool Contains(string key) => Keys.Contains(key, StringComparer.Ordinal);

    /// <summary>
    /// The basic statistics a colony's history can show: each save holds the value as of its tick (#333). Endings,
    /// departures and the save count are not read from one save (endings are counted once per type and ordinal across saves,
    /// departures adjust the launched count by the endings counted, and the save count counts files), so a colony's chart
    /// does not offer a history of them.
    /// </summary>
    public static IReadOnlyList<string> SaveHistoryKeys { get; } =
        [Days, RealPlayHours, PeakWealth, LastWealth, Raids, Threats, Killed, MaxPopulation];

    public static bool HasSaveHistory(string key) => SaveHistoryKeys.Contains(key, StringComparer.Ordinal);

    /// <summary>
    /// The definition a chart of a basic statistic uses: its unit, and its all-colonies aggregation as
    /// <see cref="OverallValue"/> computes it (the largest population is a maximum, the rest are sums).
    /// </summary>
    public static MetricDefinition Definition(string key) => new()
    {
        Key = key, StoredKey = key, LedgerIds = key,
        Category = MetricCategory.Colony,
        Unit = key switch
        {
            Days => MetricUnit.Days,
            RealPlayHours => MetricUnit.Hours,
            PeakWealth or LastWealth => MetricUnit.Silver,
            _ => MetricUnit.Count,
        },
        Provenance = MetricProvenance.SaveHistory,
        Overall = key == MaxPopulation ? OverallAggregation.Max : OverallAggregation.Sum,
        LabelKey = key, DescriptionKey = key,
    };

    public static double? Value(ColonyStats colony, string key)
    {
        if (!Contains(key))
            return null;

        if (!colony.HasObservedPoint)
        {
            return key switch
            {
                Endings => colony.EndingCounts.Values.Sum(),
                Saves => colony.SaveCount,
                Departed when colony.CountedEndings.Any(e => e.Source == EndingSource.Live) => colony.Departed,
                _ => null,
            };
        }

        return key switch
        {
            Days => colony.MaxTicks / (double)StatFormat.TicksPerDay,
            RealPlayHours => StatFormat.Hours(colony.MaxRealPlay),
            PeakWealth => colony.PeakWealthKnown ? colony.WealthMax : null,
            LastWealth => colony.LastWealthKnown ? colony.WealthLast : null,
            Raids => colony.Raids,
            Threats => colony.Threats,
            Killed => colony.Killed,
            Departed => colony.Departed,
            MaxPopulation => colony.MaxPopulation,
            Endings => colony.EndingCounts.Values.Sum(),
            Saves => colony.SaveCount,
            _ => null,
        };
    }

    public static double? OverallValue(StatsResult stats, string key)
    {
        if (stats.ColonyCount <= 0 || !Contains(key))
            return null;
        return key switch
        {
            Days => stats.TotalTicks / (double)StatFormat.TicksPerDay,
            RealPlayHours => StatFormat.Hours(stats.TotalRealPlay),
            PeakWealth => stats.PeakWealthColonyCount > 0 ? stats.PeakWealthSum : null,
            LastWealth => stats.LastWealthColonyCount > 0 ? stats.LastWealthSum : null,
            Raids => stats.Raids,
            Threats => stats.Threats,
            Killed => stats.Killed,
            Departed => stats.TotalDeparted,
            MaxPopulation => stats.MaxPopulation,
            Endings => stats.TotalEndings,
            Saves => stats.Colonies.Where(c => !c.Excluded && !c.OutOfScope).Sum(c => c.SaveCount),
            _ => null,
        };
    }
}
