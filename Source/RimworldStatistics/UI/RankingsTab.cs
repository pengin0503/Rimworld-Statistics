using System.Globalization;
using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>Ranks existing per-colony readings; no new recorder or saved metric.</summary>
internal static class RankingsTab
{
    private static readonly StatsTable Table = new(26f,
        new(52, 1), new(170, 4), new(105, 2), new(130, 2), new(100, 2));

    private sealed record Choice(string Key, string Label, MetricCategory? Category);

    private static StatsView? selectorView;
    private static object? selectorLanguage;
    private static Choice[] choices = [];
    private static Choice[] filtered = [];
    private static string? priorSearch;
    private static MetricCategory? priorCategory;
    private static string search = "";
    private static MetricCategory? category;
    private static string? selectedKey;
    private static bool descending = true;
    private static bool showAll;
    private static Vector2 choicesScroll;
    private static Vector2 tableScroll;

    private static StatsView? tableView;
    private static object? tableLanguage;
    private static string? tableKey;
    private static bool tableDescending;
    private static bool tableShowAll;
    private static string[][] rows = [];
    private static string coverage = "";

    public static void Draw(Rect rect, StatsView view, ViewPreferences preferences, Action persist)
    {
        var lang = LanguageDatabase.activeLanguage;
        if (!ReferenceEquals(selectorView, view) || !ReferenceEquals(selectorLanguage, lang))
        {
            selectorView = view;
            selectorLanguage = lang;
            choices = LeaderboardBuilder.RankableKeys(view).Select(key =>
            {
                var def = MetricLookup.Definition(view, key);
                return new Choice(key,
                    def is null ? MetricText.BasicLabel(key) : MetricText.Label(def),
                    def?.Category);
            }).OrderBy(c => c.Label, StringComparer.CurrentCulture)
              .ThenBy(c => c.Key, StringComparer.Ordinal).ToArray();
            priorSearch = null;
            if (selectedKey is null || !choices.Any(c => c.Key == selectedKey))
                selectedKey = choices.FirstOrDefault()?.Key;
        }

        var selectorWidth = Math.Min(350f, Math.Max(200f, rect.width * 0.34f));
        var left = new Rect(rect.x, rect.y, selectorWidth, rect.height);
        var right = new Rect(left.xMax + 10f, rect.y, Math.Max(0, rect.width - selectorWidth - 10f), rect.height);
        Widgets.DrawMenuSection(left);
        Widgets.DrawMenuSection(right);
        DrawSelector(left.ContractedBy(7f), preferences, persist);

        var body = right.ContractedBy(7f);
        var active = choices.FirstOrDefault(c => c.Key == selectedKey);
        if (active is null)
        {
            Widgets.Label(body, "RWS_Rankings_NoMatches".Translate());
            return;
        }

        Widgets.Label(new Rect(body.x, body.y, body.width, 26f), active.Label.Truncate(body.width));
        var controls = new ControlRow(new Rect(body.x, body.y + 30f, body.width, 28f));
        var label = descending ? (string)"RWS_Rankings_Desc".Translate() : (string)"RWS_Rankings_Asc".Translate();
        if (Widgets.ButtonText(controls.Next(label, 112f, 185f), label))
        {
            descending = !descending;
            tableScroll = Vector2.zero;
        }
        label = showAll ? (string)"RWS_Rankings_Top10".Translate() : (string)"RWS_Rankings_All".Translate();
        if (Widgets.ButtonText(controls.Next(label, 112f, 170f), label))
        {
            showAll = !showAll;
            tableScroll = Vector2.zero;
        }

        if (!ReferenceEquals(tableView, view) || !ReferenceEquals(tableLanguage, lang)
            || tableKey != selectedKey || tableDescending != descending || tableShowAll != showAll)
        {
            tableView = view;
            tableLanguage = lang;
            tableKey = selectedKey;
            tableDescending = descending;
            tableShowAll = showAll;

            var board = LeaderboardBuilder.Build(view, selectedKey!, descending);
            var def = MetricLookup.Definition(view, selectedKey!);
            rows = (showAll ? board.Entries : board.Entries.Take(10)).Select(e =>
            {
                var name = view.Stats.Colonies.FirstOrDefault(c => c.Key == e.ColonyKey) is { } stats
                    ? Dialog_RimworldStatistics.ColonyName(stats) : e.ColonyKey;
                return new[]
                {
                    e.Rank.ToString(CultureInfo.InvariantCulture), name,
                    def is null ? BasicValue(selectedKey!, e.Value)
                        : MetricText.Format(def, new NumericValue { Number = e.Value,
                            Coverage = e.Coverage, Evidence = e.Evidence }),
                    def is null ? (string)"RWS_Coverage_SnapshotOnly".Translate()
                        : MetricText.State(def, e.Coverage),
                    e.ThroughTick is { } tick ? Dialog_RimworldStatistics.Days(tick) : Dialog_RimworldStatistics.Dash,
                };
            }).ToArray();
            coverage = "RWS_Rankings_Coverage".Translate(board.Measured, board.Targets);
            if (board.HasPartial)
                coverage += " — " + (string)"RWS_Rankings_PartialNote".Translate();
        }
        var infoY = body.y + 30f + controls.Height + 4f;
        Widgets.Label(new Rect(body.x, infoY, body.width, 30f), coverage.Truncate(body.width));
        TooltipHandler.TipRegion(new Rect(body.x, infoY, body.width, 30f), coverage);
        var table = new Rect(body.x, infoY + 30f, body.width, Math.Max(0f, body.yMax - infoY - 30f));
        if (rows.Length == 0)
            Widgets.Label(table, "RWS_Rankings_NoMatches".Translate());
        else
            Table.Draw(table, ref tableScroll,
            [
                "RWS_Rankings_Position".Translate(), "RWS_Details_Col_Name".Translate(),
                "RWS_Details_Col_Value".Translate(), "RWS_Details_Col_State".Translate(),
                "RWS_Details_Col_Through".Translate(),
            ], rows);
    }

    private static void DrawSelector(Rect rect, ViewPreferences preferences, Action persist)
    {
        var label = (string)"RWS_Rankings_Search".Translate();
        Widgets.Label(new Rect(rect.x, rect.y, rect.width, 23f), label);
        var newSearch = Widgets.TextField(new Rect(rect.x, rect.y + 26f, rect.width, 27f), search) ?? "";
        if (newSearch != search)
        {
            search = newSearch;
            choicesScroll = Vector2.zero;
        }
        var categoryText = category is { } c ? DetailsTab.CategoryLabel(c) : (string)"RWS_Details_AllCategories".Translate();
        if (Widgets.ButtonText(new Rect(rect.x, rect.y + 57f, rect.width, 27f), categoryText.Truncate(rect.width)))
        {
            var menu = new List<FloatMenuOption>
            {
                new("RWS_Details_AllCategories".Translate(), () => { category = null; choicesScroll = Vector2.zero; }),
            };
            menu.AddRange(choices.Where(c => c.Category is not null).Select(c => c.Category!.Value).Distinct()
                .Select(cat => new FloatMenuOption(DetailsTab.CategoryLabel(cat), () =>
                {
                    category = cat;
                    choicesScroll = Vector2.zero;
                })));
            Find.WindowStack.Add(new FloatMenu(menu));
        }
        if (priorSearch != search || priorCategory != category)
        {
            priorSearch = search;
            priorCategory = category;
            filtered = choices.Where(c => (category is null || c.Category == category)
                && (search.Length == 0 || c.Label.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                    || c.Key.Contains(search, StringComparison.OrdinalIgnoreCase))).ToArray();
        }

        var viewport = new Rect(rect.x, rect.y + 90f, rect.width, Math.Max(0, rect.height - 90f));
        var totalWidth = Math.Max(0f, viewport.width - 16f);
        Widgets.BeginScrollView(viewport, ref choicesScroll, new Rect(0, 0, totalWidth, filtered.Length * 30f));
        var first = Math.Max(0, (int)(choicesScroll.y / 30f));
        var last = Math.Min(filtered.Length - 1, (int)((choicesScroll.y + viewport.height) / 30f) + 1);
        for (var i = first; i <= last; i++)
        {
            var option = filtered[i];
            var star = new Rect(0, i * 30f, 26f, 28f);
            var saved = preferences.FavoriteMetricKeys.Contains(option.Key, StringComparer.Ordinal);
            if (Widgets.ButtonText(star, saved ? "★" : "☆"))
            {
                if (saved)
                    preferences.FavoriteMetricKeys.Remove(option.Key);
                else if (preferences.FavoriteMetricKeys.Count < 500)
                    preferences.FavoriteMetricKeys.Add(option.Key);
                persist();
            }
            var button = new Rect(30f, i * 30f, Math.Max(0f, totalWidth - 30f), 28f);
            if (Widgets.ButtonText(button, option.Label.Truncate(button.width - 10f)))
            {
                selectedKey = option.Key;
                tableScroll = Vector2.zero;
            }
            TooltipHandler.TipRegion(star, "RWS_Fav_StarTip".Translate());
            TooltipHandler.TipRegion(button, option.Label);
        }
        Widgets.EndScrollView();
    }

    private static string BasicValue(string key, double n)
    {
        var number = n.ToString("N1", CultureInfo.InvariantCulture);
        if (key == BasicMetrics.Days)
            return "RWS_Unit_Days".Translate(number);
        if (key == BasicMetrics.RealPlayHours)
            return "RWS_HoursValue".Translate(number);
        return StatFormat.Group(n);
    }
}
