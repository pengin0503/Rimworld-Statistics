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

    private sealed record Choice(string Key, string Label, MetricCategory? Category, string? Source);

    private static StatsView? selectorView;
    private static object? selectorLanguage;
    private static Choice[] choices = [];
    private static Choice[] filtered = [];
    private static string? priorSearch;
    private static MetricCategory? priorCategory;
    private static string search = "";
    private static MetricCategory? category;
    private static string? selectedKey;
    private static bool selectorCollapsed;
    private static bool descending = true;
    private static bool showAll;
    private static Vector2 choicesScroll;
    private static Vector2 tableScroll;

    private static StatsView? tableView;
    private static object? tableLanguage;
    private static string? tableKey;
    private static bool tableDescending;
    private static bool tableShowAll;
    private static string tableNameSignature = "";
    private static string[][] rows = [];
    private static string coverage = "";
    private static readonly bool[] RightAlignedColumns = [true, false, true, false, true];

    public static void Draw(Rect rect, StatsView view, ViewPreferences preferences, Action persist, string? scopeName = null)
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
                    def?.Category, def is null ? null : MetricText.ModSourceLabel(def));
            }).OrderBy(c => c.Label, StringComparer.CurrentCulture)
              .ThenBy(c => c.Key, StringComparer.Ordinal).ToArray();
            priorSearch = null;
            if (selectedKey is null || !choices.Any(c => c.Key == selectedKey))
                selectedKey = choices.FirstOrDefault()?.Key;
        }

        var split = StatisticsWindowLayout.ResolveCollapsibleSplit(rect.width, selectorCollapsed);
        if (!selectorCollapsed && split.LeftWidth > 0f)
        {
            var left = new Rect(rect.x, rect.y, split.LeftWidth, rect.height);
            Widgets.DrawMenuSection(left);
            DrawSelector(left.ContractedBy(8f), preferences, persist);
        }

        if (split.RightWidth <= 0f)
            return;

        var right = new Rect(rect.x + split.RightXOffset, rect.y, split.RightWidth, rect.height);
        Widgets.DrawMenuSection(right);

        var body = right.ContractedBy(8f);
        var active = choices.FirstOrDefault(c => c.Key == selectedKey);
        var toggleLabel = selectorCollapsed
            ? (string)"RWS_Rankings_ShowSelector".Translate()
            : (string)"RWS_Rankings_HideSelector".Translate();

        if (active is null)
        {
            var emptyControls = new ControlRow(new Rect(body.x, body.y, body.width, 28f));
            var toggleRect = emptyControls.Next(toggleLabel, 118f, Math.Min(body.width, 200f));
            var shownToggle = toggleLabel.Truncate(Math.Max(0f, toggleRect.width - 12f));
            if (Widgets.ButtonText(toggleRect, shownToggle))
                selectorCollapsed = !selectorCollapsed;
            if (shownToggle != toggleLabel)
                TooltipHandler.TipRegion(toggleRect, toggleLabel);
            Widgets.Label(new Rect(body.x, body.y + emptyControls.Height + 6f, body.width, Math.Max(0f, body.height - emptyControls.Height - 6f)),
                "RWS_Rankings_NoMatches".Translate());
            return;
        }

        var titleRect = new Rect(body.x, body.y, body.width, 28f);
        Text.Font = GameFont.Medium;
        var shownTitle = active.Label.Truncate(titleRect.width);
        Widgets.Label(titleRect, shownTitle);
        Text.Font = GameFont.Small;
        if (shownTitle != active.Label)
            TooltipHandler.TipRegion(titleRect, active.Label);
        var sourceOffset = active.Source is null ? 0f : 21f;
        if (active.Source is { } source)
        {
            var sourceRect = new Rect(body.x, titleRect.yMax, body.width, 19f);
            var shownSource = source.Truncate(sourceRect.width);
            Widgets.Label(sourceRect, shownSource);
            if (shownSource != source)
                TooltipHandler.TipRegion(sourceRect, source);
        }
        Widgets.DrawBoxSolid(new Rect(body.x, titleRect.yMax + sourceOffset + 2f, body.width, 1f), new Color(1f, 1f, 1f, 0.14f));

        var controls = new ControlRow(new Rect(body.x, titleRect.yMax + sourceOffset + 7f, body.width, 28f));
        var selectorToggleRect = controls.Next(toggleLabel, 118f, Math.Min(body.width, 195f));
        var shownSelectorToggle = toggleLabel.Truncate(Math.Max(0f, selectorToggleRect.width - 12f));
        if (Widgets.ButtonText(selectorToggleRect, shownSelectorToggle))
            selectorCollapsed = !selectorCollapsed;
        if (shownSelectorToggle != toggleLabel)
            TooltipHandler.TipRegion(selectorToggleRect, toggleLabel);

        var label = descending ? (string)"RWS_Rankings_Desc".Translate() : (string)"RWS_Rankings_Asc".Translate();
        var orderRect = controls.Next(label, 112f, 185f);
        var shownOrder = label.Truncate(Math.Max(0f, orderRect.width - 12f));
        if (Widgets.ButtonText(orderRect, shownOrder))
        {
            descending = !descending;
            tableScroll = Vector2.zero;
        }
        if (shownOrder != label)
            TooltipHandler.TipRegion(orderRect, label);

        label = showAll ? (string)"RWS_Rankings_Top10".Translate() : (string)"RWS_Rankings_All".Translate();
        var countRect = controls.Next(label, 112f, 175f);
        var shownCount = label.Truncate(Math.Max(0f, countRect.width - 12f));
        if (Widgets.ButtonText(countRect, shownCount))
        {
            showAll = !showAll;
            tableScroll = Vector2.zero;
        }
        if (shownCount != label)
            TooltipHandler.TipRegion(countRect, label);

        label = "RWS_Scatter_Open".Translate();
        var scatterRect = controls.Next(label, 112f, 190f);
        if (Widgets.ButtonText(scatterRect, label.Truncate(Math.Max(0f, scatterRect.width - 12f))))
            Find.WindowStack.Add(new Dialog_MetricScatterPlot(view, selectedKey, null, scopeName));
        TooltipHandler.TipRegion(scatterRect, "RWS_Scatter_OpenTip".Translate());

        var nameSignature = ColonyPresentation.Signature(preferences);
        if (!ReferenceEquals(tableView, view) || !ReferenceEquals(tableLanguage, lang)
            || tableKey != selectedKey || tableDescending != descending || tableShowAll != showAll
            || tableNameSignature != nameSignature)
        {
            tableView = view;
            tableLanguage = lang;
            tableKey = selectedKey;
            tableDescending = descending;
            tableShowAll = showAll;
            tableNameSignature = nameSignature;

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
        var infoY = titleRect.yMax + sourceOffset + 7f + controls.Height + 4f;
        var infoRect = new Rect(body.x, infoY, body.width, 26f);
        var prevColor = GUI.color;
        GUI.color = new Color(0.84f, 0.86f, 0.90f);
        var shownInfo = coverage.Truncate(infoRect.width);
        Widgets.Label(infoRect, shownInfo);
        GUI.color = prevColor;
        if (shownInfo != coverage)
            TooltipHandler.TipRegion(infoRect, coverage);

        var table = new Rect(body.x, infoY + 28f, body.width, Math.Max(0f, body.yMax - infoY - 28f));
        if (rows.Length == 0)
            Widgets.Label(table, "RWS_Rankings_NoMatches".Translate());
        else
            Table.Draw(table, ref tableScroll,
            [
                "RWS_Rankings_Position".Translate(), "RWS_Details_Col_Name".Translate(),
                "RWS_Details_Col_Value".Translate(), "RWS_Details_Col_State".Translate(),
                "RWS_Details_Col_Through".Translate(),
            ], rows, RightAlignedColumns);
    }

    private static void DrawSelector(Rect rect, ViewPreferences preferences, Action persist)
    {
        var label = (string)"RWS_Rankings_Search".Translate();
        Widgets.Label(new Rect(rect.x, rect.y, rect.width, 23f), label.Truncate(rect.width));
        var newSearch = Widgets.TextField(new Rect(rect.x, rect.y + 25f, rect.width, 27f), search) ?? "";
        if (newSearch != search)
        {
            search = newSearch;
            choicesScroll = Vector2.zero;
        }
        var categoryText = category is { } c ? DetailsTab.CategoryLabel(c) : (string)"RWS_Details_AllCategories".Translate();
        var catRect = new Rect(rect.x, rect.y + 56f, rect.width, 27f);
        var shownCategory = categoryText.Truncate(Math.Max(0f, catRect.width - 12f));
        if (Widgets.ButtonText(catRect, shownCategory))
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
        if (shownCategory != categoryText)
            TooltipHandler.TipRegion(catRect, categoryText);

        if (priorSearch != search || priorCategory != category)
        {
            priorSearch = search;
            priorCategory = category;
            filtered = choices.Where(c => (category is null || c.Category == category)
                && (search.Length == 0 || c.Label.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                    || c.Key.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || (c.Source?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false))).ToArray();
        }

        var activeFilters = FilterSummary.CountActiveFilters(false, category is not null, false, search);
        var statusRow = new ControlRow(new Rect(rect.x, rect.y + 87f, rect.width, 24f));
        var countText = activeFilters > 0
            ? (string)"RWS_Filter_Shown".Translate(filtered.Length, choices.Length) + " · " + (string)"RWS_Filter_ActiveCount".Translate(activeFilters)
            : (string)"RWS_Filter_Shown".Translate(filtered.Length, choices.Length);
        var countRect = statusRow.Next(countText, 85f, rect.width);
        var anchor = Text.Anchor;
        var prevColor = GUI.color;
        Text.Anchor = TextAnchor.MiddleLeft;
        GUI.color = new Color(0.8f, 0.8f, 0.8f);
        var shownCountText = countText.Truncate(countRect.width);
        Widgets.Label(countRect, shownCountText);
        GUI.color = prevColor;
        Text.Anchor = anchor;
        if (shownCountText != countText)
            TooltipHandler.TipRegion(countRect, countText);

        if (activeFilters > 0)
        {
            var resetLabel = (string)"RWS_Filter_Reset".Translate();
            var resetRect = statusRow.Next(resetLabel, 85f, rect.width);
            var shownReset = resetLabel.Truncate(Math.Max(0f, resetRect.width - 10f));
            if (Widgets.ButtonText(resetRect, shownReset))
            {
                search = "";
                category = null;
                choicesScroll = Vector2.zero;
                filtered = choices;
                priorSearch = search;
                priorCategory = category;
            }
            if (shownReset != resetLabel)
                TooltipHandler.TipRegion(resetRect, resetLabel);
        }

        var listY = rect.y + 87f + statusRow.Height + 4f;
        var viewport = new Rect(rect.x, listY, rect.width, Math.Max(0, rect.yMax - listY));
        if (filtered.Length == 0)
        {
            Widgets.Label(viewport, "RWS_Details_NoMatch".Translate());
            return;
        }
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
            var isSelected = string.Equals(option.Key, selectedKey, StringComparison.Ordinal);
            if (isSelected)
                Widgets.DrawBoxSolid(button, new Color(0.26f, 0.44f, 0.56f, 0.35f));
            if (Widgets.ButtonText(button, option.Label.Truncate(Math.Max(0f, button.width - 10f))))
            {
                selectedKey = option.Key;
                tableScroll = Vector2.zero;
            }
            if (isSelected)
                Widgets.DrawBoxSolid(new Rect(button.x, button.y + 2f, 3f, button.height - 4f), new Color(0.45f, 0.78f, 0.94f, 0.95f));
            TooltipHandler.TipRegion(star, "RWS_Fav_StarTip".Translate());
            TooltipHandler.TipRegion(button, option.Source is null ? option.Label
                : option.Label + "\n" + option.Source);
        }
        Widgets.EndScrollView();
    }

    internal static string BasicValue(string key, double n)
    {
        var number = n.ToString("N1", CultureInfo.InvariantCulture);
        if (key == BasicMetrics.Days)
            return "RWS_Unit_Days".Translate(number);
        if (key == BasicMetrics.RealPlayHours)
            return "RWS_HoursValue".Translate(number);
        return StatFormat.Group(n);
    }
}
