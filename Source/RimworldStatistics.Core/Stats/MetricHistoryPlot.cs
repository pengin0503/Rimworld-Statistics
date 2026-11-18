namespace RimworldStatistics.Core;

/// <summary>Drawing support for per-colony histories. It changes only what is drawn, never the stored readings.</summary>
public static class MetricHistoryPlot
{
    /// <summary>
    /// Peak-preserving 1px X-column Min-Max Envelope decimation for per-colony histories:
    /// maps each point's game tick onto its 1px horizontal column across <paramref name="width"/> pixels and, within each
    /// continuous run of plain same-coverage readings inside a single 1px column, keeps at most 4 points
    /// <c>(first, min, max, last)</c> in chronological order. True extrema, missing-data gaps, partial-coverage transitions,
    /// unlinked points, and branch anchors (<see cref="MetricHistoryPoint.ContinuesFrom"/>) are 100% preserved and
    /// renumbered. When <paramref name="points"/> already fits within <paramref name="width"/> (or within <c>2 * width</c>
    /// while zoomed in), the raw <paramref name="points"/> list is returned untouched.
    /// </summary>
    public static IReadOnlyList<MetricHistoryPoint> Thin(IReadOnlyList<MetricHistoryPoint> points, int width)
        => Thin(points, width, axisFirstTick: null, axisLastTick: null, isZoomed: false);

    public static IReadOnlyList<MetricHistoryPoint> Thin(
        IReadOnlyList<MetricHistoryPoint> points,
        int width,
        int? axisFirstTick,
        int? axisLastTick,
        bool isZoomed = false)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (width < 1 || points.Count <= width || (isZoomed && points.Count <= width * 2L))
            return points;

        int firstTick;
        int lastTick;
        if (axisFirstTick is { } explicitFirst && axisLastTick is { } explicitLast)
        {
            firstTick = explicitFirst;
            lastTick = explicitLast;
        }
        else
        {
            firstTick = points[0].Tick;
            lastTick = points[0].Tick;
            for (var i = 1; i < points.Count; i++)
            {
                var tick = points[i].Tick;
                if (tick < firstTick) firstTick = tick;
                if (tick > lastTick) lastTick = tick;
            }
        }

        var tickSpan = (double)lastTick - firstTick;
        int PixelColumn(int tick)
        {
            if (width <= 1 || tickSpan <= 0d)
                return 0;
            var ratio = (tick - (double)firstTick) / tickSpan;
            return Math.Clamp((int)Math.Floor(ratio * width), 0, width - 1);
        }

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
        for (var start = 0; start < points.Count;)
        {
            var startCol = PixelColumn(points[start].Tick);
            if (points[start].Value?.Number is null)
            {
                var gapEnd = start + 1;
                for (; gapEnd < points.Count && PlainGap(gapEnd) && PixelColumn(points[gapEnd].Tick) == startCol; gapEnd++)
                {
                }
                keep[start] = keep[gapEnd - 1] = true;
                start = gapEnd;
                continue;
            }
            var end = start + 1;
            var min = start;
            var max = start;
            for (; end < points.Count && Plain(end) && PixelColumn(points[end].Tick) == startCol; end++)
            {
                if (points[end].Value!.Number < points[min].Value!.Number)
                    min = end;
                if (points[end].Value!.Number > points[max].Value!.Number)
                    max = end;
            }
            keep[start] = keep[min] = keep[max] = keep[end - 1] = true;
            start = end;
        }

        var keptCount = 0;
        for (var i = 0; i < keep.Length; i++)
            if (keep[i])
                keptCount++;
        if (keptCount == points.Count)
            return points;

        var index = new int[points.Count];
        var output = new List<MetricHistoryPoint>(keptCount);
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
