using System.Globalization;
using System.Text;
using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>
/// Two statistics across the colonies of the current view, one point per colony (V2-D). A colony without both values is
/// listed by its reason and never drawn at 0; no trend line or correlation is drawn. A point opens its colony's summary
/// report or the colony's history of either statistic.
/// </summary>
internal sealed class Dialog_MetricScatterPlot : Window
{
    private const float MarkerSize = 8f;
    private const float ClusterSpacing = 7f;
    private const float HitSlop = 3f;
    private const int TipColonies = 8;
    private const int TipNamesPerReason = 12;
    private const int MenuColonies = 30;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly Color PointColor = new(0.35f, 0.68f, 0.86f);
    private static readonly Color CautionColor = new(0.92f, 0.64f, 0.34f);
    private static readonly Color HollowFill = new(0.12f, 0.13f, 0.15f);
    private static readonly Color PlotBackground = new(0f, 0f, 0f, 0.18f);
    private static readonly Color GridColor = new(1f, 1f, 1f, 0.08f);
    private static readonly Color ZeroColor = new(1f, 1f, 1f, 0.32f);
    private static readonly Color NoteColor = new(0.84f, 0.86f, 0.90f);
    private static readonly Color WarnColor = new(0.96f, 0.80f, 0.45f);

    /// <summary>Every text the window shows for one plot in one language, built once.</summary>
    private sealed record Texts(
        MetricPickerChoice[] Choices,
        string XLabel, string YLabel, string? XSource, string? YSource,
        string XButton, string YButton, string Kinds,
        string Summary, string SummaryTip, string? NotPlotted, string? NotPlottedTip,
        IReadOnlyList<(string Text, Color Color)> Notices,
        string[] XTicks, string[] YTicks);

    private readonly StatsView view;
    private readonly string? scopeName;
    private string xKey;
    private string yKey;
    private MetricScatterPlot? plot;
    private object? textLanguage;
    private Texts? texts;
    private IReadOnlyList<ScatterCluster>? clusters;
    private Vector2 clusterArea;
    private bool[] clusterCaution = [];
    private string?[] clusterTips = [];
    private Dictionary<string, string>? colonyNames;
    private string? namesSignature;

    /// <param name="yKey">The statistic on the Y axis (the peak wealth when missing or not plottable).</param>
    /// <param name="xKey">The statistic on the X axis (days played when missing, not plottable or the same as Y).</param>
    internal Dialog_MetricScatterPlot(StatsView view, string? yKey, string? xKey = null, string? scopeName = null)
    {
        this.view = view;
        this.scopeName = scopeName;
        var y = yKey is not null && MetricScatterPlot.CanPlot(view, yKey) ? yKey : BasicMetrics.PeakWealth;
        var x = xKey is not null && MetricScatterPlot.CanPlot(view, xKey) ? xKey : BasicMetrics.Days;
        if (x == y)
            x = y == BasicMetrics.Days ? BasicMetrics.PeakWealth : BasicMetrics.Days;
        this.xKey = x;
        this.yKey = y;
        doCloseX = true;
        absorbInputAroundWindow = true;
        closeOnClickedOutside = false;
        Rebuild();
    }

    public override Vector2 InitialSize => new(
        Math.Min(1060f, Verse.UI.screenWidth * 0.92f),
        Math.Min(760f, Verse.UI.screenHeight * 0.90f));

    public override void DoWindowContents(Rect rect)
    {
        try
        {
            DrawContents(rect);
        }
        finally
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }
    }

    private void Rebuild()
    {
        plot = MetricScatterPlot.Build(view, xKey, yKey);
        texts = null;
        clusters = null;
    }

    private void DrawContents(Rect rect)
    {
        var lang = LanguageDatabase.activeLanguage;
        var prefSig = ColonyPresentation.Signature(Dialog_RimworldStatistics.ActivePreferences);
        if (texts is null || !ReferenceEquals(textLanguage, lang) || namesSignature != prefSig)
        {
            textLanguage = lang;
            namesSignature = prefSig;
            colonyNames = null; // an unnamed colony's name is translated
            texts = BuildTexts();
            clusterTips = new string?[clusterTips.Length];
        }
        var t = texts;

        Text.Font = GameFont.Medium;
        var title = (string)"RWS_Scatter_Title".Translate();
        Widgets.Label(new Rect(rect.x, rect.y, Math.Max(0f, rect.width - 36f), 32f), title.Truncate(Math.Max(0f, rect.width - 36f)));
        Text.Font = GameFont.Small;

        var controls = new ControlRow(new Rect(rect.x, rect.y + 36f, rect.width, 28f));
        if (Button(ref controls, t.XButton, t.XSource is null ? t.XButton : t.XButton + "\n" + t.XSource, 170f, 360f))
            Find.WindowStack.Add(new MetricPickerDialog(t.Choices, key => SetAxes(key, yKey), t.XButton, xKey));
        if (Button(ref controls, t.YButton, t.YSource is null ? t.YButton : t.YButton + "\n" + t.YSource, 170f, 360f))
            Find.WindowStack.Add(new MetricPickerDialog(t.Choices, key => SetAxes(xKey, key), t.YButton, yKey));
        var swap = (string)"RWS_Scatter_Swap".Translate();
        if (Button(ref controls, swap, swap, 110f, 200f))
            SetAxes(yKey, xKey);

        var y = rect.y + 36f + controls.Height + 4f;
        y = Line(rect, y, t.Kinds, t.Kinds, NoteColor);
        y = Line(rect, y, t.Summary, t.SummaryTip, Color.white);
        if (t.NotPlotted is { } notPlotted)
            y = Line(rect, y, notPlotted, t.NotPlottedTip ?? notPlotted, NoteColor);
        foreach (var (notice, color) in t.Notices)
            y = Line(rect, y, notice, notice, color);

        const float legendHeight = 24f;
        var area = new Rect(rect.x, y + 4f, rect.width, Math.Max(0f, rect.yMax - y - 4f - legendHeight));
        if (plot is not null)
            DrawPlot(area, plot, t);
        DrawLegend(new Rect(rect.x, rect.yMax - legendHeight + 2f, rect.width, legendHeight - 2f));
    }

    private static bool Button(ref ControlRow controls, string label, string tip, float min, float max)
    {
        var r = controls.Next(label, min, max);
        var shown = label.Truncate(Math.Max(0f, r.width - 12f));
        if (shown != label || tip != label)
            TooltipHandler.TipRegion(r, tip);
        return Widgets.ButtonText(r, shown);
    }

    private static float Line(Rect rect, float y, string text, string tip, Color color)
    {
        var r = new Rect(rect.x, y, rect.width, 22f);
        GUI.color = color;
        var shown = text.Truncate(r.width);
        Widgets.Label(r, shown);
        GUI.color = Color.white;
        if (shown != text || tip != text)
            TooltipHandler.TipRegion(r, tip);
        return y + 22f;
    }

    private void SetAxes(string x, string y)
    {
        if (x == xKey && y == yKey)
            return;
        xKey = x;
        yKey = y;
        Rebuild();
    }

    // ----- texts -----

    private Texts BuildTexts()
    {
        var choices = LeaderboardBuilder.RankableKeys(view)
            .Where(k => MetricScatterPlot.CanPlot(view, k))
            .Select(k => new MetricPickerChoice(k, Label(k), Source(k)))
            .OrderBy(c => c.Label, StringComparer.CurrentCulture)
            .ThenBy(c => c.Key, StringComparer.Ordinal)
            .ToArray();
        var xLabel = Label(xKey);
        var yLabel = Label(yKey);
        var xButton = (string)"RWS_Scatter_X".Translate(xLabel);
        var yButton = (string)"RWS_Scatter_Y".Translate(yLabel);
        if (plot is not { } p)
        {
            var unavailable = (string)"RWS_Scatter_Unavailable".Translate();
            return new Texts(choices, xLabel, yLabel, Source(xKey), Source(yKey), xButton, yButton, "", unavailable, unavailable,
                null, null, [], [], []);
        }

        var kinds = (string)"RWS_Scatter_Kinds".Translate(KindLabel(p.X.ValueKind), KindLabel(p.Y.ValueKind));
        var inScope = p.Points.Count + p.Exclusions.Count(e => e.Reason is not (ScatterExclusionReason.Excluded or ScatterExclusionReason.OutOfScope));
        var summary = (string)"RWS_Scatter_Summary".Translate(p.Points.Count.ToString(Inv), inScope.ToString(Inv),
            scopeName ?? (string)"RWS_Groups_All".Translate());

        var parts = new List<string>();
        var tip = new StringBuilder();
        tip.Append((string)"RWS_Scatter_NotPlottedTip".Translate());
        void Part(ScatterExclusionReason reason, ScatterAxes? axes, string key)
        {
            var matched = p.Exclusions.Where(e => e.Reason == reason && (axes is null || e.Axes == axes)).ToArray();
            if (matched.Length == 0)
                return;
            var line = (string)key.Translate(matched.Length.ToString(Inv));
            parts.Add(line);
            tip.AppendLine().AppendLine().Append(line).Append(": ");
            tip.Append(string.Join(", ", matched.Take(TipNamesPerReason).Select(e => axes is null && e.Axes != ScatterAxes.None
                ? Name(e.ColonyKey) + " " + AxesTag(e.Axes) : Name(e.ColonyKey))));
            if (matched.Length > TipNamesPerReason)
                tip.Append(' ').Append((string)"RWS_Scatter_More".Translate((matched.Length - TipNamesPerReason).ToString(Inv)));
        }
        Part(ScatterExclusionReason.Missing, ScatterAxes.X, "RWS_Scatter_Reason_MissingX");
        Part(ScatterExclusionReason.Missing, ScatterAxes.Y, "RWS_Scatter_Reason_MissingY");
        Part(ScatterExclusionReason.Missing, ScatterAxes.Both, "RWS_Scatter_Reason_MissingBoth");
        Part(ScatterExclusionReason.NotApplicable, null, "RWS_Scatter_Reason_NotApplicable");
        Part(ScatterExclusionReason.NotReached, null, "RWS_Scatter_Reason_NotReached");
        Part(ScatterExclusionReason.NoSingleValue, null, "RWS_Scatter_Reason_NoSingleValue");
        Part(ScatterExclusionReason.Excluded, null, "RWS_Scatter_Reason_Excluded");
        Part(ScatterExclusionReason.OutOfScope, null, "RWS_Scatter_Reason_OutOfScope");
        var notPlotted = parts.Count == 0 ? null : (string)"RWS_Scatter_NotPlotted".Translate(string.Join(" · ", parts));

        var notices = new List<(string, Color)>();
        if (p.Points.Count == 0)
            notices.Add(("RWS_Scatter_NoPoints".Translate(), WarnColor));
        else if (p.TooFewPoints)
            notices.Add(("RWS_Scatter_TooFew".Translate(p.Points.Count.ToString(Inv)), WarnColor));
        if (p.CautionCount > 0)
            notices.Add(("RWS_Scatter_Caution".Translate(p.CautionCount.ToString(Inv)), NoteColor));

        return new Texts(choices, xLabel, yLabel, Source(xKey), Source(yKey), xButton, yButton, kinds,
            summary, "RWS_Scatter_SummaryTip".Translate(), notPlotted, notPlotted is null ? null : tip.ToString(),
            notices, TickLabels(p.X), TickLabels(p.Y));
    }

    private string Label(string key)
        => BasicMetrics.Contains(key) ? MetricText.BasicLabel(key)
            : MetricLookup.Definition(view, key) is { } d ? MetricText.Label(d) : key;

    private string? Source(string key)
        => BasicMetrics.Contains(key) ? null : MetricLookup.Definition(view, key) is { } d ? MetricText.ModSourceLabel(d) : null;

    private string Name(string colonyKey)
    {
        if (colonyNames is null)
        {
            colonyNames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var stats in view.Stats.Colonies)
                colonyNames.TryAdd(stats.Key, Dialog_RimworldStatistics.ColonyName(stats));
        }
        return colonyNames.TryGetValue(colonyKey, out var name) ? name : colonyKey;
    }

    private static string AxesTag(ScatterAxes axes) => axes switch
    {
        ScatterAxes.X => "RWS_Scatter_OnX".Translate(),
        ScatterAxes.Y => "RWS_Scatter_OnY".Translate(),
        _ => "RWS_Scatter_OnBoth".Translate(),
    };

    private static string KindLabel(ColonyReportValueKind kind) => kind switch
    {
        ColonyReportValueKind.CumulativeTotal => "RWS_Report_Kind_Cumulative".Translate(),
        ColonyReportValueKind.PeakOrExtreme => "RWS_Report_Kind_Peak".Translate(),
        ColonyReportValueKind.LatestSnapshot => "RWS_Report_Kind_Snapshot".Translate(),
        _ => "RWS_Report_Kind_MeanRate".Translate(),
    };

    private static string[] TickLabels(ScatterAxis axis)
    {
        var percent = axis.Definition.Unit == MetricUnit.Percent;
        var suffix = axis.Definition.Unit == MetricUnit.Mood ? "%" : "";
        return axis.Ticks.Select(v => MetricChartTicks.FormatAxisValue(v, percent) + suffix).ToArray();
    }

    /// <summary>The value and its recording state, as the rankings' value and state columns show them.</summary>
    private static string FormatValue(ScatterAxis axis, ScatterValue value)
    {
        if (axis.IsBasic)
            return RankingsTab.BasicValue(axis.MetricKey, value.Number) + " · " + (string)"RWS_Coverage_SnapshotOnly".Translate();
        var text = MetricText.Format(axis.Definition, new NumericValue
        {
            Number = value.Number, Coverage = value.Coverage, Evidence = value.Evidence, ThroughTick = value.ThroughTick,
        });
        return text + " · " + MetricText.State(axis.Definition, value.Coverage);
    }

    // ----- plot -----

    private void DrawPlot(Rect area, MetricScatterPlot p, Texts t)
    {
        Text.Font = GameFont.Tiny;
        var yLabelWidth = 36f;
        foreach (var label in t.YTicks)
            yLabelWidth = Math.Max(yLabelWidth, Text.CalcSize(label).x + 8f);
        yLabelWidth = Math.Min(yLabelWidth, 120f);
        Text.Font = GameFont.Small;

        var plotRect = new Rect(area.x + yLabelWidth, area.y + 24f,
            Math.Max(0f, area.width - yLabelWidth - 16f), Math.Max(0f, area.height - 24f - 44f));
        if (plotRect.width < 60f || plotRect.height < 60f)
            return;

        // Axis names: Y above the plot, X below its tick labels.
        var yName = (string)"RWS_Scatter_Y".Translate(t.YLabel);
        var yNameRect = new Rect(area.x, area.y, area.width, 22f);
        Widgets.Label(yNameRect, yName.Truncate(yNameRect.width));
        var anchor = Text.Anchor;
        Text.Anchor = TextAnchor.UpperCenter;
        var xName = (string)"RWS_Scatter_X".Translate(t.XLabel);
        Widgets.Label(new Rect(plotRect.x, plotRect.yMax + 20f, plotRect.width, 22f), xName.Truncate(plotRect.width));

        Widgets.DrawBoxSolid(plotRect, PlotBackground);
        Text.Font = GameFont.Tiny;
        for (var i = 0; i < p.Y.Ticks.Count; i++)
        {
            var value = p.Y.Ticks[i];
            var py = plotRect.yMax - (float)p.Y.Position(value) * plotRect.height;
            Widgets.DrawBoxSolid(new Rect(plotRect.x, py, plotRect.width, 1f), value == 0d ? ZeroColor : GridColor);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(area.x, py - 9f, yLabelWidth - 5f, 18f), t.YTicks[i].Truncate(yLabelWidth - 5f));
        }
        var xLabelWidth = Math.Max(30f, plotRect.width / Math.Max(1, p.X.Ticks.Count) - 4f);
        for (var i = 0; i < p.X.Ticks.Count; i++)
        {
            var value = p.X.Ticks[i];
            var px = plotRect.x + (float)p.X.Position(value) * plotRect.width;
            Widgets.DrawBoxSolid(new Rect(px, plotRect.y, 1f, plotRect.height), value == 0d ? ZeroColor : GridColor);
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(px - xLabelWidth / 2f, plotRect.yMax + 2f, xLabelWidth, 18f), t.XTicks[i].Truncate(xLabelWidth));
        }
        Text.Anchor = anchor;

        if (clusters is null || clusterArea != plotRect.size)
        {
            clusters = p.Cluster(plotRect.width, plotRect.height, ClusterSpacing);
            clusterArea = plotRect.size;
            clusterCaution = clusters.Select(c => c.PointIndices.Any(i => p.Points[i].NeedsCaution)).ToArray();
            clusterTips = new string?[clusters.Count];
        }

        var mouse = Event.current.mousePosition;
        var reach = MarkerSize / 2f + HitSlop;
        var hovered = -1;
        var best = float.MaxValue;
        var mouseNear = Mouse.IsOver(plotRect.ExpandedBy(reach));
        for (var i = 0; i < clusters.Count; i++)
        {
            var c = clusters[i];
            var center = new Vector2(plotRect.x + (float)c.X, plotRect.yMax - (float)c.Y);
            var marker = new Rect(center.x - MarkerSize / 2f, center.y - MarkerSize / 2f, MarkerSize, MarkerSize);
            if (clusterCaution[i])
                Widgets.DrawBoxSolidWithOutline(marker, HollowFill, CautionColor, 1);
            else
                Widgets.DrawBoxSolid(marker, PointColor);
            if (c.PointIndices.Count > 1)
                Widgets.Label(new Rect(marker.xMax + 1f, marker.y - 11f, 48f, 16f), c.PointIndices.Count.ToString(Inv));

            if (!mouseNear)
                continue;
            var dx = Math.Abs(mouse.x - center.x);
            var dy = Math.Abs(mouse.y - center.y);
            if (dx <= reach && dy <= reach && dx + dy < best)
            {
                best = dx + dy;
                hovered = i;
            }
        }
        Text.Font = GameFont.Small;

        if (hovered < 0)
            return;
        var h = clusters[hovered];
        var hit = new Rect(plotRect.x + (float)h.X - reach, plotRect.yMax - (float)h.Y - reach, reach * 2f, reach * 2f);
        Widgets.DrawBox(hit.ContractedBy(HitSlop - 2f), 1);
        TooltipHandler.TipRegion(hit, clusterTips[hovered] ??= ClusterTip(p, t, h));
        if (Widgets.ButtonInvisible(hit))
            OpenMenu(p, t, h);
    }

    private string ClusterTip(MetricScatterPlot p, Texts t, ScatterCluster cluster)
    {
        var sb = new StringBuilder();
        foreach (var index in cluster.PointIndices.Take(TipColonies))
        {
            var point = p.Points[index];
            if (sb.Length > 0)
                sb.AppendLine().AppendLine();
            sb.Append(Name(point.ColonyKey));
            sb.AppendLine().Append((string)"RWS_Scatter_X".Translate(FormatValue(p.X, point.X)));
            sb.AppendLine().Append((string)"RWS_Scatter_Y".Translate(FormatValue(p.Y, point.Y)));
            if (point.ObservedAtDifferentTicks)
                sb.AppendLine().Append((string)"RWS_Scatter_TipDifferentDays".Translate(
                    Dialog_RimworldStatistics.Days(point.X.ThroughTick!.Value), Dialog_RimworldStatistics.Days(point.Y.ThroughTick!.Value)));
        }
        if (cluster.PointIndices.Count > TipColonies)
            sb.AppendLine().AppendLine().Append((string)"RWS_Scatter_More".Translate((cluster.PointIndices.Count - TipColonies).ToString(Inv)));
        sb.AppendLine().AppendLine().Append((string)"RWS_Scatter_TipClick".Translate());
        return sb.ToString();
    }

    private void OpenMenu(MetricScatterPlot p, Texts t, ScatterCluster cluster)
    {
        if (cluster.PointIndices.Count == 1)
        {
            Find.WindowStack.Add(new FloatMenu(ColonyOptions(p, t, p.Points[cluster.PointIndices[0]])));
            return;
        }
        var points = cluster.PointIndices.Select(i => p.Points[i])
            .OrderBy(pt => Name(pt.ColonyKey), StringComparer.CurrentCulture)
            .ThenBy(pt => pt.ColonyKey, StringComparer.Ordinal)
            .ToArray();
        var options = points.Take(MenuColonies)
            .Select(pt => new FloatMenuOption(Name(pt.ColonyKey), () => Find.WindowStack.Add(new FloatMenu(ColonyOptions(p, t, pt)))))
            .ToList();
        if (points.Length > MenuColonies)
            options.Add(new FloatMenuOption("RWS_Scatter_More".Translate((points.Length - MenuColonies).ToString(Inv)), null));
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private List<FloatMenuOption> ColonyOptions(MetricScatterPlot p, Texts t, ScatterPoint point)
    {
        var key = point.ColonyKey;
        var options = new List<FloatMenuOption>
        {
            new("RWS_Report_Open".Translate(), () => Find.WindowStack.Add(new Dialog_ColonySummaryReport(view, key, scopeName))),
        };
        void History(ScatterAxis axis, string label, ScatterValue value)
        {
            if (axis.IsBasic && !BasicMetrics.HasSaveHistory(axis.MetricKey))
                return;
            var comparison = new NumericValue
            {
                Number = value.Number, Coverage = value.Coverage, Evidence = value.Evidence, ThroughTick = value.ThroughTick,
            };
            options.Add(new FloatMenuOption("RWS_Scatter_History".Translate(label), () => Find.WindowStack.Add(
                new Dialog_ColonyMetricHistory(view, key, Name(key), axis.Definition, label, comparison))));
        }
        History(p.X, t.XLabel, point.X);
        if (p.Y.MetricKey != p.X.MetricKey)
            History(p.Y, t.YLabel, point.Y);
        return options;
    }

    private static void DrawLegend(Rect rect)
    {
        var x = rect.x;
        void Item(string label, Action<Rect> swatch)
        {
            var width = Math.Min(rect.xMax - x, 16f + Text.CalcSize(label).x);
            if (width <= 16f)
                return;
            swatch(new Rect(x, rect.y + 6f, 9f, 9f));
            var labelRect = new Rect(x + 14f, rect.y, width - 14f, rect.height);
            var shown = label.Truncate(labelRect.width);
            Widgets.Label(labelRect, shown);
            if (shown != label)
                TooltipHandler.TipRegion(labelRect, label);
            x += width + 18f;
        }
        Item("RWS_Scatter_LegendPoint".Translate(), r => Widgets.DrawBoxSolid(r, PointColor));
        Item("RWS_Scatter_LegendCaution".Translate(), r => Widgets.DrawBoxSolidWithOutline(r, HollowFill, CautionColor, 1));
        Item("RWS_Scatter_LegendStack".Translate(), r => Widgets.DrawBoxSolid(r, PointColor));
    }
}
