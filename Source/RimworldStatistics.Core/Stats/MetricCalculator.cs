namespace RimworldStatistics.Core;

/// <summary>One shown value. Immutable; built off the UI thread and only read while drawing.</summary>
public sealed class NumericValue
{
    public static readonly NumericValue Unknown = new() { Coverage = MetricCoverage.Unknown };

    public double? Number { get; init; }
    public MetricCoverage Coverage { get; init; }
    public double? Numerator { get; init; }
    public double? Denominator { get; init; }
    public int? SampleCount { get; init; }

    /// <summary>The game tick the value runs to (the representative observation's save tick).</summary>
    public int? ThroughTick { get; init; }

    /// <summary>For an all-colonies maximum or minimum: the colony that holds it (ties: smallest colony key).</summary>
    public string? ColonyKey { get; init; }

    /// <summary>For a ranking over labels (e.g. the scenario with most endings): the label(s) holding it, ties joined.</summary>
    public string? Label { get; init; }

    /// <summary>Known range, no value: a threshold that was not reached (a 0 ÷ 0 rate is not this: it has a denominator).</summary>
    public bool NotReached => Number is null && Denominator is null && Coverage is not (MetricCoverage.Unknown or MetricCoverage.NotApplicable);

    /// <summary>A value, a threshold not reached, or a known 0 ÷ 0.</summary>
    public bool Known => Number is not null || NotReached
        || (Numerator is not null && Denominator is not null && Coverage is not (MetricCoverage.Unknown or MetricCoverage.NotApplicable));
}

/// <summary>One metric (or one breakdown entry) for all colonies or for one colony.</summary>
public sealed class NumericRow
{
    public required MetricDefinition Definition { get; init; }

    /// <summary>"" for the metric itself; the breakdown key (e.g. a quest root def) for a breakdown row.</summary>
    public string Breakdown { get; init; } = MetricEntry.Scalar;

    public required NumericValue Value { get; init; }

    /// <summary>Colonies the value is defined for (not excluded, not "not applicable").</summary>
    public int Targets { get; init; }

    /// <summary>Of those, colonies with a known value.</summary>
    public int Valid { get; init; }

    public IReadOnlyList<NumericRow> Breakdowns { get; init; } = Array.Empty<NumericRow>();
}

public sealed class NumericColony
{
    public required string Key { get; init; }
    public bool Excluded { get; init; }

    /// <summary>Numeric observations attached to this colony.</summary>
    public int Observations { get; init; }

    /// <summary>What the colony's checkpoints (Phase C) say about its save lineage.</summary>
    public CheckpointLineage Lineage { get; init; }

    public required IReadOnlyList<NumericRow> Rows { get; init; }
}

/// <summary>The numeric projection of the profile for the Details tab (numeric expansion design §4).</summary>
public sealed class NumericView
{
    public static readonly NumericView Empty = new() { Overall = Array.Empty<NumericRow>(), Colonies = Array.Empty<NumericColony>() };

    public required IReadOnlyList<NumericRow> Overall { get; init; }
    public required IReadOnlyList<NumericColony> Colonies { get; init; }

    /// <summary>Numeric observations that could not be attached to exactly one known colony (not counted anywhere).</summary>
    public int Unattributed { get; init; }
}

/// <summary>
/// Builds the <see cref="NumericView"/> from the v1 colonies (after manual merges and exclusions) and the numeric
/// observations (numeric expansion design §2 "コロニー照合", §3 "過去観測の選び方").
/// </summary>
public static class MetricCalculator
{
    private static readonly StringComparer Ord = StringComparer.Ordinal;

    public static NumericView BuildView(Profile profile, IReadOnlyList<Colony> colonies, StatsOptions options)
        => BuildView(profile, colonies, StatsCalculator.Calculate(colonies, options));

    /// <param name="stats">The v1 statistics of the same colonies, whose ending filter and labels the rankings use.</param>
    public static NumericView BuildView(Profile profile, IReadOnlyList<Colony> colonies, StatsResult stats)
        => BuildView(profile, colonies, stats, MetricRegistry.All);

    /// <param name="definitions">The outputs to build (the published registry, or the whole catalog for verification).</param>
    public static NumericView BuildView(Profile profile, IReadOnlyList<Colony> colonies, StatsResult stats, IReadOnlyList<MetricDefinition> definitions)
    {
        var attached = Attach(profile, colonies, out var unattributed);
        var perColony = new List<(Colony Colony, List<NumericRow> Rows, CheckpointLineage Lineage)>(colonies.Count);
        foreach (var colony in colonies)
        {
            var observations = attached.GetValueOrDefault(colony) ?? new List<MetricObservation>();
            var checkpoints = CheckpointSelector.Select(observations);
            perColony.Add((colony, definitions.Select(d => ColonyRow(d, observations, checkpoints.Representative)).ToList(), checkpoints.Lineage));
        }

        var counted = perColony.Where(c => !c.Colony.Excluded).ToList();
        var overall = new List<NumericRow>();
        for (var i = 0; i < definitions.Count; i++)
        {
            var def = definitions[i];
            if (def.Overall is OverallAggregation.IndividualOnly or OverallAggregation.Ranking)
                continue;
            overall.Add(Combine(def, counted.Select(c => (c.Colony.Key, c.Rows[i])).ToList()));
        }
        var countedStats = stats.Colonies.Where(c => !c.Excluded).ToList();
        overall.AddRange(MetricRankings.Build(countedStats, counted.ToDictionary(c => c.Colony.Key, c => (IReadOnlyList<NumericRow>)c.Rows, Ord)));

        return new NumericView
        {
            Overall = overall,
            Colonies = perColony.Select(c => new NumericColony
            {
                Key = c.Colony.Key,
                Excluded = c.Colony.Excluded,
                Observations = attached.GetValueOrDefault(c.Colony)?.Count ?? 0,
                Lineage = c.Lineage,
                Rows = c.Rows.Where(r => r.Definition.Overall != OverallAggregation.Ranking).ToList(),
            }).ToList(),
            Unattributed = unattributed,
        };
    }

    // ----- attribution -----

    /// <summary>
    /// Attaches each observation to one known colony: by campaign id; else by a file alias that exactly matches a v1
    /// observation (rejected if the identities disagree); else by a unique <see cref="CampaignMatcher.SameColony"/> match.
    /// None or several candidates: not attached. No colony is ever created for numeric data.
    /// </summary>
    private static Dictionary<Colony, List<MetricObservation>> Attach(Profile profile, IReadOnlyList<Colony> colonies, out int unattributed)
    {
        var byCampaign = new Dictionary<string, HashSet<Colony>>(Ord);
        var byFile = new Dictionary<(string, long, DateTime), List<(Colony Colony, SaveObservation Obs)>>();
        foreach (var c in colonies)
        {
            foreach (var id in c.CampaignIds())
                Add(byCampaign, id, c);
            foreach (var o in c.Observations)
            {
                var k = (o.FileName, o.FileSize, o.LastWriteUtc);
                if (!byFile.TryGetValue(k, out var list))
                    byFile[k] = list = new();
                list.Add((c, o));
            }
        }

        var result = new Dictionary<Colony, List<MetricObservation>>();
        unattributed = 0;
        foreach (var m in profile.MetricObservations)
        {
            if (m.IsQuarantined)
                continue;
            var colony = Find(m, colonies, byCampaign, byFile);
            if (colony is null)
            {
                unattributed++;
                continue;
            }
            if (!result.TryGetValue(colony, out var list))
                result[colony] = list = new();
            list.Add(m);
        }
        return result;
    }

    private static void Add(Dictionary<string, HashSet<Colony>> map, string key, Colony c)
    {
        if (!map.TryGetValue(key, out var set))
            map[key] = set = new();
        set.Add(c);
    }

    private static Colony? Find(MetricObservation m, IReadOnlyList<Colony> colonies, Dictionary<string, HashSet<Colony>> byCampaign,
        Dictionary<(string, long, DateTime), List<(Colony Colony, SaveObservation Obs)>> byFile)
    {
        if (m.Identity.ModCampaignId is { } cid)
            return byCampaign.TryGetValue(cid, out var set) && set.Count == 1 ? set.First() : null;

        var fileMatches = m.FileAliases
            .SelectMany(a => byFile.GetValueOrDefault((a.FileName, a.FileSize, a.LastWriteUtc)) ?? new())
            .ToList();
        if (fileMatches.Count > 0)
        {
            var distinct = fileMatches.Select(f => f.Colony).Distinct().ToList();
            var agrees = fileMatches.All(f => f.Obs.Identity.FingerprintHash() == m.Identity.FingerprintHash()
                && f.Obs.Identity.ModCampaignId == m.Identity.ModCampaignId);
            return distinct.Count == 1 && agrees ? distinct[0] : null;
        }

        if (!m.Identity.HasFingerprint)
            return null;
        var candidates = colonies.Where(c => c.Observations.Select(o => o.Identity).Concat(c.LiveCampaigns.Select(l => l.Identity))
            .Any(id => CampaignMatcher.SameColony(id, m.Identity))).Take(2).ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }

    // ----- one colony -----

    /// <summary>Latest save tick first, then the latest time, then the smallest key.</summary>
    private static MetricObservation? Representative(IEnumerable<MetricObservation> observations)
        => observations.OrderByDescending(o => o.SaveTick).ThenByDescending(o => o.ObservedUtc).ThenBy(o => o.Key, Ord).FirstOrDefault();

    private static MetricEntry? Entry(MetricObservation o, MetricDefinition d)
        => o.Metrics.TryGetValue(d.StoredKey, out var e) && !e.IsQuarantined && e.Revision == d.Revision ? e : null;

    /// <param name="checkpoint">The colony's representative checkpoint (Phase C), chosen once for all its outputs.</param>
    private static NumericRow ColonyRow(MetricDefinition d, List<MetricObservation> all, MetricObservation? checkpoint)
    {
        var observations = all.Where(o => o.Source == d.Source).ToList();
        if (observations.Count == 0)
            return new NumericRow { Definition = d, Value = NumericValue.Unknown, Targets = 1 };

        if (d.Selection != ColonySelection.Representative)
            return AcrossObservations(d, observations);

        var rep = d.Source == MetricObservationSource.Checkpoint ? checkpoint : Representative(observations);
        if (rep is null)
            return new NumericRow { Definition = d, Value = NumericValue.Unknown, Targets = 1 };
        var entry = Entry(rep, d);
        if (entry is null)
            return new NumericRow { Definition = d, Value = DlcAbsent(d, rep) ? NotApplicable(rep) : NumericValue.Unknown, Targets = DlcAbsent(d, rep) ? 0 : 1 };

        var breakdowns = d.HasBreakdown
            ? entry.Values.Where(kv => kv.Key != MetricEntry.Scalar).OrderBy(kv => kv.Key, Ord)
                .Select(kv => Row(d, kv.Key, From(kv.Value, rep.SaveTick))).ToList()
            : new List<NumericRow>();
        NumericValue value;
        if (entry.Values.TryGetValue(MetricEntry.Scalar, out var scalar))
            value = From(scalar, rep.SaveTick);
        else if (d.HasBreakdown)
            value = new NumericValue
            {
                Number = breakdowns.Sum(b => b.Value.Number ?? 0),
                Coverage = Weakest(breakdowns.Select(b => b.Value.Coverage).DefaultIfEmpty(MetricCoverage.SnapshotOnly)),
                ThroughTick = rep.SaveTick,
            };
        else
            value = NumericValue.Unknown;
        // A family writes its zeros whatever the DLC state; a zero of a DLC output in an observation without that DLC says only
        // that the DLC was not there (a value recorded while it was active is shown).
        if (DlcAbsent(d, rep) && NothingRecorded(value) && breakdowns.All(b => NothingRecorded(b.Value)))
            return new NumericRow { Definition = d, Value = NotApplicable(rep), Targets = 0 };
        var row = Row(d, MetricEntry.Scalar, value);
        return new NumericRow { Definition = d, Value = row.Value, Targets = row.Targets, Valid = row.Valid, Breakdowns = breakdowns };
    }

    private static NumericRow AcrossObservations(MetricDefinition d, List<MetricObservation> observations)
    {
        var values = observations.Select(o => (Obs: o, Value: Entry(o, d)?.Values.GetValueOrDefault(MetricEntry.Scalar)))
            .Where(x => x.Value?.Number is not null).ToList();
        if (values.Count == 0)
            return new NumericRow { Definition = d, Value = NumericValue.Unknown, Targets = 1 };
        var best = d.Selection == ColonySelection.ObservationMax
            ? values.OrderByDescending(x => x.Value!.Number).First()
            : values.OrderBy(x => x.Value!.Number).First();
        var partial = values.Count < observations.Count || values.Any(x => x.Value!.Coverage == MetricCoverage.Partial);
        return Row(d, MetricEntry.Scalar, new NumericValue
        {
            Number = best.Value!.Number,
            Coverage = partial ? MetricCoverage.Partial : best.Value.Coverage,
            ThroughTick = observations.Max(o => o.SaveTick),
            SampleCount = values.Count,
        });
    }

    /// <summary>
    /// No value, and the observation's mod list shows the output's DLC was not active: the output does not apply. A value that
    /// was recorded (e.g. while the DLC was active) is always shown, whatever the DLC state at the observation.
    /// </summary>
    private static bool DlcAbsent(MetricDefinition d, MetricObservation o)
        => d.RequiredDlc is { } dlc && o.ActiveDlcIds is { } active && !active.Contains(dlc, StringComparer.OrdinalIgnoreCase);

    private static bool NothingRecorded(NumericValue v)
        => v.Coverage == MetricCoverage.NotApplicable || v.Number is 0 || (v.Number is null && v.Numerator is null or 0 && v.Denominator is null or 0);

    private static NumericValue NotApplicable(MetricObservation o) => new() { Coverage = MetricCoverage.NotApplicable, ThroughTick = o.SaveTick };

    private static NumericValue From(MetricValue v, int tick) => new()
    {
        Number = v.Number,
        Coverage = v.Coverage,
        Numerator = v.Numerator,
        Denominator = v.Denominator,
        SampleCount = v.SampleCount,
        ThroughTick = tick,
    };

    private static NumericRow Row(MetricDefinition d, string breakdown, NumericValue v) => new()
    {
        Definition = d,
        Breakdown = breakdown,
        Value = v,
        Targets = v.Coverage == MetricCoverage.NotApplicable ? 0 : 1,
        Valid = v.Coverage != MetricCoverage.NotApplicable && v.Known ? 1 : 0,
    };

    // ----- all colonies -----

    /// <summary>Complete is stronger than SnapshotOnly, which is stronger than Partial; anything weaker is Partial.</summary>
    private static MetricCoverage Weakest(IEnumerable<MetricCoverage> coverages)
    {
        var result = MetricCoverage.Complete;
        foreach (var c in coverages)
        {
            if (c is MetricCoverage.Partial or MetricCoverage.Unknown)
                return MetricCoverage.Partial;
            if (c == MetricCoverage.SnapshotOnly)
                result = MetricCoverage.SnapshotOnly;
        }
        return result;
    }

    private static NumericRow Combine(MetricDefinition d, List<(string Key, NumericRow Row)> colonies)
    {
        var breakdownKeys = d.HasBreakdown
            ? colonies.SelectMany(c => c.Row.Breakdowns.Select(b => b.Breakdown)).Distinct(Ord).Order(Ord).ToList()
            : new List<string>();
        var breakdowns = breakdownKeys.Select(k => Aggregate(d, k, colonies.Select(c => (c.Key, Value: BreakdownValue(c.Row, k))).ToList())).ToList();
        var scalar = Aggregate(d, MetricEntry.Scalar, colonies.Select(c => (c.Key, c.Row.Value)).ToList());
        return new NumericRow { Definition = d, Value = scalar.Value, Targets = scalar.Targets, Valid = scalar.Valid, Breakdowns = breakdowns };
    }

    /// <summary>A colony whose entry exists but lacks this breakdown key has 0 of it (the breakdown lists every key it has).</summary>
    private static NumericValue BreakdownValue(NumericRow row, string key)
    {
        var b = row.Breakdowns.FirstOrDefault(x => x.Breakdown == key);
        if (b is not null)
            return b.Value;
        return row.Value.Number is null ? row.Value : new NumericValue { Number = 0, Coverage = row.Value.Coverage, ThroughTick = row.Value.ThroughTick };
    }

    private static NumericRow Aggregate(MetricDefinition d, string breakdown, List<(string Key, NumericValue Value)> colonies)
    {
        var applicable = colonies.Where(c => c.Value.Coverage != MetricCoverage.NotApplicable).ToList();
        var known = applicable.Where(c => d.Overall == OverallAggregation.Ratio
            ? c.Value.Numerator is not null && c.Value.Denominator is not null
            : c.Value.Known).ToList();
        NumericValue Result(double? number, string? colony = null, double? num = null, double? den = null)
        {
            if (known.Count == 0)
                return NumericValue.Unknown;
            var coverage = Weakest(known.Select(k => k.Value.Coverage));
            if (known.Count < applicable.Count)
                coverage = MetricCoverage.Partial;
            return new NumericValue { Number = number, Coverage = coverage, ColonyKey = colony, Numerator = num, Denominator = den };
        }

        NumericValue value;
        switch (d.Overall)
        {
            case OverallAggregation.Sum:
                value = Result(known.Sum(k => k.Value.Number ?? 0));
                break;
            case OverallAggregation.Ratio:
                var num = known.Sum(k => k.Value.Numerator!.Value);
                var den = known.Sum(k => k.Value.Denominator!.Value);
                // 0 ÷ 0 over known colonies stays known: no value, but the colonies count as read.
                value = Result(den > 0 ? num / den : null, num: num, den: den);
                break;
            case OverallAggregation.Max or OverallAggregation.Min:
                var withNumber = known.Where(k => k.Value.Number is not null).ToList();
                if (withNumber.Count == 0)
                {
                    value = Result(null);   // every known colony is "not reached"
                    break;
                }
                var max = d.Overall == OverallAggregation.Max;
                var extreme = max ? withNumber.Max(k => k.Value.Number!.Value) : withNumber.Min(k => k.Value.Number!.Value);
                var holder = withNumber.Where(k => k.Value.Number == extreme).Select(k => k.Key).Order(Ord).First();
                value = Result(extreme, holder);
                break;
            default:
                value = NumericValue.Unknown;
                break;
        }
        return new NumericRow { Definition = d, Breakdown = breakdown, Value = value, Targets = applicable.Count, Valid = value.Known ? known.Count : 0 };
    }
}
