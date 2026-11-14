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

    private enum ChartMode { Colony, Breakdown, Overall }

    private sealed record BarText(string Name, string Value, string Tip);

    /// <summary>One immutable series with its axis range; row text is built once, when the row is first shown.</summary>
    private sealed class Series(ChartMode mode, IReadOnlyList<MetricChartBar> bars)
    {
        public ChartMode Mode { get; } = mode;
        public IReadOnlyList<MetricChartBar> Bars { get; } = bars;
        public (double Min, double Max) Range { get; } = RangeOf(bars);
        public BarText?[] Text { get; } = new BarText?[bars.Count];
    }

    private readonly StatsView view;
    private readonly MetricDefinition definition;
    private readonly MetricDefinition breakdownDefinition;
    private readonly string title;
    private readonly string metricLabel;
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
            definition = BasicDefinition(metricKey);
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
        title = "RWS_Chart_Title".Translate(chartLabel);
        colony = new Series(ChartMode.Colony, colonyBars);
        breakdown = new Series(ChartMode.Breakdown, breakdownBars);
        overall = new Series(ChartMode.Overall, overallBars);
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
        Widgets.Label(new Rect(rect.x, rect.y, rect.width - 35f, 32f), title.Truncate(rect.width - 35f));
        Text.Font = GameFont.Small;

        var canCompare = colony.Bars.Count > 0;
        var canBreakdown = breakdown.Bars.Count > 0;
        var y = rect.y + 38f;
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
            _ => (string)"RWS_Chart_ColonyNote".Translate(),
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
        var bars = series.Bars;
        var width = Math.Max(0f, rect.width - 18f);
        var labelWidth = Math.Min(260f, width * 0.31f);
        var valueWidth = Math.Min(155f, Math.Max(90f, width * 0.19f));
        var plotWidth = Math.Max(1f, width - labelWidth - valueWidth - 16f);
        var plotX = labelWidth + 8f;
        var minimum = series.Range.Min;
        var span = series.Range.Max - minimum;
        float Position(double value) => plotX + plotWidth * (float)Math.Clamp((value - minimum) / span, 0d, 1d);
        var zero = Position(0d);

        Widgets.BeginScrollView(rect, ref scroll, new Rect(0f, 0f, width, bars.Count * RowHeight));
        var first = Math.Max(0, (int)(scroll.y / RowHeight));
        var last = Math.Min(bars.Count - 1, (int)((scroll.y + rect.height) / RowHeight) + 1);
        var anchor = Text.Anchor;
        MetricChartBar? selected = null;
        Text.Anchor = TextAnchor.MiddleLeft;
        for (var i = first; i <= last; i++)
        {
            var bar = bars[i];
            var text = series.Text[i] ??= Describe(series.Mode, bar);
            var value = bar.Value.Number!.Value;
            var row = new Rect(0, i * RowHeight, width, RowHeight);
            if (i % 2 == 1)
                Widgets.DrawHighlight(row, 0.2f);
            Widgets.DrawHighlightIfMouseover(row);
            if (series.Mode == ChartMode.Colony && Widgets.ButtonInvisible(row))
                selected = bar;
            var labelRect = new Rect(4f, row.y, Math.Max(0, labelWidth - 8f), RowHeight);
            Widgets.Label(labelRect, text.Name.Truncate(labelRect.width));
            var plotRow = new Rect(plotX, row.y + 5f, plotWidth, RowHeight - 10f);
            Widgets.DrawBoxSolid(new Rect(zero, plotRow.y, 1f, plotRow.height), new Color(0.67f, 0.67f, 0.67f, 0.65f));
            var end = Position(value);
            if (value != 0)
            {
                var start = Math.Min(zero, end);
                var length = Math.Max(1f, Math.Abs(end - zero));
                Widgets.DrawBoxSolid(new Rect(start, plotRow.y + 3f, length, plotRow.height - 6f),
                    bar.Value.Coverage == MetricCoverage.Partial
                        ? new Color(0.63f, 0.66f, 0.71f) : new Color(0.35f, 0.64f, 0.78f));
            }
            var valueRect = new Rect(plotX + plotWidth + 8f, row.y, valueWidth - 5f, RowHeight);
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

    private BarText Describe(ChartMode mode, MetricChartBar bar)
    {
        var name = mode switch
        {
            ChartMode.Overall => (string)"RWS_Details_AllColonies".Translate(),
            ChartMode.Breakdown => BreakdownName(bar.Key),
            _ => ColonyName(bar.Key),
        };
        var value = MetricText.Format(mode == ChartMode.Breakdown ? breakdownDefinition : definition, bar.Value);
        return new BarText(name, value, name + ": " + value + "\n" + MetricText.State(definition, bar.Value.Coverage));
    }

    private static MetricDefinition BasicDefinition(string key) => new()
    {
        Key = key, StoredKey = key, LedgerIds = key,
        Category = MetricCategory.Colony,
        Unit = key switch
        {
            BasicMetrics.Days => MetricUnit.Days,
            BasicMetrics.RealPlayHours => MetricUnit.Hours,
            BasicMetrics.PeakWealth or BasicMetrics.LastWealth => MetricUnit.Silver,
            _ => MetricUnit.Count,
        },
        Provenance = MetricProvenance.SaveHistory,
        Overall = OverallAggregation.Sum,
        LabelKey = key, DescriptionKey = key,
    };

    private static (double Min, double Max) RangeOf(IReadOnlyList<MetricChartBar> bars)
    {
        var min = 0d;
        var max = 0d;
        foreach (var bar in bars)
        {
            min = Math.Min(min, bar.Value.Number!.Value);
            max = Math.Max(max, bar.Value.Number.Value);
        }
        return (min, min == max ? min + 1d : max);
    }

    private string ColonyName(string key)
        => view.Stats.Colonies.FirstOrDefault(c => c.Key == key) is { } stats
            ? Dialog_RimworldStatistics.ColonyName(stats) : key;

    private string BreakdownName(string key)
    {
        var source = CrossColonyCatalog.IsDerived(definition)
            ? MetricRegistry.Find(definition.StoredKey) ?? definition : definition;
        if (MetricDisplayPolicy.BreakdownLabelKey(source, key) is { } translation)
            return (string)translation.Translate();
        return DefDatabase<ThingDef>.GetNamedSilentFail(key)?.label ?? key;
    }
}
