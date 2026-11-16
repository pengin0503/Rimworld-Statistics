using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

internal static class ComparisonPanel
{
    private enum Kind { All, Group, Colony }
    private sealed record Choice(Kind Kind, string Id = "");
    private sealed record Request(ProfileSession Session, StatsView Current, string Preferences, int Selection, bool IncludeDev);
    private sealed record Column(Choice Choice, ComparisonTarget Target, StatsView View);
    private sealed record TableRow(ComparisonRow Record, string Label, string[] Text, MetricVisualKind[] VisualKinds);

    private const float RowHeight = 27f;
    private static readonly List<Choice> Choices = [new(Kind.All)];
    private static int selectionVersion;
    private static int sortColumn = -1;
    private static bool descending;
    private static Request? request;
    private static Task<Column[]>? pending;
    private static Column[]? columns;
    private static Column[]? builtFor;
    private static object? builtFilter;
    private static object? language;
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
            var label = Label(Choices[i], view.Stats, groups);
            var button = controls.Next(label, 105f, 210f);
            if (Widgets.ButtonText(button, label.Truncate(button.width - 12f)))
                ShowChoices(i, view.Stats, groups);
        }
        if (Choices.Count < Comparison.MaxColumns)
        {
            string add = "RWS_Compare_Add".Translate();
            if (Widgets.ButtonText(controls.Next(add, 105f, 180f), add))
                ShowChoices(Choices.Count, view.Stats, groups);
        }

        var y = rect.y + controls.Height + 4f;
        var next = new Request(session, view, RimworldStatisticsMod.Settings.viewPreferencesJson ?? "", selectionVersion,
            ProfileService.Options.IncludeDevMode);
        if (request != next)
        {
            request = next;
            columns = null;
            builtFor = null;
            scroll = Vector2.zero;
            var choices = Choices.ToArray();
            var targets = choices.Select(choice => Target(choice, groups)).ToArray();
            pending = Task.Run(() => choices.Select((choice, i) => new Column(choice, targets[i],
                targets[i] is ScopeTarget { Scope: var scope }
                    ? session.GetView(new StatsOptions { IncludeDevMode = next.IncludeDev }, MetricDisplayPolicy.AllDetailsVisibleUnits, scope)
                    : view)).ToArray());
        }
        if (pending is { IsCompleted: true } loaded)
        {
            pending = null;
            columns = loaded.GetAwaiter().GetResult();
        }
        if (columns is not { } current)
        {
            Widgets.Label(new Rect(rect.x, y, rect.width, RowHeight), "RWS_Loading".Translate());
            return;
        }

        var currentLanguage = LanguageDatabase.activeLanguage;
        if (!ReferenceEquals(builtFor, current) || !Equals(builtFilter, filter) || !ReferenceEquals(language, currentLanguage))
        {
            rows = Build(current, acceptDefinition, acceptStatus);
            builtFor = current;
            builtFilter = filter;
            language = currentLanguage;
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

        var (firstCol, lastCol) = ColumnLayout.VisibleUniformColumns(
            current.Length,
            plan.ColumnWidth,
            scroll.x,
            plan.DataViewportWidth);

        GUI.BeginGroup(new Rect(rect.x + plan.NameWidth, y, plan.DataViewportWidth, RowHeight));
        for (var i = firstCol; i < lastCol; i++)
        {
            var label = Label(current[i].Choice, view.Stats, groups);
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
        GUI.EndGroup();

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

        var first = Math.Max(0, (int)(scroll.y / RowHeight));
        var last = Math.Min(rows.Length - 1, (int)((scroll.y + visibleBodyHeight) / RowHeight) + 1);

        if (leftViewport.height > 0f)
        {
            GUI.BeginGroup(leftViewport);
            for (var i = first; i <= last; i++)
            {
                var row = rows[i];
                var rowY = i * RowHeight - scroll.y;
                var nameRect = new Rect(0f, rowY, plan.NameWidth, RowHeight);
                if (i % 2 == 1)
                    Widgets.DrawHighlight(nameRect, 0.25f);
                var shownName = DrawCell(nameRect, row.Label);
                if (shownName != row.Label)
                    TooltipHandler.TipRegion(nameRect, row.Label);
            }
            GUI.EndGroup();
        }

        var rightViewport = new Rect(rect.x + plan.NameWidth, bodyY, Math.Max(0f, rect.width - plan.NameWidth), bodyHeight);
        Widgets.BeginScrollView(rightViewport, ref scroll, new Rect(0f, 0f, plan.DataTotalWidth, contentHeight));
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
                    var original = column.Target is ColonyTarget target
                        ? MetricLookup.ForColony(column.View, target.ColonyKey, row.Record.MetricKey)
                        : MetricLookup.Overall(column.View, row.Record.MetricKey);
                    return original is null || value is null ? "RWS_Coverage_Unknown".Translate()
                        : text + "\n" + MetricText.Tip(original, column.View.Stats);
                }, tipId);
            }
            GUI.color = Color.white;
        }
        Widgets.EndScrollView();
        Widgets.DrawBoxSolid(new Rect(rect.x + plan.NameWidth - 1f, y, 1f, Math.Max(0f, rect.yMax - y - bottomHScrollReserve)), new Color(1f, 1f, 1f, 0.14f));
    }

    private static string DrawCell(Rect cell, string text, TextAnchor cellAnchor = TextAnchor.MiddleLeft)
    {
        var anchor = Text.Anchor;
        Text.Anchor = cellAnchor;
        cell.x += 4f;
        cell.width = Math.Max(0f, cell.width - 8f);
        var shown = text.Truncate(cell.width);
        Widgets.Label(cell, shown);
        Text.Anchor = anchor;
        return shown;
    }

    private static TableRow[] Build(Column[] current, Func<MetricDefinition, string, bool> acceptDefinition, Func<NumericRow, bool> acceptStatus)
    {
        var candidateRows = current.SelectMany(column => column.Target switch
        {
            ScopeTarget => column.View.Numeric.Overall,
            ColonyTarget target => column.View.Numeric.Colonies.FirstOrDefault(c => c.Key == target.ColonyKey)?.Rows ?? [],
            _ => [],
        });
        var definitions = candidateRows.Where(acceptStatus).Select(row => row.Definition)
            .DistinctBy(def => def.Key, StringComparer.Ordinal).Select(def => (Definition: def, Label: MetricText.Label(def)))
            .Where(entry => acceptDefinition(entry.Definition, entry.Label))
            .OrderBy(entry => entry.Label, StringComparer.CurrentCulture)
            .ThenBy(entry => entry.Definition.Key, StringComparer.Ordinal)
            .ToDictionary(entry => entry.Definition.Key, StringComparer.Ordinal);
        var values = Comparison.Build(current.Select(col => (col.Target, col.View)).ToArray(), definitions.Keys);
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
                var formatted = MetricText.Format(definition, v);
                if (v.Coverage is MetricCoverage.Partial or MetricCoverage.SnapshotOnly or MetricCoverage.NotApplicable)
                    formatted += " · " + MetricText.State(definition, v.Coverage);
                if (v.Evidence is MetricEvidence.Experimental or MetricEvidence.Mixed || definition.Acquisition is not null && !definition.Published)
                    formatted += " · " + "RWS_Exp_ExperimentalRow".Translate();
                text[i] = formatted;
                visualKinds[i] = MetricVisualState.Classify(definition, v);
            }
            return new TableRow(row, label, text, visualKinds);
        }).ToArray();
    }

    private static ComparisonTarget Target(Choice choice, IReadOnlyList<ColonyGroup>? groups) => choice.Kind switch
    {
        Kind.Group => new ScopeTarget(ViewScope.ForGroup(groups?.FirstOrDefault(group => group.Id == choice.Id)
            ?? new ColonyGroup { Id = choice.Id })),
        Kind.Colony => new ColonyTarget(choice.Id),
        _ => new ScopeTarget(ViewScope.All),
    };

    private static string Label(Choice choice, StatsResult stats, IReadOnlyList<ColonyGroup>? groups) => choice.Kind switch
    {
        Kind.All => "RWS_Details_AllColonies".Translate(),
        Kind.Group => groups?.FirstOrDefault(group => group.Id == choice.Id)?.Name ?? choice.Id,
        _ => stats.Colonies.FirstOrDefault(c => c.Key == choice.Id) is { } colony
            ? Dialog_RimworldStatistics.ColonyName(colony) : choice.Id,
    };

    private static void ShowChoices(int index, StatsResult stats, IReadOnlyList<ColonyGroup>? groups)
    {
        var menu = new List<FloatMenuOption>();
        var available = new[] { new Choice(Kind.All) }
            .Concat((groups ?? []).Select(group => new Choice(Kind.Group, group.Id)))
            .Concat(stats.Colonies.Select(colony => new Choice(Kind.Colony, colony.Key)));
        foreach (var choice in available.Where(choice => !Choices.Contains(choice)))
        {
            var label = Label(choice, stats, groups);
            if (choice.Kind != Kind.All)
                label = (choice.Kind == Kind.Group ? (string)"RWS_Compare_Group".Translate() : "RWS_Compare_Colony".Translate()) + " · " + label;
            menu.Add(new FloatMenuOption(label, () =>
            {
                if (index < Choices.Count)
                    Choices[index] = choice;
                else if (Choices.Count < Comparison.MaxColumns)
                    Choices.Add(choice);
                selectionVersion++;
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
