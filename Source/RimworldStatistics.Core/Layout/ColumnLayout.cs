namespace RimworldStatistics.Core;

public readonly record struct ColumnSpec(float MinWidth, float Weight);

public sealed record ColumnPlan(float[] Widths, float TotalWidth)
{
    public bool NeedsHorizontalScroll(float available) => TotalWidth > Math.Max(0, available);
}

public sealed record PinnedTablePlan(
    float NameWidth,
    float DataViewportWidth,
    float ColumnWidth,
    float DataTotalWidth,
    bool NeedsHorizontalScroll);

public static class ColumnLayout
{

    /// <summary>
    /// Zero-based half-open range of columns intersecting the horizontal viewport.
    /// Offscreen columns must not allocate display strings or register tooltips.
    /// </summary>
    public static (int First, int LastExclusive) VisibleColumns(
        IReadOnlyList<float> widths, float viewportStart, float viewportWidth)
    {
        if (widths.Count == 0 || viewportWidth <= 0)
            return (0, 0);

        var left = Math.Max(0f, viewportStart);
        var right = left + viewportWidth;
        var x = 0f;
        var first = widths.Count;
        var last = widths.Count;
        for (var i = 0; i < widths.Count; i++)
        {
            var width = Math.Max(0f, widths[i]);
            if (x + width > left && x < right)
            {
                if (first == widths.Count)
                    first = i;
                last = i + 1;
            }
            x += width;
            if (x >= right)
                break;
        }
        return (first, last);
    }

    public static ColumnPlan Resolve(IReadOnlyList<ColumnSpec> columns, float available)
    {
        var widths = new float[columns.Count];
        float minimum = 0;
        double totalWeight = 0;
        for (var i = 0; i < widths.Length; i++)
        {
            widths[i] = Math.Max(0, columns[i].MinWidth);
            minimum += widths[i];
            totalWeight += Math.Max(0, columns[i].Weight);
        }

        if (widths.Length == 0 || available <= minimum)
            return new ColumnPlan(widths, minimum);

        var extra = available - minimum;
        for (var i = 0; i < widths.Length; i++)
        {
            var share = totalWeight > 0 ? Math.Max(0, columns[i].Weight) / totalWeight : 1d / widths.Length;
            widths[i] += (float)(extra * share);
        }
        return new ColumnPlan(widths, available);
    }

    public static PinnedTablePlan ResolvePinned(
        float totalWidth,
        int dataColumnCount,
        float scrollbarWidth = 16f,
        float minDataColumnWidth = 155f)
    {
        if (totalWidth <= 0f || dataColumnCount <= 0)
            return new PinnedTablePlan(0f, 0f, 0f, 0f, false);

        var safeScrollbar = Math.Max(0f, scrollbarWidth);
        var innerWidth = Math.Max(0f, totalWidth - safeScrollbar);
        if (innerWidth <= 0f)
            return new PinnedTablePlan(0f, 0f, 0f, 0f, false);

        var nameWidth = innerWidth >= 420f
            ? Math.Clamp(innerWidth * 0.30f, 180f, 280f)
            : innerWidth * 0.36f;
        var dataViewportWidth = Math.Max(0f, innerWidth - nameWidth);
        var minCol = Math.Max(1f, minDataColumnWidth);
        var columnWidth = Math.Max(minCol, dataViewportWidth / dataColumnCount);
        var dataTotalWidth = columnWidth * dataColumnCount;
        var needsHScroll = dataTotalWidth > dataViewportWidth + 0.5f;
        return new PinnedTablePlan(nameWidth, dataViewportWidth, columnWidth, dataTotalWidth, needsHScroll);
    }

    public static (int First, int LastExclusive) VisibleUniformColumns(
        int columnCount, float columnWidth, float viewportStart, float viewportWidth)
    {
        if (columnCount <= 0 || columnWidth <= 0f || viewportWidth <= 0f)
            return (0, 0);

        var left = Math.Max(0f, viewportStart);
        var right = left + viewportWidth;
        var first = Math.Clamp((int)Math.Floor(left / columnWidth), 0, columnCount);
        var last = Math.Clamp((int)Math.Ceiling(right / columnWidth), first, columnCount);
        return (first, last);
    }
}
