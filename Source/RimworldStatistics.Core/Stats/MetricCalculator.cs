namespace RimworldStatistics.Core;

/// <summary>One shown value. Immutable; built off the UI thread and only read while drawing.</summary>
public sealed class NumericValue
{
    public static readonly NumericValue Unknown = new() { Coverage = MetricCoverage.Unknown };

    public double? Number { get; init; }
    public MetricCoverage Coverage { get; init; }

    /// <summary>Capture-time evidence, separate from the covered time range.</summary>
    public MetricEvidence Evidence { get; init; } = MetricEvidence.UnknownLegacy;
    public int? SinceTick { get; init; }
    public double? Numerator { get; init; }
    public double? Denominator { get; init; }
    public int? SampleCount { get; init; }

    /// <summary>The game tick the value runs to (the representative observation's save tick).</summary>
    public int? ThroughTick { get; init; }

    /// <summary>Saved MOD inventory fingerprint, in memory only; never shown or persisted as a new output.</summary>
    public string? ModSetHash { get; init; }

    /// <summary>For an all-colonies maximum or minimum: the colony that holds it (ties: smallest colony key).</summary>
    public string? ColonyKey { get; init; }

    /// <summary>All colonies tied at an extreme, in stable key order.</summary>
    public IReadOnlyList<string>? HolderKeys { get; init; }

    /// <summary>For a ranking over labels (e.g. the scenario with most endings): the label(s) holding it, ties joined.</summary>
    public string? Label { get; init; }

    /// <summary>Known range, no value: a threshold that was not reached (a 0 �� 0 rate is not this: it has a denominator).</summary>
    public bool NotReached => Number is null && Denominator is null && Coverage is not (MetricCoverage.Unknown or MetricCoverage.NotApplicable);

    /// <summary>A value, a threshold not reached, or a known 0 �� 0.</summary>
    public bool Known => Coverage is not (MetricCoverage.Unknown or MetricCoverage.NotApplicable)
        && (Number is not null || NotReached || (Numerator is not null && Denominator is not null));
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
    public bool OutOfScope { get; init; }

    /// <summary>Numeric observations attached to this colony.</summary>
    public int Observations { get; init; }

    /// <summary>What the colony's checkpoints (Phase C) say about its save lineage.</summary>
    public CheckpointLineage Lineage { get; init; }

    public required IReadOnlyList<NumericRow> Rows { get; init; }
}

/// <summary>The numeric projection of the profile for the Details tab (numeric expansion design ��4).</summary>
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
/// observations (numeric expansion design ��2 "�R���j�[�ƍ�", ��3 "�ߋ��ϑ��̑I�ѕ�").
/// </summary>
public static partial class MetricCalculator
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
        // Derived rankings consume selected input rows, but require no extra
        // per-colony observations or save-data lookups of their own.
        var sourceDefinitions = definitions.Where(d => !CrossColonyCatalog.IsDerived(d)).ToList();
        var perColony = new List<(Colony Colony, List<NumericRow> Rows, CheckpointLineage Lineage)>(colonies.Count);
        foreach (var colony in colonies)
        {
            var observations = attached.GetValueOrDefault(colony) ?? new List<MetricObservation>();
            var checkpoints = CheckpointSelector.Select(observations);
            // Group once per colony instead of scanning/allocating a source-specific list
            // for every metric definition. Order within each source stays unchanged.
            var bySource = observations.GroupBy(o => o.Source)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<MetricObservation>)g.ToList());
            // All definitions of one source use the same latest observation. Resolve
            // its order-sensitive tie break only once instead of sorting per row.
            var latestBySource = bySource.ToDictionary(kv => kv.Key, kv => Representative(kv.Value));
            perColony.Add((colony, sourceDefinitions.Select(d =>
            {
                var captured = bySource.GetValueOrDefault(d.Source) ?? Array.Empty<MetricObservation>();
                // Historic save-file evidence is a fallback only when this key
                // was never captured by a game-side SaveSnapshot. Mixing the two
                // would change live maxima or lose stronger evidence.
                if (d.Source == MetricObservationSource.SaveSnapshot
                    && d.Acquisition is { } unit
                    && !captured.Any(o => Entry(o, d) is not null)
                    && bySource.TryGetValue(MetricObservationSource.SaveFile, out var historical)
                    && historical.Any(o => Entry(o, d) is not null
                        && o.EvidenceByAcquisition?.GetValueOrDefault(unit) == MetricEvidence.Experimental))
                {
                    // A later save with no supported evidence must not erase a
                    // partial reading in an older save. Only candidates that
                    // actually captured this unit participate in selection.
                    var capturedHistory = historical.Where(o => Entry(o, d) is not null
                        && o.EvidenceByAcquisition?.GetValueOrDefault(unit) == MetricEvidence.Experimental).ToArray();
                    return ColonyRow(d, capturedHistory, null, Representative(capturedHistory));
                }
                return ColonyRow(d, captured, checkpoints.Representative, latestBySource.GetValueOrDefault(d.Source));
            }).ToList(), checkpoints.Lineage));
        }

        var counted = perColony.Where(c => c.Colony.Counted).ToList();
        var overall = new List<NumericRow>();
        for (var i = 0; i < sourceDefinitions.Count; i++)
        {
            var def = sourceDefinitions[i];
            if (def.Overall is OverallAggregation.IndividualOnly or OverallAggregation.Ranking)
                continue;
            overall.Add(Combine(def, counted.Select(c => (c.Colony.Key, c.Rows[i])).ToList()));
        }
        var countedStats = stats.Colonies.Where(c => !c.Excluded && !c.OutOfScope).ToList();
        // The ranking engine can calculate registered development-only dependencies,
        // but the normal Details view must not leak any output whose acquisition gate
        // has not been verified in game.
        var allowedRankings = definitions.Where(d => d.Overall == OverallAggregation.Ranking)
            .Select(d => d.Key).ToHashSet(Ord);
        var rowsByColony = counted.ToDictionary(c => c.Colony.Key, c => (IReadOnlyList<NumericRow>)c.Rows, Ord);
        overall.AddRange(MetricRankings.Build(countedStats, rowsByColony)
            .Where(row => allowedRankings.Contains(row.Definition.Key)));
        overall.AddRange(CrossColonyAggregator.Build(definitions, rowsByColony));

        return new NumericView
        {
            Overall = overall,
            Colonies = perColony.Select(c => new NumericColony
            {
                Key = c.Colony.Key,
                Excluded = c.Colony.Excluded,
                OutOfScope = c.Colony.OutOfScope,
                Observations = (int)Math.Min(int.MaxValue, attached.GetValueOrDefault(c.Colony)?.Sum(ObservationWeight) ?? 0L),
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
                unattributed = (int)Math.Min(int.MaxValue, (long)unattributed + ObservationWeight(m));
                continue;
            }
            if (!result.TryGetValue(colony, out var list))
                result[colony] = list = new();
            list.Add(m);
        }
        return result;
    }

    /// <summary>A compacted SaveSnapshot still represents its original save-time observations.</summary>
    private static long ObservationWeight(MetricObservation o)
        => o.Source == MetricObservationSource.SaveSnapshot
            ? Math.Max(1, o.CompactedObservationCount ?? 1) : 1L;

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
        => MetricObservationOrder.Latest(observations);

    private static MetricEntry? Entry(MetricObservation o, MetricDefinition d)
        => o.Metrics.TryGetValue(d.StoredKey, out var e) && !e.IsQuarantined && e.Revision == d.Revision ? e : null;

    /// <param name="checkpoint">The colony's representative checkpoint (Phase C), chosen once for all its outputs.</param>
    private static NumericRow ColonyRow(MetricDefinition d, IReadOnlyList<MetricObservation> observations,
        MetricObservation? checkpoint, MetricObservation? latest)
    {
        if (observations.Count == 0)
            return new NumericRow { Definition = d, Value = NumericValue.Unknown, Targets = 1 };

        if (d.Selection != ColonySelection.Representative)
            return AcrossObservations(d, observations);

        var rep = d.Source == MetricObservationSource.Checkpoint ? checkpoint : latest;
        var staleCapture = false;
        // A removed optional provider or disabled experimental faction unit
        // cannot produce a reading in newer saves. Retain the last genuine
        // captured value as partial data; never substitute a weaker
        // historical SaveFile fallback after a live capture was made.
        if (SnapshotSelectionPolicy.UsesLastCapture(d)
            && (rep is null || Entry(rep, d) is null))
        {
            var historical = MetricObservationOrder.Latest(observations.Where(o => Entry(o, d) is not null));
            if (historical is not null)
            {
                rep = historical;
                staleCapture = true;
            }
        }
        if (rep is null)
            return new NumericRow { Definition = d, Value = NumericValue.Unknown, Targets = 1 };
        var entry = Entry(rep, d);
        if (entry is null)
            return new NumericRow { Definition = d, Value = DlcAbsent(d, rep) ? NotApplicable(rep) : NumericValue.Unknown, Targets = DlcAbsent(d, rep) ? 0 : 1 };

        var breakdowns = d.HasBreakdown
            ? entry.Values.Where(kv => kv.Key != MetricEntry.Scalar).OrderBy(kv => kv.Key, Ord)
                .Select(kv => Row(d, kv.Key, From(kv.Value, rep, d))).ToList()
            : new List<NumericRow>();
        NumericValue value;
        if (entry.Values.TryGetValue(MetricEntry.Scalar, out var scalar))
            value = From(scalar, rep, d);
        else if (d.HasBreakdown)
            value = new NumericValue
            {
                Number = breakdowns.Count == 0 || breakdowns.Any(b => b.Value.Known) ? breakdowns.Sum(b => b.Value.Number ?? 0) : null,
                Coverage = Weakest(breakdowns.Select(b => b.Value.Coverage).DefaultIfEmpty(MetricCoverage.SnapshotOnly)),
                ThroughTick = rep.SaveTick,
                Evidence = EvidenceFor(rep, d),
            };
        else
            value = NumericValue.Unknown;
        if (staleCapture && value.Known)
        {
            value = new NumericValue
            {
                Number = value.Number,
                Coverage = MetricCoverage.Partial,
                Evidence = value.Evidence,
                Numerator = value.Numerator, Denominator = value.Denominator,
                SinceTick = value.SinceTick, ThroughTick = value.ThroughTick,
                ModSetHash = value.ModSetHash,
                SampleCount = value.SampleCount,
            };
        }
        // A family writes its zeros whatever the DLC state; a zero of a DLC output in an observation without that DLC says only
        // that the DLC was not there (a value recorded while it was active is shown).
        if (DlcAbsent(d, rep) && NothingRecorded(value) && breakdowns.All(b => NothingRecorded(b.Value)))
            return new NumericRow { Definition = d, Value = NotApplicable(rep), Targets = 0 };
        var row = Row(d, MetricEntry.Scalar, value);
        return new NumericRow { Definition = d, Value = row.Value, Targets = row.Targets, Valid = row.Valid, Breakdowns = breakdowns };
    }

    private static NumericRow AcrossObservations(MetricDefinition d, IReadOnlyList<MetricObservation> observations)
    {
        // A zero emitted while the DLC was absent is not an observed zero and must not
        // take part in a historical maximum/minimum. A nonzero, explicitly recorded
        // value remains evidence even when the DLC list says it was absent.
        var applicable = observations.Where(o => !DlcAbsent(d, o)
            || MeasuredNumber(Entry(o, d)?.Values.GetValueOrDefault(MetricEntry.Scalar)) is { } recorded && recorded != 0).ToList();
        if (applicable.Count == 0)
            return Row(d, MetricEntry.Scalar, new NumericValue { Coverage = MetricCoverage.NotApplicable });
        var values = applicable.Select(o => (Obs: o, Value: Entry(o, d)?.Values.GetValueOrDefault(MetricEntry.Scalar)))
            .Where(x => MeasuredNumber(x.Value) is not null).ToList();
        if (values.Count == 0)
            return new NumericRow { Definition = d, Value = NumericValue.Unknown, Targets = 1 };
        var best = d.Selection == ColonySelection.ObservationMax
            ? values.OrderByDescending(x => x.Value!.Number).First()
            : values.OrderBy(x => x.Value!.Number).First();
        // A compacted history retains a witness for any missing applicable reading;
        // raw value counts are no longer interchangeable with observation counts.
        var partial = applicable.Any(o => MeasuredNumber(Entry(o, d)?.Values.GetValueOrDefault(MetricEntry.Scalar)) is null)
            || values.Any(x => x.Value!.Coverage == MetricCoverage.Partial);
        var row = Row(d, MetricEntry.Scalar, new NumericValue
        {
            Number = best.Value!.Number,
            Coverage = partial ? MetricCoverage.Partial : best.Value.Coverage,
            ThroughTick = observations.Max(o => o.SaveTick),
            Evidence = MetricEvidencePolicy.Combine(values.Select(x => EvidenceFor(x.Obs, d))),
            SampleCount = (int)Math.Min(int.MaxValue, values.Sum(x => (long)Math.Max(1,
                x.Obs.CompactedSampleCounts?.GetValueOrDefault(d.StoredKey) ?? 1))),
        });
        if (!d.HasBreakdown || Entry(best.Obs, d) is not { } witness)
            return row;

        // A breakdown describes the winning scalar's observation, so its
        // counts refer to one actual save and remain valid after compaction.
        var breakdowns = witness.Values.Where(kv => kv.Key != MetricEntry.Scalar)
            .OrderBy(kv => kv.Key, Ord)
            .Select(kv => Row(d, kv.Key, From(kv.Value, best.Obs, d))).ToArray();
        return new NumericRow { Definition = d, Value = row.Value, Targets = row.Targets, Valid = row.Valid, Breakdowns = breakdowns };
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

    private static MetricEvidence EvidenceFor(MetricObservation observation, MetricDefinition definition)
    {
        var direct = definition.Acquisition is { } id && observation.EvidenceByAcquisition is { } evidence
            && evidence.TryGetValue(id, out var captured) ? captured : MetricEvidence.UnknownLegacy;
        // Value-type GetValueOrDefault returns default(UnknownLegacy) when an unrelated
        // compacted key exists; check presence before weakening capture confidence.
        return observation.CompactedEvidenceByMetric is { } metadata
            && metadata.TryGetValue(definition.StoredKey, out var compacted)
            ? MetricEvidencePolicy.Combine(direct, compacted) : direct;
    }

    internal static double? MeasuredNumber(MetricValue? value)
        => value?.Coverage is null or MetricCoverage.Unknown or MetricCoverage.NotApplicable ? null : value.Number;

    private static NumericValue From(MetricValue v, MetricObservation observation, MetricDefinition definition) => new()
    {
        Number = MeasuredNumber(v),
        Coverage = v.Coverage,
        Evidence = EvidenceFor(observation, definition),
        Numerator = v.Numerator,
        Denominator = v.Denominator,
        SampleCount = v.SampleCount,
        SinceTick = v.SinceTick,
        ThroughTick = v.ThroughTick ?? observation.SaveTick,
        ModSetHash = observation.ModSetHash,
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
        var breakdowns = new List<NumericRow>();
        if (d.HasBreakdown)
        {
            // Each colony's per-Def dictionary is indexed once. Previously every
            // global key searched all children and re-summed the source to decide
            // whether a missing key was a *known zero* (O(colonies * keys2)).
            // Preserve the first-child-wins behavior for malformed duplicate keys.
            var lookups = colonies.Select(c =>
            {
                var byKey = new Dictionary<string, NumericValue>(Ord);
                foreach (var child in c.Row.Breakdowns)
                    byKey.TryAdd(child.Breakdown, child.Value);
                return (c.Key, ByKey: byKey, Missing: MissingBreakdownValue(c.Row));
            }).ToArray();
            var keys = lookups.SelectMany(c => c.ByKey.Keys).Distinct(Ord).Order(Ord);
            foreach (var key in keys)
            {
                var values = lookups.Select(c => (c.Key, Value:
                    c.ByKey.TryGetValue(key, out var value) ? value : c.Missing)).ToList();
                breakdowns.Add(Aggregate(d, key, values));
            }
        }
        var scalar = Aggregate(d, MetricEntry.Scalar, colonies.Select(c => (c.Key, c.Row.Value)).ToList());
        return new NumericRow { Definition = d, Value = scalar.Value, Targets = scalar.Targets, Valid = scalar.Valid, Breakdowns = breakdowns };
    }

    // These event streams have an exhaustive per-kind split in the new decoder.
    // Legacy scalars and overflowed child sets cannot prove a missing kind was zero.
    private static readonly IReadOnlySet<string> ExhaustiveEventBreakdowns = new HashSet<string>(Ord)
    {
        TradeFamily.Deals, TradeFamily.SalesUnits, TradeFamily.BuysUnits,
        WorkFamily.Sown_, WorkFamily.HarvestByCrop, WorkFamily.MealsByDef,
        WorkDetailsFamily.CraftedByDef, WorkDetailsFamily.MinedByDef,
        MedicineFamily.Surgeries, FishingFamily.AcceptedUnitsByDef, ModdedIncidentFamily.ByDef,
    };

    /// <summary>
    /// One cached fallback for each colony. Missing breakdown keys imply zero
    /// only if the source's entire parent/split relationship proves it.
    /// </summary>
    private static NumericValue MissingBreakdownValue(NumericRow row)
    {
        if (row.Value.Number is null)
            return row.Value;

        if (ExhaustiveEventBreakdowns.Contains(row.Definition.Key))
        {
            var complete = row.Value.Coverage is MetricCoverage.Complete or MetricCoverage.SnapshotOnly
                && row.Breakdowns.All(child => child.Breakdown != MetricKeys.UnknownBucket && child.Value.Number is not null
                    && child.Value.Coverage is MetricCoverage.Complete or MetricCoverage.SnapshotOnly)
                && Math.Abs(row.Breakdowns.Sum(child => child.Value.Number ?? 0) - row.Value.Number.Value) < 1e-6;
            if (!complete)
                return new NumericValue
                {
                    Coverage = MetricCoverage.Unknown, Evidence = row.Value.Evidence,
                    SinceTick = row.Value.SinceTick, ThroughTick = row.Value.ThroughTick,
                };
        }

        return new NumericValue
        {
            Number = 0, Coverage = row.Value.Coverage, Evidence = row.Value.Evidence,
            SinceTick = row.Value.SinceTick, ThroughTick = row.Value.ThroughTick,
        };
    }

    private static NumericRow Aggregate(MetricDefinition d, string breakdown, List<(string Key, NumericValue Value)> colonies)
    {
        var applicable = colonies.Where(c => c.Value.Coverage != MetricCoverage.NotApplicable).ToList();
        var known = applicable.Where(c => c.Value.Known && (d.Overall != OverallAggregation.Ratio
            || (c.Value.Numerator is not null && c.Value.Denominator is not null))).ToList();
        NumericValue Result(double? number, string? colony = null, double? num = null, double? den = null)
        {
            if (known.Count == 0)
                return NumericValue.Unknown;
            var coverage = Weakest(known.Select(k => k.Value.Coverage));
            if (known.Count < applicable.Count)
                coverage = MetricCoverage.Partial;
            return new NumericValue
            {
                Number = number, Coverage = coverage,
                Evidence = MetricEvidencePolicy.Combine(known.Select(k => k.Value.Evidence)),
                ColonyKey = colony, Numerator = num, Denominator = den,
            };
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
                // 0 �� 0 over known colonies stays known: no value, but the colonies count as read.
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
