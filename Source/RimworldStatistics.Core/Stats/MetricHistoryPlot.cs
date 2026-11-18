namespace RimworldStatistics.Core;

/// <summary>Drawing support for per-colony histories. It changes only what is drawn, never the stored readings.</summary>
public static class MetricHistoryPlot
{
    /// <summary>
    /// For a history much denser than <paramref name="width"/> pixels: within each run of readings that each continue the
    /// previous one, keeps the first, lowest, highest and last reading of every pixel-sized chunk. Gaps, unlinked readings,
    /// branch starts and every reading a non-adjacent one continues from are always kept, and branch references are
    /// renumbered, so no line is drawn between readings that were not joined before. Returns the same list when it is
    /// already sparse enough.
    /// </summary>
    public static IReadOnlyList<MetricHistoryPoint> Thin(IReadOnlyList<MetricHistoryPoint> points, int width)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (width < 1 || points.Count <= width * 2L)
            return points;

        var referenced = new bool[points.Count];
        for (var i = 0; i < points.Count; i++)
            if (points[i].ContinuesFrom is var from && from >= 0 && from < i)
                referenced[from] = true;

        // A plain reading only continues the one before it with the same coverage: it can be merged into that run.
        bool Plain(int i) => i > 0 && points[i].ConnectToPrevious && points[i].ContinuesFrom < 0 && !points[i].Unlinked
            && !referenced[i] && points[i].Value?.Number is not null && points[i - 1].Value?.Number is not null
            && points[i].Value!.Coverage == points[i - 1].Value!.Coverage;
        bool PlainGap(int i) => i > 0 && points[i].Value?.Number is null && points[i - 1].Value?.Number is null
            && !referenced[i] && !points[i].Unlinked && points[i].ContinuesFrom < 0;

        var keep = new bool[points.Count];
        var step = Math.Max(1, points.Count / width);
        for (var start = 0; start < points.Count;)
        {
            if (points[start].Value?.Number is null)
            {
                var gapEnd = start + 1;
                for (; gapEnd < Math.Min(start + step, points.Count) && PlainGap(gapEnd); gapEnd++)
                {
                }
                keep[start] = keep[gapEnd - 1] = true;
                start = gapEnd;
                continue;
            }
            var end = start + 1;
            var min = start;
            var max = start;
            for (; end < Math.Min(start + step, points.Count) && Plain(end); end++)
            {
                if (points[end].Value!.Number < points[min].Value!.Number)
                    min = end;
                if (points[end].Value!.Number > points[max].Value!.Number)
                    max = end;
            }
            keep[start] = keep[min] = keep[max] = keep[end - 1] = true;
            start = end;
        }

        var index = new int[points.Count];
        var output = new List<MetricHistoryPoint>();
        for (var i = 0; i < points.Count; i++)
        {
            index[i] = -1;
            if (!keep[i])
                continue;
            var point = points[i];
            var original = point.ContinuesFrom is var from && from >= 0 && from < i ? from : -1;
            var mapped = original >= 0 ? index[original] : -1;
            index[i] = output.Count;
            var adjacent = mapped >= 0 && mapped == output.Count - 1;
            output.Add(mapped < 0
                ? point with { ContinuesFrom = -1, SkipsOnlyOtherBranches = false }
                : point with
                {
                    ConnectToPrevious = adjacent, ContinuesFrom = adjacent ? -1 : mapped,
                    SkipsOnlyOtherBranches = !adjacent && point.SkipsOnlyOtherBranches,
                });
        }
        return output;
    }

    /// <summary>
    /// Projects <paramref name="points"/> onto <paramref name="range"/> (keeping gaps with null values, partial coverage,
    /// and <see cref="MetricHistoryPoint.Unlinked"/> flags unchanged) and remaps predecessor indices so two kept points are
    /// joined if and only if the later one directly continued the earlier one in <paramref name="points"/>.
    /// </summary>
    public static IReadOnlyList<MetricHistoryPoint> Slice(IReadOnlyList<MetricHistoryPoint> points, HistoryTickRange range)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
            return points;

        if (range.Contains(points[0].Tick) && range.Contains(points[^1].Tick))
            return points;

        var mappedIndex = new int[points.Count];
        var output = new List<MetricHistoryPoint>();
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            if (!range.Contains(point.Tick))
            {
                mappedIndex[i] = -1;
                continue;
            }

            var pred = point.Predecessor(i);
            var mappedPred = pred >= 0 && pred < i ? mappedIndex[pred] : -1;
            mappedIndex[i] = output.Count;
            var adjacent = mappedPred >= 0 && mappedPred == output.Count - 1;
            output.Add(mappedPred < 0
                ? point with { ConnectToPrevious = false, ContinuesFrom = -1, SkipsOnlyOtherBranches = false }
                : point with
                {
                    ConnectToPrevious = adjacent,
                    ContinuesFrom = adjacent ? -1 : mappedPred,
                    SkipsOnlyOtherBranches = !adjacent && point.SkipsOnlyOtherBranches,
                });
        }
        return output;
    }
}
