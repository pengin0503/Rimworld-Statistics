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

    public static double? Value(ColonyStats colony, string key)
    {
        if (!colony.HasObservedPoint || !Contains(key))
            return null;

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
