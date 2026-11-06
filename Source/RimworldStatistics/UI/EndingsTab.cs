using System.Globalization;
using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>
/// Every ending of every colony (spec §9.2): one row per (type, ordinal) as counted in §6.2, plus save traces that no
/// recorded ending explains. Dev-mode-only endings are listed only when the setting is on (spec §4.4).
/// </summary>
public static class EndingsTab
{
    private const float RowHeight = 26f;

    private sealed record Row(DateTime? Time, string Colony, EndingType Type, string Ordinal, string Departed, string Source);

    private static StatsView? builtFor;
    private static List<Row> rows = new();
    private static Vector2 scroll;

    public static void Draw(Rect rect, StatsView view)
    {
        var includeDev = ProfileService.Options.IncludeDevMode;
        if (!ReferenceEquals(builtFor, view))
        {
            builtFor = view;
            rows = Build(view, includeDev);
        }
        if (rows.Count == 0)
        {
            Widgets.Label(rect, "RWS_Endings_None".Translate() + "\n\n" + "RWS_EndingsTooltipPreInstall".Translate());
            return;
        }

        var widths = new[] { 0.16f, 0.26f, 0.22f, 0.09f, 0.09f, 0.18f };
        var headers = new string[]
        {
            "RWS_Col_Date".Translate(), "RWS_Col_Colony".Translate(), "RWS_Col_EndingType".Translate(),
            "RWS_Col_Ordinal".Translate(), "RWS_Col_Departed".Translate(), "RWS_Col_Source".Translate(),
        };
        DrawRow(new Rect(rect.x, rect.y, rect.width - 16f, RowHeight), widths, headers, header: true);
        var outRect = new Rect(rect.x, rect.y + RowHeight + 4f, rect.width, rect.height - RowHeight - 4f);
        var viewRect = new Rect(0f, 0f, outRect.width - 16f, rows.Count * RowHeight);
        Widgets.BeginScrollView(outRect, ref scroll, viewRect);
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var rowRect = new Rect(0f, i * RowHeight, viewRect.width, RowHeight);
            if (i % 2 == 1)
                Widgets.DrawHighlight(rowRect, 0.25f);
            DrawRow(rowRect, widths, new[] { Dialog_RimworldStatistics.Local(r.Time), r.Colony, Dialog_RimworldStatistics.EndingLabel(r.Type), r.Ordinal, r.Departed, r.Source }, header: false);
        }
        Widgets.EndScrollView();
    }

    private static void DrawRow(Rect rect, float[] widths, string[] cells, bool header)
    {
        var x = rect.x;
        Text.Anchor = TextAnchor.MiddleLeft;
        if (header)
            GUI.color = new Color(0.8f, 0.8f, 0.8f);
        for (var c = 0; c < cells.Length; c++)
        {
            var w = rect.width * widths[c];
            var cell = new Rect(x + 4f, rect.y, w - 8f, rect.height);
            Widgets.Label(cell, cells[c]);
            if (Text.CalcSize(cells[c]).x > cell.width)
                TooltipHandler.TipRegion(cell, cells[c]);
            x += w;
        }
        GUI.color = Color.white;
        Text.Anchor = TextAnchor.UpperLeft;
    }

    private static List<Row> Build(StatsView view, bool includeDev)
    {
        var list = new List<Row>();
        for (var i = 0; i < view.Colonies.Count; i++)
        {
            var colony = view.Colonies[i];
            var stats = view.Stats.Colonies[i];
            var name = Dialog_RimworldStatistics.ColonyName(stats) + (stats.Excluded ? (string)"RWS_ExcludedMark".Translate() : "");
            var shownTypes = new HashSet<EndingType>();
            foreach (var pair in colony.Endings.GroupBy(e => (e.Type, e.Ordinal)))
            {
                var visible = pair.Where(e => includeDev || !e.DevMode).ToList();
                if (visible.Count == 0)
                    continue;   // dev-mode-only and the setting is off
                shownTypes.Add(pair.Key.Type);
                var best = visible.OrderByDescending(e => e.Departed).ThenBy(e => e.DevMode).First();
                var source = best.Source == EndingSource.SeededTrace ? "RWS_Source_SeededTrace".Translate() : "RWS_Source_Live".Translate();
                if (best.DevMode)
                    source += "RWS_DevModeMark".Translate();
                list.Add(new Row(visible.Min(e => e.RealTimeUtc), name, pair.Key.Type, (pair.Key.Ordinal + 1).ToString(CultureInfo.InvariantCulture),
                    best.Source == EndingSource.SeededTrace ? Dialog_RimworldStatistics.Dash : best.Departed.ToString(CultureInfo.InvariantCulture), source));
            }
            foreach (var trace in colony.Traces.Where(t => !shownTypes.Contains(t)).OrderBy(t => t))
            {
                var first = colony.Observations.Where(o => o.EndingTraces.Contains(trace)).Select(o => (DateTime?)o.LastWriteUtc).DefaultIfEmpty(null).Min();
                list.Add(new Row(first, name, trace, "RWS_AtLeastOnce".Translate(), Dialog_RimworldStatistics.Dash, "RWS_Source_SaveTrace".Translate()));
            }
        }
        return list.OrderByDescending(r => r.Time ?? DateTime.MinValue).ToList();
    }
}
