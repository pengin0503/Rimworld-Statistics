using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>
/// On-demand comparison of colony readings or the breakdown of one aggregate.
/// A colony row opens the recorded history of that metric.
/// </summary>
internal sealed class Dialog_MetricChart : Window
{
    private const float RowHeight = 34f;
    private const float AxisHeaderHeight = 24f;

    private enum ChartMode { Colony, Breakdown, Overall }

    private sealed record BarText(string Name, string Value, string Tip);

    /// <summary>One immutable series with its axis range; row text is built once, when the row is first shown.</summary>
    private sealed class Series
    {
        public Series(ChartMode mode, IReadOnlyList<MetricChartBar> bars, MetricUnit unit)
        {
            Mode = mode;
            Bars = bars;
            var (rawMin, rawMax) = RangeOf(bars);
            Ticks = MetricChartTicks.Generate(rawMin, rawMax);
            Range = (Ticks[0], Ticks[^1]);
            var percent = unit == MetricUnit.Percent;
            var suffix = unit == MetricUnit.Mood ? "%" : "";
            TickLabels = Ticks.Select(t => MetricChartTicks.FormatAxisValue(t, percent) + suffix).ToArray();
            Text = new BarText?[bars.Count];
        }

        public ChartMode Mode { get; }
        public IReadOnlyList<MetricChartBar> Bars { get; }
        public IReadOnlyList<double> Ticks { get; }
        public string[] TickLabels { get; }
        public (double Min, double Max) Range { get; }
        public BarText?[] Text { get; }
    }

    private readonly StatsView view;
    private readonly MetricDefinition definition;
    private readonly MetricDefinition breakdownDefinition;
    private readonly string title;
    private readonly string metricLabel;
    private readonly string? modSource;
    private readonly Series colony;
    private readonly Series breakdown;
    private readonly Series overall;
    private readonly string? breakdownColony;
    private readonly string? breakdownButtonLabel;
    private bool showBreakdown;
    private Vector2 scroll;

    internal Dialog_MetricChart(StatsView view, string metricKey, string? selectedColony)
    {
        this.view = view;
        string chartLabel;
        IReadOnlyList<MetricChartBar> colonyBars, breakdownBars, overallBars;
        if (BasicMetrics.Contains(metricKey))
        {
            definition = BasicMetrics.Definition(metricKey);
            chartLabel = OverviewTab.BasicLabel(metricKey);
            colonyBars = MetricChartSeries.BasicByColony(view.Stats, metricKey);
            breakdownBars = [];
            breakdownColony = null;
            var total = BasicMetrics.OverallValue(view.Stats, metricKey);
            overallBars = total is { } n && double.IsFinite(n)
                ? [new MetricChartBar(metricKey, new NumericValue { Number = n, Coverage = MetricCoverage.SnapshotOnly })]
                : [];
        }
        else
        {
            definition = MetricLookup.Definition(view, metricKey)
                ?? throw new ArgumentException("Metric not found in the current view.", nameof(metricKey));
            chartLabel = MetricText.Label(definition);
            colonyBars = MetricChartSeries.ByColony(view.Numeric, definition);
            IReadOnlyList<MetricChartBar> selected = selectedColony is null ? []
                : MetricChartSeries.Breakdowns(view.Numeric, metricKey, selectedColony);
            breakdownColony = selected.Count > 0 ? selectedColony : null;
            breakdownBars = breakdownColony is null
                ? MetricChartSeries.Breakdowns(view.Numeric, metricKey) : selected;
            overallBars = MetricChartSeries.Overall(view.Numeric, metricKey);
        }
        breakdownDefinition = definition.BreakdownUnit is { } unit ? definition with { Unit = unit } : definition;
        metricLabel = chartLabel;
        modSource = MetricText.ModSourceLabel(definition);
        title = "RWS_Chart_Title".Translate(chartLabel);
        colony = new Series(ChartMode.Colony, colonyBars, definition.Unit);
        breakdown = new Series(ChartMode.Breakdown, breakdownBars, breakdownDefinition.Unit);
        overall = new Series(ChartMode.Overall, overallBars, definition.Unit);
        if (breakdownBars.Count > 0)
            breakdownButtonLabel = breakdownColony is null
                ? (string)"RWS_Chart_ByCategory".Translate()
                : (string)"RWS_Chart_ByCategoryColony".Translate(ColonyName(breakdownColony));
        showBreakdown = breakdownColony is not null || (colonyBars.Count == 0 && breakdownBars.Count > 0);
        doCloseX = true;
        absorbInputAroundWindow = true;
        closeOnClickedOutside = false;
    }

    public override Vector2 InitialSize => new(Math.Min(1050f, Verse.UI.screenWidth * 0.90f), Math.Min(650f, Verse.UI.screenHeight * 0.88f));

    public override void DoWindowContents(Rect rect)
    {
        Text.Font = GameFont.Medium;
        var titleRect = new Rect(rect.x, rect.y, Math.Max(0f, rect.width - 35f), 32f);
        var shownTitle = title.Truncate(titleRect.width);
        Widgets.Label(titleRect, shownTitle);
        if (shownTitle != title)
            TooltipHandler.TipRegion(titleRect, title);
        Text.Font = GameFont.Small;

        var canCompare = colony.Bars.Count > 0;
        var canBreakdown = breakdown.Bars.Count > 0;
        var y = rect.y + 38f;
        if (modSource is { } source)
        {
            var sourceRect = new Rect(rect.x, y, rect.width, 20f);
            var shown = source.Truncate(sourceRect.width);
            Widgets.Label(sourceRect, shown);
            if (shown != source)
                TooltipHandler.TipRegion(sourceRect, source);
            y += 22f;
        }
        if (canCompare && canBreakdown)
        {
            var compareRect = new Rect(rect.x, y, Math.Min(205f, rect.width * 0.42f), 29f);
            var breakdownRect = new Rect(compareRect.xMax + 8f, y, Math.Min(300f, rect.width - compareRect.width - 8f), 29f);
            if (Widgets.ButtonText(compareRect, "RWS_Chart_ByColony".Translate(), active: showBreakdown))
            {
                showBreakdown = false;
                scroll = Vector2.zero;
            }
            if (Widgets.ButtonText(breakdownRect, breakdownButtonLabel!.Truncate(breakdownRect.width - 12f), active: !showBreakdown))
            {
                showBreakdown = true;
                scroll = Vector2.zero;
            }
            TooltipHandler.TipRegion(breakdownRect, breakdownButtonLabel);
            y += 36f;
        }

        var series = showBreakdown && canBreakdown ? breakdown : canCompare ? colony
            : canBreakdown ? breakdown : overall;
        if (series.Bars.Count == 0)
        {
            // No series has a measured value: no explanation of a chart that is not drawn.
            Widgets.Label(new Rect(rect.x, y, rect.width, Math.Max(0f, rect.yMax - y)), "RWS_Chart_NoData".Translate());
            return;
        }
        var note = series.Mode switch
        {
            ChartMode.Breakdown => (string)"RWS_Chart_BreakdownNote".Translate(),
            ChartMode.Overall => (string)"RWS_Chart_OverallNote".Translate(),
            _ => OpensHistory ? (string)"RWS_Chart_ColonyNote".Translate() : (string)"RWS_Chart_ColonyNoHistory".Translate(),
        };
        var noteHeight = Math.Max(27f, Text.CalcHeight(note, rect.width - 6f));
        Widgets.Label(new Rect(rect.x, y, rect.width, noteHeight), note);
        y += noteHeight + 5f;
        Widgets.Label(new Rect(rect.x, y, rect.width, 25f), "RWS_Chart_Points".Translate(series.Bars.Count));
        y += 28f;
        DrawBars(new Rect(rect.x, y, rect.width, Math.Max(0f, rect.yMax - y)), series);
    }

    private void DrawBars(Rect rect, Series series)
    {
        if (rect.height <= AxisHeaderHeight)
            return;

        var bars = series.Bars;
        var width = Math.Max(0f, rect.width - 18f);
        var labelWidth = Math.Min(260f, width * 0.31f);
        var valueWidth = Math.Min(160f, Math.Max(92f, width * 0.19f));
        var plotWidth = Math.Max(1f, width - labelWidth - valueWidth - 18f);
        var plotX = labelWidth + 8f;
        var minimum = series.Range.Min;
        var maximum = series.Range.Max;
        var scale = Math.Max(1d, Math.Max(Math.Abs(minimum), Math.Abs(maximum)));
        var minScaled = minimum / scale;
        var spanScaled = maximum / scale - minScaled;
        float Position(double value) => spanScaled <= 0d ? plotX
            : plotX + plotWidth * (float)Math.Clamp((value / scale - minScaled) / spanScaled, 0d, 1d);
        var zero = Position(0d);

        DrawAxisHeader(new Rect(rect.x, rect.y, width, AxisHeaderHeight), plotX, plotWidth, series, Position);

        var bodyRect = new Rect(rect.x, rect.y + AxisHeaderHeight, rect.width, Math.Max(0f, rect.height - AxisHeaderHeight));
        Widgets.BeginScrollView(bodyRect, ref scroll, new Rect(0f, 0f, width, bars.Count * RowHeight));
        var first = Math.Max(0, (int)(scroll.y / RowHeight));
        var last = Math.Min(bars.Count - 1, (int)((scroll.y + bodyRect.height) / RowHeight) + 1);
        var anchor = Text.Anchor;
        MetricChartBar? selected = null;
        for (var i = first; i <= last; i++)
        {
            var bar = bars[i];
            if (bar.Value.Number is not { } value || !double.IsFinite(value))
                continue;
            var text = series.Text[i] ??= Describe(series.Mode, bar);
            var row = new Rect(0, i * RowHeight, width, RowHeight);
            if (i % 2 == 1)
                Widgets.DrawHighlight(row, 0.2f);
            Widgets.DrawHighlightIfMouseover(row);
            if (series.Mode == ChartMode.Colony && OpensHistory && Widgets.ButtonInvisible(row))
                selected = bar;

            Text.Anchor = TextAnchor.MiddleLeft;
            var labelRect = new Rect(4f, row.y, Math.Max(0, labelWidth - 8f), RowHeight);
            Widgets.Label(labelRect, text.Name.Truncate(labelRect.width));

            var plotRow = new Rect(plotX, row.y + 5f, plotWidth, RowHeight - 10f);
            for (var t = 0; t < series.Ticks.Count; t++)
            {
                var tickValue = series.Ticks[t];
                var tx = Position(tickValue);
                if (tickValue == 0d)
                    continue;
                Widgets.DrawBoxSolid(new Rect(tx, row.y, 1f, RowHeight), new Color(0.7f, 0.7f, 0.7f, 0.16f));
            }
            Widgets.DrawBoxSolid(new Rect(zero - 0.5f, row.y, 1.5f, RowHeight), new Color(0.86f, 0.88f, 0.92f, 0.85f));

            var end = Position(value);
            if (value != 0d)
            {
                var start = Math.Min(zero, end);
                var length = Math.Max(1f, Math.Abs(end - zero));
                Widgets.DrawBoxSolid(new Rect(start, plotRow.y + 3f, length, plotRow.height - 6f),
                    bar.Value.Coverage == MetricCoverage.Partial
                        ? new Color(0.63f, 0.66f, 0.71f) : new Color(0.35f, 0.64f, 0.78f));
            }

            Text.Anchor = TextAnchor.MiddleRight;
            var valueRect = new Rect(plotX + plotWidth + 10f, row.y, Math.Max(0f, valueWidth - 10f), RowHeight);
            Widgets.Label(valueRect, text.Value.Truncate(valueRect.width));
            TooltipHandler.TipRegion(row, text.Tip);
        }
        Text.Anchor = anchor;
        Widgets.EndScrollView();
        if (selected is not null)
        {
            var key = MetricChartSeries.ColonyComparisonKey(definition);
            var source = key == definition.Key ? definition : MetricLookup.Definition(view, key) ?? definition;
            var label = key == definition.Key ? metricLabel : MetricText.Label(source);
            Find.WindowStack.Add(new Dialog_ColonyMetricHistory(view, selected.Key, ColonyName(selected.Key), source, label, selected.Value));
        }
    }

    private static void DrawAxisHeader(Rect headerRect, float plotX, float plotWidth, Series series, Func<double, float> position)
    {
        var axisStart = headerRect.x + plotX;
        var axisEnd = axisStart + plotWidth;
        Widgets.DrawBoxSolid(new Rect(axisStart, headerRect.yMax - 2f, plotWidth, 1f), new Color(0.75f, 0.77f, 0.82f, 0.50f));

        var prevAnchor = Text.Anchor;
        var prevColor = GUI.color;
        GUI.color = new Color(0.80f, 0.82f, 0.86f);
        var lastRight = float.NegativeInfinity;
        var tickCount = series.Ticks.Count;
        var rightmostReserved = tickCount > 1
            ? Math.Max(axisStart, axisEnd - Text.CalcSize(series.TickLabels[^1]).x - 8f)
            : float.PositiveInfinity;

        for (var i = 0; i < tickCount; i++)
        {
            var tickValue = series.Ticks[i];
            var tickLabel = series.TickLabels[i];
            var tx = headerRect.x + position(tickValue);
            var isZero = tickValue == 0d;
            Widgets.DrawBoxSolid(new Rect(tx - (isZero ? 0.5f : 0f), headerRect.yMax - 6f, isZero ? 1.5f : 1f, 5f),
                isZero ? new Color(0.90f, 0.92f, 0.96f, 0.90f) : new Color(0.75f, 0.77f, 0.82f, 0.65f));

            var textWidth = Math.Min(plotWidth, Text.CalcSize(tickLabel).x + 4f);
            float labelX;
            if (i == 0)
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                labelX = Math.Clamp(tx, axisStart, Math.Max(axisStart, axisEnd - textWidth));
            }
            else if (i == tickCount - 1)
            {
                Text.Anchor = TextAnchor.MiddleRight;
                labelX = Math.Clamp(tx - textWidth, axisStart, Math.Max(axisStart, axisEnd - textWidth));
            }
            else
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                labelX = Math.Clamp(tx - textWidth / 2f, axisStart, Math.Max(axisStart, axisEnd - textWidth));
            }

            if (i > 0 && labelX < lastRight + 6f)
                continue;
            if (i > 0 && i < tickCount - 1 && labelX + textWidth > rightmostReserved)
                continue;

            Widgets.Label(new Rect(labelX, headerRect.y, textWidth, headerRect.height - 4f), tickLabel);
            lastRight = labelX + textWidth;
        }

        GUI.color = prevColor;
        Text.Anchor = prevAnchor;
    }

    private BarText Describe(ChartMode mode, MetricChartBar bar)
    {
        var name = mode switch
        {
            ChartMode.Overall => (string)"RWS_Details_AllColonies".Translate(),
            ChartMode.Breakdown => BreakdownName(bar.Key),
            _ => ColonyName(bar.Key),
        };
        var value = MetricText.Format(mode == ChartMode.Breakdown ? breakdownDefinition : definition, bar.Value);
        // Colonies recorded for different lengths of time: a complete additive total also per recorded day of that colony.
        var perDay = mode == ChartMode.Colony ? MetricText.PerDay(ColonySource, bar.Value) : "";
        return new BarText(name, value, name + ": " + value + "\n" + MetricText.State(definition, bar.Value.Coverage) + perDay);
    }

    /// <summary>
    /// A colony's bar opens its history, except for a basic statistic no save holds (endings, departures, the save count),
    /// whose history would always be empty (#333).
    /// </summary>
    private bool OpensHistory => !BasicMetrics.Contains(definition.Key) || BasicMetrics.HasSaveHistory(definition.Key);

    /// <summary>The per-colony rows a colony comparison shows (a derived cross-colony record reads its source metric).</summary>
    private MetricDefinition ColonySource => colonySource ??= MetricChartSeries.ColonyComparisonKey(definition) is var key
        && key != definition.Key && MetricLookup.Definition(view, key) is { } source ? source : definition;
    private MetricDefinition? colonySource;

    private static (double Min, double Max) RangeOf(IReadOnlyList<MetricChartBar> bars)
    {
        var min = 0d;
        var max = 0d;
        foreach (var bar in bars)
        {
            if (bar.Value.Number is not { } n || !double.IsFinite(n))
                continue;
            min = Math.Min(min, n);
            max = Math.Max(max, n);
        }
        return (min, max);
    }

    private string ColonyName(string key)
        => view.Stats.Colonies.FirstOrDefault(c => c.Key == key) is { } stats
            ? Dialog_RimworldStatistics.ColonyName(stats) : key;

    private string BreakdownName(string key)
    {
        var source = CrossColonyCatalog.IsDerived(definition)
            ? MetricRegistry.Find(definition.StoredKey) ?? definition : definition;
        return MetricText.Breakdown(source, key, useThingLabel: true);
    }
}
