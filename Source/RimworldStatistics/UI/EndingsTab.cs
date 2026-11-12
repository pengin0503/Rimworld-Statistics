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
    private static object? language;
    private static string[][] rows = [];
    private static string[] headers = [];
    private static readonly StatsTable Table = new(RowHeight, new(112, 2), new(136, 4), new(104, 3),
        new(72, 1), new(76, 1), new(115, 2));
    private static Vector2 scroll;

    public static void Draw(Rect rect, StatsView view)
    {
        var currentLanguage = LanguageDatabase.activeLanguage;
        if (!ReferenceEquals(builtFor, view) || !ReferenceEquals(language, currentLanguage))
        {
            builtFor = view;
            language = currentLanguage;
            rows = Build(view, ProfileService.Options.IncludeDevMode).Select(r => new[]
            {
                Dialog_RimworldStatistics.Local(r.Time), r.Colony, Dialog_RimworldStatistics.EndingLabel(r.Type),
                r.Ordinal, r.Departed, r.Source,
            }).ToArray();
            headers =
            [
                "RWS_Col_Date".Translate(), "RWS_Col_Colony".Translate(), "RWS_Col_EndingType".Translate(),
                "RWS_Col_Ordinal".Translate(), "RWS_Col_Departed".Translate(), "RWS_Col_Source".Translate(),
            ];
        }
        if (rows.Length == 0)
        {
            Widgets.Label(rect, "RWS_Endings_None".Translate() + "\n\n" + "RWS_EndingsTooltipPreInstall".Translate());
            return;
        }

        Table.Draw(rect, ref scroll, headers, rows);
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
                list.Add(new Row(visible.Min(e => e.RealTimeUtc), name, pair.Key.Type, (pair.Key.Ordinal + 1L).ToString(CultureInfo.InvariantCulture),
                    best.Source == EndingSource.SeededTrace ? Dialog_RimworldStatistics.Dash : best.Departed.ToString(CultureInfo.InvariantCulture), source));
            }
            foreach (var recovered in colony.RecoveredEndings)
                list.Add(new Row(null, name, recovered.Type, (recovered.Ordinal + 1L).ToString(CultureInfo.InvariantCulture),
                    Dialog_RimworldStatistics.Dash, "RWS_Source_RecoveredLineage".Translate()));
            foreach (var trace in colony.Traces.Where(t => !shownTypes.Contains(t)).OrderBy(t => t))
            {
                var first = colony.Observations.Where(o => o.EndingTraces.Contains(trace)).Select(o => (DateTime?)o.LastWriteUtc).DefaultIfEmpty(null).Min();
                list.Add(new Row(first, name, trace, "RWS_AtLeastOnce".Translate(), Dialog_RimworldStatistics.Dash, "RWS_Source_SaveTrace".Translate()));
            }
        }
        return list.OrderByDescending(r => r.Time ?? DateTime.MinValue).ToList();
    }
}
