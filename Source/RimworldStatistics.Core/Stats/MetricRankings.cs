namespace RimworldStatistics.Core;

/// <summary>
/// Phase D1 cross-colony comparisons from v1 records and Phase A values (ledger sections ranking, ranking_dependency).
/// They use v1's ending filter (dev mode setting, (type, ordinal) grouping, traces as "at least once"), the labels of v1's
/// representative point and v1's exclusions, so they never disagree with the Overview's counts. Ties: smallest colony key.
/// Outputs whose inputs come from Phase B or C (R-001/2/4/5/6/12) and those needing new live data (N1-F-001/2/4) are not
/// built; R-011 is not supported and has no row.
/// </summary>
public static class MetricRankings
{
    public const string ByDifficultyDays = "ranking.by_difficulty";
    public const string ByDifficultyEndings = "ranking.by_difficulty.endings";
    public const string ByPermadeathDays = "ranking.by_permadeath";
    public const string ByPermadeathEndings = "ranking.by_permadeath.endings";
    public const string PopulationPeak = "ranking.population.peak";
    public const string FastestEnding = "ranking.ending.fastest";
    public const string NetWealthPerYear = "ranking.wealth.net_per_year";
    public const string TopScenario = "ranking.ending.top_scenario";
    public const string DaysByStoryteller = "ranking.survival.by_storyteller";
    public const string PlaytimePeak = "ranking.playtime.peak";
    public const string MinDeathsAtEnding = "ranking.ending.min_deaths";
    public const string ZeroDeathEndings = "ranking.ending.zero_deaths";

    // Phase D dependency ranks: not published until their B/C acquisition is verified.
    public const string DaysByBiome = "ranking.survival.by_biome";
    public const string EndingsByBiome = "ranking.endings.by_biome";
    public const string MostKills = "ranking.kills.peak";
    public const string LongestDeathFree = "ranking.death_free.peak";
    public const string MostCrafting = "ranking.crafting.peak";
    public const string LargestSale = "ranking.trade.peak_sale";
    public const string MostResearch = "ranking.research.peak";

    /// <summary>Breakdown keys of the permadeath split.</summary>
    public const string PermadeathOn = "permadeath";
    public const string PermadeathOff = "normal";

    private static readonly StringComparer Ord = StringComparer.Ordinal;
    private const double TicksPerYear = StatFormat.TicksPerDay * StatFormat.DaysPerYear;

    /// <param name="colonies">v1 statistics of the counted (not excluded) colonies.</param>
    /// <param name="rows">The numeric rows of the same colonies, by colony key.</param>
    public static IEnumerable<NumericRow> Build(IReadOnlyList<ColonyStats> colonies, IReadOnlyDictionary<string, IReadOnlyList<NumericRow>> rows)
    {
        MetricDefinition Def(string key) => MetricRegistry.Find(key) ?? throw new InvalidOperationException("unregistered ranking " + key);
        var ordered = colonies.OrderBy(c => c.Key, Ord).ToList();
        int Endings(ColonyStats c) => c.EndingCounts.Values.Sum();
        double Days(ColonyStats c) => c.MaxTicks / (double)StatFormat.TicksPerDay;

        yield return SplitSum(Def(ByDifficultyDays), ordered, c => c.Difficulty, Days);
        yield return SplitSum(Def(ByDifficultyEndings), ordered, c => c.Difficulty, c => Endings(c));
        yield return SplitSum(Def(ByPermadeathDays), ordered, Permadeath, Days);
        yield return SplitSum(Def(ByPermadeathEndings), ordered, Permadeath, c => Endings(c));
        yield return Extreme(Def(PopulationPeak), ordered.Select(c => (c.Key, (double?)c.MaxPopulation, MetricCoverage.Complete)).ToList(), max: true, ordered.Count);
        yield return Extreme(Def(PlaytimePeak), ordered.Select(c => (c.Key, (double?)(c.MaxRealPlay / 3600.0), MetricCoverage.Complete)).ToList(), max: true, ordered.Count);

        // R-007: measured ticks of recorded endings only; seeded traces and save traces have no measured time.
        var fastest = ordered.Select(c => (c.Key, Days: c.CountedEndings.Where(e => e.Source == EndingSource.Live && e.TicksGame > 0)
                .Select(e => (double?)(e.TicksGame / (double)StatFormat.TicksPerDay)).Min(), MetricCoverage.Complete))
            .Where(x => x.Days is not null).ToList();
        yield return Extreme(Def(FastestEnding), fastest, max: false, fastest.Count);

        // R-008 / N1-F-003: one value per ending as v1 counts it, a (type, ordinal) of the colony; its counter is the smallest
        // stored with that ending's counted records. An ending without any stored counter is unknown, not 0.
        var endings = ordered.SelectMany(c => c.CountedEndings.GroupBy(e => (e.Type, e.Ordinal))
            .Select(g => (c.Key, Killed: g.Select(e => e.ColonistsKilledAtEnding).Where(k => k is not null).Min()))).ToList();
        var knownEndings = endings.Count(x => x.Killed is not null);
        var byColony = endings.GroupBy(x => x.Key, Ord)
            .Select(g => (Key: g.Key, Min: g.Min(x => x.Killed), AllKnown: g.All(x => x.Killed is not null))).ToList();
        yield return Extreme(Def(MinDeathsAtEnding),
            byColony.Where(x => x.Min is not null).Select(x => (x.Key, (double?)x.Min, x.AllKnown ? MetricCoverage.Complete : MetricCoverage.Partial)).ToList(),
            max: false, byColony.Count);
        yield return new NumericRow
        {
            Definition = Def(ZeroDeathEndings),
            Value = knownEndings == 0
                ? NumericValue.Unknown
                : new NumericValue
                {
                    Number = endings.Count(x => x.Killed == 0),
                    Coverage = knownEndings < endings.Count ? MetricCoverage.Partial : MetricCoverage.Complete,
                },
            Targets = endings.Count,
            Valid = knownEndings,
        };

        // R-013: the scenario label of v1's representative point with the most counted endings.
        var byScenario = SplitSum(Def(TopScenario), ordered, c => c.Scenario, c => Endings(c));
        var top = byScenario.Breakdowns.Where(b => b.Value.Number > 0).ToList();
        var best = top.Count == 0 ? 0 : top.Max(b => b.Value.Number!.Value);
        yield return new NumericRow
        {
            Definition = byScenario.Definition,
            Value = top.Count == 0
                ? new NumericValue { Number = 0, Coverage = byScenario.Value.Coverage }
                : new NumericValue
                {
                    Number = best, Coverage = byScenario.Value.Coverage,
                    Label = string.Join(" / ", top.Where(b => b.Value.Number == best).Select(b => b.Breakdown)),
                },
            Targets = byScenario.Targets,
            Valid = byScenario.Valid,
            Breakdowns = byScenario.Breakdowns,
        };

        yield return SplitSum(Def(DaysByStoryteller), ordered, c => c.Storyteller, Days);

        // R-010: summed net change ÷ summed years spanned ((points − 1) ÷ 120). A series of one point spans no time and is
        // not a target; a colony without a known net change is missing.
        var netRows = ordered.Select(c => RowOf(rows, c.Key, MetricKeys.WealthTotalNet)?.Value ?? NumericValue.Unknown).ToList();
        var spanning = netRows.Where(v => v.Number is not null && v.SampleCount is > 1).ToList();
        var targetsNet = netRows.Count(v => !(v.Number is not null && v.SampleCount is <= 1));
        yield return new NumericRow
        {
            Definition = Def(NetWealthPerYear),
            Value = NetPerYear(spanning, spanning.Count < targetsNet),
            Targets = targetsNet,
            Valid = spanning.Count,
        };

        // R-009: the colonies' own same-index maxima (both series start-aligned, Phase A sync rule).
        var perColonist = ordered.Select(c => (c.Key, RowOf(rows, c.Key, MetricKeys.WealthPerColonistMax)?.Value))
            .Where(x => x.Value?.Number is not null).Select(x => (x.Key, x.Value!.Number, x.Value.Coverage)).ToList();
        yield return Extreme(Def(MetricKeys.WealthPerColonistMax), perColonist, max: true, ordered.Count);

        yield return BiomeSplit(Def(DaysByBiome), ordered, rows, Days);
        yield return BiomeSplit(Def(EndingsByBiome), ordered, rows, c => Endings(c));
        yield return InputPeak(Def(MostKills), ordered, rows, HealthDeathFamily.HostileKills);
        yield return InputPeak(Def(LongestDeathFree), ordered, rows, HealthDeathFamily.LongestDeathGap);
        yield return InputPeak(Def(MostCrafting), ordered, rows, WorkFamily.CraftedUnits);
        yield return InputPeak(Def(LargestSale), ordered, rows, TradeFamily.SalesPeak);
        // S-012 is the save-time *finished* research set, not research events counted by Q-001.
        yield return InputPeak(Def(MostResearch), ordered, rows, ResearchStateSnapshot.Finished);
    }

    /// <summary>
    /// Maximum over matched B/C source observations from independent colonies. The
    /// source row already chooses the representative save/checkpoint and supplies
    /// its own coverage. Missing input never silently becomes a zero.
    /// </summary>
    /// <summary>
    /// Starting biome comes from a *starting-tile* observation, never from the
    /// current player home. Missing or ambiguous values are not "unknown biome"
    /// names and are excluded from the numerator, making coverage Partial.
    /// </summary>
    private static NumericRow BiomeSplit(MetricDefinition def, IReadOnlyList<ColonyStats> colonies,
        IReadOnlyDictionary<string, IReadOnlyList<NumericRow>> rows, Func<ColonyStats, double> quantity)
    {
        var known = new List<(ColonyStats Colony, string Biome, MetricCoverage Coverage)>();
        foreach (var colony in colonies)
        {
            var source = RowOf(rows, colony.Key, ColonyStartSnapshot.Biome);
            if (source?.Value.Number != 1 || source.Breakdowns.Count != 1)
                continue;
            var biome = source.Breakdowns[0];
            if (biome.Value.Number != 1 || string.IsNullOrWhiteSpace(biome.Breakdown))
                continue;
            known.Add((colony, biome.Breakdown, source.Value.Coverage));
        }
        if (known.Count == 0)
            return new NumericRow { Definition = def, Value = NumericValue.Unknown, Targets = colonies.Count };

        var incomplete = known.Count < colonies.Count || known.Any(x => x.Coverage is MetricCoverage.Partial or MetricCoverage.Unknown);
        var coverage = incomplete ? MetricCoverage.Partial : MetricCoverage.SnapshotOnly;
        var breakdowns = known.GroupBy(x => x.Biome, Ord).OrderBy(g => g.Key, Ord)
            .Select(g => new NumericRow
            {
                Definition = def,
                Breakdown = g.Key,
                Value = new NumericValue { Number = g.Sum(x => quantity(x.Colony)), Coverage = coverage },
                Targets = g.Count(), Valid = g.Count(),
            }).ToList();
        return new NumericRow
        {
            Definition = def,
            Value = new NumericValue { Number = known.Sum(x => quantity(x.Colony)), Coverage = coverage },
            Targets = colonies.Count, Valid = known.Count, Breakdowns = breakdowns,
        };
    }

    private static NumericRow InputPeak(MetricDefinition definition, IReadOnlyList<ColonyStats> colonies,
        IReadOnlyDictionary<string, IReadOnlyList<NumericRow>> rows, string sourceKey)
    {
        var candidates = colonies.Select(c => (c.Key, Row: RowOf(rows, c.Key, sourceKey)))
            .Where(x => x.Row?.Value.Number is not null)
            .Select(x => (x.Key, Value: x.Row!.Value.Number, x.Row.Value.Coverage))
            .ToList();
        return Extreme(definition, candidates, max: true, colonies.Count);
    }

    private static string? Permadeath(ColonyStats c) => c.Permadeath switch
    {
        true => PermadeathOn,
        false => PermadeathOff,
        null => null,
    };

    private static NumericRow? RowOf(IReadOnlyDictionary<string, IReadOnlyList<NumericRow>> rows, string colony, string definitionKey)
        => rows.TryGetValue(colony, out var list) ? list.FirstOrDefault(r => r.Definition.Key == definitionKey) : null;

    private static NumericValue NetPerYear(List<NumericValue> nets, bool someMissing)
    {
        if (nets.Count == 0)
            return NumericValue.Unknown;
        var net = nets.Sum(v => v.Number!.Value);
        var years = nets.Sum(v => (v.SampleCount!.Value - 1) * HistoryMetricReader.TicksPerPoint / TicksPerYear);
        var partial = someMissing || nets.Any(v => v.Coverage == MetricCoverage.Partial);
        return new NumericValue
        {
            Number = net / years, Numerator = net, Denominator = years,
            Coverage = partial ? MetricCoverage.Partial : MetricCoverage.SnapshotOnly,
        };
    }

    /// <summary>Per-label sums; colonies without the label count under the unknown bucket. Every colony has a label or "?", so the split is complete.</summary>
    private static NumericRow SplitSum(MetricDefinition d, IReadOnlyList<ColonyStats> colonies, Func<ColonyStats, string?> label, Func<ColonyStats, double> value)
    {
        var groups = colonies.GroupBy(c => string.IsNullOrEmpty(label(c)) ? MetricKeys.UnknownBucket : label(c)!, Ord).OrderBy(g => g.Key, Ord).ToList();
        var breakdowns = groups.Select(g => new NumericRow
        {
            Definition = d,
            Breakdown = g.Key,
            Value = new NumericValue { Number = g.Sum(value), Coverage = MetricCoverage.Complete },
            Targets = g.Count(),
            Valid = g.Count(),
        }).ToList();
        return new NumericRow
        {
            Definition = d,
            Value = colonies.Count == 0 ? NumericValue.Unknown : new NumericValue { Number = colonies.Sum(value), Coverage = MetricCoverage.Complete },
            Targets = colonies.Count,
            Valid = colonies.Count,
            Breakdowns = breakdowns,
        };
    }

    /// <summary>
    /// The largest (or smallest) candidate and its colony. Partial when a candidate is Partial or fewer than
    /// <paramref name="targets"/> colonies have a value (callers pass the colonies the value is defined for).
    /// </summary>
    private static NumericRow Extreme(MetricDefinition d, List<(string Key, double? Value, MetricCoverage Coverage)> candidates, bool max, int targets)
    {
        if (candidates.Count == 0)
            return new NumericRow { Definition = d, Value = NumericValue.Unknown, Targets = targets };
        var best = max ? candidates.Max(c => c.Value!.Value) : candidates.Min(c => c.Value!.Value);
        var holder = candidates.Where(c => c.Value == best).Select(c => c.Key).Order(Ord).First();
        var valid = candidates.Select(c => c.Key).Distinct(Ord).Count();
        var partial = valid < targets || candidates.Any(c => c.Coverage is MetricCoverage.Partial or MetricCoverage.Unknown);
        var coverage = partial ? MetricCoverage.Partial : candidates.All(c => c.Coverage == MetricCoverage.Complete) ? MetricCoverage.Complete : MetricCoverage.SnapshotOnly;
        return new NumericRow
        {
            Definition = d,
            Value = new NumericValue { Number = best, Coverage = coverage, ColonyKey = holder },
            Targets = targets,
            Valid = valid,
        };
    }
}
