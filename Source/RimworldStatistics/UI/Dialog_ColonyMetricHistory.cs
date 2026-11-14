using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

internal sealed class Dialog_ColonyMetricHistory : Window
{
    private static readonly Color SeriesColor = new(0.35f, 0.64f, 0.78f);
    private static readonly Color PartialColor = new(0.63f, 0.66f, 0.71f);
    private static readonly Color GridColor = new(0.7f, 0.7f, 0.7f, 0.28f);
    private static readonly Color HollowFill = new(0.12f, 0.13f, 0.15f);
    private readonly MetricDefinition definition;
    private readonly string heading;
    private readonly string recordLabel;
    private readonly string colonyName;
    private readonly StatsView view;
    private readonly string directory;
    private readonly HashSet<string> selected;
    private readonly List<Overlay> overlays = [];
    private sealed class Overlay(string name, Color color, Task<MetricHistory> task)
    {
        public string Name { get; } = name;
        public Color Color { get; } = color;
        public Task<MetricHistory> Task { get; } = task;
        public IReadOnlyList<MetricHistoryPoint> Points { get; set; } = [];
    }
    private static readonly Color[] OverlayColors = [new(0.95f, 0.64f, 0.34f), new(0.65f, 0.8f, 0.42f)];
    private readonly Task<MetricHistory> pending;
    private string? historyNote;
    private MetricHistoryPoint[]? measured;
    private (int First, int Last) ticks;
    private (double Min, double Max) range;
    private IReadOnlyList<MetricHistoryPoint> plotPoints = [];
    private int plotWidth;

    internal Dialog_ColonyMetricHistory(StatsView view, string colonyKey, string colonyName,
        MetricDefinition definition, string metricLabel, NumericValue comparison)
    {
        this.definition = definition;
        this.view = view;
        selected = new HashSet<string>(StringComparer.Ordinal) { colonyKey };
        this.colonyName = colonyName;
        heading = "RWS_Chart_Title".Translate(metricLabel);
        recordLabel = "RWS_Chart_HistoryRecord".Translate(MetricText.Format(definition, comparison));
        directory = GenFilePaths.SavedGamesFolderPath;
        pending = Task.Run(() => ProfileService.Session.ReadChartHistory(view, colonyKey, definition, directory));
        doCloseX = true;
        absorbInputAroundWindow = true;
        closeOnClickedOutside = false;
    }

    public override Vector2 InitialSize => new(Math.Min(930f, Verse.UI.screenWidth * 0.88f), Math.Min(605f, Verse.UI.screenHeight * 0.85f));

    public override void DoWindowContents(Rect rect)
    {
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(rect.x, rect.y, rect.width - 32f, 32f), heading.Truncate(rect.width - 32f));
        Text.Font = GameFont.Small;
        var colony = new Rect(rect.x, rect.y + 35f, rect.width, 26f);
        Widgets.Label(colony, colonyName.Truncate(colony.width));
        TooltipHandler.TipRegion(colony, colonyName);
        Widgets.Label(new Rect(rect.x, rect.y + 63f, rect.width, 26f), recordLabel);
        var body = new Rect(rect.x, rect.y + 94f, rect.width, Math.Max(0, rect.height - 94f));
        if (!pending.IsCompleted)
        {
            Widgets.Label(body, "RWS_Loading".Translate());
            return;
        }
        if (!pending.IsCompletedSuccessfully)
        {
            _ = pending.Exception;
            Widgets.Label(body, "RWS_WindowError".Translate());
            return;
        }

        var history = pending.Result;
        if (measured is null)
        {
            measured = history.Points.Where(p => p.Value is not null).ToArray();
            if (measured.Length > 0)
            {
                ticks = (history.Points[0].Tick, history.Points[^1].Tick);
                range = (measured.Min(p => p.Value!.Number!.Value), measured!.Max(p => p.Value!.Number!.Value));
            }
        }
        if (measured.Length == 0)
        {
            Widgets.Label(body, "RWS_Chart_HistoryEmpty".Translate());
            return;
        }

        var add = new Rect(body.x, body.y, Math.Min(250f, body.width), 28f);
        if (Widgets.ButtonText(add, "RWS_Chart_AddColony".Translate(), active: overlays.Count < OverlayColors.Length))
        {
            var options = view.Stats.Colonies.Where(c => !c.Excluded && !c.OutOfScope && !selected.Contains(c.Key))
                .OrderBy(Dialog_RimworldStatistics.ColonyName, StringComparer.CurrentCulture)
                .Select(c => new FloatMenuOption(Dialog_RimworldStatistics.ColonyName(c), () =>
                {
                    if (!selected.Add(c.Key))
                        return;
                    var name = Dialog_RimworldStatistics.ColonyName(c);
                    overlays.Add(new(name, OverlayColors[overlays.Count], Task.Run(() =>
                        ProfileService.Session.ReadChartHistory(view, c.Key, definition, directory))));
                    plotWidth = 0;
                })).ToList();
            if (options.Count > 0)
                Find.WindowStack.Add(new FloatMenu(options));
        }

        var y = add.yMax + 3f;
        foreach (var overlay in overlays)
        {
            var row = new Rect(body.x, y, body.width, 23f);
            Widgets.DrawBoxSolid(new Rect(row.x, row.y + 5f, 12f, 12f), overlay.Color);
            var status = !overlay.Task.IsCompleted ? (string)"RWS_Loading".Translate()
                : !overlay.Task.IsCompletedSuccessfully || overlay.Task.Result.Kind != history.Kind
                    || !overlay.Task.Result.Points.Any(p => p.Value is not null)
                    ? (string)"RWS_Chart_HistoryEmpty".Translate() : "";
            if (overlay.Task.IsFaulted)
                _ = overlay.Task.Exception;
            var label = status.Length == 0 ? overlay.Name : overlay.Name + " · " + status;
            Widgets.Label(new Rect(row.x + 18f, row.y, row.width - 18f, row.height), label.Truncate(row.width - 18f));
            y = row.yMax;
        }
        body.yMin = y;
        var note = historyNote ??= Describe(history.Kind, measured.Length)
            + (measured.Any(p => p.Unlinked) ? "\n" + (string)"RWS_Chart_LegendUnlinked".Translate() : "");
        var noteHeight = Text.CalcHeight(note, body.width);
        Widgets.Label(new Rect(body.x, body.y, body.width, noteHeight), note);
        var axisWidth = Math.Min(145f, body.width * 0.28f);
        var plot = new Rect(body.x + axisWidth, body.y + noteHeight + 13f,
            body.width - axisWidth - 16f, body.height - noteHeight - 57f);
        if (plot.width >= 10f && plot.height >= 10f)
            DrawPlot(plot, axisWidth, history);
    }

    private static string Describe(MetricHistoryKind kind, int count)
    {
        var note = kind switch
        {
            MetricHistoryKind.GameRecorder => (string)"RWS_Chart_GameRecorder".Translate(),
            MetricHistoryKind.WealthRecorder => (string)"RWS_Chart_HistoryWealth".Translate(),
            MetricHistoryKind.CheckpointLineage => (string)"RWS_Chart_HistoryLineage".Translate(),
            _ => (string)"RWS_Chart_HistorySaves".Translate(),
        };
        return count == 1 ? note + "\n" + (string)"RWS_Chart_HistorySingle".Translate() : note;
    }

    private void DrawPlot(Rect plot, float axisWidth, MetricHistory history)
    {
        var pixelWidth = Math.Max(1, (int)plot.width);
        var refresh = plotWidth != pixelWidth;
        foreach (var overlay in overlays)
            refresh |= overlay.Points.Count == 0 && overlay.Task.IsCompletedSuccessfully
                && overlay.Task.Result.Kind == history.Kind && overlay.Task.Result.Points.Count != 0;
        if (refresh)
        {
            plotPoints = MetricHistoryPlot.Thin(history.Points, pixelWidth);
            ticks = (history.Points[0].Tick, history.Points[^1].Tick);
            range = (measured!.Min(p => p.Value!.Number!.Value), measured!.Max(p => p.Value!.Number!.Value));
            foreach (var overlay in overlays)
            {
                if (!overlay.Task.IsCompletedSuccessfully || overlay.Task.Result.Kind != history.Kind
                    || overlay.Task.Result.Points.Count == 0)
                    continue;
                var data = overlay.Task.Result.Points;
                overlay.Points = MetricHistoryPlot.Thin(data, pixelWidth);
                ticks = (Math.Min(ticks.First, data[0].Tick), Math.Max(ticks.Last, data[^1].Tick));
                foreach (var point in data)
                    if (point.Value?.Number is { } n)
                        range = (Math.Min(range.Min, n), Math.Max(range.Max, n));
            }
            plotWidth = pixelWidth;
        }

        float X(int tick) => plot.x + plot.width * (ticks.First == ticks.Last ? 0.5f
            : (float)((tick - ticks.First) / (double)(ticks.Last - ticks.First)));
        // Scale before subtracting to keep even opposite-sign finite extremes representable.
        var scale = Math.Max(1d, Math.Max(Math.Abs(range.Min), Math.Abs(range.Max)));
        var min = range.Min / scale;
        var span = range.Max / scale - min;
        float Y(double value) => plot.yMax - plot.height * (span == 0 ? 0.5f : (float)((value / scale - min) / span));

        var anchor = Text.Anchor;
        Text.Anchor = TextAnchor.MiddleRight;
        var gridSteps = range.Min == range.Max ? 0 : 4;
        for (var i = 0; i <= gridSteps; i++)
        {
            var fraction = gridSteps == 0 ? 0.5d : i / (double)gridSteps;
            var value = range.Min * (1d - fraction) + range.Max * fraction;
            var y = plot.yMax - plot.height * (float)fraction;
            Widgets.DrawBoxSolid(new Rect(plot.x, y, plot.width, 1f), GridColor);
            var label = MetricText.Format(definition, new NumericValue { Number = value, Coverage = MetricCoverage.SnapshotOnly });
            Widgets.Label(new Rect(plot.x - axisWidth, y - 12f, axisWidth - 6f, 24f), label.Truncate(axisWidth - 6f));
        }
        Text.Anchor = TextAnchor.MiddleCenter;
        var tickSteps = ticks.First == ticks.Last ? 0 : 4;
        for (var i = 0; i <= tickSteps; i++)
        {
            var tick = tickSteps == 0 ? ticks.First : ticks.First + (int)((ticks.Last - ticks.First) * (i / (double)tickSteps));
            Widgets.Label(new Rect(X(tick) - 36f, plot.yMax + 3f, 72f, 22f), Dialog_RimworldStatistics.Days(tick));
        }
        Widgets.Label(new Rect(plot.x, plot.yMax + 23f, plot.width, 21f), "RWS_Col_Days".Translate());
        Text.Anchor = anchor;

        DrawSeries(plotPoints, SeriesColor, PartialColor);
        foreach (var overlay in overlays)
            DrawSeries(overlay.Points, overlay.Color, Color.Lerp(overlay.Color, PartialColor, 0.5f));

        void DrawSeries(IReadOnlyList<MetricHistoryPoint> points, Color color, Color partialColor)
        {
            // A line joins two readings only where the later one continues the earlier (MetricHistoryPoint);
            // a gap (no value) joins nothing, and a reading of unproven ancestry is drawn hollow.
            var positions = new Vector2?[points.Count];
            var partials = new bool[points.Count];
            for (var i = 0; i < points.Count; i++)
            {
                var point = points[i];
                if (point.Value is not { Number: { } number } value)
                    continue;
                var position = new Vector2(X(point.Tick), Y(number));
                var partial = value.Coverage == MetricCoverage.Partial;
                positions[i] = position;
                partials[i] = partial;
                var from = point.Predecessor(i);
                var joined = from >= 0 && from < i && positions[from] is not null;
                if (joined)
                    Widgets.DrawLine(positions[from]!.Value, position, partial || partials[from] ? partialColor : color, 2f);
                var marker = partial ? partialColor : color;
                if (point.Unlinked)
                {
                    Widgets.DrawBoxSolidWithOutline(new Rect(position.x - 3.5f, position.y - 3.5f, 7f, 7f), HollowFill, marker, 1);
                    continue;
                }
                var size = points.Count <= 90 || !joined ? 5f : 2f;
                Widgets.DrawBoxSolid(new Rect(position.x - size / 2f, position.y - size / 2f, size, size), marker);
            }
        }

        if (!Mouse.IsOver(plot))
            return;
        var mouse = Event.current.mousePosition;
        var hoverTick = ticks.First + (ticks.Last - ticks.First) * (double)Math.Clamp((mouse.x - plot.x) / plot.width, 0f, 1f);
        var nearest = Nearest(measured!, hoverTick);
        var hoverName = colonyName;
        IReadOnlyList<MetricHistoryPoint> hoverSeries = history.Points;
        var bestDistance = (new Vector2(X(nearest.Tick), Y(nearest.Value!.Number!.Value)) - mouse).sqrMagnitude;
        foreach (var overlay in overlays)
        {
            if (overlay.Points.Count == 0)
                continue;
            var candidate = NearestMeasured(overlay.Points, hoverTick);
            if (candidate?.Value?.Number is not { } n)
                continue;
            var distance = (new Vector2(X(candidate.Tick), Y(n)) - mouse).sqrMagnitude;
            if (distance < bestDistance)
            {
                nearest = candidate;
                bestDistance = distance;
                hoverName = overlay.Name;
                hoverSeries = overlay.Points;
            }
        }
        var x = X(nearest.Tick);
        if ((measured!.Length > 1 || overlays.Count > 0) && Math.Abs(x - mouse.x) > 18f)
            return;
        var reading = nearest.Value!;
        var yPoint = Y(reading.Number!.Value);
        Widgets.DrawBoxSolid(new Rect(x, plot.y, 1f, plot.height), GridColor);
        Widgets.DrawBoxSolid(new Rect(x - 3f, yPoint - 3f, 6f, 6f), Color.white);
        TooltipHandler.TipRegion(plot, hoverName + "\n" + (string)"RWS_Unit_Days".Translate(Dialog_RimworldStatistics.Days(nearest.Tick))
            + "\n" + MetricText.Format(definition, reading) + "\n" + MetricText.State(definition, reading.Coverage)
            + Lineage(hoverSeries, nearest));
    }

    /// <summary>What the chart knows about where a reading comes from, beyond its value.</summary>
    private static string Lineage(IReadOnlyList<MetricHistoryPoint> series, MetricHistoryPoint point)
    {
        var text = "";
        if (point.Unlinked)
            text += "\n" + (string)"RWS_Chart_PointUnlinked".Translate();
        if (point.ContinuesFrom >= 0 && FindIndex(series, point) is var index && index >= 0
            && point.Predecessor(index) is var from && from >= 0 && from < series.Count)
            text += "\n" + (string)"RWS_Chart_PointBranch".Translate(Dialog_RimworldStatistics.Days(series[from].Tick));
        if (point.SavedTick is { } saved)
            text += "\n" + (string)"RWS_Chart_PointSavedLater".Translate(Dialog_RimworldStatistics.Days(saved));
        return text;
    }

    private static int FindIndex(IReadOnlyList<MetricHistoryPoint> series, MetricHistoryPoint point)
    {
        for (var i = 0; i < series.Count; i++)
            if (ReferenceEquals(series[i], point))
                return i;
        return -1;
    }

    private static MetricHistoryPoint? NearestMeasured(IReadOnlyList<MetricHistoryPoint> points, double tick)
    {
        if (points.Count == 0)
            return null;
        var nearest = Nearest(points, tick);
        return nearest.Value is not null ? nearest : null;
    }

    private static MetricHistoryPoint Nearest(IReadOnlyList<MetricHistoryPoint> points, double tick)
    {
        var left = 0;
        var right = points.Count - 1;
        while (left < right)
        {
            var middle = (left + right) / 2;
            if (points[middle].Tick < tick)
                left = middle + 1;
            else
                right = middle;
        }
        return left > 0 && Math.Abs(points[left - 1].Tick - tick) < Math.Abs(points[left].Tick - tick)
            ? points[left - 1] : points[left];
    }
}
