namespace RimworldStatistics.Core;

/// <summary>A family that records this session, and the definition revision of its stored totals.</summary>
public sealed record FamilySpec(string Name, int Revision);

/// <summary>What one save writes into <c>numericStatistics</c>.</summary>
public sealed class SaveDraft
{
    /// <summary>The exact text written into the save.</summary>
    public required string Json { get; init; }

    /// <summary>A new checkpoint of this session; false when an earlier checkpoint (or unknown data) is written back unchanged.</summary>
    public required bool IsNew { get; init; }

    /// <summary>The checkpoint, when <see cref="IsNew"/>: a detached copy that later events do not change.</summary>
    public CheckpointPayload? Payload { get; init; }
}

/// <summary>
/// The running totals of one game (numeric expansion design §3 "Cの確定プロトコル"): families recording this session, the
/// families recorded earlier but not this session (kept verbatim), and the parent for the next checkpoint. Events add to the
/// totals in memory; only a successful save persists them, so a failed save loses nothing and the next one carries it all.
/// Not thread-safe: the game thread records and saves; a load restores it before the game runs.
/// </summary>
public sealed class LiveMetricAccumulator
{
    private static readonly StringComparer Ord = StringComparer.Ordinal;

    // Families in the order they were first seen, so a payload is written in a stable order.
    private readonly Dictionary<string, FamilyState> _recording = new(Ord);
    private readonly Dictionary<string, FamilyState> _kept = new(Ord);
    private readonly List<string> _order = new();
    private readonly string? _opaque;   // unknown or unreadable data: written back unchanged, nothing recorded on top
    private string? _lastJson;          // the last committed (or loaded) checkpoint text
    private CheckpointPayload? _last;

    private LiveMetricAccumulator(string? opaque) => _opaque = opaque;

    /// <summary>A new game: every recording family starts at <paramref name="tick"/> (0 is the start of the game).</summary>
    public static LiveMetricAccumulator StartNew(IReadOnlyCollection<FamilySpec> recording, int tick)
    {
        var acc = new LiveMetricAccumulator(null);
        foreach (var spec in recording)
            acc.Begin(spec, tick);
        return acc;
    }

    /// <summary>
    /// A loaded game. <paramref name="raw"/> is the save's <c>numericStatistics</c> (null when the save has none). A known
    /// checkpoint becomes the parent; its families continue when their revision matches and they ran up to the save, start over
    /// when their revision changed, and are kept verbatim when they do not record this session. Unknown data is written back
    /// unchanged and nothing new is recorded on top of it.
    /// </summary>
    public static LiveMetricAccumulator Resume(string? raw, IReadOnlyCollection<FamilySpec> recording, int tick)
    {
        if (raw is null)
            return StartNew(recording, tick);
        var parsed = CheckpointJson.Parse(raw);
        if (parsed.Status != CheckpointParseStatus.Ok)
            return new LiveMetricAccumulator(raw) { _lastJson = raw };

        var payload = parsed.Payload!;
        var acc = new LiveMetricAccumulator(null) { _lastJson = raw, _last = payload };
        var specs = recording.ToDictionary(r => r.Name, Ord);
        foreach (var (name, loaded) in payload.Families)
        {
            var family = loaded.Clone();   // the loaded checkpoint itself stays as it was read
            if (specs.TryGetValue(name, out var spec) && !family.IsQuarantined && family.Revision == spec.Revision)
            {
                // Continuous only when the family recorded up to the very save this game was loaded from.
                if (family.ThroughTick != payload.SaveTick || payload.SaveTick != tick)
                    family.Continuous = false;
                acc._recording[name] = family;
                acc._order.Add(name);
            }
            else if (specs.TryGetValue(name, out spec) && !family.IsQuarantined && family.Revision < spec.Revision)
            {
                acc.Begin(spec, tick);   // an older definition: never continued, never mixed
            }
            else
            {
                acc._kept[name] = family;   // not recording now, unreadable, or of a newer definition
                acc._order.Add(name);
            }
        }
        foreach (var spec in recording)
            if (!acc._recording.ContainsKey(spec.Name) && !acc._kept.ContainsKey(spec.Name))
                acc.Begin(spec, tick);
        return acc;
    }

    private void Begin(FamilySpec spec, int tick)
    {
        _recording[spec.Name] = new FamilyState { Revision = spec.Revision, SinceTick = tick, ThroughTick = tick, Continuous = true };
        if (!_order.Contains(spec.Name))
            _order.Add(spec.Name);
    }

    public string? ParentId => _last?.CheckpointId;

    /// <summary>
    /// The checkpoint this game was loaded from, or the last one committed since, when it is a readable checkpoint. It is never
    /// changed by later events.
    /// </summary>
    public CheckpointPayload? Loaded => _last;

    /// <summary>The exact text of <see cref="Loaded"/> (or of the unknown data kept).</summary>
    public string? LoadedJson => _lastJson;

    /// <summary>Families recording now.</summary>
    public IReadOnlyCollection<string> Recording => _recording.Keys;

    /// <summary>The totals of a recording family; null when the family does not record this session.</summary>
    public FamilyState? Family(string name) => _recording.GetValueOrDefault(name);

    /// <summary>An event of a recording family was missed (e.g. it arrived off the game thread): its totals are no longer complete.</summary>
    public void MarkGap(string name)
    {
        if (_recording.TryGetValue(name, out var family))
            family.Continuous = false;
    }

    /// <summary>Stops a family (its hook failed): what it recorded is kept, measured through <paramref name="tick"/>.</summary>
    public void Stop(string name, int tick)
    {
        if (!_recording.Remove(name, out var family))
            return;
        family.ThroughTick = tick;
        _kept[name] = family;
    }

    /// <summary>
    /// What a save at <paramref name="tick"/> writes. With a recording family: a new checkpoint (<paramref name="newId"/>) whose
    /// parent is <see cref="ParentId"/>. Without one: the last committed or loaded text unchanged, or null when there is none.
    /// </summary>
    public SaveDraft? PrepareSave(int tick, DateTime utc, string newId)
    {
        if (_opaque is not null || _recording.Count == 0)
            return _lastJson is null ? null : new SaveDraft { Json = _lastJson, IsNew = false };

        var payload = new CheckpointPayload
        {
            CheckpointId = newId,
            ParentCheckpointId = ParentId,
            SaveTick = tick,
            SavedUtc = utc,
        };
        foreach (var name in _order)
        {
            if (_recording.TryGetValue(name, out var live))
            {
                var copy = live.Clone();
                copy.ThroughTick = tick;
                payload.Families[name] = copy;
            }
            else if (_kept.TryGetValue(name, out var kept))
            {
                payload.Families[name] = kept.Clone();
            }
        }
        return new SaveDraft { Json = CheckpointJson.Serialize(payload), IsNew = true, Payload = payload };
    }

    /// <summary>A save holding <paramref name="draft"/> succeeded: it becomes the parent of the next checkpoint.</summary>
    public void Commit(SaveDraft draft)
    {
        if (!draft.IsNew || draft.Payload is null)
            return;
        _last = draft.Payload;
        _lastJson = draft.Json;
    }
}
