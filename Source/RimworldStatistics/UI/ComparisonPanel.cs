using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

internal static class ComparisonPanel
{
    private enum Kind { All, Group, SmartGroup, Colony }
    private sealed record Choice(Kind Kind, string Id = "");
    private sealed record Request(ProfileSession Session, StatsView Current, string Preferences, int Selection, bool IncludeDev);
    private sealed record Column(Choice Choice, ComparisonTarget Target, StatsView View);
    private sealed record TableRow(ComparisonRow Record, string Label, string? Source, string[] Text, MetricVisualKind[] VisualKinds);

    // Space for the statistic title plus a separate MOD attribution line.
    private const float RowHeight = 40f;
    private static readonly List<Choice> Choices = [new(Kind.All)];
    private static int selectionVersion;
    private static int sortColumn = -1;
    private static bool descending;
    private static bool perDayMode;
    private static Request? request;
    // The columns of the current request, built once; a failed build waits for Retry or another request (#326).
    private static readonly RetainedBuild<Request, Column[]> ColumnBuild = new();
    private static Column[]? builtFor;
    private static object? builtFilter;
    private static bool builtPerDay;
    private static object? language;
    private static string builtNameSignature = "";
    private static TableRow[] rows = [];
    private static Vector2 scroll;

    public static void ResetScroll() => scroll = Vector2.zero;

    public static void Draw(Rect rect, ProfileSession session, StatsView view, ViewPreferences? preferences, object filter,
        Func<MetricDefinition, string, bool> acceptDefinition, Func<NumericRow, bool> acceptStatus)
    {
        var groups = preferences?.Groups;
        if (Choices.RemoveAll(choice => choice.Kind == Kind.Group && groups?.Any(g => g.Id == choice.Id) != true) > 0)
        {
            if (Choices.Count == 0)
                Choices.Add(new Choice(Kind.All));
            sortColumn = -1;
            selectionVersion++;
        }
        var controls = new ControlRow(new Rect(rect.x, rect.y, rect.width, 27f));
        for (var i = 0; i < Choices.Count; i++)
        {
            var label = Label(Choices[i], view.Stats, groups, preferences);
            var button = controls.Next(label, 105f, 210f);
            var shown = label.Truncate(Math.Max(0f, button.width - 12f));
            if (shown != label)
                TooltipHandler.TipRegion(button, label);
            if (Widgets.ButtonText(button, shown))
                ShowChoices(i, view, groups, preferences);
        }
        if (Choices.Count < Comparison.MaxColumns)
        {
            string add = "RWS_Compare_Add".Translate();
            var addButton = controls.Next(add, 105f, 180f);
            var shownAdd = add.Truncate(Math.Max(0f, addButton.width - 12f));
            if (shownAdd != add)
                TooltipHandler.TipRegion(addButton, add);
            if (Widgets.ButtonText(addButton, shownAdd))
                ShowChoices(Choices.Count, view, groups, preferences);
        }

        var rateToggleLabel = perDayMode ? (string)"RWS_Rate_ModePerDay".Translate() : (string)"RWS_Rate_ModeTotal".Translate();
        var rateToggleRect = controls.Next(rateToggleLabel, 110f, 195f);
        if (Widgets.ButtonText(rateToggleRect, rateToggleLabel.Truncate(Math.Max(0f, rateToggleRect.width - 12f))))
        {
            perDayMode = !perDayMode;
            builtFor = null;
            scroll = Vector2.zero;
        }
        TooltipHandler.TipRegion(rateToggleRect, "RWS_Rate_ModeToggleTip".Translate());

        var y = rect.y + controls.Height + 4f;
        var next = new Request(session, view, RimworldStatisticsMod.Settings.viewPreferencesJson ?? "", selectionVersion,
            ProfileService.Options.IncludeDevMode);
        if (request != next)
        {
            request = next;
            ColumnBuild.Reset();
            builtFor = null;
            scroll = Vector2.zero;
        }
        var failure = ColumnBuild.Update(next, () =>
        {
            var choices = Choices.ToArray();
            var targets = choices.Select(choice => Target(choice, groups)).ToArray();
            return Task.Run(() => choices.Select((choice, i) => new Column(choice, targets[i],
                targets[i] is ScopeTarget { Scope: var scope }
                    ? session.GetView(new StatsOptions { IncludeDevMode = next.IncludeDev }, MetricDisplayPolicy.AllDetailsVisibleUnits, scope)
                    : view)).ToArray());
        });
        if (failure is not null)
            Log.Error($"[RimworldStatistics] The comparison could not be calculated.\n{failure}");
        if (ColumnBuild.Failure is { } error)
        {
            Dialog_RimworldStatistics.DrawFailure(new Rect(rect.x, y, rect.width, Math.Max(0f, rect.yMax - y)),
                "RWS_ComparisonFailed".Translate(), error, ColumnBuild.Running, ColumnBuild.Retry);
            return;
        }
        if (ColumnBuild.Value is not { } current)
        {
            Widgets.Label(new Rect(rect.x, y, rect.width, RowHeight), "RWS_Loading".Translate());
            return;
        }

        var currentLanguage = LanguageDatabase.activeLanguage;
        var currentNameSignature = ColonyPresentation.Signature(preferences);
        if (!ReferenceEquals(builtFor, current)
            || !Equals(builtFilter, filter)
            || builtPerDay != perDayMode
            || !ReferenceEquals(language, currentLanguage)
            || builtNameSignature != currentNameSignature)
        {
            rows = Build(current, acceptDefinition, acceptStatus, perDayMode, preferences);
            builtFor = current;
            builtFilter = filter;
            builtPerDay = perDayMode;
            language = currentLanguage;
            builtNameSignature = currentNameSignature;
        }
        if (rows.Length == 0)
        {
            Widgets.Label(new Rect(rect.x, y, rect.width, RowHeight), "RWS_Details_NoMatch".Translate());
            return;
        }

        var plan = ColumnLayout.ResolvePinned(rect.width, current.Length);
        if (plan.NameWidth <= 0f || plan.DataViewportWidth <= 0f)
            return;

        string nameHeader = "RWS_Details_Col_Name".Translate();
        var leftHeader = new Rect(rect.x, y, plan.NameWidth, RowHeight);
        if (DrawCell(leftHeader, nameHeader) != nameHeader)
            TooltipHandler.TipRegion(leftHeader, nameHeader);

        var bodyY = y + RowHeight;
        var bodyHeight = Math.Max(0f, rect.yMax - bodyY);
        if (bodyHeight <= 0f)
            return;

        var bottomHScrollReserve = plan.NeedsHorizontalScroll ? 16f : 0f;
        var visibleBodyHeight = Math.Max(0f, bodyHeight - bottomHScrollReserve);
        var contentHeight = rows.Length * RowHeight;
        var leftViewport = new Rect(rect.x, bodyY, plan.NameWidth, visibleBodyHeight);
        var maxScrollY = Math.Max(0f, contentHeight - visibleBodyHeight);
        if (Event.current.type == EventType.ScrollWheel && leftViewport.Contains(Event.current.mousePosition))
        {
            scroll.y = Math.Clamp(scroll.y + Event.current.delta.y * 20f, 0f, maxScrollY);
            Event.current.Use();
        }

        var rightViewport = new Rect(rect.x + plan.NameWidth, bodyY, Math.Max(0f, rect.width - plan.NameWidth), bodyHeight);
        int firstCol;
        int lastCol;
        int first;
        int last;
        using (UiScopes.ScrollView(rightViewport, ref scroll, new Rect(0f, 0f, plan.DataTotalWidth, contentHeight)))
        using (UiScopes.GuiState())
        {
            // BeginScrollView may update the scroll position for the current input event.
            // Calculate all visible ranges after that update, then paint pinned columns and
            // the header with the same scroll snapshot as the data cells.
            (firstCol, lastCol) = ColumnLayout.VisibleUniformColumns(
                current.Length, plan.ColumnWidth, scroll.x, plan.DataViewportWidth);
            first = Math.Max(0, (int)(scroll.y / RowHeight));
            last = Math.Min(rows.Length - 1, (int)((scroll.y + visibleBodyHeight) / RowHeight) + 1);
            for (var i = first; i <= last; i++)
            {
                var row = rows[i];
                var line = new Rect(0f, i * RowHeight, plan.DataTotalWidth, RowHeight);
                if (i % 2 == 1)
                    Widgets.DrawHighlight(line, 0.25f);
                for (var c = firstCol; c < lastCol; c++)
                {
                    var cell = new Rect(c * plan.ColumnWidth, line.y, plan.ColumnWidth, RowHeight);
                    var text = row.Text[c];
                    var visualKind = row.VisualKinds[c];
                    GUI.color = visualKind == MetricVisualKind.Complete ? Color.white : MetricVisualStyle.StateColor(visualKind);
                    DrawCell(cell, text, TextAnchor.MiddleRight);
                    var value = row.Record.Values[c];
                    var column = current[c];
                    var tipId = unchecked(0x60500000 + i * Comparison.MaxColumns + c);
                    TooltipHandler.TipRegion(cell, () =>
                    {
                        if (perDayMode && column.Target is ScopeTarget)
                            return "RWS_Rate_ScopeNotApplicableTip".Translate();
                        var colonyTarget = column.Target as ColonyTarget;
                        var original = colonyTarget is not null
                            ? MetricLookup.ForColony(column.View, colonyTarget.ColonyKey, row.Record.MetricKey)
                            : MetricLookup.Overall(column.View, row.Record.MetricKey);
                        if (original is null)
                            return "RWS_Coverage_Unknown".Translate();
                        var targetColony = colonyTarget is not null
                            ? column.View.Numeric.Colonies.FirstOrDefault(nc => nc.Key == colonyTarget.ColonyKey)
                            : null;
                        var baseTip = colonyTarget is not null
                            ? DetailsTab.ColonyTip(original, column.View.Stats, targetColony)
                            : MetricText.Tip(original, column.View.Stats);
                        if (value is null)
                            return perDayMode
                                ? "RWS_Rate_IncompleteIntervalTip".Translate() + "\n" + baseTip
                                : "RWS_Coverage_Unknown".Translate();
                        return text + "\n" + baseTip;
                    }, tipId);
                }
                GUI.color = Color.white;
            }
        }

        // Pinned names and header are drawn last to use the final scroll position.
        if (leftViewport.height > 0f)
        {
            using (UiScopes.Group(leftViewport))
            {
                for (var i = first; i <= last; i++)
                {
                    var row = rows[i];
                    var rowY = i * RowHeight - scroll.y;
                    var nameRect = new Rect(0f, rowY, plan.NameWidth, RowHeight);
                    if (i % 2 == 1)
                        Widgets.DrawHighlight(nameRect, 0.25f);
                    var titleRect = row.Source is null ? nameRect
                        : new Rect(nameRect.x, nameRect.y, nameRect.width, 20f);
                    var shownName = DrawCell(titleRect, row.Label);
                    if (row.Source is null)
                    {
                        if (shownName != row.Label)
                            TooltipHandler.TipRegion(nameRect, row.Label);
                    }
                    else
                    {
                        var sourceRect = new Rect(nameRect.x, nameRect.y + 20f, nameRect.width, 19f);
                        var shownSource = DrawCell(sourceRect, row.Source);
                        if (shownName != row.Label)
                            TooltipHandler.TipRegion(titleRect, row.Label);
                        if (shownSource != row.Source)
                            TooltipHandler.TipRegion(sourceRect, row.Source);
                    }
                }
            }
        }

        using (UiScopes.Group(new Rect(rect.x + plan.NameWidth, y, plan.DataViewportWidth, RowHeight)))
        {
            for (var i = firstCol; i < lastCol; i++)
            {
                var label = Label(current[i].Choice, view.Stats, groups, preferences);
                var header = new Rect(i * plan.ColumnWidth - scroll.x, 0, plan.ColumnWidth, RowHeight);
                if (sortColumn == i)
                    label += descending ? " ▼" : " ▲";
                if (Widgets.ButtonText(header, label.Truncate(Math.Max(1f, plan.ColumnWidth - 12f))))
                {
                    descending = sortColumn != i || !descending;
                    sortColumn = i;
                    builtFor = null;
                    scroll = Vector2.zero;
                }
                TooltipHandler.TipRegion(header, label);
            }
        }

        Widgets.DrawBoxSolid(new Rect(rect.x + plan.NameWidth - 1f, y, 1f, Math.Max(0f, rect.yMax - y - bottomHScrollReserve)), new Color(1f, 1f, 1f, 0.14f));
    }

    private static string DrawCell(Rect cell, string text, TextAnchor cellAnchor = TextAnchor.MiddleLeft)
    {
        using (UiScopes.GuiState())
        {
            Text.Anchor = cellAnchor;
            cell.x += 4f;
            cell.width = Math.Max(0f, cell.width - 8f);
            var shown = text.Truncate(cell.width);
            Widgets.Label(cell, shown);
            return shown;
        }
    }

    private static TableRow[] Build(
        Column[] current,
        Func<MetricDefinition, string, bool> acceptDefinition,
        Func<NumericRow, bool> acceptStatus,
        bool perDay,
        ViewPreferences? preferences)
    {
        var candidateRows = current.SelectMany(column => column.Target switch
        {
            ScopeTarget => column.View.Numeric.Overall,
            ColonyTarget target => column.View.Numeric.Colonies.FirstOrDefault(c => c.Key == target.ColonyKey)?.Rows ?? [],
            _ => [],
        });
        var definitions = candidateRows.Where(acceptStatus).Select(row => row.Definition)
            .Where(def => !perDay || MetricRates.IsAdditiveTotal(def))
            .DistinctBy(def => def.Key, StringComparer.Ordinal).Select(def => (Definition: def, Label: MetricText.Label(def)))
            .Where(entry => acceptDefinition(entry.Definition, entry.Label))
            .OrderBy(entry => entry.Label, StringComparer.CurrentCulture)
            .ThenBy(entry => entry.Definition.Key, StringComparer.Ordinal)
            .ToDictionary(entry => entry.Definition.Key, StringComparer.Ordinal);
        var values = Comparison.Build(current.Select(col => (col.Target, col.View)).ToArray(), definitions.Keys, perDay);
        var ordered = sortColumn >= 0 && sortColumn < current.Length ? Comparison.Sort(values, sortColumn, descending) : values;
        return ordered.Select(row =>
        {
            var (definition, label) = definitions[row.MetricKey];
            var text = new string[row.Values.Count];
            var visualKinds = new MetricVisualKind[row.Values.Count];
            for (var i = 0; i < row.Values.Count; i++)
            {
                var v = row.Values[i];
                if (v is null)
                {
                    text[i] = Dialog_RimworldStatistics.Dash;
                    visualKinds[i] = MetricVisualKind.Missing;
                    continue;
                }
                var col = current[i];
                var originalRow = !perDay
                    ? (col.Target is ColonyTarget ct
                        ? MetricLookup.ForColony(col.View, ct.ColonyKey, row.MetricKey)
                        : MetricLookup.Overall(col.View, row.MetricKey))
                    : null;
                var formatted = perDay && v.Number is { } rateNum
                    ? MetricText.FormatRateValue(definition, rateNum)
                    : originalRow is not null
                        ? DetailsTab.FormatRowValue(
                            originalRow,
                            key => col.View.Stats.Colonies.FirstOrDefault(s => s.Key == key) is { } cs
                                ? Dialog_RimworldStatistics.ColonyName(cs, preferences)
                                : key)
                        : MetricText.Format(definition, v);
                if (!perDay && v.Coverage is MetricCoverage.Partial or MetricCoverage.SnapshotOnly or MetricCoverage.NotApplicable)
                    formatted += " · " + MetricText.State(definition, v.Coverage);
                if (v.Evidence is MetricEvidence.Experimental or MetricEvidence.Mixed || definition.Acquisition is not null && !definition.Published)
                    formatted += " · " + "RWS_Exp_ExperimentalRow".Translate();
                text[i] = formatted;
                visualKinds[i] = originalRow is not null
                    ? MetricVisualState.Classify(originalRow)
                    : MetricVisualState.Classify(definition, v);
            }
            return new TableRow(row, label, MetricText.ModSourceLabel(definition), text, visualKinds);
        }).ToArray();
    }

    private static ComparisonTarget Target(Choice choice, IReadOnlyList<ColonyGroup>? groups) => choice.Kind switch
    {
        Kind.Group => new ScopeTarget(ViewScope.ForGroup(groups?.FirstOrDefault(group => group.Id == choice.Id)
            ?? new ColonyGroup { Id = choice.Id })),
        Kind.SmartGroup => SmartGroupSpec.TryParse(choice.Id, out var smart) && smart is not null
            ? new ScopeTarget(ViewScope.ForSmartGroup(smart))
            : new ScopeTarget(ViewScope.All),
        Kind.Colony => new ColonyTarget(choice.Id),
        _ => new ScopeTarget(ViewScope.All),
    };

    private static string Label(Choice choice, StatsResult stats, IReadOnlyList<ColonyGroup>? groups, ViewPreferences? preferences) => choice.Kind switch
    {
        Kind.All => "RWS_Details_AllColonies".Translate(),
        Kind.Group => groups?.FirstOrDefault(group => group.Id == choice.Id)?.Name ?? choice.Id,
        Kind.SmartGroup => SmartGroupSpec.TryParse(choice.Id, out var smart) && smart is not null
            ? SmartScopeUi.Label(smart)
            : choice.Id,
        _ => stats.Colonies.FirstOrDefault(c => c.Key == choice.Id) is { } colony
            ? Dialog_RimworldStatistics.ColonyName(colony, preferences) : choice.Id,
    };

    private static void ApplyChoice(int index, Choice choice)
    {
        if (index < Choices.Count)
            Choices[index] = choice;
        else if (Choices.Count < Comparison.MaxColumns)
            Choices.Add(choice);
        selectionVersion++;
    }

    private static void ShowChoices(int index, StatsView view, IReadOnlyList<ColonyGroup>? groups, ViewPreferences? preferences)
    {
        var stats = view.Stats;
        var menu = new List<FloatMenuOption>();
        var scopeChoices = new[] { new Choice(Kind.All) }
            .Concat((groups ?? []).Select(group => new Choice(Kind.Group, group.Id)));
        foreach (var choice in scopeChoices.Where(choice => !Choices.Contains(choice)))
        {
            var label = Label(choice, stats, groups, preferences);
            if (choice.Kind == Kind.Group)
                label = (string)"RWS_Compare_Group".Translate() + " · " + label;
            menu.Add(new FloatMenuOption(label, () => ApplyChoice(index, choice)));
        }

        var takenSmartIds = Choices
            .Where((c, i) => i != index && c.Kind == Kind.SmartGroup)
            .Select(c => c.Id)
            .ToHashSet(StringComparer.Ordinal);
        menu.Add(new FloatMenuOption("RWS_SmartGroup_Menu".Translate(), () =>
        {
            SmartScopeUi.OpenMenu(view, spec => ApplyChoice(index, new Choice(Kind.SmartGroup, spec.Id)), takenSmartIds);
        }));

        var currentColonyChoice = index < Choices.Count && Choices[index].Kind == Kind.Colony
            ? Choices[index].Id
            : null;
        var takenOtherColonies = Choices
            .Where((c, i) => i != index && c.Kind == Kind.Colony)
            .Select(c => c.Id)
            .ToHashSet(StringComparer.Ordinal);
        var selectableColonies = stats.Colonies
            .Where(c => !takenOtherColonies.Contains(c.Key))
            .ToArray();
        if (selectableColonies.Length > 0)
        {
            var pickHeading = (string)"RWS_Compare_PickColony".Translate();
            var pickMenuLabel = (string)"RWS_Compare_Colony".Translate() + " · " + pickHeading;
            menu.Add(new FloatMenuOption(pickMenuLabel, () =>
            {
                UiScopes.OpenOrReplaceWindow(new ColonyPickerDialog(
                    selectableColonies,
                    preferences ?? Dialog_RimworldStatistics.ActivePreferences,
                    picked => ApplyChoice(index, new Choice(Kind.Colony, picked.Key)),
                    heading: pickHeading,
                    selectedColonyKey: currentColonyChoice));
            }));
        }

        if (index < Choices.Count && Choices.Count > 1)
            menu.Add(new FloatMenuOption("RWS_Compare_Remove".Translate(), () =>
            {
                Choices.RemoveAt(index);
                sortColumn = -1;
                selectionVersion++;
            }));
        if (menu.Count > 0)
            Find.WindowStack.Add(new FloatMenu(menu));
    }
}
