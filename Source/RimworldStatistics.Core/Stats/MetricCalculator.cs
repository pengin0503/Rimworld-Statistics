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

    /// <summary>
    /// A live total recorded without a gap from <see cref="SinceTick"/> through its save and not weakened by its family: it
    /// counts exactly what was recorded in that range, even when recording started after the game did (#307). Complete
    /// values are gapless too; a partial value from a checkpoint read before this was kept is not. In memory only.
    /// </summary>
    public bool Gapless { get; init; }

    /// <summary>Saved MOD inventory fingerprint, in memory only; never shown or persisted as a new output.</summary>
    public string? ModSetHash { get; init; }

    /// <summary>For an all-colonies maximum or minimum: the colony that holds it (ties: smallest colony key).</summary>
    public string? ColonyKey { get; init; }

    /// <summary>All colonies tied at an extreme, in stable key order.</summary>
    public IReadOnlyList<string>? HolderKeys { get; init; }

    /// <summary>For a ranking over labels (e.g. the scenario with most endings): the label(s) holding it, ties joined.</summary>
    public string? Label { get; init; }

    /// <summary>
    /// For a cross-colony top item or category (<c>cross.top.*</c>): every breakdown key tied at the top, in stable key
    /// order, so the UI names each key as its breakdown row does (#334). <see cref="Label"/> joins the same keys
    /// (<see cref="CrossColonyAggregator.TiedLabel"/>). In memory only.
    /// </summary>
    public IReadOnlyList<string>? LabelKeys { get; init; }

    /// <summary>
    /// The output has no additive scalar (e.g. a goodwill min/max range) and is measured through its breakdown rows. It has no
    /// number of its own, but it is a known reading, never a threshold that was not reached (#329). In memory only.
    /// </summary>
    public bool BreakdownOnly { get; init; }

    /// <summary>Known range, no value: a threshold that was not reached (a 0 ÷ 0 rate is not this: it has a denominator).</summary>
    public bool NotReached => Number is null && Denominator is null && !BreakdownOnly
        && Coverage is not (MetricCoverage.Unknown or MetricCoverage.NotApplicable);

    /// <summary>A value, a threshold not reached, a known 0 ÷ 0, or a reading measured through its breakdowns.</summary>
    public bool Known => Coverage is not (MetricCoverage.Unknown or MetricCoverage.NotApplicable)
        && (Number is not null || NotReached || BreakdownOnly || (Numerator is not null && Denominator is not null));
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
        => BuildViewCancellable(profile, colonies, stats, definitions, CancellationToken.None);

    /// <param name="definitions">The outputs to build (the published registry, or the whole catalog for verification).</param>
    internal static NumericView BuildViewCancellable(Profile profile, IReadOnlyList<Colony> colonies, StatsResult stats, IReadOnlyList<MetricDefinition> definitions, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var attached = Attach(profile, colonies, out var unattributed);
        return BuildViewWithAttached(profile, colonies, stats, definitions, attached, unattributed, ct);
    }

    internal static NumericView BuildViewWithAttached(
        Profile profile,
        IReadOnlyList<Colony> colonies,
        StatsResult stats,
        IReadOnlyList<MetricDefinition> definitions,
        Dictionary<Colony, List<MetricObservation>> attached,
        int unattributed,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Derived rankings consume selected input rows, but require no extra
        // per-colony observations or save-data lookups of their own.
        var sourceDefinitions = definitions.Where(d => !CrossColonyCatalog.IsDerived(d)).ToList();
        var perColony = new List<(Colony Colony, List<NumericRow> Rows, CheckpointLineage Lineage)>(colonies.Count);
        foreach (var colony in colonies)
        {
            ct.ThrowIfCancellationRequested();
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
            ct.ThrowIfCancellationRequested();
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
    internal static Dictionary<Colony, List<MetricObservation>> Attach(Profile profile, IReadOnlyList<Colony> colonies, out int unattributed)
    {
        if (profile.MetricObservations.Count == 0)
        {
            unattributed = 0;
            return new Dictionary<Colony, List<MetricObservation>>();
        }
        var byCampaign = new Dictionary<string, HashSet<Colony>>(Ord);
        var byFile = new Dictionary<(string, long, DateTime), List<(Colony Colony, SaveObservation Obs)>>();
        var byFingerprint = new Dictionary<string, List<(Colony Colony, HashSet<string> Pawns)>>(Ord);
        foreach (var c in colonies)
        {
            foreach (var id in c.CampaignIds())
                Add(byCampaign, id, c);
            foreach (var o in c.Observations)
            {
                var utc = UtcTime.ToUtc(o.LastWriteUtc);
                var k = (o.FileName, o.FileSize, utc);
                if (!byFile.TryGetValue(k, out var list))
                    byFile[k] = list = new();
                list.Add((c, o));
                AddFingerprintEntry(byFingerprint, c, o.Identity);
            }
            foreach (var live in c.LiveCampaigns)
                AddFingerprintEntry(byFingerprint, c, live.Identity);
        }

        var result = new Dictionary<Colony, List<MetricObservation>>();
        unattributed = 0;
        foreach (var m in profile.MetricObservations)
        {
            if (m.IsQuarantined)
                continue;
            var colony = Find(m, byCampaign, byFile, byFingerprint);
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

    private static void AddFingerprintEntry(
        Dictionary<string, List<(Colony Colony, HashSet<string> Pawns)>> byFingerprint,
        Colony colony,
        ColonyIdentity? identity)
    {
        if (identity is not { HasFingerprint: true } || identity.FingerprintHash() is not { } hash)
            return;
        if (!byFingerprint.TryGetValue(hash, out var list))
            byFingerprint[hash] = list = new();
        list.Add((colony, new HashSet<string>(identity.StartingPawnIds, Ord)));
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

    private static Colony? Find(
        MetricObservation m,
        Dictionary<string, HashSet<Colony>> byCampaign,
        Dictionary<(string, long, DateTime), List<(Colony Colony, SaveObservation Obs)>> byFile,
        Dictionary<string, List<(Colony Colony, HashSet<string> Pawns)>> byFingerprint)
    {
        if (m.Identity is null)
            return null;
        if (m.Identity.ModCampaignId is { } cid)
            return byCampaign.TryGetValue(cid, out var set) && set.Count == 1 ? set.First() : null;

        Colony? matchedColony = null;
        var anyFileMatch = false;
        var disagrees = false;
        string? obsFingerprint = null;
        foreach (var a in m.FileAliases)
        {
            if (!byFile.TryGetValue((a.FileName, a.FileSize, UtcTime.ToUtc(a.LastWriteUtc)), out var hits))
                continue;
            obsFingerprint ??= m.Identity.FingerprintHash();
            for (var i = 0; i < hits.Count; i++)
            {
                anyFileMatch = true;
                var hit = hits[i];
                if (hit.Obs.Identity is null || hit.Obs.Identity.FingerprintHash() != obsFingerprint
                    || hit.Obs.Identity.ModCampaignId != m.Identity.ModCampaignId)
                {
                    disagrees = true;
                    break;
                }
                if (matchedColony is null)
                    matchedColony = hit.Colony;
                else if (!ReferenceEquals(matchedColony, hit.Colony))
                {
                    disagrees = true;
                    break;
                }
            }
            if (disagrees)
                break;
        }
        if (anyFileMatch)
            return disagrees ? null : matchedColony;

        if (!m.Identity.HasFingerprint || m.Identity.FingerprintHash() is not { } fpHash)
            return null;
        if (!byFingerprint.TryGetValue(fpHash, out var candidates))
            return null;
        var obsPawns = new HashSet<string>(m.Identity.StartingPawnIds, Ord);
        Colony? uniqueColony = null;
        for (var i = 0; i < candidates.Count; i++)
        {
            var entry = candidates[i];
            if (ReferenceEquals(uniqueColony, entry.Colony))
                continue;
            if (obsPawns.IsSubsetOf(entry.Pawns) || entry.Pawns.IsSubsetOf(obsPawns))
            {
                if (uniqueColony is not null)
                    return null;
                uniqueColony = entry.Colony;
            }
        }
        return uniqueColony;
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
            var historical = MetricObservationOrder.Latest(observations.Where(o => !o.Conflicted && Entry(o, d) is not null));
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
        {
            value = From(scalar, rep, d);
            // A scalar written as "not applicable" over measured breakdowns means the output has no additive total, not
            // that it does not apply: the parent is a known reading of its breakdowns (#329, the goodwill range).
            if (d.HasBreakdown && scalar.Coverage == MetricCoverage.NotApplicable
                && breakdowns.Where(b => b.Value.Known).ToList() is { Count: > 0 } measured)
                value = new NumericValue
                {
                    BreakdownOnly = true,
                    Coverage = Weakest(measured.Select(b => b.Value.Coverage)),
                    Evidence = value.Evidence,
                    SinceTick = value.SinceTick,
                    ThroughTick = value.ThroughTick,
                    ModSetHash = value.ModSetHash,
                };
        }
        else if (d.HasBreakdown)
            value = new NumericValue
            {
                Number = breakdowns.Count == 0 || breakdowns.Any(b => b.Value.Known) ? breakdowns.Sum(b => b.Value.Number ?? 0) : null,
                // A checkpoint written before its family stored this scalar (#330): the family's range is carried by every
                // breakdown, and an empty breakdown of a live total is a lower bound, not a save-time snapshot.
                Coverage = Weakest(breakdowns.Select(b => b.Value.Coverage).DefaultIfEmpty(
                    d.Source == MetricObservationSource.Checkpoint ? MetricCoverage.Partial : MetricCoverage.SnapshotOnly)),
                SinceTick = breakdowns.Select(b => b.Value.SinceTick).Where(t => t is not null).DefaultIfEmpty(null).Min(),
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
                BreakdownOnly = value.BreakdownOnly,
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

    private static bool BeatsObservation(MetricObservation candidate, MetricObservation current)
    {
        if (candidate.SaveTick != current.SaveTick)
            return candidate.SaveTick > current.SaveTick;
        var cmp = UtcTime.ToUtc(candidate.ObservedUtc).CompareTo(UtcTime.ToUtc(current.ObservedUtc));
        if (cmp != 0)
            return cmp > 0;
        return string.CompareOrdinal(candidate.Key, current.Key) < 0;
    }

    private static NumericRow AcrossObservations(MetricDefinition d, IReadOnlyList<MetricObservation> observations)
    {
        var anyNonConflicted = false;
        var anyApplicable = false;
        var anyMissing = false;
        var anyPartial = false;
        var maxSaveTick = int.MinValue;
        long totalSamples = 0;
        var values = new List<(MetricObservation Obs, NumericValue Value)>();

        for (var i = 0; i < observations.Count; i++)
        {
            var o = observations[i];
            if (o.Conflicted)
                continue;
            anyNonConflicted = true;
            if (o.SaveTick > maxSaveTick)
                maxSaveTick = o.SaveTick;

            var scalar = Entry(o, d)?.Values.GetValueOrDefault(MetricEntry.Scalar);
            var measured = MeasuredNumber(scalar);
            // A zero emitted while the DLC was absent is not an observed zero and must not
            // take part in a historical maximum/minimum. A nonzero, explicitly recorded
            // value remains evidence even when the DLC list says it was absent.
            if (DlcAbsent(d, o) && !(measured is { } recorded && recorded != 0))
                continue;
            anyApplicable = true;

            if (measured is null || !double.IsFinite(measured.Value))
            {
                anyMissing = true;
                continue;
            }
            var converted = From(scalar!, o, d);
            values.Add((o, converted));
            if (converted.Coverage == MetricCoverage.Partial)
                anyPartial = true;
            totalSamples += Math.Max(1, o.CompactedSampleCounts?.GetValueOrDefault(d.StoredKey) ?? 1);
        }

        if (!anyNonConflicted)
            return new NumericRow { Definition = d, Value = NumericValue.Unknown, Targets = 1 };
        if (!anyApplicable)
            return Row(d, MetricEntry.Scalar, new NumericValue { Coverage = MetricCoverage.NotApplicable });
        if (values.Count == 0)
            return new NumericRow { Definition = d, Value = NumericValue.Unknown, Targets = 1 };

        var minMode = d.Selection == ColonySelection.ObservationMin;
        var best = values[0];
        var bestNum = best.Value.Number!.Value;
        for (var i = 1; i < values.Count; i++)
        {
            var cur = values[i];
            var curNum = cur.Value.Number!.Value;
            var better = minMode ? curNum < bestNum : curNum > bestNum;
            if (better || (curNum == bestNum && BeatsObservation(cur.Obs, best.Obs)))
            {
                best = cur;
                bestNum = curNum;
            }
        }

        var partial = anyMissing || anyPartial;
        var evidence = values[0].Obs is { } firstObs ? EvidenceFor(firstObs, d) : MetricEvidence.UnknownLegacy;
        if (values.Count > 1)
            evidence = MetricEvidencePolicy.Combine(values.Select(x => EvidenceFor(x.Obs, d)));

        var row = Row(d, MetricEntry.Scalar, new NumericValue
        {
            Number = best.Value.Number,
            Coverage = partial ? MetricCoverage.Partial : best.Value.Coverage,
            ThroughTick = maxSaveTick,
            Evidence = evidence,
            SampleCount = (int)Math.Min(int.MaxValue, totalSamples),
        });
        if (!d.HasBreakdown || Entry(best.Obs, d) is not { } witness)
            return row;

        // A breakdown describes the winning scalar's observation, so its
        // counts refer to one actual save and remain valid after compaction.
        var breakdowns = d.Selection == ColonySelection.ObservationMaxByBreakdown
            ? BreakdownMaxima(d, values, row.Value)
            : witness.Values.Where(kv => kv.Key != MetricEntry.Scalar)
                .OrderBy(kv => kv.Key, Ord)
                .Select(kv => Row(d, kv.Key, From(kv.Value, best.Obs, d))).ToArray();
        return new NumericRow { Definition = d, Value = row.Value, Targets = row.Targets, Valid = row.Valid, Breakdowns = breakdowns };
    }

    private static NumericRow[] BreakdownMaxima(MetricDefinition definition,
        List<(MetricObservation Obs, NumericValue Value)> observations, NumericValue scalar)
    {
        var peaks = new Dictionary<string, (MetricValue Value, MetricObservation Obs)>(Ord);
        for (var i = 0; i < observations.Count; i++)
        {
            var obs = observations[i].Obs;
            var entry = Entry(obs, definition);
            if (entry is null)
                continue;
            foreach (var (key, val) in entry.Values)
            {
                if (key == MetricEntry.Scalar)
                    continue;
                var num = MeasuredNumber(val);
                if (num is null || !double.IsFinite(num.Value))
                    continue;
                if (!peaks.TryGetValue(key, out var existing)
                    || val.Number > existing.Value.Number
                    || (val.Number == existing.Value.Number && BeatsObservation(obs, existing.Obs)))
                {
                    peaks[key] = (val, obs);
                }
            }
        }
        return peaks
            .OrderBy(kv => kv.Key, Ord)
            .Select(kv => Row(definition, kv.Key, new NumericValue
            {
                Number = kv.Value.Value.Number,
                Coverage = scalar.Coverage,
                Evidence = scalar.Evidence,
                SinceTick = kv.Value.Value.SinceTick,
                ThroughTick = kv.Value.Obs.SaveTick,
            })).ToArray();
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
        Gapless = Gapless(v, observation, definition),
    };

    private static bool Gapless(MetricValue v, MetricObservation observation, MetricDefinition definition)
    {
        if (v.Coverage == MetricCoverage.Complete)
            return true;
        return v.Coverage == MetricCoverage.Partial && v.Weakened != true
            && definition.Source == MetricObservationSource.Checkpoint && observation.Source == MetricObservationSource.Checkpoint
            && v.SinceTick is not null && v.ThroughTick == observation.SaveTick
            && definition.Acquisition is { Length: > 0 } family
            && observation.ContinuousFamilies is { } continuous && continuous.Contains(family, StringComparer.Ordinal);
    }

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
        var reuseBuffer = new List<(string Key, NumericValue Value)>(colonies.Count);
        if (d.HasBreakdown)
        {
            // Each colony's per-Def dictionary is indexed once. Previously every
            // global key searched all children and re-summed the source to decide
            // whether a missing key was a *known zero* (O(colonies * keys²)).
            // Preserve the first-child-wins behavior for malformed duplicate keys.
            var lookups = new (string Key, Dictionary<string, NumericValue> ByKey, NumericValue Missing)[colonies.Count];
            var allKeys = new HashSet<string>(Ord);
            for (var i = 0; i < colonies.Count; i++)
            {
                var c = colonies[i];
                var byKey = new Dictionary<string, NumericValue>(c.Row.Breakdowns.Count, Ord);
                for (var j = 0; j < c.Row.Breakdowns.Count; j++)
                {
                    var child = c.Row.Breakdowns[j];
                    if (byKey.TryAdd(child.Breakdown, child.Value))
                        allKeys.Add(child.Breakdown);
                }
                lookups[i] = (c.Key, byKey, MissingBreakdownValue(c.Row));
            }
            foreach (var key in allKeys.Order(Ord))
            {
                reuseBuffer.Clear();
                for (var i = 0; i < lookups.Length; i++)
                {
                    var item = lookups[i];
                    reuseBuffer.Add((item.Key, item.ByKey.TryGetValue(key, out var val) ? val : item.Missing));
                }
                breakdowns.Add(Aggregate(d, key, reuseBuffer));
            }
        }
        reuseBuffer.Clear();
        for (var i = 0; i < colonies.Count; i++)
            reuseBuffer.Add((colonies[i].Key, colonies[i].Row.Value));
        var scalar = Aggregate(d, MetricEntry.Scalar, reuseBuffer);
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
        MembershipFamily.JoinsByXenotype, HealthDeathFamily.DeathsByXenotype,
    };

    /// <summary>
    /// One cached fallback for each colony. Missing breakdown keys imply zero
    /// only if the source's entire parent/split relationship proves it.
    /// </summary>
    private static NumericValue MissingBreakdownValue(NumericRow row)
    {
        if (row.Value.Number is null)
            return row.Value;

        if (row.Definition.Selection == ColonySelection.ObservationMaxByBreakdown &&
            row.Value.Coverage == MetricCoverage.Partial)
            return new NumericValue
            {
                Coverage = MetricCoverage.Unknown, Evidence = row.Value.Evidence,
                SinceTick = row.Value.SinceTick, ThroughTick = row.Value.ThroughTick,
            };

        if (ExhaustiveEventBreakdowns.Contains(row.Definition.Key)
            || (row.Definition.ProviderId is not null
                && row.Definition.Source == MetricObservationSource.Checkpoint
                && row.Definition.HasBreakdown))
        {
            double breakdownSum = 0;
            var complete = row.Value.Coverage is MetricCoverage.Complete or MetricCoverage.SnapshotOnly;
            if (complete)
            {
                for (var i = 0; i < row.Breakdowns.Count; i++)
                {
                    var child = row.Breakdowns[i];
                    if (child.Breakdown == MetricKeys.UnknownBucket
                        || child.Value.Number is not { } childNum
                        || !double.IsFinite(childNum)
                        || child.Value.Coverage is not (MetricCoverage.Complete or MetricCoverage.SnapshotOnly))
                    {
                        complete = false;
                        break;
                    }
                    breakdownSum += childNum;
                }
                if (complete)
                    complete = UtcTime.RelativeEquals(breakdownSum, row.Value.Number.Value);
            }
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
        var applicableCount = 0;
        var knownCount = 0;
        var anyNonFinite = false;
        double sum = 0;
        double num = 0;
        double den = 0;
        var maxMode = d.Overall == OverallAggregation.Max;
        var hasExtreme = false;
        double extreme = 0;
        var holder = "";
        var weakest = MetricCoverage.Complete;
        var evidence = MetricEvidence.UnknownLegacy;
        var firstKnown = true;

        for (var i = 0; i < colonies.Count; i++)
        {
            var c = colonies[i];
            if (c.Value.Coverage == MetricCoverage.NotApplicable)
                continue;
            applicableCount++;
            if (!c.Value.Known)
                continue;
            if (d.Overall == OverallAggregation.Ratio && (c.Value.Numerator is null || c.Value.Denominator is null))
                continue;

            knownCount++;
            if (c.Value.Coverage is MetricCoverage.Partial or MetricCoverage.Unknown)
                weakest = MetricCoverage.Partial;
            else if (c.Value.Coverage == MetricCoverage.SnapshotOnly && weakest != MetricCoverage.Partial)
                weakest = MetricCoverage.SnapshotOnly;
            evidence = firstKnown ? c.Value.Evidence : MetricEvidencePolicy.Combine(evidence, c.Value.Evidence);
            firstKnown = false;

            switch (d.Overall)
            {
                case OverallAggregation.Sum:
                    if (c.Value.Number is { } sv)
                    {
                        if (!double.IsFinite(sv))
                            anyNonFinite = true;
                        else
                        {
                            sum += sv;
                            if (!double.IsFinite(sum))
                                anyNonFinite = true;
                        }
                    }
                    break;
                case OverallAggregation.Ratio:
                    num += c.Value.Numerator!.Value;
                    den += c.Value.Denominator!.Value;
                    break;
                case OverallAggregation.Max or OverallAggregation.Min:
                    if (c.Value.Number is { } ev && double.IsFinite(ev))
                    {
                        if (!hasExtreme || (maxMode ? ev > extreme : ev < extreme)
                            || (ev == extreme && Ord.Compare(c.Key, holder) < 0))
                        {
                            extreme = ev;
                            holder = c.Key;
                            hasExtreme = true;
                        }
                    }
                    break;
            }
        }

        NumericValue Result(double? number, string? colony = null, double? rNum = null, double? rDen = null)
        {
            if (knownCount == 0)
                return NumericValue.Unknown;
            var coverage = knownCount < applicableCount ? MetricCoverage.Partial : weakest;
            return new NumericValue
            {
                Number = number, Coverage = coverage,
                Evidence = evidence,
                ColonyKey = colony, Numerator = rNum, Denominator = rDen,
            };
        }

        NumericValue value;
        switch (d.Overall)
        {
            case OverallAggregation.Sum:
                value = knownCount == 0 || anyNonFinite || !double.IsFinite(sum)
                    ? NumericValue.Unknown
                    : Result(sum);
                break;
            case OverallAggregation.Ratio:
                if (knownCount == 0 || !double.IsFinite(num) || !double.IsFinite(den))
                    value = NumericValue.Unknown;
                else
                {
                    var ratio = den > 0 ? num / den : (double?)null;
                    if (ratio is { } r && !double.IsFinite(r))
                        value = NumericValue.Unknown;
                    else
                        value = Result(ratio, rNum: num, rDen: den);
                }
                break;
            case OverallAggregation.Max or OverallAggregation.Min:
                value = !hasExtreme ? Result(null) : Result(extreme, holder);
                break;
            default:
                value = NumericValue.Unknown;
                break;
        }
        return new NumericRow { Definition = d, Breakdown = breakdown, Value = value, Targets = applicableCount, Valid = value.Known ? knownCount : 0 };
    }
}
