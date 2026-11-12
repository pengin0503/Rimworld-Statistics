namespace RimworldStatistics.Core;

public readonly record struct ColumnSpec(float MinWidth, float Weight);

public sealed record ColumnPlan(float[] Widths, float TotalWidth)
{
    public bool NeedsHorizontalScroll(float available) => TotalWidth > Math.Max(0, available);
}

public static class ColumnLayout
{
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
}
