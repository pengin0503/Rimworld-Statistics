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

    /// <summary>A value carrying this family's measured range and coverage (weakened to partial when <paramref name="partial"/>).</summary>
    public MetricValue Value(long? integer = null, double? real = null, double? numerator = null, double? denominator = null,
        int? samples = null, bool partial = false, MetricCoverage? coverage = null) => new()
    {
        Coverage = coverage ?? (partial ? MetricCoverage.Partial : Coverage),
        IntegerValue = integer,
        RealValue = real is { } r && double.IsFinite(r) ? r : null,
        Numerator = numerator,
        Denominator = denominator,
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
                    if (v.Coverage is MetricCoverage.Complete or MetricCoverage.SnapshotOnly or MetricCoverage.NotApplicable)
                        v.Coverage = MetricCoverage.Partial;
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
        => (a?.By ?? new Dictionary<string, double>()).ToDictionary(kv => kv.Key, kv => Value(integer: (long)Math.Round(kv.Value), partial: a!.Overflow),
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
        var n = (int)(a?.N ?? 0);
        Put(key, n == 0 || (max ? a!.Max : a!.Min) is not { } v
            ? Value(coverage: MetricCoverage.NotApplicable, samples: 0)
            : Value(real: v * scale, samples: n));
    }

    /// <summary>The mean of the samples (sum ÷ count) with its numerator and denominator; no sample: not applicable.</summary>
    public void PutMean(string acc, string key, double scale = 1)
    {
        var a = State.Peek(acc);
        var n = a?.N ?? 0;
        Put(key, n == 0
            ? Value(coverage: MetricCoverage.NotApplicable, numerator: 0, denominator: 0, samples: 0)
            : Value(real: a!.Sum!.Value * scale / n, numerator: a.Sum.Value * scale, denominator: n, samples: (int)n));
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
        var best = by.Values.Max();
        var holders = by.Where(kv => kv.Value == best).Select(kv => kv.Key).Order(StringComparer.Ordinal)
            .ToDictionary(k => k, _ => Value(integer: (long)Math.Round(best), partial: a.Overflow), StringComparer.Ordinal);
        PutBreakdown(key, holders, Value(integer: (long)Math.Round(best), partial: a.Overflow));
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
            if (!long.TryParse(k, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var p))
                continue;
            if (p < firstWhole || p >= completeBefore)
                continue;
            best = Math.Max(best, v);
        }
        Put(key, Value(integer: (long)Math.Round(best), partial: !State.Continuous || a?.Overflow == true));
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
        FoodFamily.Family,
        TradeFamily.Family,
        DiplomacyFamily.Family,
        MedicineFamily.Family,
        DisasterFamily.Family,
        RoyaltyFamily.Family,
        IdeologyFamily.Family,
        OdysseyFamily.Family,
        BiotechFamily.Family,
        CaravanFamily.Family,
    };

    public static LiveFamily? Find(string name) => All.FirstOrDefault(f => f.Name == name);
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
        };
        families ??= LiveFamilies.All;
        foreach (var (name, state) in payload.Families)
        {
            if (state.IsQuarantined || families.FirstOrDefault(f => f.Name == name) is not { } family || family.Revision != state.Revision)
                continue;
            var built = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);
            try
            {
                family.Build(new FamilyOutputContext(state, payload.SaveTick, built));
            }
            catch (Exception e) when (e is ArithmeticException or InvalidOperationException or ArgumentException or KeyNotFoundException or NullReferenceException)
            {
                continue;   // one family's broken totals never take the others with them
            }
            foreach (var (key, entry) in built)
                obs.Metrics[key] = entry;
        }
        return obs;
    }
}
