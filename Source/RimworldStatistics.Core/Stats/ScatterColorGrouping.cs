namespace RimworldStatistics.Core;

/// <summary>
/// Attribute dot-coloring modes for <see cref="MetricScatterPlot"/>.
/// Strictly categorical visual encoding only — no regression line, trend line, or correlation coefficient is ever computed.
/// </summary>
public enum ScatterColorMode
{
    DataStatus,
    EndingReached,
    Permadeath,
    Difficulty,
    Storyteller,
}

public enum ScatterColorBucketKind
{
    Normal,
    EndingReached,
    EndingNotReached,
    Permadeath,
    ReloadAnytime,
    CategoryValue,
    Other,
    Unknown,
}

public sealed record ScatterColorBucket(
    int Index,
    ScatterColorBucketKind Kind,
    string? RawValue,
    int PointCount);

public sealed record ScatterColorAssignment(
    ScatterColorMode Mode,
    IReadOnlyList<ScatterColorBucket> Buckets,
    IReadOnlyList<int> PointBucketIndices);

/// <summary>
/// Assigns each plotted <see cref="ScatterPoint"/> to a deterministic categorical color bucket based on existing
/// <see cref="ColonyStats"/> metadata. Never computes regression lines or correlation statistics.
/// </summary>
public static class ScatterColorGrouping
{
    public const int DefaultMaxDistinctCategories = 6;

    public static ScatterColorAssignment Assign(
        MetricScatterPlot plot,
        StatsResult stats,
        ScatterColorMode mode,
        int maxDistinctCategories = DefaultMaxDistinctCategories)
    {
        ArgumentNullException.ThrowIfNull(plot);
        ArgumentNullException.ThrowIfNull(stats);
        maxDistinctCategories = Math.Max(1, maxDistinctCategories);

        var coloniesByKey = new Dictionary<string, ColonyStats>(StringComparer.Ordinal);
        foreach (var colony in stats.Colonies)
            coloniesByKey.TryAdd(colony.Key, colony);

        if (mode == ScatterColorMode.DataStatus || plot.Points.Count == 0)
        {
            var single = plot.Points.Count == 0
                ? Array.Empty<ScatterColorBucket>()
                : [new ScatterColorBucket(0, ScatterColorBucketKind.Normal, null, plot.Points.Count)];
            var zeros = plot.Points.Count == 0 ? Array.Empty<int>() : new int[plot.Points.Count];
            return new ScatterColorAssignment(mode, single, zeros);
        }

        if (mode == ScatterColorMode.EndingReached)
            return AssignFixed(
                plot,
                coloniesByKey,
                mode,
                [ScatterColorBucketKind.EndingReached, ScatterColorBucketKind.EndingNotReached],
                colony => colony is not null && colony.EndingCounts.Values.Sum() > 0
                    ? ScatterColorBucketKind.EndingReached
                    : ScatterColorBucketKind.EndingNotReached);

        if (mode == ScatterColorMode.Permadeath)
            return AssignFixed(
                plot,
                coloniesByKey,
                mode,
                [ScatterColorBucketKind.Permadeath, ScatterColorBucketKind.ReloadAnytime, ScatterColorBucketKind.Unknown],
                colony => colony is { HasObservedPoint: true, Permadeath: { } perm }
                    ? (perm ? ScatterColorBucketKind.Permadeath : ScatterColorBucketKind.ReloadAnytime)
                    : ScatterColorBucketKind.Unknown);

        return AssignNamedCategories(
            plot,
            coloniesByKey,
            mode,
            maxDistinctCategories,
            colony => mode == ScatterColorMode.Difficulty ? colony?.Difficulty : colony?.Storyteller);
    }

    /// <summary>
    /// Resolves the bucket index for a <see cref="ScatterCluster"/>: returns the shared bucket index when all points in the
    /// cluster belong to the same bucket, or <c>-1</c> when the cluster mixes multiple attribute categories.
    /// </summary>
    public static int ResolveClusterBucket(ScatterCluster cluster, ScatterColorAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(cluster);
        ArgumentNullException.ThrowIfNull(assignment);
        if (cluster.PointIndices.Count == 0 || assignment.PointBucketIndices.Count == 0)
            return 0;
        var first = assignment.PointBucketIndices[cluster.PointIndices[0]];
        for (var i = 1; i < cluster.PointIndices.Count; i++)
        {
            if (assignment.PointBucketIndices[cluster.PointIndices[i]] != first)
                return -1;
        }
        return first;
    }

    private static ScatterColorAssignment AssignFixed(
        MetricScatterPlot plot,
        Dictionary<string, ColonyStats> coloniesByKey,
        ScatterColorMode mode,
        ScatterColorBucketKind[] candidateKinds,
        Func<ColonyStats?, ScatterColorBucketKind> classify)
    {
        var rawKinds = new ScatterColorBucketKind[plot.Points.Count];
        var counts = new Dictionary<ScatterColorBucketKind, int>();
        for (var i = 0; i < plot.Points.Count; i++)
        {
            coloniesByKey.TryGetValue(plot.Points[i].ColonyKey, out var colony);
            var kind = classify(colony);
            rawKinds[i] = kind;
            counts[kind] = counts.GetValueOrDefault(kind) + 1;
        }

        var buckets = new List<ScatterColorBucket>();
        var bucketMap = new Dictionary<ScatterColorBucketKind, int>();
        foreach (var kind in candidateKinds)
        {
            if (!counts.TryGetValue(kind, out var count) || count <= 0)
                continue;
            var idx = buckets.Count;
            bucketMap[kind] = idx;
            buckets.Add(new ScatterColorBucket(idx, kind, null, count));
        }

        var indices = new int[plot.Points.Count];
        for (var i = 0; i < rawKinds.Length; i++)
            indices[i] = bucketMap[rawKinds[i]];

        return new ScatterColorAssignment(mode, buckets, indices);
    }

    private static ScatterColorAssignment AssignNamedCategories(
        MetricScatterPlot plot,
        Dictionary<string, ColonyStats> coloniesByKey,
        ScatterColorMode mode,
        int maxDistinctCategories,
        Func<ColonyStats?, string?> selector)
    {
        var rawValues = new string?[plot.Points.Count];
        var valueCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var unknownCount = 0;

        for (var i = 0; i < plot.Points.Count; i++)
        {
            coloniesByKey.TryGetValue(plot.Points[i].ColonyKey, out var colony);
            var val = selector(colony);
            if (string.IsNullOrWhiteSpace(val))
            {
                rawValues[i] = null;
                unknownCount++;
            }
            else
            {
                rawValues[i] = val;
                valueCounts[val] = valueCounts.GetValueOrDefault(val) + 1;
            }
        }

        var orderedValues = valueCounts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .ToArray();

        var keepNamedCount = orderedValues.Length <= maxDistinctCategories
            ? orderedValues.Length
            : Math.Max(1, maxDistinctCategories - 1);

        var buckets = new List<ScatterColorBucket>();
        var namedBucketMap = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < keepNamedCount; i++)
        {
            var (name, count) = orderedValues[i];
            var idx = buckets.Count;
            namedBucketMap[name] = idx;
            buckets.Add(new ScatterColorBucket(idx, ScatterColorBucketKind.CategoryValue, name, count));
        }

        var otherBucketIndex = -1;
        if (orderedValues.Length > keepNamedCount)
        {
            var otherCount = 0;
            for (var i = keepNamedCount; i < orderedValues.Length; i++)
                otherCount += orderedValues[i].Value;
            otherBucketIndex = buckets.Count;
            buckets.Add(new ScatterColorBucket(otherBucketIndex, ScatterColorBucketKind.Other, null, otherCount));
        }

        var unknownBucketIndex = -1;
        if (unknownCount > 0)
        {
            unknownBucketIndex = buckets.Count;
            buckets.Add(new ScatterColorBucket(unknownBucketIndex, ScatterColorBucketKind.Unknown, null, unknownCount));
        }

        var indices = new int[plot.Points.Count];
        for (var i = 0; i < rawValues.Length; i++)
        {
            var val = rawValues[i];
            if (val is null)
                indices[i] = unknownBucketIndex;
            else if (namedBucketMap.TryGetValue(val, out var namedIdx))
                indices[i] = namedIdx;
            else
                indices[i] = otherBucketIndex;
        }

        return new ScatterColorAssignment(mode, buckets, indices);
    }
}
