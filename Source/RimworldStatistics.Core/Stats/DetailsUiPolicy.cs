namespace RimworldStatistics.Core;

public enum MetricVisualKind
{
    Complete,
    Partial,
    Missing,
    Experimental,
    NotApplicable,
}

public enum CellAlignment
{
    Left,
    Right,
}

public static class MetricVisualState
{
    /// <summary>A group is complete only when every member is complete; mixed states are partial.</summary>
    public static MetricVisualKind ClassifyGroup(IReadOnlyList<MetricStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        if (statuses.Count == 0)
            return MetricVisualKind.Missing;
        var first = Classify(statuses[0]);
        for (var i = 1; i < statuses.Count; i++)
            if (Classify(statuses[i]) != first)
                return MetricVisualKind.Partial;
        return first;
    }

    public static MetricVisualKind Classify(MetricStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (status.Availability == MetricAvailability.NoApplicableColonies)
            return MetricVisualKind.NotApplicable;
        if (status.Availability == MetricAvailability.NoMeasuredColonies)
            return MetricVisualKind.Missing;
        if (MetricStatusClassifier.IsPartial(status))
            return MetricVisualKind.Partial;
        if (status.Verification != MetricVerification.Standard)
            return MetricVisualKind.Experimental;
        return MetricVisualKind.Complete;
    }

    public static MetricVisualKind Classify(MetricDefinition definition, NumericValue value)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(value);
        if (value.Coverage == MetricCoverage.NotApplicable)
            return MetricVisualKind.NotApplicable;
        if (value.Coverage == MetricCoverage.Unknown)
            return MetricVisualKind.Missing;
        if (value.Coverage == MetricCoverage.Partial)
            return MetricVisualKind.Partial;
        if (definition.ProviderId is not null
            || definition.Acquisition is not null && !definition.Published
            || value.Evidence is MetricEvidence.Experimental or MetricEvidence.Mixed)
            return MetricVisualKind.Experimental;
        return MetricVisualKind.Complete;
    }

    public static MetricVisualKind Classify(NumericRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Value.Coverage == MetricCoverage.NotApplicable
            || MetricAvailabilityPolicy.Classify(row) == MetricAvailability.NoApplicableColonies)
            return MetricVisualKind.NotApplicable;
        if (row.Value.Coverage == MetricCoverage.Unknown
            || MetricAvailabilityPolicy.Classify(row) == MetricAvailability.NoMeasuredColonies)
            return MetricVisualKind.Missing;
        if (MetricStatusClassifier.IsPartial(row))
            return MetricVisualKind.Partial;
        if (MetricStatusClassifier.IsExperimental(row))
            return MetricVisualKind.Experimental;
        return MetricVisualKind.Complete;
    }
}

public readonly record struct DetailsColumnMetrics(
    float LeftOffset,
    float RightPad,
    float ContentWidth,
    HorizontalSpan ChartIcon);

public static class DetailsTableLayout
{
    public const float ChartIconSize = 12f;
    public const float ChartIconMarginRight = 6f;

    public static CellAlignment ColumnAlignment(int columnIndex)
        => columnIndex is 1 or 3 ? CellAlignment.Right : CellAlignment.Left;

    public static DetailsColumnMetrics MeasureColumn(
        int columnIndex,
        float columnX,
        float columnWidth,
        float indent,
        bool isMetricRow,
        bool hasExpand)
    {
        var leftOffset = columnIndex == 0 ? (isMetricRow ? 30f : 3f) + Math.Max(0f, indent) : 3f;
        var rightPad = columnIndex == 0
            ? (isMetricRow ? ChartIconSize + ChartIconMarginRight + 4f : 4f)
            : 4f;

        var safeWidth = Math.Max(0f, columnWidth);
        HorizontalSpan chartIcon = default;
        if (columnIndex == 0 && isMetricRow)
        {
            var minPrefix = leftOffset + (hasExpand ? 25f : 0f);
            var iconX = columnX + safeWidth - ChartIconMarginRight - ChartIconSize;
            if (iconX >= columnX + minPrefix + 4f && iconX + ChartIconSize <= columnX + safeWidth)
                chartIcon = new HorizontalSpan(iconX, ChartIconSize);
            else
                rightPad = 4f;
        }

        var contentWidth = Math.Max(0f, safeWidth - leftOffset - rightPad);
        return new DetailsColumnMetrics(leftOffset, rightPad, contentWidth, chartIcon);
    }
}

/// <summary>
/// Row heights of the Details table (#306): a statistic shown with its provider ("MOD: name") under the title needs two
/// lines; every other row (and the header) needs one, so it is lower and more rows fit.
/// </summary>
public static class DetailsRowHeights
{
    public const float Header = 30f;
    // One line of text, with room above and below: the provider row fits the same line into TitleLineHeight.
    public const float Plain = 28f;
    // The title line, then the provider line from SourceLineTop.
    public const float WithSource = 36f;
    public const float TitleLineHeight = 19f;
    public const float SourceLineTop = 18f;
    public const float SourceLineHeight = 18f;

    public static float Row(bool hasSource) => hasSource ? WithSource : Plain;
}

/// <summary>
/// The top of each row of a list whose rows have different heights (prefix sums), and the rows a scroll position shows,
/// found by binary search, so that only the visible rows are drawn however long the list is.
/// </summary>
public sealed class RowOffsets
{
    public static RowOffsets Empty { get; } = new([]);

    // tops[i] is the top of row i; tops[Count] is the height of the whole list.
    private readonly float[] tops;

    public RowOffsets(IReadOnlyList<float> heights)
    {
        tops = new float[heights.Count + 1];
        for (var i = 0; i < heights.Count; i++)
            tops[i + 1] = tops[i] + Math.Max(0f, heights[i]);
    }

    public int Count => tops.Length - 1;

    public float Total => tops[^1];

    public float Top(int row) => tops[row];

    public float Height(int row) => tops[row + 1] - tops[row];

    /// <summary>
    /// The first and last row that overlap [scrollY, scrollY + viewHeight), the range the scroll view shows;
    /// (0, -1) when none does.
    /// </summary>
    public (int First, int Last) Visible(float scrollY, float viewHeight)
    {
        if (Count == 0 || !(viewHeight > 0f) || float.IsNaN(scrollY) || float.IsInfinity(scrollY))
            return (0, -1);
        var top = Math.Max(0f, scrollY);
        var bottom = scrollY + viewHeight;
        if (top >= Total || bottom <= 0f)
            return (0, -1);
        var first = RowAt(top);
        // The last row that starts above the bottom edge; a row that starts exactly there is not shown.
        var last = LastStartingBefore(bottom);
        return first <= last ? (first, last) : (0, -1);
    }

    /// <summary>The row that contains y: the last row whose top is at or above y (y is clamped to the list).</summary>
    public int RowAt(float y)
    {
        if (Count == 0)
            return -1;
        int lo = 0, hi = Count - 1;
        while (lo < hi)
        {
            var mid = lo + (hi - lo + 1) / 2;
            if (tops[mid] <= y)
                lo = mid;
            else
                hi = mid - 1;
        }
        return lo;
    }

    private int LastStartingBefore(float y)
    {
        int lo = 0, hi = Count - 1;
        if (!(tops[0] < y))
            return -1;
        while (lo < hi)
        {
            var mid = lo + (hi - lo + 1) / 2;
            if (tops[mid] < y)
                lo = mid;
            else
                hi = mid - 1;
        }
        return lo;
    }
}

public static class FilterSummary
{
    public static int CountActiveFilters(
        bool hasPrimaryFilter,
        bool hasSecondaryFilter,
        bool hasTertiaryFilter,
        string? search)
    {
        var count = 0;
        if (hasPrimaryFilter)
            count++;
        if (hasSecondaryFilter)
            count++;
        if (hasTertiaryFilter)
            count++;
        if (!string.IsNullOrWhiteSpace(search))
            count++;
        return count;
    }
}
