using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

internal sealed class Dialog_ColonyMetricHistory : Window
{
    private static readonly Color SeriesColor = new(0.35f, 0.64f, 0.78f);
    private static readonly Color PartialColor = new(0.63f, 0.66f, 0.71f);
    private static readonly Color GridColor = new(0.7f, 0.7f, 0.7f, 0.28f);
    private readonly MetricDefinition definition;
    private readonly string heading;
    private readonly string recordLabel;
    private readonly string colonyName;
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
        this.colonyName = colonyName;
        heading = "RWS_Chart_Title".Translate(metricLabel);
        recordLabel = "RWS_Chart_HistoryRecord".Translate(MetricText.Format(definition, comparison));
        var directory = GenFilePaths.SavedGamesFolderPath;
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
                range = (measured.Min(p => p.Value!.Number!.Value), measured.Max(p => p.Value!.Number!.Value));
            }
        }
        if (measured.Length == 0)
        {
            Widgets.Label(body, "RWS_Chart_HistoryEmpty".Translate());
            return;
        }

        var note = historyNote ??= Describe(history.Kind, measured.Length);
        var noteHeight = Text.CalcHeight(note, body.width);
        Widgets.Label(new Rect(body.x, body.y, body.width, noteHeight), note);
        var axisWidth = Math.Min(145f, body.width * 0.28f);
        var plot = new Rect(body.x + axisWidth, body.y + noteHeight + 13f,
            body.width - axisWidth - 16f, body.height - noteHeight - 57f);
        if (plot.width >= 10f && plot.height >= 10f)
            DrawPlot(plot, axisWidth, history.Points);
    }

    private static string Describe(MetricHistoryKind kind, int count)
    {
        var note = kind switch
        {
            MetricHistoryKind.WealthRecorder => (string)"RWS_Chart_HistoryWealth".Translate(),
            MetricHistoryKind.CheckpointLineage => (string)"RWS_Chart_HistoryLineage".Translate(),
            _ => (string)"RWS_Chart_HistorySaves".Translate(),
        };
        return count == 1 ? note + "\n" + (string)"RWS_Chart_HistorySingle".Translate() : note;
    }

    private void DrawPlot(Rect plot, float axisWidth, IReadOnlyList<MetricHistoryPoint> points)
    {
        var pixelWidth = Math.Max(1, (int)plot.width);
        if (plotWidth != pixelWidth)
        {
            plotPoints = PlotPoints(points, pixelWidth);
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

        Vector2? previous = null;
        var previousPartial = false;
        foreach (var point in plotPoints)
        {
            if (point.Value is not { Number: { } number } value)
            {
                previous = null;
                continue;
            }
            var position = new Vector2(X(point.Tick), Y(number));
            var partial = value.Coverage == MetricCoverage.Partial;
            if (previous is { } start && point.ConnectToPrevious)
                Widgets.DrawLine(start, position, partial || previousPartial ? PartialColor : SeriesColor, 2f);
            var size = plotPoints.Count <= 90 || previous is null || !point.ConnectToPrevious ? 5f : 2f;
            Widgets.DrawBoxSolid(new Rect(position.x - size / 2f, position.y - size / 2f, size, size), partial ? PartialColor : SeriesColor);
            previous = position;
            previousPartial = partial;
        }

        if (!Mouse.IsOver(plot))
            return;
        var mouse = Event.current.mousePosition;
        var hoverTick = ticks.First + (ticks.Last - ticks.First) * (double)Math.Clamp((mouse.x - plot.x) / plot.width, 0f, 1f);
        var nearest = Nearest(hoverTick);
        var x = X(nearest.Tick);
        if (measured!.Length > 1 && Math.Abs(x - mouse.x) > 18f)
            return;
        var reading = nearest.Value!;
        var yPoint = Y(reading.Number!.Value);
        Widgets.DrawBoxSolid(new Rect(x, plot.y, 1f, plot.height), GridColor);
        Widgets.DrawBoxSolid(new Rect(x - 3f, yPoint - 3f, 6f, 6f), Color.white);
        TooltipHandler.TipRegion(plot, (string)"RWS_Unit_Days".Translate(Dialog_RimworldStatistics.Days(nearest.Tick))
            + "\n" + MetricText.Format(definition, reading) + "\n" + MetricText.State(definition, reading.Coverage));
    }

    private MetricHistoryPoint Nearest(double tick)
    {
        var points = measured!;
        var left = 0;
        var right = points.Length - 1;
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

    private static IReadOnlyList<MetricHistoryPoint> PlotPoints(IReadOnlyList<MetricHistoryPoint> points, int width)
    {
        if (points.Count <= width * 2)
            return points;
        // Preserve gaps and extrema while reducing redraw work for long dense histories.
        var output = new List<MetricHistoryPoint>();
        var step = Math.Max(1, points.Count / width);
        for (var start = 0; start < points.Count;)
        {
            var first = points[start];
            if (first.Value is null)
            {
                output.Add(first);
                do { start++; } while (start < points.Count && points[start].Value is null);
                continue;
            }
            var min = start;
            var max = start;
            var end = start + 1;
            for (; end < Math.Min(start + step, points.Count) && points[end].ConnectToPrevious
                && points[end].Value is { } value; end++)
            {
                if (value.Number < points[min].Value!.Number)
                    min = end;
                if (value.Number > points[max].Value!.Number)
                    max = end;
            }
            foreach (var index in new[] { start, Math.Min(min, max), Math.Max(min, max), end - 1 }.Distinct())
                output.Add(points[index]);
            start = end;
        }
        return output;
    }
}
