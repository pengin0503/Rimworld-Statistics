namespace RimworldStatistics.Core;

/// <summary>
/// Save ancestry proven by embedded checkpoint ids, for one colony's history chart (Issue #72, audit C1).
/// <para>
/// A save that holds checkpoint C at C's own tick is the state that C recorded. A save that holds C but was made
/// later descends from C: it wrote the last committed checkpoint unchanged and recorded nothing new. This version does
/// that only for an unreadable checkpoint kept verbatim, a nested save, or while its save hook is off; earlier versions
/// also did it whenever every family was off. Such a save is not C itself, so nothing can descend from it through C.
/// Checkpoints chain only through an exact, unique, unconflicted parent id whose tick is not later than its child's,
/// within one campaign. A missing parent, a conflict, a cycle or a duplicate id ends a chain: whatever lies beyond it is
/// not proven. The same campaign id, a matching tick or a file name prove nothing.
/// </para>
/// </summary>
internal sealed class CheckpointAncestry
{
    private static readonly StringComparer Ord = StringComparer.Ordinal;

    /// <summary>A reading's place in the lineage: <c>Exact</c> when the save is that checkpoint's own state.</summary>
    internal readonly record struct Anchor(string Key, bool Exact);

    private readonly Dictionary<string, MetricObservation> _nodes = new(Ord);
    // A file version (name, size, write time) names one save: two checkpoints claiming it is a contradiction (null).
    private readonly Dictionary<(string, long, DateTime), string?> _byFile = new();
    // The game tick of each imported save file version that holds a checkpoint (null: two readings of it disagree).
    private readonly Dictionary<(string, long, DateTime), int?> _fileTicks = new();
    private readonly ILookup<DateTime, (string, long, DateTime)> _filesByWriteTime;
    private readonly Dictionary<string, string?> _parents = new(Ord);
    private readonly ILookup<(string?, int), MetricObservation> _byTick;

    public CheckpointAncestry(IEnumerable<MetricObservation> observations)
    {
        var all = observations as IReadOnlyCollection<MetricObservation> ?? observations.ToList();
        var duplicates = new HashSet<string>(Ord);
        foreach (var o in all)
        {
            if (o.IsQuarantined || o.Source != MetricObservationSource.Checkpoint || o.Key.Length == 0)
                continue;
            if (!_nodes.TryAdd(o.Key, o))
                duplicates.Add(o.Key);
        }
        foreach (var key in duplicates)
            _nodes.Remove(key);
        foreach (var key in _nodes.Where(n => n.Value.Conflicted).Select(n => n.Key).ToArray())
            _nodes.Remove(key);

        foreach (var node in _nodes.Values)
            foreach (var alias in node.FileAliases)
            {
                var file = (alias.FileName, alias.FileSize, alias.LastWriteUtc);
                _byFile[file] = _byFile.TryGetValue(file, out var other) && other != node.Key ? null : node.Key;
            }
        foreach (var o in all)
        {
            if (o.IsQuarantined || o.Source != MetricObservationSource.SaveFile || o.SaveTick < 0)
                continue;
            foreach (var alias in o.FileAliases)
            {
                var file = (alias.FileName, alias.FileSize, alias.LastWriteUtc);
                if (_byFile.ContainsKey(file))
                    _fileTicks[file] = _fileTicks.TryGetValue(file, out var tick) && tick != o.SaveTick ? null : o.SaveTick;
            }
        }
        _filesByWriteTime = _fileTicks.Keys.ToLookup(f => f.Item3);
        _byTick = _nodes.Values.ToLookup(n => (n.Identity.ModCampaignId, n.SaveTick));
    }

    public bool IsEmpty => _nodes.Count == 0;

    public MetricObservation? Node(string key) => _nodes.GetValueOrDefault(key);

    /// <summary>The proven parent checkpoint of <paramref name="key"/>, or null where the chain ends.</summary>
    public string? Parent(string key)
    {
        if (_parents.TryGetValue(key, out var cached))
            return cached;
        string? parent = null;
        if (_nodes.TryGetValue(key, out var node) && node.ParentKey is { Length: > 0 } id && id != key
            && _nodes.TryGetValue(id, out var candidate) && candidate.SaveTick >= 0 && candidate.SaveTick <= node.SaveTick
            && candidate.Identity.ModCampaignId is { Length: > 0 } campaign && campaign == node.Identity.ModCampaignId)
            parent = id;
        _parents[key] = parent;
        return parent;
    }

    /// <summary>
    /// Where a save read from these byte-identical file copies sits, given that save's own game tick. A copy whose
    /// checkpoint was not recorded says nothing; copies naming different checkpoints contradict each other.
    /// </summary>
    public Anchor? OfFile(IEnumerable<MetricFileAlias> aliases, int saveTick)
    {
        string? key = null;
        foreach (var alias in aliases)
        {
            if (!_byFile.TryGetValue((alias.FileName, alias.FileSize, alias.LastWriteUtc), out var found))
                continue;
            if (found is null || (key is not null && key != found))
                return null;
            key = found;
        }
        return key is null ? null : At(key, saveTick);
    }

    public Anchor? OfFile(string fileName, long fileSize, DateTime lastWriteUtc, int saveTick)
        => _byFile.TryGetValue((fileName, fileSize, lastWriteUtc), out var key) && key is not null ? At(key, saveTick) : null;

    /// <summary>
    /// A game-side snapshot: the checkpoint the same save wrote, committed by it (its own state) or carried unchanged from
    /// an earlier save (a descendant of that state), exactly as a read of the save file places it. For records made before
    /// that link was stored, the save file imported with exactly the snapshot's campaign, tick and file write time names
    /// it: a checkpoint of that tick from a file written then, or the checkpoint such a file was read to hold.
    /// </summary>
    public Anchor? OfSnapshot(MetricObservation snapshot)
    {
        if (snapshot.CheckpointKey is { } key)
            return _nodes.TryGetValue(key, out var linked) && linked.Identity.ModCampaignId == snapshot.Identity.ModCampaignId
                ? At(key, snapshot.SaveTick)
                : null;
        if (snapshot.Identity.ModCampaignId is not { Length: > 0 } campaign)
            return null;
        string? match = null;
        bool Accept(string? candidate)
        {
            if (candidate is null || (match is not null && match != candidate))
                return false;
            match = candidate;
            return true;
        }
        foreach (var node in _byTick[(campaign, snapshot.SaveTick)])
            if (node.FileAliases.Any(a => a.LastWriteUtc == snapshot.ObservedUtc) && !Accept(node.Key))
                return null;
        foreach (var file in _filesByWriteTime[snapshot.ObservedUtc])
        {
            var held = _byFile[file];
            if (held is not null && _nodes[held].Identity.ModCampaignId != campaign)
                continue;
            // A file version read with two ticks is two saves under one name, size and write time: either may be this one.
            if (_fileTicks[file] is not { } tick || (tick == snapshot.SaveTick && !Accept(held)))
                return null;
        }
        return match is null ? null : At(match, snapshot.SaveTick);
    }

    private Anchor? At(string key, int saveTick)
    {
        var tick = _nodes[key].SaveTick;
        return tick < 0 || tick > saveTick ? null : new Anchor(key, tick == saveTick);
    }

    /// <summary>
    /// Whether every anchor is one state: the same checkpoint at the same tick, or exact re-saves of one state made while
    /// paused (a parent chain within one tick). Returns the descendant-most of them; null when they may differ.
    /// </summary>
    public Anchor? Same(IReadOnlyCollection<Anchor?> anchors)
    {
        Anchor? result = null;
        foreach (var anchor in anchors)
        {
            if (anchor is not { } a)
                return null;
            if (result is not { } r || r == a)
            {
                result = a;
                continue;
            }
            if (!a.Exact || !r.Exact)
                return null;
            if (DescendsWithinTick(a.Key, r.Key))
                result = a;
            else if (!DescendsWithinTick(r.Key, a.Key))
                return null;
        }
        return result;
    }

    /// <summary>
    /// The proven ancestor checkpoints of a reading at <paramref name="anchor"/> whose tick is above
    /// <paramref name="afterTick"/>, nearest first: for a carried checkpoint the checkpoint itself, then its parents.
    /// </summary>
    public IEnumerable<string> AncestorsAfter(Anchor anchor, int afterTick)
    {
        var seen = new HashSet<string>(Ord);
        for (var key = anchor.Exact ? Parent(anchor.Key) : anchor.Key;
             key is not null && _nodes.TryGetValue(key, out var node) && node.SaveTick > afterTick && seen.Add(key);
             key = Parent(key))
            yield return key;
    }

    private bool DescendsWithinTick(string descendant, string ancestor)
    {
        var tick = _nodes[descendant].SaveTick;
        var seen = new HashSet<string>(Ord);
        for (var key = Parent(descendant); key is not null && seen.Add(key) && _nodes[key].SaveTick == tick; key = Parent(key))
            if (key == ancestor)
                return true;
        return false;
    }

    /// <summary>
    /// For each anchor in tick order, the index of the nearest earlier reading that is a proven ancestor state, or -1.
    /// Only exact anchors can be ancestors. An exact anchor is also the state of its parents at its own tick: re-saves made
    /// while paused are one state (<see cref="Same"/>), so a save played on from any of them continues that reading.
    /// A later reading can never be the ancestor of an earlier one, so a key's answer is cached once found: readings
    /// added afterwards are later than every checkpoint on that key's chain.
    /// </summary>
    public int[] NearestAncestors(IReadOnlyList<Anchor?> anchors)
    {
        var result = new int[anchors.Count];
        var exactAt = new Dictionary<string, int>(Ord);
        var nearest = new Dictionary<string, int>(Ord);
        var path = new List<string>();
        var seen = new HashSet<string>(Ord);
        for (var i = 0; i < anchors.Count; i++)
        {
            result[i] = -1;
            if (anchors[i] is not { } anchor)
                continue;
            path.Clear();
            seen.Clear();
            var found = -1;
            for (var key = anchor.Exact ? Parent(anchor.Key) : anchor.Key; key is not null && seen.Add(key); key = Parent(key))
            {
                if (exactAt.TryGetValue(key, out found) || nearest.TryGetValue(key, out found))
                    break;
                found = -1;
                path.Add(key);
            }
            if (found >= 0)
                foreach (var key in path)
                    nearest[key] = found;
            result[i] = found;
            if (!anchor.Exact)
                continue;
            var tick = _nodes[anchor.Key].SaveTick;
            seen.Clear();
            for (var key = anchor.Key; key is not null && seen.Add(key) && _nodes[key].SaveTick == tick; key = Parent(key))
                exactAt.TryAdd(key, i);
        }
        return result;
    }
}
