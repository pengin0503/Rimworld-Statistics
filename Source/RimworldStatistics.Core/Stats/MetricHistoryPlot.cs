namespace RimworldStatistics.Core;

/// <summary>Drawing support for per-colony histories. It changes only what is drawn, never the stored readings.</summary>
public static class MetricHistoryPlot
{
    /// <summary>
    /// For a history much denser than <paramref name="width"/> pixels: within each run of readings that each continue the
    /// previous one, keeps the first, lowest, highest and last reading of every pixel-sized chunk. Gaps, unlinked readings,
    /// branch starts and every reading another branch continues from are always kept, and branch references are
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

        // A plain reading only continues the one before it: it can be merged into that run.
        bool Plain(int i) => i > 0 && points[i].ConnectToPrevious && points[i].ContinuesFrom < 0 && !points[i].Unlinked
            && !referenced[i] && points[i].Value?.Number is not null && points[i - 1].Value?.Number is not null;

        var keep = new bool[points.Count];
        var step = Math.Max(1, points.Count / width);
        for (var start = 0; start < points.Count;)
        {
            if (points[start].Value?.Number is null)
            {
                // A run of gaps draws nothing: one gap marker is enough unless a reading refers to another one.
                keep[start] = start == 0 || points[start - 1].Value?.Number is not null || referenced[start]
                    || points[start].Unlinked || points[start].ContinuesFrom >= 0;
                start++;
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
            output.Add(mapped < 0
                ? point with { ContinuesFrom = -1 }
                : point with { ConnectToPrevious = mapped == output.Count - 1, ContinuesFrom = mapped == output.Count - 1 ? -1 : mapped });
        }
        return output;
    }
}
