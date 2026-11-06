namespace RimworldStatistics.Core;

public sealed class StatsOptions
{
    /// <summary>Show and count endings recorded while dev mode was on (spec D6; default off).</summary>
    public bool IncludeDevMode { get; init; }
}

/// <summary>Per-colony figures (spec §4, §9.2 Colonies tab).</summary>
public sealed class ColonyStats
{
    public required string Key { get; init; }
    public string? FactionName { get; init; }
    public string? WorldName { get; init; }
    public string? Scenario { get; init; }
    public string? Storyteller { get; init; }

    public int MaxTicks { get; init; }
    public double MaxRealPlay { get; init; }
    public int Raids { get; init; }
    public int Threats { get; init; }
    public int Killed { get; init; }
    public int Departed { get; init; }
    public int MaxPopulation { get; init; }
    public double WealthMax { get; init; }
    public double WealthLast { get; init; }

    public required IReadOnlyDictionary<EndingType, int> EndingCounts { get; init; }

    /// <summary>Some (type, ordinal) has only dev-mode records.</summary>
    public bool HasDevOnlyEndings { get; init; }

    public required IReadOnlyList<string> GameVersions { get; init; }
    public int SaveCount { get; init; }
    public DateTime? LastPlayedUtc { get; init; }
    public bool Excluded { get; init; }
}

/// <summary>Totals across colonies (spec §4.1, §4.2). Excluded colonies are listed but not counted.</summary>
public sealed class StatsResult
{
    public int ColonyCount { get; init; }
    public int ExcludedColonyCount { get; init; }
    public bool HasData => ColonyCount > 0;

    public long TotalTicks { get; init; }
    public double TotalRealPlay { get; init; }

    public int TotalEndings { get; init; }
    public required IReadOnlyDictionary<EndingType, int> EndingsByType { get; init; }
    public int TotalDeparted { get; init; }

    public double PeakWealthSum { get; init; }
    public double LastWealthSum { get; init; }
    public double SingleMaxWealth { get; init; }

    public int Raids { get; init; }
    public int Threats { get; init; }
    public int Killed { get; init; }
    public long LongestTicks { get; init; }
    public int MaxPopulation { get; init; }

    public required IReadOnlyList<ColonyStats> Colonies { get; init; }
}

public static class StatsCalculator
{
    public static StatsResult Calculate(IReadOnlyList<Colony> colonies, StatsOptions options)
    {
        var all = colonies.Select(c => CalculateColony(c, options)).ToList();
        var counted = all.Where(c => !c.Excluded).ToList();
        var byType = new Dictionary<EndingType, int>();
        foreach (var c in counted)
            foreach (var (type, n) in c.EndingCounts)
                byType[type] = byType.GetValueOrDefault(type) + n;

        return new StatsResult
        {
            ColonyCount = counted.Count,
            ExcludedColonyCount = all.Count - counted.Count,
            TotalTicks = counted.Sum(c => (long)c.MaxTicks),
            TotalRealPlay = counted.Sum(c => c.MaxRealPlay),
            TotalEndings = byType.Values.Sum(),
            EndingsByType = byType,
            TotalDeparted = counted.Sum(c => c.Departed),
            PeakWealthSum = counted.Sum(c => c.WealthMax),
            LastWealthSum = counted.Sum(c => c.WealthLast),
            SingleMaxWealth = counted.Select(c => c.WealthMax).DefaultIfEmpty(0).Max(),
            Raids = counted.Sum(c => c.Raids),
            Threats = counted.Sum(c => c.Threats),
            Killed = counted.Sum(c => c.Killed),
            LongestTicks = counted.Select(c => (long)c.MaxTicks).DefaultIfEmpty(0).Max(),
            MaxPopulation = counted.Select(c => c.MaxPopulation).DefaultIfEmpty(0).Max(),
            Colonies = all,
        };
    }

    /// <summary>One point in a colony's history: a save observation or a live record's maxima.</summary>
    private readonly record struct Point(ObservedValues Values, DateTime Time, DisplayInfo Display);

    public static ColonyStats CalculateColony(Colony colony, StatsOptions options)
    {
        // Superseded observations are history of this colony and count too.
        var points = colony.Observations.Select(o => new Point(o.Values, o.LastWriteUtc, o.Display))
            .Concat(colony.LiveCampaigns.Select(l => new Point(l.Max, l.LastSavedUtc, l.Display)))
            .ToList();
        int MaxOf(Func<ObservedValues, int> f) => points.Select(p => f(p.Values)).DefaultIfEmpty(0).Max();
        double MaxOfD(Func<ObservedValues, double> f) => points.Select(p => f(p.Values)).DefaultIfEmpty(0).Max();

        // Spec §4.2: the last wealth (and the display name, §9.2) come from the furthest point; ties go to the newer one.
        Point? furthest = points.Count == 0 ? null : points.OrderByDescending(p => p.Values.TicksGame).ThenByDescending(p => p.Time).First();

        // Endings grouped by (type, ordinal) after colony grouping (spec §6.2), so several campaign ids do not double count.
        var pairs = colony.Endings.GroupBy(e => (e.Type, e.Ordinal)).ToList();
        bool Counted(IGrouping<(EndingType, int), EndingEvent> g) => options.IncludeDevMode || g.Any(e => !e.DevMode);
        var counts = new Dictionary<EndingType, int>();
        foreach (var g in pairs.Where(Counted))
            counts[g.Key.Item1] = counts.GetValueOrDefault(g.Key.Item1) + 1;
        foreach (var t in colony.Traces)
            counts[t] = Math.Max(counts.GetValueOrDefault(t), 1);   // a trace means "at least once"

        // Spec §4.1 / §4.4 departed.
        var maxLaunched = Math.Max(MaxOf(v => v.ColonistsLaunched), colony.Endings.Select(e => e.LaunchedTotalAfter).DefaultIfEmpty(0).Max());
        int departed;
        if (options.IncludeDevMode)
        {
            departed = Math.Max(maxLaunched, pairs.Sum(g => g.Max(e => e.Departed)));
        }
        else
        {
            var normalSum = pairs.Where(g => g.Any(e => !e.DevMode)).Sum(g => g.Where(e => !e.DevMode).Max(e => e.Departed));
            var devDepartures = pairs.Where(g => g.Any(e => e.DevMode)).Sum(g => g.Where(e => e.DevMode).Max(e => e.Departed));
            departed = Math.Max(Math.Max(0, maxLaunched - devDepartures), normalSum);
        }

        var times = colony.Observations.Select(o => o.LastWriteUtc).Concat(colony.LiveCampaigns.Select(l => l.LastSavedUtc)).ToList();
        return new ColonyStats
        {
            Key = colony.Key,
            FactionName = furthest?.Display.FactionName,
            WorldName = furthest?.Display.WorldName,
            Scenario = furthest?.Display.Scenario,
            Storyteller = furthest?.Display.Storyteller,
            MaxTicks = MaxOf(v => v.TicksGame),
            MaxRealPlay = MaxOfD(v => v.RealPlayTime),
            Raids = MaxOf(v => v.NumRaidsEnemy),
            Threats = MaxOf(v => v.NumThreatsQueued),
            Killed = MaxOf(v => v.ColonistsKilled),
            Departed = departed,
            MaxPopulation = MaxOf(v => v.GreatestPopulation),
            WealthMax = MaxOfD(v => v.WealthMax),
            WealthLast = furthest?.Values.WealthLast ?? 0,
            EndingCounts = counts,
            HasDevOnlyEndings = pairs.Any(g => g.All(e => e.DevMode)),
            GameVersions = colony.Observations.Select(o => o.GameVersion).Concat(colony.Endings.Select(e => e.GameVersion))
                .OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
            SaveCount = colony.Observations.Select(o => o.FileName).Distinct(StringComparer.Ordinal).Count(),
            LastPlayedUtc = times.Count == 0 ? null : times.Max(),
            Excluded = colony.Excluded,
        };
    }
}
