namespace RimworldStatistics.Core;

/// <summary>
/// An acquisition family of Phase C/D2: its stored-state revision and how its totals become metric values. The name is the
/// metrics ledger's section name (numeric expansion design §2 "機能登録時の取得単位は台帳の節名に合わせる").
/// </summary>
public sealed class LiveFamily
{
    public required string Name { get; init; }

    /// <summary>The revision of the stored totals; totals of another revision are never continued or interpreted.</summary>
    public required int Revision { get; init; }

    /// <summary>Package id of the DLC the family needs; null for Core.</summary>
    public string? RequiredDlc { get; init; }

    public required Action<FamilyOutputContext> Build { get; init; }

    /// <summary>
    /// Called before an interruption (a gap, a stop, a resume after unrecorded time) drops the family's active state, with the
    /// last tick the range is known to have been measured through and the latest tick the game had reached when the
    /// interruption was handled. A family keeps here what its open state already measured; nothing may span the gap.
    /// </summary>
    public Action<FamilyState, int, int>? Interrupted { get; init; }

    /// <summary>
    /// Called when the family starts a new recording interval, before any event: a new game, a family not recorded before, or
    /// totals of an older revision that start over. A family marks here the rules the interval is recorded under when a rule
    /// changed without a new revision, so totals continued from before the change can be told apart (#392).
    /// </summary>
    public Action<FamilyState>? Started { get; init; }

    public FamilySpec Spec => new(Name, Revision);
}

/// <summary>Builds the metric values of one family at one checkpoint.</summary>
public sealed class FamilyOutputContext
{
    public FamilyOutputContext(FamilyState state, int saveTick, IDictionary<string, MetricEntry> into)
    {
        State = state;
        SaveTick = saveTick;
        Into = into;
    }

    public FamilyState State { get; }
    public int SaveTick { get; }
    public IDictionary<string, MetricEntry> Into { get; }

    /// <summary>Recorded from the start of the game, without a gap.</summary>
    public bool FromStart => State.SinceTick == 0 && State.Continuous;

    /// <summary>Complete when recorded from the start without a gap up to this save; otherwise partial.</summary>
    public MetricCoverage Coverage => FromStart && State.ThroughTick == SaveTick ? MetricCoverage.Complete : MetricCoverage.Partial;

    /// <summary>
    /// A value carrying this family's measured range and coverage (weakened to partial when <paramref name="partial"/>).
    /// A family weakens a value only here or with <see cref="MarkPartial"/>, which mark it <see cref="MetricValue.Weakened"/>:
    /// a partial value without that mark counts exactly what was recorded in its range (#307).
    /// </summary>
    public MetricValue Value(long? integer = null, double? real = null, double? numerator = null, double? denominator = null,
        int? samples = null, bool partial = false, MetricCoverage? coverage = null) => new()
    {
        Coverage = coverage ?? (partial ? MetricCoverage.Partial : Coverage),
        Weakened = partial || coverage == MetricCoverage.Partial ? true : null,
        IntegerValue = integer,
        RealValue = real is { } r && double.IsFinite(r) ? r : null,
        Numerator = numerator is { } num && double.IsFinite(num) ? num : null,
        Denominator = denominator is { } den && double.IsFinite(den) ? den : null,
        SampleCount = samples,
        SinceTick = State.SinceTick,
        ThroughTick = State.ThroughTick,
    };

    public void Put(string key, MetricValue scalar) => Into[key] = MetricEntry.Of(MetricKeys.Revision(key), scalar);

    public void PutBreakdown(string key, IReadOnlyDictionary<string, MetricValue> values, MetricValue? scalar = null)
    {
        var entry = new MetricEntry { Revision = MetricKeys.Revision(key) };
        foreach (var (k, v) in values)
            entry.Values[k] = v;
        if (scalar is not null)
            entry.Values[MetricEntry.Scalar] = scalar;
        Into[key] = entry;
    }

    /// <summary>
    /// Weakens the values of <paramref name="keys"/> to partial when a set they depend on reached its cap: a value is then a lower
    /// bound, and "no sample" no longer proves there was nothing.
    /// </summary>
    public void MarkPartial(params string[] keys)
    {
        foreach (var key in keys)
            if (Into.TryGetValue(key, out var entry))
                foreach (var v in entry.Values.Values)
                {
                    // A value already partial because its recording started late is weakened too.
                    v.Weakened = true;
                    if (v.Coverage is MetricCoverage.Complete or MetricCoverage.SnapshotOnly or MetricCoverage.NotApplicable)
                        v.Coverage = MetricCoverage.Partial;
                }
    }

    // ----- shapes shared by the families -----

    /// <summary>The count <c>N</c> of an accumulator (0 when nothing happened in the range).</summary>
    public void PutCount(string acc, string key) => Put(key, Value(integer: State.Peek(acc)?.N ?? 0));

    /// <summary>A total and its per-key breakdown; the breakdown's scalar is the same total.</summary>
    public void CountAndBreakdown(string acc, string countKey, string? breakdownKey)
    {
        var a = State.Peek(acc);
        Put(countKey, Value(integer: a?.N ?? 0));
        if (breakdownKey != null)
            PutBreakdown(breakdownKey, Counts(a), Value(integer: a?.N ?? 0, partial: a?.Overflow == true));
    }

    /// <summary>Integer amounts per key (partial when keys were dropped at the cap).</summary>
    public Dictionary<string, MetricValue> Counts(Accumulator? a)
        => (a?.By ?? new Dictionary<string, double>()).ToDictionary(
            kv => kv.Key,
            kv => Value(integer: UtcTime.ClampToInt64(kv.Value), partial: a!.Overflow),
            StringComparer.Ordinal);

    /// <summary>A real sum (0 when nothing happened in the range).</summary>
    public void PutSum(string acc, string key, double scale = 1) => Put(key, Value(real: (State.Peek(acc)?.Sum ?? 0) * scale));

    /// <summary>
    /// The extreme of the samples of an accumulator, scaled (e.g. ticks to days). No sample: not applicable (a duration with
    /// no completed interval, a peak with nothing measured), as the ledger's "期間のない平均・分母0・証拠不足は —".
    /// </summary>
    public void PutExtreme(string acc, string key, bool max, double scale = 1)
    {
        var a = State.Peek(acc);
        var n = a?.N ?? 0;
        var scaled = (max ? a?.Max : a?.Min) is { } v ? v * scale : double.NaN;
        Put(key, n <= 0 || !double.IsFinite(scaled)
            ? Value(coverage: MetricCoverage.NotApplicable, samples: 0)
            : Value(real: scaled, samples: UtcTime.ClampToInt32(n)));
    }

    /// <summary>The mean of the samples (sum ÷ count) with its numerator and denominator; no sample: not applicable.</summary>
    public void PutMean(string acc, string key, double scale = 1)
    {
        var a = State.Peek(acc);
        var n = a?.N ?? 0;
        if (n <= 0)
        {
            Put(key, Value(coverage: MetricCoverage.NotApplicable, numerator: 0, denominator: 0, samples: 0));
            return;
        }
        var clampedSamples = UtcTime.ClampToInt32(n);
        if (a?.Sum is not { } rawSum || !double.IsFinite(rawSum) || !double.IsFinite(scale))
        {
            Put(key, Value(coverage: MetricCoverage.Unknown, samples: clampedSamples));
            return;
        }
        var scaled = rawSum * scale;
        var mean = scaled / n;
        Put(key, double.IsFinite(scaled) && double.IsFinite(mean)
            ? Value(real: mean, numerator: scaled, denominator: n, samples: clampedSamples)
            : Value(coverage: MetricCoverage.Unknown, samples: clampedSamples));
    }

    /// <summary>
    /// The largest per-key amount and the key(s) holding it (ties: all of them), e.g. the doctor with most tends. The scalar
    /// is the amount; the breakdown names the holders. Partial when keys were dropped at the cap.
    /// </summary>
    public void PutTop(string acc, string key)
    {
        var a = State.Peek(acc);
        if (a?.By is not { Count: > 0 } by)
        {
            Put(key, Value(coverage: MetricCoverage.NotApplicable));
            return;
        }
        var hasBest = false;
        double best = 0;
        foreach (var v in by.Values)
        {
            if (double.IsFinite(v) && (!hasBest || v > best))
            {
                best = v;
                hasBest = true;
            }
        }
        if (!hasBest)
        {
            Put(key, Value(coverage: MetricCoverage.NotApplicable));
            return;
        }
        var rounded = UtcTime.ClampToInt64(best);
        var holders = by.Where(kv => kv.Value == best).Select(kv => kv.Key).Order(StringComparer.Ordinal)
            .ToDictionary(k => k, _ => Value(integer: rounded, partial: a.Overflow), StringComparer.Ordinal);
        PutBreakdown(key, holders, Value(integer: rounded, partial: a.Overflow));
    }

    /// <summary>The number of distinct keys (partial when keys were dropped at the cap).</summary>
    public void PutDistinct(string acc, string key)
    {
        var a = State.Peek(acc);
        Put(key, Value(integer: a?.By?.Count ?? 0, partial: a?.Overflow == true));
    }

    /// <summary>
    /// The largest amount in one whole period (a game day, a game year) of a breakdown keyed by period index. Only periods the
    /// range covers entirely count; a range with a gap is partial. No whole period: not applicable.
    /// </summary>
    public void PutCompletePeriodPeak(string acc, string key, int periodTicks)
    {
        if (periodTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(periodTicks));
        var a = State.Peek(acc);
        // Count the zero-output periods too. An absent bucket means zero activity,
        // not that a fully observed game day/year was never measured.
        var firstWhole = ((long)State.SinceTick + periodTicks - 1) / periodTicks;
        var completeBefore = State.ThroughTick / (long)periodTicks;
        if (firstWhole >= completeBefore)
        {
            Put(key, Value(coverage: MetricCoverage.NotApplicable));
            return;
        }

        double best = 0;
        foreach (var (k, v) in a?.By ?? new Dictionary<string, double>())
        {
            if (!double.IsFinite(v))
                continue;
            if (!long.TryParse(k, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var p))
                continue;
            if (p < firstWhole || p >= completeBefore)
                continue;
            best = Math.Max(best, v);
        }
        Put(key, Value(integer: UtcTime.ClampToInt64(best), partial: !State.Continuous || a?.Overflow == true));
    }

    /// <summary>The first event's day, known only for a range recorded from the start (null: not reached yet).</summary>
    public void PutFirstDay(string acc, string key)
    {
        var first = State.Peek(acc)?.First;
        Put(key, !FromStart ? Value(coverage: MetricCoverage.Unknown) : Value(real: first is { } f ? f / (double)StatFormat.TicksPerDay : null));
    }
}

/// <summary>The families shipped with this version (each one behind its own acquisition gate in the game).</summary>
public static class LiveFamilies
{
    public static IReadOnlyList<LiveFamily> All { get; } = new[]
    {
        ResearchFamily.Family,
        MentalFamily.Family,
        HealthDeathFamily.Family,
        DownFamily.Family,
        MembershipFamily.Family,
        RaidFamily.Family,
        LifetimeFamily.Family,
        SocialFamily.Family,
        AnimalsFamily.Family,
        WorkFamily.Family,
        WorkDetailsFamily.Family,
        ChoreFamily.Family,
        FoodFamily.Family,
        TradeFamily.Family,
        DiplomacyFamily.Family,
        MedicineFamily.Family,
        DisasterFamily.Family,
        ModdedIncidentFamily.Family,
        VpeStatistics.Family,
        VpeStatistics.CastFamily,
        OutpostProduction.Family,
        DbhStatistics.Family,
        RoyaltyFamily.Family,
        RoyaltyLinkingFamily.Family,
        IdeologyFamily.Family,
        IdeologyExpansionFamily.Family,
        OdysseyFamily.Family,
        OdysseyVoyageFamily.Family,
        FishingFamily.Family,
        BiotechFamily.Family,
        BiotechGeneticsFamily.Family,
        AnomalyFamily.Family,
        CaravanFamily.Family,
    };

    private static IReadOnlyList<LiveFamily> extra = Array.Empty<LiveFamily>();

    /// <summary>The installed built-in and optional families, frozen after the startup adapter scan.</summary>
    public static IReadOnlyList<LiveFamily> Active
    {
        get
        {
            var optional = Volatile.Read(ref extra);
            return optional.Count == 0 ? All : All.Concat(optional).ToArray();
        }
    }

    public static void SetOptionalFamilies(IReadOnlyList<LiveFamily> families)
    {
        var names = All.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        if (families.Any(f => !names.Add(f.Name)))
            throw new ArgumentException("A provider tried to register an existing family", nameof(families));
        Volatile.Write(ref extra, families.OrderBy(f => f.Name, StringComparer.Ordinal).ToArray());
    }

    public static LiveFamily? Find(string name) => Active.FirstOrDefault(f => f.Name == name);
}

/// <summary>Turns a checkpoint into the profile's numeric observation (shared by the save hook, the load and the import).</summary>
public static class CheckpointMetrics
{
    /// <summary>
    /// The observation of <paramref name="payload"/>, read from (or written as) <paramref name="json"/>. Each family is built on its
    /// own: an unknown, unreadable or other-revision family adds nothing, and a family that fails adds nothing either.
    /// </summary>
    public static MetricObservation ToObservation(CheckpointPayload payload, string json, ColonyIdentity identity, string? gameVersion,
        List<string>? activeDlcIds, IReadOnlyList<LiveFamily>? families = null)
    {
        var obs = new MetricObservation
        {
            Source = MetricObservationSource.Checkpoint,
            FormatVersion = payload.FormatVersion,
            Key = payload.CheckpointId,
            ParentKey = payload.ParentCheckpointId,
            PayloadHash = CheckpointJson.Hash(json),
            Identity = identity.Clone(),
            GameVersion = gameVersion,
            SaveTick = payload.SaveTick,
            ObservedUtc = payload.SavedUtc,
            ActiveDlcIds = activeDlcIds?.ToList(),
            ContinuousFamilies = new List<string>(),
        };
        families ??= LiveFamilies.Active;
        foreach (var (name, state) in payload.Families)
        {
            if (state.IsQuarantined || families.FirstOrDefault(f => f.Name == name) is not { } family || family.Revision != state.Revision)
                continue;
            var built = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);
            try
            {
                family.Build(new FamilyOutputContext(state, payload.SaveTick, built));
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // One family's broken totals or builder never take the others with them, whatever it throws (an optional
                // MOD's builder too); its totals stay in the checkpoint unread.
                continue;
            }
            if (state.Evidence is { } evidence)
            {
                obs.EvidenceByAcquisition ??= new Dictionary<string, MetricEvidence>(StringComparer.Ordinal);
                obs.EvidenceByAcquisition[name] = evidence;
            }
            if (state.ProviderId is { Length: > 0 } providerId && state.ProviderCapture is { } capture)
            {
                obs.ProviderCaptures ??= new Dictionary<string, ModProviderCapture>(StringComparer.Ordinal);
                obs.ProviderCaptures[providerId] = capture;
            }
            foreach (var (key, entry) in built)
                obs.Metrics[key] = entry;
            // Recorded without a gap from its own start through this save (a family kept from an earlier session stops
            // before it): its values that are not weakened count exactly what happened in that range (#307).
            if (state.Continuous && state.ThroughTick == payload.SaveTick)
                obs.ContinuousFamilies.Add(name);
        }
        return obs;
    }
}
