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
