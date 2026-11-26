using System.Globalization;
using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>One row per colony with sortable columns, an exclude checkbox, and the source saves of the selected row (spec §9.2).</summary>
public static class ColoniesTab
{
    private const float RowHeight = 28f;
    private const float SearchBarHeight = 30f;
    private const float BaseDetailHeight = 224f;
    // Additional per-colony fields must remain readable in smaller windows.
    private const float MinTableWidth = 1260f;

    private sealed record Column(string LabelKey, float Width, Func<ColonyStats, IComparable> Sort,
        Func<ColonyStats, string> Text, Func<ColonyStats, bool>? HasValue = null);

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly Column[] Columns =
    {
        new("RWS_Col_Name", 0.15f, s => Dialog_RimworldStatistics.ColonyName(s), Dialog_RimworldStatistics.ColonyName),
        new("RWS_Col_World", 0.10f, s => s.WorldName ?? "", s => s.WorldName ?? Dialog_RimworldStatistics.Dash,
            s => !string.IsNullOrEmpty(s.WorldName)),
        new("RWS_Col_Scenario", 0.13f, s => (s.Scenario ?? "") + "/" + (s.Storyteller ?? ""),
            s => $"{s.Scenario ?? Dialog_RimworldStatistics.Dash} / {s.Storyteller ?? Dialog_RimworldStatistics.Dash}",
            s => !string.IsNullOrEmpty(s.Scenario) || !string.IsNullOrEmpty(s.Storyteller)),
        // A colony known only from ending records has no observed save or live point: its counters are unknown, not 0 (#332).
        new("RWS_Col_Days", 0.06f, s => s.MaxTicks, s => Observed(s, Dialog_RimworldStatistics.Days(s.MaxTicks)), Observed),
        new("RWS_Col_PeakWealth", 0.08f, s => s.WealthMax, s => s.PeakWealthKnown ? StatFormat.Group(s.WealthMax) : Dialog_RimworldStatistics.Dash,
            s => s.PeakWealthKnown),
        new("RWS_Col_LastWealth", 0.08f, s => s.WealthLast, s => s.LastWealthKnown ? StatFormat.Group(s.WealthLast) : Dialog_RimworldStatistics.Dash,
            s => s.LastWealthKnown),
        new("RWS_Col_Raids", 0.05f, s => s.Raids, s => Observed(s, s.Raids.ToString(Inv)), Observed),
        new("RWS_Col_Killed", 0.05f, s => s.Killed, s => Observed(s, s.Killed.ToString(Inv)), Observed),
        new("RWS_Stat_Threats", 0.06f, s => s.Threats, s => Observed(s, s.Threats.ToString(Inv)), Observed),
        new("RWS_Stat_Departed", 0.06f, s => s.Departed, s => s.Departed.ToString(Inv)),
        new("RWS_Stat_MaxPopulation", 0.07f, s => s.MaxPopulation, s => Observed(s, s.MaxPopulation.ToString(Inv)), Observed),
        new("RWS_Stat_RealPlay", 0.08f, s => s.MaxRealPlay,
            s => Observed(s, "RWS_HoursValue".Translate(StatFormat.Hours(s.MaxRealPlay).ToString("N1", Inv))), Observed),
        new("RWS_Col_Endings", 0.05f, s => s.EndingCounts.Values.Sum(), s => s.EndingCounts.Values.Sum().ToString(Inv)),
        new("RWS_Col_Versions", 0.07f, s => string.Join(",", s.GameVersions), s => Dialog_RimworldStatistics.ShortVersions(s.GameVersions),
            s => s.GameVersions.Count > 0),
        new("RWS_Col_Saves", 0.05f, s => s.SaveCount, s => s.SaveCount.ToString(Inv)),
        new("RWS_Col_LastPlayed", 0.08f, s => s.LastPlayedUtc ?? DateTime.MinValue, s => Dialog_RimworldStatistics.Local(s.LastPlayedUtc),
            s => s.LastPlayedUtc.HasValue),
    };

    private static bool Observed(ColonyStats s) => s.HasObservedPoint;

    private static string Observed(ColonyStats s, string text) => s.HasObservedPoint ? text : Dialog_RimworldStatistics.Dash;

    private const float ExcludeWidth = 0.05f;
    private static readonly float TotalColumnWeight = Columns.Sum(c => c.Width) + ExcludeWidth;
    private static ColumnPlan? columnPlan;
    private static object? tableLanguage;
    private static float tableWidth;
    private static string[] labels = [];
    private static ColumnSpec[] specs = [];

    private static int sortColumn = 3;
    private static bool ascending;
    private static string search = "";
    private static string? selectedKey;
    private static string? editingColonyKey;
    private static string editingPreferencesSignature = "";
    private static string nameDraft = "";
    // Inline row-name editing is session-local; only SetCustomName/ClearCustomName persist (U16).
    private static string? inlineEditColonyKey;
    private static string inlineEditDraft = "";
    private static readonly Color CurrentColonyTint = new(0.26f, 0.44f, 0.56f, 0.35f);
    private static Vector2 scroll;
    private static Vector2 detailScroll;
    private static readonly StatsTable DetailTable = new(24f, new(180, 5), new(90, 2), new(68, 1), new(95, 1), new(95, 1));
    private sealed record DetailCache(Colony Colony, SaveFileIndex? Files, object? Language, string[] Headers, string[][] Rows);
    private static DetailCache? details;

    private sealed class TableCache(StatsView view, int sortColumn, bool ascending, string search, string nameSignature, int[] order)
    {
        public StatsView View { get; } = view;
        public int SortColumn { get; } = sortColumn;
        public bool Ascending { get; } = ascending;
        public string Search { get; } = search;
        public string NameSignature { get; } = nameSignature;
        public int[] Order { get; } = order;
        public string[]?[] Cells { get; } = new string[order.Length][];
        public string[]?[] Display { get; } = new string[order.Length][];
    }

    private static TableCache? cache;

    /// <summary>The empty state's action (U47): leave for the Data management tab that imports the saves.</summary>
    private static void DrawEmptyState(Rect rect)
    {
        var text = (string)"RWS_NoData".Translate();
        var height = Math.Min(rect.height, Text.CalcHeight(text, Math.Max(1f, rect.width)));
        Widgets.Label(new Rect(rect.x, rect.y, rect.width, height), text);
        var label = (string)"RWS_Empty_OpenImport".Translate();
        var button = new Rect(rect.x, rect.y + height + 10f,
            Math.Min(rect.width, Math.Max(200f, Text.CalcSize(label).x + 24f)), 32f);
        if (Widgets.ButtonText(button, label))
            Dialog_RimworldStatistics.SwitchTo(Dialog_RimworldStatistics.Tab.Import);
    }

    public static void Draw(Rect rect, StatsView view, SaveFileIndex? files, ViewPreferences? preferences = null, Action? savePreferences = null)
    {
        if (view.Colonies.Count == 0)
        {
            DrawEmptyState(rect);
            return;
        }

        var language = LanguageDatabase.activeLanguage;
        if (!ReferenceEquals(tableLanguage, language) || labels.Length == 0)
        {
            tableLanguage = language;
            labels = Columns.Select(c => (string)c.LabelKey.Translate()).Append((string)"RWS_Exclude".Translate()).ToArray();
            specs = Columns.Select((c, i) => new ColumnSpec(
                Math.Max(MinTableWidth * c.Width / TotalColumnWeight, Text.CalcSize(labels[i] + " ▲").x + 8f), c.Width))
                .Append(new ColumnSpec(Math.Max(MinTableWidth * ExcludeWidth / TotalColumnWeight, Text.CalcSize(labels[^1]).x + 8f), ExcludeWidth))
                .ToArray();
            columnPlan = null;
            cache = null;
        }
        var available = Math.Max(0f, rect.width - 16f);
        if (columnPlan is null || tableWidth != available)
        {
            columnPlan = ColumnLayout.Resolve(specs, available);
            tableWidth = available;
            if (cache is not null)
            {
                Array.Clear(cache.Cells);
                Array.Clear(cache.Display);
            }
        }

        var nameSignature = ColonyPresentation.Signature(preferences);
        // Resolve the played colony once per draw using the established in-game campaign-id pattern (U35).
        var currentColonyKey = Current.ProgramState == ProgramState.Playing
            ? InGameColonyAccess.Resolve(view, GameComponent_RimworldStatistics.CurrentOrNull?.campaignId).ColonyKey
            : null;
        var currentMarker = currentColonyKey is not null ? (string)"RWS_InGame_CurrentColonyMark".Translate() : "";
        if (cache is null || !ReferenceEquals(cache.View, view) || cache.SortColumn != sortColumn
            || cache.Ascending != ascending || cache.Search != search || cache.NameSignature != nameSignature)
        {
            var indices = Enumerable.Range(0, view.Colonies.Count)
                .Where(i => ColonyPresentation.MatchesSearch(
                    view.Stats.Colonies[i], preferences, Dialog_RimworldStatistics.UnnamedColonyFallback, search));
            var col = Columns[sortColumn];
            var key = col.Sort;
            var hasValue = col.HasValue;
            var primary = hasValue is null
                ? indices.OrderBy(_ => 0)
                : indices.OrderBy(i => hasValue(view.Stats.Colonies[i]) ? 0 : 1);
            var sorted = (ascending
                ? primary.ThenBy(i => key(view.Stats.Colonies[i]))
                : primary.ThenByDescending(i => key(view.Stats.Colonies[i]))).ToArray();
            cache = new TableCache(view, sortColumn, ascending, search, nameSignature, sorted);
        }

        var table = cache;
        var order = table.Order;

        DrawSearchBar(new Rect(rect.x, rect.y, rect.width, SearchBarHeight), order.Length, view.Colonies.Count);
        var contentTop = rect.y + SearchBarHeight + 4f;
        var contentHeight = Math.Max(0f, rect.yMax - contentTop);
        if (contentHeight <= 0f)
            return;

        if (order.Length == 0)
        {
            Widgets.Label(new Rect(rect.x, contentTop, rect.width, 28f), "RWS_Details_NoMatch".Translate());
            return;
        }

        var selectedRow = Array.FindIndex(order, i => view.Colonies[i].Key == selectedKey);
        var selected = selectedRow < 0 ? -1 : order[selectedRow];
        var detailHeight = selected >= 0 ? Math.Min(BaseDetailHeight, Math.Max(136f, contentHeight * 0.46f)) : 0f;
        var tableHeight = Math.Max(0f, contentHeight - (selected >= 0 ? detailHeight + 8f : 0f));
        var width = columnPlan.TotalWidth;

        if (tableHeight >= RowHeight)
        {
            // Keep the sortable header fixed vertically while tracking horizontal body scroll.
            using (UiScopes.Group(new Rect(rect.x, contentTop, available, RowHeight)))
            {
                var headerColumns = ColumnLayout.VisibleColumns(columnPlan.Widths, scroll.x, available);
                DrawHeader(new Rect(-scroll.x, 0f, width, RowHeight), headerColumns.First, headerColumns.LastExclusive);
            }

            var outRect = new Rect(rect.x, contentTop + RowHeight, rect.width, Math.Max(0f, tableHeight - RowHeight));
            if (outRect.height > 0f)
            {
                var viewRect = new Rect(0f, 0f, width, order.Length * RowHeight);
                using (UiScopes.ScrollView(outRect, ref scroll, viewRect))
                {
                    var visibleColumns = ColumnLayout.VisibleColumns(columnPlan.Widths, scroll.x, outRect.width);
                    var first = Math.Max(0, (int)(scroll.y / RowHeight));
                    var last = Math.Min(order.Length - 1, (int)((scroll.y + outRect.height) / RowHeight) + 1);
                    for (var row = first; row <= last; row++)
                        DrawColony(new Rect(0f, row * RowHeight, width, RowHeight), view.Colonies[order[row]], view.Stats.Colonies[order[row]],
                            row, table, visibleColumns.First, visibleColumns.LastExclusive, preferences, savePreferences,
                            currentColonyKey, currentMarker);
                }
            }
        }

        if (selected >= 0 && detailHeight > 0f)
        {
            DrawDetail(
                new Rect(rect.x, contentTop + tableHeight + 8f, rect.width, detailHeight),
                view,
                view.Colonies[selected],
                view.Stats.Colonies[selected],
                files,
                preferences,
                savePreferences,
                nameSignature);
        }
    }

    private static void DrawSearchBar(Rect rect, int shownCount, int totalCount)
    {
        var searchLabel = (string)"RWS_Colonies_Search".Translate();
        var labelWidth = Math.Min(Text.CalcSize(searchLabel).x + 6f, Math.Max(0f, rect.width * 0.25f));
        var hasFilter = !string.IsNullOrWhiteSpace(search);
        var resetLabel = (string)"RWS_Filter_Reset".Translate();
        var resetWidth = hasFilter ? Math.Min(140f, Math.Max(84f, Text.CalcSize(resetLabel).x + 16f)) : 0f;
        var summary = (string)"RWS_Filter_Shown".Translate(shownCount, totalCount);
        var summaryWidth = Math.Min(180f, Text.CalcSize(summary).x + 10f);

        var prevAnchor = Text.Anchor;
        var prevColor = GUI.color;
        try
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            var labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
            Widgets.Label(labelRect, searchLabel.Truncate(labelWidth));
            TooltipHandler.TipRegion(labelRect, "RWS_Colonies_SearchTip".Translate());

            var rightReserved = summaryWidth + (hasFilter ? resetWidth + 12f : 6f);
            var fieldWidth = Math.Max(80f, Math.Min(320f, rect.width - labelWidth - rightReserved - 8f));
            var fieldRect = new Rect(labelRect.xMax + 4f, rect.y + 1f, Math.Max(0f, Math.Min(fieldWidth, rect.xMax - labelRect.xMax - 4f)), rect.height - 2f);
            Text.Anchor = TextAnchor.UpperLeft;
            var next = Widgets.TextField(fieldRect, search) ?? "";
            TooltipHandler.TipRegion(fieldRect, "RWS_Colonies_SearchTip".Translate());
            if (next != search)
            {
                search = next;
                scroll = Vector2.zero;
            }

            var x = fieldRect.xMax + 8f;
            if (x + 40f < rect.xMax)
            {
                var shownRect = new Rect(x, rect.y, Math.Min(summaryWidth, Math.Max(0f, rect.xMax - x - (hasFilter ? resetWidth + 6f : 0f))), rect.height);
                if (shownRect.width > 20f)
                {
                    Text.Anchor = TextAnchor.MiddleLeft;
                    GUI.color = new Color(0.8f, 0.8f, 0.8f);
                    var shownSummary = summary.Truncate(shownRect.width);
                    Widgets.Label(shownRect, shownSummary);
                    GUI.color = prevColor;
                    if (shownSummary != summary)
                        TooltipHandler.TipRegion(shownRect, summary);
                    x = shownRect.xMax + 6f;
                }
            }

            if (hasFilter && x + 40f <= rect.xMax)
            {
                Text.Anchor = TextAnchor.UpperLeft;
                var resetRect = new Rect(x, rect.y + 1f, Math.Min(resetWidth, rect.xMax - x), rect.height - 2f);
                var shownReset = resetLabel.Truncate(Math.Max(0f, resetRect.width - 10f));
                if (shownReset != resetLabel)
                    TooltipHandler.TipRegion(resetRect, resetLabel);
                if (Widgets.ButtonText(resetRect, shownReset))
                {
                    search = "";
                    scroll = Vector2.zero;
                }
            }
        }
        finally
        {
            Text.Anchor = prevAnchor;
            GUI.color = prevColor;
        }
    }

    private static void DrawHeader(Rect rect, int firstVisible, int lastVisible)
    {
        var x = rect.x;
        using (UiScopes.GuiState())
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(0.8f, 0.8f, 0.8f);
            for (var c = 0; c < Columns.Length; c++)
            {
                if (c >= firstVisible && c < lastVisible)
                {
                    var cell = new Rect(x, rect.y, columnPlan!.Widths[c], rect.height);
                    var label = labels[c] + (sortColumn == c ? (ascending ? " ▲" : " ▼") : "");
                    Widgets.Label(new Rect(cell.x + 3f, cell.y, cell.width - 6f, cell.height), label);
                    Widgets.DrawHighlightIfMouseover(cell);
                    TooltipHandler.TipRegion(cell, label);
                    if (Widgets.ButtonInvisible(cell))
                    {
                        if (sortColumn == c) ascending = !ascending;
                        else { sortColumn = c; ascending = false; }
                    }
                }
                x += columnPlan!.Widths[c];
            }
            var excludeWidth = columnPlan!.Widths[^1];
            if (lastVisible > Columns.Length)
            {
                Widgets.Label(new Rect(x + 3f, rect.y, excludeWidth - 6f, rect.height), labels[^1]);
                TooltipHandler.TipRegion(new Rect(x, rect.y, excludeWidth, rect.height), "RWS_ExcludeTip".Translate());
            }
        }
    }

    private static void DrawColony(Rect rect, Colony colony, ColonyStats s, int row, TableCache table,
        int firstVisible, int lastVisible, ViewPreferences? preferences, Action? savePreferences,
        string? currentColonyKey, string currentMarker)
    {
        var isCurrent = currentColonyKey is not null && string.Equals(colony.Key, currentColonyKey, StringComparison.Ordinal);
        // Current-colony tint is drawn first; selection and mouseover highlights still win (U35).
        if (isCurrent)
            Widgets.DrawBoxSolid(rect, CurrentColonyTint);
        if (colony.Key == selectedKey)
            Widgets.DrawHighlightSelected(rect);
        else if (!isCurrent && row % 2 == 1)
            Widgets.DrawHighlight(rect, 0.25f);
        Widgets.DrawHighlightIfMouseover(rect);
        var x = rect.x;
        using (UiScopes.GuiState())
        {
            if (s.Excluded)
                GUI.color = new Color(1f, 1f, 1f, 0.5f);
            Text.Anchor = TextAnchor.MiddleLeft;
            // Fill only intersecting cells on demand. Horizontal scrolling reuses
            // cached strings for previously visible columns instead of materializing all 16.
            var texts = table.Cells[row] ??= new string[Columns.Length];
            var shown = table.Display[row] ??= new string[Columns.Length];
            for (var c = 0; c < Columns.Length; c++)
            {
                var columnWidth = columnPlan!.Widths[c];
                if (c >= firstVisible && c < lastVisible)
                {
                    var cell = new Rect(x + 3f, rect.y, columnWidth - 6f, rect.height);
                    var text = texts[c] ??= Columns[c].Text(s);
                    if (c == 0)
                    {
                        // The current marker is applied outside the cell cache, so it never goes stale.
                        DrawNameCell(cell, colony, s, preferences, savePreferences, text, columnWidth - 6f, isCurrent, currentMarker);
                    }
                    else
                    {
                        var label = shown[c] ??= text.Truncate(columnWidth - 6f);
                        Widgets.Label(cell, label);
                        if (label != text)
                            TooltipHandler.TipRegion(cell, text);
                    }
                }
                x += columnWidth;
            }
        }

        var excluded = s.Excluded;
        var box = new Rect(x + 4f, rect.y + (rect.height - 24f) / 2f, 24f, 24f);
        Widgets.Checkbox(box.x, box.y, ref excluded);
        if (excluded != s.Excluded)
        {
            var keys = colony.AliasKeys.Prepend(colony.Key).ToList();
            ProfileService.Session.MutateWithoutBlocking(p => ProfileMutations.SetExcluded(p, keys, excluded));
        }
        else if (Widgets.ButtonInvisible(new Rect(rect.x, rect.y, x - rect.x, rect.height)))
        {
            selectedKey = selectedKey == colony.Key ? null : colony.Key;
        }
    }

    /// <summary>Inline display-name cell (U16): click the pencil to edit in place, right-click for "reset to original".</summary>
    private static void DrawNameCell(Rect cell, Colony colony, ColonyStats s, ViewPreferences? preferences,
        Action? savePreferences, string baseText, float width, bool isCurrent, string currentMarker)
    {
        var canEdit = preferences is not null && savePreferences is not null;
        var showButtons = canEdit && width >= 70f;
        var contentWidth = showButtons ? Math.Max(10f, width - 22f) : width;
        var text = baseText + (isCurrent ? currentMarker : "");
        var label = text.Truncate(contentWidth);
        var button = new Rect(cell.xMax - 20f, cell.y + 2f, 18f, cell.height - 4f);
        var ev = Event.current;

        if (canEdit && inlineEditColonyKey == colony.Key)
        {
            if (ev is not null && ev.type == EventType.KeyDown)
            {
                if (ev.keyCode == KeyCode.Escape)
                {
                    inlineEditColonyKey = null;
                    inlineEditDraft = "";
                    ev.Use();
                    return;
                }
                if (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter)
                {
                    CommitInlineName(colony, preferences!, savePreferences!);
                    ev.Use();
                    return;
                }
            }
            var originalInfo = (string)"RWS_ColonyName_Original".Translate(Dialog_RimworldStatistics.OriginalColonyName(s));
            var editTip = (string)"RWS_ColonyName_EditTip".Translate() + "\n" + originalInfo;
            Text.Anchor = TextAnchor.UpperLeft;
            var edited = Widgets.TextField(new Rect(cell.x, cell.y, contentWidth, cell.height), inlineEditDraft) ?? "";
            if (edited != inlineEditDraft)
                inlineEditDraft = edited.Length > ViewPreferences.MaxNameLength ? edited[..ViewPreferences.MaxNameLength] : edited;
            TooltipHandler.TipRegion(cell, editTip);
            if (showButtons && Widgets.ButtonText(button, "✓"))
                CommitInlineName(colony, preferences!, savePreferences!);
        }
        else
        {
            Widgets.Label(cell, label);
            if (ColonyPresentation.HasCustomName(preferences, s))
            {
                var original = Dialog_RimworldStatistics.OriginalColonyName(s);
                TooltipHandler.TipRegion(cell, text + "\n" + "RWS_ColonyName_Original".Translate(original));
            }
            else if (label != text)
            {
                TooltipHandler.TipRegion(cell, text);
            }
            if (showButtons)
            {
                var original = Dialog_RimworldStatistics.OriginalColonyName(s);
                TooltipHandler.TipRegion(button, (string)"RWS_ColonyName_EditTip".Translate() + "\n"
                    + "RWS_ColonyName_Original".Translate(original));
                if (Widgets.ButtonText(button, "✎"))
                    StartInlineEdit(colony, preferences!);
            }
        }

        if (canEdit && ev is not null && ev.type == EventType.MouseDown && ev.button == 1 && cell.Contains(ev.mousePosition)
            && Find.WindowStack is { } stack)
        {
            var target = colony;
            var prefs = preferences!;
            var persist = savePreferences!;
            stack.Add(new FloatMenu([
                new FloatMenuOption((string)"RWS_ColonyName_ResetOriginal".Translate(),
                    () => ResetInlineName(target, prefs, persist)),
            ]));
            ev.Use();
        }
    }

    private static void StartInlineEdit(Colony colony, ViewPreferences preferences)
    {
        inlineEditColonyKey = colony.Key;
        inlineEditDraft = ColonyPresentation.TryGetCustomName(preferences, colony) ?? "";
    }

    private static void CommitInlineName(Colony colony, ViewPreferences preferences, Action savePreferences)
    {
        var current = ColonyPresentation.TryGetCustomName(preferences, colony);
        var normalized = ColonyPresentation.NormalizeDisplayName(inlineEditDraft);
        if (!string.Equals(normalized, current, StringComparison.Ordinal)
            && ColonyPresentation.SetCustomName(preferences, colony.Key, colony.AliasKeys, inlineEditDraft))
            savePreferences();
        inlineEditColonyKey = null;
        inlineEditDraft = "";
        // Name signature is recomputed from preferences on the next Draw, rebuilding the table cache.
        cache = null;
    }

    private static void ResetInlineName(Colony colony, ViewPreferences preferences, Action savePreferences)
    {
        if (ColonyPresentation.ClearCustomName(preferences, colony.Key, colony.AliasKeys))
            savePreferences();
        inlineEditColonyKey = null;
        inlineEditDraft = "";
        cache = null;
    }

    private static void DrawDetail(Rect rect, StatsView view, Colony colony, ColonyStats stats, SaveFileIndex? files,
        ViewPreferences? preferences, Action? savePreferences, string nameSignature)
    {
        Widgets.DrawMenuSection(rect);
        var inner = rect.ContractedBy(6f);
        var y = inner.y;

        if (preferences is not null && savePreferences is not null)
        {
            if (editingColonyKey != colony.Key || editingPreferencesSignature != nameSignature)
            {
                editingColonyKey = colony.Key;
                editingPreferencesSignature = nameSignature;
                nameDraft = ColonyPresentation.TryGetCustomName(preferences, stats) ?? "";
            }

            const float nameRowHeight = 28f;
            var nameLabel = (string)"RWS_ColonyName_Label".Translate();
            var saveLabel = (string)"RWS_ColonyName_Save".Translate();
            var clearLabel = (string)"RWS_ColonyName_Clear".Translate();
            var originalName = Dialog_RimworldStatistics.OriginalColonyName(stats);
            var originalInfo = (string)"RWS_ColonyName_Original".Translate(originalName);
            var editTip = (string)"RWS_ColonyName_EditTip".Translate() + "\n" + originalInfo;

            var labelW = Math.Min(135f, Math.Max(60f, Text.CalcSize(nameLabel).x + 6f));
            var saveW = Math.Min(140f, Math.Max(82f, Text.CalcSize(saveLabel).x + 16f));
            var clearW = Math.Min(120f, Math.Max(70f, Text.CalcSize(clearLabel).x + 16f));
            var maxFieldW = Math.Max(70f, inner.width - labelW - saveW - clearW - 16f);
            var fieldW = Math.Min(240f, maxFieldW);

            using (UiScopes.GuiState())
            {
                var lblRect = new Rect(inner.x, y, Math.Min(labelW, inner.width), nameRowHeight);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(lblRect, nameLabel.Truncate(lblRect.width));
                TooltipHandler.TipRegion(lblRect, editTip);
                Text.Anchor = TextAnchor.UpperLeft;

                var fldRect = new Rect(lblRect.xMax + 4f, y + 1f, Math.Max(0f, Math.Min(fieldW, inner.xMax - lblRect.xMax - 4f)), nameRowHeight - 2f);
                var edited = Widgets.TextField(fldRect, nameDraft) ?? "";
                if (edited != nameDraft)
                    nameDraft = edited.Length > ViewPreferences.MaxNameLength ? edited[..ViewPreferences.MaxNameLength] : edited;
                TooltipHandler.TipRegion(fldRect, editTip);

                var currentCustom = ColonyPresentation.TryGetCustomName(preferences, stats);
                var normalizedDraft = ColonyPresentation.NormalizeDisplayName(nameDraft);
                var canSave = !string.Equals(normalizedDraft, currentCustom, StringComparison.Ordinal);
                var canClear = currentCustom is not null || nameDraft.Length > 0;

                var saveRect = new Rect(fldRect.xMax + 6f, y + 1f, Math.Max(0f, Math.Min(saveW, inner.xMax - fldRect.xMax - 6f)), nameRowHeight - 2f);
                var shownSave = saveLabel.Truncate(Math.Max(0f, saveRect.width - 10f));
                if (shownSave != saveLabel)
                    TooltipHandler.TipRegion(saveRect, saveLabel);
                if (saveRect.width > 20f && Widgets.ButtonText(saveRect, shownSave, active: canSave) && canSave)
                {
                    if (ColonyPresentation.SetCustomName(preferences, colony.Key, colony.AliasKeys, nameDraft))
                        savePreferences();
                    nameDraft = ColonyPresentation.TryGetCustomName(preferences, stats) ?? "";
                    editingPreferencesSignature = ColonyPresentation.Signature(preferences);
                }

                var clearRect = new Rect(saveRect.xMax + 4f, y + 1f, Math.Max(0f, Math.Min(clearW, inner.xMax - saveRect.xMax - 4f)), nameRowHeight - 2f);
                var shownClear = clearLabel.Truncate(Math.Max(0f, clearRect.width - 10f));
                if (shownClear != clearLabel)
                    TooltipHandler.TipRegion(clearRect, clearLabel);
                if (clearRect.width > 20f && Widgets.ButtonText(clearRect, shownClear, active: canClear) && canClear)
                {
                    var changed = ColonyPresentation.ClearCustomName(preferences, colony.Key, colony.AliasKeys);
                    nameDraft = "";
                    if (changed)
                        savePreferences();
                    editingPreferencesSignature = ColonyPresentation.Signature(preferences);
                }

                var origX = clearRect.xMax + 8f;
                if (inner.xMax - origX >= 60f)
                {
                    var origRect = new Rect(origX, y, inner.xMax - origX, nameRowHeight);
                    using (UiScopes.GuiState())
                    {
                        Text.Anchor = TextAnchor.MiddleLeft;
                        GUI.color = new Color(0.78f, 0.78f, 0.82f);
                        var shownOrig = originalInfo.Truncate(origRect.width);
                        Widgets.Label(origRect, shownOrig);
                    }
                    TooltipHandler.TipRegion(origRect, originalInfo);
                }
            }

            y += nameRowHeight + 4f;
        }

        var controls = new ControlRow(new Rect(inner.x, y, inner.width, 28f));
        var reportLabel = (string)"RWS_Report_Open".Translate();
        var reportBtn = controls.Next(reportLabel, 135f, Math.Min(inner.width, 260f));
        if (Widgets.ButtonText(reportBtn, reportLabel.Truncate(Math.Max(0f, reportBtn.width - 12f))))
            UiScopes.OpenOrReplaceWindow(new Dialog_ColonySummaryReport(view, colony.Key));
        TooltipHandler.TipRegion(reportBtn, "RWS_Report_OpenTip".Translate());

        var lineageLabel = (string)"RWS_Lineage_Open".Translate();
        var lineageBtn = controls.Next(lineageLabel, 130f, Math.Min(inner.width, 260f));
        var shownBtn = lineageLabel.Truncate(Math.Max(0f, lineageBtn.width - 12f));
        if (Widgets.ButtonText(lineageBtn, shownBtn))
            UiScopes.OpenOrReplaceWindow(new Dialog_ColonySaveLineage(view, colony.Key, files));
        TooltipHandler.TipRegion(lineageBtn, "RWS_Lineage_OpenTip".Translate());

        var chronicleLabel = (string)"RWS_Chronicle_Open".Translate();
        var chronicleBtn = controls.Next(chronicleLabel, 125f, Math.Min(inner.width, 250f));
        if (Widgets.ButtonText(chronicleBtn, chronicleLabel.Truncate(Math.Max(0f, chronicleBtn.width - 12f))))
            UiScopes.OpenOrReplaceWindow(new Dialog_ColonyChronicle(view, colony.Key));
        TooltipHandler.TipRegion(chronicleBtn, "RWS_Chronicle_OpenTip".Translate());

        if (preferences is not null && savePreferences is not null)
        {
            var favLabel = (string)"RWS_ColonyFav_ManageButton".Translate();
            var favBtn = controls.Next(favLabel, 130f, Math.Min(inner.width, 250f));
            if (Widgets.ButtonText(favBtn, favLabel.Truncate(Math.Max(0f, favBtn.width - 12f))))
                ColonyChartPicker.OpenMetricPicker(view, stats, preferences, savePreferences);
            TooltipHandler.TipRegion(favBtn, "RWS_ColonyFav_ManageTip".Translate());
        }

        var language = LanguageDatabase.activeLanguage;
        if (details is not { } data || !ReferenceEquals(data.Colony, colony) || !ReferenceEquals(data.Files, files)
            || !ReferenceEquals(data.Language, language))
            details = data = BuildDetail(colony, files, language);
        var tableTop = y + controls.Height + 4f;
        var tableRect = new Rect(inner.x, tableTop, inner.width, Math.Max(0f, inner.yMax - tableTop));
        if (tableRect.height > 0f)
            DetailTable.Draw(tableRect, ref detailScroll, data.Headers, data.Rows);
    }

    private static DetailCache BuildDetail(Colony colony, SaveFileIndex? files, object? language)
    {
        var headers = new string[]
        {
            "RWS_Col_File".Translate(), "RWS_Col_Version".Translate(), "RWS_Col_Days".Translate(),
            "RWS_Col_Superseded".Translate(), "RWS_Col_FilePresent".Translate(),
        };
        var lines = colony.Observations
            .OrderByDescending(o => o.Status == ObservationStatus.Ok ? 1 : 0)
            .ThenByDescending(o => o.Values.TicksGame)
            .Select(o => new[]
            {
                o.FileName,
                o.GameVersion ?? Dialog_RimworldStatistics.Dash,
                o.Status == ObservationStatus.Ok ? Dialog_RimworldStatistics.Days(o.Values.TicksGame) : Dialog_RimworldStatistics.Dash,
                o.Superseded ? (string)"RWS_Yes".Translate() : (string)"RWS_No".Translate(),
                files == null ? Dialog_RimworldStatistics.Dash : files.Contains(o) ? (string)"RWS_Yes".Translate() : (string)"RWS_FileMissing".Translate(),
            })
            .Concat(colony.LiveCampaigns.Select(l => new[]
            {
                (string)"RWS_LiveRecord".Translate(),
                Dialog_RimworldStatistics.Dash,
                l.HasObservation ? Dialog_RimworldStatistics.Days(l.Max.TicksGame) : Dialog_RimworldStatistics.Dash,
                Dialog_RimworldStatistics.Dash,
                Dialog_RimworldStatistics.Dash,
            }))
            .ToArray();
        return new DetailCache(colony, files, language, headers, lines);
    }
}
