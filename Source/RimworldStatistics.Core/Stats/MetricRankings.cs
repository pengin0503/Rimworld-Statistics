namespace RimworldStatistics.Core;

/// <summary>
/// Phase D1 cross-colony comparisons from v1 records and Phase A values (ledger sections ranking, ranking_dependency).
/// They use v1's ending filter (dev mode setting, (type, ordinal) grouping, traces as "at least once"), the labels of v1's
/// representative point and v1's exclusions, so they never disagree with the Overview's counts. Ties: smallest colony key.
/// Outputs whose inputs come from Phase B or C (R-001/2/4/5/6/12), the starting biome (N1-F-001/2) and the participants stored
/// with an ending (N1-F-004) are built here but registered behind their unverified acquisition units
/// (<see cref="RankingDependencyCatalog"/>); R-011 is not supported and has no row.
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
    public const string KillsToDeaths = "ranking.kills_to_colonist_deaths";
    public const string MostKills = "ranking.kills.peak";
    public const string LongestDeathFree = "ranking.death_free.peak";
    public const string MostCrafting = "ranking.crafting.peak";
    public const string LargestSale = "ranking.trade.peak_sale";
    public const string MostResearch = "ranking.research.peak";
    public const string MinPartyAtEnding = "ranking.ending.min_party";

    /// <summary>The ending types with a participation definition in 1.7.4985 (see <see cref="EndingSignal.Party"/>).</summary>
    public static readonly IReadOnlySet<EndingType> PartyEndings = new HashSet<EndingType> { EndingType.Ship, EndingType.RoyalAscent, EndingType.Archonexus };

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
        var rowIndex = BuildRowIndex(rows);
        int Endings(ColonyStats c) => c.EndingCounts.Values.Sum();
        double Days(ColonyStats c) => c.MaxTicks / (double)StatFormat.TicksPerDay;
        // A colony known only from ending records has no observed save or live point: its days, population and play time
        // are unknown, not 0 (#332). It stays a target, so the ranking becomes Partial instead of Complete.
        static bool Observed(ColonyStats c) => c.HasObservedPoint;
        var observed = ordered.Where(Observed).ToList();

        yield return SplitSum(Def(ByDifficultyDays), ordered, c => c.Difficulty, Days, Observed);
        yield return SplitSum(Def(ByDifficultyEndings), ordered, c => c.Difficulty, c => Endings(c));
        yield return SplitSum(Def(ByPermadeathDays), ordered, Permadeath, Days, Observed);
        yield return SplitSum(Def(ByPermadeathEndings), ordered, Permadeath, c => Endings(c));
        yield return Extreme(Def(PopulationPeak), observed.Select(c => (c.Key, (double?)c.MaxPopulation, MetricCoverage.Complete)).ToList(), max: true, ordered.Count);
        yield return Extreme(Def(PlaytimePeak), observed.Select(c => (c.Key, (double?)(c.MaxRealPlay / 3600.0), MetricCoverage.Complete)).ToList(), max: true, ordered.Count);

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

        yield return SplitSum(Def(DaysByStoryteller), ordered, c => c.Storyteller, Days, Observed);

        // R-010: summed net change ÷ summed years spanned ((points − 1) ÷ 120). A series of one point spans no time or
        // NotApplicable is not a target; a colony without a known net change is missing.
        var netRows = ordered.Select(c => RowOf(rowIndex, c.Key, MetricKeys.WealthTotalNet)?.Value ?? NumericValue.Unknown).ToList();
        var targetsNet = netRows.Count(v => v.Coverage != MetricCoverage.NotApplicable && !(v.Number is not null && v.SampleCount is <= 1));
        var spanning = netRows.Where(v => v.Number is { } n && double.IsFinite(n) && v.SampleCount is > 1).ToList();
        yield return new NumericRow
        {
            Definition = Def(NetWealthPerYear),
            Value = NetPerYear(spanning, spanning.Count < targetsNet),
            Targets = targetsNet,
            Valid = spanning.Count,
        };

        // R-009: the colonies' own same-index maxima (both series start-aligned, Phase A sync rule).
        var perColonist = ordered.Select(c => (c.Key, RowOf(rowIndex, c.Key, MetricKeys.WealthPerColonistMax)?.Value))
            .Where(x => x.Value?.Number is { } n && double.IsFinite(n))
            .Select(x => (x.Key, x.Value!.Number, x.Value.Coverage)).ToList();
        yield return Extreme(Def(MetricKeys.WealthPerColonistMax), perColonist, max: true, ordered.Count);

        yield return BiomeSplit(Def(DaysByBiome), ordered, rowIndex, Days);
        yield return BiomeSplit(Def(EndingsByBiome), ordered, rowIndex, c => Endings(c));
        yield return SharedIntervalKillDeathRatio(Def(KillsToDeaths), ordered, rowIndex);
        yield return InputPeak(Def(MostKills), ordered, rowIndex, HealthDeathFamily.HostileKills);
        yield return InputPeak(Def(LongestDeathFree), ordered, rowIndex, HealthDeathFamily.LongestDeathGap);
        yield return InputPeak(Def(MostCrafting), ordered, rowIndex, WorkFamily.CraftedUnits);
        yield return InputPeak(Def(LargestSale), ordered, rowIndex, TradeFamily.SalesPeak);
        // S-012 is the save-time *finished* research set, not research events counted by Q-001.
        yield return InputPeak(Def(MostResearch), ordered, rowIndex, ResearchStateSnapshot.Finished);
        yield return MinParty(Def(MinPartyAtEnding), ordered);
    }

    private static Dictionary<string, Dictionary<string, NumericRow>> BuildRowIndex(
        IReadOnlyDictionary<string, IReadOnlyList<NumericRow>> rows)
    {
        var index = new Dictionary<string, Dictionary<string, NumericRow>>(rows.Count, Ord);
        foreach (var (colonyKey, list) in rows)
        {
            var dict = new Dictionary<string, NumericRow>(list.Count, Ord);
            for (var i = 0; i < list.Count; i++)
                dict.TryAdd(list[i].Definition.Key, list[i]);
            index[colonyKey] = dict;
        }
        return index;
    }

    /// <summary>
    /// Evidence of a dependency ranking is the combined capture evidence of the input rows that took part (#328), so an
    /// Experimental input is never shown as Legacy and a Legacy input is never upgraded. v1 rankings have no acquisition
    /// unit and keep <see cref="MetricEvidence.UnknownLegacy"/>, which the classifier ignores for them.
    /// </summary>
    private static MetricEvidence InputEvidence(IEnumerable<NumericRow?> inputs)
        => MetricEvidencePolicy.Combine(inputs.Where(r => r is not null).Select(r => r!.Value.Evidence));

    /// <summary>
    /// N1-F-004: the fewest confirmed participants of a counted ending, over the ending types with a participation definition.
    /// One value per (type, ordinal) as v1 counts it (the smallest party stored with its records); a colony whose counted
    /// endings of those types lack a stored party is a target without a value. No party anywhere is unknown, never 0.
    /// </summary>
    private static NumericRow MinParty(MetricDefinition def, IReadOnlyList<ColonyStats> colonies)
    {
        var byColony = colonies
            .Select(c => (c.Key, Endings: c.CountedEndings.Where(e => PartyEndings.Contains(e.Type)).GroupBy(e => (e.Type, e.Ordinal))
                .Select(g => g.Select(e => e.PartyAtEnding).Where(n => n is not null).Min()).ToList()))
            .Where(x => x.Endings.Count > 0)
            .Select(x => (x.Key, Min: x.Endings.Min(), AllKnown: x.Endings.All(n => n is not null)))
            .ToList();
        // An ending record stores no capture-time evidence. A party is only written by the unverified ending_party unit of
        // this MOD (older records leave it null), so a known party is Experimental evidence, never Legacy.
        return Extreme(def,
            byColony.Where(x => x.Min is not null).Select(x => (x.Key, (double?)x.Min, x.AllKnown ? MetricCoverage.Complete : MetricCoverage.Partial)).ToList(),
            max: false, byColony.Count, MetricEvidence.Experimental);
    }

    /// <summary>
    /// B-001 and P-004 are emitted by the SAME health_death family in the SAME
    /// representative checkpoint. Never combine a latest save's vanilla deaths
    /// counter with an older checkpoint's kills or silently replace absent inputs
    /// with zero.
    /// </summary>
    private static NumericRow SharedIntervalKillDeathRatio(MetricDefinition def, IReadOnlyList<ColonyStats> colonies,
        IReadOnlyDictionary<string, Dictionary<string, NumericRow>> rows)
    {
        double kills = 0, deaths = 0;
        var valid = 0;
        var weak = false;
        var inputs = new List<NumericRow?>();
        foreach (var colony in colonies)
        {
            var k = RowOf(rows, colony.Key, HealthDeathFamily.HostileKills);
            var d = RowOf(rows, colony.Key, HealthDeathFamily.DeathsByRaid);
            if (k?.Value.Number is not { } kv || d?.Value.Number is not { } dv
                || !double.IsFinite(kv) || !double.IsFinite(dv)
                || k.Value.ThroughTick is null || d.Value.ThroughTick != k.Value.ThroughTick
                || kv < 0 || dv < 0)
                continue;
            valid++;
            kills += kv;
            deaths += dv;
            inputs.Add(k);
            inputs.Add(d);
            if (k.Value.Coverage is MetricCoverage.Partial or MetricCoverage.Unknown
                || d.Value.Coverage is MetricCoverage.Partial or MetricCoverage.Unknown)
                weak = true;
        }
        if (valid == 0 || !double.IsFinite(kills) || !double.IsFinite(deaths))
            return new NumericRow { Definition = def, Targets = colonies.Count, Valid = valid, Value = NumericValue.Unknown };
        var ratio = deaths > 0 ? kills / deaths : (double?)null;
        if (ratio is { } r && !double.IsFinite(r))
            return new NumericRow { Definition = def, Targets = colonies.Count, Valid = valid, Value = NumericValue.Unknown };
        var incomplete = weak || valid < colonies.Count;
        return new NumericRow
        {
            Definition = def,
            Targets = colonies.Count,
            Valid = valid,
            Value = new NumericValue
            {
                Number = ratio,
                Numerator = kills, Denominator = deaths,
                Coverage = incomplete ? MetricCoverage.Partial : MetricCoverage.Complete,
                Evidence = InputEvidence(inputs),
            },
        };
    }

    private static NumericRow BiomeSplit(MetricDefinition def, IReadOnlyList<ColonyStats> colonies,
        IReadOnlyDictionary<string, Dictionary<string, NumericRow>> rows, Func<ColonyStats, double> quantity)
    {
        var known = new List<(ColonyStats Colony, string Biome, MetricCoverage Coverage, NumericRow Source)>();
        foreach (var colony in colonies)
        {
            var source = RowOf(rows, colony.Key, ColonyStartSnapshot.Biome);
            if (source?.Value.Number != 1 || source.Breakdowns.Count != 1)
                continue;
            var biome = source.Breakdowns[0];
            if (biome.Value.Number != 1 || string.IsNullOrWhiteSpace(biome.Breakdown))
                continue;
            known.Add((colony, biome.Breakdown, source.Value.Coverage, source));
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
                Value = new NumericValue
                {
                    Number = g.Sum(x => quantity(x.Colony)), Coverage = coverage,
                    Evidence = InputEvidence(g.Select(x => x.Source)),
                },
                Targets = g.Count(), Valid = g.Count(),
            }).ToList();
        return new NumericRow
        {
            Definition = def,
            Value = new NumericValue
            {
                Number = known.Sum(x => quantity(x.Colony)), Coverage = coverage,
                Evidence = InputEvidence(known.Select(x => x.Source)),
            },
            Targets = colonies.Count, Valid = known.Count, Breakdowns = breakdowns,
        };
    }

    private static NumericRow InputPeak(MetricDefinition definition, IReadOnlyList<ColonyStats> colonies,
        IReadOnlyDictionary<string, Dictionary<string, NumericRow>> rows, string sourceKey)
    {
        var all = colonies.Select(c => (c.Key, Row: RowOf(rows, c.Key, sourceKey))).ToList();
        var applicable = all.Where(x => x.Row?.Value.Coverage != MetricCoverage.NotApplicable).ToList();
        if (applicable.Count == 0 && all.Count > 0)
            return new NumericRow { Definition = definition, Value = new NumericValue { Coverage = MetricCoverage.NotApplicable }, Targets = 0 };
        var inputs = applicable.Where(x => x.Row?.Value.Number is { } n && double.IsFinite(n)).ToList();
        var candidates = inputs.Select(x => (x.Key, Value: x.Row!.Value.Number, x.Row.Value.Coverage)).ToList();
        return Extreme(definition, candidates, max: true, applicable.Count, InputEvidence(inputs.Select(x => x.Row)));
    }

    private static string? Permadeath(ColonyStats c) => c.Permadeath switch
    {
        true => PermadeathOn,
        false => PermadeathOff,
        null => null,
    };

    private static NumericRow? RowOf(IReadOnlyDictionary<string, Dictionary<string, NumericRow>> rows, string colony, string definitionKey)
        => rows.TryGetValue(colony, out var map) && map.TryGetValue(definitionKey, out var row) ? row : null;

    private static NumericValue NetPerYear(List<NumericValue> nets, bool someMissing)
    {
        if (nets.Count == 0)
            return NumericValue.Unknown;
        double net = 0;
        double years = 0;
        var partial = someMissing;
        for (var i = 0; i < nets.Count; i++)
        {
            var v = nets[i];
            if (v.Number is not { } n || !double.IsFinite(n) || v.SampleCount is not > 1)
                return NumericValue.Unknown;
            var y = (v.SampleCount.Value - 1) * HistoryMetricReader.TicksPerPoint / TicksPerYear;
            if (!double.IsFinite(y) || y <= 0)
                continue;
            net += n;
            years += y;
            if (v.Coverage == MetricCoverage.Partial)
                partial = true;
        }
        if (years <= 0 || !double.IsFinite(net) || !double.IsFinite(years))
            return NumericValue.Unknown;
        var ratio = net / years;
        if (!double.IsFinite(ratio))
            return NumericValue.Unknown;
        return new NumericValue
        {
            Number = ratio, Numerator = net, Denominator = years,
            Coverage = partial ? MetricCoverage.Partial : MetricCoverage.SnapshotOnly,
        };
    }

    /// <summary>
    /// Per-label sums; colonies without the label count under the unknown bucket. Every colony has a label or "?", so the
    /// split is complete. A colony for which <paramref name="measured"/> is false has no value: it stays a target of its
    /// bucket and of the total, adds nothing, and makes them Partial (a bucket with no measured colony is Unknown).
    /// </summary>
    private static NumericRow SplitSum(MetricDefinition d, IReadOnlyList<ColonyStats> colonies, Func<ColonyStats, string?> label,
        Func<ColonyStats, double> value, Func<ColonyStats, bool>? measured = null)
    {
        measured ??= static _ => true;
        NumericValue Sum(IReadOnlyCollection<ColonyStats> group)
        {
            var known = group.Where(measured).ToList();
            return known.Count == 0 ? NumericValue.Unknown : new NumericValue
            {
                Number = known.Sum(value),
                Coverage = known.Count < group.Count ? MetricCoverage.Partial : MetricCoverage.Complete,
            };
        }
        var groups = colonies.GroupBy(c => string.IsNullOrEmpty(label(c)) ? MetricKeys.UnknownBucket : label(c)!, Ord).OrderBy(g => g.Key, Ord).ToList();
        var breakdowns = groups.Select(g => (g.Key, Members: g.ToList())).Select(g => new NumericRow
        {
            Definition = d,
            Breakdown = g.Key,
            Value = Sum(g.Members),
            Targets = g.Members.Count,
            Valid = g.Members.Count(measured),
        }).ToList();
        return new NumericRow
        {
            Definition = d,
            Value = colonies.Count == 0 ? NumericValue.Unknown : Sum(colonies.ToList()),
            Targets = colonies.Count,
            Valid = colonies.Count(measured),
            Breakdowns = breakdowns,
        };
    }

    /// <summary>
    /// The largest (or smallest) candidate and its colony. Partial when a candidate is Partial or fewer than
    /// <paramref name="targets"/> colonies have a value (callers pass the colonies the value is defined for).
    /// </summary>
    private static NumericRow Extreme(MetricDefinition d, List<(string Key, double? Value, MetricCoverage Coverage)> candidates, bool max, int targets,
        MetricEvidence evidence = MetricEvidence.UnknownLegacy)
    {
        var finite = candidates.Where(c => c.Value is { } v && double.IsFinite(v)).ToList();
        if (finite.Count == 0)
            return new NumericRow { Definition = d, Value = NumericValue.Unknown, Targets = targets };
        var best = max ? finite.Max(c => c.Value!.Value) : finite.Min(c => c.Value!.Value);
        var holder = finite.Where(c => c.Value == best).Select(c => c.Key).Order(Ord).First();
        var valid = finite.Select(c => c.Key).Distinct(Ord).Count();
        var partial = valid < targets || finite.Any(c => c.Coverage is MetricCoverage.Partial or MetricCoverage.Unknown);
        var coverage = partial ? MetricCoverage.Partial : finite.All(c => c.Coverage == MetricCoverage.Complete) ? MetricCoverage.Complete : MetricCoverage.SnapshotOnly;
        return new NumericRow
        {
            Definition = d,
            Value = new NumericValue { Number = best, Coverage = coverage, ColonyKey = holder, Evidence = evidence },
            Targets = targets,
            Valid = valid,
        };
    }
}
