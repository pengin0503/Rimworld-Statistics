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

    private sealed record Row(
        DateTime? Time, string Colony, EndingType Type, string Ordinal, string Departed, string Source);

    private static StatsView? builtFor;
    private static object? language;
    private static string nameSignature = "";
    private static bool includesDevMode;
    private static Row[] allRows = [];
    private static string[][] rows = [];
    private static string[] headers = [];
    private static readonly StatsTable Table = new(RowHeight, new(112, 2), new(136, 4), new(104, 3),
        new(72, 1), new(76, 1), new(115, 2));
    private static Vector2 scroll;
    // U3 controls. The shaped rows are cached on these values, like the other tabs' row caches.
    private static string search = "";
    private static bool ascending;
    private static EndingType? typeFilter;
    private static bool shapeDirty = true;
    private static string shapedSearch = "";
    private static bool shapedAscending;
    private static EndingType? shapedTypeFilter;

    public static void Draw(Rect rect, StatsView view)
    {
        var currentLanguage = LanguageDatabase.activeLanguage;
        var currentNameSignature = ColonyPresentation.Signature(Dialog_RimworldStatistics.ActivePreferences);
        var includeDev = ProfileService.Options.IncludeDevMode;
        if (!ReferenceEquals(builtFor, view) || !ReferenceEquals(language, currentLanguage)
            || nameSignature != currentNameSignature || includesDevMode != includeDev)
        {
            builtFor = view;
            language = currentLanguage;
            nameSignature = currentNameSignature;
            includesDevMode = includeDev;
            allRows = Build(view, includeDev).ToArray();
            headers =
            [
                "RWS_Col_Date".Translate(), "RWS_Col_Colony".Translate(), "RWS_Col_EndingType".Translate(),
                "RWS_Col_Ordinal".Translate(), "RWS_Col_Departed".Translate(), "RWS_Col_Source".Translate(),
            ];
            // A type that is no longer present (for example after turning dev mode off) falls back to All.
            if (typeFilter is { } filter && allRows.All(row => row.Type != filter))
                typeFilter = null;
            shapeDirty = true;
        }
        if (allRows.Length == 0)
        {
            DrawEmptyState(rect);
            return;
        }

        var controls = new ControlRow(new Rect(rect.x, rect.y, rect.width, 28f));
        var searchWidth = Math.Max(140f, Math.Min(rect.width * 0.45f, 320f));
        var searchRect = controls.Next(searchWidth);
        var nextSearch = Widgets.TextField(searchRect, search) ?? "";
        if (nextSearch != search)
        {
            search = nextSearch;
            scroll = Vector2.zero;
        }
        TooltipHandler.TipRegion(searchRect, "RWS_Endings_SearchTip".Translate());

        var sortText = ascending
            ? (string)"RWS_Endings_SortAsc".Translate()
            : (string)"RWS_Endings_SortDesc".Translate();
        var sortRect = controls.Next(sortText, 120f, 200f);
        var shownSort = sortText.Truncate(Math.Max(0f, sortRect.width - 12f));
        if (shownSort != sortText)
            TooltipHandler.TipRegion(sortRect, sortText);
        if (Widgets.ButtonText(sortRect, shownSort))
        {
            ascending = !ascending;
            scroll = Vector2.zero;
        }

        var filterText = typeFilter is { } type
            ? Dialog_RimworldStatistics.EndingLabel(type)
            : (string)"RWS_Endings_FilterAll".Translate();
        var filterRect = controls.Next(filterText, 130f, 220f);
        var shownFilter = filterText.Truncate(Math.Max(0f, filterRect.width - 12f));
        if (shownFilter != filterText)
            TooltipHandler.TipRegion(filterRect, filterText);
        if (Widgets.ButtonText(filterRect, shownFilter))
            ShowEndingTypeMenu();

        var goText = (string)"RWS_Endings_GoToColony".Translate();
        var goRect = controls.Next(goText, 130f, 210f);
        var shownGo = goText.Truncate(Math.Max(0f, goRect.width - 12f));
        if (shownGo != goText)
            TooltipHandler.TipRegion(goRect, goText);
        if (Widgets.ButtonText(goRect, shownGo))
            ShowColonyMenu(view);

        if (shapeDirty || search != shapedSearch || ascending != shapedAscending || typeFilter != shapedTypeFilter)
            ShapeRows();

        var top = rect.y + controls.Height + 6f;
        var tableRect = new Rect(rect.x, top, rect.width, Math.Max(0f, rect.yMax - top));
        if (rows.Length == 0)
        {
            Widgets.Label(new Rect(rect.x, top, rect.width, 28f), "RWS_Details_NoMatch".Translate());
            return;
        }
        Table.Draw(tableRect, ref scroll, headers, rows);
    }

    /// <summary>The empty state's action (U44): leave for the Data management tab that imports the saves.</summary>
    private static void DrawEmptyState(Rect rect)
    {
        var text = (string)"RWS_Endings_None".Translate() + "\n\n" + (string)"RWS_EndingsTooltipPreInstall".Translate();
        var height = Math.Min(rect.height, Text.CalcHeight(text, Math.Max(1f, rect.width)));
        Widgets.Label(new Rect(rect.x, rect.y, rect.width, height), text);
        var label = (string)"RWS_Empty_OpenImport".Translate();
        var button = new Rect(rect.x, rect.y + height + 10f,
            Math.Min(rect.width, Math.Max(200f, Text.CalcSize(label).x + 24f)), 32f);
        if (Widgets.ButtonText(button, label))
            Dialog_RimworldStatistics.SwitchTo(Dialog_RimworldStatistics.Tab.Import);
    }

    private static void ShowEndingTypeMenu()
    {
        var menu = new List<FloatMenuOption>
        {
            new("RWS_Endings_FilterAll".Translate(), () => { typeFilter = null; scroll = Vector2.zero; }),
        };
        menu.AddRange(allRows.Select(row => row.Type).Distinct()
            .OrderBy(Dialog_RimworldStatistics.EndingLabel, StringComparer.CurrentCulture)
            .Select(type => new FloatMenuOption(Dialog_RimworldStatistics.EndingLabel(type), () =>
            {
                typeFilter = type;
                scroll = Vector2.zero;
            })));
        Find.WindowStack.Add(new FloatMenu(menu));
    }

    /// <summary>
    /// U3: open the colony chronicle for a colony in this view. The chronicle dialog takes the same view and key the
    /// Colonies tab passes, so no extra selection state is needed.
    /// </summary>
    private static void ShowColonyMenu(StatsView view)
    {
        var menu = new List<FloatMenuOption>();
        for (var i = 0; i < view.Colonies.Count; i++)
        {
            var colony = view.Colonies[i];
            var stats = view.Stats.Colonies[i];
            menu.Add(new FloatMenuOption(Dialog_RimworldStatistics.ColonyName(stats), () =>
                UiScopes.OpenOrReplaceWindow(new Dialog_ColonyChronicle(view, colony.Key))));
        }
        menu.Sort((a, b) => StringComparer.CurrentCulture.Compare(a.Label, b.Label));
        if (menu.Count > 0)
            Find.WindowStack.Add(new FloatMenu(menu));
    }

    private static void ShapeRows()
    {
        var term = search.Trim();
        var filtered = allRows
            .Where(row => typeFilter is not { } filter || row.Type == filter)
            .Where(row => term.Length == 0
                || UnitSearch.Matches(row.Colony, "", term)
                || UnitSearch.Matches(Dialog_RimworldStatistics.EndingLabel(row.Type), "", term));
        var ordered = ascending
            ? filtered.OrderBy(row => row.Time is null ? 1 : 0)
                .ThenBy(row => row.Time ?? DateTime.MinValue)
            : filtered.OrderBy(row => row.Time is null ? 1 : 0)
                .ThenByDescending(row => row.Time ?? DateTime.MinValue);
        rows = ordered.Select(row => new[]
        {
            Dialog_RimworldStatistics.Local(row.Time), row.Colony, Dialog_RimworldStatistics.EndingLabel(row.Type),
            row.Ordinal, row.Departed, row.Source,
        }).ToArray();
        shapedSearch = search;
        shapedAscending = ascending;
        shapedTypeFilter = typeFilter;
        shapeDirty = false;
    }

    private static List<Row> Build(StatsView view, bool includeDev)
    {
        var list = new List<Row>();
        for (var i = 0; i < view.Colonies.Count; i++)
        {
            var colony = view.Colonies[i];
            var stats = view.Stats.Colonies[i];
            var name = Dialog_RimworldStatistics.ColonyName(stats) + (stats.Excluded ? (string)"RWS_ExcludedMark".Translate() : "");
            foreach (var pair in colony.Endings.GroupBy(e => (e.Type, e.Ordinal)))
            {
                var visible = pair.Where(e => includeDev || !e.DevMode).ToList();
                if (visible.Count == 0)
                    continue;   // dev-mode-only and the setting is off
                var best = EndingTraces.Representative(visible);
                var source = best.Source == EndingSource.SeededTrace ? "RWS_Source_SeededTrace".Translate() : "RWS_Source_Live".Translate();
                if (best.DevMode)
                    source += "RWS_DevModeMark".Translate();
                list.Add(new Row(visible.Min(e => e.RealTimeUtc), name, pair.Key.Type, (pair.Key.Ordinal + 1L).ToString(CultureInfo.InvariantCulture),
                    best.Source == EndingSource.SeededTrace ? Dialog_RimworldStatistics.Dash : best.Departed.ToString(CultureInfo.InvariantCulture), source));
            }
            foreach (var recovered in colony.RecoveredEndings)
                list.Add(new Row(null, name, recovered.Type, (recovered.Ordinal + 1L).ToString(CultureInfo.InvariantCulture),
                    Dialog_RimworldStatistics.Dash, "RWS_Source_RecoveredLineage".Translate()));
            foreach (var trace in EndingTraces.Unexplained(colony))
            {
                var first = EndingTraces.FirstTraceUtc(colony, trace);
                list.Add(new Row(first, name, trace, "RWS_AtLeastOnce".Translate(), Dialog_RimworldStatistics.Dash, "RWS_Source_SaveTrace".Translate()));
            }
        }
        return list;
    }
}
