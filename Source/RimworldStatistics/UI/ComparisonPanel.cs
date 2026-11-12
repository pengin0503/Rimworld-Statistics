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
    private sealed record TableRow(ComparisonRow Record, string Label, string[] Text);

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

    public static void Draw(Rect rect, ProfileSession session, StatsView view, ViewPreferences? preferences, object filter,
        Func<MetricDefinition, string, bool> acceptDefinition, Func<NumericRow, bool> acceptStatus)
    {
        var groups = preferences?.Groups;
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

        var available = Math.Max(0, rect.width - 16f);
        var width = Math.Max(available, 255f + current.Length * 155f);
        var labelWidth = Math.Max(255f, width * 0.33f);
        var columnWidth = (width - labelWidth) / current.Length;
        GUI.BeginGroup(new Rect(rect.x, y, available, RowHeight));
        DrawCell(new Rect(-scroll.x, 0, labelWidth, RowHeight), "RWS_Details_Col_Name".Translate());
        for (var i = 0; i < current.Length; i++)
        {
            var label = Label(current[i].Choice, view.Stats, groups);
            var header = new Rect(labelWidth + i * columnWidth - scroll.x, 0, columnWidth, RowHeight);
            if (sortColumn == i)
                label += descending ? " ▼" : " ▲";
            if (Widgets.ButtonText(header, label.Truncate(columnWidth - 12f)))
            {
                descending = sortColumn != i || !descending;
                sortColumn = i;
                builtFor = null;
                scroll = Vector2.zero;
            }
            TooltipHandler.TipRegion(header, label);
        }
        GUI.EndGroup();

        var viewport = new Rect(rect.x, y + RowHeight, rect.width, rect.yMax - y - RowHeight);
        if (viewport.height <= 0)
            return;
        Widgets.BeginScrollView(viewport, ref scroll, new Rect(0, 0, width, rows.Length * RowHeight));
        var first = Math.Max(0, (int)(scroll.y / RowHeight));
        var last = Math.Min(rows.Length - 1, (int)((scroll.y + viewport.height) / RowHeight) + 1);
        for (var i = first; i <= last; i++)
        {
            var row = rows[i];
            var line = new Rect(0, i * RowHeight, width, RowHeight);
            if (i % 2 == 1)
                Widgets.DrawHighlight(line, 0.25f);
            DrawCell(new Rect(0, line.y, labelWidth, RowHeight), row.Label);
            for (var c = 0; c < current.Length; c++)
            {
                var cell = new Rect(labelWidth + c * columnWidth, line.y, columnWidth, RowHeight);
                var text = row.Text[c];
                DrawCell(cell, text);
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
        }
        Widgets.EndScrollView();
    }

    private static void DrawCell(Rect cell, string text)
    {
        var anchor = Text.Anchor;
        Text.Anchor = TextAnchor.MiddleLeft;
        cell.x += 4f;
        cell.width -= 8f;
        Widgets.Label(cell, text.Truncate(cell.width));
        Text.Anchor = anchor;
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
            var text = row.Values.Select(v =>
            {
                if (v is null)
                    return Dialog_RimworldStatistics.Dash;
                var formatted = MetricText.Format(definition, v);
                if (v.Coverage is MetricCoverage.Partial or MetricCoverage.SnapshotOnly or MetricCoverage.NotApplicable)
                    formatted += " · " + MetricText.State(definition, v.Coverage);
                if (v.Evidence is MetricEvidence.Experimental or MetricEvidence.Mixed || definition.Acquisition is not null && !definition.Published)
                    formatted += " · " + "RWS_Exp_ExperimentalRow".Translate();
                return formatted;
            }).ToArray();
            return new TableRow(row, label, text);
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
