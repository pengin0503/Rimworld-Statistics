using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>
/// On-demand bar chart for existing numeric statistics. The charts compare saved
/// colony readings or the recorded breakdown of one aggregate; neither is a time series.
/// </summary>
internal sealed class Dialog_MetricChart : Window
{
    private const float RowHeight = 34f;
    private readonly StatsView view;
    private readonly MetricDefinition definition;
    private readonly IReadOnlyList<MetricChartBar> colonyBars;
    private readonly IReadOnlyList<MetricChartBar> breakdownBars;
    private readonly IReadOnlyList<MetricChartBar> overallBars;
    private readonly string? breakdownColony;
    private readonly string metricKey;
    private bool showBreakdown;
    private Vector2 scroll;

    internal Dialog_MetricChart(StatsView view, string metricKey, string? selectedColony)
    {
        this.view = view;
        this.metricKey = metricKey;
        definition = MetricLookup.Definition(view, metricKey)
            ?? throw new ArgumentException("Metric not found in the current view.", nameof(metricKey));
        colonyBars = MetricChartSeries.ByColony(view.Numeric, metricKey);
        IReadOnlyList<MetricChartBar> selected = selectedColony is null ? [] : MetricChartSeries.Breakdowns(view.Numeric, metricKey, selectedColony);
        breakdownColony = selected.Count > 0 ? selectedColony : null;
        breakdownBars = breakdownColony is null
            ? MetricChartSeries.Breakdowns(view.Numeric, metricKey) : selected;
        overallBars = MetricChartSeries.Overall(view.Numeric, metricKey);
        showBreakdown = breakdownColony is not null || (colonyBars.Count == 0 && breakdownBars.Count > 0);
        doCloseX = true;
        absorbInputAroundWindow = true;
        closeOnClickedOutside = false;
    }

    public override Vector2 InitialSize => new(1050f, 650f);

    public override void DoWindowContents(Rect rect)
    {
        var title = (string)"RWS_Chart_Title".Translate(MetricText.Label(definition));
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(rect.x, rect.y, rect.width - 35f, 32f), title.Truncate(rect.width - 35f));
        Text.Font = GameFont.Small;

        var canCompare = colonyBars.Count > 0;
        var canBreakdown = breakdownBars.Count > 0;
        var y = rect.y + 38f;
        if (canCompare && canBreakdown)
        {
            var compare = new Rect(rect.x, y, Math.Min(205f, rect.width * 0.42f), 29f);
            var breakdown = new Rect(compare.xMax + 8f, y, Math.Min(300f, rect.width - compare.width - 8f), 29f);
            if (Widgets.ButtonText(compare, "RWS_Chart_ByColony".Translate(), active: showBreakdown))
            {
                showBreakdown = false;
                scroll = Vector2.zero;
            }
            var categoryLabel = breakdownColony is null
                ? (string)"RWS_Chart_ByCategory".Translate()
                : (string)"RWS_Chart_ByCategoryColony".Translate(ColonyName(breakdownColony));
            if (Widgets.ButtonText(breakdown, categoryLabel.Truncate(breakdown.width - 12f), active: !showBreakdown))
            {
                showBreakdown = true;
                scroll = Vector2.zero;
            }
            TooltipHandler.TipRegion(breakdown, categoryLabel);
            y += 36f;
        }

        var bars = showBreakdown && canBreakdown ? breakdownBars : canCompare ? colonyBars
            : canBreakdown ? breakdownBars : overallBars;
        var isBreakdown = bars == breakdownBars;
        var note = isBreakdown ? (string)"RWS_Chart_BreakdownNote".Translate()
            : bars == overallBars ? (string)"RWS_Chart_OverallNote".Translate()
            : (string)"RWS_Chart_ColonyNote".Translate();
        var noteHeight = Math.Max(27f, Text.CalcHeight(note, rect.width - 6f));
        Widgets.Label(new Rect(rect.x, y, rect.width, noteHeight), note);
        y += noteHeight + 5f;
        Widgets.Label(new Rect(rect.x, y, rect.width, 25f), "RWS_Chart_Points".Translate(bars.Count));
        y += 28f;

        var area = new Rect(rect.x, y, rect.width, Math.Max(0f, rect.yMax - y));
        if (bars.Count == 0)
        {
            Widgets.Label(area, "RWS_Chart_NoData".Translate());
            return;
        }
        DrawBars(area, bars, isBreakdown, bars == overallBars);
    }

    private void DrawBars(Rect rect, IReadOnlyList<MetricChartBar> bars, bool breakdown, bool overall)
    {
        var width = Math.Max(0f, rect.width - 18f);
        var labelWidth = Math.Min(260f, width * 0.31f);
        var valueWidth = Math.Min(155f, Math.Max(90f, width * 0.19f));
        var plotWidth = Math.Max(1f, width - labelWidth - valueWidth - 16f);
        var plotX = labelWidth + 8f;
        var minimum = Math.Min(0d, bars.Min(bar => bar.Value.Number!.Value));
        var maximum = Math.Max(0d, bars.Max(bar => bar.Value.Number!.Value));
        if (maximum == minimum)
            maximum = minimum + 1d;
        var span = maximum - minimum;
        float Position(double value) => plotX + plotWidth * (float)Math.Clamp((value - minimum) / span, 0d, 1d);
        var zero = Position(0d);
        var contentHeight = bars.Count * RowHeight;

        Widgets.BeginScrollView(rect, ref scroll, new Rect(0f, 0f, width, contentHeight));
        var first = Math.Max(0, (int)(scroll.y / RowHeight));
        var last = Math.Min(bars.Count - 1, (int)((scroll.y + rect.height) / RowHeight) + 1);
        var valueDefinition = breakdown && definition.BreakdownUnit is { } unit
            ? definition with { Unit = unit } : definition;
        var anchor = Text.Anchor;
        Text.Anchor = TextAnchor.MiddleLeft;
        for (var i = first; i <= last; i++)
        {
            var bar = bars[i];
            var value = bar.Value.Number!.Value;
            var row = new Rect(0, i * RowHeight, width, RowHeight);
            if (i % 2 == 1)
                Widgets.DrawHighlight(row, 0.2f);
            Widgets.DrawHighlightIfMouseover(row);
            var name = overall ? (string)"RWS_Details_AllColonies".Translate()
                : breakdown ? BreakdownName(bar.Key) : ColonyName(bar.Key);
            var labelRect = new Rect(4f, row.y, Math.Max(0, labelWidth - 8f), RowHeight);
            Widgets.Label(labelRect, name.Truncate(labelRect.width));
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
            var text = MetricText.Format(valueDefinition, bar.Value);
            var valueRect = new Rect(plotX + plotWidth + 8f, row.y, valueWidth - 5f, RowHeight);
            Widgets.Label(valueRect, text.Truncate(valueRect.width));
            TooltipHandler.TipRegion(row, name + ": " + text + "\n" + MetricText.State(definition, bar.Value.Coverage));
        }
        Text.Anchor = anchor;
        Widgets.EndScrollView();
    }

    private string ColonyName(string key)
        => view.Stats.Colonies.FirstOrDefault(c => c.Key == key) is { } colony
            ? Dialog_RimworldStatistics.ColonyName(colony) : key;

    private string BreakdownName(string key)
    {
        var source = CrossColonyCatalog.IsDerived(definition)
            ? MetricRegistry.Find(definition.StoredKey) ?? definition : definition;
        if (MetricDisplayPolicy.BreakdownLabelKey(source, key) is { } translation)
            return translation.Translate();
        return DefDatabase<ThingDef>.GetNamedSilentFail(key)?.label ?? key;
    }
}
