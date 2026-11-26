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

    private sealed record Choice(string Key, string Label, MetricCategory? Category, string? Source, bool CanRankPerDay);

    private static StatsView? selectorView;
    private static object? selectorLanguage;
    private static Choice[] choices = [];
    private static Choice[] filtered = [];
    private static string? priorSearch;
    private static MetricCategory? priorCategory;
    private static bool priorPerDay;
    private static string search = "";
    private static MetricCategory? category;
    private static string? selectedKey;
    private static bool selectorCollapsed;
    private static bool descending = true;
    private static bool showAll;
    private static bool perDayMode;
    private static RateUnit rateUnit = RateUnit.PerDay;
    private static Vector2 choicesScroll;
    private static Vector2 tableScroll;
    private static bool restorePending;
    private static ViewPreferences? viewPreferences;
    private static Action? persistViewState;
    private static PersistedState? lastPersistedState;

    private readonly record struct PersistedState(string Search, MetricCategory? Category, string? SelectedKey,
        bool SelectorCollapsed, bool Descending, bool ShowAll, bool PerDayMode, RateUnit RateUnit);

    private static StatsView? tableView;
    private static object? tableLanguage;
    private static string? tableKey;
    private static bool tableDescending;
    private static bool tableShowAll;
    private static bool tablePerDay;
    private static RateUnit tableRateUnit = RateUnit.PerDay;
    private static string tableNameSignature = "";
    private static string[][] rows = [];
    private static string coverage = "";
    private static readonly bool[] RightAlignedColumns = [true, false, true, false, true];
    private static Choice[] perDayChoices = [];
    private static object? headerLanguage;
    private static bool headerPerDay;
    private static RateUnit headerRateUnit = RateUnit.PerDay;
    private static string[]? cachedHeaders;

    /// <summary>U4: the statistics window asks for a restore on its first draw after opening.</summary>
    internal static void RequestRestore() => restorePending = true;

    public static void Draw(Rect rect, StatsView view, ViewPreferences preferences, Action persist, string? scopeName = null)
    {
        viewPreferences = preferences;
        persistViewState = persist;
        var lang = LanguageDatabase.activeLanguage;
        if (!ReferenceEquals(selectorView, view) || !ReferenceEquals(selectorLanguage, lang))
        {
            selectorView = view;
            selectorLanguage = lang;
            choices = LeaderboardBuilder.RankableKeys(view).Select(key =>
            {
                var def = MetricLookup.Definition(view, key);
                var canPerDay = LeaderboardBuilder.CanRankPerDay(view, key);
                return new Choice(key,
                    def is null ? MetricText.PerColonyBasicLabel(key) : MetricText.Label(def),
                    def?.Category, def is null ? null : MetricText.ModSourceLabel(def),
                    canPerDay);
            }).OrderBy(c => c.Label, StringComparer.CurrentCulture)
              .ThenBy(c => c.Key, StringComparer.Ordinal).ToArray();
            perDayChoices = choices.Where(c => c.CanRankPerDay).ToArray();
            priorSearch = null;
        }

        if (restorePending)
        {
            restorePending = false;
            RestoreState(preferences);
            lastPersistedState = CaptureState();
        }

        var eligibleChoices = perDayMode ? perDayChoices : choices;
        if (selectedKey is null || !eligibleChoices.Any(c => c.Key == selectedKey))
        {
            selectedKey = eligibleChoices.FirstOrDefault()?.Key;
            if (selectedKey is not null)
                descending = perDayMode || LeaderboardBuilder.DefaultDescending(MetricLookup.Definition(view, selectedKey));
        }

        var split = StatisticsWindowLayout.ResolveCollapsibleSplit(rect.width, selectorCollapsed);
        if (!selectorCollapsed && split.LeftWidth > 0f)
        {
            var left = new Rect(rect.x, rect.y, split.LeftWidth, rect.height);
            Widgets.DrawMenuSection(left);
            DrawSelector(left.ContractedBy(8f), view, eligibleChoices, preferences, persist);
        }

        if (split.RightWidth <= 0f)
        {
            PersistViewStateIfChanged();
            return;
        }

        var right = new Rect(rect.x + split.RightXOffset, rect.y, split.RightWidth, rect.height);
        Widgets.DrawMenuSection(right);

        var body = right.ContractedBy(8f);
        var active = eligibleChoices.FirstOrDefault(c => c.Key == selectedKey);
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
            if (perDayMode)
            {
                var rateLabel = (string)"RWS_Rate_ModePerDay".Translate();
                var rateRect = emptyControls.Next(rateLabel, 110f, 195f);
                if (Widgets.ButtonText(rateRect, rateLabel.Truncate(Math.Max(0f, rateRect.width - 12f))))
                    perDayMode = false;
                DrawRateUnitButton(emptyControls);
            }
            Widgets.Label(new Rect(body.x, body.y + emptyControls.Height + 6f, body.width, Math.Max(0f, body.height - emptyControls.Height - 6f)),
                "RWS_Rankings_NoMatches".Translate());
            PersistViewStateIfChanged();
            return;
        }

        var titleRect = new Rect(body.x, body.y, body.width, 28f);
        var titleText = perDayMode ? RateTitle(active.Label) : active.Label;
        string shownTitle;
        using (UiScopes.GuiState())
        {
            Text.Font = GameFont.Medium;
            shownTitle = titleText.Truncate(titleRect.width);
            Widgets.Label(titleRect, shownTitle);
        }
        if (shownTitle != titleText)
            TooltipHandler.TipRegion(titleRect, titleText);
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

        var modeLabel = perDayMode ? (string)"RWS_Rate_ModePerDay".Translate() : (string)"RWS_Rate_ModeTotal".Translate();
        var modeRect = controls.Next(modeLabel, 112f, 195f);
        var shownMode = modeLabel.Truncate(Math.Max(0f, modeRect.width - 12f));
        if (Widgets.ButtonText(modeRect, shownMode))
        {
            perDayMode = !perDayMode;
            if (selectedKey is not null)
                descending = perDayMode || LeaderboardBuilder.DefaultDescending(MetricLookup.Definition(view, selectedKey));
            tableScroll = Vector2.zero;
            choicesScroll = Vector2.zero;
        }
        TooltipHandler.TipRegion(modeRect, "RWS_Rate_ModeToggleTip".Translate());

        if (perDayMode)
            DrawRateUnitButton(controls);

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
            UiScopes.OpenOrReplaceWindow(new Dialog_MetricScatterPlot(view, selectedKey, null, scopeName, yPerDay: perDayMode));
        TooltipHandler.TipRegion(scatterRect, "RWS_Scatter_OpenTip".Translate());

        var nameSignature = ColonyPresentation.Signature(preferences);
        if (!ReferenceEquals(tableView, view) || !ReferenceEquals(tableLanguage, lang)
            || tableKey != selectedKey || tableDescending != descending || tableShowAll != showAll
            || tablePerDay != perDayMode || tableRateUnit != rateUnit || tableNameSignature != nameSignature)
        {
            tableView = view;
            tableLanguage = lang;
            tableKey = selectedKey;
            tableDescending = descending;
            tableShowAll = showAll;
            tablePerDay = perDayMode;
            tableRateUnit = rateUnit;
            tableNameSignature = nameSignature;

            var board = LeaderboardBuilder.Build(view, selectedKey!, descending, perDay: perDayMode);
            var def = MetricLookup.Definition(view, selectedKey!);
            rows = (showAll ? board.Entries : board.Entries.Take(10)).Select(e =>
            {
                var name = view.Stats.Colonies.FirstOrDefault(c => c.Key == e.ColonyKey) is { } stats
                    ? Dialog_RimworldStatistics.ColonyName(stats) : e.ColonyKey;
                var valueText = def is null
                    ? BasicValue(selectedKey!, e.Value)
                    : perDayMode
                        ? MetricText.FormatRateValue(def, e.Value, rateUnit)
                        : MetricText.Format(def, new NumericValue
                        {
                            Number = e.Value, Coverage = e.Coverage, Evidence = e.Evidence,
                        });
                var stateText = def is null
                    ? (string)"RWS_Coverage_SnapshotOnly".Translate()
                    : perDayMode && e.RecordedDays is { } days
                        ? (string)"RWS_Rate_RecordedInterval".Translate(days.ToString("N1", CultureInfo.InvariantCulture))
                        : MetricText.State(def, e.Coverage);
                return new[]
                {
                    e.Rank.ToString(CultureInfo.InvariantCulture), name,
                    valueText,
                    stateText,
                    e.ThroughTick is { } tick ? Dialog_RimworldStatistics.Days(tick) : Dialog_RimworldStatistics.Dash,
                };
            }).ToArray();
            coverage = "RWS_Rankings_Coverage".Translate(board.Measured, board.Targets);
            if (board.HasPartial)
                coverage += " — " + (string)(perDayMode ? "RWS_Rankings_RatePartialNote" : "RWS_Rankings_PartialNote").Translate();
        }
        var infoY = titleRect.yMax + sourceOffset + 7f + controls.Height + 4f;
        var infoRect = new Rect(body.x, infoY, body.width, 26f);
        string shownInfo;
        using (UiScopes.GuiState())
        {
            GUI.color = new Color(0.84f, 0.86f, 0.90f);
            shownInfo = coverage.Truncate(infoRect.width);
            Widgets.Label(infoRect, shownInfo);
        }
        if (shownInfo != coverage)
            TooltipHandler.TipRegion(infoRect, coverage);

        var table = new Rect(body.x, infoY + 28f, body.width, Math.Max(0f, body.yMax - infoY - 28f));
        if (rows.Length == 0)
        {
            Widgets.Label(table, "RWS_Rankings_NoMatches".Translate());
        }
        else
        {
            if (cachedHeaders is null || !ReferenceEquals(headerLanguage, lang)
                || headerPerDay != perDayMode || headerRateUnit != rateUnit)
            {
                headerLanguage = lang;
                headerPerDay = perDayMode;
                headerRateUnit = rateUnit;
                var valueHeader = (string)"RWS_Details_Col_Value".Translate();
                if (perDayMode)
                    valueHeader += " · " + RateUnitLabel(rateUnit);
                cachedHeaders =
                [
                    "RWS_Rankings_Position".Translate(), "RWS_Details_Col_Name".Translate(),
                    valueHeader, "RWS_Details_Col_State".Translate(),
                    "RWS_Details_Col_Through".Translate(),
                ];
            }
            Table.Draw(table, ref tableScroll, cachedHeaders, rows, RightAlignedColumns);
        }
        PersistViewStateIfChanged();
    }

    private static void RestoreState(ViewPreferences prefs)
    {
        search = prefs.RankingsSearch ?? "";
        category = prefs.RankingsCategory is { } savedCategory && Enum.IsDefined(typeof(MetricCategory), savedCategory)
            ? (MetricCategory)savedCategory : null;
        selectedKey = prefs.RankingsSelectedKey;
        selectorCollapsed = prefs.RankingsSelectorCollapsed;
        descending = prefs.RankingsDescending;
        showAll = prefs.RankingsShowAll;
        perDayMode = prefs.RankingsPerDayMode;
        rateUnit = Enum.IsDefined(typeof(RateUnit), prefs.RankingsRateUnit)
            ? (RateUnit)prefs.RankingsRateUnit : RateUnit.PerDay;
        choicesScroll = Vector2.zero;
        tableScroll = Vector2.zero;
        priorSearch = null;
    }

    private static PersistedState CaptureState() => new(search, category, selectedKey, selectorCollapsed, descending, showAll, perDayMode, rateUnit);

    private static void PersistViewStateIfChanged()
    {
        var state = CaptureState();
        if (lastPersistedState == state)
            return;
        lastPersistedState = state;
        if (viewPreferences is not { } prefs)
            return;
        prefs.RankingsSearch = search ?? "";
        prefs.RankingsCategory = category is { } selectedCategory ? (int)selectedCategory : null;
        prefs.RankingsSelectedKey = selectedKey;
        prefs.RankingsSelectorCollapsed = selectorCollapsed;
        prefs.RankingsDescending = descending;
        prefs.RankingsShowAll = showAll;
        prefs.RankingsPerDayMode = perDayMode;
        prefs.RankingsRateUnit = (int)rateUnit;
        persistViewState?.Invoke();
    }

    // FloatMenu callbacks run after this tab's Draw, so they save at once instead of waiting for the next frame.
    private static void SetCategory(MetricCategory? value)
    {
        category = value;
        choicesScroll = Vector2.zero;
        PersistViewStateIfChanged();
    }

    private static void SetRateUnit(RateUnit value)
    {
        rateUnit = value;
        tableScroll = Vector2.zero;
        PersistViewStateIfChanged();
    }

    private static string RateUnitLabel(RateUnit unit) => unit switch
    {
        RateUnit.PerYear => "RWS_Rate_Unit_PerYear".Translate(),
        RateUnit.PerRealHour => "RWS_Rate_Unit_PerRealHour".Translate(),
        _ => "RWS_Rate_Unit_PerDay".Translate(),
    };

    private static string RateTitle(string label) => rateUnit switch
    {
        RateUnit.PerYear => "RWS_Rate_TitleSuffixPerYear".Translate(label),
        RateUnit.PerRealHour => "RWS_Rate_TitleSuffixPerRealHour".Translate(label),
        _ => "RWS_Rate_TitleSuffix".Translate(label),
    };

    /// <summary>U39: a FloatMenu button beside the mode toggle; only per-day rates have a unit choice.</summary>
    private static void DrawRateUnitButton(ControlRow controls)
    {
        var unitLabel = (string)"RWS_Rate_Unit".Translate(RateUnitLabel(rateUnit));
        var rect = controls.Next(unitLabel, 120f, 210f);
        var shown = unitLabel.Truncate(Math.Max(0f, rect.width - 12f));
        if (Widgets.ButtonText(rect, shown))
        {
            Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
            {
                new(RateUnitLabel(RateUnit.PerDay), () => SetRateUnit(RateUnit.PerDay)),
                new(RateUnitLabel(RateUnit.PerYear), () => SetRateUnit(RateUnit.PerYear)),
                new(RateUnitLabel(RateUnit.PerRealHour), () => SetRateUnit(RateUnit.PerRealHour)),
            }));
        }
        var tip = (string)"RWS_Rate_UnitTip".Translate();
        TooltipHandler.TipRegion(rect, shown != unitLabel ? unitLabel + "\n" + tip : tip);
    }

    private static void DrawSelector(Rect rect, StatsView view, Choice[] eligibleChoices, ViewPreferences preferences, Action persist)
    {
        var label = (string)"RWS_Rankings_Search".Translate();
        Widgets.Label(new Rect(rect.x, rect.y, rect.width, 23f), label.Truncate(rect.width));
        var newSearch = (Widgets.TextField(new Rect(rect.x, rect.y + 25f, rect.width, 27f), search) ?? "");
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
                new("RWS_Details_AllCategories".Translate(), () => SetCategory(null)),
            };
            menu.AddRange(eligibleChoices.Where(c => c.Category is not null).Select(c => c.Category!.Value).Distinct()
                .Select(cat => new FloatMenuOption(DetailsTab.CategoryLabel(cat), () => SetCategory(cat))));
            Find.WindowStack.Add(new FloatMenu(menu));
        }
        if (shownCategory != categoryText)
            TooltipHandler.TipRegion(catRect, categoryText);

        var trimmed = search.Trim();
        if (priorSearch != trimmed || priorCategory != category || priorPerDay != perDayMode)
        {
            priorSearch = trimmed;
            priorCategory = category;
            priorPerDay = perDayMode;
            // U33: identical syntax to DetailsTab (AND terms, '-' exclusions) over label, key and MOD source.
            var query = SearchQuery.Parse(trimmed);
            filtered = eligibleChoices.Where(c => (category is null || c.Category == category)
                && query.Matches(c.Label, c.Key, c.Source)).ToArray();
        }

        var activeFilters = FilterSummary.CountActiveFilters(perDayMode, category is not null, false, trimmed);
        var statusRow = new ControlRow(new Rect(rect.x, rect.y + 87f, rect.width, 24f));
        var countText = activeFilters > 0
            ? (string)"RWS_Filter_Shown".Translate(filtered.Length, eligibleChoices.Length) + " · " + (string)"RWS_Filter_ActiveCount".Translate(activeFilters)
            : (string)"RWS_Filter_Shown".Translate(filtered.Length, eligibleChoices.Length);
        var countRect = statusRow.Next(countText, 85f, rect.width);
        string shownCountText;
        using (UiScopes.GuiState())
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(0.8f, 0.8f, 0.8f);
            shownCountText = countText.Truncate(countRect.width);
            Widgets.Label(countRect, shownCountText);
        }
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
                perDayMode = false;
                if (selectedKey is not null)
                    descending = LeaderboardBuilder.DefaultDescending(MetricLookup.Definition(view, selectedKey));
                choicesScroll = Vector2.zero;
                priorSearch = null;
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
        using (UiScopes.ScrollView(viewport, ref choicesScroll, new Rect(0, 0, totalWidth, filtered.Length * 30f)))
        {
            var first = Math.Max(0, (int)(choicesScroll.y / 30f));
            var last = Math.Min(filtered.Length - 1, (int)((choicesScroll.y + viewport.height) / 30f) + 1);
            for (var i = first; i <= last; i++)
            {
                var option = filtered[i];
                var star = new Rect(0, i * 30f, 26f, 28f);
                var saved = preferences.IsFavoriteMetric(option.Key);
                if (Widgets.ButtonText(star, saved ? "★" : "☆"))
                {
                    if (preferences.ToggleFavoriteMetric(option.Key))
                        persist();
                }
                var button = new Rect(30f, i * 30f, Math.Max(0f, totalWidth - 30f), 28f);
                var isSelected = string.Equals(option.Key, selectedKey, StringComparison.Ordinal);
                if (isSelected)
                    Widgets.DrawBoxSolid(button, new Color(0.26f, 0.44f, 0.56f, 0.35f));
                if (Widgets.ButtonText(button, option.Label.Truncate(Math.Max(0f, button.width - 10f))))
                {
                    selectedKey = option.Key;
                    descending = perDayMode || LeaderboardBuilder.DefaultDescending(MetricLookup.Definition(view, option.Key));
                    tableScroll = Vector2.zero;
                }
                if (isSelected)
                    Widgets.DrawBoxSolid(new Rect(button.x, button.y + 2f, 3f, button.height - 4f), new Color(0.45f, 0.78f, 0.94f, 0.95f));
                TooltipHandler.TipRegion(star, "RWS_Fav_StarTip".Translate());
                TooltipHandler.TipRegion(button, option.Source is null ? option.Label
                    : option.Label + "\n" + option.Source);
            }
        }
    }

    internal static string BasicValue(string key, double n)
    {
        var (_, grouping) = Dialog_RimworldStatistics.DisplayOptions();
        var number = DisplayFormat.Number(n, 1, grouping);
        if (key == BasicMetrics.Days)
            return DetailsTab.DayCount(n);
        if (key == BasicMetrics.RealPlayHours)
            return "RWS_HoursValue".Translate(number);
        return DisplayFormat.WholeNumber(n, grouping);
    }
}
