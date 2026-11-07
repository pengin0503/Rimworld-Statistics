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
}

/// <summary>The families shipped with this version (each one behind its own acquisition gate in the game).</summary>
public static class LiveFamilies
{
    public static IReadOnlyList<LiveFamily> All { get; } = Array.Empty<LiveFamily>();

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
