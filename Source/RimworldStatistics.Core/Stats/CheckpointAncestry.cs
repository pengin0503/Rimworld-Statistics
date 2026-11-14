namespace RimworldStatistics.Core;

/// <summary>
/// Save ancestry proven by embedded checkpoint ids, for one colony's history chart (Issue #72, audit C1).
/// <para>
/// A save that holds checkpoint C at C's own tick is the state that C recorded. A save that holds C but was made
/// later descends from C: the MOD writes the last committed checkpoint unchanged whenever a save records nothing new
/// (every family off, an unreadable checkpoint kept verbatim, a nested save). Such a save is not C itself, so nothing
/// can descend from it through C. Checkpoints chain only through an exact, unique, unconflicted parent id whose tick is
/// not later than its child's, within one campaign. A missing parent, a conflict, a cycle or a duplicate id ends a chain:
/// whatever lies beyond it is not proven. The same campaign id, a matching tick or a file name prove nothing.
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
    private readonly Dictionary<string, string?> _parents = new(Ord);
    private readonly ILookup<(string?, int), MetricObservation> _byTick;

    public CheckpointAncestry(IEnumerable<MetricObservation> observations)
    {
        var duplicates = new HashSet<string>(Ord);
        foreach (var o in observations)
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
    /// A game-side snapshot: the checkpoint the same save committed, or, for records made before that link was stored,
    /// the one imported from the save file with exactly the snapshot's tick, campaign and file write time.
    /// </summary>
    public Anchor? OfSnapshot(MetricObservation snapshot)
    {
        if (snapshot.CheckpointKey is { } key)
            return _nodes.TryGetValue(key, out var linked) && linked.Identity.ModCampaignId == snapshot.Identity.ModCampaignId
                ? At(key, snapshot.SaveTick) is { Exact: true } exact ? exact : null
                : null;
        if (snapshot.Identity.ModCampaignId is not { Length: > 0 } campaign)
            return null;
        string? match = null;
        foreach (var node in _byTick[(campaign, snapshot.SaveTick)])
        {
            if (!node.FileAliases.Any(a => a.LastWriteUtc == snapshot.ObservedUtc))
                continue;
            if (match is not null)
                return null;
            match = node.Key;
        }
        return match is null ? null : new Anchor(match, Exact: true);
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
    /// Only exact anchors can be ancestors. A later reading can never be the ancestor of an earlier one, so a key's
    /// answer is cached once found: readings added afterwards are later than every checkpoint on that key's chain.
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
            if (anchor.Exact)
                exactAt.TryAdd(anchor.Key, i);
        }
        return result;
    }
}
