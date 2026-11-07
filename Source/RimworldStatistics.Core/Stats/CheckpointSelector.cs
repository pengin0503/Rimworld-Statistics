namespace RimworldStatistics.Core;

/// <summary>What the checkpoints of one colony say about its save lineage (numeric expansion design §3 step 4).</summary>
public enum CheckpointLineage
{
    /// <summary>No checkpoint.</summary>
    None,

    /// <summary>One chain from one root: every parent is known.</summary>
    Linear,

    /// <summary>Two checkpoints share a known parent: saves continued from the same point (not proof of a rewind).</summary>
    Branched,

    /// <summary>A missing parent, several roots, a cycle or a checkpoint id with two different contents.</summary>
    Unknown,
}

public sealed record CheckpointSelection(MetricObservation? Representative, CheckpointLineage Lineage, int Checkpoints);

/// <summary>
/// Chooses the one checkpoint whose cumulative totals a colony shows: the latest save tick, then the latest save time, then the
/// smallest id. A conflicted checkpoint is never chosen, a compressed one (an ancestor whose values were dropped) only counts
/// for the lineage. Totals of different branches are never added: each checkpoint already holds its whole lineage.
/// </summary>
public static class CheckpointSelector
{
    private static readonly StringComparer Ord = StringComparer.Ordinal;

    public static CheckpointSelection Select(IEnumerable<MetricObservation> observations)
    {
        var nodes = observations.Where(o => !o.IsQuarantined && o.Source == MetricObservationSource.Checkpoint).ToList();
        if (nodes.Count == 0)
            return new CheckpointSelection(null, CheckpointLineage.None, 0);
        var rep = nodes.Where(o => !o.Conflicted && !o.Compressed)
            .OrderByDescending(o => o.SaveTick).ThenByDescending(o => o.ObservedUtc).ThenBy(o => o.Key, Ord).FirstOrDefault();
        return new CheckpointSelection(rep, Lineage(nodes), nodes.Count);
    }

    private static CheckpointLineage Lineage(List<MetricObservation> nodes)
    {
        if (nodes.Any(n => n.Conflicted))
            return CheckpointLineage.Unknown;
        var byKey = new Dictionary<string, MetricObservation>(Ord);
        foreach (var n in nodes)
            if (!byKey.TryAdd(n.Key, n))
                return CheckpointLineage.Unknown;   // one id twice in one colony's list
        var roots = 0;
        var children = new Dictionary<string, int>(Ord);
        foreach (var n in nodes)
        {
            if (n.ParentKey is null)
            {
                roots++;
                continue;
            }
            if (!byKey.ContainsKey(n.ParentKey))
                return CheckpointLineage.Unknown;   // the parent was never seen (deleted save, recorded without this mod)
            children[n.ParentKey] = children.GetValueOrDefault(n.ParentKey) + 1;
        }
        if (roots != 1)
            return CheckpointLineage.Unknown;       // several starts, or only a cycle
        // Every node must reach the root: walk up once per node, marking nodes known to reach it (linear in the node count).
        var reaches = new HashSet<string>(Ord);
        var path = new List<string>();
        foreach (var n in nodes)
        {
            path.Clear();
            var onPath = new HashSet<string>(Ord);
            for (var cur = n; ; cur = byKey[cur.ParentKey!])
            {
                if (reaches.Contains(cur.Key))
                    break;
                if (!onPath.Add(cur.Key))
                    return CheckpointLineage.Unknown;   // a cycle
                path.Add(cur.Key);
                if (cur.ParentKey is null)
                    break;
            }
            reaches.UnionWith(path);
        }
        return children.Values.Any(c => c > 1) ? CheckpointLineage.Branched : CheckpointLineage.Linear;
    }
}
