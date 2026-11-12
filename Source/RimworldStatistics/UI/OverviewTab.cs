using System.Globalization;
using System.Text;
using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>Spec §4.1 on top (large), §4.2 below, with the tooltips of §4.4.</summary>
public static class OverviewTab
{
    private const float TopCellHeight = 92f;
    private const float BottomCellHeight = 70f;

    private sealed record Cell(string Label, string Value, string Tip)
    {
        private float lastWidth = -1;
        private string label = "";
        private string value = "";
        private string tip = "";

        public (string Label, string Value, string Tip) Fit(float width, GameFont font)
        {
            if (lastWidth == width)
                return (label, value, tip);

            lastWidth = width;
            Text.Font = GameFont.Small;
            label = Label.Truncate(width);
            Text.Font = font;
            value = Value.Truncate(width);
            tip = label != Label || value != Value ? Label + " — " + Value + "\n\n" + Tip : Tip;
            return (label, value, tip);
        }
    }
    private static StatsView? builtFor;
    private static object? language;
    private static Cell[] top = [];
    private static Cell[] bottom = [];
    private static Vector2 scroll;

    public static void Draw(Rect rect, StatsView view)
    {
        var currentLanguage = LanguageDatabase.activeLanguage;
        if (!ReferenceEquals(builtFor, view) || !ReferenceEquals(language, currentLanguage))
        {
            (top, bottom) = Build(view);
            builtFor = view;
            language = currentLanguage;
        }

        const float gap = 10f;
        var width = Math.Max(1, rect.width - 16f);
        var topColumns = Math.Max(1, (int)((width + gap) / (250f + gap)));
        var bottomColumns = Math.Max(1, (int)((width + gap) / (186f + gap)));
        var topHeight = ((top.Length + topColumns - 1) / topColumns) * (TopCellHeight + gap);
        var bottomHeight = ((bottom.Length + bottomColumns - 1) / bottomColumns) * (BottomCellHeight + gap);
        Widgets.BeginScrollView(rect, ref scroll, new Rect(0, 0, width, topHeight + 14f + bottomHeight));
        DrawGrid(top, topColumns, 0, TopCellHeight, GameFont.Medium);
        DrawGrid(bottom, bottomColumns, topHeight + 14f, BottomCellHeight, GameFont.Small);
        Widgets.EndScrollView();

        void DrawGrid(Cell[] cells, int columns, float y, float height, GameFont font)
        {
            var cellWidth = (width - gap * (columns - 1)) / columns;
            for (var i = 0; i < cells.Length; i++)
                DrawCell(new Rect((i % columns) * (cellWidth + gap), y + (i / columns) * (height + gap), cellWidth, height), cells[i], font);
        }
    }

    private static (Cell[] Top, Cell[] Bottom) Build(StatsView view)
    {
        var s = view.Stats;
        var has = s.HasData;
        var basis = Basis(view);
        string V(string value) => has ? value : Dialog_RimworldStatistics.Dash;
        string Tip(string? extra = null) => has ? (extra is null ? basis : extra + "\n\n" + basis) : (string)"RWS_NoData".Translate() + (extra is null ? "" : "\n\n" + extra);

        string WealthValue(double value, int colonies) => colonies == 0 ? Dialog_RimworldStatistics.Dash : StatFormat.Group(value);
        string WealthTip(int colonies) => Tip("RWS_WealthCoverage".Translate(colonies, s.ColonyCount - colonies));

        string preInstall = "RWS_EndingsTooltipPreInstall".Translate();
        var top = new Cell[]
        {
            new("RWS_Stat_Days".Translate(), V(Dialog_RimworldStatistics.DaysWithYears(s.TotalTicks)), Tip()),
            new("RWS_Stat_Endings".Translate(), V(s.TotalEndings.ToString(CultureInfo.InvariantCulture)), Tip(EndingBreakdown(s) + preInstall)),
            new("RWS_Stat_Departed".Translate(), V(s.TotalDeparted.ToString(CultureInfo.InvariantCulture)), Tip(preInstall)),
            new("RWS_Stat_PeakWealthSum".Translate(), WealthValue(s.PeakWealthSum, s.PeakWealthColonyCount), WealthTip(s.PeakWealthColonyCount)),
            new("RWS_Stat_Colonies".Translate(), V(s.ColonyCount.ToString(CultureInfo.InvariantCulture)), Tip()),
            new("RWS_Stat_RealPlay".Translate(), V("RWS_HoursValue".Translate(StatFormat.Hours(s.TotalRealPlay).ToString("N1", CultureInfo.InvariantCulture))), Tip()),
        };
        var bottom = new Cell[]
        {
            new("RWS_Stat_Raids".Translate(), V(s.Raids.ToString(CultureInfo.InvariantCulture)), Tip()),
            new("RWS_Stat_Threats".Translate(), V(s.Threats.ToString(CultureInfo.InvariantCulture)), Tip()),
            new("RWS_Stat_Killed".Translate(), V(s.Killed.ToString(CultureInfo.InvariantCulture)), Tip()),
            new("RWS_Stat_Longest".Translate(), V(Dialog_RimworldStatistics.DaysWithYears(s.LongestTicks)), Tip()),
            new("RWS_Stat_SingleMaxWealth".Translate(), WealthValue(s.SingleMaxWealth, s.PeakWealthColonyCount), WealthTip(s.PeakWealthColonyCount)),
            new("RWS_Stat_MaxPopulation".Translate(), V(s.MaxPopulation.ToString(CultureInfo.InvariantCulture)), Tip()),
            new("RWS_Stat_LastWealthSum".Translate(), WealthValue(s.LastWealthSum, s.LastWealthColonyCount), WealthTip(s.LastWealthColonyCount)),
        };

        return (top, bottom);
    }

    private static void DrawCell(Rect cell, Cell item, GameFont valueFont)
    {
        Widgets.DrawHighlight(cell, 0.35f);
        Widgets.DrawHighlightIfMouseover(cell);
        var inner = cell.ContractedBy(8f);
        var (label, value, tip) = item.Fit(inner.width, valueFont);
        Text.Font = GameFont.Small;
        GUI.color = new Color(0.8f, 0.8f, 0.8f);
        Widgets.Label(new Rect(inner.x, inner.y, inner.width, 24f), label);
        GUI.color = Color.white;
        Text.Font = valueFont;
        Widgets.Label(new Rect(inner.x, inner.y + 26f, inner.width, inner.height - 26f), value);
        Text.Font = GameFont.Small;
        TooltipHandler.TipRegion(cell, tip);
    }

    /// <summary>"Computed from N colonies (excluded M)" plus what is missing (spec §4.4).</summary>
    private static string Basis(StatsView view)
    {
        string text = "RWS_TooltipBasis".Translate(view.Stats.ColonyCount, view.Stats.ExcludedColonyCount);
        if (view.Stats.OutOfScopeColonyCount > 0)
            text += "\n" + "RWS_Groups_OutOfScope".Translate(view.Stats.OutOfScopeColonyCount);
        return view.MissingObservations > 0 ? text + "\n" + "RWS_TooltipMissing".Translate(view.MissingObservations) : text;
    }

    private static string EndingBreakdown(StatsResult s)
    {
        var sb = new StringBuilder();
        foreach (var (type, n) in s.EndingsByType.OrderBy(kv => kv.Key))
            sb.Append(Dialog_RimworldStatistics.EndingLabel(type)).Append(": ").Append(n.ToString(CultureInfo.InvariantCulture)).Append('\n');
        if (sb.Length > 0)
            sb.Append('\n');
        return sb.ToString();
    }
}
