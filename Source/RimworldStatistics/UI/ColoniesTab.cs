using System.Globalization;
using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>One row per colony with sortable columns, an exclude checkbox, and the source saves of the selected row (spec §9.2).</summary>
public static class ColoniesTab
{
    private const float RowHeight = 28f;
    private const float DetailHeight = 190f;
    // Additional per-colony fields must remain readable in smaller windows.
    private const float MinTableWidth = 1260f;

    private sealed record Column(string LabelKey, float Width, Func<ColonyStats, IComparable> Sort, Func<ColonyStats, string> Text);

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly Column[] Columns =
    {
        new("RWS_Col_Name", 0.15f, s => Dialog_RimworldStatistics.ColonyName(s), Dialog_RimworldStatistics.ColonyName),
        new("RWS_Col_World", 0.10f, s => s.WorldName ?? "", s => s.WorldName ?? Dialog_RimworldStatistics.Dash),
        new("RWS_Col_Scenario", 0.13f, s => (s.Scenario ?? "") + "/" + (s.Storyteller ?? ""), s => $"{s.Scenario ?? Dialog_RimworldStatistics.Dash} / {s.Storyteller ?? Dialog_RimworldStatistics.Dash}"),
        new("RWS_Col_Days", 0.06f, s => s.MaxTicks, s => Dialog_RimworldStatistics.Days(s.MaxTicks)),
        new("RWS_Col_PeakWealth", 0.08f, s => s.WealthMax, s => s.PeakWealthKnown ? StatFormat.Group(s.WealthMax) : Dialog_RimworldStatistics.Dash),
        new("RWS_Col_LastWealth", 0.08f, s => s.WealthLast, s => s.LastWealthKnown ? StatFormat.Group(s.WealthLast) : Dialog_RimworldStatistics.Dash),
        new("RWS_Col_Raids", 0.05f, s => s.Raids, s => s.Raids.ToString(Inv)),
        new("RWS_Col_Killed", 0.05f, s => s.Killed, s => s.Killed.ToString(Inv)),
        new("RWS_Stat_Threats", 0.06f, s => s.Threats, s => s.Threats.ToString(Inv)),
        new("RWS_Stat_Departed", 0.06f, s => s.Departed, s => s.Departed.ToString(Inv)),
        new("RWS_Stat_MaxPopulation", 0.07f, s => s.MaxPopulation, s => s.MaxPopulation.ToString(Inv)),
        new("RWS_Stat_RealPlay", 0.08f, s => s.MaxRealPlay,
            s => "RWS_HoursValue".Translate(StatFormat.Hours(s.MaxRealPlay).ToString("N1", Inv))),
        new("RWS_Col_Endings", 0.05f, s => s.EndingCounts.Values.Sum(), s => s.EndingCounts.Values.Sum().ToString(Inv)),
        new("RWS_Col_Versions", 0.07f, s => string.Join(",", s.GameVersions), s => Dialog_RimworldStatistics.ShortVersions(s.GameVersions)),
        new("RWS_Col_Saves", 0.05f, s => s.SaveCount, s => s.SaveCount.ToString(Inv)),
        new("RWS_Col_LastPlayed", 0.08f, s => s.LastPlayedUtc ?? DateTime.MinValue, s => Dialog_RimworldStatistics.Local(s.LastPlayedUtc)),
    };

    private const float ExcludeWidth = 0.05f;
    private static readonly float TotalColumnWeight = Columns.Sum(c => c.Width) + ExcludeWidth;

    private static int sortColumn = 3;
    private static bool ascending;
    private static string? selectedKey;
    private static Vector2 scroll;
    private static Vector2 detailScroll;

    private sealed class TableCache(StatsView view, int sortColumn, bool ascending, int[] order)
    {
        public StatsView View { get; } = view;
        public int SortColumn { get; } = sortColumn;
        public bool Ascending { get; } = ascending;
        public int[] Order { get; } = order;
        public string[]?[] Cells { get; } = new string[order.Length][];
        public float[]?[] Widths { get; } = new float[order.Length][];
    }

    private static TableCache? cache;

    public static void Draw(Rect rect, StatsView view, SaveFileIndex? files)
    {
        if (cache is null || !ReferenceEquals(cache.View, view) || cache.SortColumn != sortColumn || cache.Ascending != ascending)
        {
            var indices = Enumerable.Range(0, view.Colonies.Count);
            var key = Columns[sortColumn].Sort;
            var sorted = (ascending ? indices.OrderBy(i => key(view.Stats.Colonies[i])) : indices.OrderByDescending(i => key(view.Stats.Colonies[i]))).ToArray();
            cache = new TableCache(view, sortColumn, ascending, sorted);
        }
        var table = cache;
        var order = table.Order;
        if (order.Length == 0)
        {
            Widgets.Label(rect, "RWS_NoData".Translate());
            return;
        }

        var selectedRow = Array.FindIndex(order, i => view.Colonies[i].Key == selectedKey);
        var selected = selectedRow < 0 ? -1 : order[selectedRow];
        var tableHeight = rect.height - (selected >= 0 ? DetailHeight + 8f : 0f);
        var width = Math.Max(rect.width - 16f, MinTableWidth);
        // Keep the sortable header fixed vertically while tracking horizontal body scroll.
        GUI.BeginGroup(new Rect(rect.x, rect.y, Math.Max(0f, rect.width - 16f), RowHeight));
        DrawHeader(new Rect(-scroll.x, 0f, width, RowHeight));
        GUI.EndGroup();

        var outRect = new Rect(rect.x, rect.y + RowHeight, rect.width, tableHeight - RowHeight);
        var viewRect = new Rect(0f, 0f, width, order.Length * RowHeight);
        Widgets.BeginScrollView(outRect, ref scroll, viewRect);
        var first = Math.Max(0, (int)(scroll.y / RowHeight));
        var last = Math.Min(order.Length - 1, (int)((scroll.y + outRect.height) / RowHeight) + 1);
        for (var row = first; row <= last; row++)
            DrawColony(new Rect(0f, row * RowHeight, width, RowHeight), view.Colonies[order[row]], view.Stats.Colonies[order[row]], row, table);
        Widgets.EndScrollView();

        if (selected >= 0)
            DrawDetail(new Rect(rect.x, rect.y + tableHeight + 8f, rect.width, DetailHeight), view.Colonies[selected], files);
    }

    private static void DrawHeader(Rect rect)
    {
        var x = rect.x;
        Text.Anchor = TextAnchor.MiddleLeft;
        GUI.color = new Color(0.8f, 0.8f, 0.8f);
        for (var c = 0; c < Columns.Length; c++)
        {
            var cell = new Rect(x, rect.y, rect.width * Columns[c].Width / TotalColumnWeight, rect.height);
            var label = (string)Columns[c].LabelKey.Translate() + (sortColumn == c ? (ascending ? " ▲" : " ▼") : "");
            Widgets.Label(new Rect(cell.x + 3f, cell.y, cell.width - 6f, cell.height), label);
            Widgets.DrawHighlightIfMouseover(cell);
            TooltipHandler.TipRegion(cell, label);
            if (Widgets.ButtonInvisible(cell))
            {
                if (sortColumn == c) ascending = !ascending;
                else { sortColumn = c; ascending = false; }
            }
            x += cell.width;
        }
        var excludeWidth = rect.width * ExcludeWidth / TotalColumnWeight;
        Widgets.Label(new Rect(x + 3f, rect.y, excludeWidth - 6f, rect.height), "RWS_Exclude".Translate());
        TooltipHandler.TipRegion(new Rect(x, rect.y, excludeWidth, rect.height), "RWS_ExcludeTip".Translate());
        GUI.color = Color.white;
        Text.Anchor = TextAnchor.UpperLeft;
    }

    private static void DrawColony(Rect rect, Colony colony, ColonyStats s, int row, TableCache table)
    {
        if (colony.Key == selectedKey)
            Widgets.DrawHighlightSelected(rect);
        else if (row % 2 == 1)
            Widgets.DrawHighlight(rect, 0.25f);
        Widgets.DrawHighlightIfMouseover(rect);
        if (s.Excluded)
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
        var x = rect.x;
        Text.Anchor = TextAnchor.MiddleLeft;
        if (table.Cells[row] is not { } texts)
        {
            texts = Columns.Select(c => c.Text(s)).ToArray();
            table.Widths[row] = texts.Select(text => Text.CalcSize(text).x).ToArray();
            table.Cells[row] = texts;
        }
        var widths = table.Widths[row]!;
        for (var c = 0; c < Columns.Length; c++)
        {
            var col = Columns[c];
            var cell = new Rect(x + 3f, rect.y, rect.width * col.Width / TotalColumnWeight - 6f, rect.height);
            var text = texts[c];
            Widgets.Label(cell, text);
            if (widths[c] > cell.width)
                TooltipHandler.TipRegion(cell, text);
            x += rect.width * col.Width / TotalColumnWeight;
        }
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;

        var excluded = s.Excluded;
        var box = new Rect(x + 4f, rect.y + (rect.height - 24f) / 2f, 24f, 24f);
        Widgets.Checkbox(box.x, box.y, ref excluded);
        if (excluded != s.Excluded)
        {
            var keys = colony.AliasKeys.Prepend(colony.Key).ToList();
            ProfileService.Session.Mutate(p => ProfileMutations.SetExcluded(p, keys, excluded), SaveMode.Background);
        }
        else if (Widgets.ButtonInvisible(new Rect(rect.x, rect.y, x - rect.x, rect.height)))
        {
            selectedKey = selectedKey == colony.Key ? null : colony.Key;
        }
    }

    private static void DrawDetail(Rect rect, Colony colony, SaveFileIndex? files)
    {
        Widgets.DrawMenuSection(rect);
        var inner = rect.ContractedBy(6f);
        var widths = new[] { 0.46f, 0.18f, 0.10f, 0.13f, 0.13f };
        var headers = new string[]
        {
            "RWS_Col_File".Translate(), "RWS_Col_Version".Translate(), "RWS_Col_Days".Translate(),
            "RWS_Col_Superseded".Translate(), "RWS_Col_FilePresent".Translate(),
        };
        var lines = colony.Observations
            .OrderByDescending(o => o.Values.TicksGame)
            .Select(o => new[]
            {
                o.FileName, o.GameVersion ?? Dialog_RimworldStatistics.Dash, Dialog_RimworldStatistics.Days(o.Values.TicksGame),
                o.Superseded ? (string)"RWS_Yes".Translate() : (string)"RWS_No".Translate(),
                files == null ? Dialog_RimworldStatistics.Dash : files.Contains(o) ? (string)"RWS_Yes".Translate() : (string)"RWS_FileMissing".Translate(),
            })
            .Concat(colony.LiveCampaigns.Select(l => new[]
            {
                (string)"RWS_LiveRecord".Translate(), Dialog_RimworldStatistics.Dash, Dialog_RimworldStatistics.Days(l.Max.TicksGame),
                Dialog_RimworldStatistics.Dash, Dialog_RimworldStatistics.Dash,
            }))
            .ToList();

        Row(new Rect(inner.x, inner.y, inner.width - 16f, 24f), widths, headers, header: true);
        var outRect = new Rect(inner.x, inner.y + 26f, inner.width, inner.height - 26f);
        var viewRect = new Rect(0f, 0f, outRect.width - 16f, lines.Count * 24f);
        Widgets.BeginScrollView(outRect, ref detailScroll, viewRect);
        for (var i = 0; i < lines.Count; i++)
            Row(new Rect(0f, i * 24f, viewRect.width, 24f), widths, lines[i], header: false);
        Widgets.EndScrollView();
    }

    private static void Row(Rect rect, float[] widths, string[] cells, bool header)
    {
        var x = rect.x;
        Text.Anchor = TextAnchor.MiddleLeft;
        if (header)
            GUI.color = new Color(0.8f, 0.8f, 0.8f);
        for (var c = 0; c < cells.Length; c++)
        {
            var w = rect.width * widths[c];
            var cell = new Rect(x + 3f, rect.y, w - 6f, rect.height);
            Widgets.Label(cell, cells[c]);
            if (Text.CalcSize(cells[c]).x > cell.width)
                TooltipHandler.TipRegion(cell, cells[c]);
            x += w;
        }
        GUI.color = Color.white;
        Text.Anchor = TextAnchor.UpperLeft;
    }
}
