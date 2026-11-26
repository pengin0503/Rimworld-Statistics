using System.Globalization;
using System.Text;
using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>
/// The numeric statistics (numeric expansion design §4): all colonies or one colony, a category filter, a search over the
/// shown names, and sorting by name or value. Numbers only. The rows are built from the cached immutable
/// <see cref="NumericView"/> when an input changes; drawing touches only the visible rows.
/// </summary>
public static class DetailsTab
{
    private const float ControlHeight = 30f;
    private static ColumnPlan? tablePlan;
    private static ViewPreferences? favoritePreferences;
    private static Action? persistFavorites;
    private static float tableWidth;
    private static object? tableLanguage;
    // The widest state and count texts of the current rows (headers included), measured when the rows are built.
    private static float stateTextWidth;
    private static float countTextWidth;
    private static (string Text, string? Full)[][] cellCache = [];
    private static Func<string>?[] tipCache = [];
    private static Func<string>?[] chartTipCache = [];
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private sealed record Line(string Label, string Value, string State, string Count, Func<string> Tip, int TipId, int Depth, string? ExpandKey, MetricVisualKind VisualKind, string? MetricKey, string? ModSource, bool SupportsColonyHistory);

    private sealed class LazyTip(Func<string> build)
    {
        private string? _text;
        public string Get() => _text ??= build();
    }

    private static int buildVersion;

    private enum DetailsViewMode { Lifetime, Other, All }
    private enum DetailsStatusFilter { All, Measured, Partial, Missing, Experimental }

    private sealed record Inputs(NumericView View, StatsResult Stats, bool PerColony, string? Colony, MetricCategory? Category, string Search,
        bool ByValue, bool Ascending, int ExpandedVersion, DetailsViewMode Mode, DetailsStatusFilter StatusFilter, bool GroupDerived,
        MetricSourceFilter SourceFilter, bool HideZero, object? Language, string ColonyNameSignature);

    private readonly record struct PersistedState(DetailsViewMode ViewMode, bool PerColony, string? ColonyKey, MetricCategory? Category,
        string Search, bool ByValue, bool Ascending, DetailsStatusFilter StatusFilter, bool GroupDerived,
        MetricSourceFilter SourceFilter, bool HideZero, int ExpandedVersion);

    // Show all metrics immediately, including optional MOD outputs and non-lifetime readings.
    private static DetailsViewMode viewMode = DetailsViewMode.All;
    private static bool perColony;
    private static bool comparing;
    private static string? colonyKey;
    private static MetricCategory? category;
    private static string search = "";
    private static bool byValue;
    private static bool ascending = true;
    private static DetailsStatusFilter statusFilter;
    private static bool groupDerived = true;
    private static MetricSourceFilter sourceFilter;
    private static bool hideZero;
    private static readonly HashSet<string> Expanded = new(StringComparer.Ordinal);
    private static int expandedVersion;
    private static Vector2 scroll;
    private static Inputs? builtFor;
    private static bool restorePending;
    private static PersistedState? lastPersistedState;
    private static List<Line> lines = new();
    // Row tops, rebuilt with the rows: a MOD-owned statistic has a second line for its provider (#306).
    private static RowOffsets offsets = RowOffsets.Empty;
    private static int totalMetricCount;
    private static int matchedMetricCount;

    /// <summary>U4: the statistics window asks for a restore on its first draw after opening.</summary>
    internal static void RequestRestore() => restorePending = true;

    public static void Draw(Rect rect, StatsView view, ProfileSession session, ViewPreferences? favorites = null, Action? saveFavorites = null, string? scopeName = null)
    {
        favoritePreferences = favorites;
        persistFavorites = saveFavorites;
        var numeric = view.Numeric;
        if (restorePending)
        {
            restorePending = false;
            if (favorites is not null)
                RestoreState(favorites, numeric);
            lastPersistedState = CaptureState();
        }
        if (numeric.Colonies.Count == 0)
        {
            PersistViewStateIfChanged();
            Widgets.Label(rect, "RWS_NoData".Translate());
            return;
        }
        if (!comparing && perColony && (colonyKey is null || numeric.Colonies.All(c => c.Key != colonyKey)))
            colonyKey = numeric.Colonies.OrderBy(c => Name(view.Stats, c.Key), StringComparer.CurrentCulture).First().Key;

        var y = rect.y;
        y += DrawControls(new Rect(rect.x, y, rect.width, ControlHeight), view, scopeName) + 4f;

        if (!comparing)
        {
            float noteHeight;
            using (UiScopes.GuiState())
            {
                GUI.color = new Color(0.8f, 0.8f, 0.8f);
                var note = (string)"RWS_Details_Note".Translate(view.Stats.ColonyCount, view.Stats.ExcludedColonyCount);
                if (view.Stats.OutOfScopeColonyCount > 0)
                    note += " " + "RWS_Groups_OutOfScope".Translate(view.Stats.OutOfScopeColonyCount);
                if (numeric.Unattributed > 0)
                    note += " " + "RWS_Details_Unattributed".Translate(numeric.Unattributed);
                noteHeight = Text.CalcHeight(note, rect.width);
                Widgets.Label(new Rect(rect.x, y, rect.width, noteHeight), note);
            }
            y += noteHeight + 4f;
        }

        const float filterHeight = 25f;
        var filterRow = new ControlRow(new Rect(rect.x, y, rect.width, filterHeight));
        var filterLabel = StatusFilterLabel(statusFilter);
        var filterRect = filterRow.Next(filterLabel, 140f, Math.Min(rect.width, 230f));
        var shownFilter = filterLabel.Truncate(Math.Max(0f, filterRect.width - 10f));
        if (shownFilter != filterLabel)
            TooltipHandler.TipRegion(filterRect, filterLabel);
        if (Widgets.ButtonText(filterRect, shownFilter))
        {
            Find.WindowStack.Add(new FloatMenu(Enum.GetValues<DetailsStatusFilter>()
                .Select(value => new FloatMenuOption(StatusFilterLabel(value), () => SetStatusFilter(value))).ToList()));
        }
        if (!comparing)
        {
            if (!perColony)
            {
                var canGroupNow = DerivedMetricGroups.ShouldGroup(true, perColony, byValue, search);
                string groupLabel = "RWS_Details_GroupDerived".Translate();
                var toggleRect = filterRow.Next(groupLabel + "   ", 145f, Math.Min(rect.width, 260f));
                var nextGroup = groupDerived;
                using (UiScopes.GuiState())
                {
                    if (!canGroupNow)
                        GUI.color = new Color(0.65f, 0.65f, 0.65f);
                    // Greyed out while searching or sorting by value, and then it does not take clicks either (#336).
                    Widgets.CheckboxLabeled(toggleRect, groupLabel.Truncate(Math.Max(0f, toggleRect.width - 30f)), ref nextGroup,
                        disabled: !canGroupNow);
                }
                TooltipHandler.TipRegion(toggleRect,
                    canGroupNow ? "RWS_Details_GroupDerivedTip".Translate() : "RWS_Details_GroupDerivedFlatTip".Translate());
                if (nextGroup != groupDerived)
                {
                    groupDerived = nextGroup;
                    scroll = Vector2.zero;
                }
            }
            // U18: measured zeros only; rows without a measured number stay governed by the status filter.
            string hideLabel = "RWS_Details_HideZero".Translate();
            var hideRect = filterRow.Next(hideLabel + "   ", 130f, Math.Min(rect.width, 230f));
            var nextHideZero = hideZero;
            Widgets.CheckboxLabeled(hideRect, hideLabel.Truncate(Math.Max(0f, hideRect.width - 30f)), ref nextHideZero);
            TooltipHandler.TipRegion(hideRect, "RWS_Details_HideZeroTip".Translate());
            if (nextHideZero != hideZero)
            {
                hideZero = nextHideZero;
                scroll = Vector2.zero;
            }
        }

        var language = LanguageDatabase.activeLanguage;
        var colonyNameSig = ColonyPresentation.Signature(favorites);
        if (!comparing)
        {
            var inputs = new Inputs(numeric, view.Stats, perColony, colonyKey, category, search, byValue, ascending, expandedVersion, viewMode, statusFilter, groupDerived, sourceFilter, hideZero, language, colonyNameSig);
            if (builtFor != inputs)
            {
                if (builtFor is { } previous && previous with { ExpandedVersion = inputs.ExpandedVersion } != inputs)
                    scroll = Vector2.zero;
                builtFor = inputs;
                SetLines(Build(inputs));
            }
        }

        var activeFilters = FilterSummary.CountActiveFilters(
            viewMode != DetailsViewMode.All, category is not null, statusFilter != DetailsStatusFilter.All,
            sourceFilter != MetricSourceFilter.All, hideZero, search);
        var summaryText = comparing
            ? (activeFilters > 0 ? (string)"RWS_Filter_ActiveCount".Translate(activeFilters) : "")
            : activeFilters > 0
                ? (string)"RWS_Filter_Shown".Translate(matchedMetricCount, totalMetricCount) + " · " + (string)"RWS_Filter_ActiveCount".Translate(activeFilters)
                : (string)"RWS_Filter_Shown".Translate(matchedMetricCount, totalMetricCount);
        if (summaryText.Length > 0)
        {
            var summaryRect = filterRow.Next(summaryText, 90f, Math.Min(rect.width, 250f));
            using (UiScopes.GuiState())
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = new Color(0.8f, 0.8f, 0.8f);
                Widgets.Label(summaryRect, summaryText.Truncate(summaryRect.width));
            }
            TooltipHandler.TipRegion(summaryRect, summaryText);
        }
        if (activeFilters > 0)
        {
            var resetLabel = (string)"RWS_Filter_Reset".Translate();
            var resetRect = filterRow.Next(resetLabel, 95f, Math.Min(rect.width, 175f));
            if (Widgets.ButtonText(resetRect, resetLabel.Truncate(Math.Max(0f, resetRect.width - 10f))))
                ResetFilters(numeric, view.Stats, language);
        }

        y += filterRow.Height + 4f;
        if (comparing)
        {
            var filter = (viewMode, category, search: search.Trim(), statusFilter, sourceFilter, hideZero);
            var filterQuery = SearchQuery.Parse(filter.search);
            ComparisonPanel.Draw(new Rect(rect.x, y, rect.width, rect.yMax - y), session, view, favorites, filter,
                (definition, label) => MatchesMode(definition, filter.viewMode) && (filter.category is null || definition.Category == filter.category)
                    && MatchesSource(definition, filter.sourceFilter) && MatchesSearch(definition, label, filterQuery),
                row => MatchesStatus(row, filter.statusFilter) && !IsHiddenZero(row.Value, filter.hideZero));
            PersistViewStateIfChanged();
            return;
        }

        var available = Math.Max(0, rect.width - 16f);
        if (tablePlan is null || tableWidth != available || !ReferenceEquals(tableLanguage, language))
        {
            tableWidth = available;
            tableLanguage = language;
            tablePlan = DetailsTableLayout.PlanColumns(available, stateTextWidth, countTextWidth);
            Array.Clear(cellCache);
            Array.Clear(tipCache);
            Array.Clear(chartTipCache);
        }
        var width = tablePlan.TotalWidth;
        using (UiScopes.Group(new Rect(rect.x, y, available, DetailsRowHeights.Header)))
        {
            DrawHeader(new Rect(-scroll.x, 0, width, DetailsRowHeights.Header));
        }
        y += DetailsRowHeights.Header;
        var outRect = new Rect(rect.x, y, rect.width, rect.yMax - y);
        if (outRect.height <= 0)
        {
            PersistViewStateIfChanged();
            return;
        }
        if (lines.Count == 0)
        {
            Widgets.Label(new Rect(outRect.x, outRect.y, outRect.width, 28f), "RWS_Details_NoMatch".Translate());
            if (activeFilters > 0 && outRect.height >= 64f
                && Widgets.ButtonText(new Rect(outRect.x, outRect.y + 32f, Math.Min(180f, outRect.width), 28f), "RWS_Filter_Reset".Translate()))
            {
                ResetFilters(numeric, view.Stats, language);
            }
            PersistViewStateIfChanged();
            return;
        }
        var selectedColonyStats = perColony && colonyKey is not null
            ? view.Stats.Colonies.FirstOrDefault(c => c.Key == colonyKey)
            : null;
        var viewRect = new Rect(0f, 0f, width, offsets.Total);
        using (UiScopes.ScrollView(outRect, ref scroll, viewRect))
        {
            var (first, last) = offsets.Visible(scroll.y, outRect.height);
            for (var i = first; i <= last; i++)
                DrawLine(new Rect(0f, offsets.Top(i), width, offsets.Height(i)), lines[i], i, view, selectedColonyStats, scopeName);
        }
        PersistViewStateIfChanged();
    }

    private static void SetLines(List<Line> built)
    {
        lines = built;
        offsets = new RowOffsets(built.Select(line => DetailsRowHeights.Row(line.ModSource is not null)).ToArray());
        cellCache = new (string, string?)[built.Count][];
        tipCache = new Func<string>?[built.Count];
        chartTipCache = new Func<string>?[built.Count];
        // The state and count columns fit the widest text of the rows just built, so the value column gets the rest.
        stateTextWidth = WidestText(StateHeader(), built.Select(line => line.State));
        countTextWidth = WidestText(CountHeader(), built.Select(line => line.Count));
        tablePlan = null;
    }

    private static float WidestText(string header, IEnumerable<string> texts)
    {
        var widest = Text.CalcSize(header).x;
        foreach (var text in new HashSet<string>(texts, StringComparer.Ordinal))
            widest = Math.Max(widest, Text.CalcSize(text).x);
        return widest;
    }

    private static string StateHeader() => "RWS_Details_Col_State".Translate();

    private static string CountHeader() => perColony
        ? "RWS_Details_Col_Through".Translate()
        : "RWS_Details_Col_Colonies".Translate();

    private static void ResetFilters(NumericView numeric, StatsResult stats, object? language)
    {
        viewMode = DetailsViewMode.All;
        category = null;
        statusFilter = DetailsStatusFilter.All;
        sourceFilter = MetricSourceFilter.All;
        hideZero = false;
        search = "";
        scroll = Vector2.zero;
        if (comparing)
        {
            ComparisonPanel.ResetScroll();
        }
        else
        {
            var inputs = new Inputs(numeric, stats, perColony, colonyKey, category, search, byValue, ascending, expandedVersion, viewMode, statusFilter, groupDerived, sourceFilter, hideZero, language, ColonyPresentation.Signature(favoritePreferences));
            builtFor = inputs;
            SetLines(Build(inputs));
        }
        PersistViewStateIfChanged();
    }

    private static void RestoreState(ViewPreferences prefs, NumericView numeric)
    {
        var mode = (DetailsViewMode)prefs.DetailsViewMode;
        viewMode = Enum.IsDefined(mode) ? mode : DetailsViewMode.All;
        perColony = prefs.DetailsPerColony;
        // Comparison mode is not part of the persisted view state; a fresh window starts in the normal view.
        comparing = false;
        colonyKey = prefs.DetailsColonyKey is { } savedColony && numeric.Colonies.Any(c => c.Key == savedColony)
            ? savedColony : null;
        category = prefs.DetailsCategory is { } savedCategory && Enum.IsDefined(typeof(MetricCategory), savedCategory)
            ? (MetricCategory)savedCategory : null;
        search = prefs.DetailsSearch ?? "";
        byValue = prefs.DetailsByValue;
        ascending = prefs.DetailsAscending;
        var status = (DetailsStatusFilter)prefs.DetailsStatusFilter;
        statusFilter = Enum.IsDefined(status) ? status : DetailsStatusFilter.All;
        groupDerived = prefs.DetailsGroupDerived;
        var source = (MetricSourceFilter)prefs.DetailsSourceFilter;
        sourceFilter = Enum.IsDefined(source) ? source : MetricSourceFilter.All;
        hideZero = prefs.DetailsHideZero;
        Expanded.Clear();
        foreach (var key in (prefs.DetailsExpandedKeys ?? new List<string>()).Take(ViewPreferences.MaxExpandedKeys))
            if (!string.IsNullOrWhiteSpace(key))
                Expanded.Add(key);
        expandedVersion++;
        scroll = Vector2.zero;
        builtFor = null;
    }

    private static PersistedState CaptureState() => new(viewMode, perColony, colonyKey, category, search, byValue,
        ascending, statusFilter, groupDerived, sourceFilter, hideZero, expandedVersion);

    private static void PersistViewStateIfChanged()
    {
        var state = CaptureState();
        if (lastPersistedState == state)
            return;
        lastPersistedState = state;
        if (favoritePreferences is not { } prefs)
            return;
        prefs.DetailsViewMode = (int)viewMode;
        prefs.DetailsPerColony = perColony;
        prefs.DetailsColonyKey = colonyKey;
        prefs.DetailsCategory = category is { } selectedCategory ? (int)selectedCategory : null;
        prefs.DetailsSearch = search ?? "";
        prefs.DetailsByValue = byValue;
        prefs.DetailsAscending = ascending;
        prefs.DetailsStatusFilter = (int)statusFilter;
        prefs.DetailsGroupDerived = groupDerived;
        prefs.DetailsSourceFilter = (int)sourceFilter;
        prefs.DetailsHideZero = hideZero;
        prefs.DetailsExpandedKeys = Expanded.Take(ViewPreferences.MaxExpandedKeys).ToList();
        persistFavorites?.Invoke();
    }

    // FloatMenu and picker callbacks run after this tab's Draw, so they save at once instead of waiting for the next frame.
    private static void SetViewMode(DetailsViewMode mode)
    {
        viewMode = mode;
        scroll = Vector2.zero;
        PersistViewStateIfChanged();
    }

    private static void SetDisplay(bool compare, bool singleColony)
    {
        (comparing, perColony) = (compare, singleColony);
        scroll = Vector2.zero;
        PersistViewStateIfChanged();
    }

    private static void SetColonyKey(string key)
    {
        colonyKey = key;
        PersistViewStateIfChanged();
    }

    private static void SetCategory(MetricCategory? value)
    {
        category = value;
        scroll = Vector2.zero;
        PersistViewStateIfChanged();
    }

    private static void SetSourceFilter(MetricSourceFilter value)
    {
        sourceFilter = value;
        scroll = Vector2.zero;
        PersistViewStateIfChanged();
    }

    private static void SetStatusFilter(DetailsStatusFilter value)
    {
        statusFilter = value;
        scroll = Vector2.zero;
        PersistViewStateIfChanged();
    }

    // #336: grouping/navigation controls are paused while searching or sorting by value, exactly like the group checkbox.
    private static bool CanExpandAll => !comparing && !byValue && string.IsNullOrWhiteSpace(search);

    /// <summary>U41: expand or collapse every derived group and breakdown row currently reachable from the built rows.</summary>
    private static void SetAllExpanded(bool expand)
    {
        if (!CanExpandAll || builtFor is null)
            return;
        if (!expand)
        {
            Expanded.Clear();
        }
        else
        {
            // Rebuilding reveals nested breakdown keys one level at a time; a few passes reach every visible depth.
            for (var pass = 0; pass < 4; pass++)
            {
                var added = false;
                foreach (var builtLine in Build(builtFor with { ExpandedVersion = expandedVersion }))
                    if (builtLine.ExpandKey is { } key)
                        added |= Expanded.Add(key);
                if (!added)
                    break;
            }
        }
        expandedVersion++;
        scroll = Vector2.zero;
        PersistViewStateIfChanged();
    }

    private static float DrawControls(Rect rect, StatsView view, string? scopeName)
    {
        var controls = new ControlRow(rect);
        bool Button(string text, float max)
        {
            var button = controls.Next(text, 80f, max);
            var shown = text.Truncate(Math.Max(0, button.width - 12f));
            if (shown != text)
                TooltipHandler.TipRegion(button, text);
            return Widgets.ButtonText(button, shown);
        }

        if (Button(ViewModeLabel(viewMode), 250f))
        {
            Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
            {
                new(ViewModeLabel(DetailsViewMode.Lifetime), () => SetViewMode(DetailsViewMode.Lifetime)),
                new(ViewModeLabel(DetailsViewMode.Other), () => SetViewMode(DetailsViewMode.Other)),
                new(ViewModeLabel(DetailsViewMode.All), () => SetViewMode(DetailsViewMode.All)),
            }));
        }

        var displayLabel = comparing ? (string)"RWS_Compare_Mode".Translate()
            : perColony ? (string)"RWS_Details_PerColony".Translate() : (string)"RWS_Details_AllColonies".Translate();
        if (Button(displayLabel, 240f))
            Find.WindowStack.Add(new FloatMenu(
            [
                new FloatMenuOption("RWS_Details_AllColonies".Translate(), () => SetDisplay(false, false)),
                new FloatMenuOption("RWS_Details_PerColony".Translate(), () => SetDisplay(false, true)),
                new FloatMenuOption("RWS_Compare_Mode".Translate(), () => SetDisplay(true, false)),
            ]));
        if (!comparing && perColony && colonyKey is { } selected)
        {
            if (Button(Name(view.Stats, selected), 260f))
            {
                var selectableKeys = view.Numeric.Colonies.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
                var selectable = view.Stats.Colonies.Where(s => selectableKeys.Contains(s.Key)).ToArray();
                UiScopes.OpenOrReplaceWindow(new ColonyPickerDialog(
                    selectable,
                    favoritePreferences,
                    picked => SetColonyKey(picked.Key),
                    selectedColonyKey: selected));
            }
            var reportLabel = (string)"RWS_Report_Open".Translate();
            var reportRect = controls.Next(reportLabel, 120f, 220f);
            if (Widgets.ButtonText(reportRect, reportLabel.Truncate(Math.Max(0f, reportRect.width - 12f))))
                UiScopes.OpenOrReplaceWindow(new Dialog_ColonySummaryReport(view, selected, scopeName));
            TooltipHandler.TipRegion(reportRect, "RWS_Report_OpenTip".Translate());
        }
        var scatterLabel = (string)"RWS_Scatter_Open".Translate();
        var scatterRect = controls.Next(scatterLabel, 110f, 200f);
        if (Widgets.ButtonText(scatterRect, scatterLabel.Truncate(Math.Max(0f, scatterRect.width - 12f))))
            UiScopes.OpenOrReplaceWindow(new Dialog_MetricScatterPlot(view, null, null, scopeName));
        TooltipHandler.TipRegion(scatterRect, "RWS_Scatter_OpenTip".Translate());
        if (Button(category is { } c0 ? CategoryLabel(c0) : (string)"RWS_Details_AllCategories".Translate(), 240f))
        {
            var options = new List<FloatMenuOption> { new("RWS_Details_AllCategories".Translate(), () => SetCategory(null)) };
            options.AddRange(view.Numeric.Colonies.SelectMany(c => c.Rows).Select(r => r.Definition.Category)
                .Concat(view.Numeric.Overall.Select(r => r.Definition.Category)).Distinct()
                .Select(c => new FloatMenuOption(CategoryLabel(c), () => SetCategory(c))));
            Find.WindowStack.Add(new FloatMenu(options));
        }
        // U5: source filter. MOD wins over DLC, which wins over Vanilla (see MatchesSource).
        if (Button(SourceFilterLabel(sourceFilter), 170f))
        {
            Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
            {
                new(SourceFilterLabel(MetricSourceFilter.All), () => SetSourceFilter(MetricSourceFilter.All)),
                new(SourceFilterLabel(MetricSourceFilter.Vanilla), () => SetSourceFilter(MetricSourceFilter.Vanilla)),
                new(SourceFilterLabel(MetricSourceFilter.Dlc), () => SetSourceFilter(MetricSourceFilter.Dlc)),
                new(SourceFilterLabel(MetricSourceFilter.Mod), () => SetSourceFilter(MetricSourceFilter.Mod)),
            }));
        }
        // U41: mirror the disabled group checkbox while searching or sorting by value (#336).
        if (!comparing)
        {
            var canExpandAll = CanExpandAll;
            var expandLabel = (string)"RWS_Details_ExpandAll".Translate();
            var collapseLabel = (string)"RWS_Details_CollapseAll".Translate();
            var expandRect = controls.Next(expandLabel, 95f, 175f);
            var collapseRect = controls.Next(collapseLabel, 105f, 190f);
            using (UiScopes.GuiState())
            {
                if (!canExpandAll)
                    GUI.color = new Color(0.65f, 0.65f, 0.65f);
                if (Widgets.ButtonText(expandRect, expandLabel.Truncate(Math.Max(0f, expandRect.width - 12f))))
                    SetAllExpanded(true);
                if (Widgets.ButtonText(collapseRect, collapseLabel.Truncate(Math.Max(0f, collapseRect.width - 12f))))
                    SetAllExpanded(false);
            }
            if (!canExpandAll)
            {
                var flatTip = (string)"RWS_Details_GroupDerivedFlatTip".Translate();
                TooltipHandler.TipRegion(expandRect, flatTip);
                TooltipHandler.TipRegion(collapseRect, flatTip);
            }
        }
        var searchLabel = (string)"RWS_Details_Search".Translate();
        var labelWidth = Math.Min(Text.CalcSize(searchLabel).x + 6f, Math.Max(0, rect.width - 120f));
        var searchArea = controls.Next(labelWidth + 120f, fill: true);
        using (UiScopes.GuiState())
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(searchArea.x, searchArea.y, labelWidth, searchArea.height), searchLabel.Truncate(labelWidth));
        }
        var field = new Rect(searchArea.x + labelWidth, searchArea.y + 2f, searchArea.width - labelWidth, searchArea.height - 4f);
        search = Widgets.TextField(field, search) ?? "";
        return controls.Height;
    }

    private static bool MatchesStatus(NumericRow row, DetailsStatusFilter filter) => filter switch
    {
        DetailsStatusFilter.Measured => MetricDisplayPolicy.HasMeasuredNumber(row),
        DetailsStatusFilter.Partial => MetricStatusClassifier.IsPartial(row),
        DetailsStatusFilter.Missing => !MetricDisplayPolicy.HasMeasuredNumber(row) && row.Value.Coverage != MetricCoverage.NotApplicable,
        DetailsStatusFilter.Experimental => MetricStatusClassifier.IsExperimental(row),
        _ => true,
    };

    private static bool MatchesMode(MetricDefinition definition, DetailsViewMode mode) => mode switch
    {
        DetailsViewMode.Lifetime => MetricDisplayPolicy.IsLifetimePrimaryRow(definition),
        DetailsViewMode.Other => !MetricDisplayPolicy.IsLifetimeCumulative(definition),
        _ => true,
    };

    /// <summary>U33: the same query syntax as the Rankings selector (AND terms, <c>-term</c> exclusions).</summary>
    private static bool MatchesSearch(MetricDefinition definition, string label, SearchQuery.Parsed query)
        => query.IsEmpty || query.Matches(SearchCandidates.ForMetric(definition, label));

    private static bool MatchesSource(MetricDefinition definition, MetricSourceFilter filter) => filter switch
    {
        // A MOD-owned statistic that also requires a DLC counts as MOD.
        MetricSourceFilter.Mod => !string.IsNullOrWhiteSpace(definition.OwnerPackageId),
        MetricSourceFilter.Dlc => string.IsNullOrWhiteSpace(definition.OwnerPackageId)
            && !string.IsNullOrWhiteSpace(definition.RequiredDlc),
        MetricSourceFilter.Vanilla => string.IsNullOrWhiteSpace(definition.OwnerPackageId)
            && string.IsNullOrWhiteSpace(definition.RequiredDlc),
        _ => true,
    };

    private static bool IsHiddenZero(NumericValue value, bool hide)
        => hide && value.Number is { } number && number == 0.0;

    private static string SourceFilterLabel(MetricSourceFilter filter) => filter switch
    {
        MetricSourceFilter.Vanilla => "RWS_Details_SourceVanilla".Translate(),
        MetricSourceFilter.Dlc => "RWS_Details_SourceDLC".Translate(),
        MetricSourceFilter.Mod => "RWS_Details_SourceMOD".Translate(),
        _ => "RWS_Details_SourceAll".Translate(),
    };

    private static string StatusFilterLabel(DetailsStatusFilter filter) => filter switch
    {
        DetailsStatusFilter.Measured => "RWS_Details_StatusMeasured".Translate(),
        DetailsStatusFilter.Partial => "RWS_Details_StatusPartial".Translate(),
        DetailsStatusFilter.Missing => "RWS_Details_StatusMissing".Translate(),
        DetailsStatusFilter.Experimental => "RWS_Details_StatusExperimental".Translate(),
        _ => "RWS_Details_StatusAll".Translate(),
    };

    private static string ViewModeLabel(DetailsViewMode mode) => mode switch
    {
        DetailsViewMode.Lifetime => "RWS_Details_ViewLifetime".Translate(),
        DetailsViewMode.Other => "RWS_Details_ViewOther".Translate(),
        _ => "RWS_Details_ViewAll".Translate(),
    };

    private static object? headerLanguage;
    private static bool headerByValue;
    private static bool headerAscending;
    private static string[]? cachedHeaders;

    private static void DrawHeader(Rect rect)
    {
        var lang = LanguageDatabase.activeLanguage;
        if (cachedHeaders is null || !ReferenceEquals(headerLanguage, lang)
            || headerByValue != byValue || headerAscending != ascending)
        {
            headerLanguage = lang;
            headerByValue = byValue;
            headerAscending = ascending;
            cachedHeaders =
            [
                (string)"RWS_Details_Col_Name".Translate() + (!byValue ? (ascending ? " ▲" : " ▼") : ""),
                (string)"RWS_Details_Col_Value".Translate() + (byValue ? (ascending ? " ▲" : " ▼") : ""),
                StateHeader(),
                CountHeader(),
            ];
        }
        var headers = cachedHeaders;
        var x = rect.x;
        using (UiScopes.GuiState())
        {
            GUI.color = new Color(0.8f, 0.8f, 0.8f);
            for (var c = 0; c < headers.Length; c++)
            {
                var cell = new Rect(x, rect.y, tablePlan!.Widths[c], rect.height);
                var metrics = DetailsTableLayout.MeasureColumn(c, x, cell.width, 0f, isMetricRow: false, hasExpand: false);
                var labelRect = new Rect(x + metrics.LeftOffset, cell.y, metrics.ContentWidth, cell.height);
                Text.Anchor = DetailsTableLayout.ColumnAlignment(c) == CellAlignment.Right
                    ? TextAnchor.MiddleRight
                    : TextAnchor.MiddleLeft;
                var shown = headers[c].Truncate(Math.Max(1f, labelRect.width));
                Widgets.Label(labelRect, shown);
                if (c <= 1)
                {
                    Widgets.DrawHighlightIfMouseover(cell);
                    if (Widgets.ButtonInvisible(cell))
                    {
                        var wantValue = c == 1;
                        if (byValue == wantValue)
                            ascending = !ascending;
                        else
                            (byValue, ascending) = (wantValue, !wantValue);   // values start largest first
                    }
                }
                if (c == 3 && perColony)
                {
                    var tip = shown != headers[c] ? headers[c] + "\n" + (string)"RWS_Details_ThroughTip".Translate() : (string)"RWS_Details_ThroughTip".Translate();
                    TooltipHandler.TipRegion(cell, tip);
                }
                else if (shown != headers[c])
                {
                    TooltipHandler.TipRegion(cell, headers[c]);
                }
                x += cell.width;
            }
        }
    }

    private static void MutateFavorites(ViewPreferences? fallbackPrefs, Action? fallbackSave, Action<ViewPreferences> mutate)
    {
        if (RimworldStatisticsMod.Settings is not null)
        {
            Dialog_RimworldStatistics.UpdateActivePreferences(mutate);
            if (fallbackPrefs is not null && !ReferenceEquals(fallbackPrefs, Dialog_RimworldStatistics.ActivePreferences))
                mutate(fallbackPrefs);
            return;
        }
        if (fallbackPrefs is not null)
        {
            mutate(fallbackPrefs);
            fallbackSave?.Invoke();
        }
    }

    private static void DrawLine(Rect rect, Line line, int index, StatsView view, ColonyStats? selectedColonyStats, string? scopeName)
    {
        if (index % 2 == 1)
            Widgets.DrawHighlight(rect, 0.25f);
        Widgets.DrawHighlightIfMouseover(rect);
        var widths = tablePlan!.Widths;
        var hit = DerivedMetricGroups.ComputeHitboxes(rect.x, rect.width, line.Depth, line.MetricKey is not null, line.ExpandKey is not null);
        var nameMetrics = DetailsTableLayout.MeasureColumn(0, rect.x, widths[0], hit.Indent, line.MetricKey is not null, line.ExpandKey is not null);
        if (cellCache[index] is not { } cells)
        {
            var values = new[]
            {
                (line.ExpandKey is { } k ? (Expanded.Contains(k) ? "▾ " : "▸ ") : "") + line.Label,
                line.Value, line.State, line.Count,
            };
            cells = new (string Text, string? Full)[values.Length];
            var cx = rect.x;
            for (var c = 0; c < values.Length; c++)
            {
                var col = c == 0 ? nameMetrics : DetailsTableLayout.MeasureColumn(c, cx, widths[c], hit.Indent, line.MetricKey is not null, line.ExpandKey is not null);
                var shown = values[c].Truncate(Math.Max(1f, col.ContentWidth));
                cells[c] = (shown, shown == values[c] ? null : values[c]);
                cx += widths[c];
            }
            cellCache[index] = cells;
            var fullText = string.Join("\n", cells.Where(c => c.Full is not null).Select(c => c.Full));
            var tip = fullText.Length == 0 ? line.Tip : new LazyTip(() => fullText + "\n\n" + line.Tip()).Get;
            var fullTip = line.MetricKey is null ? tip
                : new LazyTip(() => tip() + "\n\n" + (string)"RWS_Calc_Hint".Translate()).Get;
            tipCache[index] = fullTip;
            chartTipCache[index] = line.MetricKey is null ? null
                : new LazyTip(() => fullTip() + "\n\n" + "RWS_Chart_OpenTip".Translate()).Get;
        }
        var x = rect.x;
        using (UiScopes.GuiState())
        {
            for (var c = 0; c < cells.Length; c++)
            {
                var col = c == 0 ? nameMetrics : DetailsTableLayout.MeasureColumn(c, x, widths[c], hit.Indent, line.MetricKey is not null, line.ExpandKey is not null);
                var cell = new Rect(x + col.LeftOffset, rect.y, col.ContentWidth, rect.height);
                Text.Anchor = DetailsTableLayout.ColumnAlignment(c) == CellAlignment.Right
                    ? TextAnchor.MiddleRight
                    : TextAnchor.MiddleLeft;
                // Only the state column is colored; the state it names is never repeated in the value's color.
                GUI.color = c == 2 ? MetricVisualStyle.StateColor(line.VisualKind) : Color.white;
                if (c == 0 && line.ModSource is { } source)
                {
                    Widgets.Label(new Rect(cell.x, cell.y, cell.width, DetailsRowHeights.TitleLineHeight), cells[c].Text);
                    GUI.color = new Color(0.78f, 0.78f, 0.81f);
                    Widgets.Label(new Rect(cell.x, cell.y + DetailsRowHeights.SourceLineTop, cell.width, DetailsRowHeights.SourceLineHeight),
                        source.Truncate(cell.width));
                }
                else
                {
                    Widgets.Label(cell, cells[c].Text);
                }
                x += widths[c];
            }
        }
        // The star, disclosure triangle, and chart click area remain strictly disjoint.
        Rect? chartRect = !hit.Chart.IsEmpty
            ? new Rect(hit.Chart.X, rect.y, hit.Chart.Width, rect.height)
            : null;
        if (line.MetricKey is not null && !nameMetrics.ChartIcon.IsEmpty)
        {
            var iconRect = new Rect(
                nameMetrics.ChartIcon.X,
                rect.y + (rect.height - DetailsTableLayout.ChartIconSize) * 0.5f,
                nameMetrics.ChartIcon.Width,
                DetailsTableLayout.ChartIconSize);
            MetricVisualStyle.DrawMiniChartIcon(iconRect, chartRect is { } hoverArea && Mouse.IsOver(hoverArea));
        }
        // One tooltip per row: the same id keeps RimWorld from stacking a second copy,
        // and the chart hint is shown only over the area that opens the chart.
        var starRect = new Rect(hit.Star.X, rect.y + 2f, hit.Star.Width, rect.height - 4f);
        var supportsColonyFav = perColony && colonyKey is not null && line.SupportsColonyHistory;
        if (line.MetricKey is { } metricKey && favoritePreferences is not null && !hit.Star.IsEmpty)
        {
            var savedMetric = favoritePreferences.IsFavoriteMetric(metricKey);
            var savedColony = supportsColonyFav && colonyKey is { } activeKey
                && ColonyHistoryFavorites.IsFavorite(favoritePreferences, activeKey, selectedColonyStats?.AliasKeys, metricKey);

            if (Widgets.ButtonText(starRect, savedMetric || savedColony ? "★" : "☆"))
            {
                if (supportsColonyFav && colonyKey is { } targetColonyKey)
                {
                    var prefs = favoritePreferences;
                    var save = persistFavorites;
                    var colonyLabel = Name(view.Stats, targetColonyKey);
                    var aliases = selectedColonyStats?.AliasKeys;
                    Find.WindowStack.Add(new FloatMenu(
                    [
                        new FloatMenuOption(
                            savedMetric ? "RWS_ColonyFav_MenuMetricRemove".Translate() : "RWS_ColonyFav_MenuMetricAdd".Translate(),
                            () => MutateFavorites(prefs, save, p => p.ToggleFavoriteMetric(metricKey))),
                        new FloatMenuOption(
                            savedColony
                                ? "RWS_ColonyFav_MenuColonyRemove".Translate(colonyLabel)
                                : "RWS_ColonyFav_MenuColonyAdd".Translate(colonyLabel),
                            () => MutateFavorites(prefs, save, p =>
                                ColonyHistoryFavorites.Toggle(p, targetColonyKey, aliases, metricKey))),
                    ]));
                }
                else
                {
                    MutateFavorites(favoritePreferences, persistFavorites, p => p.ToggleFavoriteMetric(metricKey));
                }
            }
        }
        if (line.MetricKey is not null && favoritePreferences is not null && !hit.Star.IsEmpty && Mouse.IsOver(starRect))
            TooltipHandler.TipRegion(rect, () => supportsColonyFav ? "RWS_ColonyFav_StarMenuTip".Translate() : "RWS_Fav_StarTip".Translate(), line.TipId);
        else
            TooltipHandler.TipRegion(rect,
                chartRect is { } hintArea && Mouse.IsOver(hintArea) ? chartTipCache[index]! : tipCache[index]!, line.TipId);
        if (line.MetricKey is { } chartKey && chartRect is { } chartArea && Widgets.ButtonInvisible(chartArea))
            UiScopes.OpenOrReplaceWindow(new Dialog_MetricChart(view, chartKey, perColony ? colonyKey : null, scopeName));
        if (line.ExpandKey is { } key && !hit.Toggle.IsEmpty)
        {
            var toggle = new Rect(hit.Toggle.X, rect.y, hit.Toggle.Width, rect.height);
            if (Widgets.ButtonInvisible(toggle))
            {
                if (!Expanded.Remove(key))
                    Expanded.Add(key);
                expandedVersion++;
            }
        }
        // U21: every left-click area of a metric row already opens its chart (the star and triangle claim the rest),
        // so the calculation popover is offered through a right-click menu instead of stealing a left click.
        if (line.MetricKey is { } calcKey && Mouse.IsOver(rect) && Input.GetMouseButtonDown(1)
            && MetricRegistry.Find(calcKey) is { } calcDefinition)
        {
            var definition = calcDefinition;
            Find.WindowStack.Add(new FloatMenu(
            [
                new FloatMenuOption((string)"RWS_Calc_Menu".Translate(), () => OpenCalculation(definition)),
            ]));
        }
    }

    /// <summary>U21: a read-only dialog with the metric description and the metadata that determines its calculation.</summary>
    private static void OpenCalculation(MetricDefinition definition)
    {
        var body = new StringBuilder();
        body.Append((string)"RWS_Calc_Description".Translate()).Append('\n');
        var description = definition.ProviderId is not null
            ? ProviderMetricText.Description(definition, TryTranslate)
            : (string)definition.DescriptionKey.Translate();
        body.AppendLine(string.IsNullOrWhiteSpace(description) ? Dialog_RimworldStatistics.Dash : description);
        body.Append('\n').Append((string)"RWS_Calc_Calculation".Translate()).Append('\n');
        body.Append((string)"RWS_Calc_Provenance".Translate()).Append(": ").Append((string)ProvenanceLabel(definition.Provenance)).Append('\n');
        body.Append((string)"RWS_Calc_Aggregation".Translate()).Append(": ").Append(AggregationLabel(definition.Overall)).Append('\n');
        body.Append((string)"RWS_Calc_Unit".Translate()).Append(": ").Append(UnitLabel(definition.Unit)).Append('\n');
        body.Append((string)"RWS_Calc_Source".Translate()).Append(": ").Append(SourceLabel(definition.Source)).Append('\n');
        if (definition.Acquisition is { } acquisition)
            body.Append((string)"RWS_Calc_Acquisition".Translate()).Append(": ").AppendLine(acquisition);
        body.Append((string)"RWS_Calc_StoredKey".Translate()).Append(": ").AppendLine(definition.StoredKey);
        body.Append((string)"RWS_Calc_LedgerIds".Translate()).Append(": ").AppendLine(definition.LedgerIds);
        Find.WindowStack.Add(new Dialog_MessageBox(body.ToString().TrimEnd('\n'), title: DefinitionLabel(definition)));
    }

    private static string AggregationLabel(OverallAggregation aggregation) => aggregation switch
    {
        OverallAggregation.Sum => "RWS_Calc_Agg_Sum".Translate(),
        OverallAggregation.Max => "RWS_Calc_Agg_Max".Translate(),
        OverallAggregation.Min => "RWS_Calc_Agg_Min".Translate(),
        OverallAggregation.Ratio => "RWS_Calc_Agg_Ratio".Translate(),
        OverallAggregation.IndividualOnly => "RWS_Calc_Agg_IndividualOnly".Translate(),
        _ => "RWS_Calc_Agg_Ranking".Translate(),
    };

    private static string UnitLabel(MetricUnit unit) => unit switch
    {
        MetricUnit.Count => "RWS_Calc_Unit_Count".Translate(),
        MetricUnit.Silver => "RWS_Calc_Unit_Silver".Translate(),
        MetricUnit.Days => "RWS_Calc_Unit_Days".Translate(),
        MetricUnit.Percent => "RWS_Calc_Unit_Percent".Translate(),
        MetricUnit.Mood => "RWS_Calc_Unit_Mood".Translate(),
        MetricUnit.PerYear => "RWS_Calc_Unit_PerYear".Translate(),
        MetricUnit.Hours => "RWS_Calc_Unit_Hours".Translate(),
        MetricUnit.SilverPerYear => "RWS_Calc_Unit_SilverPerYear".Translate(),
        MetricUnit.Years => "RWS_Calc_Unit_Years".Translate(),
        MetricUnit.Celsius => "RWS_Calc_Unit_Celsius".Translate(),
        MetricUnit.Watts => "RWS_Calc_Unit_Watts".Translate(),
        MetricUnit.WattDays => "RWS_Calc_Unit_WattDays".Translate(),
        MetricUnit.Nutrition => "RWS_Calc_Unit_Nutrition".Translate(),
        _ => "RWS_Calc_Unit_PerCellDay".Translate(),
    };

    private static string SourceLabel(MetricObservationSource source) => source switch
    {
        MetricObservationSource.SaveFile => "RWS_Calc_Source_SaveFile".Translate(),
        MetricObservationSource.SaveSnapshot => "RWS_Calc_Source_SaveSnapshot".Translate(),
        _ => "RWS_Calc_Source_Checkpoint".Translate(),
    };

    // ----- building the rows (only when an input changes) -----

    private static List<Line> Build(Inputs input)
    {
        var version = unchecked(++buildVersion);
        IReadOnlyList<NumericRow> rows;
        NumericColony? colony = null;
        if (input.PerColony)
        {
            colony = input.View.Colonies.FirstOrDefault(c => c.Key == input.Colony);
            rows = colony?.Rows ?? (IReadOnlyList<NumericRow>)Array.Empty<NumericRow>();
        }
        else
        {
            rows = input.View.Overall;
        }

        var term = input.Search.Trim();
        var query = SearchQuery.Parse(term);
        var selected = rows
            .Where(r => input.Category is null || r.Definition.Category == input.Category)
            .Where(r => MatchesStatus(r, input.StatusFilter))
            .Where(r => MatchesMode(r.Definition, input.Mode))
            .Where(r => MatchesSource(r.Definition, input.SourceFilter))
            // U18: applied before derived grouping, so an all-zero derived group disappears.
            .Where(r => !IsHiddenZero(r.Value, input.HideZero))
            .Select(r => (Row: r, Label: DefinitionLabel(r.Definition)))
            .Where(x => MatchesSearch(x.Row.Definition, x.Label, query))
            .ToList();
        totalMetricCount = rows.Count;
        matchedMetricCount = selected.Count;

        List<(NumericRow Row, string Label)> ordered;
        if (input.ByValue)
        {
            // Unknown values go last in either direction.
            var known = selected.Where(x => x.Row.Value.Number is not null);
            var unknown = selected.Where(x => x.Row.Value.Number is null).OrderBy(x => x.Label, StringComparer.CurrentCulture);
            ordered = (input.Ascending ? known.OrderBy(x => x.Row.Value.Number) : known.OrderByDescending(x => x.Row.Value.Number))
                .ThenBy(x => x.Label, StringComparer.CurrentCulture).Concat(unknown).ToList();
        }
        else
        {
            ordered = (input.Ascending
                ? selected.OrderBy(x => x.Label, StringComparer.CurrentCulture)
                : selected.OrderByDescending(x => x.Label, StringComparer.CurrentCulture)).ToList();
        }

        var breakdownOnlyKeys = input.PerColony
            ? ColonyHistoryFavorites.BuildBreakdownOnlyKeys(input.View)
            : null;
        var enableGrouping = DerivedMetricGroups.ShouldGroup(input.GroupDerived, input.PerColony, input.ByValue, term);
        var grouped = DerivedMetricGroups.Group(ordered, x => x.Row.Definition, enableGrouping);
        var result = new List<Line>();
        foreach (var node in grouped)
        {
            var (row, label) = node.Item;
            var crossTop = CrossColonyCatalog.IsDerived(row.Definition)
                && row.Definition.Key.StartsWith(CrossColonyCatalog.TopPrefix, StringComparison.Ordinal);
            var hasBreakdowns = row.Breakdowns.Count > 0 && (row.Definition.HasBreakdown || crossTop);
            var expandable = hasBreakdowns || node.Children.Count > 0;
            var expandKey = expandable ? (input.PerColony ? "c:" : "a:") + row.Definition.Key : null;
            result.Add(MakeLine(row, label, 0, expandKey, input, colony, breakdownOnlyKeys,
                tipId: unchecked(0x52570000 + version * 4099 + result.Count), isMetricRow: true));
            if (expandKey is null || !Expanded.Contains(expandKey))
                continue;

            foreach (var child in node.Children)
            {
                var childCrossTop = CrossColonyCatalog.IsDerived(child.Row.Definition)
                    && child.Row.Definition.Key.StartsWith(CrossColonyCatalog.TopPrefix, StringComparison.Ordinal);
                var childHasBreakdowns = child.Row.Breakdowns.Count > 0 && (child.Row.Definition.HasBreakdown || childCrossTop);
                var childExpandKey = childHasBreakdowns ? (input.PerColony ? "c:" : "a:") + child.Row.Definition.Key : null;
                result.Add(MakeLine(child.Row, child.Label, 1, childExpandKey, input, colony, breakdownOnlyKeys,
                    tipId: unchecked(0x52570000 + version * 4099 + result.Count), isMetricRow: true));
                if (childExpandKey is not null && Expanded.Contains(childExpandKey))
                    AppendBreakdowns(child.Row, childExpandKey, 2, childCrossTop, input, colony, version, result);
            }

            if (hasBreakdowns)
                AppendBreakdowns(row, expandKey, 1, crossTop, input, colony, version, result);
        }
        return result;
    }

    private static void AppendBreakdowns(NumericRow row, string parentExpandKey, int depth, bool crossTop,
        Inputs input, NumericColony? colony, int version, List<Line> result)
    {
        var sourceDef = crossTop ? MetricRegistry.Find(row.Definition.StoredKey) ?? row.Definition : row.Definition;
        foreach (var b in row.Breakdowns.OrderByDescending(b => b.Value.Number ?? double.MinValue).ThenBy(b => b.Breakdown, StringComparer.Ordinal))
        {
            if (IsHiddenZero(b.Value, input.HideZero))
                continue;
            var childKey = crossTop && b.Breakdowns.Count > 0 ? parentExpandKey + ":" + b.Breakdown : null;
            var breakdownLabel = MetricText.Breakdown(sourceDef, b.Breakdown,
                useThingLabel: crossTop && CrossColonyCatalog.UsesThingDefBreakdown(row.Definition));
            result.Add(MakeLine(b, breakdownLabel, depth, childKey, input, colony, null,
                tipId: unchecked(0x52570000 + version * 4099 + result.Count), isMetricRow: false));
            if (childKey is not null && Expanded.Contains(childKey))
                foreach (var contribution in b.Breakdowns)
                {
                    if (IsHiddenZero(contribution.Value, input.HideZero))
                        continue;
                    result.Add(MakeLine(contribution, Name(input.Stats, contribution.Breakdown),
                        depth + 1, null, input, colony, null,
                        tipId: unchecked(0x52570000 + version * 4099 + result.Count), isMetricRow: false));
                }
        }
    }

    private static Line MakeLine(NumericRow row, string label, int depth, string? expandKey, Inputs input, NumericColony? colony,
        IReadOnlySet<string>? breakdownOnlyKeys, int tipId, bool isMetricRow)
    {
        var d = row.Definition;
        var v = row.Value;
        var count = input.PerColony
            ? v.ThroughTick is { } t ? Dialog_RimworldStatistics.Days(t) : Dialog_RimworldStatistics.Dash
            : $"{row.Valid.ToString(Inv)} / {row.Targets.ToString(Inv)}";

        var value = FormatRowValue(row, key => Name(input.Stats, key));
        var state = CoverageLabel(d, v.Coverage);
        if (d.Acquisition is not null && !d.Published)
            state = (string)"RWS_Exp_ExperimentalRow".Translate() + " · " + state;

        var supportsHistory = isMetricRow && breakdownOnlyKeys is not null
            && ColonyHistoryFavorites.SupportsColonyHistory(d, breakdownOnlyKeys);
        var lazy = new LazyTip(() => BuildTip(row, label, isMetricRow, input, colony));
        return new Line(label, value, state, count, lazy.Get, tipId, depth, expandKey,
            MetricVisualState.Classify(row), isMetricRow ? d.Key : null,
            isMetricRow ? MetricText.ModSourceLabel(d) : null, supportsHistory);
    }

    private static string BuildTip(NumericRow row, string label, bool isMetricRow, Inputs input, NumericColony? colony)
    {
        var d = row.Definition;
        var v = row.Value;
        var tip = new StringBuilder();
        // A provider's statistic without a translated description shows none, never its key (#336).
        var description = d.ProviderId is not null ? ProviderMetricText.Description(d, TryTranslate)
            : (string)d.DescriptionKey.Translate();
        tip.Append(label).Append('\n');
        if (description is not null)
            tip.Append(description).Append('\n');
        tip.Append('\n');
        if (d.OwnerPackageId is { } owner)
        {
            tip.Append((string)"RWS_Mod_Provider".Translate(MetricText.ModDisplayName(owner, d.OwnerDisplayName))).Append('\n');
            if (!ModsConfig.IsActive(owner))
                tip.Append((string)"RWS_Mod_SourceUnavailable".Translate()).Append('\n');
        }
        var stateText = d.Acquisition is not null && !d.Published
            ? (string)"RWS_Exp_ExperimentalRow".Translate() + " · " + CoverageLabel(d, v.Coverage)
            : CoverageLabel(d, v.Coverage);
        tip.Append((string)"RWS_Details_Tip_Source".Translate(ProvenanceLabel(d.Provenance), stateText)).Append('\n');
        if (d.Acquisition is not null && !d.Published)
        {
            if (v.SinceTick is { } first)
                tip.Append("RWS_Exp_Since".Translate(Dialog_RimworldStatistics.Days(first))).Append('\n');
            if (d.RequiredDlc is { } dlc)
                tip.Append("RWS_Exp_RequiresDlc".Translate(MetricText.DlcDisplayName(dlc))).Append('\n');
        }
        var availabilityMessageShown = false;
        if (input.PerColony)
        {
            if (v.ThroughTick is { } through)
                tip.Append((string)"RWS_Details_Tip_Through".Translate(Dialog_RimworldStatistics.Days(through))).Append('\n');
            if (d.Provenance != MetricProvenance.SaveSnapshot)
                tip.Append((string)"RWS_Details_Tip_StartGame".Translate()).Append('\n');
            if (colony is not null)
                tip.Append((string)"RWS_Details_Tip_Observations".Translate(colony.Observations)).Append('\n');
        }
        else
        {
            tip.Append((string)"RWS_Details_Tip_Colonies".Translate(row.Targets, row.Valid)).Append('\n');
            if (isMetricRow)
            {
                // Show a concise message only when there are no applicable or measured colonies.
                switch (MetricAvailabilityPolicy.Classify(row))
                {
                    case MetricAvailability.NoApplicableColonies:
                        tip.Append((string)"RWS_Details_AvailabilityNone".Translate()).Append('\n');
                        availabilityMessageShown = true;
                        break;
                    case MetricAvailability.NoMeasuredColonies:
                        tip.Append((string)"RWS_Details_AvailabilityMissing".Translate()).Append('\n');
                        availabilityMessageShown = true;
                        break;
                }
            }
            if (v.ColonyKey is { } holder)
                tip.Append((string)"RWS_Details_Tip_Holder".Translate(Name(input.Stats, holder))).Append('\n');
            if (v.HolderKeys is { Count: > 1 } tied)
                tip.Append((string)"RWS_Cross_TiedHolders".Translate(string.Join(", ", tied.Select(k => Name(input.Stats, k))))).Append('\n');
            if (CrossColonyCatalog.IsDerived(d) && v.ThroughTick is { } recorded)
                tip.Append((string)"RWS_Cross_RecordedAt".Translate(Dialog_RimworldStatistics.Days(recorded))).Append('\n');
        }
        if (v.SampleCount is { } n)
            tip.Append((string)"RWS_Details_Tip_Samples".Translate(n)).Append('\n');
        if (v.Numerator is { } num && v.Denominator is { } den && d.Overall == OverallAggregation.Ratio)
            tip.Append((string)"RWS_Details_Tip_Ratio".Translate(Number(num), Number(den))).Append('\n');
        if (v.Coverage == MetricCoverage.Unknown && !availabilityMessageShown)
        {
            tip.Append((string)"RWS_Details_Tip_Unknown".Translate()).Append('\n');
        }
        return tip.ToString().TrimEnd('\n');
    }

    // ----- formatting (PR-6a U10: one display format choice for Details, Rankings and the window) -----

    private static string Number(double value)
    {
        var (_, grouping) = Dialog_RimworldStatistics.DisplayOptions();
        return Math.Abs(value - Math.Round(value)) < 1e-9
            ? DisplayFormat.WholeNumber(value, grouping)
            : DisplayFormat.Number(value, 2, grouping);
    }

    /// <summary>U10: a day count (not ticks) in the chosen decimal-days or years-and-days form, with its unit.</summary>
    internal static string DayCount(double days, string sign = "")
    {
        var (format, grouping) = Dialog_RimworldStatistics.DisplayOptions();
        var text = DisplayFormat.DayCount(days, format, grouping, Dialog_RimworldStatistics.YearsAndDaysText);
        return format == DisplayDayFormat.YearsAndDays
            ? sign + text
            : (string)"RWS_Unit_Days".Translate(sign + DisplayFormat.Number(days, 1, grouping));
    }

    internal static string FormatRowValue(NumericRow row, Func<string, string> colonyName)
    {
        var d = row.Definition;
        var v = row.Value;
        // A range without an additive total (the goodwill range) shows its measured ends instead of a dash (#329).
        var value = MetricDisplayPolicy.BreakdownRange(row) is { } range
            ? Format(d, new NumericValue { Number = range.Min, Coverage = v.Coverage }) + " – "
                + Format(d, new NumericValue { Number = range.Max, Coverage = v.Coverage })
            : Format(row.Breakdown.Length > 0 && d.BreakdownUnit is { } bu ? d with { Unit = bu } : d, v);
        if ((d.Category == MetricCategory.Rankings || CrossColonyCatalog.IsDerived(d)) && row.Breakdown.Length == 0)
        {
            // Cross-colony records retain their original category but still
            // show the winning item or the colony holding the record.
            if (v.LabelKeys is { Count: > 0 } tied)
            {
                // The top items or categories named as their breakdown rows name them (#334).
                var source = MetricRegistry.Find(d.StoredKey) ?? d;
                var useThingLabel = CrossColonyCatalog.UsesThingDefBreakdown(d);
                value += " · " + CrossColonyAggregator.TiedLabel(tied,
                    key => MetricText.Breakdown(source, key, useThingLabel));
            }
            else if (v.Label is { } holderLabel)
                value += " · " + holderLabel;
            else if (v.ColonyKey is { } holderColony)
                value += " · " + colonyName(holderColony);
        }
        return value;
    }

    internal static string Format(MetricDefinition d, NumericValue v)
    {
        if (MetricDisplayPolicy.IsUnrankedTop(d, v))
            return "RWS_Details_UnrankedTop".Translate();
        if (v.NotReached)
            return "RWS_Details_NotReached".Translate();
        if (v.Number is not { } n)
            return Dialog_RimworldStatistics.Dash;
        var sign = d.Signed && n > 0 ? "+" : "";
        var (_, grouping) = Dialog_RimworldStatistics.DisplayOptions();
        return d.Unit switch
        {
            MetricUnit.Silver => sign + DisplayFormat.WholeNumber(n, grouping),
            MetricUnit.Count => Math.Abs(n - Math.Round(n)) < 1e-9
                ? DisplayFormat.WholeNumber(n, grouping)
                : DisplayFormat.Number(n, 1, grouping),
            MetricUnit.Days => DayCount(n, sign),
            MetricUnit.Percent => sign + DisplayFormat.Number(n * 100, 1, grouping) + "%",
            MetricUnit.Mood => DisplayFormat.Number(n, 1, grouping) + "%",
            MetricUnit.PerYear => "RWS_Unit_PerYear".Translate(DisplayFormat.Number(n, 2, grouping)),
            MetricUnit.Hours => "RWS_HoursValue".Translate(DisplayFormat.Number(n, 1, grouping)),
            MetricUnit.SilverPerYear => "RWS_Unit_SilverPerYear".Translate(sign + DisplayFormat.WholeNumber(n, grouping)),
            MetricUnit.Years => "RWS_Unit_Years".Translate(DisplayFormat.Number(n, 1, grouping)),
            MetricUnit.Celsius => DisplayFormat.Number(n, 1, grouping) + " °C",
            MetricUnit.Watts => DisplayFormat.Number(n, 1, grouping) + " W",
            MetricUnit.WattDays => DisplayFormat.Number(n, 1, grouping) + " Wd",
            MetricUnit.Nutrition => "RWS_Unit_Nutrition".Translate(DisplayFormat.Number(n, 2, grouping)),
            MetricUnit.PerCellDay => "RWS_Unit_PerCellDay".Translate(DisplayFormat.Number(n, 3, grouping)),
            _ => DisplayFormat.Number(n, 2, grouping),
        };
    }

    internal static string OverallTip(NumericRow row, StatsResult stats)
        => BuildTip(row, DefinitionLabel(row.Definition), true,
            new Inputs(NumericView.Empty, stats, false, null, null, "", false, true, 0,
                DetailsViewMode.All, DetailsStatusFilter.All, true, MetricSourceFilter.All, false, null, ""), null);

    internal static string ColonyTip(NumericRow row, StatsResult stats, NumericColony? colony)
        => BuildTip(row, DefinitionLabel(row.Definition), true,
            new Inputs(NumericView.Empty, stats, true, colony?.Key, null, "", false, true, 0,
                DetailsViewMode.All, DetailsStatusFilter.All, true, MetricSourceFilter.All, false, null, ""), colony);

    internal static string DefinitionLabel(MetricDefinition d)
    {
        string label;
        if (CrossColonyCatalog.IsDerived(d) && MetricRegistry.Find(d.StoredKey) is { } source)
            label = d.Key.StartsWith(CrossColonyCatalog.MaxPrefix, StringComparison.Ordinal)
                ? (string)"RWS_Cross_MaxColony".Translate((string)source.LabelKey.Translate())
                : d.Key.StartsWith(CrossColonyCatalog.MinPrefix, StringComparison.Ordinal)
                    ? (string)"RWS_Cross_MinColony".Translate((string)source.LabelKey.Translate())
                    : d.Key.StartsWith(CrossColonyCatalog.SumPrefix, StringComparison.Ordinal)
                        ? (string)"RWS_Cross_Sum".Translate((string)source.LabelKey.Translate())
                        : d.DescriptionKey == "RWS_CrossDesc_TopCategory"
                            ? (string)"RWS_Cross_TopCategory".Translate((string)source.LabelKey.Translate())
                            : (string)"RWS_Cross_TopType".Translate((string)source.LabelKey.Translate());
        else
            label = d.ProviderId is not null
                // A provider's statistic without a translation must not show 'mod.owner.family.name' (#336).
                ? ProviderMetricText.Label(d, TryTranslate)
                : (string)d.LabelKey.Translate();

        // Show only the measured statistic; source MOD attribution is separate UI metadata.
        return label;
    }

    /// <summary>The key's text in the active language, else in the default language (as <c>Translate()</c> falls back), else null.</summary>
    private static string? TryTranslate(string key)
    {
        if (key.TryTranslate(out var text))
            return text;
        return LanguageDatabase.defaultLanguage is { } fallback && fallback.TryGetTextFromKey(key, out text) ? (string)text : null;
    }

    private static string Name(StatsResult stats, string key)
        => stats.Colonies.FirstOrDefault(c => c.Key == key) is { } s ? Dialog_RimworldStatistics.ColonyName(s) : key;

    // One literal key per value (no string concatenation), so the translation-key check sees every key.
    internal static string CategoryLabel(MetricCategory c) => c switch
    {
        MetricCategory.Wealth => "RWS_Category_Wealth".Translate(),
        MetricCategory.Population => "RWS_Category_Population".Translate(),
        MetricCategory.Defense => "RWS_Category_Defense".Translate(),
        MetricCategory.Quests => "RWS_Category_Quests".Translate(),
        MetricCategory.Colony => "RWS_Category_Colony".Translate(),
        MetricCategory.Research => "RWS_Category_Research".Translate(),
        MetricCategory.Mind => "RWS_Category_Mind".Translate(),
        MetricCategory.Combat => "RWS_Category_Combat".Translate(),
        MetricCategory.Social => "RWS_Category_Social".Translate(),
        MetricCategory.Medicine => "RWS_Category_Medicine".Translate(),
        MetricCategory.Work => "RWS_Category_Work".Translate(),
        MetricCategory.Animals => "RWS_Category_Animals".Translate(),
        MetricCategory.Economy => "RWS_Category_Economy".Translate(),
        MetricCategory.Environment => "RWS_Category_Environment".Translate(),
        MetricCategory.Exploration => "RWS_Category_Exploration".Translate(),
        MetricCategory.Facilities => "RWS_Category_Facilities".Translate(),
        MetricCategory.Royalty => "RWS_Category_Royalty".Translate(),
        MetricCategory.Ideology => "RWS_Category_Ideology".Translate(),
        MetricCategory.Biotech => "RWS_Category_Biotech".Translate(),
        MetricCategory.Anomaly => "RWS_Category_Anomaly".Translate(),
        MetricCategory.Odyssey => "RWS_Category_Odyssey".Translate(),
        _ => "RWS_Category_Rankings".Translate(),
    };

    private static string ProvenanceLabel(MetricProvenance p) => p switch
    {
        MetricProvenance.VanillaCounter => "RWS_Provenance_VanillaCounter".Translate(),
        MetricProvenance.SaveHistory => "RWS_Provenance_SaveHistory".Translate(),
        MetricProvenance.SaveSnapshot => "RWS_Provenance_SaveSnapshot".Translate(),
        _ => "RWS_Provenance_LiveAggregate".Translate(),
    };

    internal static string CoverageLabel(MetricDefinition d, MetricCoverage c) => c switch
    {
        MetricCoverage.Complete => "RWS_Coverage_Complete".Translate(),
        MetricCoverage.Partial => "RWS_Coverage_Partial".Translate(),
        MetricCoverage.SnapshotOnly when d.Provenance == MetricProvenance.SaveHistory => "RWS_Coverage_History".Translate(),
        MetricCoverage.SnapshotOnly => "RWS_Coverage_SnapshotOnly".Translate(),
        MetricCoverage.NotApplicable => "RWS_Coverage_NotApplicable".Translate(),
        _ => "RWS_Coverage_Unknown".Translate(),
    };
}
