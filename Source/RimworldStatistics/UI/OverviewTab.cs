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

    public static void Draw(Rect rect, StatsView view)
    {
        var s = view.Stats;
        var has = s.HasData;
        var basis = Basis(view);
        string V(string value) => has ? value : Dialog_RimworldStatistics.Dash;
        string Tip(string? extra = null) => has ? (extra is null ? basis : extra + "\n\n" + basis) : (string)"RWS_NoData".Translate() + (extra is null ? "" : "\n\n" + extra);

        string WealthValue(double value, int colonies) => colonies == 0 ? Dialog_RimworldStatistics.Dash : StatFormat.Group(value);
        string WealthTip(int colonies) => Tip("RWS_WealthCoverage".Translate(colonies, s.ColonyCount - colonies));

        string preInstall = "RWS_EndingsTooltipPreInstall".Translate();
        var top = new (string Label, string Value, string Tip)[]
        {
            ("RWS_Stat_Days".Translate(), V(Dialog_RimworldStatistics.DaysWithYears(s.TotalTicks)), Tip()),
            ("RWS_Stat_Endings".Translate(), V(s.TotalEndings.ToString(CultureInfo.InvariantCulture)), Tip(EndingBreakdown(s) + preInstall)),
            ("RWS_Stat_Departed".Translate(), V(s.TotalDeparted.ToString(CultureInfo.InvariantCulture)), Tip(preInstall)),
            ("RWS_Stat_PeakWealthSum".Translate(), WealthValue(s.PeakWealthSum, s.PeakWealthColonyCount), WealthTip(s.PeakWealthColonyCount)),
            ("RWS_Stat_Colonies".Translate(), V(s.ColonyCount.ToString(CultureInfo.InvariantCulture)), Tip()),
            ("RWS_Stat_RealPlay".Translate(), V("RWS_HoursValue".Translate(StatFormat.Hours(s.TotalRealPlay).ToString("N1", CultureInfo.InvariantCulture))), Tip()),
        };
        var bottom = new (string Label, string Value, string Tip)[]
        {
            ("RWS_Stat_Raids".Translate(), V(s.Raids.ToString(CultureInfo.InvariantCulture)), Tip()),
            ("RWS_Stat_Threats".Translate(), V(s.Threats.ToString(CultureInfo.InvariantCulture)), Tip()),
            ("RWS_Stat_Killed".Translate(), V(s.Killed.ToString(CultureInfo.InvariantCulture)), Tip()),
            ("RWS_Stat_Longest".Translate(), V(Dialog_RimworldStatistics.DaysWithYears(s.LongestTicks)), Tip()),
            ("RWS_Stat_SingleMaxWealth".Translate(), WealthValue(s.SingleMaxWealth, s.PeakWealthColonyCount), WealthTip(s.PeakWealthColonyCount)),
            ("RWS_Stat_MaxPopulation".Translate(), V(s.MaxPopulation.ToString(CultureInfo.InvariantCulture)), Tip()),
            ("RWS_Stat_LastWealthSum".Translate(), WealthValue(s.LastWealthSum, s.LastWealthColonyCount), WealthTip(s.LastWealthColonyCount)),
        };

        const float gap = 10f;
        var topWidth = (rect.width - gap * 2) / 3f;
        for (var i = 0; i < top.Length; i++)
        {
            var cell = new Rect(rect.x + (i % 3) * (topWidth + gap), rect.y + (i / 3) * (TopCellHeight + gap), topWidth, TopCellHeight);
            DrawCell(cell, top[i], GameFont.Medium);
        }
        var y = rect.y + 2 * (TopCellHeight + gap) + 14f;
        var bottomWidth = (rect.width - gap * 3) / 4f;
        for (var i = 0; i < bottom.Length; i++)
        {
            var cell = new Rect(rect.x + (i % 4) * (bottomWidth + gap), y + (i / 4) * (BottomCellHeight + gap), bottomWidth, BottomCellHeight);
            DrawCell(cell, bottom[i], GameFont.Small);
        }
    }

    private static void DrawCell(Rect cell, (string Label, string Value, string Tip) item, GameFont valueFont)
    {
        Widgets.DrawHighlight(cell, 0.35f);
        Widgets.DrawHighlightIfMouseover(cell);
        var inner = cell.ContractedBy(8f);
        Text.Font = GameFont.Small;
        GUI.color = new Color(0.8f, 0.8f, 0.8f);
        Widgets.Label(new Rect(inner.x, inner.y, inner.width, 24f), item.Label);
        GUI.color = Color.white;
        Text.Font = valueFont;
        Widgets.Label(new Rect(inner.x, inner.y + 26f, inner.width, inner.height - 26f), item.Value);
        Text.Font = GameFont.Small;
        TooltipHandler.TipRegion(cell, item.Tip);
    }

    /// <summary>"Computed from N colonies (excluded M)" plus what is missing (spec §4.4).</summary>
    private static string Basis(StatsView view)
    {
        string text = "RWS_TooltipBasis".Translate(view.Stats.ColonyCount, view.Stats.ExcludedColonyCount);
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
