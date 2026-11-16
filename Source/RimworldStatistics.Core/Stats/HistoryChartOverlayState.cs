namespace RimworldStatistics.Core;

public readonly record struct HistoryLegendPlacement(int Index, float X, float Y, float Width);

public sealed record HistoryLegendPlan(
    IReadOnlyList<HistoryLegendPlacement> Items,
    float TotalHeight,
    int RowCount);

/// <summary>Computes wrapped multi-line placements for chart legend items so narrow windows never drop items.</summary>
public static class HistoryLegendLayout
{
    public static HistoryLegendPlan Resolve(
        IReadOnlyList<float> itemWidths,
        float maxWidth,
        float rowHeight = 22f,
        float itemGap = 16f,
        float rowGap = 2f)
    {
        if (itemWidths.Count == 0 || !float.IsFinite(maxWidth) || maxWidth <= 0f)
            return new HistoryLegendPlan([], 0f, 0);

        var safeRowHeight = float.IsFinite(rowHeight) && rowHeight > 0f ? rowHeight : 22f;
        var safeItemGap = float.IsFinite(itemGap) && itemGap >= 0f ? itemGap : 0f;
        var safeRowGap = float.IsFinite(rowGap) && rowGap >= 0f ? rowGap : 0f;

        var items = new HistoryLegendPlacement[itemWidths.Count];
        var x = 0f;
        var y = 0f;
        var rowCount = 1;
        var itemsInRow = 0;

        for (var i = 0; i < itemWidths.Count; i++)
        {
            var raw = float.IsFinite(itemWidths[i]) && itemWidths[i] > 0f ? itemWidths[i] : 0f;
            var width = Math.Min(raw, maxWidth);
            if (itemsInRow > 0 && x + width > maxWidth + 0.01f)
            {
                rowCount++;
                x = 0f;
                y += safeRowHeight + safeRowGap;
                itemsInRow = 0;
            }

            items[i] = new HistoryLegendPlacement(i, x, y, width);
            x += width + safeItemGap;
            itemsInRow++;
        }

        var totalHeight = rowCount * safeRowHeight + (rowCount - 1) * safeRowGap;
        return new HistoryLegendPlan(items, totalHeight, rowCount);
    }
}

public enum HistoryOverlayStatus
{
    Loading = 0,
    Completed = 1,
    Empty = 2,
    Incompatible = 3,
}

public readonly record struct HistoryPlotBounds(
    int FirstTick,
    int LastTick,
    double MinValue,
    double MaxValue,
    bool HasPartial,
    bool HasUnlinked);

/// <summary>Tracks comparison colony overlays, slot colors, async cancellation/staleness, and aggregate plot bounds.</summary>
public sealed class HistoryOverlayTracker
{
    public sealed class Entry
    {
        internal Entry(string colonyKey, string colonyName, int colorIndex, long requestId)
        {
            ColonyKey = colonyKey;
            ColonyName = colonyName;
            ColorIndex = colorIndex;
            RequestId = requestId;
        }

        public string ColonyKey { get; }
        public string ColonyName { get; }
        public int ColorIndex { get; }
        public long RequestId { get; }
        public HistoryOverlayStatus Status { get; internal set; } = HistoryOverlayStatus.Loading;
        public IReadOnlyList<MetricHistoryPoint> RawPoints { get; internal set; } = [];
        public MetricHistoryPoint[] Measured { get; internal set; } = [];
        internal int CachedWidth { get; set; }
        internal IReadOnlyList<MetricHistoryPoint> ThinnedPoints { get; set; } = [];

        public IReadOnlyList<MetricHistoryPoint> GetOrThinPoints(int pixelWidth)
        {
            if (Status != HistoryOverlayStatus.Completed)
                return [];
            if (CachedWidth != pixelWidth)
            {
                ThinnedPoints = MetricHistoryPlot.Thin(RawPoints, pixelWidth);
                CachedWidth = pixelWidth;
            }
            return ThinnedPoints;
        }
    }

    private readonly HashSet<string> selected;
    private readonly List<Entry> entries = [];
    private readonly int maxOverlays;
    private long nextRequestId;

    public HistoryOverlayTracker(string primaryColonyKey, int maxOverlays = 2)
    {
        ArgumentException.ThrowIfNullOrEmpty(primaryColonyKey);
        if (maxOverlays < 0)
            throw new ArgumentOutOfRangeException(nameof(maxOverlays));
        this.maxOverlays = maxOverlays;
        selected = new HashSet<string>(StringComparer.Ordinal) { primaryColonyKey };
    }

    public int MaxOverlays => maxOverlays;
    public IReadOnlyList<Entry> Entries => entries;
    public bool CanAdd => entries.Count < maxOverlays;

    public bool IsSelected(string colonyKey) => selected.Contains(colonyKey);

    public bool TryAdd(string colonyKey, string colonyName, out Entry? entry)
    {
        if (entries.Count >= maxOverlays || string.IsNullOrEmpty(colonyKey) || !selected.Add(colonyKey))
        {
            entry = null;
            return false;
        }

        var colorIndex = FirstUnusedColorIndex();
        entry = new Entry(colonyKey, colonyName, colorIndex, ++nextRequestId);
        entries.Add(entry);
        return true;
    }

    public bool Remove(string colonyKey)
    {
        var index = entries.FindIndex(e => string.Equals(e.ColonyKey, colonyKey, StringComparison.Ordinal));
        if (index < 0)
            return false;

        entries.RemoveAt(index);
        selected.Remove(colonyKey);
        return true;
    }

    public bool TryComplete(long requestId, MetricHistory history, MetricHistoryKind primaryKind)
    {
        ArgumentNullException.ThrowIfNull(history);
        var entry = FindByRequestId(requestId);
        if (entry is null)
            return false;

        var measured = history.Points.Where(p => p.Value is not null).ToArray();
        if (measured.Length == 0)
        {
            entry.Status = HistoryOverlayStatus.Empty;
            entry.RawPoints = [];
            entry.Measured = [];
            entry.ThinnedPoints = [];
            entry.CachedWidth = 0;
            return true;
        }

        if (history.Kind != primaryKind)
        {
            entry.Status = HistoryOverlayStatus.Incompatible;
            entry.RawPoints = [];
            entry.Measured = [];
            entry.ThinnedPoints = [];
            entry.CachedWidth = 0;
            return true;
        }

        entry.Status = HistoryOverlayStatus.Completed;
        entry.RawPoints = history.Points;
        entry.Measured = measured;
        entry.ThinnedPoints = [];
        entry.CachedWidth = 0;
        return true;
    }

    public bool TryFail(long requestId)
    {
        var entry = FindByRequestId(requestId);
        if (entry is null)
            return false;

        entry.Status = HistoryOverlayStatus.Empty;
        entry.RawPoints = [];
        entry.Measured = [];
        entry.ThinnedPoints = [];
        entry.CachedWidth = 0;
        return true;
    }

    public HistoryPlotBounds ComputeBounds(
        MetricHistory primaryHistory,
        IReadOnlyList<MetricHistoryPoint> primaryMeasured)
    {
        ArgumentNullException.ThrowIfNull(primaryHistory);
        ArgumentNullException.ThrowIfNull(primaryMeasured);

        var firstTick = primaryHistory.Points.Count > 0 ? primaryHistory.Points[0].Tick : 0;
        var lastTick = primaryHistory.Points.Count > 0 ? primaryHistory.Points[^1].Tick : 0;
        var minValue = double.PositiveInfinity;
        var maxValue = double.NegativeInfinity;
        var hasPartial = false;
        var hasUnlinked = false;

        FoldMeasured(primaryMeasured, ref minValue, ref maxValue, ref hasPartial, ref hasUnlinked);

        foreach (var entry in entries)
        {
            if (entry.Status != HistoryOverlayStatus.Completed || entry.RawPoints.Count == 0)
                continue;
            firstTick = Math.Min(firstTick, entry.RawPoints[0].Tick);
            lastTick = Math.Max(lastTick, entry.RawPoints[^1].Tick);
            FoldMeasured(entry.Measured, ref minValue, ref maxValue, ref hasPartial, ref hasUnlinked);
        }

        if (!double.IsFinite(minValue) || !double.IsFinite(maxValue))
        {
            minValue = 0d;
            maxValue = 0d;
        }

        return new HistoryPlotBounds(firstTick, lastTick, minValue, maxValue, hasPartial, hasUnlinked);
    }

    private Entry? FindByRequestId(long requestId)
    {
        for (var i = 0; i < entries.Count; i++)
            if (entries[i].RequestId == requestId)
                return entries[i];
        return null;
    }

    private int FirstUnusedColorIndex()
    {
        for (var candidate = 0; candidate < maxOverlays; candidate++)
        {
            var used = false;
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].ColorIndex == candidate)
                {
                    used = true;
                    break;
                }
            }
            if (!used)
                return candidate;
        }
        return 0;
    }

    private static void FoldMeasured(
        IReadOnlyList<MetricHistoryPoint> points,
        ref double minValue,
        ref double maxValue,
        ref bool hasPartial,
        ref bool hasUnlinked)
    {
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            if (p.Unlinked)
                hasUnlinked = true;
            if (p.Value is not { } v)
                continue;
            if (v.Coverage == MetricCoverage.Partial)
                hasPartial = true;
            if (v.Number is { } n && double.IsFinite(n))
            {
                if (n < minValue)
                    minValue = n;
                if (n > maxValue)
                    maxValue = n;
            }
        }
    }
}
